using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ReviewedConceptResolverTests
{
    [Fact]
    public void Resolve_AbsentLoadReturnsAbsent()
    {
        var load = new ReviewedConceptLoadResult(
            ReviewedConceptLoadStatus.Absent,
            "reviewed-concepts.json",
            null,
            null,
            []);

        var result = new ReviewedConceptResolver().Resolve(
            load,
            new ReviewedConceptValidationResult(false, [], []),
            ReviewedConceptTestData.Evidence());

        Assert.Equal(ReviewedConceptResolutionResult.Absent, result);
    }

    [Fact]
    public void Resolve_InvalidLoadOrEnvelopeReturnsInvalidWithoutProfiles()
    {
        var diagnostic = new ReviewedConceptDiagnostic(
            "RC001",
            AnalysisDiagnosticSeverity.Error,
            ReviewedConceptDiagnosticScope.Catalog,
            "Catalog invalid.");
        var load = new ReviewedConceptLoadResult(
            ReviewedConceptLoadStatus.Invalid,
            "reviewed-concepts.json",
            null,
            null,
            [diagnostic]);

        var result = new ReviewedConceptResolver().Resolve(
            load,
            new ReviewedConceptValidationResult(false, [], []),
            ReviewedConceptTestData.Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.Invalid, result.Status);
        Assert.Empty(result.Profiles);
        Assert.Contains(diagnostic, result.Diagnostics);
    }

    [Fact]
    public void Resolve_StaleAssignmentIsExcludedWhileValidSiblingRemainsActive()
    {
        var evidence = ReviewedConceptTestData.Evidence();
        var declaration = ReviewedConceptTestData.WithFingerprint(
            ReviewedConceptTestData.Declaration("provider-boundary", "entity:business-service") with
            {
                Assignments =
                [
                    ReviewedConceptTestData.Assignment("entity:business-service"),
                    ReviewedConceptTestData.Assignment("entity:model") with
                    {
                        SourceFingerprint = "stale"
                    }
                ]
            });
        var catalog = ReviewedConceptTestData.Catalog() with { Declarations = [declaration] };
        var validation = new ReviewedConceptValidator().Validate(catalog, evidence);

        var result = new ReviewedConceptResolver().Resolve(
            ReviewedConceptTestData.Loaded(catalog), validation, evidence);

        Assert.Equal(ReviewedConceptResolutionStatus.ValidWithDiagnostics, result.Status);
        Assert.Single(result.Profiles, item => item.EntityId == "entity:business-service");
        Assert.DoesNotContain(result.Profiles, item => item.EntityId == "entity:model");
        Assert.Contains(result.Diagnostics, item => item.Code == "RC402" && item.EntityId == "entity:model");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("path")]
    public void Resolve_MissingOrPathMismatchedAssignmentIsLocalized(string failure)
    {
        var evidence = ReviewedConceptTestData.Evidence();
        var invalid = failure == "missing"
            ? new ReviewedConceptAssignment("entity:missing", "src/Missing.cs:1", "fingerprint")
            : ReviewedConceptTestData.Assignment("entity:model") with { SourceReference = "src/Other.cs:1" };
        var declaration = ReviewedConceptTestData.WithFingerprint(
            ReviewedConceptTestData.Declaration("provider-boundary", "entity:business-service") with
            {
                Assignments = [ReviewedConceptTestData.Assignment("entity:business-service"), invalid]
            });
        var catalog = ReviewedConceptTestData.Catalog() with { Declarations = [declaration] };
        var validation = new ReviewedConceptValidator().Validate(catalog, evidence);

        var result = new ReviewedConceptResolver().Resolve(
            ReviewedConceptTestData.Loaded(catalog), validation, evidence);

        Assert.Equal(ReviewedConceptResolutionStatus.ValidWithDiagnostics, result.Status);
        Assert.Single(result.Profiles);
        Assert.Equal("entity:business-service", result.Profiles[0].EntityId);
        Assert.Contains(result.Diagnostics, item => item.Code == (failure == "missing" ? "RC400" : "RC401"));
    }

    [Fact]
    public void Resolve_ValidCatalogProducesStableOrderedProfiles()
    {
        var evidence = ReviewedConceptTestData.Evidence();
        var catalog = ReviewedConceptTestData.Catalog(reversed: true);
        var validation = new ReviewedConceptValidator().Validate(catalog, evidence);

        var result = new ReviewedConceptResolver().Resolve(
            ReviewedConceptTestData.Loaded(catalog), validation, evidence);

        Assert.Equal(ReviewedConceptResolutionStatus.Valid, result.Status);
        Assert.Equal(
            result.Profiles.OrderBy(item => item.EntityId, StringComparer.Ordinal).Select(item => item.EntityId),
            result.Profiles.Select(item => item.EntityId));
        Assert.All(result.Profiles, item => Assert.NotEmpty(item.Concepts));
    }
}
