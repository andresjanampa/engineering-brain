using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class EvaluationSuiteSerializer
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public async Task<EvaluationSuite> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        var suite = JsonSerializer.Deserialize<EvaluationSuite>(json, JsonOptions)
            ?? throw new InvalidDataException("Evaluation suite is empty.");
        if (suite.EvaluationSchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidDataException($"Evaluation schema {suite.EvaluationSchemaVersion} is unsupported; expected {CurrentSchemaVersion}.");
        }
        if (string.IsNullOrWhiteSpace(suite.Id) || suite.Cases.Count == 0
            || suite.Cases.Any(item => string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.InitiativePath))
            || suite.Cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != suite.Cases.Count)
        {
            throw new InvalidDataException("Evaluation suite has missing or duplicate case identities.");
        }

        var root = Path.GetDirectoryName(Path.GetFullPath(path))!;
        foreach (var item in suite.Cases)
        {
            var initiativePath = ResolveWithin(root, item.InitiativePath);
            if (!File.Exists(initiativePath))
            {
                throw new InvalidDataException($"Evaluation initiative does not exist: {item.InitiativePath}");
            }
        }
        return suite;
    }

    public static string ResolveWithin(string root, string relativePath)
    {
        root = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Evaluation path escapes the suite directory.");
        }
        return path;
    }

    internal static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public static class EvaluationMetricsCalculator
{
    public static double RecallAt(IReadOnlyList<string> ranked, IReadOnlyList<string> required, int k)
    {
        if (required.Count == 0) return 1;
        var expected = required.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ranked.Take(k).Count(expected.Contains) / (double)expected.Count;
    }

    public static double PrecisionAt(
        IReadOnlyList<string> ranked,
        IReadOnlyList<string> required,
        IReadOnlyList<string> acceptable,
        int k)
    {
        var relevant = required.Concat(acceptable).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (relevant.Count == 0) return 0;
        var considered = ranked.Take(k).ToArray();
        return considered.Count(relevant.Contains) / (double)k;
    }

    public static double ReciprocalRank(IReadOnlyList<string> ranked, IReadOnlyList<string> required)
    {
        var expected = required.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (expected.Count == 0) return 1;
        for (var index = 0; index < ranked.Count; index++)
        {
            if (expected.Contains(ranked[index])) return 1d / (index + 1);
        }
        return 0;
    }

    public static double Median(IEnumerable<int> values)
    {
        var ordered = values.Order().ToArray();
        if (ordered.Length == 0) return 0;
        var middle = ordered.Length / 2;
        return ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2d : ordered[middle];
    }
}

public sealed class EvaluationBaselineComparer
{
    public IReadOnlyList<EvaluationRegression> Compare(
        EvaluationBaseline baseline,
        EvaluationAggregateMetrics current,
        EvaluationThresholds? thresholds = null,
        IReadOnlyList<EvaluationCaseResult>? currentCases = null)
    {
        thresholds ??= new EvaluationThresholds();
        var regressions = new List<EvaluationRegression>();
        Decrease("Recall@10", baseline.Aggregate.RecallAt10, current.RecallAt10);
        Decrease("MRR", baseline.Aggregate.MeanReciprocalRank, current.MeanReciprocalRank);
        Decrease("Project MRR", baseline.Aggregate.ProjectMeanReciprocalRank, current.ProjectMeanReciprocalRank);
        Increase("Non-test TestCandidateRatio@10", baseline.Aggregate.NonTestCaseTestCandidateRatioAt10,
            current.NonTestCaseTestCandidateRatioAt10, thresholds.TestNoiseIncrease);
        Growth("Average CALL #2 tokens", baseline.Aggregate.AverageCall2Tokens, current.AverageCall2Tokens);
        Growth("Maximum CALL #2 tokens", baseline.Aggregate.MaximumCall2Tokens, current.MaximumCall2Tokens);
        if (currentCases is not null)
        {
            var byId = currentCases.ToDictionary(item => item.Id, StringComparer.Ordinal);
            foreach (var previousCase in baseline.Cases)
            {
                if (!byId.TryGetValue(previousCase.Id, out var currentCase)) continue;
                Decrease($"{previousCase.Id} Recall@10", previousCase.RecallAt10, currentCase.Retrieval.RecallAt10);
                Decrease($"{previousCase.Id} MRR", previousCase.MeanReciprocalRank, currentCase.Retrieval.MeanReciprocalRank);
                Decrease($"{previousCase.Id} Project MRR", previousCase.ProjectMeanReciprocalRank, currentCase.Retrieval.ProjectMeanReciprocalRank);
                Growth($"{previousCase.Id} CALL #2 tokens", previousCase.EstimatedCall2Tokens, currentCase.Context.EstimatedCall2Tokens);
            }
        }
        return regressions;

        void Decrease(string metric, double previous, double value)
        {
            if (previous - value > thresholds.CriticalMetricDecrease)
                regressions.Add(new(metric, previous, value, "Critical metric decreased beyond the configured threshold."));
        }
        void Increase(string metric, double previous, double value, double allowed)
        {
            if (value - previous > allowed)
                regressions.Add(new(metric, previous, value, "Noise increased beyond the configured threshold."));
        }
        void Growth(string metric, double previous, double value)
        {
            if (previous > 0 && ((value - previous) / previous) * 100 > thresholds.ContextGrowthPercent)
                regressions.Add(new(metric, previous, value, "Context grew beyond the configured percentage."));
        }
    }
}

public sealed class EvaluationHarness
{
    public const string RetrievalVersion = "lexical-graph-v1";
    private readonly InitiativeCandidateRetriever _retriever;
    private readonly InitiativeContextBuilder _contextBuilder;
    private readonly AnalysisEvidenceValidator _validator;
    private readonly TokenEstimator _estimator;
    private readonly TokenBudgetOptions _budget;

    public EvaluationHarness(
        InitiativeCandidateRetriever? retriever = null,
        InitiativeContextBuilder? contextBuilder = null,
        AnalysisEvidenceValidator? validator = null,
        TokenEstimator? estimator = null,
        TokenBudgetOptions? budget = null)
    {
        _retriever = retriever ?? new InitiativeCandidateRetriever();
        _estimator = estimator ?? new TokenEstimator();
        _budget = budget ?? new TokenBudgetOptions();
        _contextBuilder = contextBuilder ?? new InitiativeContextBuilder(estimator: _estimator, budget: _budget);
        _validator = validator ?? new AnalysisEvidenceValidator();
    }

    public async Task<IReadOnlyList<EvaluationCaseResult>> EvaluateAsync(
        EvaluationSuite suite,
        string suitePath,
        ProjectMemorySyncResult memory,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetDirectoryName(Path.GetFullPath(suitePath))!;
        var results = new List<EvaluationCaseResult>();
        foreach (var item in suite.Cases)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var initiative = await File.ReadAllTextAsync(
                EvaluationSuiteSerializer.ResolveWithin(root, item.InitiativePath), cancellationToken);
            var retrieval = _retriever.Retrieve(item.Understanding, memory.Manifest, memory.SourceSnapshot);
            var context = await _contextBuilder.BuildAsync(item.Understanding, retrieval, memory, cancellationToken);
            var analysis = CreateGoldenAnalysis(item, memory.SourceSnapshot);
            var validated = _validator.Validate(analysis, memory.SourceSnapshot);
            results.Add(EvaluateCase(item, initiative, retrieval, context, analysis, validated, memory.SourceSnapshot));
        }
        return results;
    }

    public static EvaluationAggregateMetrics Aggregate(IReadOnlyList<EvaluationCaseResult> cases)
    {
        var nonTest = cases.Where(item => !item.Tags.Contains("test-relevant", StringComparer.OrdinalIgnoreCase)).ToArray();
        var test = cases.Where(item => item.Tags.Contains("test-relevant", StringComparer.OrdinalIgnoreCase)).ToArray();
        var entityCases = cases.Where(item => item.Retrieval.RequiredEntityCount > 0).ToArray();
        var projectCases = cases.Where(item => item.Retrieval.RequiredProjectCount > 0).ToArray();
        var requiringEvidence = cases.Sum(item => item.Recommendations.EvidenceReferencesRequiringValidation);
        return new EvaluationAggregateMetrics(
            cases.Count, cases.Count(item => item.Passed),
            Average(entityCases, item => item.Retrieval.RecallAt5), Average(entityCases, item => item.Retrieval.RecallAt10),
            Average(entityCases, item => item.Retrieval.PrecisionAt5), Average(entityCases, item => item.Retrieval.PrecisionAt10),
            Average(entityCases, item => item.Retrieval.MeanReciprocalRank), Average(projectCases, item => item.Retrieval.ProjectRecallAt3),
            Average(projectCases, item => item.Retrieval.ProjectMeanReciprocalRank),
            nonTest.Length == 0 ? 0 : nonTest.Average(item => item.Retrieval.TestCandidateRatioAt10),
            test.Length == 0 ? 0 : test.Average(item => item.Retrieval.TestCandidateRatioAt10),
            cases.Count == 0 ? 0 : cases.Average(item => item.Context.CandidateCount),
            cases.Count == 0 ? 0 : cases.Average(item => item.Context.SelectedComponentCount),
            cases.Count == 0 ? 0 : cases.Average(item => item.Context.EstimatedCall2Tokens),
            EvaluationMetricsCalculator.Median(cases.Select(item => item.Context.EstimatedCall2Tokens)),
            cases.Count == 0 ? 0 : cases.Max(item => item.Context.EstimatedCall2Tokens),
            cases.Count(item => item.Context.BudgetViolation),
            requiringEvidence == 0 ? 1 : cases.Sum(item => item.Recommendations.ValidatedEvidenceReferences) / (double)requiringEvidence,
            cases.Sum(item => item.Recommendations.InvalidEvidence),
            cases.Sum(item => item.Recommendations.FabricatedEntitiesAccepted),
            cases.Count(item => item.Recommendations.NeedsClarificationExpected),
            cases.Count(item => item.Recommendations.NeedsClarificationActual));

        static double Average(IReadOnlyList<EvaluationCaseResult> source, Func<EvaluationCaseResult, double> selector) =>
            source.Count == 0 ? 0 : source.Average(selector);
    }

    public static IReadOnlyList<EvaluationCategoryMetrics> Categories(IReadOnlyList<EvaluationCaseResult> cases) =>
        cases.SelectMany(item => item.Tags.Select(tag => (Tag: tag, Case: item)))
            .GroupBy(item => item.Tag, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var entityCases = group.Where(item => item.Case.Retrieval.RequiredEntityCount > 0).ToArray();
                var projectCases = group.Where(item => item.Case.Retrieval.RequiredProjectCount > 0).ToArray();
                return new EvaluationCategoryMetrics(
                    group.Key, group.Count(), entityCases.Length, projectCases.Length,
                    entityCases.Length == 0 ? 0 : entityCases.Average(item => item.Case.Retrieval.RecallAt10),
                    entityCases.Length == 0 ? 0 : entityCases.Average(item => item.Case.Retrieval.MeanReciprocalRank),
                    projectCases.Length == 0 ? 0 : projectCases.Average(item => item.Case.Retrieval.ProjectMeanReciprocalRank),
                    group.Average(item => item.Case.Context.EstimatedCall2Tokens));
            })
            .ToArray();

    private EvaluationCaseResult EvaluateCase(
        EvaluationCase item,
        string initiative,
        CandidateRetrievalResult retrieval,
        InitiativeContext context,
        InitiativeAnalysis analysis,
        IReadOnlyList<ValidatedRecommendation> validated,
        RepositorySnapshot snapshot)
    {
        var entityRanked = retrieval.Components.Select(candidate => MatchIdentity(candidate, item.Expected.RequiredEntities
            .Concat(item.Expected.AcceptableEntities).Concat(item.Expected.NegativeEntities))).ToArray();
        var projectRanked = retrieval.Projects.Select(candidate => MatchIdentity(candidate, item.Expected.RequiredProjects
            .Concat(item.Expected.AcceptableProjects).Concat(item.Expected.NegativeProjects))).ToArray();
        var retrievalMetrics = new RetrievalEvaluationMetrics(
            EvaluationMetricsCalculator.RecallAt(entityRanked, item.Expected.RequiredEntities, 5),
            EvaluationMetricsCalculator.RecallAt(entityRanked, item.Expected.RequiredEntities, 10),
            EvaluationMetricsCalculator.PrecisionAt(entityRanked, item.Expected.RequiredEntities, item.Expected.AcceptableEntities, 5),
            EvaluationMetricsCalculator.PrecisionAt(entityRanked, item.Expected.RequiredEntities, item.Expected.AcceptableEntities, 10),
            EvaluationMetricsCalculator.ReciprocalRank(entityRanked, item.Expected.RequiredEntities),
            EvaluationMetricsCalculator.RecallAt(projectRanked, item.Expected.RequiredProjects, 3),
            EvaluationMetricsCalculator.ReciprocalRank(projectRanked, item.Expected.RequiredProjects),
            retrieval.Components.Take(5).Count(IsTestCandidate),
            retrieval.Components.Take(10).Count(IsTestCandidate),
            retrieval.Components.Count == 0 ? 0 : retrieval.Components.Take(10).Count(IsTestCandidate) / (double)Math.Min(10, retrieval.Components.Count),
            Dominates(entityRanked, item.Expected.NegativeEntities)
                || Dominates(projectRanked, item.Expected.NegativeProjects),
            item.Expected.RequiredEntities.Count,
            item.Expected.RequiredProjects.Count);
        var requiring = validated.Where(value => value.Recommendation.Decision != RecommendationDecision.Create)
            .Sum(value => value.Recommendation.Evidence.Count);
        var recommendations = new RecommendationEvaluationMetrics(
            item.Expected.RecommendationTypes.Count(type => analysis.Recommendations.Any(value => value.Decision == type)),
            item.Expected.RecommendationTypes.Count,
            validated.Count(value => value.ValidationStatus == EvidenceValidationStatus.Validated),
            validated.Count(value => value.ValidationStatus == EvidenceValidationStatus.Invalid),
            validated.Count(value => value.ValidationStatus == EvidenceValidationStatus.PartiallyValidated),
            validated.Count(value => value.ValidationStatus == EvidenceValidationStatus.Proposal),
            analysis.Recommendations.Count(value => !item.Expected.RecommendationTypes.Contains(value.Decision)),
            analysis.Recommendations.Count(value => value.EpistemicStatus == EpistemicStatus.Fact),
            analysis.Recommendations.Count(value => value.EpistemicStatus == EpistemicStatus.Inference),
            analysis.Recommendations.Count(value => value.EpistemicStatus == EpistemicStatus.Proposal),
            analysis.Recommendations.Count(value => value.EpistemicStatus == EpistemicStatus.Unknown),
            validated.Where(value => value.Recommendation.Decision != RecommendationDecision.Create).Sum(value => value.ValidEvidence.Count),
            requiring,
            requiring == 0 ? 1 : validated.Where(value => value.Recommendation.Decision != RecommendationDecision.Create).Sum(value => value.ValidEvidence.Count) / (double)requiring,
            validated.SelectMany(value => value.ValidEvidence).Count(evidence => evidence.EntityId is not null
                && !snapshot.Entities.Any(entity => entity.Id == evidence.EntityId)),
            item.Expected.AnalysisStatus == InitiativeAnalysisStatus.NeedsClarification,
            analysis.Status == InitiativeAnalysisStatus.NeedsClarification);
        var call1 = _estimator.Estimate(initiative) + _estimator.Estimate(InitiativeAnalysisPrompts.Understanding);
        var call2 = context.EstimatedTokens + _estimator.Estimate(InitiativeAnalysisPrompts.ArchitectureAnalysis);
        var contextMetrics = new ContextEvaluationMetrics(
            call1, call2,
            context.Segments.Count(segment => segment.Kind == ContextSegmentKind.ProjectNote),
            context.Segments.Count(segment => segment.Kind == ContextSegmentKind.ComponentNote),
            retrieval.Relations.Count,
            retrieval.ComponentsConsidered,
            retrieval.Components.Count,
            context.PrunedNotePaths.Count,
            call2 * 100d / _budget.MaximumReasoningInputTokens,
            call2 > _budget.MaximumReasoningInputTokens);
        var missingEntities = item.Expected.RequiredEntities.Where(required => !entityRanked.Take(10)
            .Contains(required, StringComparer.OrdinalIgnoreCase)).ToArray();
        var missingProjects = item.Expected.RequiredProjects.Where(required => !projectRanked.Take(3)
            .Contains(required, StringComparer.OrdinalIgnoreCase)).ToArray();
        var diagnostics = new List<string>();
        if (missingEntities.Length > 0) diagnostics.Add($"Required entities missing from top 10: {string.Join(", ", missingEntities)}");
        if (missingProjects.Length > 0) diagnostics.Add($"Required projects missing from top 3: {string.Join(", ", missingProjects)}");
        if (retrievalMetrics.NegativeCandidateDominates) diagnostics.Add("A negative candidate appears in the top 3.");
        var passed = missingEntities.Length == 0 && missingProjects.Length == 0
            && !retrievalMetrics.NegativeCandidateDominates
            && recommendations.ExpectedTypesFound == recommendations.ExpectedTypesTotal
            && recommendations.InvalidEvidence == 0
            && recommendations.UnsupportedRecommendations == 0
            && recommendations.EvidenceValidationRate == 1
            && recommendations.FabricatedEntitiesAccepted == 0
            && recommendations.NeedsClarificationExpected == recommendations.NeedsClarificationActual
            && !contextMetrics.BudgetViolation;
        return new EvaluationCaseResult(item.Id, item.Tags, passed, retrievalMetrics, recommendations, contextMetrics,
            retrieval.Components.Take(10).Select((value, index) => Ranked(value.EntityId, index, value.Score, value.MatchReasons, IsTestCandidate(value))).ToArray(),
            retrieval.Projects.Take(5).Select((value, index) => Ranked(value.ProjectId, index, value.Score, value.MatchReasons, IsTestCandidate(value))).ToArray(),
            missingEntities, missingProjects, diagnostics);
    }

    private static InitiativeAnalysis CreateGoldenAnalysis(EvaluationCase item, RepositorySnapshot snapshot)
    {
        var evidenceEntities = item.Expected.EvidenceEntities.Select(value => FindEntity(snapshot, value)).Where(value => value is not null).Cast<CodeEntity>().ToArray();
        var recommendations = item.Expected.RecommendationTypes.Select(decision =>
        {
            var evidence = decision == RecommendationDecision.Create || evidenceEntities.Length == 0
                ? []
                : new[] { ToEvidence(snapshot, evidenceEntities[0]) };
            return new AnalysisRecommendation(decision, item.Description, "Golden deterministic evaluation response.",
                decision == RecommendationDecision.Create ? EpistemicStatus.Proposal : EpistemicStatus.Fact,
                evidence, [], []);
        }).ToArray();
        return new InitiativeAnalysis(item.Expected.AnalysisStatus, "Deterministic golden analysis", [], [], recommendations,
            [], [], item.Expected.AnalysisStatus == InitiativeAnalysisStatus.NeedsClarification ? ["Clarify the triggering event and delivery channel."] : [],
            "Evaluation fixture; no probabilistic quality claim.");
    }

    private static EvidenceReference ToEvidence(RepositorySnapshot snapshot, CodeEntity entity) => new(
        EvidenceKind.Entity, snapshot.Repository.Id, snapshot.Git.Branch ?? "(no branch)", entity.Id, entity.ProjectId,
        null, null, null, entity.RelativeFilePath, entity.StartLine, entity.EndLine, entity.ResolutionLevel);

    private static CodeEntity? FindEntity(RepositorySnapshot snapshot, string identity) => snapshot.Entities.FirstOrDefault(entity =>
        entity.Id.Equals(identity, StringComparison.OrdinalIgnoreCase)
        || entity.Name.Equals(identity, StringComparison.OrdinalIgnoreCase)
        || entity.FullName.Equals(identity, StringComparison.OrdinalIgnoreCase));

    private static string MatchIdentity(ComponentCandidate candidate, IEnumerable<string> expected) => expected.FirstOrDefault(value =>
        value.Equals(candidate.EntityId, StringComparison.OrdinalIgnoreCase)
        || value.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase)
        || value.Equals(candidate.FullName, StringComparison.OrdinalIgnoreCase)) ?? candidate.EntityId;

    private static string MatchIdentity(ProjectCandidate candidate, IEnumerable<string> expected) => expected.FirstOrDefault(value =>
        value.Equals(candidate.ProjectId, StringComparison.OrdinalIgnoreCase)
        || value.Equals(candidate.Name, StringComparison.OrdinalIgnoreCase)
        || value.Equals(Path.GetFileNameWithoutExtension(candidate.RelativePath), StringComparison.OrdinalIgnoreCase)) ?? candidate.ProjectId;

    private static bool IsTestCandidate(ComponentCandidate candidate) => IsTestCandidate(candidate.ProjectId) || IsTestCandidate(candidate.RelativePath);
    private static bool IsTestCandidate(ProjectCandidate candidate) => IsTestCandidate(candidate.Name) || IsTestCandidate(candidate.RelativePath);
    private static bool IsTestCandidate(string value) => value.Contains(".Tests", StringComparison.OrdinalIgnoreCase)
        || value.Contains("/tests/", StringComparison.OrdinalIgnoreCase) || value.Contains("\\tests\\", StringComparison.OrdinalIgnoreCase);
    private static bool Dominates(IReadOnlyList<string> ranked, IReadOnlyList<string> negative)
    {
        var top = ranked.Take(3).ToArray();
        var count = top.Count(value => negative.Contains(value, StringComparer.OrdinalIgnoreCase));
        return top.Length > 0 && (negative.Contains(top[0], StringComparer.OrdinalIgnoreCase) || count > top.Length / 2);
    }
    private static RankedEvaluationCandidate Ranked(string identity, int index, int score, IReadOnlyList<MatchReason> reasons, bool test) =>
        new(identity, index + 1, score, reasons, test);
}

public sealed class EvaluationResultStore
{
    private static readonly JsonSerializerOptions JsonOptions = EvaluationSuiteSerializer.CreateOptions();
    private readonly string _dataRoot;
    public EvaluationResultStore(string? dataRoot = null) => _dataRoot = dataRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".engineering-brain");

    public async Task<string> SaveResultAsync(EvaluationRunResult result, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(_dataRoot, "repositories", KnowledgeIdentity.CreateRepositoryKey(result.RepositoryId),
            "evaluations", "branches", KnowledgeIdentity.CreateBranchKey(result.Branch));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "latest.json");
        await WriteAsync(path, result with { ResultPath = path }, cancellationToken);
        return path;
    }

    public static async Task<EvaluationBaseline?> LoadBaselineAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<EvaluationBaseline>(json, JsonOptions)
            ?? throw new InvalidDataException("Evaluation baseline is empty.");
    }

    public static Task SaveBaselineAsync(string path, EvaluationBaseline baseline, CancellationToken cancellationToken = default) =>
        WriteAsync(path, baseline, cancellationToken);

    private static async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var json = JsonSerializer.Serialize(value, JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), cancellationToken);
    }
}
