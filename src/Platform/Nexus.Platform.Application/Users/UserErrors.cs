using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Application.Users;

/// <summary>
/// Caller-facing errors of the user use cases that are not domain invariants. The database
/// constraints they correspond to are registered with the web layer
/// (<c>AddNexusConstraintViolation</c>) using these same errors, so a violation detected by
/// PostgreSQL reads exactly like one detected in code.
/// </summary>
public static class UserErrors
{
    /// <summary>Partial unique index enforcing ORG-R9 (email unique among active users, case-insensitive).</summary>
    public const string EmailActiveUniqueConstraint = "uq_app_user_email_active";

    /// <summary>UNIQUE on <c>app_user.external_subject</c>: one user per <c>oid</c>.</summary>
    public const string ExternalSubjectUniqueConstraint = "app_user_external_subject_key";

    /// <summary>FK <c>app_user.business_unit_id → business_unit.id</c>.</summary>
    public const string BusinessUnitForeignKey = "app_user_business_unit_id_fkey";

    /// <summary>Primary key hit by two concurrent idempotent creates with the same client id.</summary>
    public const string UserPrimaryKey = "app_user_pkey";

    /// <summary>Rule ORG-R9.</summary>
    public static readonly Error EmailAlreadyUsed =
        new("ORG-R9", "Another active user already has this email.");

    public static readonly Error OidAlreadyUsed =
        Error.Conflict("Another user already has this oid (external_subject).");

    /// <summary>
    /// <c>businessUnitId</c> does not reference an existing business unit. Not a rule of
    /// domain/org.md §4 (no id to cite), so it carries a descriptive code.
    /// </summary>
    public static readonly Error BusinessUnitNotFound =
        new("BUSINESS_UNIT_NOT_FOUND", "businessUnitId does not reference an existing business unit.");

    public static readonly Error IdAlreadyUsed =
        Error.Conflict("A user with this id already exists with different content.");

    public static Error NotFound(Guid id) => Error.NotFound($"User '{id}' does not exist.");

    public static readonly Error StaleVersion =
        Error.Concurrency("The user has changed since the ETag sent in If-Match was obtained.");
}
