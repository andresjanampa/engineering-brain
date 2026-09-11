using System.Diagnostics;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class RepositoryAnalysisEngine
{
    private readonly IRepositoryScanner _scanner;
    private readonly IGitInfoProvider _gitInfo;
    private readonly IGitChangeProvider? _gitChanges;
    private readonly IReadOnlyList<ILanguageAnalyzer> _analyzers;
    private readonly IRepositorySnapshotStore _store;
    private readonly IncrementalChangeDetector _changeDetector;
    private readonly AffectedProjectResolver _affectedProjects;
    private readonly GraphMerger _graphMerger;
    private readonly GraphIntegrityValidator _graphValidator;

    public RepositoryAnalysisEngine(
        IRepositoryScanner scanner,
        IGitInfoProvider gitInfo,
        IReadOnlyList<ILanguageAnalyzer> analyzers,
        IRepositorySnapshotStore store,
        IGitChangeProvider? gitChanges = null,
        IncrementalChangeDetector? changeDetector = null,
        AffectedProjectResolver? affectedProjects = null,
        GraphMerger? graphMerger = null,
        GraphIntegrityValidator? graphValidator = null)
    {
        _scanner = scanner;
        _gitInfo = gitInfo;
        _analyzers = analyzers;
        _store = store;
        _gitChanges = gitChanges;
        _changeDetector = changeDetector ?? new IncrementalChangeDetector();
        _affectedProjects = affectedProjects ?? new AffectedProjectResolver();
        _graphMerger = graphMerger ?? new GraphMerger();
        _graphValidator = graphValidator ?? new GraphIntegrityValidator();
    }

    public async Task<RepositoryAnalysisResult> ScanAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var scan = await _scanner.ScanAsync(path, cancellationToken);
        var git = await _gitInfo.GetInfoAsync(scan.Repository.Root, cancellationToken);
        var loaded = await _store.LoadLatestAsync(scan.Repository.Id, cancellationToken);
        var previous = loaded.Snapshot;
        var analyzerVersion = string.Join(
            "+",
            _analyzers.Select(analyzer => analyzer.Version).OrderBy(version => version, StringComparer.Ordinal));
        var currentGraph = ProjectDependencyGraph.Create(
            scan.Repository.Root,
            scan.Files,
            previous?.Projects ?? []);

        var fullReason = GetInitialFullReason(loaded, scan.Repository, git, analyzerVersion);
        IReadOnlyList<FileChange> changes;
        AffectedProjectPlan? projectPlan = null;

        if (previous is null)
        {
            changes = scan.Files.Select(file => new FileChange(
                    FileChangeKind.Added,
                    file.RelativePath,
                    null,
                    ChangeDetectionMethod.FileSystem,
                    currentGraph.FindOwner(file.RelativePath),
                    $"{file.RelativePath} has no previous snapshot baseline."))
                .ToArray();
        }
        else
        {
            IReadOnlyList<GitRename> renames = [];
            if (fullReason is null && _gitChanges is not null && git.IsRepository)
            {
                renames = await _gitChanges.GetRenamesAsync(
                    scan.Repository.Root,
                    previous.Git.HeadCommit,
                    git.HeadCommit,
                    cancellationToken);
            }

            changes = _changeDetector.Detect(previous.Files, scan.Files, renames);
            if (fullReason is null)
            {
                projectPlan = _affectedProjects.Resolve(changes, currentGraph, previous.Projects);
                if (projectPlan.RequiresFullAnalysis)
                {
                    fullReason = projectPlan.FullAnalysisReason;
                }
                changes = projectPlan.Changes;
            }
        }

        var isFull = fullReason is not null;
        GraphMergeResult graph;
        IReadOnlyList<string> direct;
        IReadOnlyList<string> transitive;
        IReadOnlyList<string> analyzed;
        IReadOnlyList<string> reused;

        if (isFull)
        {
            var fullAnalysis = await AnalyzeLanguagesAsync(
                scan.Repository.Root,
                scan.Files,
                includedProjectPaths: null,
                reusableEntities: null,
                cancellationToken);
            graph = new GraphMergeResult(
                fullAnalysis.Projects,
                fullAnalysis.Entities,
                fullAnalysis.Relations,
                fullAnalysis.Analysis,
                fullAnalysis.Diagnostics,
                0,
                fullAnalysis.Entities.Count);
            analyzed = fullAnalysis.Projects.Select(project => project.RelativePath).ToArray();
            reused = [];
            direct = projectPlan?.DirectlyAffectedProjects
                ?? currentGraph.Projects.Select(project => project.RelativePath).ToArray();
            transitive = projectPlan?.TransitivelyAffectedProjects ?? [];
        }
        else
        {
            projectPlan ??= _affectedProjects.Resolve(changes, currentGraph, previous!.Projects);
            LanguageAnalysisResult regenerated;
            if (projectPlan.ProjectsToAnalyze.Count == 0)
            {
                regenerated = EmptyAnalysis(analyzerVersion);
            }
            else
            {
                var reusableProjectIds = previous!.Projects
                    .Where(project => projectPlan.ProjectsToReuse.Contains(
                        project.RelativePath,
                        StringComparer.OrdinalIgnoreCase))
                    .Select(project => project.Id)
                    .ToHashSet(StringComparer.Ordinal);
                var reusableEntities = previous.Entities
                    .Where(entity => entity.ProjectId is not null
                        && reusableProjectIds.Contains(entity.ProjectId))
                    .ToArray();
                regenerated = await AnalyzeLanguagesAsync(
                    scan.Repository.Root,
                    scan.Files,
                    projectPlan.ProjectsToAnalyze,
                    reusableEntities,
                    cancellationToken);
            }

            graph = _graphMerger.Merge(
                previous!,
                regenerated,
                projectPlan.ProjectsToAnalyze,
                currentGraph.Projects.Select(project => project.RelativePath).ToArray(),
                analyzerVersion);
            direct = projectPlan.DirectlyAffectedProjects;
            transitive = projectPlan.TransitivelyAffectedProjects;
            analyzed = projectPlan.ProjectsToAnalyze;
            reused = projectPlan.ProjectsToReuse;
        }

        var integrity = _graphValidator.Validate(graph.Entities, graph.Relations);
        _graphValidator.ThrowIfInvalid(integrity);
        var metrics = new ScanPerformanceMetrics(
            scan.Files.Count,
            changes.Count,
            graph.Projects.Count,
            analyzed.Count,
            reused.Count,
            graph.ReusedEntities,
            graph.RegeneratedEntities,
            stopwatch.ElapsedMilliseconds);
        var incremental = new IncrementalAnalysisSummary(
            isFull ? ScanExecutionMode.Full : ScanExecutionMode.Incremental,
            loaded.Status != SnapshotLoadStatus.NotFound,
            fullReason,
            changes,
            direct,
            transitive,
            analyzed,
            reused,
            metrics,
            integrity);
        var snapshot = new RepositorySnapshot(
            SnapshotJsonSerializer.CurrentSchemaVersion,
            DateTimeOffset.UtcNow,
            scan.Repository,
            git,
            scan.Files,
            scan.Languages,
            graph.Projects,
            graph.Entities,
            graph.Relations,
            graph.Analysis,
            graph.Diagnostics,
            incremental);
        var snapshotPath = await _store.SaveAsync(snapshot, cancellationToken);

        return new RepositoryAnalysisResult(snapshot, snapshotPath);
    }

    private async Task<LanguageAnalysisResult> AnalyzeLanguagesAsync(
        string repositoryRoot,
        IReadOnlyList<ScannedFile> files,
        IReadOnlyList<string>? includedProjectPaths,
        IReadOnlyList<CodeEntity>? reusableEntities,
        CancellationToken cancellationToken)
    {
        var entities = new List<CodeEntity>();
        var relations = new List<CodeRelation>();
        var projects = new List<ProjectInfo>();
        var diagnostics = new List<AnalysisDiagnostic>();

        foreach (var analyzer in _analyzers)
        {
            var result = await analyzer.AnalyzeAsync(
                new LanguageAnalysisRequest(
                    repositoryRoot,
                    files,
                    includedProjectPaths,
                    reusableEntities),
                cancellationToken);
            entities.AddRange(result.Entities);
            relations.AddRange(result.Relations);
            projects.AddRange(result.Projects);
            diagnostics.AddRange(result.Diagnostics);
        }

        var semanticCount = projects.Count(project => project.AnalysisMode == ProjectAnalysisMode.Semantic);
        var fallbackCount = projects.Count - semanticCount;
        var mode = semanticCount == projects.Count && projects.Count > 0
            ? AnalysisMode.FullSemantic
            : semanticCount > 0
                ? AnalysisMode.PartialSemantic
                : AnalysisMode.SyntaxFallback;
        return new LanguageAnalysisResult(
            entities,
            relations,
            projects,
            new AnalysisSummary(
                mode,
                string.Join("+", _analyzers.Select(analyzer => analyzer.Version).OrderBy(value => value, StringComparer.Ordinal)),
                projects.Count,
                semanticCount,
                fallbackCount),
            diagnostics);
    }

    private static string? GetInitialFullReason(
        SnapshotLoadResult loaded,
        RepositoryInfo repository,
        GitInfo git,
        string analyzerVersion)
    {
        if (loaded.Status != SnapshotLoadStatus.Loaded)
        {
            return loaded.Reason;
        }

        var previous = loaded.Snapshot!;
        if (!previous.Repository.Id.Equals(repository.Id, StringComparison.Ordinal))
        {
            return "The previous snapshot belongs to another repository.";
        }

        if (!string.Equals(previous.Git.Branch, git.Branch, StringComparison.Ordinal))
        {
            return $"Git branch changed from {previous.Git.Branch ?? "none"} to {git.Branch ?? "none"}.";
        }

        if (!previous.Analysis.AnalyzerVersion.Equals(analyzerVersion, StringComparison.Ordinal))
        {
            return $"Analyzer version changed from {previous.Analysis.AnalyzerVersion} to {analyzerVersion}.";
        }

        return null;
    }

    private static LanguageAnalysisResult EmptyAnalysis(string analyzerVersion) => new(
        [],
        [],
        [],
        new AnalysisSummary(AnalysisMode.SyntaxFallback, analyzerVersion, 0, 0, 0),
        []);
}
