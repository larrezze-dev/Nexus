using System.Diagnostics.CodeAnalysis;

namespace Nexus.BuildingBlocks.Domain;

/// <summary>
/// A business-rule error, as opposed to a technical exception. <paramref name="Code"/> is the
/// id of the violated business rule as cited in the domain docs (e.g. "CAT-R2"),
/// conventions-backend.md §3.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "'Error' is the name specified by the Result/Error pattern this type belongs to; " +
                    "it is a type name, not a reserved keyword in C#, so it does not conflict in this language.")]
public sealed record Error(string Code, string Message)
{
    /// <summary>The absence of an error, used internally by successful <see cref="Result"/>s.</summary>
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>Shape validation failure (<see cref="ErrorCodes.Validation"/>, 400).</summary>
    public static Error Validation(string message) => new(ErrorCodes.Validation, message);

    /// <summary>The addressed resource does not exist or is not visible (<see cref="ErrorCodes.NotFound"/>, 404).</summary>
    public static Error NotFound(string message) => new(ErrorCodes.NotFound, message);

    /// <summary>Stale optimistic-concurrency version (<see cref="ErrorCodes.Concurrency"/>, 409).</summary>
    public static Error Concurrency(string message) => new(ErrorCodes.Concurrency, message);

    /// <summary>The resource already exists with different content (<see cref="ErrorCodes.Conflict"/>, 409).</summary>
    public static Error Conflict(string message) => new(ErrorCodes.Conflict, message);
}
