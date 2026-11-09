using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Nexus.Projections.Migrations;

/// <summary>
/// One <c>db/clickhouse/V&lt;yyyyMMddHHmm&gt;__&lt;description&gt;.sql</c> file (db/clickhouse/README.md): its
/// version, description, SHA-256 checksum (lowercase hex of the file BYTES, exactly as shipped) and its
/// statements.
/// </summary>
internal sealed partial class ClickHouseMigrationScript
{
    private ClickHouseMigrationScript(string path, string version, string description, string checksum, IReadOnlyList<string> statements)
    {
        Path = path;
        Version = version;
        Description = description;
        Checksum = checksum;
        Statements = statements;
        Name = string.Create(CultureInfo.InvariantCulture, $"V{version}__{description}");
    }

    /// <summary>Display name, e.g. <c>V202611082100__base</c>.</summary>
    public string Name { get; }

    public string Path { get; }

    /// <summary>The digits after <c>V</c>, e.g. <c>202611082100</c>. Application order = ordinal order of this string.</summary>
    public string Version { get; }

    /// <summary>The part after <c>__</c>, underscores turned into spaces (as Flyway does), e.g. <c>base</c>.</summary>
    public string Description { get; }

    /// <summary>Lowercase hex SHA-256 of the file bytes.</summary>
    public string Checksum { get; }

    /// <summary>The statements to execute, in file order, without the empty or comment-only fragments.</summary>
    public IReadOnlyList<string> Statements { get; }

    /// <summary>Reads every <c>V*.sql</c> file of <paramref name="directory"/>, ordered by version.</summary>
    /// <exception cref="ClickHouseMigrationException">The directory is missing, a file name is malformed, or two files share a version.</exception>
    public static IReadOnlyList<ClickHouseMigrationScript> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new ClickHouseMigrationException($"ClickHouse migrations directory '{directory}' does not exist.");
        }

        var scripts = Directory.EnumerateFiles(directory, "V*.sql", SearchOption.TopDirectoryOnly)
            .Select(Load)
            .OrderBy(s => s.Version, StringComparer.Ordinal)
            .ToList();

        var duplicate = scripts.GroupBy(s => s.Version, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ClickHouseMigrationException(
                $"Two ClickHouse migrations share version {duplicate.Key}: {string.Join(", ", duplicate.Select(s => System.IO.Path.GetFileName(s.Path)))}.");
        }

        return scripts;
    }

    public static ClickHouseMigrationScript Load(string path)
    {
        var fileName = System.IO.Path.GetFileName(path);
        var match = FileNamePattern().Match(fileName);
        if (!match.Success)
        {
            throw new ClickHouseMigrationException(
                $"ClickHouse migration file name '{fileName}' does not match V<yyyyMMddHHmm>__<description>.sql.");
        }

        var bytes = File.ReadAllBytes(path);
        var checksum = Convert.ToHexStringLower(SHA256.HashData(bytes));
        // StreamReader semantics: honours a BOM if present, UTF-8 otherwise.
        using var reader = new StreamReader(new MemoryStream(bytes), System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        return new ClickHouseMigrationScript(
            path,
            match.Groups["version"].Value,
            match.Groups["description"].Value.Replace('_', ' '),
            checksum,
            SplitStatements(text));
    }

    /// <summary>
    /// Splits on <c>;</c> (README: no semicolon other than the statement terminators) and drops every
    /// fragment that is empty or holds only <c>--</c> comment lines and whitespace: ClickHouse answers
    /// 400 "Empty query" to those. A kept fragment is sent as-is (leading comments included: ClickHouse
    /// ignores them).
    /// </summary>
    internal static IReadOnlyList<string> SplitStatements(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Split(';')
            .Where(fragment => !IsEmptyOrCommentOnly(fragment))
            .Select(fragment => fragment.Trim())
            .ToList();
    }

    private static bool IsEmptyOrCommentOnly(string fragment)
    {
        foreach (var rawLine in fragment.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    public override string ToString() => Name;

    [GeneratedRegex(@"^V(?<version>[0-9]+)__(?<description>[A-Za-z0-9_]+)\.sql$", RegexOptions.CultureInvariant)]
    private static partial Regex FileNamePattern();
}

/// <summary>The ClickHouse schema cannot be brought up to date: <c>nexus-projections</c> must not start.</summary>
public sealed class ClickHouseMigrationException : Exception
{
    public ClickHouseMigrationException()
    {
    }

    public ClickHouseMigrationException(string message)
        : base(message)
    {
    }

    public ClickHouseMigrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
