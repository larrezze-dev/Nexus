using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Locations;

/// <summary>
/// Caller-facing errors of the location use cases that are not domain invariants. The database
/// constraints they correspond to are registered with the web layer
/// (<c>AddNexusConstraintViolation</c>) using these same errors.
/// </summary>
public static class LocationErrors
{
    /// <summary>UNIQUE on <c>location.code</c> (nullable, so only non-null values collide).</summary>
    public const string CodeUniqueConstraint = "location_code_key";

    /// <summary>Primary key hit by two concurrent idempotent creates with the same client id.</summary>
    public const string LocationPrimaryKey = "location_pkey";

    public static readonly Error CodeAlreadyUsed =
        Error.Conflict("Another location already uses this code.");

    public static readonly Error IdAlreadyUsed =
        Error.Conflict("A location with this id already exists with different content.");

    public static Error NotFound(Guid id) => Error.NotFound($"Location '{id}' does not exist.");

    public static readonly Error StaleVersion =
        Error.Concurrency("The location has changed since the ETag sent in If-Match was obtained.");
}
