using System.Text.RegularExpressions;
using Nexus.Projections.Abstractions;

namespace Nexus.Projections.Runtime;

/// <summary>
/// The projections registered in DI (<c>IEnumerable&lt;IProjection&gt;</c>), validated once: unique
/// names, safe identifiers. Resolving it instantiates every projection, so a broken definition (e.g. a
/// malformed <c>topics.yaml</c>) fails at startup, before any consumer starts.
/// </summary>
public sealed partial class ProjectionCatalog
{
    /// <summary>Consumer group prefix (conventions-events.md §8: <c>projections.&lt;proyeccion&gt;</c>).</summary>
    public const string GroupPrefix = "projections.";

    private readonly Dictionary<string, IProjection> _byName;

    public ProjectionCatalog(IEnumerable<IProjection> projections)
    {
        ArgumentNullException.ThrowIfNull(projections);
        All = projections.ToList();
        _byName = new Dictionary<string, IProjection>(StringComparer.Ordinal);
        foreach (var projection in All)
        {
            Validate(projection);
            if (!_byName.TryAdd(projection.Name, projection))
            {
                throw new InvalidOperationException($"Two projections are named '{projection.Name}' (they would share consumer group {GroupId(projection)}).");
            }
        }
    }

    public IReadOnlyList<IProjection> All { get; }

    public IEnumerable<string> Names => _byName.Keys.Order(StringComparer.Ordinal);

    public IProjection? Find(string name) => _byName.GetValueOrDefault(name);

    public IProjection? FindByGroupId(string groupId) =>
        groupId.StartsWith(GroupPrefix, StringComparison.Ordinal) ? Find(groupId[GroupPrefix.Length..]) : null;

    public static string GroupId(IProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return GroupPrefix + projection.Name;
    }

    /// <summary>A short label of the subscription, shown in <c>/health</c> next to the group.</summary>
    public static string SubscriptionLabel(IProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return projection.Subscription.Pattern ?? string.Join(",", projection.Subscription.Topics ?? []);
    }

    private static void Validate(IProjection projection)
    {
        if (!NamePattern().IsMatch(projection.Name))
            throw new InvalidOperationException($"Projection name '{projection.Name}' must match {NamePattern()}.");
        // The table name is interpolated in TRUNCATE TABLE (rebuild) and in the bulk-copy INSERT: only a
        // plain qualified identifier is accepted. It comes from code, never from a request.
        if (!TableNamePattern().IsMatch(projection.TableName))
            throw new InvalidOperationException($"Projection '{projection.Name}': table name '{projection.TableName}' must be <database>.<table> with [A-Za-z0-9_] identifiers.");
        if (projection.ColumnNames.Count == 0 || projection.ColumnNames.Any(c => !ColumnPattern().IsMatch(c)))
            throw new InvalidOperationException($"Projection '{projection.Name}': column names must be non-empty [A-Za-z0-9_] identifiers.");
        if (projection.Subscription is null || (projection.Subscription.Pattern is null && projection.Subscription.Topics is null))
            throw new InvalidOperationException($"Projection '{projection.Name}' has no subscription.");
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*\\.[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex TableNamePattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ColumnPattern();
}

/// <summary>
/// Which topics a <see cref="ProjectionSubscription"/> selects, evaluated in .NET (rebuild snapshot, lag
/// probe). A regex subscription is consumed by librdkafka (POSIX extended regex); here the same pattern
/// runs on .NET's engine. Both agree on the subset NEXUS patterns use (anchors <c>^ $</c>, literals,
/// <c>\.</c>, bracket classes like <c>[a-z0-9-]</c>, <c>. * + ?</c>, alternation); do not use
/// <c>\d</c>, lookarounds, back-references or lazy quantifiers in a projection pattern.
/// <para>
/// <c>*.DLQ</c> topics are never projected, whatever the pattern says (the step 0.8 ficha: "excepto
/// <c>.DLQ</c>"): the rebuild never reads them and the consumer skips (and commits) any record from one.
/// </para>
/// </summary>
public sealed class ProjectionTopicMatcher
{
    public const string DlqSuffix = ".DLQ";

    private readonly Regex? _pattern;
    private readonly HashSet<string>? _topics;

    public ProjectionTopicMatcher(ProjectionSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (subscription.Pattern is { } pattern)
        {
            _pattern = new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));
        }
        else
        {
            _topics = new HashSet<string>(subscription.Topics ?? [], StringComparer.Ordinal);
        }
    }

    public static bool IsDlq(string topic) => topic.EndsWith(DlqSuffix, StringComparison.Ordinal);

    public bool Matches(string topic)
    {
        ArgumentNullException.ThrowIfNull(topic);
        if (IsDlq(topic))
        {
            return false;
        }

        return _pattern is not null ? _pattern.IsMatch(topic) : _topics!.Contains(topic);
    }
}
