using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Parties;

/// <summary>
/// Caller-facing errors of the party use cases that are not domain invariants. The database
/// constraint listed here is registered with the web layer (<c>AddNexusConstraintViolation</c>)
/// using this same error, so a violation detected by PostgreSQL reads exactly like one detected
/// in code.
/// </summary>
public static class PartyErrors
{
    /// <summary>Primary key hit by two concurrent idempotent creates with the same client id.</summary>
    public const string PartyPrimaryKey = "party_pkey";

    public static readonly Error IdAlreadyUsed =
        Error.Conflict("A party with this id already exists with different content.");

    public static Error NotFound(Guid id) => Error.NotFound($"Party '{id}' does not exist.");

    public static readonly Error StaleVersion =
        Error.Concurrency("The party has changed since the ETag sent in If-Match was obtained.");
}
