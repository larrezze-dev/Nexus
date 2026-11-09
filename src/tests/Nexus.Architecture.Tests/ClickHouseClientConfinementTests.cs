using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace Nexus.Architecture.Tests;

/// <summary>
/// Enforces ficha 0.8 track S item 5 / conventions-api.md §7: ClickHouse.Client is only
/// referenced from Nexus.BuildingBlocks.Analytics and Nexus.Projections (production code).
/// Test projects (*.Tests and Nexus.BuildingBlocks.Testing) are exempt. Services consume
/// ClickHouse through Analytics' API, so a ProjectReference to Analytics is allowed; a direct
/// PackageReference or a source-level usage of the client in any other project is not.
/// Same approach as DomainProjectDependencyTests: parse files, no extra packages.
/// </summary>
public sealed class ClickHouseClientConfinementTests
{
    private const string PackageName = "ClickHouse.Client";

    private static readonly string[] AllowedProjectNames =
    [
        "Nexus.BuildingBlocks.Analytics",
        "Nexus.Projections",
    ];

    private static readonly Regex UsageRegex = new(
        @"^\s*(global\s+)?using\s+(static\s+)?ClickHouse\b|\bClickHouse\.Client\.",
        RegexOptions.Multiline | RegexOptions.Compiled);

    [Fact(DisplayName = "ARCH 0.8-S5: ClickHouse.Client only in BuildingBlocks.Analytics and Nexus.Projections")]
    public void ClickHouseClient_IsConfinedToAnalyticsAndProjections()
    {
        var all = FindProjectFiles();
        all.Count.ShouldBeGreaterThan(5);

        var names = all.Select(p => Path.GetFileNameWithoutExtension(p)).ToHashSet(StringComparer.Ordinal);
        foreach (var allowed in AllowedProjectNames)
        {
            names.ShouldContain(allowed);
        }

        var violations = new List<string>();

        foreach (var project in all.Where(p => !IsExempt(p)))
        {
            if (HasDirectPackageReference(project))
            {
                violations.Add($"{Path.GetFileName(project)} has a direct PackageReference to {PackageName}");
            }

            foreach (var source in EnumerateSources(Path.GetDirectoryName(project)!))
            {
                if (HasClickHouseUsage(File.ReadAllText(source)))
                {
                    violations.Add($"{Path.GetRelativePath(FindSrcDirectory(), source)} uses the ClickHouse client");
                }
            }
        }

        violations.ShouldBeEmpty();
    }

    [Fact(DisplayName = "ARCH 0.8-S5: detectors fire on offending csproj and source")]
    public void Detectors_FireOnOffendingInputs()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexus-arch-s5-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var bad = Path.Combine(dir, "Bad.csproj");
            File.WriteAllText(bad, "<Project><ItemGroup><PackageReference Include=\"ClickHouse.Client\" /></ItemGroup></Project>");
            HasDirectPackageReference(bad).ShouldBeTrue();

            var good = Path.Combine(dir, "Good.csproj");
            File.WriteAllText(good, "<Project><ItemGroup><PackageReference Include=\"Shouldly\" /></ItemGroup></Project>");
            HasDirectPackageReference(good).ShouldBeFalse();

            HasClickHouseUsage("using ClickHouse.Client.ADO;\nclass A {}").ShouldBeTrue();
            HasClickHouseUsage("global using ClickHouse.Client;").ShouldBeTrue();
            HasClickHouseUsage("var c = new ClickHouse.Client.ADO.ClickHouseConnection(\"\");").ShouldBeTrue();
            HasClickHouseUsage("using System;\nclass A { string s = \"x\"; }").ShouldBeFalse();

            IsExempt("/x/Nexus.Foo.Tests/Nexus.Foo.Tests.csproj").ShouldBeTrue();
            IsExempt("/x/Nexus.BuildingBlocks.Testing/Nexus.BuildingBlocks.Testing.csproj").ShouldBeTrue();
            IsExempt("/x/Nexus.Projections/Nexus.Projections.csproj").ShouldBeTrue();
            IsExempt("/x/Nexus.Foo/Nexus.Foo.csproj").ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static bool IsExempt(string projectFile)
    {
        var name = Path.GetFileNameWithoutExtension(projectFile);
        return name.EndsWith(".Tests", StringComparison.Ordinal)
               || name == "Nexus.BuildingBlocks.Testing"
               || AllowedProjectNames.Contains(name);
    }

    private static bool HasDirectPackageReference(string projectFile) =>
        XDocument.Load(projectFile)
            .Descendants("PackageReference")
            .Any(e => string.Equals(e.Attribute("Include")?.Value, PackageName, StringComparison.OrdinalIgnoreCase));

    private static bool HasClickHouseUsage(string source) => UsageRegex.IsMatch(source);

    private static IEnumerable<string> EnumerateSources(string projectDirectory) =>
        Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
            {
                var relative = Path.GetRelativePath(projectDirectory, path)
                    .Split(Path.DirectorySeparatorChar);
                return !relative.Contains("bin") && !relative.Contains("obj");
            });

    private static List<string> FindProjectFiles() =>
        Directory.EnumerateFiles(FindSrcDirectory(), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Contains("obj"))
            .ToList();

    private static string FindSrcDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Nexus.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate src/Nexus.sln walking up from the test output directory.");
    }
}
