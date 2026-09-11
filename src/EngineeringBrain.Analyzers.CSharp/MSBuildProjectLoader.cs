using EngineeringBrain.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace EngineeringBrain.Analyzers.CSharp;

internal static class MSBuildProjectLoader
{
    private const int MaximumWorkspaceDiagnosticsPerLoad = 20;

    public static async Task<MSBuildLoadResult> LoadAsync(
        ProjectDiscoveryResult discovery,
        string repositoryRoot,
        IReadOnlyList<string>? includedProjectPaths,
        CancellationToken cancellationToken)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var discoveredByPath = discovery.Projects.ToDictionary(
            project => Path.GetFullPath(project.FullPath),
            comparer);
        var loaded = new Dictionary<string, LoadedProjectContext>(comparer);
        var diagnostics = new List<AnalysisDiagnostic>();
        var projectDiagnostics = discovery.Projects.ToDictionary(
            project => project.RelativePath,
            _ => new List<AnalysisDiagnostic>(),
            StringComparer.OrdinalIgnoreCase);
        var solutionMembership = new Dictionary<string, IReadOnlyList<string>>(
            StringComparer.OrdinalIgnoreCase);
        var selectedPaths = includedProjectPaths?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetProjects = selectedPaths is null
            ? discovery.Projects
            : discovery.Projects.Where(project => selectedPaths.Contains(project.RelativePath)).ToArray();

        foreach (var solution in selectedPaths is null ? discovery.Solutions : [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var workspace = CreateWorkspace();
            var workspaceDiagnostics = CaptureWorkspaceDiagnostics(workspace);

            try
            {
                var previouslyLoaded = loaded.Keys.ToHashSet(comparer);
                var loadedSolution = await workspace.OpenSolutionAsync(
                    solution.FullPath,
                    cancellationToken: cancellationToken);
                var members = await CaptureProjectsAsync(
                    loadedSolution,
                    discoveredByPath,
                    loaded,
                    diagnostics,
                    projectDiagnostics,
                    repositoryRoot,
                    cancellationToken);
                ApplyProjectFailures(
                    workspaceDiagnostics,
                    discovery,
                    previouslyLoaded,
                    loaded,
                    diagnostics,
                    projectDiagnostics,
                    repositoryRoot);
                solutionMembership[solution.RelativePath] = members;
                AddWorkspaceDiagnostics(
                    workspaceDiagnostics,
                    discovery,
                    repositoryRoot,
                    diagnostics,
                    projectDiagnostics);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                diagnostics.Add(new AnalysisDiagnostic(
                    "CSHARP_SOLUTION_LOAD_FAILED",
                    AnalysisDiagnosticSeverity.Warning,
                    $"Solution load failed; projects will be attempted individually. Reason: {SafeDiagnostic.Message(exception, repositoryRoot)}",
                    solution.RelativePath));
            }
        }

        foreach (var discoveredProject in targetProjects.Where(project => !loaded.ContainsKey(project.FullPath)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var workspace = CreateWorkspace();
            var workspaceDiagnostics = CaptureWorkspaceDiagnostics(workspace);

            try
            {
                var previouslyLoaded = loaded.Keys.ToHashSet(comparer);
                var project = await workspace.OpenProjectAsync(
                    discoveredProject.FullPath,
                    cancellationToken: cancellationToken);
                await CaptureProjectsAsync(
                    project.Solution,
                    discoveredByPath,
                    loaded,
                    diagnostics,
                    projectDiagnostics,
                    repositoryRoot,
                    cancellationToken);
                ApplyProjectFailures(
                    workspaceDiagnostics,
                    discovery,
                    previouslyLoaded,
                    loaded,
                    diagnostics,
                    projectDiagnostics,
                    repositoryRoot);
                AddWorkspaceDiagnostics(
                    workspaceDiagnostics,
                    discovery,
                    repositoryRoot,
                    diagnostics,
                    projectDiagnostics);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var diagnostic = new AnalysisDiagnostic(
                    "CSHARP_PROJECT_LOAD_FAILED",
                    AnalysisDiagnosticSeverity.Warning,
                    $"Semantic load failed; syntax fallback was used. Reason: {SafeDiagnostic.Message(exception, repositoryRoot)}",
                    discoveredProject.RelativePath);
                diagnostics.Add(diagnostic);
                projectDiagnostics[discoveredProject.RelativePath].Add(diagnostic);
            }
        }

        foreach (var project in targetProjects.Where(project => !loaded.ContainsKey(project.FullPath)))
        {
            if (projectDiagnostics[project.RelativePath].Any(diagnostic =>
                    diagnostic.Code == "CSHARP_PROJECT_LOAD_FAILED"))
            {
                continue;
            }

            var diagnostic = new AnalysisDiagnostic(
                "CSHARP_COMPILATION_UNAVAILABLE",
                AnalysisDiagnosticSeverity.Warning,
                "A C# compilation was not available; syntax fallback was used.",
                project.RelativePath);
            diagnostics.Add(diagnostic);
            projectDiagnostics[project.RelativePath].Add(diagnostic);
        }

        return new MSBuildLoadResult(
            loaded.Values.OrderBy(context => context.Discovery.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(),
            projectDiagnostics.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<AnalysisDiagnostic>)pair.Value,
                StringComparer.OrdinalIgnoreCase),
            solutionMembership,
            diagnostics);
    }

    private static MSBuildWorkspace CreateWorkspace()
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DesignTimeBuild"] = "true",
            ["BuildingInsideVisualStudio"] = "true",
            ["BuildProjectReferences"] = "false",
            ["Restore"] = "false"
        };
        var workspace = MSBuildWorkspace.Create(properties);
        workspace.SkipUnrecognizedProjects = true;
        return workspace;
    }

    private static List<WorkspaceDiagnostic> CaptureWorkspaceDiagnostics(MSBuildWorkspace workspace)
    {
        var diagnostics = new List<WorkspaceDiagnostic>();
        workspace.RegisterWorkspaceFailedHandler(eventArgs =>
        {
            if (diagnostics.Count < MaximumWorkspaceDiagnosticsPerLoad)
            {
                diagnostics.Add(eventArgs.Diagnostic);
            }
        });
        return diagnostics;
    }

    private static async Task<IReadOnlyList<string>> CaptureProjectsAsync(
        Solution solution,
        IReadOnlyDictionary<string, DiscoveredProject> discoveredByPath,
        IDictionary<string, LoadedProjectContext> loaded,
        ICollection<AnalysisDiagnostic> diagnostics,
        IReadOnlyDictionary<string, List<AnalysisDiagnostic>> projectDiagnostics,
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var members = new List<string>();

        foreach (var project in solution.Projects.Where(project => project.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (project.FilePath is null)
            {
                continue;
            }

            var fullPath = Path.GetFullPath(project.FilePath);
            if (!discoveredByPath.TryGetValue(fullPath, out var discovered))
            {
                continue;
            }

            members.Add(discovered.RelativePath);
            if (loaded.ContainsKey(fullPath))
            {
                continue;
            }

            try
            {
                var compilation = await project.GetCompilationAsync(cancellationToken);
                if (compilation is not null)
                {
                    loaded.Add(fullPath, new LoadedProjectContext(discovered, project, compilation));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var diagnostic = new AnalysisDiagnostic(
                    "CSHARP_COMPILATION_FAILED",
                    AnalysisDiagnosticSeverity.Warning,
                    $"Compilation creation failed; syntax fallback was used. Reason: {SafeDiagnostic.Message(exception, repositoryRoot)}",
                    discovered.RelativePath);
                diagnostics.Add(diagnostic);
                projectDiagnostics[discovered.RelativePath].Add(diagnostic);
            }
        }

        return members.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddWorkspaceDiagnostics(
        IEnumerable<WorkspaceDiagnostic> workspaceDiagnostics,
        ProjectDiscoveryResult discovery,
        string repositoryRoot,
        ICollection<AnalysisDiagnostic> diagnostics,
        IReadOnlyDictionary<string, List<AnalysisDiagnostic>> projectDiagnostics)
    {
        foreach (var workspaceDiagnostic in workspaceDiagnostics)
        {
            var associatedProject = FindAssociatedProject(
                workspaceDiagnostic.Message,
                discovery.Projects);
            var diagnostic = new AnalysisDiagnostic(
                "CSHARP_MSBUILD_DIAGNOSTIC",
                workspaceDiagnostic.Kind == WorkspaceDiagnosticKind.Failure
                    ? AnalysisDiagnosticSeverity.Warning
                    : AnalysisDiagnosticSeverity.Information,
                SafeDiagnostic.Message(workspaceDiagnostic.Message, repositoryRoot),
                associatedProject?.RelativePath);
            diagnostics.Add(diagnostic);
            if (associatedProject is not null)
            {
                projectDiagnostics[associatedProject.RelativePath].Add(diagnostic);
            }
        }
    }

    private static void ApplyProjectFailures(
        IEnumerable<WorkspaceDiagnostic> workspaceDiagnostics,
        ProjectDiscoveryResult discovery,
        IReadOnlySet<string> previouslyLoaded,
        IDictionary<string, LoadedProjectContext> loaded,
        ICollection<AnalysisDiagnostic> diagnostics,
        IReadOnlyDictionary<string, List<AnalysisDiagnostic>> projectDiagnostics,
        string repositoryRoot)
    {
        foreach (var workspaceDiagnostic in workspaceDiagnostics.Where(diagnostic =>
                     diagnostic.Kind == WorkspaceDiagnosticKind.Failure))
        {
            var project = FindAssociatedProject(workspaceDiagnostic.Message, discovery.Projects);
            if (project is null
                || previouslyLoaded.Contains(project.FullPath)
                || !loaded.Remove(project.FullPath))
            {
                continue;
            }

            if (projectDiagnostics[project.RelativePath].Any(diagnostic =>
                    diagnostic.Code == "CSHARP_PROJECT_LOAD_FAILED"))
            {
                continue;
            }

            var diagnostic = new AnalysisDiagnostic(
                "CSHARP_PROJECT_LOAD_FAILED",
                AnalysisDiagnosticSeverity.Warning,
                $"Semantic load was incomplete; syntax fallback was used. Reason: {SafeDiagnostic.Message(workspaceDiagnostic.Message, repositoryRoot)}",
                project.RelativePath);
            diagnostics.Add(diagnostic);
            projectDiagnostics[project.RelativePath].Add(diagnostic);
        }
    }

    private static DiscoveredProject? FindAssociatedProject(
        string message,
        IReadOnlyList<DiscoveredProject> projects)
    {
        var normalizedMessage = message.Replace('\\', '/');
        var pathMatches = projects.Where(project =>
                normalizedMessage.Contains(project.FullPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)
                || normalizedMessage.Contains(project.RelativePath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (pathMatches.Length == 1)
        {
            return pathMatches[0];
        }

        if (pathMatches.Length > 1)
        {
            return null;
        }

        var fileNameMatches = projects.Where(project =>
                normalizedMessage.Contains(Path.GetFileName(project.RelativePath), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return fileNameMatches.Length == 1 ? fileNameMatches[0] : null;
    }
}

internal sealed record LoadedProjectContext(
    DiscoveredProject Discovery,
    Project Project,
    Compilation Compilation);

internal sealed record MSBuildLoadResult(
    IReadOnlyList<LoadedProjectContext> LoadedProjects,
    IReadOnlyDictionary<string, IReadOnlyList<AnalysisDiagnostic>> ProjectDiagnostics,
    IReadOnlyDictionary<string, IReadOnlyList<string>> SolutionMembership,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics);
