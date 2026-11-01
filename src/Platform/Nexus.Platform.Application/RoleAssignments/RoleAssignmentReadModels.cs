using Nexus.Platform.Application.Common;
using Nexus.Platform.Domain.RoleAssignments;

namespace Nexus.Platform.Application.RoleAssignments;

/// <summary>Full detail read model of a role assignment (<c>RoleAssignment</c> schema) plus its <see cref="Version"/> for the <c>ETag</c>.</summary>
public sealed record RoleAssignmentDetails(
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
    DateTimeOffset UpdatedAt,
    uint Version)
{
    /// <summary>Builds the read model from a freshly loaded/saved aggregate and its row metadata.</summary>
    public static RoleAssignmentDetails From(RoleAssignment assignment, RowMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        return new RoleAssignmentDetails(
            assignment.Id, assignment.UserId, assignment.RoleId, assignment.ScopeType, assignment.ScopeEntityId,
            assignment.ValidFrom, assignment.ValidTo, assignment.GrantedBy, assignment.Source, assignment.SourceRefId,
            assignment.RevokedBy, metadata.CreatedAt, metadata.UpdatedAt, metadata.Version);
    }
}

/// <summary>List read model of a role assignment (<c>RoleAssignmentListItem</c> schema).</summary>
public sealed record RoleAssignmentListItem(
    Guid Id,
    Guid UserId,
    Guid RoleId,
    string ScopeType,
    Guid? ScopeEntityId,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo,
    string Source);

/// <summary>A user's effective scope, grouped by dimension (<c>EffectiveScope</c> schema, domain/access.md §4).</summary>
public sealed record EffectiveScope(
    IReadOnlyList<Guid> LegalEntityIds,
    IReadOnlyList<Guid> BusinessUnitIds,
    IReadOnlyList<Guid> WarehouseIds,
    IReadOnlyList<Guid> ProjectIds);
