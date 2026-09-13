using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ReviewedConceptValidatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ValidateIntegrity_SchemaOneAndSchemaTwoAreSupported(int schemaVersion)
    {
        var catalog = ReviewedConceptTestData.Catalog() with { SchemaVersion = schemaVersion };
        if (schemaVersion == 2)
        {
            catalog = catalog with { IdentityMigrations = [WithFingerprint(Migration())] };
        }

        var result = new ReviewedConceptValidator().ValidateIntegrity(
            catalog,
            new ReviewedConceptCatalogIdentity(catalog.RepositoryId, catalog.Branch, catalog.BranchKey));

        Assert.True(result.CatalogIsValid);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("missing-old")]
    [InlineData("missing-new")]
    [InlineData("same-identity")]
    [InlineData("missing-reviewer")]
    [InlineData("missing-previous-fingerprint")]
    [InlineData("absolute-destination-reference")]
    [InlineData("bad-fingerprint")]
    public void ValidateIntegrity_InvalidIdentityMigrationIsCatalogInvalid(string failure)
    {
        var migration = WithFingerprint(Migration());
        migration = failure switch
        {
            "missing-old" => migration with { OldEntityId = null! },
            "missing-new" => migration with { NewEntityId = null! },
            "same-identity" => migration with { NewEntityId = migration.OldEntityId },
            "missing-reviewer" => migration with { Review = migration.Review with { Reviewer = null! } },
            "missing-previous-fingerprint" => migration with { PreviousCatalogFingerprint = null! },
            "absolute-destination-reference" => migration with { DestinationSourceReference = "C:\\source.cs:1" },
            "bad-fingerprint" => migration with { Fingerprint = "wrong" },
            _ => throw new InvalidOperationException()
        };
        var catalog = ReviewedConceptTestData.Catalog() with
        {
            SchemaVersion = 2,
            IdentityMigrations = [migration]
        };

        var result = new ReviewedConceptValidator().ValidateIntegrity(
            catalog,
            new ReviewedConceptCatalogIdentity(catalog.RepositoryId, catalog.Branch, catalog.BranchKey));

        Assert.False(result.CatalogIsValid);
        Assert.Empty(result.Declarations);
        Assert.Contains(result.Diagnostics, item => item.Code == "RC111");
        Assert.All(result.Diagnostics, item => Assert.Single(item.Message.Split('\n')));
    }

    [Fact]
    public void ValidateIntegrity_DuplicateMigrationFingerprintIsInvalid()
    {
        var migration = WithFingerprint(Migration());
        var catalog = ReviewedConceptTestData.Catalog() with
        {
            SchemaVersion = 2,
            IdentityMigrations = [migration, migration]
        };

        var result = new ReviewedConceptValidator().ValidateIntegrity(
            catalog,
            new ReviewedConceptCatalogIdentity(catalog.RepositoryId, catalog.Branch, catalog.BranchKey));

        Assert.False(result.CatalogIsValid);
        Assert.Contains(result.Diagnostics, item => item.Code == "RC112");
    }

    [Fact]
    public void ValidateIntegrity_UnsortedAffectedConceptsCanonicalizeWithoutChangingMeaning()
    {
        var first = WithFingerprint(Migration(["concept-a", "concept-b"]));
        var second = WithFingerprint(Migration(["concept-b", "concept-a"]));
        var catalog = ReviewedConceptTestData.Catalog() with
        {
            SchemaVersion = 2,
            IdentityMigrations = [second]
        };

        var result = new ReviewedConceptValidator().ValidateIntegrity(
            catalog,
            new ReviewedConceptCatalogIdentity(catalog.RepositoryId, catalog.Branch, catalog.BranchKey));

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.True(result.CatalogIsValid);
        Assert.Empty(result.Diagnostics);
    }

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

    private static ReviewedConceptIdentityMigration Migration(
        IReadOnlyList<string>? affectedConceptIds = null)
    {
        var catalog = ReviewedConceptTestData.Catalog();
        var evidence = ReviewedConceptTestData.Evidence();
        var component = evidence.Components["entity:business-service"];
        return new ReviewedConceptIdentityMigration(
            catalog.RepositoryId,
            catalog.Branch,
            catalog.BranchKey,
            "entity:old",
            component.EntityId,
            affectedConceptIds ?? [catalog.Declarations[0].ConceptId],
            "previous-catalog",
            $"{component.RelativePath}:{component.StartLine}",
            component.SourceFingerprint,
            new ReviewedConceptReview(
                "reviewer",
                1,
                new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero)),
            "pending");
    }

    private static ReviewedConceptIdentityMigration WithFingerprint(
        ReviewedConceptIdentityMigration migration) => migration with
        {
            Fingerprint = ReviewedConceptSerializer.CreateIdentityMigrationFingerprint(migration)
        };
}
