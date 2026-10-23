using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Persistence.Numbering;
using Nexus.BuildingBlocks.Testing;
using Nexus.BuildingBlocks.Tests.Support;
using Shouldly;

namespace Nexus.BuildingBlocks.Tests;

/// <summary>
/// DB-R5 (conventions-database.md §11): document numbers have no gaps; the counter moves in the
/// document's own transaction and a rollback returns the number. Runs on
/// <c>test_runtime.number_sequence</c>, created at run time with the exact §11 DDL (see
/// <see cref="RuntimeTestSchema"/>). Every test uses its own fresh scope key.
/// </summary>
public sealed class DocumentNumberGeneratorTests(PostgresFixture db) : IAsyncLifetime
{
    private const int ConcurrentCallers = 50;

    private static readonly DocumentNumberingOptions Options = new() { Schema = RuntimeTestSchema.Schema };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceDataSource DataSource => db.GetServiceDataSource(RuntimeTestSchema.OwningServiceRole);

    public async ValueTask InitializeAsync() => await RuntimeTestSchema.EnsureCreatedAsync(db, Ct);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact(DisplayName = "DB-R5: 50 concurrent callers with the same scope_key get 1..50, no gaps, no duplicates")]
    public async Task ConcurrentCallers_SameScope_GetGapFreeUniqueNumbers()
    {
        var scopeKey = NewScopeKey();

        // Every caller first opens its own unit of work (own connection and transaction), then
        // all of them request a number at the same moment: maximal contention on the one
        // counter row, which they must serialize on.
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;

        async Task<long> CallerAsync()
        {
            await using var uow = UnitOfWorks.Create(DataSource, Guid.CreateVersion7());
            await uow.BeginAsync(Ct);
            if (Interlocked.Increment(ref started) == ConcurrentCallers)
            {
                allStarted.SetResult();
            }

            // Bounded: if any caller failed to begin, fail fast instead of waiting forever.
            await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(60), Ct);
            var number = await new DocumentNumberGenerator(uow, Options).NextNumberAsync(scopeKey, Ct);
            await uow.CommitAsync(Ct);
            return number;
        }

        var numbers = await Task.WhenAll(Enumerable.Range(0, ConcurrentCallers).Select(_ => Task.Run(CallerAsync, Ct)));

        numbers.Order().ShouldBe(Enumerable.Range(1, ConcurrentCallers).Select(n => (long)n));
        (await NextValueAsync(scopeKey)).ShouldBe(ConcurrentCallers + 1);
    }

    [Fact(DisplayName = "DB-R5: a rolled-back document returns its number; the next document reuses it (no gap)")]
    public async Task Rollback_ReturnsTheNumber()
    {
        var scopeKey = NewScopeKey();

        (await TakeNumberAsync(scopeKey, commit: true)).ShouldBe(1);
        (await TakeNumberAsync(scopeKey, commit: false)).ShouldBe(2);
        (await TakeNumberAsync(scopeKey, commit: true)).ShouldBe(2);
        (await TakeNumberAsync(scopeKey, commit: true)).ShouldBe(3);
        (await NextValueAsync(scopeKey)).ShouldBe(4);
    }

    [Fact(DisplayName = "DB-R5: scope keys number independently")]
    public async Task DifferentScopes_AreIndependent()
    {
        var scopeA = NewScopeKey();
        var scopeB = NewScopeKey();

        (await TakeNumberAsync(scopeA, commit: true)).ShouldBe(1);
        (await TakeNumberAsync(scopeA, commit: true)).ShouldBe(2);
        (await TakeNumberAsync(scopeB, commit: true)).ShouldBe(1);
    }

    [Fact(DisplayName = "DB-R5: a number cannot be taken outside an active unit of work")]
    public async Task NextNumber_WithoutActiveUnitOfWork_Throws()
    {
        var scopeKey = NewScopeKey();
        await using var uow = UnitOfWorks.Create(DataSource, Guid.CreateVersion7());

        await Should.ThrowAsync<InvalidOperationException>(() =>
            new DocumentNumberGenerator(uow, Options).NextNumberAsync(scopeKey, Ct));

        (await NextValueAsync(scopeKey)).ShouldBeNull();
    }

    private async Task<long> TakeNumberAsync(string scopeKey, bool commit)
    {
        await using var uow = UnitOfWorks.Create(DataSource, Guid.CreateVersion7());
        await uow.BeginAsync(Ct);
        var number = await new DocumentNumberGenerator(uow, Options).NextNumberAsync(scopeKey, Ct);
        if (commit)
        {
            await uow.CommitAsync(Ct);
        }
        else
        {
            await uow.RollbackAsync(Ct);
        }

        return number;
    }

    private Task<long?> NextValueAsync(string scopeKey) =>
        db.ScalarAsync<long?>($"SELECT next_value FROM {RuntimeTestSchema.Schema}.number_sequence WHERE scope_key = $1", scopeKey);

    // '<DOC>:<le_code>:<yyyy>' shape (§11), unique per test so parallel tests and reruns never share a counter.
    private static string NewScopeKey() => $"TST:{Guid.CreateVersion7():N}:2026";
}
