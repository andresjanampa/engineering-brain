using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ReviewedConceptValidatorTests
{
    [Theory]
    [InlineData("schema")]
    [InlineData("repository")]
    [InlineData("branch")]
    [InlineData("branch-key")]
    [InlineData("snapshot")]
    [InlineData("analyzer")]
    [InlineData("duplicate")]
    public void Validate_InvalidEnvelopeDisablesCatalog(string invalidField)
    {
        var evidence = ReviewedConceptTestData.Evidence();
        var catalog = ReviewedConceptTestData.Catalog();
        catalog = invalidField switch
        {
            "schema" => catalog with { SchemaVersion = 999 },
            "repository" => catalog with { RepositoryId = "repository:other" },
            "branch" => catalog with { Branch = "feature/other" },
            "branch-key" => catalog with { BranchKey = "other--key" },
            "snapshot" => catalog with { SourceSnapshotSchema = 999 },
            "analyzer" => catalog with { SourceAnalyzerVersion = "other-analyzer" },
            "duplicate" => catalog with { Declarations = [catalog.Declarations[0], catalog.Declarations[0]] },
            _ => throw new InvalidOperationException()
        };

        var result = new ReviewedConceptValidator().Validate(catalog, evidence);

        Assert.False(result.CatalogIsValid);
        Assert.Empty(result.Declarations);
        Assert.Contains(result.Diagnostics, item => item.Scope == ReviewedConceptDiagnosticScope.Catalog);
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("branch")]
    [InlineData("declarations")]
    public void Validate_NullEnvelopeMemberDisablesCatalogWithoutThrowing(string member)
    {
        var catalog = ReviewedConceptTestData.Catalog();
        catalog = member switch
        {
            "repository" => catalog with { RepositoryId = null! },
            "branch" => catalog with { Branch = null! },
            "declarations" => catalog with { Declarations = null! },
            _ => throw new InvalidOperationException()
        };

        var result = new ReviewedConceptValidator().Validate(catalog, ReviewedConceptTestData.Evidence());

        Assert.False(result.CatalogIsValid);
        Assert.Empty(result.Declarations);
        Assert.Contains(result.Diagnostics, item => item.Scope == ReviewedConceptDiagnosticScope.Catalog);
    }

    [Fact]
    public void Validate_IncompleteDeclarationIsExcludedWhileValidSiblingRemains()
    {
        var valid = ReviewedConceptTestData.WithFingerprint(
            ReviewedConceptTestData.Declaration("valid-concept", "entity:business-service") with
            {
                AnchorPolicy = ReviewedConceptAnchorPolicy.NotRequired,
                AnchorTokens = [],
                QualificationSupportTokens = []
            });
        var incomplete = ReviewedConceptTestData.Declaration("incomplete-concept", "entity:model") with
        {
            AnchorTokens = null!,
            Assignments = null!,
            Provenance = null!,
            Review = null!
        };

        var result = new ReviewedConceptValidator().Validate(
            ReviewedConceptTestData.Catalog() with { Declarations = [valid, incomplete] },
            ReviewedConceptTestData.Evidence());

        Assert.True(result.CatalogIsValid);
        Assert.Single(result.Declarations, item => item.ConceptId == "valid-concept");
        Assert.Contains(result.Diagnostics,
            item => item.Scope == ReviewedConceptDiagnosticScope.Declaration
                && item.ConceptId == "incomplete-concept");
    }

    [Fact]
    public void Validate_InvalidDeclarationAndAssignmentAreLocalized()
    {
        var evidence = ReviewedConceptTestData.Evidence();
        var valid = ReviewedConceptTestData.WithFingerprint(
            ReviewedConceptTestData.Declaration("valid-concept", "entity:business-service") with
            {
                AnchorPolicy = ReviewedConceptAnchorPolicy.NotRequired,
                AnchorTokens = [],
                QualificationSupportTokens = []
            });
        var invalidDeclaration = ReviewedConceptTestData.WithFingerprint(
            ReviewedConceptTestData.Declaration("broken-concept", "entity:model")) with
            { Definition = " " };
        var mixed = ReviewedConceptTestData.WithFingerprint(
            ReviewedConceptTestData.Declaration("mixed-concept", "entity:business-service") with
            {
                AnchorPolicy = ReviewedConceptAnchorPolicy.NotRequired,
                AnchorTokens = [],
                QualificationSupportTokens = [],
                Assignments =
                [
                    ReviewedConceptTestData.Assignment("entity:business-service"),
                    new ReviewedConceptAssignment("", "src/invalid.cs:1", "fingerprint")
                ]
            });

        var result = new ReviewedConceptValidator().Validate(
            ReviewedConceptTestData.Catalog() with
            {
                Declarations = [valid, invalidDeclaration, mixed]
            },
            evidence);

        Assert.True(result.CatalogIsValid);
        Assert.Contains(result.Declarations, item => item.ConceptId == "valid-concept");
        Assert.DoesNotContain(result.Declarations, item => item.ConceptId == "broken-concept");
        Assert.Single(result.Declarations.Single(item => item.ConceptId == "mixed-concept").Assignments);
        Assert.Contains(result.Diagnostics, item => item.Scope == ReviewedConceptDiagnosticScope.Declaration);
        Assert.Contains(result.Diagnostics, item => item.Scope == ReviewedConceptDiagnosticScope.Assignment);
    }

    [Fact]
    public void Validate_ClearAnchorRequiresQualificationSupport()
    {
        var evidence = ReviewedConceptTestData.Evidence();
        var declaration = ReviewedConceptTestData.WithFingerprint(
            ReviewedConceptTestData.Declaration("initiative-analysis-persistence", "entity:model") with
            {
                AnchorTokens = [["initiative"]],
                QualificationSupportTokens = [],
                ContextSupportTokens = ["analysis"]
            });

        var result = new ReviewedConceptValidator().Validate(
            ReviewedConceptTestData.Catalog() with { Declarations = [declaration] },
            evidence);

        Assert.True(result.CatalogIsValid);
        Assert.Empty(result.Declarations);
        Assert.Contains(result.Diagnostics, item => item.Scope == ReviewedConceptDiagnosticScope.Declaration);
    }

    [Fact]
    public void Validate_NormalizedTokenAndAbsoluteAssignmentAreRejectedLocally()
    {
        var evidence = ReviewedConceptTestData.Evidence();
        var declaration = ReviewedConceptTestData.WithFingerprint(
            ReviewedConceptTestData.Declaration("provider-boundary", "entity:business-service") with
            {
                AnchorTokens = [["Provider"]]
            });
        var assignmentDeclaration = ReviewedConceptTestData.WithFingerprint(
            ReviewedConceptTestData.Declaration("valid-concept", "entity:model") with
            {
                AnchorPolicy = ReviewedConceptAnchorPolicy.NotRequired,
                AnchorTokens = [],
                QualificationSupportTokens = [],
                Assignments =
                [
                    ReviewedConceptTestData.Assignment("entity:model"),
                    new ReviewedConceptAssignment("entity:service", "C:\\source.cs:1", "fingerprint")
                ]
            });

        var result = new ReviewedConceptValidator().Validate(
            ReviewedConceptTestData.Catalog() with
            {
                Declarations = [declaration, assignmentDeclaration]
            },
            evidence);

        Assert.DoesNotContain(result.Declarations, item => item.ConceptId == "provider-boundary");
        Assert.Single(result.Declarations.Single(item => item.ConceptId == "valid-concept").Assignments);
    }
}
