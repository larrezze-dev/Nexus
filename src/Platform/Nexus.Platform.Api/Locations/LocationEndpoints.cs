using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Application.Paging;
using Nexus.BuildingBlocks.Domain;
using Nexus.BuildingBlocks.Web.Authorization;
using Nexus.BuildingBlocks.Web.ErrorHandling;
using Nexus.BuildingBlocks.Web.Http;
using Nexus.BuildingBlocks.Web.OpenApi;
using Nexus.Platform.Api.Users;
using Nexus.Platform.Application.Locations;
using Nexus.Platform.Application.Locations.CreateLocation;
using Nexus.Platform.Application.Locations.GetLocation;
using Nexus.Platform.Application.Locations.GetLocations;
using Nexus.Platform.Application.Locations.UpdateLocation;

namespace Nexus.Platform.Api.Locations;

/// <summary>
/// <c>/v1/locations</c> (domain/org.md §5; <c>contracts/openapi/platform.yaml</c>, tag
/// Locations). Endpoints only translate HTTP ↔ commands/queries (conventions-backend.md §2). No
/// deactivate operation (domain/org.md §5/§6: Location has none).
/// </summary>
public static class LocationEndpoints
{
    public static RouteGroupBuilder MapLocationEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/v1/locations").WithTags("Locations");

        group.MapGet(string.Empty, ListLocations)
            .WithName("listLocations")
            .ValidatedBy<GetLocationsQuery>()
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Read)
            .WithScope("none");

        group.MapPost(string.Empty, CreateLocation)
            .WithName("createLocation")
            .ValidatedBy<CreateLocationCommand>()
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Create)
            .WithScope("none");

        group.MapGet("/{id:guid}", GetLocation)
            .WithName("getLocation")
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Read)
            .WithScope("none");

        group.MapPut("/{id:guid}", UpdateLocation)
            .WithName("updateLocation")
            .ValidatedBy<UpdateLocationCommand>()
            .RequirePermission("ORG_STRUCTURE", PermissionAction.Update)
            .WithScope("none");

        return group;
    }

    internal static async Task<Results<Ok<PageResponse<LocationListItemResponse>>, ProblemHttpResult>> ListLocations(
        IDispatcher dispatcher,
        CancellationToken cancellationToken,
        int page = PageLimits.DefaultPage,
        int pageSize = PageLimits.DefaultPageSize,
        string? sort = null)
    {
        var result = await dispatcher.Send(new GetLocationsQuery(page, pageSize, sort), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var value = result.Value;
        return TypedResults.Ok(new PageResponse<LocationListItemResponse>(
            value.Items
                .Select(l => new LocationListItemResponse(l.Id, l.Code, l.Name, l.City, l.CountryCode, l.IsActive))
                .ToList(),
            value.Page,
            value.PageSize,
            value.Total));
    }

    internal static async Task<Results<Created<LocationResponse>, Ok<LocationResponse>, ProblemHttpResult>> CreateLocation(
        CreateLocationRequest request,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(
            new CreateLocationCommand(request.Id, request.Code, request.Name, request.Street, request.PostalCode, request.City, request.Region, request.CountryCode),
            cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var (location, created) = result.Value;
        response.SetETag(location.Version.ToString(CultureInfo.InvariantCulture));
        var body = LocationResponse.From(location);

        return created
            ? TypedResults.Created($"/v1/locations/{location.Id}", body)
            : TypedResults.Ok(body);
    }

    internal static async Task<Results<Ok<LocationResponse>, ProblemHttpResult>> GetLocation(
        Guid id,
        IDispatcher dispatcher,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        var result = await dispatcher.Send(new GetLocationQuery(id), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(result.Value.Version.ToString(CultureInfo.InvariantCulture));
        return TypedResults.Ok(LocationResponse.From(result.Value));
    }

    internal static async Task<Results<Ok<LocationResponse>, ProblemHttpResult>> UpdateLocation(
        Guid id,
        UpdateLocationRequest request,
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
            new UpdateLocationCommand(id, expected, request.Code, request.Name, request.Street, request.PostalCode, request.City, request.Region, request.CountryCode),
            cancellationToken);

        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        response.SetETag(result.Value.Version.ToString(CultureInfo.InvariantCulture));
        return TypedResults.Ok(LocationResponse.From(result.Value));
    }

    private static bool TryReadExpectedVersion(HttpRequest request, out uint expected, out ProblemHttpResult problem)
    {
        expected = default;
        problem = null!;

        if (!request.TryGetIfMatch(out var token))
        {
            problem = NexusProblems.PreconditionRequired();
            return false;
        }

        if (token is null || !uint.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out expected))
        {
            problem = LocationErrors.StaleVersion.ToProblem();
            return false;
        }

        return true;
    }
}
