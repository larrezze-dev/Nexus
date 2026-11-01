using System.Text.Json;
using Dapper;
using Npgsql;

namespace Nexus.BuildingBlocks.Web.Authorization;

/// <summary>
/// Resolves the <c>x-scope: body.scopeEntityId</c> of <c>POST /v1/role-assignments</c> into the
/// <c>access.has_permission</c> arguments that implement ACC-R4: "the granter needs
/// <c>ROLE_ASSIGNMENT:CREATE</c> in a scope that covers the target scope". Coverage follows the
/// hierarchy GLOBAL ⊃ GROUP ⊃ LEGAL_ENTITY ⊃ BUSINESS_UNIT ⊃ WAREHOUSE (PROJECT stands apart).
/// <para>
/// A <see langword="null"/> result means "no basis to grant": the caller treats it as
/// <c>has_permission</c> returning false (403). Malformed input never throws: ACC-R1/ACC-R2
/// validation (422) is the handler's job, and the filter only has to fail closed, not crash.
/// </para>
/// </summary>
internal static class RoleAssignmentScopeResolver
{
    /// <summary>Reads <c>scopeType</c>/<c>scopeEntityId</c> from a JSON request body.
    /// Property names are matched case-insensitively, like ASP.NET Core's JSON binding.</summary>
    public static bool TryParse(JsonElement body, out string? scopeType, out Guid? scopeEntityId)
    {
        scopeType = null;
        scopeEntityId = null;
        if (body.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in body.EnumerateObject())
        {
            if (string.Equals(property.Name, "scopeType", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                scopeType = property.Value.GetString();
            }
            else if (string.Equals(property.Name, "scopeEntityId", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    scopeEntityId = null;
                }
                else if (property.Value.ValueKind == JsonValueKind.String && property.Value.TryGetGuid(out var id))
                {
                    scopeEntityId = id;
                }
                else
                {
                    return false;
                }
            }
        }

        return scopeType is not null;
    }

    public static async Task<PermissionTarget?> ResolveAsync(
        string? scopeType,
        Guid? scopeEntityId,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        if (scopeType == "GLOBAL")
        {
            // Only a GLOBAL grant covers GLOBAL (see PermissionTarget.GlobalOnly).
            return PermissionTarget.GlobalOnly();
        }

        if (scopeEntityId is not { } id)
        {
            // Non-GLOBAL without an entity id violates ACC-R1: the handler answers 422; here
            // there is simply nothing a grant could cover.
            return null;
        }

        switch (scopeType)
        {
            case "LEGAL_ENTITY":
                // has_permission derives the LE's group itself: GROUP and LEGAL_ENTITY grants match.
                return new PermissionTarget(id, null, null, null);

            case "BUSINESS_UNIT":
            {
                // Pass the owning LE too: has_permission derives GROUP only from
                // p_legal_entity_id, never from p_business_unit_id alone.
                var legalEntityId = await connection.QuerySingleOrDefaultAsync<Guid?>(
                    new CommandDefinition(
                        "SELECT legal_entity_id FROM org.business_unit WHERE id = @id",
                        new { id },
                        transaction,
                        cancellationToken: cancellationToken));

                // Unknown BU: keep just the id, so only GLOBAL (or an exact BU grant) covers it
                // and a GLOBAL admin still reaches the handler's ACC-R2 422 instead of a 403.
                return new PermissionTarget(legalEntityId, id, null, null);
            }

            case "WAREHOUSE":
            {
                var owner = await connection.QuerySingleOrDefaultAsync<WarehouseOwner?>(
                    new CommandDefinition(
                        "SELECT legal_entity_id AS LegalEntityId, business_unit_id AS BusinessUnitId FROM org.warehouse WHERE id = @id",
                        new { id },
                        transaction,
                        cancellationToken: cancellationToken));

                return new PermissionTarget(owner?.LegalEntityId, owner?.BusinessUnitId, id, null);
            }

            case "GROUP":
                // Fail closed: only a GLOBAL grant covers a GROUP target here. has_permission has
                // no group parameter -- it derives the group from p_legal_entity_id -- and any LE
                // passed to reach the GROUP clause also satisfies its LEGAL_ENTITY clause. Passing
                // "some LE of the group" would therefore let an ORG_ADMIN of one LE grant a
                // GROUP-scoped role covering every sibling LE: a privilege escalation. A
                // GROUP-scoped granter is (temporarily) refused; see the proposed ADR on a
                // p_group_id parameter for has_permission.
                return PermissionTarget.GlobalOnly();

            case "PROJECT":
                // Projects live in nexus-operations; the platform cannot resolve their LE
                // (ARC-R3), so only GLOBAL or an exact PROJECT grant covers one. ACC-R2 rejects
                // PROJECT before step 2.1 anyway (handler).
                return new PermissionTarget(null, null, null, id);

            default:
                return null;
        }
    }

    private sealed record WarehouseOwner(Guid LegalEntityId, Guid BusinessUnitId);
}
