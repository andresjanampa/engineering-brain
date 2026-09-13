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
        var expectations = item.UnderstandingExpectations;
        var required = expectations.RequiredCapabilities;
        var requiredMatches = required.Select(value => MatchExpectation(
            value, expectations.CapabilityAlternatives, capabilityValues, ignoreConnectives: false)).ToArray();
        var requiredHits = requiredMatches.Where(value => value.Matched).Select(value => value.Id).ToArray();
        var acceptableMatches = expectations.AcceptableCapabilities.Select(value => MatchExpectation(
            value, expectations.CapabilityAlternatives, capabilityValues, ignoreConnectives: false)).ToArray();
        var acceptableHits = acceptableMatches.Where(value => value.Matched).Select(value => value.Id).ToArray();
        var expectedUnknownTopics = item.UnderstandingExpectations.ExpectedUnknownTopics;
        var unknownTopicMatches = expectedUnknownTopics.Select(value => MatchExpectation(
            value, expectations.UnknownTopicAlternatives, actual.Unknowns, ignoreConnectives: true)).ToArray();
        var unknownTopicHits = unknownTopicMatches.Where(value => value.Matched).Select(value => value.Id).ToArray();
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
            expectedUnknownTopics.Count,
            unknownTopicHits.Length,
            expectedUnknownTopics.Count == 0 ? 1 : unknownTopicHits.Length / (double)expectedUnknownTopics.Count,
            expectedUnknownTopics.Except(unknownTopicHits, StringComparer.Ordinal).ToArray(),
            fields.Single(field => field.Field == "searchTerms").Coverage,
            golden.SearchTerms.Where(value => !Matches(value, actual.SearchTerms)).ToArray(),
            extra,
            extra.Where(value => !repositoryTokens.Contains(value)).ToArray(),
            SummaryRequiresHumanReview: true)
        {
            RequiredCapabilityMatches = requiredMatches,
            AcceptableCapabilityMatches = acceptableMatches,
            UnknownTopicMatches = unknownTopicMatches
        };
    }

    public LiveRetrievalMetrics EvaluateRetrieval(
        CandidateRetrievalResult retrieval,
        LiveRepositoryExpectations expected)
    {
        var entityResults = expected.RequiredEntities.Select(required => new
        {
            Expected = required,
            Rank = FirstRank(retrieval.Components, candidate => MatchesEntity(candidate, required))
        }).ToArray();
        var projectResults = expected.RequiredProjects.Select(required => new
        {
            Expected = required,
            Rank = FirstRank(retrieval.Projects, candidate => MatchesProject(candidate, required))
        }).ToArray();
        var entityRanks = entityResults.Select(value => value.Rank).ToArray();
        var projectRanks = projectResults.Select(value => value.Rank).ToArray();
        return new LiveRetrievalMetrics(
            Recall(entityRanks, 5),
            Recall(entityRanks, 10),
            ReciprocalRank(entityRanks),
            entityRanks.Where(rank => rank is not null).Min(),
            Recall(projectRanks, 3),
            ReciprocalRank(projectRanks))
        {
            MissingRequiredEntities = entityResults.Where(value => value.Rank is null)
                .Select(value => value.Expected).ToArray(),
            MissingRequiredProjects = projectResults.Where(value => value.Rank is null)
                .Select(value => value.Expected).ToArray()
        };
    }

    public static LiveRetrievalComparison CompareRetrieval(
        LiveRetrievalMetrics golden,
        LiveRetrievalMetrics actual) => new(
            golden,
            actual,
            actual.RecallAt5 - golden.RecallAt5,
            actual.RecallAt10 - golden.RecallAt10,
            actual.MeanReciprocalRank - golden.MeanReciprocalRank);

    public static LiveRetrievalComparison? SummarizeRetrieval(
        IReadOnlyList<LiveEvaluationCaseResult> results)
    {
        var comparisons = results.Where(value => value.RetrievalComparison is not null)
            .Select(value => value.RetrievalComparison!).ToArray();
        if (comparisons.Length == 0) return null;

        LiveRetrievalMetrics Average(Func<LiveRetrievalComparison, LiveRetrievalMetrics> select)
        {
            var values = comparisons.Select(select).ToArray();
            return new LiveRetrievalMetrics(
                values.Average(value => value.RecallAt5),
                values.Average(value => value.RecallAt10),
                values.Average(value => value.MeanReciprocalRank),
                null,
                values.Average(value => value.ProjectRecallAt3),
                values.Average(value => value.ProjectMeanReciprocalRank));
        }

        return CompareRetrieval(Average(value => value.Golden), Average(value => value.Actual));
    }

    public LiveCall2Metrics EvaluateAnalysis(
        LiveEvaluationCase item,
        InitiativeAnalysis analysis,
        IReadOnlyList<GovernedRecommendation> governed,
        RepositorySnapshot snapshot)
    {
        var validated = governed.Select(value => value.ValidatedRecommendation).ToArray();
        var decisions = analysis.Recommendations.Select(value => value.Decision).ToHashSet();
        var expectedDecision = item.AnalysisExpectations.AcceptableDecisionTypes.Count == 0
            || item.AnalysisExpectations.AcceptableDecisionTypes.Any(decisions.Contains);
        var requiringValidation = validated.Where(value => value.ValidationStatus != EvidenceValidationStatus.Proposal).ToArray();
        var validEvidence = requiringValidation.Sum(value => value.ValidEvidence.Count);
        var allEvidence = requiringValidation.Sum(value => value.Recommendation.Evidence.Count);
        var referencedEntities = analysis.RelevantEntityIds.Concat(analysis.Recommendations
                .SelectMany(value => value.Evidence).Where(value => value.EntityId is not null).Select(value => value.EntityId!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referencedProjects = analysis.RelevantProjectIds.Concat(analysis.Recommendations
                .SelectMany(value => value.Evidence).Where(value => value.ProjectId is not null).Select(value => value.ProjectId!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedEntities = item.RepositoryExpectations.RequiredEntities
            .Concat(item.RepositoryExpectations.AcceptableEntities).Distinct(StringComparer.Ordinal).ToArray();
        var expectedProjects = item.RepositoryExpectations.RequiredProjects
            .Concat(item.RepositoryExpectations.AcceptableProjects).Distinct(StringComparer.Ordinal).ToArray();
        var entityHits = expectedEntities.Where(value => ReferencedEntity(value, referencedEntities, snapshot)).ToArray();
        var projectHits = expectedProjects.Where(value => ReferencedProject(value, referencedProjects, snapshot)).ToArray();
        var statusCorrect = item.AnalysisExpectations.AcceptableStatuses.Contains(analysis.Status);
        var clarificationRelevant = item.AnalysisExpectations.ExpectedClarificationTopics.Count == 0
            || item.AnalysisExpectations.ExpectedClarificationTopics
                .All(topic => TopicMatches(topic, analysis.ClarifyingQuestions));
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
            item.AnalysisExpectations.AcceptableStatuses,
            analysis.Status,
            statusCorrect,
            analysis.ClarifyingQuestions.Count,
            clarificationRelevant,
            entityHits,
            item.RepositoryExpectations.RequiredEntities
                .Where(value => !entityHits.Contains(value, StringComparer.Ordinal)).ToArray(),
            projectHits,
            item.RepositoryExpectations.RequiredProjects
                .Where(value => !projectHits.Contains(value, StringComparer.Ordinal)).ToArray());
    }

    public static LivePolicyMetrics EvaluatePolicy(
        LivePolicyExpectations expected,
        PolicyGovernanceResult governance)
    {
        var policyResults = governance.Recommendations.SelectMany(value => value.PolicyResults).ToArray();
        var activations = expected.Activations.Select(expectation =>
        {
            var statuses = policyResults
                .Where(result => result.PolicyId.Equals(expectation.PolicyId, StringComparison.Ordinal))
                .Select(result => result.ComplianceStatus)
                .ToArray();
            var actualActive = statuses.Any(status => status != PolicyComplianceStatus.NotApplicable);
            return new LivePolicyActivationResult(
                expectation.PolicyId,
                expectation.ExpectedActive,
                actualActive,
                expectation.ExpectedActive == actualActive,
                statuses);
        }).ToArray();
        var outcomeCorrect = expected.AcceptableOutcomes.Count == 0
            || expected.AcceptableOutcomes.Contains(governance.Outcome);
        var escapes = governance.Recommendations.Count(recommendation =>
            recommendation.PolicyResults.Any(result => result.Severity == PolicySeverity.Block
                && result.ComplianceStatus == PolicyComplianceStatus.Violated)
            && recommendation.Disposition != RecommendationDisposition.Rejected);
        return new LivePolicyMetrics(
            activations,
            activations.Length == 0 ? 1 : activations.Count(value => value.Correct) / (double)activations.Length,
            expected.AcceptableOutcomes,
            governance.Outcome,
            outcomeCorrect,
            expected.ExpectedBlockedRecommendationEscapeCount,
            escapes,
            escapes == expected.ExpectedBlockedRecommendationEscapeCount);
    }

    public static LiveEvaluationAggregate Aggregate(
        IReadOnlyList<LiveEvaluationCaseResult> results,
        Func<ReasoningCallUsage, decimal?>? estimateCost = null)
    {
        var successful = results.Where(value => value.Status == LiveEvaluationExecutionStatus.Succeeded).ToArray();
        var understandings = results.Where(value => value.UnderstandingMetrics is not null).ToArray();
        var comparisons = results.Where(value => value.RetrievalComparison is not null).ToArray();
        var call2 = successful.Where(value => value.Call2Metrics is not null).ToArray();
        var policy = successful.Where(value => value.PolicyMetrics is not null).ToArray();
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
            Average(understandings, value => value.UnderstandingMetrics!.UnknownTopicCoverage),
            Average(comparisons, value => value.RetrievalComparison!.RecallAt5Delta),
            Average(comparisons, value => value.RetrievalComparison!.RecallAt10Delta),
            Average(comparisons, value => value.RetrievalComparison!.MeanReciprocalRankDelta),
            Average(call2, value => value.Call2Metrics!.ExpectedDecisionHitRate),
            Average(call2, value => value.Call2Metrics!.EvidenceValidationRate),
            call2.Sum(value => value.Call2Metrics!.InvalidEvidence),
            call2.Sum(value => value.Call2Metrics!.FabricatedEntitiesAccepted),
            Average(call2, value => value.Call2Metrics!.AnalysisStatusCorrect ? 1 : 0),
            Average(policy, value => value.PolicyMetrics!.PolicyActivationAccuracy),
            Average(policy, value => value.PolicyMetrics!.PolicyOutcomeCorrect ? 1 : 0),
            policy.Sum(value => value.PolicyMetrics!.BlockedRecommendationEscapeCount),
            contexts.Length == 0 ? 0 : contexts.Average(),
            EvaluationMetricsCalculator.Median(contexts),
            contexts.Length == 0 ? 0 : contexts.Max(),
            OutboundFindings(results, PolicyContentScope.CompleteRepository),
            OutboundFindings(results, PolicyContentScope.SourceBodies),
            OutboundFindings(results, PolicyContentScope.Secrets),
            OutboundFindings(results, PolicyContentScope.AbsoluteLocalPaths),
            OutboundFindings(results, PolicyContentScope.RawSnapshot),
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
        static int OutboundFindings(
            IReadOnlyList<LiveEvaluationCaseResult> source,
            PolicyContentScope category) => source
            .SelectMany(value => value.OutboundPolicyAssessments)
            .SelectMany(assessment => assessment.Results)
            .Where(result => result.Category == category && result.Outcome == OutboundPolicyOutcome.Blocked)
            .Sum(result => result.FindingCount);
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

    private bool TopicMatches(string expected, IEnumerable<string> actual)
    {
        return MatchExpectation(expected, [], actual, ignoreConnectives: true).Matched;
    }

    private LiveLexicalExpectationMatch MatchExpectation(
        string id,
        IReadOnlyList<LiveLexicalExpectationAlternatives> configuredAlternatives,
        IEnumerable<string> actual,
        bool ignoreConnectives)
    {
        var configured = configuredAlternatives.SingleOrDefault(value => value.Id.Equals(id, StringComparison.Ordinal));
        IReadOnlyList<IReadOnlyList<string>> alternatives = configured?.Alternatives
            ?? [new[] { id }];
        var actualItems = actual.Select(value => new
        {
            Value = value,
            Tokens = _normalizer.Tokenize(value).ToHashSet(StringComparer.Ordinal)
        }).ToArray();

        foreach (var alternative in alternatives)
        {
            var tokens = _normalizer.Tokenize(alternative)
                .Where(token => !ignoreConnectives || token != "or")
                .ToArray();
            if (tokens.Length == 0) continue;
            var matched = actualItems.FirstOrDefault(value => tokens.All(value.Tokens.Contains));
            if (matched is not null)
                return new LiveLexicalExpectationMatch(id, true, alternative.ToArray(), matched.Value);
        }

        return new LiveLexicalExpectationMatch(id, false, null, null);
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
                .Select(item => item.ValidatedRecommendation.ValidationStatus.ToString())
                .ToHashSet(StringComparer.Ordinal)).ToArray())));

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
