using Nexus.Platform.Application.Groups;

namespace Nexus.Platform.Api.Groups;

// HTTP shapes of contracts/openapi/platform.yaml (Groups). PascalCase here, camelCase on the
// wire (JsonSerializerDefaults.Web). `required` marks the contract's required request
// properties: the JSON binder rejects a body without them (400) and the generated OpenAPI
// document lists them as required, like the contract.

/// <summary><c>CreateGroupRequest</c>.</summary>
public sealed record CreateGroupRequest
{
    /// <summary>Optional client-generated UUIDv7 (idempotent create).</summary>
    public Guid? Id { get; init; }

    public required string LegalName { get; init; }

    public string? DisplayName { get; init; }

    public required string Code { get; init; }

    public string ConsolidationMethod { get; init; } = "FULL";

    public required string ReportingCurrencyCode { get; init; }
}

/// <summary><c>UpdateGroupRequest</c>.</summary>
public sealed record UpdateGroupRequest
{
    public required string Code { get; init; }

    public required string ConsolidationMethod { get; init; }

    public required string ReportingCurrencyCode { get; init; }
}

/// <summary><c>Group</c>.</summary>
public sealed record GroupResponse(
    Guid Id,
    Guid PartyId,
    string Code,
    string ConsolidationMethod,
    string ReportingCurrencyCode,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static GroupResponse From(GroupDetails group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return new GroupResponse(
            group.Id, group.PartyId, group.Code, group.ConsolidationMethod, group.ReportingCurrencyCode,
            group.CreatedAt, group.UpdatedAt);
    }
}

/// <summary><c>GroupListItem</c>.</summary>
public sealed record GroupListItemResponse(Guid Id, Guid PartyId, string Code, string ReportingCurrencyCode);
