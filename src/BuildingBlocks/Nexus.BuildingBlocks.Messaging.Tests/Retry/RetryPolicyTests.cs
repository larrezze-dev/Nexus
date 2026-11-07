using Nexus.BuildingBlocks.Messaging.Retry;
using Shouldly;

namespace Nexus.BuildingBlocks.Messaging.Tests.Retry;

/// <summary>TimeProvider whose timers fire immediately and record the requested due times.</summary>
internal sealed class ImmediateTimeProvider : TimeProvider
{
    public List<TimeSpan> Delays { get; } = [];

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Delays.Add(dueTime);
        var timer = new ImmediateTimer();
        ThreadPool.QueueUserWorkItem(_ => callback(state));
        return timer;
    }

    private sealed class ImmediateTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public class RetryPolicyTests
{
    [Fact(DisplayName = "conventions-events §7: the default is 1 attempt + 3 retries (MaxAttempts = 4)")]
    public void Default_is_one_attempt_plus_three_retries()
    {
        var options = new RetryOptions();
        options.MaxAttempts.ShouldBe(4);
        options.Delays.ShouldBe([TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)]);
    }

    [Fact]
    public async Task Succeeds_on_second_attempt_without_calling_onExhausted()
    {
        var time = new ImmediateTimeProvider();
        var policy = new RetryPolicy(time);
        var calls = 0;
        var exhausted = 0;

        await policy.ExecuteWithRetryAsync(
            _ => ++calls < 2 ? throw new InvalidOperationException("boom") : Task.CompletedTask,
            _ => { exhausted++; return Task.CompletedTask; },
            CancellationToken.None);

        calls.ShouldBe(2);
        exhausted.ShouldBe(0);
        time.Delays.ShouldBe([TimeSpan.FromMilliseconds(200)]);
    }

    [Fact(DisplayName = "conventions-events §7: failing 3 times and succeeding on the 4th attempt is NOT dead-lettered")]
    public async Task Fails_three_times_succeeds_on_fourth_without_calling_onExhausted()
    {
        var time = new ImmediateTimeProvider();
        var policy = new RetryPolicy(time);
        var calls = 0;
        var exhausted = 0;

        await policy.ExecuteWithRetryAsync(
            _ => ++calls <= 3 ? throw new InvalidOperationException($"fail {calls}") : Task.CompletedTask,
            _ => { exhausted++; return Task.CompletedTask; },
            CancellationToken.None);

        calls.ShouldBe(4);
        exhausted.ShouldBe(0);
        time.Delays.ShouldBe([TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)]);
    }

    [Fact(DisplayName = "conventions-events §7: failing all 4 attempts calls onExhausted once with the last exception, after 200 ms, 1 s and 5 s")]
    public async Task Fails_all_four_attempts_calls_onExhausted_once_with_last_exception()
    {
        var time = new ImmediateTimeProvider();
        var policy = new RetryPolicy(time);
        var calls = 0;
        var captured = new List<Exception>();

        await policy.ExecuteWithRetryAsync(
            _ => throw new InvalidOperationException($"fail {++calls}"),
            ex => { captured.Add(ex); return Task.CompletedTask; },
            CancellationToken.None);

        calls.ShouldBe(4);
        captured.Count.ShouldBe(1);
        captured[0].Message.ShouldBe("fail 4");
        time.Delays.ShouldBe([TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)]);
    }

    [Fact]
    public async Task MaxAttempts_is_configurable_and_reuses_the_last_delay()
    {
        var time = new ImmediateTimeProvider();
        var policy = new RetryPolicy(new RetryOptions { MaxAttempts = 6 }, time);
        var calls = 0;

        await policy.ExecuteWithRetryAsync(
            _ => throw new InvalidOperationException($"{++calls}"), _ => Task.CompletedTask, CancellationToken.None);

        calls.ShouldBe(6);
        time.Delays.ShouldBe([TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)]);
    }

    [Fact]
    public async Task Cancellation_propagates_and_is_not_dead_lettered()
    {
        using var cts = new CancellationTokenSource();
        var policy = new RetryPolicy(new ImmediateTimeProvider());
        var exhausted = 0;

        await Should.ThrowAsync<OperationCanceledException>(() => policy.ExecuteWithRetryAsync(
            ct => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            _ => { exhausted++; return Task.CompletedTask; },
            cts.Token));
        exhausted.ShouldBe(0);
    }

    [Fact(DisplayName = "RetryPolicy: with MaxAttempts = 1, a cancelled attempt propagates OperationCanceledException and is never dead-lettered")]
    public async Task Single_attempt_cancellation_propagates_and_never_calls_onExhausted()
    {
        using var cts = new CancellationTokenSource();
        var policy = new RetryPolicy(new RetryOptions { MaxAttempts = 1 }, new ImmediateTimeProvider());
        var calls = 0;
        var exhausted = 0;

        await Should.ThrowAsync<OperationCanceledException>(() => policy.ExecuteWithRetryAsync(
            ct => { calls++; cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            _ => { exhausted++; return Task.CompletedTask; },
            cts.Token));
        calls.ShouldBe(1);
        exhausted.ShouldBe(0);
    }
}
