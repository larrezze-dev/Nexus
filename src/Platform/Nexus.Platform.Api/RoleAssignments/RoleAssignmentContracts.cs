using Nexus.Platform.Application.RoleAssignments;

namespace Nexus.Platform.Api.RoleAssignments;

// HTTP shapes of contracts/openapi/platform.yaml (RoleAssignments). PascalCase here, camelCase
// on the wire (JsonSerializerDefaults.Web).

/// <summary><c>CreateRoleAssignmentRequest</c>.</summary>
public sealed record CreateRoleAssignmentRequest
{
    /// <summary>Optional client-generated UUIDv7 (idempotent create).</summary>
    public Guid? Id { get; init; }

    public required Guid UserId { get; init; }

    public required Guid RoleId { get; init; }

    public required string ScopeType { get; init; }

    public Guid? ScopeEntityId { get; init; }

    public required DateTimeOffset ValidFrom { get; init; }

    public DateTimeOffset? ValidTo { get; init; }
}

/// <summary><c>RoleAssignment</c>.</summary>
public sealed record RoleAssignmentResponse(
    Guid Id,
    Guid UserId,
    Guid RoleId,
    string ScopeType,
    Guid? ScopeEntityId,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    Guid GrantedBy,
    string Source,
    Guid? SourceRefId,
    Guid? RevokedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static RoleAssignmentResponse From(RoleAssignmentDetails assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        return new RoleAssignmentResponse(
            assignment.Id, assignment.UserId, assignment.RoleId, assignment.ScopeType, assignment.ScopeEntityId,
            assignment.ValidFrom, assignment.ValidTo, assignment.GrantedBy, assignment.Source, assignment.SourceRefId,
            assignment.RevokedBy, assignment.CreatedAt, assignment.UpdatedAt);
    }
}

/// <summary><c>RoleAssignmentListItem</c>.</summary>
public sealed record RoleAssignmentListItemResponse(
    Guid Id,
    Guid UserId,
    Guid RoleId,
    string ScopeType,
    Guid? ScopeEntityId,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    string Source);

/// <summary><c>EffectiveScope</c>.</summary>
public sealed record EffectiveScopeResponse(
    IReadOnlyList<Guid> LegalEntityIds,
    IReadOnlyList<Guid> BusinessUnitIds,
    IReadOnlyList<Guid> WarehouseIds,
    IReadOnlyList<Guid> ProjectIds)
{
    public static EffectiveScopeResponse From(EffectiveScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new EffectiveScopeResponse(scope.LegalEntityIds, scope.BusinessUnitIds, scope.WarehouseIds, scope.ProjectIds);
    }
}
