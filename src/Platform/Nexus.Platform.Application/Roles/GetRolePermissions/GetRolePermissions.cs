using FluentValidation;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Roles.GetRolePermissions;

/// <summary>
/// <c>GET /v1/roles/{id}/permissions?asOf=</c> (<c>getRolePermissions</c>, A10): the role's
/// grants valid at <paramref name="AsOf"/> (defaults to now).
/// </summary>
public sealed record GetRolePermissionsQuery(Guid RoleId, DateTimeOffset? AsOf, int Page, int PageSize, string? Sort)
    : IQuery<PagedResult<PermissionGrantItem>>;

internal sealed class GetRolePermissionsValidator : AbstractValidator<GetRolePermissionsQuery>
{
    public GetRolePermissionsValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, PageLimits.MaxPageSize);
        RuleFor(q => q.Sort).Custom((sort, context) =>
        {
            if (!SortSpec.TryParse(sort, PermissionGrantSortFields.Allowed, out _, out var error))
            {
                context.AddFailure("sort", error!);
            }
        });
    }
}

internal sealed class GetRolePermissionsHandler : IQueryHandler<GetRolePermissionsQuery, PagedResult<PermissionGrantItem>>
{
    private readonly IRequestTransaction _transaction;
    private readonly IRoleReadDb _roles;
    private readonly IRolePermissionReadDb _rolePermissions;
    private readonly TimeProvider _timeProvider;

    public GetRolePermissionsHandler(
        IRequestTransaction transaction, IRoleReadDb roles, IRolePermissionReadDb rolePermissions, TimeProvider timeProvider)
    {
        _transaction = transaction;
        _roles = roles;
        _rolePermissions = rolePermissions;
        _timeProvider = timeProvider;
    }

    public async Task<Result<PagedResult<PermissionGrantItem>>> Handle(
        GetRolePermissionsQuery query, CancellationToken cancellationToken)
    {
        SortSpec.TryParse(query.Sort, PermissionGrantSortFields.Allowed, out var sort, out _);

        await _transaction.BeginAsync(cancellationToken);

        if (await _roles.GetByIdAsync(query.RoleId, cancellationToken) is null)
        {
            return Result.Failure<PagedResult<PermissionGrantItem>>(RoleErrors.NotFound(query.RoleId));
        }

        var asOf = query.AsOf ?? _timeProvider.GetUtcNow();
        var page = await _rolePermissions.ListGrantsAsync(query.RoleId, asOf, query.Page, query.PageSize, sort, cancellationToken);
        await _transaction.CommitAsync(cancellationToken);

        return Result.Success(page);
    }
}
