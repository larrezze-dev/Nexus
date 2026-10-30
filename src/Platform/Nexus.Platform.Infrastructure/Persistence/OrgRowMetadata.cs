using Microsoft.EntityFrameworkCore.ChangeTracking;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Persistence.Auditing;
using Nexus.Platform.Application.Common;

namespace Nexus.Platform.Infrastructure.Persistence;

/// <summary>Builds <see cref="RowMetadata"/> from a tracked entity's shadow properties.</summary>
internal static class OrgRowMetadata
{
    public static RowMetadata Read(EntityEntry entry)
    {
        var audit = AuditColumns.Read(entry);
        return new RowMetadata(RowVersion.Read(entry), audit.CreatedAt, audit.UpdatedAt);
    }
}
