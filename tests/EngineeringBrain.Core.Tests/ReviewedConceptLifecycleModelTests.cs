using EngineeringBrain.Core;

namespace EngineeringBrain.Core.Tests;

public sealed class ReviewedConceptLifecycleModelTests
{
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
