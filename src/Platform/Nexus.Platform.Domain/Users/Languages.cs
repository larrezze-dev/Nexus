namespace Nexus.Platform.Domain.Users;

/// <summary>
/// UI languages of NEXUS (CLAUDE.md §3: de/en/fr/it/es), the exact set of the
/// <c>org.app_user.preferred_language</c> CHECK and of the OpenAPI enums.
/// </summary>
public static class Languages
{
    /// <summary>Reference language and column default.</summary>
    public const string Default = "de";

    public static IReadOnlySet<string> Supported { get; } =
        new HashSet<string>(["de", "en", "fr", "it", "es"], StringComparer.Ordinal);
}
