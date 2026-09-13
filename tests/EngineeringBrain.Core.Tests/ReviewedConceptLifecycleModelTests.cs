using EngineeringBrain.Core;

namespace EngineeringBrain.Core.Tests;

public sealed class ReviewedConceptLifecycleModelTests
{
    [Theory]
    [InlineData(ReviewedConceptRemapOutcome.Remapped, 0)]
    [InlineData(ReviewedConceptRemapOutcome.Blocked, 3)]
    public void RemapExitCode_IsStable(ReviewedConceptRemapOutcome outcome, int expected)
    {
        Assert.Equal(expected, ReviewedConceptLifecycleExitCode.ForRemap(outcome));
    }

    [Theory]
    [InlineData(ReviewedConceptRefreshOutcome.Refreshed, 0)]
    [InlineData(ReviewedConceptRefreshOutcome.Unchanged, 0)]
    [InlineData(ReviewedConceptRefreshOutcome.Blocked, 3)]
    public void RefreshExitCode_IsStable(ReviewedConceptRefreshOutcome outcome, int expected)
    {
        Assert.Equal(expected, ReviewedConceptLifecycleExitCode.ForRefresh(outcome));
    }

    [Fact]
    public void RemapResult_CarriesAuditFingerprintsAndCounts()
    {
        var result = new ReviewedConceptRemapResult(
            ReviewedConceptRemapOutcome.Remapped,
            "repository", "engineering-brain", "main", "main--key", "catalog.json",
            "previous", "next", 2, 3, 25, 43, 29, 2, 0, []);

        Assert.Equal(2, result.MappingCount);
        Assert.Equal(3, result.RemappedAssignmentCount);
        Assert.Equal(2, result.IdentityMigrationCount);
        Assert.Equal("next", result.NewCatalogFingerprint);
    }

    [Fact]
    public void RefreshResult_CarriesRebindingFingerprintsAndCounts()
    {
        var result = new ReviewedConceptRefreshResult(
            ReviewedConceptRefreshOutcome.Refreshed,
            "repository", "engineering-brain", "main", "main--key", "catalog.json",
            "previous", "next", 25, 43, 29, 43, 25, 7, 0, []);

        Assert.Equal(43, result.RecomputedAssignmentCount);
        Assert.Equal(25, result.RecomputedDeclarationFingerprintCount);
        Assert.Equal(7, result.ReboundSourceReferenceCount);
        Assert.Equal("next", result.NewCatalogFingerprint);
    }

    [Theory]
    [InlineData(ReviewedConceptResolutionStatus.Valid, 0)]
    [InlineData(ReviewedConceptResolutionStatus.ValidWithDiagnostics, 3)]
    [InlineData(ReviewedConceptResolutionStatus.Invalid, 4)]
    [InlineData(ReviewedConceptResolutionStatus.Absent, 5)]
    [InlineData(ReviewedConceptResolutionStatus.Unknown, 4)]
    public void ValidationExitCode_IsStable(
        ReviewedConceptResolutionStatus status,
        int expected)
    {
        Assert.Equal(expected, ReviewedConceptLifecycleExitCode.ForValidation(status));
    }

    [Fact]
    public void PromotionResult_CarriesSourceTargetFingerprintsAndCounts()
    {
        var result = new ReviewedConceptPromotionResult(
            ReviewedConceptPromotionOutcome.Promoted,
            "repository", "engineering-brain",
            "feature/source", "feature-source--key",
            "main", "main--key",
            "source.json", "target.json",
            "source-fingerprint", "previous-fingerprint", "new-fingerprint",
            25, 43, 29, 43, 25, 7, 0, []);

        Assert.Equal(ReviewedConceptPromotionOutcome.Promoted, result.Outcome);
        Assert.Equal(43, result.RecomputedAssignmentCount);
        Assert.Equal(0, result.RejectedOrStaleAssignmentCount);
        Assert.Equal("new-fingerprint", result.NewTargetCatalogFingerprint);
    }

    [Theory]
    [InlineData(ReviewedConceptWriteOutcome.Created)]
    [InlineData(ReviewedConceptWriteOutcome.Updated)]
    [InlineData(ReviewedConceptWriteOutcome.Unchanged)]
    public void WriteResult_ExposesOnlySuccessfulOutcomes(ReviewedConceptWriteOutcome outcome)
    {
        var result = new ReviewedConceptWriteResult(outcome, "reviewed-concepts.json", "fingerprint");

        Assert.Equal(outcome, result.Outcome);
    }

    [Fact]
    public void StatusResult_UsesZeroCountsForAbsentCatalog()
    {
        var result = CreateStatus(
            ReviewedConceptResolutionStatus.Absent,
            declarationCount: 0,
            assignmentCount: 0);

        Assert.Equal(0, result.DeclarationCount);
        Assert.Equal(0, result.AssignmentCount);
        Assert.Equal(0, result.ResolvedProfileCount);
    }

    [Fact]
    public void StatusResult_UsesUnknownCountsForUnreadableCatalog()
    {
        var result = CreateStatus(
            ReviewedConceptResolutionStatus.Invalid,
            declarationCount: null,
            assignmentCount: null);

        Assert.Null(result.DeclarationCount);
        Assert.Null(result.AssignmentCount);
    }

    private static ReviewedConceptLifecycleStatusResult CreateStatus(
        ReviewedConceptResolutionStatus status,
        int? declarationCount,
        int? assignmentCount) => new(
        "repository",
        "engineering-brain",
        "main",
        "main--key",
        "reviewed-concepts.json",
        status,
        null,
        declarationCount,
        assignmentCount,
        0,
        0,
        []);
}
