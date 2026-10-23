namespace Nexus.BuildingBlocks.Testing;

/// <summary>
/// Locates a path relative to the repository root from any test assembly's own output
/// directory, by walking up from <see cref="AppContext.BaseDirectory"/> until
/// <paramref name="relativePath"/> (passed to <see cref="Find"/>) exists. Every test project runs
/// from a different <c>bin/.../net10.0</c> folder, so this is the one thing that needs
/// repeating; everything built on top of it (locating <c>db/bootstrap</c>,
/// <c>db/migrations</c>, <c>db/test-migrations</c> for <see cref="PostgresFixture"/>, or
/// <c>contracts/events/_test</c> for a test project that resolves <see cref="TestEvent"/>'s
/// schema) does not.
/// </summary>
public static class RepoRoot
{
    /// <summary>
    /// Returns the absolute path of <paramref name="relativePath"/> resolved against the
    /// repository root. Throws <see cref="DirectoryNotFoundException"/> if no ancestor of
    /// <see cref="AppContext.BaseDirectory"/> contains it.
    /// </summary>
    public static string Find(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException($"'{relativePath}' not found above {AppContext.BaseDirectory}.");
    }
}
