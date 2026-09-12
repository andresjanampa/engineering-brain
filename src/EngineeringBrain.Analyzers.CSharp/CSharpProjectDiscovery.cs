using System.Xml;
using System.Xml.Linq;
using EngineeringBrain.Core;

namespace EngineeringBrain.Analyzers.CSharp;

internal static class CSharpProjectDiscovery
{
    public static ProjectDiscoveryResult Discover(LanguageAnalysisRequest request)
    {
        var diagnostics = new List<AnalysisDiagnostic>();
        var projects = new List<DiscoveredProject>();

        foreach (var file in request.Files.Where(file => file.Extension == ".csproj"))
        {
            var fullPath = ToFullPath(request.RepositoryRoot, file.RelativePath);
            var name = Path.GetFileNameWithoutExtension(file.RelativePath);
            var targetFrameworks = Array.Empty<string>();
            var projectReferences = Array.Empty<DiscoveredProjectReference>();

            try
            {
                var document = XDocument.Load(fullPath, LoadOptions.SetLineInfo);
                name = ReadLiteralProperty(document, "AssemblyName") ?? name;
                targetFrameworks = ReadTargetFrameworks(document);
                projectReferences = ReadProjectReferences(document, request.RepositoryRoot, fullPath);
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or XmlException
                or ArgumentException
                or NotSupportedException)
            {
                diagnostics.Add(new AnalysisDiagnostic(
                    "CSHARP_PROJECT_METADATA_UNREADABLE",
                    AnalysisDiagnosticSeverity.Warning,
                    $"Project metadata could not be read: {SafeDiagnostic.Message(exception, request.RepositoryRoot)}",
                    file.RelativePath));
            }

            projects.Add(new DiscoveredProject(
                StableEntityId.Create("C#", CodeEntityType.Project, file.RelativePath, name),
                name,
                file.RelativePath,
                fullPath,
                targetFrameworks,
                projectReferences));
        }

        var solutions = request.Files
            .Where(file => file.Extension == ".sln")
            .Select(file => new DiscoveredSolution(
                StableEntityId.Create(
                    "C#",
                    CodeEntityType.Solution,
                    file.RelativePath,
                    Path.GetFileNameWithoutExtension(file.RelativePath)),
                Path.GetFileNameWithoutExtension(file.RelativePath),
                file.RelativePath,
                ToFullPath(request.RepositoryRoot, file.RelativePath)))
            .OrderBy(solution => solution.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProjectDiscoveryResult(
            projects.OrderBy(project => project.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            solutions,
            diagnostics);
    }

    public static string ToRelativePath(string repositoryRoot, string fullPath) =>
        Path.GetRelativePath(repositoryRoot, fullPath).Replace('\\', '/');

    public static string ToFullPath(string repositoryRoot, string relativePath) =>
        Path.GetFullPath(Path.Combine(
            repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string? ReadLiteralProperty(XContainer document, string propertyName)
    {
        var value = document.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == propertyName)
            ?.Value.Trim();

        return string.IsNullOrWhiteSpace(value) || value.Contains("$(", StringComparison.Ordinal)
            ? null
            : value;
    }

    private static string[] ReadTargetFrameworks(XContainer document)
    {
        var value = ReadLiteralProperty(document, "TargetFrameworks")
            ?? ReadLiteralProperty(document, "TargetFramework");

        return value is null
            ? []
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private static DiscoveredProjectReference[] ReadProjectReferences(
        XContainer document,
        string repositoryRoot,
        string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)!;

        return document.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => new
            {
                Include = element.Attribute("Include")?.Value,
                Line = element is IXmlLineInfo lineInfo && lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Include)
                && !item.Include.Contains("$(", StringComparison.Ordinal))
            .Select(item => new
            {
                FullPath = Path.GetFullPath(Path.Combine(projectDirectory, item.Include!)),
                item.Line
            })
            .Where(item => IsUnderRoot(repositoryRoot, item.FullPath))
            .Select(item => new DiscoveredProjectReference(
                ToRelativePath(repositoryRoot, item.FullPath),
                item.Line))
            .GroupBy(reference => reference.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(reference => reference.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsUnderRoot(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }
}

internal sealed record DiscoveredProject(
    string Id,
    string Name,
    string RelativePath,
    string FullPath,
    IReadOnlyList<string> TargetFrameworks,
    IReadOnlyList<DiscoveredProjectReference> ProjectReferences);

internal sealed record DiscoveredProjectReference(string RelativePath, int Line);

internal sealed record DiscoveredSolution(
    string Id,
    string Name,
    string RelativePath,
    string FullPath);

internal sealed record ProjectDiscoveryResult(
    IReadOnlyList<DiscoveredProject> Projects,
    IReadOnlyList<DiscoveredSolution> Solutions,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics);
