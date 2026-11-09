using System.Data;
using ClickHouse.Client;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Utility;
using Nexus.BuildingBlocks.Analytics.Scope;

namespace Nexus.BuildingBlocks.Analytics.Querying;

/// <inheritdoc cref="IAnalyticsQueryExecutor"/>
internal sealed class AnalyticsQueryExecutor : IAnalyticsQueryExecutor
{
    private readonly IAnalyticsConnectionFactory _connectionFactory;

    public AnalyticsQueryExecutor(IAnalyticsConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        IAnalyticsQuery<TResult> query,
        ScopeFilter scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(scope);

        // Nothing visible: answer without ClickHouse (no round trip, and no "IN []" edge case).
        if (scope.IsEmpty)
        {
            return query.EmptyResult;
        }

        await using var session = new ScopedSession(_connectionFactory, scope);
        return await query.ExecuteAsync(session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The scoped session of one execution. Opens its connection lazily, on the first
    /// statement, and refuses any use after the execution ended.</summary>
    private sealed class ScopedSession : IAnalyticsSession, IAsyncDisposable
    {
        private const string InScope = "legal_entity_id IN {" + AnalyticsStatement.ScopeLegalEntitiesParameter + ":Array(UUID)}";

        /// <summary>ClickHouse <c>TIMEOUT_EXCEEDED</c>.</summary>
        private const int TimeoutExceededErrorCode = 159;

        private readonly IAnalyticsConnectionFactory _connectionFactory;
        private readonly ScopeFilter _scope;
        private readonly string _predicate;
        private ClickHouseConnection? _connection;
        private bool _disposed;

        public ScopedSession(IAnalyticsConnectionFactory connectionFactory, ScopeFilter scope)
        {
            _connectionFactory = connectionFactory;
            _scope = scope;

            // Structure only depends on the flag; the ids are always a bound parameter.
            _predicate = scope.IncludeUnscoped
                ? "(" + InScope + " OR legal_entity_id IS NULL)"
                : "(" + InScope + ")";
        }

        public async Task<IReadOnlyList<T>> QueryAsync<T>(
            AnalyticsStatement statement,
            Func<IDataRecord, T> map,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(statement);
            ArgumentNullException.ThrowIfNull(map);
            ObjectDisposedException.ThrowIf(_disposed, this);

            try
            {
                var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);

                await using var command = connection.CreateCommand();
                command.CommandText = statement.Sql.Replace(AnalyticsStatement.ScopePlaceholder, _predicate, StringComparison.Ordinal);
                command.AddParameter(AnalyticsStatement.ScopeLegalEntitiesParameter, _scope.LegalEntityIds.ToArray());
                foreach (var (name, value) in statement.Parameters)
                {
                    command.AddParameter(name, value);
                }

                var rows = new List<T>();
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    rows.Add(map(reader));
                }

                return rows;
            }
            catch (ClickHouseServerException ex) when (ex.ErrorCode == TimeoutExceededErrorCode)
            {
                // ClickHouse enforced max_execution_time (the normal path).
                throw Timeout(ex);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Not the caller's token: the HttpClient backstop timeout fired.
                throw Timeout(ex);
            }
        }

        private TimeoutException Timeout(Exception inner) => new(
            $"The analytic query exceeded its time limit of {_connectionFactory.QueryTimeout} " +
            $"('{NexusAnalyticsOptions.SectionName}:{nameof(NexusAnalyticsOptions.QueryTimeout)}').",
            inner);

        public async ValueTask DisposeAsync()
        {
            _disposed = true;
            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
                _connection = null;
            }
        }

        private async Task<ClickHouseConnection> GetConnectionAsync(CancellationToken cancellationToken)
        {
            if (_connection is null)
            {
                var connection = _connectionFactory.Create();
                try
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                _connection = connection;
            }

            return _connection;
        }
    }
}
