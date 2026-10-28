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

    public static readonly NexusProblemType NotFound =
        new(404, BaseUri + "not-found", "Resource not found", "NOT_FOUND");

    public static readonly NexusProblemType Concurrency =
        new(409, BaseUri + "conflict", "Concurrency conflict", "CONCURRENCY");

    public static readonly NexusProblemType InvalidState =
        new(409, BaseUri + "conflict", "Invalid state transition", "INVALID_STATE");

    public static readonly NexusProblemType BusinessRule =
        new(422, BaseUri + "unprocessable-entity", "Business rule violation", "BUSINESS_RULE");

    public static readonly NexusProblemType PreconditionRequired =
        new(428, BaseUri + "precondition-required", "Missing If-Match", "PRECONDITION_REQUIRED");

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
