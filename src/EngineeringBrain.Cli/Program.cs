using EngineeringBrain.Analyzers.CSharp;
using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

return await BrainCli.RunAsync(args);

internal static class BrainCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || !args[0].Equals("scan", StringComparison.OrdinalIgnoreCase))
        {
            WriteUsage();
            return args.Length == 0 ? 0 : 2;
        }

        if (args.Length > 2)
        {
            Console.Error.WriteLine("Too many arguments.");
            WriteUsage();
            return 2;
        }

        var path = args.Length == 2 ? args[1] : Environment.CurrentDirectory;
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

            WriteSummary(result.Snapshot, result.SnapshotPath);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Scan cancelled.");
            return 130;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Scan failed: {exception.Message}");
            return 1;
        }
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
        Console.WriteLine("If path is omitted, the current directory is scanned.");
    }
}
