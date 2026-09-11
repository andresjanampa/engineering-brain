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
            IGitInfoProvider git = new GitInfoProvider();
            IReadOnlyList<ILanguageAnalyzer> analyzers = [new CSharpAnalyzer()];
            IRepositorySnapshotStore store = new LocalRepositorySnapshotStore();

            var scan = await scanner.ScanAsync(path, cancellation.Token);
            var gitInfo = await git.GetInfoAsync(scan.Repository.Root, cancellation.Token);
            var analysisRequest = new LanguageAnalysisRequest(scan.Repository.Root, scan.Files);

            var entities = new List<CodeEntity>();
            var relations = new List<CodeRelation>();
            var projects = new List<ProjectInfo>();
            var diagnostics = new List<AnalysisDiagnostic>();
            AnalysisSummary? analysis = null;
            foreach (var analyzer in analyzers)
            {
                var result = await analyzer.AnalyzeAsync(analysisRequest, cancellation.Token);
                entities.AddRange(result.Entities);
                relations.AddRange(result.Relations);
                projects.AddRange(result.Projects);
                diagnostics.AddRange(result.Diagnostics);
                analysis = result.Analysis;
            }

            var snapshot = new RepositorySnapshot(
                SchemaVersion: SnapshotJsonSerializer.CurrentSchemaVersion,
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                scan.Repository,
                gitInfo,
                scan.Files,
                scan.Languages,
                projects,
                entities,
                relations,
                analysis ?? new AnalysisSummary(AnalysisMode.SyntaxFallback, "none", 0, 0, 0),
                diagnostics);
            var snapshotPath = await store.SaveAsync(snapshot, cancellation.Token);

            WriteSummary(snapshot, snapshotPath);
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

        WriteSection("C# Analysis");
        Console.WriteLine($"Mode: {snapshot.Analysis.Mode}");
        Console.WriteLine($"Analyzer: {snapshot.Analysis.AnalyzerVersion}");
        Console.WriteLine($"Entities: {snapshot.Entities.Count}");
        Console.WriteLine($"Relations: {snapshot.Relations.Count}");
        Console.WriteLine($"Diagnostics: {snapshot.Diagnostics.Count}");

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
