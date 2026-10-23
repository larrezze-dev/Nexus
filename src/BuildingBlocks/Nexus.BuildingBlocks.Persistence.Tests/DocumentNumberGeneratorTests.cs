using Nexus.BuildingBlocks.Persistence.Numbering;
using Shouldly;

namespace Nexus.BuildingBlocks.Persistence.Tests;

public sealed class DocumentNumberGeneratorTests(PersistenceDatabaseFixture db) : IAsyncLifetime
{
    private static readonly DocumentNumberingOptions Options = new() { Schema = "uow_test" };

    private readonly ServiceDataSource _dataSource = db.CreateServiceDataSource();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    // (f) DB-R5: 50 concurrent units of work on one scope key get 1..50, no gaps, no repeats.
    [Fact]
    public async Task ConcurrentCalls_OnSameScope_AreGapFreeAndUnique_DB_R5()
    {
        var scope = $"TST:LE-CH:{Guid.NewGuid():N}";

        var numbers = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
        {
            await using var uow = db.CreateUnitOfWork(_dataSource, Guid.CreateVersion7(), Guid.CreateVersion7());
            await uow.BeginAsync(Ct);
            var number = await new DocumentNumberGenerator(uow, Options).NextNumberAsync(scope, Ct);
            await uow.CommitAsync(Ct);
            return number;
        }, Ct)));

        numbers.Order().ShouldBe(Enumerable.Range(1, 50).Select(i => (long)i));
    }

    [Fact]
    public async Task RolledBackNumber_IsReissued_DB_R5()
    {
        var scope = $"TST:LE-CH:{Guid.NewGuid():N}";

        (await Next(scope, commit: true)).ShouldBe(1);
        (await Next(scope, commit: false)).ShouldBe(2);
        (await Next(scope, commit: true)).ShouldBe(2);
        (await Next("other-" + scope, commit: true)).ShouldBe(1);
    }

    [Fact]
    public async Task SeveralNumbersInOneUnitOfWork_AreConsecutive()
    {
        var scope = $"TST:LE-CH:{Guid.NewGuid():N}";
        await using var uow = db.CreateUnitOfWork(_dataSource, Guid.CreateVersion7(), Guid.CreateVersion7());
        await uow.BeginAsync(Ct);
        var generator = new DocumentNumberGenerator(uow, Options);
        (await generator.NextNumberAsync(scope, Ct)).ShouldBe(1);
        (await generator.NextNumberAsync(scope, Ct)).ShouldBe(2);
        (await generator.NextNumberAsync(scope, Ct)).ShouldBe(3);
        await uow.CommitAsync(Ct);
    }

    [Fact]
    public async Task OutsideAnActiveUnitOfWork_Throws()
    {
        await using var uow = db.CreateUnitOfWork(_dataSource, Guid.CreateVersion7(), Guid.CreateVersion7());
        await Should.ThrowAsync<InvalidOperationException>(
            () => new DocumentNumberGenerator(uow, Options).NextNumberAsync("X", Ct));
    }

    [Theory]
    [InlineData("uow_test\"; DROP TABLE x; --")]
    [InlineData("Upper")]
    [InlineData("a.b")]
    [InlineData("")]
    public void InvalidSchemaName_IsRejected(string schema)
    {
        var uow = db.CreateUnitOfWork(_dataSource, Guid.CreateVersion7(), Guid.CreateVersion7());
        Should.Throw<ArgumentException>(() => new DocumentNumberGenerator(uow, new DocumentNumberingOptions { Schema = schema }));
    }

    private async Task<long> Next(string scope, bool commit)
    {
        await using var uow = db.CreateUnitOfWork(_dataSource, Guid.CreateVersion7(), Guid.CreateVersion7());
        await uow.BeginAsync(Ct);
        var number = await new DocumentNumberGenerator(uow, Options).NextNumberAsync(scope, Ct);
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
}
