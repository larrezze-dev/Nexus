using ClickHouse.Client.Copy;
using Nexus.Projections.Abstractions;

namespace Nexus.Projections.Runtime;

/// <summary>Writes a batch of projection rows. Seam for tests; the real one is <see cref="ClickHouseProjectionSink"/>.</summary>
public interface IProjectionSink
{
    /// <summary>
    /// Inserts <paramref name="rows"/> into <see cref="IProjection.TableName"/>. Completes only when the
    /// server acknowledged the insert; throws otherwise (the caller then keeps the batch and retries).
    /// </summary>
    Task InsertAsync(IProjection projection, IReadOnlyList<object?[]> rows, CancellationToken ct);
}

/// <summary>
/// <see cref="IProjectionSink"/> over <see cref="ClickHouseBulkCopy"/>: the rows travel in RowBinary,
/// typed against the table's own column types (read by <see cref="ClickHouseBulkCopy.InitAsync"/>), so no
/// value is ever rendered into SQL text, and Map/UUID/Nullable/DateTime64 need no hand-written
/// formatting. <see cref="ClickHouseBulkCopy.BatchSize"/> is set to the batch length and
/// <see cref="ClickHouseBulkCopy.MaxDegreeOfParallelism"/> to 1, so one flush is ONE INSERT (a single
/// block, applied atomically by MergeTree). A retried insert after a failure may duplicate rows that did
/// land: <c>ReplacingMergeTree</c> collapses them (conventions-events.md §8).
/// </summary>
public sealed class ClickHouseProjectionSink(ClickHouseConnectionProvider connections) : IProjectionSink
{
    public async Task InsertAsync(IProjection projection, IReadOnlyList<object?[]> rows, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            return;
        }

        using var connection = connections.Create();
        using var bulk = new ClickHouseBulkCopy(connection)
        {
            DestinationTableName = projection.TableName,
            ColumnNames = projection.ColumnNames.ToArray(),
            BatchSize = rows.Count,
            MaxDegreeOfParallelism = 1,
        };
        await bulk.InitAsync().WaitAsync(ct).ConfigureAwait(false);
        await bulk.WriteToServerAsync(rows.Select(static r => (object[])r!), ct).ConfigureAwait(false);
    }
}
