using Nexus.Platform.Application.Locations;

namespace Nexus.Platform.Api.Locations;

// HTTP shapes of contracts/openapi/platform.yaml (Locations). PascalCase here, camelCase on
// the wire (JsonSerializerDefaults.Web).

/// <summary><c>CreateLocationRequest</c>.</summary>
public sealed record CreateLocationRequest
{
    /// <summary>Optional client-generated UUIDv7 (idempotent create).</summary>
    public Guid? Id { get; init; }

    public string? Code { get; init; }

    public required string Name { get; init; }

    public string? Street { get; init; }

    public string? PostalCode { get; init; }

    public string? City { get; init; }

    public string? Region { get; init; }

    public required string CountryCode { get; init; }
}

/// <summary><c>UpdateLocationRequest</c>.</summary>
public sealed record UpdateLocationRequest
{
    public string? Code { get; init; }

    public required string Name { get; init; }

    public string? Street { get; init; }

    public string? PostalCode { get; init; }

    public string? City { get; init; }

    public string? Region { get; init; }

    public required string CountryCode { get; init; }
}

/// <summary><c>Location</c>.</summary>
public sealed record LocationResponse(
    Guid Id,
    string? Code,
    string Name,
    string? Street,
    string? PostalCode,
    string? City,
    string? Region,
    string CountryCode,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static LocationResponse From(LocationDetails location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return new LocationResponse(
            location.Id, location.Code, location.Name, location.Street, location.PostalCode,
            location.City, location.Region, location.CountryCode, location.IsActive,
            location.CreatedAt, location.UpdatedAt);
    }
}

/// <summary><c>LocationListItem</c>.</summary>
public sealed record LocationListItemResponse(Guid Id, string? Code, string Name, string? City, string CountryCode, bool IsActive);
