namespace Nexus.BuildingBlocks.Domain;

/// <summary>
/// The few <see cref="Error.Code"/> values that are not business-rule ids. A business rule
/// failure carries its rule id (<c>"ORG-R9"</c>) and becomes a 422; these well-known codes
/// describe outcomes that conventions-backend.md §7 maps to a different HTTP status, so that a
/// handler can report them through <see cref="Result"/> instead of throwing:
/// <list type="bullet">
///   <item><see cref="Validation"/> → 400 (shape validation, produced by the dispatcher).</item>
///   <item><see cref="NotFound"/> → 404 (never distinguishes "missing" from "hidden by RLS").</item>
///   <item><see cref="Concurrency"/> → 409 (the <c>If-Match</c> version is stale, BE-R3).</item>
///   <item><see cref="Conflict"/> → 409 (idempotent create with the same id but different
///   content, or a duplicate natural key; conventions-api.md §5).</item>
/// </list>
/// They are lower-case on purpose: rule ids are upper-case (<c>XXX-Rn</c>), so the two sets
/// can never collide.
/// </summary>
public static class ErrorCodes
{
    public const string Validation = "validation";

    public const string NotFound = "not_found";

    public const string Concurrency = "concurrency";

    public const string Conflict = "conflict";
}
