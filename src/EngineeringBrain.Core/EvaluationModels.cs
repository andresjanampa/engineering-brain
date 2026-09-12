namespace EngineeringBrain.Core;

public enum EvaluationSplit
{
    Tuning,
    Holdout
}

public sealed record EvaluationSuite(
    int EvaluationSchemaVersion,
    string Id,
    string Description,
    IReadOnlyList<EvaluationCase> Cases);

public sealed record EvaluationCase(
    string Id,
    string Description,
    string InitiativePath,
    IReadOnlyList<string> Tags,
    EvaluationSplit Split,
    InitiativeUnderstanding Understanding,
    EvaluationExpectations Expected);

public sealed record EvaluationExpectations(
    IReadOnlyList<string> RequiredEntities,
    IReadOnlyList<string> AcceptableEntities,
    IReadOnlyList<string> NegativeEntities,
    IReadOnlyList<string> RequiredProjects,
    IReadOnlyList<string> AcceptableProjects,
    IReadOnlyList<string> NegativeProjects,
    IReadOnlyList<RecommendationDecision> RecommendationTypes,
    IReadOnlyList<string> EvidenceEntities,
    InitiativeAnalysisStatus AnalysisStatus);

public sealed record RankedEvaluationCandidate(
    string Identity,
    int Rank,
    int Score,
    IReadOnlyList<MatchReason> MatchReasons,
    bool IsTestCandidate);

public sealed record RetrievalEvaluationMetrics(
    double RecallAt5,
    double RecallAt10,
    double PrecisionAt5,
    double PrecisionAt10,
    double MeanReciprocalRank,
    double ProjectRecallAt3,
    double ProjectMeanReciprocalRank,
    int TestCandidatesAt5,
    int TestCandidatesAt10,
    double TestCandidateRatioAt5,
    double TestCandidateRatioAt10,
    bool NegativeCandidateDominates,
    int RequiredEntityCount,
    int RequiredProjectCount);

public sealed record RecommendationEvaluationMetrics(
    int ExpectedTypesFound,
    int ExpectedTypesTotal,
    int ValidatedRecommendations,
    int InvalidEvidence,
    int PartiallyValidated,
    int Proposals,
    int UnsupportedRecommendations,
    int FactStatements,
    int InferenceStatements,
    int ProposalStatements,
    int UnknownStatements,
    int ValidatedEvidenceReferences,
    int EvidenceReferencesRequiringValidation,
    double EvidenceValidationRate,
    int FabricatedEntitiesAccepted,
    bool NeedsClarificationExpected,
    bool NeedsClarificationActual);

public sealed record ContextEvaluationMetrics(
    int EstimatedCall1Tokens,
    int EstimatedCall2Tokens,
    int ProjectNotes,
    int ComponentNotes,
    int GraphRelations,
    int CandidateCount,
    int SelectedComponentCount,
    int PrunedNoteCount,
    double BudgetUtilizationPercent,
    bool BudgetViolation);

public sealed record EvaluationCaseResult(
    string Id,
    IReadOnlyList<string> Tags,
    EvaluationSplit Split,
    bool Passed,
    RetrievalEvaluationMetrics Retrieval,
    RecommendationEvaluationMetrics Recommendations,
    ContextEvaluationMetrics Context,
    IReadOnlyList<RankedEvaluationCandidate> TopComponents,
    IReadOnlyList<RankedEvaluationCandidate> TopProjects,
    IReadOnlyList<string> MissingRequiredEntities,
    IReadOnlyList<string> MissingRequiredProjects,
    IReadOnlyList<string> Diagnostics);

public sealed record EvaluationAggregateMetrics(
    int Cases,
    int Passed,
    double RecallAt5,
    double RecallAt10,
    double PrecisionAt5,
    double PrecisionAt10,
    double MeanReciprocalRank,
    double ProjectRecallAt3,
    double ProjectMeanReciprocalRank,
    double NonTestCaseTestCandidateRatioAt5,
    double NonTestCaseTestCandidateRatioAt10,
    double TestRelevantCaseTestCandidateRatioAt5,
    double TestRelevantCaseTestCandidateRatioAt10,
    double AverageCandidateCount,
    double AverageSelectedComponents,
    double AverageCall2Tokens,
    double MedianCall2Tokens,
    int MaximumCall2Tokens,
    int BudgetViolations,
    double EvidenceValidationRate,
    int InvalidEvidenceCount,
    int FabricatedEntitiesAccepted,
    int NeedsClarificationExpected,
    int NeedsClarificationActual);

public sealed record EvaluationCategoryMetrics(
    string Category,
    int Cases,
    int EntityRetrievalCases,
    int ProjectRetrievalCases,
    double RecallAt10,
    double MeanReciprocalRank,
    double ProjectMeanReciprocalRank,
    double AverageCall2Tokens);

public sealed record EvaluationGroupedMetrics(
    EvaluationAggregateMetrics Tuning,
    EvaluationAggregateMetrics Holdout,
    EvaluationAggregateMetrics All);

public sealed record EvaluationRegression(
    string Metric,
    double Baseline,
    double Current,
    string Reason);

public sealed record EvaluationRunResult(
    int EvaluationSchemaVersion,
    string SuiteId,
    string RepositoryId,
    string Branch,
    string AnalyzerVersion,
    string RetrievalVersion,
    DateTimeOffset EvaluatedAtUtc,
    EvaluationAggregateMetrics Aggregate,
    EvaluationGroupedMetrics Splits,
    IReadOnlyList<EvaluationCategoryMetrics> Categories,
    IReadOnlyList<EvaluationCaseResult> Cases,
    IReadOnlyList<EvaluationRegression> Regressions,
    string BaselineStatus,
    string ResultPath);

public sealed record EvaluationBaseline(
    int EvaluationSchemaVersion,
    string SuiteId,
    string AnalyzerVersion,
    string RetrievalVersion,
    int CaseCount,
    EvaluationAggregateMetrics Aggregate,
    IReadOnlyList<EvaluationBaselineCase> Cases,
    EvaluationGroupedMetrics? Splits = null);

public sealed record EvaluationBaselineCase(
    string Id,
    double RecallAt10,
    double MeanReciprocalRank,
    double ProjectMeanReciprocalRank,
    int EstimatedCall2Tokens,
    EvaluationSplit Split = EvaluationSplit.Tuning);

public sealed record EvaluationThresholds(
    double CriticalMetricDecrease = 0.10,
    double TestNoiseIncrease = 0.10,
    double ContextGrowthPercent = 50);

public sealed record OutboundValidationResult(
    bool IsValid,
    int SourceBodyFindings,
    int SecretFindings,
    int AbsolutePathFindings,
    int RawSnapshotFindings,
    IReadOnlyList<string> DiagnosticCodes);

public sealed record PreviewCall1Manifest(
    string InitiativeHash,
    int CharacterCount,
    int EstimatedTokens,
    string InterpretationSource);

public sealed record PreviewCall2Manifest(
    IReadOnlyList<string> SelectedProjectIds,
    IReadOnlyList<string> SelectedEntityIds,
    IReadOnlyList<string> SelectedNotePaths,
    int SelectedRelations,
    int ProjectNotes,
    int ComponentNotes,
    int EstimatedTokens,
    bool SourceBodiesIncluded);

public sealed record RemoteContextPreview(
    int PreviewSchemaVersion,
    string InitiativeFileName,
    string RepositoryId,
    string Branch,
    string InterpretationModel,
    string ReasoningModel,
    string InterpretationReasoningEffort,
    string AnalysisReasoningEffort,
    PreviewCall1Manifest Call1,
    PreviewCall2Manifest Call2,
    OutboundValidationResult Security,
    int HardTokenLimit,
    bool WithinBudget,
    string ManifestPath,
    CandidateRetrievalResult Retrieval);
