using Shouldly;

namespace Nexus.BuildingBlocks.Testing;

/// <summary>Assertion helpers for RLS-filtered query results (readability sugar over Shouldly).</summary>
public static class RlsAssertions
{
    /// <summary>
    /// Asserts that none of <paramref name="rows"/> carries one of <paramref name="forbiddenIds"/>
    /// -- the row must not come back from the query at all, not merely be "marked" inaccessible.
    /// Fails with a message naming exactly which forbidden ids leaked through.
    /// </summary>
    public static void AssertInvisible<T>(IEnumerable<T> rows, Func<T, Guid> idSelector, params Guid[] forbiddenIds)
    {
        var visibleIds = rows.Select(idSelector).ToList();
        var leaked = forbiddenIds.Where(visibleIds.Contains).ToList();
        leaked.ShouldBeEmpty(
            $"Expected none of [{string.Join(", ", forbiddenIds)}] to be visible, but found: [{string.Join(", ", leaked)}].");
    }
}
