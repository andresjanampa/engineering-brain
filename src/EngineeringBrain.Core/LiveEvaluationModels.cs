namespace EngineeringBrain.Core;

public enum LiveEvaluationExecutionStatus
{
    Succeeded,
    ExecutionFailed,
    ProviderFailure,
    StructuredOutputFailure,
    SecurityBlocked
}

public sealed record LiveEvaluationSuite(
    int LiveEvaluationSchemaVersion,
    string Id,
    string Description,
    IReadOnlyList<LiveEvaluationCase> Cases);

public sealed record LiveEvaluationCase(
    string Id,
    string Description,
    string InitiativePath,
    InitiativeUnderstanding GoldenUnderstanding,
    LiveUnderstandingExpectations UnderstandingExpectations,
    LiveRepositoryExpectations RepositoryExpectations,
    LiveAnalysisExpectations AnalysisExpectations,
    LivePolicyExpectations PolicyExpectations);

public sealed record LiveUnderstandingExpectations(
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> AcceptableCapabilities,
    IReadOnlyList<string> ExpectedUnknownTopics);

public sealed record LiveRepositoryExpectations(
    IReadOnlyList<string> RequiredEntities,
    IReadOnlyList<string> AcceptableEntities,
    IReadOnlyList<string> RequiredProjects,
    IReadOnlyList<string> AcceptableProjects);

public sealed record LiveAnalysisExpectations(
    IReadOnlyList<InitiativeAnalysisStatus> AcceptableStatuses,
    IReadOnlyList<RecommendationDecision> AcceptableDecisionTypes,
    IReadOnlyList<string> ExpectedClarificationTopics);

public sealed record LivePolicyActivationExpectation(
    string PolicyId,
    bool ExpectedActive);

public sealed record LivePolicyExpectations(
    IReadOnlyList<LivePolicyActivationExpectation> Activations,
    IReadOnlyList<PolicyOutcome> AcceptableOutcomes,
    int ExpectedBlockedRecommendationEscapeCount);

public sealed record LiveEvaluationPlan(
    LiveEvaluationSuite Suite,
    IReadOnlyList<LiveEvaluationCase> Cases,
    int Runs,
    int ExpectedLogicalCalls,
    int EstimatedMaximumInputTokens);

public sealed record LiveFieldCoverage(
    string Field,
    int Expected,
    int Matched,
    double Coverage,
    IReadOnlyList<string> Missing);

public sealed record LiveUnderstandingMetrics(
    IReadOnlyList<LiveFieldCoverage> Fields,
    int RequiredCapabilities,
    int RequiredCapabilitiesHit,
    double RequiredCapabilityHitRate,
    IReadOnlyList<string> MissingRequiredCapabilities,
    IReadOnlyList<string> AcceptableCapabilitiesHit,
    int ExpectedUnknownTopics,
    int ExpectedUnknownTopicsHit,
    double UnknownTopicCoverage,
    IReadOnlyList<string> MissingExpectedUnknownTopics,
    double SearchTermCoverage,
    IReadOnlyList<string> MissingSearchTerms,
    IReadOnlyList<string> ExtraSearchTerms,
    IReadOnlyList<string> PotentiallyHarmfulSearchTerms,
    bool SummaryRequiresHumanReview);

public sealed record LiveRetrievalMetrics(
    double RecallAt5,
    double RecallAt10,
    double MeanReciprocalRank,
    int? FirstRequiredRank,
    double ProjectRecallAt3,
    double ProjectMeanReciprocalRank);

public sealed record LiveRetrievalComparison(
    LiveRetrievalMetrics Golden,
    LiveRetrievalMetrics Actual,
    double RecallAt5Delta,
    double RecallAt10Delta,
    double MeanReciprocalRankDelta);

public sealed record LiveCall2Metrics(
    bool StructuredResponseValid,
    bool ExpectedDecisionTypePresent,
    double ExpectedDecisionHitRate,
    int ValidatedRecommendations,
    int InvalidEvidence,
    int PartiallyValidatedEvidence,
    int ProposalCount,
    int FabricatedEntitiesAccepted,
    double EvidenceValidationRate,
    IReadOnlyList<InitiativeAnalysisStatus> AcceptableStatuses,
    InitiativeAnalysisStatus ActualStatus,
    bool AnalysisStatusCorrect,
    int ClarifyingQuestionCount,
    bool ClarifyingQuestionsRelevant,
    IReadOnlyList<string> RelevantEntityHits,
    IReadOnlyList<string> MissingRelevantEntities,
    IReadOnlyList<string> RelevantProjectHits,
    IReadOnlyList<string> MissingRelevantProjects);

public sealed record LivePolicyActivationResult(
    string PolicyId,
    bool ExpectedActive,
    bool ActualActive,
    bool Correct,
    IReadOnlyList<PolicyComplianceStatus> ActualStatuses);

public sealed record LivePolicyMetrics(
    IReadOnlyList<LivePolicyActivationResult> Activations,
    double PolicyActivationAccuracy,
    IReadOnlyList<PolicyOutcome> AcceptableOutcomes,
    PolicyOutcome ActualOutcome,
    bool PolicyOutcomeCorrect,
    int ExpectedBlockedRecommendationEscapeCount,
    int BlockedRecommendationEscapeCount,
    bool BlockedRecommendationEscapeCountCorrect);

public sealed record LiveContextMetrics(
    int EstimatedTokens,
    int ProjectNotes,
    int ComponentNotes,
    int Relations,
    int SelectedComponents,
    int PrunedNotes);

public sealed record LiveHumanReview(
    int? InitiativeUnderstanding,
    int? ArchitecturalRelevance,
    int? RecommendationUsefulness,
    int? EvidenceDiscipline,
    int? UncertaintyHandling,
    int? Actionability,
    string? Notes);

public sealed record LiveEvaluationCaseResult(
    string CaseId,
    int RunNumber,
    LiveEvaluationExecutionStatus Status,
    string InitiativeFileName,
    string InitiativeHash,
    InitiativeUnderstanding? Understanding,
    LiveUnderstandingMetrics? UnderstandingMetrics,
    CandidateRetrievalResult? Retrieval,
    LiveRetrievalComparison? RetrievalComparison,
    LiveContextMetrics? Context,
    InitiativeAnalysis? Analysis,
    IReadOnlyList<GovernedRecommendation> Recommendations,
    PolicyOutcome? PolicyOutcome,
    LiveCall2Metrics? Call2Metrics,
    LivePolicyMetrics? PolicyMetrics,
    IReadOnlyList<ReasoningCallUsage> Usage,
    IReadOnlyList<OutboundValidationResult> SecurityChecks,
    string? ErrorCategory,
    string? ErrorMessage,
    LiveHumanReview HumanReview);

public sealed record LiveUsageSummary(
    int LogicalCalls,
    int ProviderAttempts,
    int EstimatedInputTokens,
    int ActualInputTokens,
    int CachedInputTokens,
    int ActualOutputTokens,
    int ReasoningTokens,
    long DurationMilliseconds,
    int Retries,
    decimal? EstimatedCostUsd);

public sealed record LiveConsistencyMetrics(
    int MaximumRunsPerCase,
    double StatusAgreement,
    double DecisionAgreement,
    double EntityReferenceOverlap,
    double CapabilityOverlap,
    double EvidenceValidityAgreement);

public sealed record LiveEvaluationAggregate(
    int CasesExecuted,
    int CasesSucceeded,
    int CasesFailed,
    int SecurityBlocked,
    int StructuredOutputFailures,
    double CapabilityHitRate,
    double UnknownDetectionAccuracy,
    double AverageRecallAt5Delta,
    double AverageRecallAt10Delta,
    double AverageMeanReciprocalRankDelta,
    double ExpectedDecisionHitRate,
    double EvidenceValidationRate,
    int InvalidEvidence,
    int FabricatedEntitiesAccepted,
    double AnalysisStatusAccuracy,
    double PolicyActivationAccuracy,
    double PolicyOutcomeAccuracy,
    int BlockedRecommendationEscapeCount,
    double AverageContextTokens,
    double MedianContextTokens,
    int MaximumContextTokens,
    int SourceBodyOutbound,
    int SecretOutbound,
    int AbsolutePathOutbound,
    int RawSnapshotOutbound,
    LiveUsageSummary Usage);

public sealed record LiveEvaluationRun(
    int LiveResultSchemaVersion,
    string RunId,
    DateTimeOffset StartedAtUtc,
    string SuiteId,
    int SuiteVersion,
    string RepositoryId,
    string RepositoryName,
    string Branch,
    string? Commit,
    string Provider,
    string InterpretationModel,
    string ReasoningModel,
    string InterpretationReasoningEffort,
    string AnalysisReasoningEffort,
    int RequestedRuns,
    int ExpectedLogicalCalls,
    IReadOnlyList<LiveEvaluationCaseResult> Cases,
    LiveEvaluationAggregate Aggregate,
    LiveConsistencyMetrics Consistency,
    string ResultDirectory,
    string SummaryPath,
    string ReviewPath);
