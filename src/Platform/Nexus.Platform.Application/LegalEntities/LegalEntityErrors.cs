using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.LegalEntities;

/// <summary>
/// Caller-facing errors of the legal entity use cases that are not domain invariants. The
/// database constraints they correspond to are registered with the web layer
/// (<c>AddNexusConstraintViolation</c>) using these same errors, so a violation detected by
/// PostgreSQL reads exactly like one detected in code.
/// </summary>
public static class LegalEntityErrors
{
    /// <summary>UNIQUE on <c>legal_entity.code</c>.</summary>
    public const string CodeUniqueConstraint = "legal_entity_code_key";

    /// <summary>FK <c>legal_entity.group_id → "group".id</c>.</summary>
    public const string GroupForeignKey = "legal_entity_group_id_fkey";

    /// <summary>Primary key hit by two concurrent idempotent creates with the same client id.</summary>
    public const string LegalEntityPrimaryKey = "legal_entity_pkey";

    public static readonly Error CodeAlreadyUsed =
        Error.Conflict("Another legal entity already has this code.");

    /// <summary>
    /// <c>groupId</c> does not reference an existing group. Not a rule of domain/org.md §4 (no
    /// id to cite), so it carries a descriptive code.
    /// </summary>
    public static readonly Error GroupNotFound =
        new("GROUP_NOT_FOUND", "groupId does not reference an existing group.");

    public static readonly Error IdAlreadyUsed =
        Error.Conflict("A legal entity with this id already exists with different content.");

    public static Error NotFound(Guid id) => Error.NotFound($"Legal entity '{id}' does not exist.");

    public static readonly Error StaleVersion =
        Error.Concurrency("The legal entity has changed since the ETag sent in If-Match was obtained.");

    /// <summary>Rule ORG-R5.</summary>
    public static readonly Error HasActiveChildren =
        new("ORG-R5", "A legal entity cannot be deactivated while it has an active BusinessUnit or Warehouse.");
}
