using EngineeringBrain.Analyzers.CSharp;
using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Analyzers.CSharp.Tests;

public sealed class InitiativeAnalysisDogfoodTests
{
    [Fact]
    public async Task PythonAnalyzerInitiative_UsesRealRepositorySignalsWithOfflineProvider()
    {
        const string initiative = "Agregar soporte para analizar proyectos Python manteniendo la arquitectura extensible existente.";
        var repositoryRoot = FindRepositoryRoot();
        var dataRoot = Path.Combine(Path.GetTempPath(), $"engineering-brain-dogfood-{Guid.NewGuid():N}");
        try
        {
            var git = new GitInfoProvider();
            var scan = await new RepositoryAnalysisEngine(
                new RepositoryScanner(),
                git,
                [new CSharpAnalyzer()],
                new LocalRepositorySnapshotStore(dataRoot),
                git).ScanAsync(repositoryRoot);
            var memory = await new ProjectMemoryService(
                store: new LocalProjectMemoryStore(dataRoot)).SyncAsync(scan.Snapshot);
            var provider = new DogfoodReasoningProvider(scan.Snapshot);

            var result = await new InitiativeAnalysisService(
                provider,
                store: new LocalInitiativeAnalysisStore(dataRoot)).AnalyzeAsync(
                new InitiativeAnalysisRequest(
                    "python-analyzer.md",
                    initiative,
                    memory,
                    "offline-interpretation",
                    "offline-reasoning",
                    PersistResult: false));

            Assert.Contains(result.Retrieval.Components, candidate => candidate.Name == "ILanguageAnalyzer");
            Assert.Contains(result.Retrieval.Components, candidate => candidate.Name == "CSharpAnalyzer");
            Assert.Contains(result.Retrieval.Projects, candidate =>
                candidate.RelativePath.Contains("EngineeringBrain.Analyzers.CSharp", StringComparison.Ordinal));
            Assert.Equal(
                EvidenceValidationStatus.Validated,
                Assert.Single(result.Recommendations).ValidatedRecommendation.ValidationStatus);
            Assert.DoesNotContain("public class", provider.Requests[1].UserData, StringComparison.Ordinal);

            Console.WriteLine($"Initiative: {initiative}");
            Console.WriteLine($"Capabilities: {string.Join(", ", result.Understanding.TechnicalCapabilities)}");
            Console.WriteLine($"Projects considered/selected: {result.Retrieval.ProjectsConsidered}/{result.Retrieval.Projects.Count}");
            foreach (var project in result.Retrieval.Projects)
            {
                Console.WriteLine($"Project: {project.Name}; path={project.RelativePath}; score={project.Score}");
            }
            Console.WriteLine($"Components considered/selected: {result.Retrieval.ComponentsConsidered}/{result.Retrieval.Components.Count}");
            foreach (var candidate in result.Retrieval.Components.Take(5))
            {
                Console.WriteLine($"Candidate: {candidate.FullName}; score={candidate.Score}; reasons={string.Join(", ", candidate.MatchReasons.Select(reason => $"{reason.Signal}:{reason.MatchedValue}(+{reason.Points})"))}");
            }

            Console.WriteLine($"Context estimated tokens: {result.Context.EstimatedTokens}");
            Console.WriteLine($"Analysis: {result.Analysis.Summary}; status={result.Analysis.Status}");
            Console.WriteLine(
                $"Recommendation: {result.Recommendations[0].ValidatedRecommendation.Recommendation.Decision}; "
                + $"evidence={result.Recommendations[0].ValidatedRecommendation.ValidationStatus}; "
                + $"policy={result.PolicyOutcome}; disposition={result.Recommendations[0].Disposition}");
        }
        finally
        {
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EngineeringBrain.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("EngineeringBrain.sln was not found.");
    }

    private sealed class DogfoodReasoningProvider : IReasoningProvider
    {
        private readonly RepositorySnapshot _snapshot;

        public DogfoodReasoningProvider(RepositorySnapshot snapshot)
        {
            _snapshot = snapshot;
        }

        public string Name => "OfflineFake";

        public List<ReasoningRequest> Requests { get; } = [];

        public Task<ReasoningResult<T>> GenerateStructuredAsync<T>(
            ReasoningRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            object value = typeof(T) == typeof(InitiativeUnderstanding)
                ? new InitiativeUnderstanding(
                    "Add support for analyzing Python projects through the existing extensibility model.",
                    ["developer"],
                    ["Analyze Python projects."],
                    [],
                    [],
                    [],
                    ["language analyzer", "architecture extension"],
                    ["python", "language analyzer", "extension"],
                    ["preserve existing extensibility"],
                    ["Python analysis depth is unspecified."])
                : CreateAnalysis(request.UserData);
            return Task.FromResult(new ReasoningResult<T>(
                (T)value,
                new ReasoningCallUsage(request.Stage, Name, request.Model, request.EstimatedInputTokens,
                    request.EstimatedInputTokens, 0, 20, 1, 0)));
        }

        private InitiativeAnalysis CreateAnalysis(string context)
        {
            var entityLine = context.Split('\n').First(line => line.StartsWith("ENTITY | ", StringComparison.Ordinal));
            var entityId = entityLine.Split('|', StringSplitOptions.TrimEntries)[1];
            var entity = _snapshot.Entities.Single(item => item.Id == entityId);
            var evidence = new EvidenceReference(
                EvidenceKind.Entity,
                _snapshot.Repository.Id,
                _snapshot.Git.Branch ?? "(no branch)",
                entity.Id,
                entity.ProjectId,
                null,
                null,
                null,
                entity.RelativeFilePath,
                entity.StartLine,
                entity.EndLine,
                entity.ResolutionLevel);
            var recommendation = new AnalysisRecommendation(
                RecommendationDecision.Reuse,
                entity.FullName,
                "Use the existing language analyzer extension point identified by static analysis.",
                EpistemicStatus.Inference,
                [evidence],
                ["Known impact is limited to selected graph relations."],
                ["Python parser implementation is not known."],
                []);
            return new InitiativeAnalysis(
                InitiativeAnalysisStatus.Complete,
                "Extend the existing language analyzer architecture.",
                [entity.ProjectId!],
                [entity.Id],
                [recommendation],
                [],
                [],
                [],
                "The integration point is backed by semantic snapshot evidence; implementation details remain unknown.");
        }
    }
}
