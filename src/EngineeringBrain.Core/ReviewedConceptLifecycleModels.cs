namespace EngineeringBrain.Core;

public sealed record ReviewedConceptCatalogIdentity(
    string RepositoryId,
    string Branch,
    string BranchKey);

public sealed record ReviewedConceptLifecycleStatusResult(
    string RepositoryId,
    string RepositoryName,
    string Branch,
    string BranchKey,
    string CatalogPath,
    ReviewedConceptResolutionStatus Status,
    string? CatalogFingerprint,
    int? DeclarationCount,
    int? AssignmentCount,
    int ResolvedProfileCount,
    int InvalidOrStaleAssignmentCount,
    IReadOnlyList<ReviewedConceptDiagnostic> Diagnostics);

public enum ReviewedConceptPromotionOutcome
{
    Promoted,
    Unchanged,
    Blocked
}

public enum ReviewedConceptWriteOutcome
{
    Created,
    Updated,
    Unchanged
}

public sealed record ReviewedConceptWriteResult(
    ReviewedConceptWriteOutcome Outcome,
    string Path,
    string Fingerprint);

public sealed record ReviewedConceptPromotionResult(
    ReviewedConceptPromotionOutcome Outcome,
    string RepositoryId,
    string RepositoryName,
    string SourceBranch,
    string SourceBranchKey,
    string TargetBranch,
    string TargetBranchKey,
    string SourceCatalogPath,
    string TargetCatalogPath,
    string? SourceCatalogFingerprint,
    string? PreviousTargetCatalogFingerprint,
    string? NewTargetCatalogFingerprint,
    int DeclarationCount,
    int AssignmentCount,
    int ActiveProfileCount,
    int RecomputedAssignmentCount,
    int RecomputedDeclarationFingerprintCount,
    int ReboundSourceReferenceCount,
    int RejectedOrStaleAssignmentCount,
    IReadOnlyList<ReviewedConceptDiagnostic> Diagnostics);
