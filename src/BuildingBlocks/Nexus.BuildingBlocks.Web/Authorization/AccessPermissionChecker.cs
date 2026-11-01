using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Http;
using Nexus.BuildingBlocks.Persistence;

namespace Nexus.BuildingBlocks.Web.Authorization;

/// <summary>
/// The real <see cref="IPermissionChecker"/> (step 0.6): calls
/// <c>access.has_permission</c> inside the request's own transaction (conventions-backend.md
/// §5), so it sees the same <c>app.current_user_id</c> as the handler that runs next.
/// <para>
/// Scoped: it shares the request's <see cref="IUnitOfWork"/>. It begins that unit of work
/// itself; the handler's later <c>BeginAsync</c> is a no-op on the already active instance.
/// </para>
/// Supported <c>x-scope</c> values (the only ones in the contracts today):
/// <list type="bullet">
///   <item><c>none</c> (or no scope metadata): the permission in any scope (ACC-R10).</item>
///   <item><c>global</c>: a <c>GLOBAL</c>-scoped assignment specifically.</item>
///   <item><c>body.scopeEntityId</c>: <c>POST /v1/role-assignments</c>, ACC-R4
///   (<see cref="RoleAssignmentScopeResolver"/>).</item>
/// </list>
/// Any other value throws: an endpoint declaring a scope this checker does not understand is a
/// programming error, and silently checking "any scope" instead would widen access.
/// </summary>
internal sealed class AccessPermissionChecker : IPermissionChecker
{
    internal const string ScopeNone = "none";
    internal const string ScopeGlobal = "global";
    internal const string ScopeRoleAssignmentBody = "body.scopeEntityId";

    private const string HasPermissionSql =
        "SELECT access.has_permission(@permission, @legalEntityId::uuid, @businessUnitId::uuid, @warehouseId::uuid, @projectId::uuid)";

    private readonly IUnitOfWork _unitOfWork;

    public AccessPermissionChecker(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<bool> HasPermissionAsync(
        HttpContext httpContext,
        PermissionMetadata permission,
        PermissionScopeMetadata? scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(permission);

        // Opens the request's transaction (or reuses it) and stamps app.current_user_id.
        await _unitOfWork.BeginAsync(cancellationToken);

        PermissionTarget? target = scope?.Scope switch
        {
            null or ScopeNone => PermissionTarget.AnyScope,
            ScopeGlobal => PermissionTarget.GlobalOnly(),
            ScopeRoleAssignmentBody => await ResolveRoleAssignmentBodyAsync(httpContext.Request, cancellationToken),
            var other => throw new InvalidOperationException(
                $"Unsupported permission scope '{other}' on endpoint requiring '{permission.Permission}'."),
        };

        if (target is not { } t)
        {
            return false;
        }

        return await _unitOfWork.Connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(
                HasPermissionSql,
                new
                {
                    permission = permission.Permission,
                    legalEntityId = t.LegalEntityId,
                    businessUnitId = t.BusinessUnitId,
                    warehouseId = t.WarehouseId,
                    projectId = t.ProjectId,
                },
                _unitOfWork.Transaction,
                cancellationToken: cancellationToken));
    }

    private async Task<PermissionTarget?> ResolveRoleAssignmentBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        // Minimal API endpoint filters run after parameter binding, so the body has already
        // been consumed by the time we get here. PermissionBodyBufferingMiddleware enables
        // buffering for body-scoped endpoints before binding, which makes it seekable.
        if (!request.Body.CanSeek)
        {
            throw new InvalidOperationException(
                $"x-scope '{ScopeRoleAssignmentBody}' needs a re-readable body: call app.UseNexusPermissionBodyBuffering() in the host.");
        }

        JsonDocument document;
        try
        {
            request.Body.Position = 0;
            document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            // Unparseable body: no basis to grant. Model binding answers 400 on its own.
            return null;
        }
        finally
        {
            request.Body.Position = 0;
        }

        using (document)
        {
            if (!RoleAssignmentScopeResolver.TryParse(document.RootElement, out var scopeType, out var scopeEntityId))
            {
                return null;
            }

            return await RoleAssignmentScopeResolver.ResolveAsync(
                scopeType,
                scopeEntityId,
                _unitOfWork.Connection,
                _unitOfWork.Transaction,
                cancellationToken);
        }
    }
}
