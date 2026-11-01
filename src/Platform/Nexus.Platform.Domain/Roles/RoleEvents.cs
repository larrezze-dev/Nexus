using Nexus.BuildingBlocks.Domain;

namespace Nexus.Platform.Domain.Roles;

/// <summary>
/// <c>nexus.platform.role.updated</c> (domain/access.md §9), matching
/// <c>contracts/events/platform/role.updated.v1.avsc</c> field by field. Raised on create,
/// update, deactivate and permission changes alike (full-state, EV-R4): there is deliberately no
/// separate <c>role.created</c>/<c>role.deactivated</c>, unlike the <c>org.*</c> aggregates.
/// <c>access</c> has no RLS (domain/access.md §4 preamble), so there is no legal-entity scope.
/// </summary>
public sealed record RoleUpdated(
    Guid Id,
    string Code,
    IReadOnlyDictionary<string, string> CaptionI18n,
    string? Description,
    bool IsSystem,
    bool IsActive,
    IReadOnlyList<string> PermissionCodes) : IDomainEvent
{
    public string AggregateType => "role";

    public Guid AggregateId => Id;

    public string EventType => "platform.role.updated";

    public int SchemaVersion => 1;

    public string Topic => "nexus." + EventType;

    public Guid? LegalEntityId => null;
}
