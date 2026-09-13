namespace EngineeringBrain.Core;

public enum ReasoningStage
{
    InitiativeUnderstanding,
    ArchitectureAnalysis
}

public enum InitiativeAnalysisStatus
{
    Complete,
    NeedsClarification
}

public enum RecommendationDecision
{
    Reuse,
    Extend,
    Create,
    AvoidModifying
}

public enum EpistemicStatus
{
    Fact,
    Inference,
    Proposal,
    Unknown
}

public enum EvidenceKind
{
    Entity,
    Project,
    Relation,
    SourceLocation
}

public enum EvidenceValidationStatus
{
    Validated,
    PartiallyValidated,
    Invalid,
    Proposal
}

public enum PolicyActionOperation
{
    RemoteTransmission,
    ModifyComponent,
    CreateExternalIntegration,
    Other
}

public enum PolicyActionBoundary
{
    Local,
    Remote,
    None,
    Unknown
}

public enum PolicyContentScope
{
    None,
    BoundedFacts,
    SourceBodies,
    RawSnapshot,
    CompleteRepository,
    Secrets,
    AbsoluteLocalPaths,
    Unknown
}

public enum PolicyAuthorizationMode
{
    NotApplicable,
    Explicit,
    Automatic,
    None,
    Unknown
}

public enum PolicySourceKind
{
    System,
    Project
}

public enum PolicySeverity
{
    Block,
    Warn
}

public enum PolicyComplianceStatus
{
    Compliant,
    Violated,
    NotApplicable,
    Unknown
}

public enum RecommendationDisposition
{
    Accepted,
    NeedsReview,
    Rejected
}

public enum PolicyOutcome
{
    Allowed,
    Warning,
    Blocked,
    Unknown
}

public enum ContextSegmentKind
{
    InitiativeUnderstanding,
    RepositoryIdentity,
    RootIndex,
    ArchitectureOverview,
    ProjectNote,
    ComponentNote,
    GraphEvidence,
    SourceBody,
    RawSnapshot,
    CompleteRepository
}

public sealed record InitiativeUnderstanding(
    string Summary,
    IReadOnlyList<string> Actors,
    IReadOnlyList<string> FunctionalRequirements,
    IReadOnlyList<string> BusinessRules,
    IReadOnlyList<string> DataRequirements,
    IReadOnlyList<string> Integrations,
    IReadOnlyList<string> TechnicalCapabilities,
    IReadOnlyList<string> SearchTerms,
    IReadOnlyList<string> Constraints,
    IReadOnlyList<string> Unknowns);

public sealed record EvidenceReference(
    EvidenceKind Kind,
    string RepositoryId,
    string Branch,
    string? EntityId,
    string? ProjectId,
    string? SourceEntityId,
    string? TargetEntityId,
    CodeRelationType? RelationType,
    string? RelativePath,
    int? StartLine,
    int? EndLine,
    ResolutionLevel? ResolutionLevel);

public sealed record PolicyRelevantAction(
    PolicyActionOperation Operation,
    PolicyActionBoundary Boundary,
    PolicyContentScope ContentScope,
    PolicyAuthorizationMode Authorization,
    string? TargetEntityId,
    string? TargetProjectId);

public sealed record AnalysisRecommendation(
    RecommendationDecision Decision,
    string Subject,
    string Reason,
    EpistemicStatus EpistemicStatus,
    IReadOnlyList<EvidenceReference> Evidence,
    IReadOnlyList<string> PotentialImpact,
    IReadOnlyList<string> Unknowns,
    IReadOnlyList<PolicyRelevantAction> PolicyRelevantActions);

public sealed record InitiativeAnalysis(
    InitiativeAnalysisStatus Status,
    string Summary,
    IReadOnlyList<string> RelevantProjectIds,
    IReadOnlyList<string> RelevantEntityIds,
    IReadOnlyList<AnalysisRecommendation> Recommendations,
    IReadOnlyList<string> Risks,
    IReadOnlyList<string> Unknowns,
    IReadOnlyList<string> ClarifyingQuestions,
    string OverallConfidenceExplanation);

public sealed record MatchReason(string Signal, string MatchedValue, int Points);

public sealed record ProjectCandidate(
    string ProjectId,
    string Name,
    string RelativePath,
    int Score,
    IReadOnlyList<MatchReason> MatchReasons);

public sealed record ComponentCandidate(
    string EntityId,
    string Name,
    string FullName,
    string ProjectId,
    string RelativePath,
    CodeEntityType EntityType,
    ResolutionLevel ResolutionLevel,
    int Score,
    bool GraphExpanded,
    IReadOnlyList<MatchReason> MatchReasons);

public sealed record CandidateGraphRelation(
    string SourceEntityId,
    string TargetEntityId,
    CodeRelationType RelationType,
    string RelativePath,
    int StartLine,
    int EndLine,
    ResolutionLevel ResolutionLevel);

public sealed record CandidateRetrievalResult(
    int ProjectsConsidered,
    int ComponentsConsidered,
    IReadOnlyList<ProjectCandidate> Projects,
    IReadOnlyList<ComponentCandidate> Components,
    IReadOnlyList<CandidateGraphRelation> Relations);

public sealed record ReasoningRequest(
    ReasoningStage Stage,
    string Model,
    string SystemInstructions,
    string UserData,
    int MaximumOutputTokens,
    int EstimatedInputTokens);

public sealed record ReasoningCallUsage(
    ReasoningStage Stage,
    string Provider,
    string Model,
    int EstimatedInputTokens,
    int? ActualInputTokens,
    int? CachedInputTokens,
    int? ActualOutputTokens,
    long DurationMilliseconds,
    int Retries,
    string? ReasoningEffort = null,
    int? ReasoningTokens = null);

public sealed record ReasoningResult<T>(T Value, ReasoningCallUsage Usage);

public sealed record ContextSegment(
    ContextSegmentKind Kind,
    string Content,
    int EstimatedTokens,
    string? SourceIdentity,
    int Rank,
    bool Mandatory);

public sealed record InitiativeContext(
    string Content,
    int EstimatedTokens,
    IReadOnlyList<string> IncludedNotePaths,
    IReadOnlyList<string> PrunedNotePaths,
    IReadOnlyList<ContextSegment> Segments);

public sealed record ValidatedRecommendation(
    AnalysisRecommendation Recommendation,
    EvidenceValidationStatus ValidationStatus,
    IReadOnlyList<EvidenceReference> ValidEvidence,
    IReadOnlyList<string> ValidationDiagnostics);

public sealed record PolicyProvenance(
    string Authority,
    string SourceReference,
    string? RepositoryId,
    string? Branch,
    string? Commit,
    string? RelativePath,
    string? ContentHash);

public sealed record PolicyComplianceResult(
    string PolicyId,
    int PolicyVersion,
    PolicySourceKind Source,
    PolicySeverity Severity,
    PolicyComplianceStatus ComplianceStatus,
    string Diagnostic,
    PolicyProvenance Provenance);

public sealed record GovernedRecommendation(
    ValidatedRecommendation ValidatedRecommendation,
    IReadOnlyList<PolicyComplianceResult> PolicyResults,
    RecommendationDisposition Disposition);

public sealed record PolicyGovernanceResult(
    IReadOnlyList<GovernedRecommendation> Recommendations,
    PolicyOutcome Outcome);

public sealed record InitiativeAnalysisUsage(
    IReadOnlyList<ReasoningCallUsage> Calls,
    int EstimatedInputTokens,
    int ActualInputTokens,
    int CachedInputTokens,
    int ActualOutputTokens);

public sealed record InitiativeAnalysisResult(
    string AnalysisId,
    string InitiativeFileName,
    string InitiativeContentHash,
    RepositoryInfo Repository,
    GitInfo Git,
    InitiativeUnderstanding Understanding,
    CandidateRetrievalResult Retrieval,
    InitiativeContext Context,
    InitiativeAnalysis Analysis,
    IReadOnlyList<GovernedRecommendation> Recommendations,
    PolicyOutcome PolicyOutcome,
    InitiativeAnalysisUsage Usage,
    string? SavedAnalysisPath,
    IReadOnlyList<OutboundPolicyAssessment> OutboundPolicyAssessments);

public sealed record InitiativeAnalysisRequest(
    string InitiativeFileName,
    string InitiativeText,
    ProjectMemorySyncResult Memory,
    string InterpretationModel,
    string ReasoningModel,
    bool PersistResult = true);
