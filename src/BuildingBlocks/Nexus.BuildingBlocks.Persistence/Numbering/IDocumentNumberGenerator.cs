using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;

namespace Nexus.BuildingBlocks.Persistence.Numbering;

/// <summary>
/// Gap-free document numbers (conventions-database.md §11, DB-R5). Each call takes the next
/// value of a counter row in <c>&lt;schema&gt;.number_sequence</c> inside the active
/// <see cref="IUnitOfWork"/> transaction: concurrent callers with the same scope key serialize
/// on that row's lock, and a rollback returns the number. Never backed by a PostgreSQL
/// <c>SEQUENCE</c> (not transactional).
/// </summary>
public interface IDocumentNumberGenerator
{
    /// <summary>
    /// Returns the next number (1, 2, 3, ...) for <paramref name="scopeKey"/>, already formed
    /// by the calling domain (e.g. <c>"PO:LE-CH:2026"</c>); formatting the human-readable code
    /// is the domain's job.
    /// </summary>
    Task<long> NextNumberAsync(string scopeKey, CancellationToken cancellationToken = default);
}

/// <summary>Service configuration for <see cref="DocumentNumberGenerator"/>.</summary>
public sealed class DocumentNumberingOptions
{
    /// <summary>
    /// The service's own schema holding <c>number_sequence</c> (ARC-R1: a service numbers only
    /// in its own schema). Lower-case unquoted identifier.
    /// </summary>
    public required string Schema { get; init; }
}

/// <inheritdoc />
public sealed partial class DocumentNumberGenerator : IDocumentNumberGenerator
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly string _sql;

    public DocumentNumberGenerator(IUnitOfWork unitOfWork, DocumentNumberingOptions options)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(options);

        // Identifiers cannot be bound as parameters: whitelist, then quote.
        if (options.Schema is null || !SchemaPattern().IsMatch(options.Schema))
        {
            throw new ArgumentException(
                $"Invalid schema name '{options.Schema}': expected a lower-case identifier matching [a-z_][a-z0-9_]{{0,62}}.",
                nameof(options));
        }

        _unitOfWork = unitOfWork;
        _sql =
            $"INSERT INTO \"{options.Schema}\".number_sequence AS s (scope_key, next_value) VALUES ($1, 2) " +
            "ON CONFLICT (scope_key) DO UPDATE SET next_value = s.next_value + 1 " +
            "RETURNING next_value - 1";
    }

    public async Task<long> NextNumberAsync(string scopeKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);

        // Throws if no unit of work is active: a number outside the document's transaction
        // could not be rolled back with it (DB-R5).
        await using var command = new NpgsqlCommand(_sql, _unitOfWork.Connection, _unitOfWork.Transaction);
        command.Parameters.Add(new NpgsqlParameter<string> { TypedValue = scopeKey, NpgsqlDbType = NpgsqlDbType.Text });
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return (long)result!;
    }

    [GeneratedRegex("^[a-z_][a-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaPattern();
}
