using System.Xml;
using System.Xml.Linq;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed record ProjectDependencyNode(
    string Name,
    string RelativePath,
    IReadOnlyList<string> References);

public sealed class ProjectDependencyGraph
{
    private readonly StringComparer _comparer;
    private readonly IReadOnlyDictionary<string, ProjectDependencyNode> _projects;

    private ProjectDependencyGraph(
        StringComparer comparer,
        IReadOnlyDictionary<string, ProjectDependencyNode> projects)
    {
        _comparer = comparer;
        _projects = projects;
    }

    public IReadOnlyList<ProjectDependencyNode> Projects => _projects.Values
        .OrderBy(project => project.RelativePath, _comparer)
        .ToArray();

    public static ProjectDependencyGraph Create(
        string repositoryRoot,
        IReadOnlyList<ScannedFile> currentFiles,
        IReadOnlyList<ProjectInfo> previousProjects)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var previousByPath = previousProjects.ToDictionary(project => project.RelativePath, comparer);
        var projects = new Dictionary<string, ProjectDependencyNode>(comparer);

        foreach (var file in currentFiles.Where(file => file.Extension == ".csproj"))
        {
            var name = previousByPath.TryGetValue(file.RelativePath, out var previous)
                ? previous.Name
                : Path.GetFileNameWithoutExtension(file.RelativePath);
            var references = TryReadReferences(repositoryRoot, file.RelativePath)
                ?? previous?.ProjectReferences
                ?? [];
            projects[file.RelativePath] = new ProjectDependencyNode(
                name,
                file.RelativePath,
                references.Distinct(comparer).OrderBy(path => path, comparer).ToArray());
        }

        return new ProjectDependencyGraph(comparer, projects);
    }

    public string? FindOwner(string relativePath)
    {
        if (_projects.ContainsKey(relativePath))
        {
            return relativePath;
        }

        var normalized = NormalizePath(relativePath);
        return _projects.Values
            .Where(project => IsUnderProject(project.RelativePath, normalized))
            .OrderByDescending(project => GetProjectDirectory(project.RelativePath).Length)
            .Select(project => project.RelativePath)
            .FirstOrDefault();
    }

    public IReadOnlySet<string> GetTransitiveDependents(IEnumerable<string> projectPaths)
    {
        var affected = new HashSet<string>(projectPaths, _comparer);
        var pending = new Queue<string>(affected);
        while (pending.Count > 0)
        {
            var dependency = pending.Dequeue();
            foreach (var dependent in _projects.Values.Where(project =>
                         project.References.Contains(dependency, _comparer)))
            {
                if (affected.Add(dependent.RelativePath))
                {
                    pending.Enqueue(dependent.RelativePath);
                }
            }
        }

        return affected;
    }

    private static IReadOnlyList<string>? TryReadReferences(string repositoryRoot, string relativePath)
    {
        try
        {
            var projectPath = Path.GetFullPath(Path.Combine(
                repositoryRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            var document = XDocument.Load(projectPath);
            return document.Descendants()
                .Where(element => element.Name.LocalName == "ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include)
                    && !include.Contains("$(", StringComparison.Ordinal))
                .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include!)))
                .Where(path => IsUnderRoot(repositoryRoot, path))
                .Select(path => NormalizePath(Path.GetRelativePath(repositoryRoot, path)))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or XmlException
            or ArgumentException
            or NotSupportedException)
        {
            return null;
        }
    }

    private static bool IsUnderProject(string projectPath, string candidatePath)
    {
        var directory = GetProjectDirectory(projectPath);
        return directory.Length == 0
            || candidatePath.StartsWith($"{directory}/", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetProjectDirectory(string projectPath) =>
        NormalizePath(Path.GetDirectoryName(projectPath) ?? string.Empty).TrimEnd('/');

    private static bool IsUnderRoot(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/');
}
