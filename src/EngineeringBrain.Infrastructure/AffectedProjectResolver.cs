using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed record AffectedProjectPlan(
    bool RequiresFullAnalysis,
    string? FullAnalysisReason,
    IReadOnlyList<FileChange> Changes,
    IReadOnlyList<string> DirectlyAffectedProjects,
    IReadOnlyList<string> TransitivelyAffectedProjects,
    IReadOnlyList<string> ProjectsToAnalyze,
    IReadOnlyList<string> ProjectsToReuse);

public sealed class AffectedProjectResolver
{
    private static readonly HashSet<string> GlobalConfigurationNames = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "Directory.Build.props",
        "Directory.Build.targets",
        "Directory.Build.rsp",
        "Directory.Packages.props",
        "global.json",
        "NuGet.Config"
    };

    public AffectedProjectPlan Resolve(
        IReadOnlyList<FileChange> changes,
        ProjectDependencyGraph currentGraph,
        IReadOnlyList<ProjectInfo> previousProjects)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var previousGraph = CreatePreviousGraph(previousProjects, comparer);
        var direct = new HashSet<string>(comparer);
        var mappedChanges = new List<FileChange>();

        foreach (var change in changes)
        {
            var paths = new[] { change.CurrentPath, change.PreviousPath }
                .Where(path => path is not null)
                .Cast<string>()
                .Distinct(comparer)
                .ToArray();
            var globalPath = paths.FirstOrDefault(path =>
                GlobalConfigurationNames.Contains(Path.GetFileName(path)));
            if (globalPath is not null)
            {
                return Full(changes, $"Global build configuration changed: {globalPath}.", currentGraph);
            }

            var solutionPath = paths.FirstOrDefault(path =>
                Path.GetExtension(path).Equals(".sln", StringComparison.OrdinalIgnoreCase));
            if (solutionPath is not null)
            {
                return Full(changes, $"Solution structure changed: {solutionPath}.", currentGraph);
            }

            var currentOwner = change.CurrentPath is null
                ? null
                : currentGraph.FindOwner(change.CurrentPath);
            var previousOwner = change.PreviousPath is null
                ? null
                : FindPreviousOwner(change.PreviousPath, previousProjects);
            if (currentOwner is not null)
            {
                direct.Add(currentOwner);
            }

            if (previousOwner is not null)
            {
                direct.Add(previousOwner);
            }

            var projectPath = currentOwner ?? previousOwner;
            mappedChanges.Add(change with { ProjectPath = projectPath });

            if (paths.Any(path => Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase))
                && projectPath is null)
            {
                return Full(mappedChanges.Concat(changes.Skip(mappedChanges.Count)).ToArray(),
                    $"A changed C# file could not be mapped to a project: {paths[0]}.",
                    currentGraph);
            }
        }

        var affected = new HashSet<string>(direct, comparer);
        affected.UnionWith(currentGraph.GetTransitiveDependents(direct));
        affected.UnionWith(GetPreviousDependents(previousGraph, direct, comparer));
        var currentPaths = currentGraph.Projects.Select(project => project.RelativePath).ToHashSet(comparer);
        var projectsToAnalyze = affected.Where(currentPaths.Contains).OrderBy(path => path, comparer).ToArray();
        var transitive = affected.Except(direct, comparer).OrderBy(path => path, comparer).ToArray();
        var reused = currentPaths.Except(projectsToAnalyze, comparer).OrderBy(path => path, comparer).ToArray();

        return new AffectedProjectPlan(
            false,
            null,
            mappedChanges,
            direct.OrderBy(path => path, comparer).ToArray(),
            transitive,
            projectsToAnalyze,
            reused);
    }

    private static AffectedProjectPlan Full(
        IReadOnlyList<FileChange> changes,
        string reason,
        ProjectDependencyGraph currentGraph)
    {
        var projects = currentGraph.Projects.Select(project => project.RelativePath).ToArray();
        return new AffectedProjectPlan(true, reason, changes, projects, [], projects, []);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> CreatePreviousGraph(
        IReadOnlyList<ProjectInfo> projects,
        StringComparer comparer) => projects.ToDictionary(
            project => project.RelativePath,
            project => project.ProjectReferences,
            comparer);

    private static IReadOnlySet<string> GetPreviousDependents(
        IReadOnlyDictionary<string, IReadOnlyList<string>> graph,
        IEnumerable<string> direct,
        StringComparer comparer)
    {
        var affected = new HashSet<string>(direct, comparer);
        var pending = new Queue<string>(affected);
        while (pending.Count > 0)
        {
            var dependency = pending.Dequeue();
            foreach (var pair in graph.Where(pair => pair.Value.Contains(dependency, comparer)))
            {
                if (affected.Add(pair.Key))
                {
                    pending.Enqueue(pair.Key);
                }
            }
        }

        return affected;
    }

    private static string? FindPreviousOwner(
        string relativePath,
        IReadOnlyList<ProjectInfo> previousProjects)
    {
        var documented = previousProjects.FirstOrDefault(project =>
            project.Documents.Contains(relativePath, StringComparer.OrdinalIgnoreCase));
        if (documented is not null)
        {
            return documented.RelativePath;
        }

        if (previousProjects.Any(project =>
                project.RelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase)))
        {
            return relativePath;
        }

        return previousProjects
            .Where(project => IsUnderProject(project.RelativePath, relativePath))
            .OrderByDescending(project => Path.GetDirectoryName(project.RelativePath)?.Length ?? 0)
            .Select(project => project.RelativePath)
            .FirstOrDefault();
    }

    private static bool IsUnderProject(string projectPath, string candidatePath)
    {
        var directory = (Path.GetDirectoryName(projectPath) ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        return directory.Length == 0
            || candidatePath.Replace('\\', '/').StartsWith($"{directory}/", StringComparison.OrdinalIgnoreCase);
    }
}
