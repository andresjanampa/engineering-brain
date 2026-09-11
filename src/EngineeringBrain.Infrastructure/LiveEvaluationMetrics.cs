using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class LiveEvaluationMetricCalculator
{
    private readonly InitiativeTermNormalizer _normalizer;

    public LiveEvaluationMetricCalculator(InitiativeTermNormalizer? normalizer = null)
    {
        _normalizer = normalizer ?? new InitiativeTermNormalizer();
    }

    public LiveUnderstandingMetrics EvaluateUnderstanding(
        LiveEvaluationCase item,
        InitiativeUnderstanding actual,
        RepositorySnapshot snapshot)
    {
        var golden = item.GoldenUnderstanding;
        var fields = new[]
        {
            Coverage("actors", golden.Actors, actual.Actors),
            Coverage("functionalRequirements", golden.FunctionalRequirements, actual.FunctionalRequirements),
            Coverage("businessRules", golden.BusinessRules, actual.BusinessRules),
            Coverage("dataRequirements", golden.DataRequirements, actual.DataRequirements),
            Coverage("integrations", golden.Integrations, actual.Integrations),
            Coverage("technicalCapabilities", golden.TechnicalCapabilities, actual.TechnicalCapabilities),
            Coverage("searchTerms", golden.SearchTerms, actual.SearchTerms),
            Coverage("constraints", golden.Constraints, actual.Constraints),
            Coverage("unknowns", golden.Unknowns, actual.Unknowns)
        };
        var capabilityValues = actual.TechnicalCapabilities.Concat(actual.SearchTerms).ToArray();
        var required = item.Expected.RequiredCapabilities;
        var requiredHits = required.Where(value => Matches(value, capabilityValues)).ToArray();
        var acceptableHits = item.Expected.AcceptableCapabilities.Where(value => Matches(value, capabilityValues)).ToArray();
        var goldenSearchTokens = _normalizer.Tokenize(golden.SearchTerms).ToHashSet(StringComparer.Ordinal);
        var actualSearchTokens = _normalizer.Tokenize(actual.SearchTerms).ToHashSet(StringComparer.Ordinal);
        var extra = actualSearchTokens.Except(goldenSearchTokens, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var repositoryTokens = RepositoryTokens(snapshot);
        return new LiveUnderstandingMetrics(
            fields,
            required.Count,
            requiredHits.Length,
            required.Count == 0 ? 1 : requiredHits.Length / (double)required.Count,
            required.Except(requiredHits, StringComparer.Ordinal).ToArray(),
            acceptableHits,
            golden.Unknowns.Count > 0 || item.Expected.ExpectedNeedsClarification,
            actual.Unknowns.Count > 0,
            fields.Single(field => field.Field == "unknowns").Coverage,
            fields.Single(field => field.Field == "searchTerms").Coverage,
            golden.SearchTerms.Where(value => !Matches(value, actual.SearchTerms)).ToArray(),
            extra,
            extra.Where(value => !repositoryTokens.Contains(value)).ToArray(),
            SummaryRequiresHumanReview: true);
    }

    public LiveRetrievalMetrics EvaluateRetrieval(
        CandidateRetrievalResult retrieval,
        LiveEvaluationExpectations expected)
    {
        var entityRanks = expected.RequiredEntities.Select(required => FirstRank(
            retrieval.Components, candidate => MatchesEntity(candidate, required))).ToArray();
        var projectRanks = expected.RequiredProjects.Select(required => FirstRank(
            retrieval.Projects, candidate => MatchesProject(candidate, required))).ToArray();
        return new LiveRetrievalMetrics(
            Recall(entityRanks, 5),
            Recall(entityRanks, 10),
            ReciprocalRank(entityRanks),
            entityRanks.Where(rank => rank is not null).Min(),
            Recall(projectRanks, 3),
            ReciprocalRank(projectRanks));
    }

    public static LiveRetrievalComparison CompareRetrieval(
        LiveRetrievalMetrics golden,
        LiveRetrievalMetrics actual) => new(
            golden,
            actual,
            actual.RecallAt5 - golden.RecallAt5,
            actual.RecallAt10 - golden.RecallAt10,
            actual.MeanReciprocalRank - golden.MeanReciprocalRank);

    public LiveCall2Metrics EvaluateAnalysis(
        LiveEvaluationCase item,
        InitiativeAnalysis analysis,
        IReadOnlyList<ValidatedRecommendation> validated,
        RepositorySnapshot snapshot)
    {
        var decisions = analysis.Recommendations.Select(value => value.Decision).ToHashSet();
        var expectedDecision = item.Expected.AcceptableDecisionTypes.Count == 0
            || item.Expected.AcceptableDecisionTypes.Any(decisions.Contains);
        var requiringValidation = validated.Where(value => value.ValidationStatus != EvidenceValidationStatus.Proposal).ToArray();
        var validEvidence = requiringValidation.Sum(value => value.ValidEvidence.Count);
        var allEvidence = requiringValidation.Sum(value => value.Recommendation.Evidence.Count);
        var referencedEntities = analysis.RelevantEntityIds.Concat(analysis.Recommendations
                .SelectMany(value => value.Evidence).Where(value => value.EntityId is not null).Select(value => value.EntityId!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referencedProjects = analysis.RelevantProjectIds.Concat(analysis.Recommendations
                .SelectMany(value => value.Evidence).Where(value => value.ProjectId is not null).Select(value => value.ProjectId!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedEntities = item.Expected.RequiredEntities.Concat(item.Expected.AcceptableEntities).Distinct(StringComparer.Ordinal).ToArray();
        var expectedProjects = item.Expected.RequiredProjects.Concat(item.Expected.AcceptableProjects).Distinct(StringComparer.Ordinal).ToArray();
        var entityHits = expectedEntities.Where(value => ReferencedEntity(value, referencedEntities, snapshot)).ToArray();
        var projectHits = expectedProjects.Where(value => ReferencedProject(value, referencedProjects, snapshot)).ToArray();
        var expectedNeedsClarification = item.Expected.ExpectedNeedsClarification;
        var actualNeedsClarification = analysis.Status == InitiativeAnalysisStatus.NeedsClarification;
        var clarificationRelevant = !expectedNeedsClarification || analysis.ClarifyingQuestions.Any(question =>
            item.GoldenUnderstanding.Unknowns.Any(unknown => SharesToken(question, unknown)));
        var fabricatedAccepted = validated.SelectMany(value => value.ValidEvidence)
            .Count(evidence => evidence.EntityId is not null
                && !snapshot.Entities.Any(entity => entity.Id.Equals(evidence.EntityId, StringComparison.Ordinal)));
        return new LiveCall2Metrics(
            StructuredResponseValid: true,
            expectedDecision,
            expectedDecision ? 1 : 0,
            validated.Count(value => value.ValidationStatus == EvidenceValidationStatus.Validated),
            validated.Count(value => value.ValidationStatus == EvidenceValidationStatus.Invalid),
            validated.Count(value => value.ValidationStatus == EvidenceValidationStatus.PartiallyValidated),
            validated.Count(value => value.ValidationStatus == EvidenceValidationStatus.Proposal),
            fabricatedAccepted,
            allEvidence == 0 ? 1 : validEvidence / (double)allEvidence,
            expectedNeedsClarification,
            actualNeedsClarification,
            expectedNeedsClarification == actualNeedsClarification,
            analysis.ClarifyingQuestions.Count,
            clarificationRelevant,
            entityHits,
            item.Expected.RequiredEntities.Where(value => !entityHits.Contains(value, StringComparer.Ordinal)).ToArray(),
            projectHits,
            item.Expected.RequiredProjects.Where(value => !projectHits.Contains(value, StringComparer.Ordinal)).ToArray());
    }

    public static LiveEvaluationAggregate Aggregate(
        IReadOnlyList<LiveEvaluationCaseResult> results,
        Func<ReasoningCallUsage, decimal?>? estimateCost = null)
    {
        var successful = results.Where(value => value.Status == LiveEvaluationExecutionStatus.Succeeded).ToArray();
        var understandings = results.Where(value => value.UnderstandingMetrics is not null).ToArray();
        var comparisons = results.Where(value => value.RetrievalComparison is not null).ToArray();
        var call2 = successful.Where(value => value.Call2Metrics is not null).ToArray();
        var contexts = results.Where(value => value.Context is not null).Select(value => value.Context!.EstimatedTokens).ToArray();
        var calls = results.SelectMany(value => value.Usage).ToArray();
        var costs = estimateCost is null ? [] : calls.Select(estimateCost).Where(value => value is not null).Select(value => value!.Value).ToArray();
        return new LiveEvaluationAggregate(
            results.Count,
            successful.Length,
            results.Count - successful.Length,
            results.Count(value => value.Status == LiveEvaluationExecutionStatus.SecurityBlocked),
            results.Count(value => value.Status == LiveEvaluationExecutionStatus.StructuredOutputFailure),
            Average(understandings, value => value.UnderstandingMetrics!.RequiredCapabilityHitRate),
            Average(understandings, value => value.UnderstandingMetrics!.ExpectedUnknowns == value.UnderstandingMetrics.UnknownsDetected ? 1 : 0),
            Average(comparisons, value => value.RetrievalComparison!.RecallAt5Delta),
            Average(comparisons, value => value.RetrievalComparison!.RecallAt10Delta),
            Average(comparisons, value => value.RetrievalComparison!.MeanReciprocalRankDelta),
            Average(call2, value => value.Call2Metrics!.ExpectedDecisionHitRate),
            Average(call2, value => value.Call2Metrics!.EvidenceValidationRate),
            call2.Sum(value => value.Call2Metrics!.InvalidEvidence),
            call2.Sum(value => value.Call2Metrics!.FabricatedEntitiesAccepted),
            Average(call2, value => value.Call2Metrics!.NeedsClarificationCorrect ? 1 : 0),
            contexts.Length == 0 ? 0 : contexts.Average(),
            EvaluationMetricsCalculator.Median(contexts),
            contexts.Length == 0 ? 0 : contexts.Max(),
            OutboundSent(results, value => value.SourceBodyFindings),
            OutboundSent(results, value => value.SecretFindings),
            OutboundSent(results, value => value.AbsolutePathFindings),
            OutboundSent(results, value => value.RawSnapshotFindings),
            new LiveUsageSummary(
                calls.Length,
                calls.Length + calls.Sum(value => value.Retries),
                calls.Sum(value => value.EstimatedInputTokens),
                calls.Sum(value => value.ActualInputTokens ?? 0),
                calls.Sum(value => value.CachedInputTokens ?? 0),
                calls.Sum(value => value.ActualOutputTokens ?? 0),
                calls.Sum(value => value.ReasoningTokens ?? 0),
                calls.Sum(value => value.DurationMilliseconds),
                calls.Sum(value => value.Retries),
                costs.Length == calls.Length ? costs.Sum() : null));

        static double Average(IReadOnlyList<LiveEvaluationCaseResult> source, Func<LiveEvaluationCaseResult, double> selector) =>
            source.Count == 0 ? 0 : source.Average(selector);
        static int OutboundSent(IReadOnlyList<LiveEvaluationCaseResult> source, Func<OutboundValidationResult, int> selector) =>
            source.SelectMany(value => value.SecurityChecks).Where(value => value.IsValid).Sum(selector);
    }

    private LiveFieldCoverage Coverage(string field, IReadOnlyList<string> expected, IReadOnlyList<string> actual)
    {
        var missing = expected.Where(value => !Matches(value, actual)).ToArray();
        return new LiveFieldCoverage(field, expected.Count, expected.Count - missing.Length,
            expected.Count == 0 ? 1 : (expected.Count - missing.Length) / (double)expected.Count, missing);
    }

    private bool Matches(string expected, IEnumerable<string> actual)
    {
        var expectedTokens = _normalizer.Tokenize(expected);
        var actualTokens = _normalizer.Tokenize(actual).ToHashSet(StringComparer.Ordinal);
        return expectedTokens.Count > 0 && expectedTokens.All(actualTokens.Contains);
    }

    private bool SharesToken(string left, string right)
    {
        var rightTokens = _normalizer.Tokenize(right).ToHashSet(StringComparer.Ordinal);
        return _normalizer.Tokenize(left).Any(rightTokens.Contains);
    }

    private HashSet<string> RepositoryTokens(RepositorySnapshot snapshot) => _normalizer.Tokenize(
        snapshot.Entities.SelectMany(value => new[] { value.Name, value.FullName, value.RelativeFilePath }),
        snapshot.Projects.SelectMany(value => new[] { value.Name, value.RelativePath })).ToHashSet(StringComparer.Ordinal);

    private static bool MatchesEntity(ComponentCandidate candidate, string expected) =>
        candidate.EntityId.Equals(expected, StringComparison.OrdinalIgnoreCase)
        || candidate.Name.Equals(expected, StringComparison.OrdinalIgnoreCase)
        || candidate.FullName.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesProject(ProjectCandidate candidate, string expected) =>
        candidate.ProjectId.Equals(expected, StringComparison.OrdinalIgnoreCase)
        || candidate.Name.Equals(expected, StringComparison.OrdinalIgnoreCase)
        || Path.GetFileNameWithoutExtension(candidate.RelativePath).Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static int? FirstRank<T>(IReadOnlyList<T> values, Func<T, bool> predicate)
    {
        for (var index = 0; index < values.Count; index++) if (predicate(values[index])) return index + 1;
        return null;
    }

    private static double Recall(IReadOnlyList<int?> ranks, int maximum) =>
        ranks.Count == 0 ? 1 : ranks.Count(rank => rank is not null && rank <= maximum) / (double)ranks.Count;

    private static double ReciprocalRank(IReadOnlyList<int?> ranks)
    {
        if (ranks.Count == 0) return 1;
        var rank = ranks.Where(value => value is not null).Min();
        return rank is null ? 0 : 1d / rank.Value;
    }

    private static bool ReferencedEntity(string expected, HashSet<string> actual, RepositorySnapshot snapshot)
    {
        var entity = snapshot.Entities.FirstOrDefault(value => value.Id.Equals(expected, StringComparison.OrdinalIgnoreCase)
            || value.Name.Equals(expected, StringComparison.OrdinalIgnoreCase)
            || value.FullName.Equals(expected, StringComparison.OrdinalIgnoreCase));
        return actual.Contains(expected) || entity is not null && actual.Contains(entity.Id);
    }

    private static bool ReferencedProject(string expected, HashSet<string> actual, RepositorySnapshot snapshot)
    {
        var project = snapshot.Projects.FirstOrDefault(value => value.Id.Equals(expected, StringComparison.OrdinalIgnoreCase)
            || value.Name.Equals(expected, StringComparison.OrdinalIgnoreCase)
            || Path.GetFileNameWithoutExtension(value.RelativePath).Equals(expected, StringComparison.OrdinalIgnoreCase));
        return actual.Contains(expected) || project is not null && actual.Contains(project.Id);
    }
}

public static class LiveConsistencyCalculator
{
    public static LiveConsistencyMetrics Calculate(IReadOnlyList<LiveEvaluationCaseResult> results)
    {
        var groups = results.GroupBy(value => value.CaseId, StringComparer.Ordinal).ToArray();
        return new LiveConsistencyMetrics(
            groups.Length == 0 ? 0 : groups.Max(group => group.Count()),
            Average(groups, group => Agreement(group.Select(value => value.Status.ToString()).ToArray())),
            Average(groups, group => Pairwise(group.Where(Succeeded).Select(value => value.Analysis!.Recommendations
                .Select(item => item.Decision.ToString()).ToHashSet(StringComparer.Ordinal)).ToArray())),
            Average(groups, group => Pairwise(group.Where(Succeeded).Select(value => value.Analysis!.RelevantEntityIds
                .ToHashSet(StringComparer.Ordinal)).ToArray())),
            Average(groups, group => Pairwise(group.Where(Succeeded).Select(value => value.Understanding!.TechnicalCapabilities
                .Concat(value.Understanding.SearchTerms).ToHashSet(StringComparer.OrdinalIgnoreCase)).ToArray())),
            Average(groups, group => Pairwise(group.Where(Succeeded).Select(value => value.Recommendations
                .Select(item => item.ValidationStatus.ToString()).ToHashSet(StringComparer.Ordinal)).ToArray())));

        static bool Succeeded(LiveEvaluationCaseResult value) => value.Status == LiveEvaluationExecutionStatus.Succeeded;
        static double Average(IEnumerable<IGrouping<string, LiveEvaluationCaseResult>> source,
            Func<IGrouping<string, LiveEvaluationCaseResult>, double> selector)
        {
            var values = source.Select(selector).ToArray();
            return values.Length == 0 ? 1 : values.Average();
        }
    }

    private static double Agreement(IReadOnlyList<string> values) => values.Count <= 1
        ? 1
        : values.GroupBy(value => value, StringComparer.Ordinal).Max(group => group.Count()) / (double)values.Count;

    private static double Pairwise(IReadOnlyList<HashSet<string>> sets)
    {
        if (sets.Count <= 1) return 1;
        var scores = new List<double>();
        for (var left = 0; left < sets.Count; left++)
        for (var right = left + 1; right < sets.Count; right++)
        {
            var union = sets[left].Union(sets[right], sets[left].Comparer).Count();
            scores.Add(union == 0 ? 1 : sets[left].Intersect(sets[right], sets[left].Comparer).Count() / (double)union);
        }
        return scores.Average();
    }
}
