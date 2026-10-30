using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.Locations;

/// <summary>
/// <c>org.location</c> (domain/org.md §3): a physical place (minimal 0.4 shape; geo coordinates
/// arrive in step 1.1). No <c>Party</c>, no deactivation (not part of
/// <c>contracts/openapi/platform.yaml</c>'s Locations operations), no invariants so far.
/// </summary>
public sealed class Location : AggregateRoot<Guid>
{
    private Location(Guid id)
        : base(id)
    {
    }

    public string? Code { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Street { get; private set; }

    public string? PostalCode { get; private set; }

    public string? City { get; private set; }

    public string? Region { get; private set; }

    public string CountryCode { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    /// <summary>Creates an active location and raises <see cref="LocationCreated"/>.</summary>
    public static Result<Location> Create(
        Guid id,
        string? code,
        string name,
        string? street,
        string? postalCode,
        string? city,
        string? region,
        string countryCode)
    {
        Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Guard.Against.NullOrWhiteSpace(countryCode, nameof(countryCode));

        var location = new Location(id)
        {
            Code = code,
            Name = name,
            Street = street,
            PostalCode = postalCode,
            City = city,
            Region = region,
            CountryCode = countryCode,
            IsActive = true,
        };

        location.RaiseEvent(new LocationCreated(
            location.Id, location.Code, location.Name, location.Street, location.PostalCode,
            location.City, location.Region, location.CountryCode, location.IsActive));

        return Result.Success(location);
    }

    /// <summary>
    /// Replaces every editable field (PUT semantics). Returns whether anything changed; raises
    /// <see cref="LocationUpdated"/> only in that case.
    /// </summary>
    public bool Update(string? code, string name, string? street, string? postalCode, string? city, string? region, string countryCode)
    {
        Guard.Against.NullOrWhiteSpace(name, nameof(name));
        Guard.Against.NullOrWhiteSpace(countryCode, nameof(countryCode));

        var changed =
            !string.Equals(Code, code, StringComparison.Ordinal)
            || !string.Equals(Name, name, StringComparison.Ordinal)
            || !string.Equals(Street, street, StringComparison.Ordinal)
            || !string.Equals(PostalCode, postalCode, StringComparison.Ordinal)
            || !string.Equals(City, city, StringComparison.Ordinal)
            || !string.Equals(Region, region, StringComparison.Ordinal)
            || !string.Equals(CountryCode, countryCode, StringComparison.Ordinal);

        if (!changed)
        {
            return false;
        }

        Code = code;
        Name = name;
        Street = street;
        PostalCode = postalCode;
        City = city;
        Region = region;
        CountryCode = countryCode;

        RaiseEvent(new LocationUpdated(Id, Code, Name, Street, PostalCode, City, Region, CountryCode, IsActive));
        return true;
    }
}
