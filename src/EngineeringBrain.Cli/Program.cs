using EngineeringBrain.Analyzers.CSharp;
using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

return await BrainCli.RunAsync(args);

internal static class BrainCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            WriteUsage();
            return 0;
        }

        var isScan = args[0].Equals("scan", StringComparison.OrdinalIgnoreCase);
        var isMemorySync = args.Length >= 2
            && args[0].Equals("memory", StringComparison.OrdinalIgnoreCase)
            && args[1].Equals("sync", StringComparison.OrdinalIgnoreCase);
        var isAnalyze = args[0].Equals("analyze", StringComparison.OrdinalIgnoreCase);
        if (isAnalyze)
        {
            return await RunAnalyzeAsync(args);
        }

        var maximumArguments = isMemorySync ? 3 : 2;
        if ((!isScan && !isMemorySync) || args.Length > maximumArguments)
        {
            Console.Error.WriteLine("Invalid command or too many arguments.");
            WriteUsage();
            return 2;
        }

        var path = isMemorySync
            ? args.Length == 3 ? args[2] : Environment.CurrentDirectory
            : args.Length == 2 ? args[1] : Environment.CurrentDirectory;
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            IRepositoryScanner scanner = new RepositoryScanner();
            var git = new GitInfoProvider();
            IReadOnlyList<ILanguageAnalyzer> analyzers = [new CSharpAnalyzer()];
            IRepositorySnapshotStore store = new LocalRepositorySnapshotStore();
            var engine = new RepositoryAnalysisEngine(scanner, git, analyzers, store, git);
            var result = await engine.ScanAsync(path, cancellation.Token);

            if (isMemorySync)
            {
                var memory = await new ProjectMemoryService().SyncAsync(
                    result.Snapshot,
                    cancellation.Token);
                WriteMemorySummary(memory);
            }
            else
            {
                WriteSummary(result.Snapshot, result.SnapshotPath);
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Command cancelled.");
            return 130;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Command failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunAnalyzeAsync(string[] args)
    {
        if (!TryParseAnalyze(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            WriteUsage();
            return 2;
        }

        using var cancellation = CreateCancellationSource();
        try
        {
            if (!File.Exists(options.InitiativePath))
            {
                throw new FileNotFoundException("Initiative file was not found.", options.InitiativePath);
            }

            var initiative = await File.ReadAllTextAsync(options.InitiativePath, cancellation.Token);
            var apiKey = RemoteReasoningAuthorization.RequireOpenAIApiKey(options.AllowRemote);
            Console.WriteLine("Remote reasoning: explicitly authorized");
            Console.WriteLine("Call 1 sends only the initiative text. Call 2 sends selected Project Memory and evidence metadata.");
            Console.WriteLine("Source bodies, repository files, environment files, credentials, and the full snapshot are not sent.");

            IRepositoryScanner scanner = new RepositoryScanner();
            var git = new GitInfoProvider();
            IReadOnlyList<ILanguageAnalyzer> analyzers = [new CSharpAnalyzer()];
            IRepositorySnapshotStore snapshotStore = new LocalRepositorySnapshotStore();
            var engine = new RepositoryAnalysisEngine(scanner, git, analyzers, snapshotStore, git);
            var scan = await engine.ScanAsync(options.RepositoryPath, cancellation.Token);
            var memory = await new ProjectMemoryService().SyncAsync(scan.Snapshot, cancellation.Token);
            IReasoningProvider provider = new OpenAIReasoningProvider(apiKey);
            var service = new InitiativeAnalysisService(provider);
            var result = await service.AnalyzeAsync(
                new InitiativeAnalysisRequest(
                    options.InitiativePath,
                    initiative,
                    memory,
                    options.InterpretationModel,
                    options.ReasoningModel),
                cancellation.Token);
            WriteAnalysis(result);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Command cancelled.");
            return 130;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException
            or InvalidDataException)
        {
            Console.Error.WriteLine($"Command failed: {exception.Message}");
            return 1;
        }
    }

    private static CancellationTokenSource CreateCancellationSource()
    {
        var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        return cancellation;
    }

    private static bool TryParseAnalyze(
        string[] args,
        out AnalyzeOptions options,
        out string error)
    {
        options = null!;
        error = string.Empty;
        if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            error = "Analyze requires an initiative Markdown file.";
            return false;
        }

        var initiativePath = Path.GetFullPath(args[1]);
        var repositoryPath = Environment.CurrentDirectory;
        var allowRemote = false;
        var interpretationModel = Environment.GetEnvironmentVariable("ENGINEERING_BRAIN_INTERPRETATION_MODEL")
            ?? "gpt-5.6-luna";
        var reasoningModel = Environment.GetEnvironmentVariable("ENGINEERING_BRAIN_REASONING_MODEL")
            ?? "gpt-5.6-sol";
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--allow-remote":
                    allowRemote = true;
                    break;
                case "--repo" when index + 1 < args.Length:
                    repositoryPath = Path.GetFullPath(args[++index]);
                    break;
                case "--interpretation-model" when index + 1 < args.Length:
                    interpretationModel = args[++index];
                    break;
                case "--reasoning-model" when index + 1 < args.Length:
                    reasoningModel = args[++index];
                    break;
                default:
                    error = $"Unknown or incomplete analyze option: {args[index]}";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(interpretationModel) || string.IsNullOrWhiteSpace(reasoningModel))
        {
            error = "Reasoning model names cannot be empty.";
            return false;
        }

        options = new AnalyzeOptions(
            initiativePath,
            repositoryPath,
            allowRemote,
            interpretationModel,
            reasoningModel);
        return true;
    }

    private static void WriteAnalysis(InitiativeAnalysisResult result)
    {
        Console.WriteLine("Engineering Brain");
        WriteSection("Initiative Analysis");
        Console.WriteLine($"Repository: {result.Repository.Name}");
        Console.WriteLine($"Branch: {result.Git.Branch ?? "(no branch)"}");
        Console.WriteLine($"Commit: {result.Git.HeadCommit ?? "n/a"}");
        Console.WriteLine($"Working tree: {FormatWorkingTree(result.Git)}");
        Console.WriteLine($"Status: {result.Analysis.Status}");
        Console.WriteLine($"Summary: {result.Analysis.Summary}");

        WriteSection("Initiative Understanding");
        Console.WriteLine(result.Understanding.Summary);
        WriteValues("Capabilities", result.Understanding.TechnicalCapabilities);
        WriteValues("Unknowns", result.Understanding.Unknowns);

        WriteSection("Deterministic Retrieval");
        Console.WriteLine($"Projects considered: {result.Retrieval.ProjectsConsidered}");
        Console.WriteLine($"Projects selected: {result.Retrieval.Projects.Count}");
        Console.WriteLine($"Components considered: {result.Retrieval.ComponentsConsidered}");
        Console.WriteLine($"Components selected: {result.Retrieval.Components.Count}");
        foreach (var candidate in result.Retrieval.Components)
        {
            var reasons = string.Join(", ", candidate.MatchReasons.Select(reason => $"{reason.Signal}:{reason.MatchedValue} (+{reason.Points})"));
            Console.WriteLine($"- {candidate.FullName} | score {candidate.Score} | {reasons}");
        }

        WriteSection("Recommendations");
        foreach (var recommendation in result.Recommendations)
        {
            Console.WriteLine($"{recommendation.Recommendation.Decision}: {recommendation.Recommendation.Subject}");
            Console.WriteLine($"Evidence: {recommendation.ValidationStatus}; epistemic: {recommendation.Recommendation.EpistemicStatus}");
            Console.WriteLine($"Reason: {recommendation.Recommendation.Reason}");
            foreach (var evidence in recommendation.ValidEvidence)
            {
                Console.WriteLine($"- {FormatEvidence(evidence)}");
            }
            foreach (var diagnostic in recommendation.ValidationDiagnostics)
            {
                Console.WriteLine($"- Validation: {diagnostic}");
            }
        }

        WriteSection("Risks And Questions");
        WriteValues("Risks", result.Analysis.Risks);
        WriteValues("Unknowns", result.Analysis.Unknowns);
        WriteValues("Questions", result.Analysis.ClarifyingQuestions);
        Console.WriteLine($"Confidence: {result.Analysis.OverallConfidenceExplanation}");

        WriteSection("Token Usage");
        Console.WriteLine($"Selected context estimate: {result.Context.EstimatedTokens}");
        foreach (var call in result.Usage.Calls)
        {
            Console.WriteLine(
                $"{call.Stage}: provider={call.Provider}; model={call.Model}; estimated input={call.EstimatedInputTokens}; "
                + $"actual input={FormatUsage(call.ActualInputTokens)}; cached={FormatUsage(call.CachedInputTokens)}; "
                + $"output={FormatUsage(call.ActualOutputTokens)}; duration={call.DurationMilliseconds} ms; retries={call.Retries}");
        }

        WriteSection("Analysis Record");
        Console.WriteLine(result.SavedAnalysisPath ?? "Not persisted");
    }

    private static string FormatEvidence(EvidenceReference evidence)
    {
        var identity = evidence.EntityId ?? evidence.ProjectId
            ?? (evidence.SourceEntityId is not null
                ? $"{evidence.SourceEntityId} {evidence.RelationType} {evidence.TargetEntityId}"
                : evidence.Kind.ToString());
        var location = evidence.RelativePath is null
            ? string.Empty
            : evidence.StartLine is null
                ? $" at {evidence.RelativePath}"
                : $" at {evidence.RelativePath}:{evidence.StartLine}-{evidence.EndLine}";
        return $"{evidence.Kind}: {identity}{location} ({evidence.ResolutionLevel?.ToString() ?? "n/a"})";
    }

    private static string FormatUsage(int? tokens) => tokens?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable";

    private static void WriteValues(string label, IReadOnlyList<string> values)
    {
        Console.WriteLine($"{label}: {(values.Count == 0 ? "none" : string.Join("; ", values))}");
    }

    private static void WriteMemorySummary(ProjectMemorySyncResult result)
    {
        Console.WriteLine("Engineering Brain");
        WriteSection("Project Memory");
        Console.WriteLine($"Repository: {result.SourceSnapshot.Repository.Name}");
        Console.WriteLine($"Branch: {result.Manifest.Branch}");
        Console.WriteLine($"Mode: {result.Mode}");
        Console.WriteLine($"Snapshot: schema {result.Manifest.SourceSnapshotSchema}");
        Console.WriteLine($"Knowledge: schema {result.Manifest.KnowledgeSchemaVersion}");

        WriteSection("Source");
        Console.WriteLine($"Changes: {result.SourceSnapshot.Incremental.Metrics.ChangedFiles}");
        Console.WriteLine($"Projects: {result.SourceSnapshot.Projects.Count}");
        Console.WriteLine($"Entities: {result.SourceSnapshot.Entities.Count}");
        Console.WriteLine($"Relations: {result.SourceSnapshot.Relations.Count}");

        WriteSection("Memory");
        Console.WriteLine($"Project notes: {result.Metrics.ProjectNotes}");
        Console.WriteLine($"Component notes: {result.Metrics.ComponentNotes}");
        Console.WriteLine($"Managed notes: {result.Metrics.TotalManagedNotes}");
        Console.WriteLine($"Created: {result.Metrics.Created}");
        Console.WriteLine($"Updated: {result.Metrics.Updated}");
        Console.WriteLine($"Deleted: {result.Metrics.Deleted}");
        Console.WriteLine($"Reused: {result.Metrics.Reused}");
        Console.WriteLine($"Elapsed: {result.Metrics.ElapsedMilliseconds} ms");

        WriteSection("Integrity");
        Console.WriteLine(result.Integrity.IsValid ? "Valid" : "Invalid");

        WriteSection("Location");
        Console.WriteLine(result.Location);
    }

    private static void WriteSummary(RepositorySnapshot snapshot, string snapshotPath)
    {
        Console.WriteLine("Engineering Brain");
        WriteSection("Repository");
        Console.WriteLine($"Name: {snapshot.Repository.Name}");
        Console.WriteLine($"Root: {snapshot.Repository.Root}");
        Console.WriteLine($"Branch: {snapshot.Git.Branch ?? "not a Git repository"}");
        Console.WriteLine($"Commit: {snapshot.Git.HeadCommit ?? "n/a"}");
        Console.WriteLine($"Working tree: {FormatWorkingTree(snapshot.Git)}");

        WriteSection("Scan");
        Console.WriteLine($"Mode: {snapshot.Incremental.Mode}");
        Console.WriteLine($"Previous snapshot: {(snapshot.Incremental.PreviousSnapshotFound ? "found" : "not found")}");
        if (snapshot.Incremental.FullScanReason is not null)
        {
            Console.WriteLine($"Reason: {snapshot.Incremental.FullScanReason}");
        }

        WriteSection("Changes");
        foreach (var kind in Enum.GetValues<FileChangeKind>())
        {
            Console.WriteLine($"{kind}: {snapshot.Incremental.Changes.Count(change => change.Kind == kind)}");
        }
        foreach (var change in snapshot.Incremental.Changes.Take(10))
        {
            var path = change.Kind == FileChangeKind.Renamed
                ? $"{change.PreviousPath} -> {change.CurrentPath}"
                : change.CurrentPath ?? change.PreviousPath ?? "unknown";
            var project = change.ProjectPath is null ? string.Empty : $"; project: {change.ProjectPath}";
            Console.WriteLine($"- {change.Kind}: {path} ({change.DetectionMethod}{project})");
        }
        if (snapshot.Incremental.Changes.Count > 10)
        {
            Console.WriteLine($"Additional changes saved in snapshot: {snapshot.Incremental.Changes.Count - 10}");
        }

        WriteSection("Files");
        Console.WriteLine($"Total: {snapshot.Files.Count}");

        WriteSection("Languages");
        foreach (var language in snapshot.Languages)
        {
            Console.WriteLine($"{language.Language}: {language.FileCount}");
        }

        WriteSection("Projects");
        Console.WriteLine($"Detected: {snapshot.Analysis.DetectedProjects}");
        Console.WriteLine($"Semantic: {snapshot.Analysis.SemanticProjects}");
        Console.WriteLine($"Fallback: {snapshot.Analysis.FallbackProjects}");
        Console.WriteLine($"Reanalyzed: {snapshot.Incremental.Metrics.ProjectsAnalyzed}");
        Console.WriteLine($"Reused: {snapshot.Incremental.Metrics.ProjectsReused}");

        WriteProjectList("Directly affected", snapshot.Incremental.DirectlyAffectedProjects);
        WriteProjectList("Transitively affected", snapshot.Incremental.TransitivelyAffectedProjects);
        WriteProjectList("Reuse", snapshot.Incremental.ReusedProjects);

        WriteSection("C# Analysis");
        Console.WriteLine($"Mode: {snapshot.Analysis.Mode}");
        Console.WriteLine($"Analyzer: {snapshot.Analysis.AnalyzerVersion}");
        Console.WriteLine($"Entities: {snapshot.Entities.Count}");
        Console.WriteLine($"Relations: {snapshot.Relations.Count}");
        Console.WriteLine($"Diagnostics: {snapshot.Diagnostics.Count}");

        WriteSection("Incremental Metrics");
        Console.WriteLine($"Files: {snapshot.Incremental.Metrics.TotalFiles}");
        Console.WriteLine($"Changed files: {snapshot.Incremental.Metrics.ChangedFiles}");
        Console.WriteLine($"Entities reused: {snapshot.Incremental.Metrics.EntitiesReused}");
        Console.WriteLine($"Entities regenerated: {snapshot.Incremental.Metrics.EntitiesRegenerated}");
        Console.WriteLine($"Elapsed: {snapshot.Incremental.Metrics.ElapsedMilliseconds} ms");
        Console.WriteLine($"Graph integrity: {(snapshot.Incremental.GraphIntegrity.IsValid ? "valid" : "invalid")}");

        WriteSection("Architecture");
        foreach (var type in Enum.GetValues<CodeEntityType>())
        {
            var count = snapshot.Entities.Count(entity => entity.EntityType == type);
            if (count > 0)
            {
                Console.WriteLine($"{GetEntityLabel(type)}: {count}");
            }
        }

        WriteSection("Relations");
        var reportedRelationTypes = new[]
        {
            CodeRelationType.Contains,
            CodeRelationType.ReferencesProject,
            CodeRelationType.Inherits,
            CodeRelationType.Implements
        };
        foreach (var relationType in reportedRelationTypes)
        {
            Console.WriteLine($"{relationType}: {snapshot.Relations.Count(relation => relation.RelationType == relationType)}");
        }

        var relevantDiagnostics = snapshot.Diagnostics
            .Where(diagnostic => diagnostic.Severity != AnalysisDiagnosticSeverity.Information
                || diagnostic.Code.Contains("LOAD", StringComparison.Ordinal))
            .Take(10)
            .ToArray();
        if (relevantDiagnostics.Length > 0)
        {
            WriteSection("Project Load Diagnostics");
            foreach (var diagnostic in relevantDiagnostics)
            {
                Console.WriteLine($"{diagnostic.ProjectPath ?? "Repository"}: {diagnostic.Message}");
            }

            if (snapshot.Diagnostics.Count > relevantDiagnostics.Length)
            {
                Console.WriteLine($"Additional diagnostics saved in snapshot: {snapshot.Diagnostics.Count - relevantDiagnostics.Length}");
            }
        }

        WriteSection("Snapshot");
        Console.WriteLine($"Saved to: {snapshotPath}");
    }

    private static string FormatWorkingTree(GitInfo git) => git.IsWorkingTreeClean switch
    {
        true => "clean",
        false => "dirty",
        null => "n/a"
    };

    private static string GetEntityLabel(CodeEntityType type) => type switch
    {
        CodeEntityType.Class => "Classes",
        CodeEntityType.Property => "Properties",
        _ => $"{type}s"
    };

    private static void WriteProjectList(string title, IReadOnlyList<string> projects)
    {
        if (projects.Count == 0)
        {
            return;
        }

        WriteSection(title);
        foreach (var project in projects)
        {
            Console.WriteLine(project);
        }
    }

    private static void WriteSection(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
        Console.WriteLine(new string('-', 40));
    }

    private static void WriteUsage()
    {
        Console.WriteLine("Engineering Brain");
        Console.WriteLine();
        Console.WriteLine("Usage: brain scan [path]");
        Console.WriteLine("       brain memory sync [path]");
        Console.WriteLine("       brain analyze <initiative.md> [--repo <path>] --allow-remote");
        Console.WriteLine("           [--interpretation-model <model>] [--reasoning-model <model>]");
        Console.WriteLine("If path is omitted, the current directory is scanned.");
    }

    private sealed record AnalyzeOptions(
        string InitiativePath,
        string RepositoryPath,
        bool AllowRemote,
        string InterpretationModel,
        string ReasoningModel);
}
