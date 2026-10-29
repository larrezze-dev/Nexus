using Nexus.BuildingBlocks.Domain;

namespace Nexus.BuildingBlocks.Web.ErrorHandling;

/// <summary>
/// The RFC 9457 `type`/`code` pairs for every situation in conventions-backend.md §7, kept in
/// one place so every path that produces a <c>application/problem+json</c> response (the
/// exception handler, the built-in ASP.NET Core problem-details pipeline for 401/403/404, and
/// <see cref="ResultExtensions"/> for 422) agrees on the same values. The schema itself
/// (`type`, `title`, `status`, `detail`, `instance`, `code`, `errors`) is
/// <c>contracts/openapi/_shared/components.yaml#/components/schemas/Problem</c>.
/// </summary>
internal sealed class NexusProblemType
{
    private const string BaseUri = "https://nexus.nussli.ch/problems/";

    public static readonly NexusProblemType Validation =
        new(400, BaseUri + "validation", "Validation failed", "VALIDATION");

    public static readonly NexusProblemType BadRequest =
        new(400, BaseUri + "bad-request", "Malformed request", "BAD_REQUEST");

    public static readonly NexusProblemType Unauthorized =
        new(401, BaseUri + "unauthorized", "Not authenticated", "UNAUTHORIZED");

    public static readonly NexusProblemType Forbidden =
        new(403, BaseUri + "forbidden", "Access denied", "FORBIDDEN");

    /// <summary>
    /// Authenticated, but the token's <c>oid</c> maps to no active <c>org.app_user</c>
    /// (ADR-0006). Same <c>type</c> as <see cref="Forbidden"/>; the lower-case <c>code</c> is the
    /// exact value fixed by <c>contracts/openapi/platform.yaml</c> (<c>GET /v1/me</c>, 403).
    /// </summary>
    public static readonly NexusProblemType UserNotProvisioned =
        new(403, BaseUri + "forbidden", "User not provisioned", "user_not_provisioned");

    public static readonly NexusProblemType NotFound =
        new(404, BaseUri + "not-found", "Resource not found", "NOT_FOUND");

    public static readonly NexusProblemType Concurrency =
        new(409, BaseUri + "conflict", "Concurrency conflict", "CONCURRENCY");

    /// <summary>
    /// The resource already exists with different content (idempotent create with a reused
    /// <c>id</c>, conventions-api.md §5) or a natural key is already taken. Code
    /// <c>CONFLICT</c>, as in <c>_shared/components.yaml#/components/responses/Conflict</c>.
    /// </summary>
    public static readonly NexusProblemType Conflict =
        new(409, BaseUri + "conflict", "Conflict", "CONFLICT");

    public static readonly NexusProblemType InvalidState =
        new(409, BaseUri + "conflict", "Invalid state transition", "INVALID_STATE");

    public static readonly NexusProblemType BusinessRule =
        new(422, BaseUri + "unprocessable-entity", "Business rule violation", "BUSINESS_RULE");

    public static readonly NexusProblemType PreconditionRequired =
        new(428, BaseUri + "precondition-required", "Missing If-Match", "PRECONDITION_REQUIRED");

    /// <summary>
    /// The problem type and wire <c>code</c> for a failed <c>Result</c>'s <see cref="Error"/>:
    /// a well-known <see cref="ErrorCodes"/> value maps to its own status and upper-case code;
    /// anything else is a business-rule id → 422 with that id as <c>code</c>.
    /// </summary>
    public static (NexusProblemType ProblemType, string Code) For(Error error)
    {
        var wellKnown = error.Code switch
        {
            ErrorCodes.Validation => Validation,
            ErrorCodes.NotFound => NotFound,
            ErrorCodes.Concurrency => Concurrency,
            ErrorCodes.Conflict => Conflict,
            _ => null,
        };

        return wellKnown is null ? (BusinessRule, error.Code) : (wellKnown, wellKnown.Code);
    }

    private NexusProblemType(int status, string type, string title, string code)
    {
        Status = status;
        Type = type;
        Title = title;
        Code = code;
    }

    public int Status { get; }

    public string Type { get; }

    public string Title { get; }

    public string Code { get; }
}
