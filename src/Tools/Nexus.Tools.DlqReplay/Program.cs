using System.Globalization;
using Nexus.BuildingBlocks.Messaging.Dlq;

// Usage: Nexus.Tools.DlqReplay <topic>.DLQ [--yes] [--bootstrap-servers host:port]
// Bootstrap servers default to NEXUS__KAFKA__BOOTSTRAPSERVERS, then localhost:9092 (compose).
var positional = new List<string>();
var assumeYes = false;
var bootstrap = Environment.GetEnvironmentVariable("NEXUS__KAFKA__BOOTSTRAPSERVERS") ?? "localhost:9092";
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--yes" or "-y":
            assumeYes = true;
            break;
        case "--bootstrap-servers" when i + 1 < args.Length:
            bootstrap = args[++i];
            break;
        default:
            positional.Add(args[i]);
            break;
    }
}

if (positional.Count != 1)
{
    await Console.Error.WriteLineAsync("usage: dlq-replay <topic>.DLQ [--yes] [--bootstrap-servers host:port]");
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

try
{
    var replayer = new DlqReplayer(bootstrap);
    var plan = replayer.Plan(positional[0]);
    if (plan.Pending == 0)
    {
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{plan.DlqTopic}: nothing to replay."));
        return 0;
    }

    Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
        $"{plan.Pending} message(s) of {plan.DlqTopic} will be re-injected into {plan.TargetTopic} ({bootstrap})."));
    if (!assumeYes)
    {
        Console.Write("Continue? [y/N] ");
        var answer = Console.ReadLine()?.Trim();
        if (!string.Equals(answer, "y", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(answer, "yes", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Aborted, nothing re-injected.");
            return 1;
        }
    }

    var replayed = await replayer.ReplayAsync(plan.DlqTopic, cts.Token);
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Re-injected {replayed} message(s) into {plan.TargetTopic}."));
    return 0;
}
catch (OperationCanceledException)
{
    await Console.Error.WriteLineAsync("Interrupted; already re-injected messages are recorded, the rest stay pending.");
    return 130;
}
catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Confluent.Kafka.KafkaException)
{
    await Console.Error.WriteLineAsync($"ERROR: {ex.Message}");
    return 1;
}
