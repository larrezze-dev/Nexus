using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.Locations;

namespace Nexus.Platform.Application.Locations;

/// <summary>Detail read model of a location (<c>Location</c> schema) plus its <see cref="Version"/> for the <c>ETag</c>.</summary>
public sealed record LocationDetails(
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
    DateTime UpdatedAt,
    uint Version)
{
    /// <summary>Builds the read model from a freshly loaded/saved aggregate and its row metadata.</summary>
    public static LocationDetails From(Location location, RowMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(location);

        return new LocationDetails(
            location.Id, location.Code, location.Name, location.Street, location.PostalCode,
            location.City, location.Region, location.CountryCode, location.IsActive,
            metadata.CreatedAt.UtcDateTime, metadata.UpdatedAt.UtcDateTime, metadata.Version);
    }
}

/// <summary>List read model of a location (<c>LocationListItem</c> schema).</summary>
public sealed record LocationListItem(Guid Id, string? Code, string Name, string? City, string CountryCode, bool IsActive);
