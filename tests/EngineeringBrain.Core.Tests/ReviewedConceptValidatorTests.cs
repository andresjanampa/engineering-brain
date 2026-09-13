using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ReviewedConceptValidatorTests
{
    [Fact]
    public void ValidateIntegrity_UsesExpectedSourceIdentityWithoutResolvingSourceCode()
    {
        var catalog = ReviewedConceptTestData.Catalog() with
        {
            SourceSnapshotSchema = 1,
            SourceAnalyzerVersion = "historical-analyzer"
        };
        var evidence = ReviewedConceptTestData.Evidence();
        var identity = new ReviewedConceptCatalogIdentity(
            evidence.RepositoryId, catalog.Branch, catalog.BranchKey);

        var result = new ReviewedConceptValidator().ValidateIntegrity(catalog, identity);

        Assert.True(result.CatalogIsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("repository", "RC101")]
    [InlineData("branch", "RC102")]
    [InlineData("branch-key", "RC103")]
    [InlineData("snapshot", "RC109")]
    [InlineData("analyzer", "RC110")]
    [InlineData("duplicate", "RC107")]
    public void ValidateIntegrity_InvalidArtifactIdentityOrStructureDisablesCatalog(
        string invalidField,
        string expectedCode)
    {
        var catalog = ReviewedConceptTestData.Catalog();
        var evidence = ReviewedConceptTestData.Evidence();
        var identity = new ReviewedConceptCatalogIdentity(
            evidence.RepositoryId, catalog.Branch, catalog.BranchKey);
        catalog = invalidField switch
        {
            "repository" => catalog with { RepositoryId = "repository:other" },
            "branch" => catalog with { Branch = "feature/other" },
            "branch-key" => catalog with { BranchKey = "other--key" },
            "snapshot" => catalog with { SourceSnapshotSchema = 0 },
            "analyzer" => catalog with { SourceAnalyzerVersion = " " },
            "duplicate" => catalog with { Declarations = [catalog.Declarations[0], catalog.Declarations[0]] },
            _ => throw new InvalidOperationException()
        };

        var result = new ReviewedConceptValidator().ValidateIntegrity(catalog, identity);

        Assert.False(result.CatalogIsValid);
        Assert.Empty(result.Declarations);
        Assert.Contains(result.Diagnostics, item => item.Code == expectedCode);
    }

    [Fact]
    public void ValidateIntegrity_IncompleteDeclarationIsExcludedWithoutFingerprinting()
    {
        var catalog = ReviewedConceptTestData.Catalog();
        var valid = catalog.Declarations[0];
        var incomplete = catalog.Declarations[1] with
        {
            Provenance = null!,
            Review = null!,
            Assignments = [null!]
        };
        var identity = new ReviewedConceptCatalogIdentity(
            catalog.RepositoryId, catalog.Branch, catalog.BranchKey);

        var result = new ReviewedConceptValidator().ValidateIntegrity(
            catalog with { Declarations = [valid, incomplete] },
            identity);

        Assert.True(result.CatalogIsValid);
        Assert.Single(result.Declarations);
        Assert.Equal(valid.ConceptId, result.Declarations[0].ConceptId);
        Assert.Contains(result.Diagnostics, item => item.Code == "RC208");
    }

    [Fact]
    public void Validate_CurrentEvidenceStillRejectsSchemaAndAnalyzerMismatch()
    {
        var catalog = ReviewedConceptTestData.Catalog() with
        {
            SourceSnapshotSchema = 1,
            SourceAnalyzerVersion = "historical-analyzer"
        };

        var result = new ReviewedConceptValidator().Validate(
            catalog,
            ReviewedConceptTestData.Evidence());

        Assert.False(result.CatalogIsValid);
        Assert.Contains(result.Diagnostics, item => item.Code == "RC104");
        Assert.Contains(result.Diagnostics, item => item.Code == "RC105");
    }

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

    [Theory]
    [InlineData("provenance-reference")]
    [InlineData("provenance-hash")]
    [InlineData("reviewer")]
    [InlineData("assignment-entry")]
    [InlineData("assignment-entity")]
    [InlineData("assignment-source")]
    [InlineData("assignment-fingerprint")]
    public void Validate_NullNestedDeclarationMemberIsExcludedWithoutThrowing(string member)
    {
        var valid = ReviewedConceptTestData.WithFingerprint(
            ReviewedConceptTestData.Declaration("valid-concept", "entity:business-service") with
            {
                AnchorPolicy = ReviewedConceptAnchorPolicy.NotRequired,
                AnchorTokens = [],
                QualificationSupportTokens = []
            });
        var incomplete = ReviewedConceptTestData.Declaration("nested-incomplete", "entity:model");
        incomplete = member switch
        {
            "provenance-reference" => incomplete with
            {
                Provenance = incomplete.Provenance with { SourceReference = null! }
            },
            "provenance-hash" => incomplete with
            {
                Provenance = incomplete.Provenance with { SourceHash = null! }
            },
            "reviewer" => incomplete with
            {
                Review = incomplete.Review with { Reviewer = null! }
            },
            "assignment-entry" => incomplete with { Assignments = [null!] },
            "assignment-entity" => incomplete with
            {
                Assignments = [incomplete.Assignments[0] with { EntityId = null! }]
            },
            "assignment-source" => incomplete with
            {
                Assignments = [incomplete.Assignments[0] with { SourceReference = null! }]
            },
            "assignment-fingerprint" => incomplete with
            {
                Assignments = [incomplete.Assignments[0] with { SourceFingerprint = null! }]
            },
            _ => throw new InvalidOperationException()
        };

        var result = new ReviewedConceptValidator().Validate(
            ReviewedConceptTestData.Catalog() with { Declarations = [valid, incomplete] },
            ReviewedConceptTestData.Evidence());

        Assert.True(result.CatalogIsValid);
        Assert.Single(result.Declarations, item => item.ConceptId == "valid-concept");
        Assert.Contains(result.Diagnostics, item => item.Code == "RC208"
            && item.Scope == ReviewedConceptDiagnosticScope.Declaration
            && item.ConceptId == "nested-incomplete");
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
