using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.RoleAssignments;

/// <summary>
/// <c>nexus.platform.role-assignment.changed</c> (domain/access.md §9), matching
/// <c>contracts/events/platform/role-assignment.changed.v1.avsc</c> field by field. Full-state
/// payload (EV-R4) for both a grant (<see cref="RoleAssignment.Create"/>, <c>changeKind:
/// "GRANTED"</c>) and a revoke (<see cref="RoleAssignment.Revoke"/>, <c>changeKind:
/// "REVOKED"</c>) - there is deliberately no separate event type per change kind, like
/// <c>RoleUpdated</c>. <c>access</c> has no RLS (domain/access.md §4 preamble), so there is no
/// legal-entity scope at the envelope level.
/// </summary>
public sealed record RoleAssignmentChanged(
    Guid Id,
    Guid UserId,
    Guid RoleId,
    string ScopeType,
    Guid? ScopeEntityId,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    Guid GrantedBy,
    Guid? RevokedBy,
    string Source,
    Guid? SourceRefId,
    string ChangeKind) : IDomainEvent
{
    public string AggregateType => "role_assignment";

    public Guid AggregateId => Id;

    public string EventType => "platform.role-assignment.changed";

    public int SchemaVersion => 1;

    public string Topic => "nexus." + EventType;

    public Guid? LegalEntityId => null;
}
