using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace Nexus.Architecture.Tests;

/// <summary>
/// Enforces the dependency rule from conventions-backend.md §2: "Domain no depende de nada
/// salvo BuildingBlocks.Domain. Sin EF, sin Kafka, sin ASP.NET."
///
/// Why parsing .csproj files instead of reflecting over the built assemblies (NetArchTest or
/// similar): at this stage the *.Domain projects are still empty class libraries with no
/// dependency graph to inspect at runtime - a loaded-assembly check would have nothing to
/// assert against today and would only start working once real code exists. Parsing the
/// .csproj PackageReference items is equivalent in intent (it fails the instant someone adds
/// a forbidden package, which is exactly when this rule should fire) and needs no new
/// dependency: NetArchTest.Rules is not in Directory.Packages.props, and adding it would
/// require an ADR (CLAUDE.md §3 "Licencias" / §2 "paquete fuera de las listas aprobadas").
/// </summary>
public sealed class DomainProjectDependencyTests
{
    private static readonly string[] ForbiddenPackagePrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Confluent.Kafka",
    ];

    [Fact]
    public void DomainProjects_DoNotReferenceInfrastructureFrameworks()
    {
        var domainProjectFiles = FindDomainProjectFiles();

        // Sanity check: if this ever finds zero *.Domain projects, the discovery logic is
        // broken (there is always at least Nexus.BuildingBlocks.Domain in the solution).
        domainProjectFiles.ShouldNotBeEmpty();

        var violations = new List<string>();

        foreach (var domainProjectFile in domainProjectFiles)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var package in CollectPackageReferencesTransitively(domainProjectFile, visited))
            {
                if (ForbiddenPackagePrefixes.Any(prefix => package.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    violations.Add($"{Path.GetFileName(domainProjectFile)} references forbidden package '{package}'");
                }
            }
        }

        violations.ShouldBeEmpty();
    }

    /// <summary>
    /// Collects every PackageReference of <paramref name="projectFile"/> plus, recursively,
    /// every PackageReference reachable through its ProjectReference items - this is what
    /// "directa ni transitiva" means for a .csproj-based check (NuGet transitivity for a
    /// package's own dependencies is not relevant here: the forbidden names are top-level
    /// framework packages that any offending project would reference directly).
    /// </summary>
    private static IEnumerable<string> CollectPackageReferencesTransitively(string projectFile, HashSet<string> visited)
    {
        if (!visited.Add(projectFile))
        {
            yield break;
        }

        var document = XDocument.Load(projectFile);
        var projectDirectory = Path.GetDirectoryName(projectFile)!;

        foreach (var package in document
                     .Descendants("PackageReference")
                     .Select(element => element.Attribute("Include")?.Value)
                     .Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            yield return package!;
        }

        var projectReferencePaths = document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include!.Replace('\\', Path.DirectorySeparatorChar))));

        foreach (var referencedProject in projectReferencePaths)
        {
            foreach (var package in CollectPackageReferencesTransitively(referencedProject, visited))
            {
                yield return package;
            }
        }
    }

    private static List<string> FindDomainProjectFiles()
    {
        var srcDirectory = FindSrcDirectory();

        return Directory
            .EnumerateFiles(srcDirectory, "*.csproj", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(Path.GetDirectoryName(path)!).EndsWith(".Domain", StringComparison.Ordinal))
            .ToList();
    }

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
