using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.BusinessUnits;

/// <summary>
/// Caller-facing errors of the business unit use cases that are not domain invariants. The
/// database constraints they correspond to are registered with the web layer
/// (<c>AddNexusConstraintViolation</c>) using these same errors.
/// </summary>
public static class BusinessUnitErrors
{
    /// <summary>UNIQUE(legal_entity_id, code).</summary>
    public const string CodeUniqueConstraint = "business_unit_legal_entity_id_code_key";

    /// <summary>FK <c>business_unit.legal_entity_id → legal_entity.id</c>.</summary>
    public const string LegalEntityForeignKey = "business_unit_legal_entity_id_fkey";

    /// <summary>Primary key hit by two concurrent idempotent creates with the same client id.</summary>
    public const string BusinessUnitPrimaryKey = "business_unit_pkey";

    public static readonly Error CodeAlreadyUsed =
        Error.Conflict("Another business unit of this legal entity already uses this code.");

    /// <summary><c>legalEntityId</c> does not reference an existing legal entity. Not a numbered rule.</summary>
    public static readonly Error LegalEntityNotFound =
        new("LEGAL_ENTITY_NOT_FOUND", "legalEntityId does not reference an existing legal entity.");

    public static readonly Error IdAlreadyUsed =
        Error.Conflict("A business unit with this id already exists with different content.");

    public static Error NotFound(Guid id) => Error.NotFound($"Business unit '{id}' does not exist.");

    public static readonly Error StaleVersion =
        Error.Concurrency("The business unit has changed since the ETag sent in If-Match was obtained.");
}
