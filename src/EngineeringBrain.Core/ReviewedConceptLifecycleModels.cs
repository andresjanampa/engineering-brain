namespace EngineeringBrain.Core;

public sealed record ReviewedConceptCatalogIdentity(
    string RepositoryId,
    string Branch,
    string BranchKey);

public sealed record ReviewedConceptIdentityMapping(
    string OldEntityId,
    string NewEntityId);

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

public enum ReviewedConceptRemapOutcome
{
    Remapped,
    Blocked
}

public enum ReviewedConceptRefreshOutcome
{
    Refreshed,
    Unchanged,
    Blocked
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

public sealed record ReviewedConceptRemapResult(
    ReviewedConceptRemapOutcome Outcome,
    string RepositoryId,
    string RepositoryName,
    string Branch,
    string BranchKey,
    string CatalogPath,
    string? PreviousCatalogFingerprint,
    string? NewCatalogFingerprint,
    int MappingCount,
    int RemappedAssignmentCount,
    int DeclarationCount,
    int AssignmentCount,
    int ActiveProfileCount,
    int IdentityMigrationCount,
    int RemainingStaleAssignmentCount,
    IReadOnlyList<ReviewedConceptDiagnostic> Diagnostics);

public sealed record ReviewedConceptRefreshResult(
    ReviewedConceptRefreshOutcome Outcome,
    string RepositoryId,
    string RepositoryName,
    string Branch,
    string BranchKey,
    string CatalogPath,
    string? PreviousCatalogFingerprint,
    string? NewCatalogFingerprint,
    int DeclarationCount,
    int AssignmentCount,
    int ActiveProfileCount,
    int RecomputedAssignmentCount,
    int RecomputedDeclarationFingerprintCount,
    int ReboundSourceReferenceCount,
    int RejectedOrStaleAssignmentCount,
    IReadOnlyList<ReviewedConceptDiagnostic> Diagnostics);

public static class ReviewedConceptLifecycleExitCode
{
    public static int ForValidation(ReviewedConceptResolutionStatus status) => status switch
    {
        ReviewedConceptResolutionStatus.Valid => 0,
        ReviewedConceptResolutionStatus.ValidWithDiagnostics => 3,
        ReviewedConceptResolutionStatus.Invalid => 4,
        ReviewedConceptResolutionStatus.Absent => 5,
        _ => 4
    };

    public static int ForRemap(ReviewedConceptRemapOutcome outcome) => outcome switch
    {
        ReviewedConceptRemapOutcome.Remapped => 0,
        _ => 3
    };

    public static int ForRefresh(ReviewedConceptRefreshOutcome outcome) => outcome switch
    {
        ReviewedConceptRefreshOutcome.Refreshed => 0,
        ReviewedConceptRefreshOutcome.Unchanged => 0,
        _ => 3
    };
}
