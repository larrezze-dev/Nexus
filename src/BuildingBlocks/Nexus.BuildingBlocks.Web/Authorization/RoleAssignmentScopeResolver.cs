using System.Text.Json;
using Dapper;
using Npgsql;

namespace Nexus.BuildingBlocks.Web.Authorization;

/// <summary>
/// Resolves the <c>x-scope: body.scopeEntityId</c> of <c>POST /v1/role-assignments</c> into the
/// <c>access.has_permission</c> arguments that implement ACC-R4: "the granter needs
/// <c>ROLE_ASSIGNMENT:CREATE</c> in a scope that covers the target scope". Coverage follows the
/// hierarchy GLOBAL ⊃ GROUP ⊃ LEGAL_ENTITY ⊃ BUSINESS_UNIT ⊃ WAREHOUSE (PROJECT stands apart).
/// Also used, through <c>IRoleAssignmentScopeChecker</c>, for the scope of an existing row
/// (<c>revokeRoleAssignment</c>'s <c>ROLE_ASSIGNMENT:UPDATE</c> covering check).
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

                // Unknown BU: keep just the id, so only GLOBAL (or an exact BU grant) covers it.
                // A GLOBAL admin therefore gets past this filter and receives the create
                // handler's ACC-R2 422 (scopeEntityId must exist, checked against
                // org.business_unit there); anyone else gets a 403 here.
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

                // Unknown warehouse: same as an unknown BU above (GLOBAL → handler's ACC-R2 422).
                return new PermissionTarget(owner?.LegalEntityId, owner?.BusinessUnitId, id, null);
            }

            case "GROUP":
                // ADR-0015 (Aceptado): only p_group_id is passed. has_permission's GROUP clause
                // compares against it directly, and with no legal entity/BU/warehouse/project
                // passed, none of the narrower clauses can match -- so only a GLOBAL grant or a
                // GROUP grant on this exact group covers it. An ORG_ADMIN of one LE of the group
                // gets false (no escalation to the whole group), and since the function's "no
                // scope given" catch-all also requires p_group_id IS NULL, a GROUP-only call is
                // never mistaken for the listing-only "any scope" form (ACC-R10).
                return new PermissionTarget(null, null, null, null, GroupId: id);

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
