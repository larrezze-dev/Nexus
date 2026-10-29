using Microsoft.AspNetCore.Http.HttpResults;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Web.Authorization;
using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.BuildingBlocks.Web.Http;
using Nexus.Platform.Api.Users;
using Nexus.Platform.Application.Parties;
using Nexus.Platform.Application.Parties.CreateParty;
using Nexus.Platform.Application.Parties.DeactivateParty;
using Nexus.Platform.Application.Parties.GetParties;
using Nexus.Platform.Application.Parties.GetParty;
using Nexus.Platform.Application.Parties.UpdateParty;

namespace Nexus.Platform.Api.Parties;

/// <summary>
/// <c>/v1/parties</c> (domain/org.md §5; <c>contracts/openapi/platform.yaml</c>, tag Parties).
/// Endpoints only translate HTTP ↔ commands/queries (conventions-backend.md §2): body/route →
/// command, <c>If-Match</c> → expected version, <c>Result</c> → status + <c>ETag</c>.
/// </summary>
public static class PartyEndpoints
{
    public static RouteGroupBuilder MapPartyEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1/parties").WithTags("Parties");

        group.MapGet(string.Empty, ListParties)
            .WithName("listParties")
            .RequirePermission("PARTY", PermissionAction.Read)
            .WithScope("none");

        group.MapPost(string.Empty, CreateParty)
            .WithName("createParty")
            .RequirePermission("PARTY", PermissionAction.Create)
            .WithScope("none");

        group.MapGet("/{id:guid}", GetParty)
            .WithName("getParty")
            .RequirePermission("PARTY", PermissionAction.Read)
            .WithScope("none");

        group.MapPut("/{id:guid}", UpdateParty)
            .WithName("updateParty")
            .RequirePermission("PARTY", PermissionAction.Update)
            .WithScope("none");

        group.MapPost("/{id:guid}/deactivate", DeactivateParty)
            .WithName("deactivateParty")
            .RequirePermission("PARTY", PermissionAction.Update)
            .WithScope("none");

        return group;
    }

    internal static async Task<Results<Ok<PageResponse<PartyListItemResponse>>, ProblemHttpResult>> ListParties(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        int page = PageLimits.DefaultPage,
        int pageSize = PageLimits.DefaultPageSize,
        string? sort = null,
        string? partyType = null,
        bool? isInternal = null,
        string? q = null)
    {
        var result = await dispatcher.Send(new GetPartiesQuery(page, pageSize, sort, partyType, isInternal, q), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var value = result.Value;
        return TypedResults.Ok(new PageResponse<PartyListItemResponse>(
            value.Items
                .Select(p => new PartyListItemResponse(p.Id, p.PartyType, p.LegalName, p.DisplayName, p.IsInternal, p.IsActive))
                .ToList(),
            value.Page,
            value.PageSize,
            value.Total));
    }

    internal static async Task<Results<Created<PartyResponse>, Ok<PartyResponse>, ProblemHttpResult>> CreateParty(
        CreatePartyRequest request,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new CreatePartyCommand(
                request.Id,
                request.PartyType,
                request.LegalName,
                request.DisplayName,
                request.TaxId,
                request.ErpCompanyCode,
                request.IsInternal),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var (party, created) = result.Value;
        response.SetETag(party.Version.ToString());
        var body = PartyResponse.From(party);

        return created
            ? TypedResults.Created($"/v1/parties/{party.Id}", body)
            : TypedResults.Ok(body);
    }

    internal static async Task<Results<Ok<PartyResponse>, ProblemHttpResult>> GetParty(
        Guid id,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new GetPartyQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(result.Value.Version.ToString());
        return TypedResults.Ok(PartyResponse.From(result.Value));
    }

    internal static async Task<Results<Ok<PartyResponse>, ProblemHttpResult>> UpdateParty(
        Guid id,
        UpdatePartyRequest request,
        IDispatcher dispatcher,
        HttpRequest httpRequest,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (!TryReadExpectedVersion(httpRequest, out var expected, out var problem))
        {
            return problem;
        }

        var result = await dispatcher.Send(
            new UpdatePartyCommand(id, expected, request.LegalName, request.DisplayName, request.TaxId, request.ErpCompanyCode),
            cancellationToken);

        return ToPartyResponse(result, response);
    }

    internal static async Task<Results<Ok<PartyResponse>, ProblemHttpResult>> DeactivateParty(
        Guid id,
        IDispatcher dispatcher,
        HttpRequest httpRequest,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (!TryReadExpectedVersion(httpRequest, out var expected, out var problem))
        {
            return problem;
        }

        var result = await dispatcher.Send(new DeactivatePartyCommand(id, expected), cancellationToken);
        return ToPartyResponse(result, response);
    }

    private static Results<Ok<PartyResponse>, ProblemHttpResult> ToPartyResponse(
        BuildingBlocks.Domain.Result<PartyDetails> result,
        HttpResponse response)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(result.Value.Version.ToString());
        return TypedResults.Ok(PartyResponse.From(result.Value));
    }

    /// <summary>
    /// <c>If-Match</c> → expected version: absent → 428; present but not a version this API
    /// ever issued → 409 (it cannot match the current one, conventions-api.md §5).
    /// </summary>
    private static bool TryReadExpectedVersion(HttpRequest request, out PartyVersion expected, out ProblemHttpResult problem)
    {
        expected = default;
        problem = null!;

        if (!request.TryGetIfMatch(out var token))
        {
            problem = NexusProblems.PreconditionRequired();
            return false;
        }

        if (!PartyVersion.TryParse(token, out expected))
        {
            problem = PartyErrors.StaleVersion.ToProblem();
            return false;
        }

        return true;
    }
}
