using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Nexus.BuildingBlocks.Application;
using Nexus.BuildingBlocks.Persistence;
using Nexus.BuildingBlocks.Web.Correlation;
using Shouldly;

namespace Nexus.BuildingBlocks.Web.Tests;

public sealed class CorrelationMiddlewareTests
{
    private const string Header = CorrelationMiddleware.HeaderName;

    [Fact]
    public async Task HeaderPresent_IsReusedInContextRequestResponseAndLogScope()
    {
        var incoming = Guid.CreateVersion7();
        var context = new DefaultHttpContext();
        context.Request.Headers[Header] = incoming.ToString("D", CultureInfo.InvariantCulture);
        var run = new Run();

        await run.InvokeAsync(context);

        run.Correlation.CorrelationId.ShouldBe(incoming);
        run.Correlation.CausationId.ShouldBeNull();
        run.IdGenerator.Calls.ShouldBe(0);
        run.SeenInNext.ShouldBe(incoming);
        context.Request.Headers[Header].ToString().ShouldBe(incoming.ToString("D", CultureInfo.InvariantCulture));
        context.Response.Headers[Header].ToString().ShouldBe(incoming.ToString("D", CultureInfo.InvariantCulture));
        run.Logger.ScopeValue.ShouldBe(incoming.ToString("D", CultureInfo.InvariantCulture));
        run.Logger.ScopeOpenDuringNext.ShouldBeTrue();
    }

    [Fact]
    public async Task HeaderAbsent_NewIdIsGeneratedAndAddedToResponse()
    {
        var context = new DefaultHttpContext();
        var run = new Run();

        await run.InvokeAsync(context);

        run.IdGenerator.Calls.ShouldBe(1);
        run.Correlation.CorrelationId.ShouldBe(run.IdGenerator.Last);
        run.Correlation.CorrelationId.Version.ShouldBe(7);
        context.Response.Headers[Header].ToString().ShouldBe(run.IdGenerator.Last.ToString("D", CultureInfo.InvariantCulture));
        context.Request.Headers[Header].ToString().ShouldBe(run.IdGenerator.Last.ToString("D", CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("abc\r\nInjected: header")]
    public async Task HeaderInvalid_IsDiscardedAndReplaced(string value)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[Header] = value;
        var run = new Run();

        await run.InvokeAsync(context);

        run.IdGenerator.Calls.ShouldBe(1);
        run.Correlation.CorrelationId.ShouldBe(run.IdGenerator.Last);
        context.Response.Headers[Header].ToString().ShouldBe(run.IdGenerator.Last.ToString("D", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task HeaderRepeated_IsAmbiguous_AndReplaced()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[Header] = new([Guid.CreateVersion7().ToString(), Guid.CreateVersion7().ToString()]);
        var run = new Run();

        await run.InvokeAsync(context);

        run.IdGenerator.Calls.ShouldBe(1);
        run.Correlation.CorrelationId.ShouldBe(run.IdGenerator.Last);
    }

    private sealed class Run
    {
        public CorrelationContext Correlation { get; } = new();

        public CountingIdGenerator IdGenerator { get; } = new();

        public ScopeCapturingLogger Logger { get; } = new();

        public Guid? SeenInNext { get; private set; }

        public Task InvokeAsync(HttpContext context)
        {
            var middleware = new CorrelationMiddleware(_ =>
            {
                SeenInNext = Correlation.CorrelationId;
                Logger.ScopeOpenDuringNext = Logger.ScopeOpen;
                return Task.CompletedTask;
            });
            return middleware.InvokeAsync(context, Correlation, IdGenerator, Logger);
        }
    }

    private sealed class CountingIdGenerator : IIdGenerator
    {
        public int Calls { get; private set; }

        public Guid Last { get; private set; }

        public Guid NewId()
        {
            Calls++;
            Last = Guid.CreateVersion7(TimeProvider.System.GetUtcNow());
            return Last;
        }
    }

    private sealed class ScopeCapturingLogger : ILogger<CorrelationMiddleware>
    {
        public object? ScopeValue { get; private set; }

        public bool ScopeOpen { get; private set; }

        public bool ScopeOpenDuringNext { get; set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            if (state is IEnumerable<KeyValuePair<string, object>> pairs)
            {
                ScopeValue = pairs.Single(p => p.Key == CorrelationMiddleware.LogScopeKey).Value;
            }

            ScopeOpen = true;
            return new Scope(this);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class Scope(ScopeCapturingLogger owner) : IDisposable
        {
            public void Dispose() => owner.ScopeOpen = false;
        }
    }
}
