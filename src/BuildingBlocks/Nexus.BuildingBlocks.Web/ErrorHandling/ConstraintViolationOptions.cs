using Microsoft.Extensions.DependencyInjection;
using Nexus.BuildingBlocks.Domain;

namespace Nexus.BuildingBlocks.Web.ErrorHandling;

/// <summary>
/// Translations of named database constraints into caller-facing errors, used by
/// <see cref="NexusExceptionHandler"/> when a write fails with an integrity violation
/// (SQLSTATE class 23: unique, foreign key, check...).
/// <para>
/// Why at the database and not only in a validator: some invariants are enforced by the
/// database itself (e.g. ORG-R9, the partial unique index <c>uq_app_user_email_active</c>);
/// a check-then-insert in the handler would still race with a concurrent request, so the
/// constraint stays the single source of truth and its violation is what gets translated.
/// Only constraints registered here are translated; any other violation is a bug and keeps
/// surfacing as a 500.
/// </para>
/// </summary>
public sealed class ConstraintViolationOptions
{
    private readonly Dictionary<string, Error> _errors = new(StringComparer.Ordinal);

    /// <summary>The registered constraint names and their errors.</summary>
    public IReadOnlyDictionary<string, Error> Errors => _errors;

    /// <summary>
    /// Translates a violation of <paramref name="constraintName"/> into <paramref name="error"/>,
    /// with the same status rules as a failed <c>Result</c> (a rule id → 422; an
    /// <see cref="ErrorCodes"/> value → its own status, e.g. <see cref="ErrorCodes.Conflict"/> → 409).
    /// </summary>
    public ConstraintViolationOptions Map(string constraintName, Error error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(constraintName);
        ArgumentNullException.ThrowIfNull(error);
        _errors[constraintName] = error;
        return this;
    }
}

/// <summary>Registration of <see cref="ConstraintViolationOptions"/> entries.</summary>
public static class ConstraintViolationServiceCollectionExtensions
{
    /// <summary>
    /// Registers a constraint translation, e.g.
    /// <c>services.AddNexusConstraintViolation("uq_app_user_email_active", new Error("ORG-R9", "..."))</c>.
    /// Requires <see cref="NexusProblemDetailsServiceCollectionExtensions.AddNexusProblemDetails"/>.
    /// </summary>
    public static IServiceCollection AddNexusConstraintViolation(this IServiceCollection services, string constraintName, Error error)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure<ConstraintViolationOptions>(options => options.Map(constraintName, error));
        return services;
    }
}
