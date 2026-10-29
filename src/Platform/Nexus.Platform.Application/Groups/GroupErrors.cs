using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Groups;

/// <summary>
/// Caller-facing errors of the group use cases that are not domain invariants. The database
/// constraints they correspond to are registered with the web layer
/// (<c>AddNexusConstraintViolation</c>) using these same errors, so a violation detected by
/// PostgreSQL reads exactly like one detected in code.
/// </summary>
public static class GroupErrors
{
    /// <summary>UNIQUE on <c>"group".code</c>.</summary>
    public const string CodeUniqueConstraint = "group_code_key";

    /// <summary>Primary key hit by two concurrent idempotent creates with the same client id.</summary>
    public const string GroupPrimaryKey = "group_pkey";

    public static readonly Error CodeAlreadyUsed =
        Error.Conflict("Another group already has this code.");

    public static readonly Error IdAlreadyUsed =
        Error.Conflict("A group with this id already exists with different content.");

    public static Error NotFound(Guid id) => Error.NotFound($"Group '{id}' does not exist.");

    public static readonly Error StaleVersion =
        Error.Concurrency("The group has changed since the ETag sent in If-Match was obtained.");
}
