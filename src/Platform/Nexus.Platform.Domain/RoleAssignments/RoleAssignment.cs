using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.RoleAssignments;

/// <summary>
/// <c>access.role_assignment</c> (domain/access.md §3/§4): grants a <c>Role</c> to a user in a
/// scope (<see cref="ScopeType"/>/<see cref="ScopeEntityId"/>). Append-only in spirit: a grant is
/// never edited, only revoked (<see cref="Revoke"/> sets <see cref="ValidTo"/>/
/// <see cref="RevokedBy"/>, it never deletes).
/// <para>
/// ACC-R1 ("<c>scopeEntityId</c> is required unless <c>scopeType == GLOBAL</c>"), ACC-R2
/// ("<c>PROJECT</c> scope is rejected before step 2.1") and ACC-R5 ("nobody grants/revokes their
/// own assignment") are all checked <b>here</b>, not by the caller: unlike ORG-R3 on
/// <c>Warehouse</c>, none of them needs a database lookup of another aggregate - they only need
/// this row's own fields plus the acting user's id, which <see cref="Create"/>/<see cref="Revoke"/>
/// already receive as parameters (conventions-backend.md §3). ACC-R4 (the granter's own scope
/// must cover the target scope) is enforced earlier, by <c>AccessPermissionChecker</c> on the
/// endpoint filter, before a command ever reaches this aggregate.
/// </para>
/// </summary>
public sealed class RoleAssignment : AggregateRoot<Guid>
{
    /// <summary>The scope types the DDL's <c>CHECK</c> allows (domain/access.md §3).</summary>
    public const string ScopeGroup = "GROUP";
    public const string ScopeLegalEntity = "LEGAL_ENTITY";
    public const string ScopeBusinessUnit = "BUSINESS_UNIT";
    public const string ScopeWarehouse = "WAREHOUSE";
    public const string ScopeProject = "PROJECT";
    public const string ScopeGlobal = "GLOBAL";

    /// <summary>The <c>source</c> values the DDL's <c>CHECK</c> allows.</summary>
    public const string SourceManual = "MANUAL";
    public const string SourceResourceAssignment = "RESOURCE_ASSIGNMENT";
    public const string SourceSeed = "SEED";

    private RoleAssignment(Guid id)
        : base(id)
    {
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }

    public string ScopeType { get; private set; } = string.Empty;

    public Guid? ScopeEntityId { get; private set; }

    public DateTimeOffset ValidFrom { get; private set; }

    public DateTimeOffset? ValidTo { get; private set; }

    public Guid GrantedBy { get; private set; }

    public string Source { get; private set; } = SourceManual;

    public Guid? SourceRefId { get; private set; }

    public Guid? RevokedBy { get; private set; }

    /// <summary>
    /// Creates a <c>MANUAL</c> grant (every API-created assignment; <c>SEED</c>/
    /// <c>RESOURCE_ASSIGNMENT</c> rows come from seeds/other flows, not this use case) and raises
    /// <see cref="RoleAssignmentChanged"/> with <c>changeKind: "GRANTED"</c>.
    /// </summary>
    public static Result<RoleAssignment> Create(
        Guid id,
        Guid userId,
        Guid roleId,
        string scopeType,
        Guid? scopeEntityId,
        DateTimeOffset validFrom,
        DateTimeOffset? validTo,
        Guid grantedBy)
    {
        Guard.Against.NullOrWhiteSpace(scopeType, nameof(scopeType));

        // ACC-R2: PROJECT scope has no access.project_ref yet (step 2.1) - rejected
        // unconditionally, before even looking at scopeEntityId.
        if (scopeType == ScopeProject)
        {
            return Result.Failure<RoleAssignment>(
                new Error("ACC-R2", "scopeType 'PROJECT' is not supported until step 2.1."));
        }

        // ACC-R1: mirrors the DDL's CHECK ((scope_type = 'GLOBAL') = (scope_entity_id IS NULL)).
        var isGlobal = scopeType == ScopeGlobal;
        if (isGlobal != (scopeEntityId is null))
        {
            return Result.Failure<RoleAssignment>(new Error(
                "ACC-R1",
                "'scopeEntityId' is required unless scopeType is 'GLOBAL', and must be absent when it is."));
        }

        // ACC-R5: nobody grants a role assignment to themselves.
        if (userId == grantedBy)
        {
            return Result.Failure<RoleAssignment>(
                new Error("ACC-R5", "A user cannot grant a role assignment to themselves."));
        }

        var assignment = new RoleAssignment(id)
        {
            UserId = userId,
            RoleId = roleId,
            ScopeType = scopeType,
            ScopeEntityId = scopeEntityId,
            ValidFrom = validFrom,
            ValidTo = validTo,
            GrantedBy = grantedBy,
            Source = SourceManual,
            SourceRefId = null,
        };

        assignment.RaiseEvent(assignment.ToChangedEvent("GRANTED"));
        return Result.Success(assignment);
    }

    /// <summary>
    /// Revokes the assignment: sets <see cref="ValidTo"/> to <paramref name="now"/> and
    /// <see cref="RevokedBy"/>, never deletes the row (ACC-R3). Idempotent: an assignment that no
    /// longer grants anything as of <paramref name="now"/> - already revoked
    /// (<see cref="RevokedBy"/> set) or already expired (<see cref="ValidTo"/> &lt;=
    /// <paramref name="now"/>) - is returned unchanged (<see langword="false"/>, no new event).
    /// A still-active assignment that merely carries a <b>future</b> <see cref="ValidTo"/> (a
    /// time-boxed grant, e.g. ACC-R8's <c>FIELD_CAPTURE</c>) is <b>not</b> "already revoked": its
    /// <see cref="ValidTo"/> is pulled in to <paramref name="now"/>. Checked in this
    /// order: ACC-R5 (nobody revokes their own assignment) before ACC-R6 (the seeded SYSTEM/first
    /// administrator GLOBAL grant can never be revoked through the API) - both can be true of the
    /// same row at once (e.g. SYSTEM revoking its own seeded GLOBAL grant), and ACC-R5 is checked
    /// first so that "self" always reports as ACC-R5, consistently with <see cref="Create"/>.
    /// </summary>
    public Result<bool> Revoke(Guid revokedBy, DateTimeOffset now)
    {
        if (RevokedBy is not null || (ValidTo is { } validTo && validTo <= now))
        {
            return Result.Success(false);
        }

        if (UserId == revokedBy)
        {
            return Result.Failure<bool>(
                new Error("ACC-R5", "A user cannot revoke their own role assignment."));
        }

        if (Source == SourceSeed && ScopeType == ScopeGlobal)
        {
            return Result.Failure<bool>(new Error(
                "ACC-R6",
                "A seeded GLOBAL role assignment cannot be revoked through the API."));
        }

        ValidTo = now;
        RevokedBy = revokedBy;

        RaiseEvent(ToChangedEvent("REVOKED"));
        return Result.Success(true);
    }

    private RoleAssignmentChanged ToChangedEvent(string changeKind) => new(
        Id, UserId, RoleId, ScopeType, ScopeEntityId, ValidFrom, ValidTo, GrantedBy, RevokedBy, Source, SourceRefId, changeKind);
}
