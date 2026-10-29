using Microsoft.AspNetCore.Http.HttpResults;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Web.Authorization;
using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.BuildingBlocks.Web.Http;
using Nexus.BuildingBlocks.Web.OpenApi;
using Nexus.Platform.Api.Users;
using Nexus.Platform.Application.Currencies;
using Nexus.Platform.Application.Currencies.GetCurrencies;
using Nexus.Platform.Application.Currencies.GetCurrency;
using Nexus.Platform.Application.Currencies.UpdateCurrency;

namespace Nexus.Platform.Api.Currencies;

/// <summary>
/// <c>/v1/currencies</c> (domain/org.md §5; <c>contracts/openapi/platform.yaml</c>, tag
/// Currencies). Endpoints only translate HTTP ↔ commands/queries (conventions-backend.md §2):
/// body/route → command, <c>If-Match</c> → expected version, <c>Result</c> → status +
/// <c>ETag</c>. No create: currencies are seeded (<c>db/seed/common</c>), never created through
/// the API.
/// </summary>
public static class CurrencyEndpoints
{
    public static RouteGroupBuilder MapCurrencyEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1/currencies").WithTags("Currencies");

        group.MapGet(string.Empty, ListCurrencies)
            .WithName("listCurrencies")
            .ValidatedBy<GetCurrenciesQuery>()
            .RequirePermission("CURRENCY", PermissionAction.Read)
            .WithScope("none");

        group.MapGet("/{code}", GetCurrency)
            .WithName("getCurrency")
            .ValidatedBy<GetCurrencyQuery>()
            .RequirePermission("CURRENCY", PermissionAction.Read)
            .WithScope("none");

        group.MapPut("/{code}", UpdateCurrency)
            .WithName("updateCurrency")
            .ValidatedBy<UpdateCurrencyCommand>()
            .RequirePermission("CURRENCY", PermissionAction.Update)
            .WithScope("none")
            .WithRules("ORG-R7");

        return group;
    }

    internal static async Task<Results<Ok<PageResponse<CurrencyListItemResponse>>, ProblemHttpResult>> ListCurrencies(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        int page = PageLimits.DefaultPage,
        int pageSize = PageLimits.DefaultPageSize,
        string? sort = null)
    {
        var result = await dispatcher.Send(new GetCurrenciesQuery(page, pageSize, sort), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var value = result.Value;
        return TypedResults.Ok(new PageResponse<CurrencyListItemResponse>(
            value.Items
                .Select(c => new CurrencyListItemResponse(c.Code, c.CaptionI18n, c.MinorUnits, c.IsActive))
                .ToList(),
            value.Page,
            value.PageSize,
            value.Total));
    }

    internal static async Task<Results<Ok<CurrencyResponse>, ProblemHttpResult>> GetCurrency(
        string code,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new GetCurrencyQuery(code), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(result.Value.Version.ToString());
        return TypedResults.Ok(CurrencyResponse.From(result.Value));
    }

    internal static async Task<Results<Ok<CurrencyResponse>, ProblemHttpResult>> UpdateCurrency(
        string code,
        UpdateCurrencyRequest request,
        IDispatcher dispatcher,
        HttpRequest httpRequest,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        if (!TryReadExpectedVersion(httpRequest, out var expected, out var problem))
        {
            return problem;
        }

        var result = await dispatcher.Send(new UpdateCurrencyCommand(code, expected, request.IsActive), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(result.Value.Version.ToString());
        return TypedResults.Ok(CurrencyResponse.From(result.Value));
    }

    /// <summary>
    /// <c>If-Match</c> → expected version: absent → 428; present but not a version this API
    /// ever issued → 409 (it cannot match the current one, conventions-api.md §5).
    /// </summary>
    private static bool TryReadExpectedVersion(HttpRequest request, out CurrencyVersion expected, out ProblemHttpResult problem)
    {
        expected = default;
        problem = null!;

        if (!request.TryGetIfMatch(out var token))
        {
            problem = NexusProblems.PreconditionRequired();
            return false;
        }

        if (!CurrencyVersion.TryParse(token, out expected))
        {
            problem = CurrencyErrors.StaleVersion.ToProblem();
            return false;
        }

        return true;
    }
}
