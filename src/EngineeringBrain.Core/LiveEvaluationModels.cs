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
    LiveEvaluationExpectations Expected);

public sealed record LiveEvaluationExpectations(
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> AcceptableCapabilities,
    InitiativeAnalysisStatus ExpectedStatus,
    IReadOnlyList<RecommendationDecision> AcceptableDecisionTypes,
    IReadOnlyList<string> RequiredEntities,
    IReadOnlyList<string> AcceptableEntities,
    IReadOnlyList<string> RequiredProjects,
    IReadOnlyList<string> AcceptableProjects,
    bool ExpectedNeedsClarification);

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
    bool ExpectedUnknowns,
    bool UnknownsDetected,
    double UnknownCoverage,
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
    bool NeedsClarificationExpected,
    bool NeedsClarificationActual,
    bool NeedsClarificationCorrect,
    int ClarifyingQuestionCount,
    bool ClarifyingQuestionsRelevant,
    IReadOnlyList<string> RelevantEntityHits,
    IReadOnlyList<string> MissingRelevantEntities,
    IReadOnlyList<string> RelevantProjectHits,
    IReadOnlyList<string> MissingRelevantProjects);

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
    IReadOnlyList<ValidatedRecommendation> Recommendations,
    LiveCall2Metrics? Call2Metrics,
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
    double NeedsClarificationAccuracy,
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
