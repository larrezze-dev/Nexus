using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Warehouses;

/// <summary>
/// Caller-facing errors of the warehouse use cases that are not domain invariants. The database
/// constraints they correspond to are registered with the web layer
/// (<c>AddNexusConstraintViolation</c>) using these same errors.
/// </summary>
public static class WarehouseErrors
{
    public const string CodeUniqueConstraint = "warehouse_code_key";

    public const string LocationForeignKey = "warehouse_location_id_fkey";

    public const string LegalEntityForeignKey = "warehouse_legal_entity_id_fkey";

    public const string BusinessUnitForeignKey = "warehouse_business_unit_id_fkey";

    /// <summary>Primary key hit by two concurrent idempotent creates with the same client id.</summary>
    public const string WarehousePrimaryKey = "warehouse_pkey";

    public static readonly Error CodeAlreadyUsed =
        Error.Conflict("Another warehouse already uses this code.");

    public static readonly Error LocationNotFound =
        new("LOCATION_NOT_FOUND", "locationId does not reference an existing location.");

    public static readonly Error LegalEntityNotFound =
        new("LEGAL_ENTITY_NOT_FOUND", "legalEntityId does not reference an existing legal entity.");

    public static readonly Error BusinessUnitNotFound =
        new("BUSINESS_UNIT_NOT_FOUND", "businessUnitId does not reference an existing business unit.");

    /// <summary>Rule ORG-R3: a warehouse's business unit must belong to the same legal entity.</summary>
    public static readonly Error LegalEntityMismatch =
        new("ORG-R3", "The warehouse's businessUnitId must belong to the same legalEntityId.");

    public static readonly Error IdAlreadyUsed =
        Error.Conflict("A warehouse with this id already exists with different content.");

    public static Error NotFound(Guid id) => Error.NotFound($"Warehouse '{id}' does not exist.");

    public static readonly Error StaleVersion =
        Error.Concurrency("The warehouse has changed since the ETag sent in If-Match was obtained.");
}
