using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ReviewedConceptSerializerTests
{
    [Fact]
    public void Deserialize_SchemaOneDefaultsToEmptyIdentityMigrationLedger()
    {
        var restored = ReviewedConceptSerializer.Deserialize(
            ReviewedConceptSerializer.Serialize(ReviewedConceptTestData.Catalog()));

        Assert.Empty(restored.IdentityMigrations);
    }

    [Fact]
    public void Serialize_SchemaOneRemainsByteCompatibleWithExistingFixture()
    {
        var original = File.ReadAllText(TestDataPath("reviewed-concepts-v2.json"));
        var normalized = KnowledgeIdentity.NormalizeLineEndings(original).TrimEnd('\n') + "\n";

        var restored = ReviewedConceptSerializer.Deserialize(original);

        Assert.Equal(normalized, ReviewedConceptSerializer.Serialize(restored));
        Assert.DoesNotContain("identityMigrations", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaTwo_RoundTripsCanonicalIdentityMigrationLedger()
    {
        var migration = WithFingerprint(Migration(["concept-b", "concept-a"]));
        var catalog = ReviewedConceptTestData.Catalog() with
        {
            SchemaVersion = 2,
            IdentityMigrations = [migration]
        };

        var serialized = ReviewedConceptSerializer.Serialize(catalog);
        var restored = ReviewedConceptSerializer.Deserialize(serialized);

        Assert.Equal(serialized, ReviewedConceptSerializer.Serialize(restored));
        Assert.Equal(["concept-a", "concept-b"], restored.IdentityMigrations[0].AffectedConceptIds);
        Assert.Equal(migration.Fingerprint, restored.IdentityMigrations[0].Fingerprint);
    }

    [Fact]
    public void IdentityMigrationFingerprint_IsStableAcrossAffectedConceptOrder()
    {
        var first = Migration(["concept-a", "concept-b"]);
        var second = Migration(["concept-b", "concept-a"]);

        Assert.Equal(
            ReviewedConceptSerializer.CreateIdentityMigrationFingerprint(first),
            ReviewedConceptSerializer.CreateIdentityMigrationFingerprint(second));
    }

    [Fact]
    public void IdentityMigrationFingerprint_ChangesForDecisionReviewerOrEvidenceChange()
    {
        var migration = Migration(["concept-a"]);
        var fingerprint = ReviewedConceptSerializer.CreateIdentityMigrationFingerprint(migration);

        Assert.NotEqual(fingerprint, ReviewedConceptSerializer.CreateIdentityMigrationFingerprint(
            migration with { OldEntityId = "entity:other-old" }));
        Assert.NotEqual(fingerprint, ReviewedConceptSerializer.CreateIdentityMigrationFingerprint(
            migration with { NewEntityId = "entity:other-new" }));
        Assert.NotEqual(fingerprint, ReviewedConceptSerializer.CreateIdentityMigrationFingerprint(
            migration with { Review = migration.Review with { Reviewer = "other-reviewer" } }));
        Assert.NotEqual(fingerprint, ReviewedConceptSerializer.CreateIdentityMigrationFingerprint(
            migration with { DestinationSourceFingerprint = "other-source" }));
    }

    [Fact]
    public void CreateCatalogFingerprint_IsCanonicalAndDeterministic()
    {
        var catalog = ReviewedConceptTestData.Catalog();
        var expected = KnowledgeIdentity.ContentHash(ReviewedConceptSerializer.Serialize(catalog));

        Assert.Equal(expected, ReviewedConceptSerializer.CreateCatalogFingerprint(catalog));
    }

    [Fact]
    public void Serialize_IsDeterministicAcrossInputOrderingAndLineEndings()
    {
        var first = ReviewedConceptTestData.Catalog(reversed: false);
        var second = ReviewedConceptTestData.Catalog(reversed: true) with
        {
            Declarations = ReviewedConceptTestData.Catalog(reversed: true).Declarations
                .Select(item => item with { Definition = item.Definition.Replace("\n", "\r\n") })
                .ToArray()
        };

        Assert.Equal(
            ReviewedConceptSerializer.Serialize(first),
            ReviewedConceptSerializer.Serialize(second));
    }

    [Fact]
    public void DeclarationFingerprint_ExcludesStoredFingerprintAndIncludesReviewAndAssignments()
    {
        var declaration = ReviewedConceptTestData.Declaration();
        var first = ReviewedConceptSerializer.CreateDeclarationFingerprint(
            declaration with { Fingerprint = "ignored-a" });
        var second = ReviewedConceptSerializer.CreateDeclarationFingerprint(
            declaration with { Fingerprint = "ignored-b" });
        var changed = ReviewedConceptSerializer.CreateDeclarationFingerprint(
            declaration with
            {
                Review = declaration.Review with { Version = declaration.Review.Version + 1 }
            });

        Assert.Equal(first, second);
        Assert.NotEqual(first, changed);
    }

    [Fact]
    public void DeclarationFingerprint_PreservesFieldBoundariesWhenMetadataContainsNewlines()
    {
        var declaration = ReviewedConceptTestData.Declaration();
        var first = declaration with
        {
            Provenance = new ReviewedConceptProvenance("source-a\nsource-b", "source-c")
        };
        var second = declaration with
        {
            Provenance = new ReviewedConceptProvenance("source-a", "source-b\nsource-c")
        };

        var firstFingerprint = ReviewedConceptSerializer.CreateDeclarationFingerprint(first);
        var secondFingerprint = ReviewedConceptSerializer.CreateDeclarationFingerprint(second);

        Assert.NotEqual(firstFingerprint, secondFingerprint);
    }

    [Fact]
    public void Deserialize_MalformedJsonThrowsSafeInvalidDataException()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            ReviewedConceptSerializer.Deserialize("{not-json"));

        Assert.Equal("Reviewed concept JSON is invalid.", exception.Message);
    }

    [Fact]
    public void Serialize_RoundTripsAllFieldsWithCanonicalLineEndings()
    {
        var serialized = ReviewedConceptSerializer.Serialize(ReviewedConceptTestData.Catalog());
        var restored = ReviewedConceptSerializer.Deserialize(serialized);

        Assert.Equal(serialized, ReviewedConceptSerializer.Serialize(restored));
        Assert.DoesNotContain("\r", serialized, StringComparison.Ordinal);
        Assert.EndsWith("\n", serialized, StringComparison.Ordinal);
        Assert.False(serialized.EndsWith("\n\n", StringComparison.Ordinal));
    }

    private static ReviewedConceptIdentityMigration Migration(IReadOnlyList<string> affectedConceptIds) => new(
        "repository",
        "main",
        "main--key",
        "entity:old",
        "entity:new",
        affectedConceptIds,
        "previous-catalog",
        "src/New.cs:10",
        "new-source-fingerprint",
        new ReviewedConceptReview(
            "reviewer",
            1,
            new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero)),
        "pending");

    private static ReviewedConceptIdentityMigration WithFingerprint(
        ReviewedConceptIdentityMigration migration) => migration with
        {
            Fingerprint = ReviewedConceptSerializer.CreateIdentityMigrationFingerprint(migration)
        };

    private static string TestDataPath(string fileName)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "EngineeringBrain.sln")))
        {
            current = current.Parent;
        }

        return Path.Combine(
            current!.FullName,
            "tests",
            "EngineeringBrain.Core.Tests",
            "TestData",
            fileName);
    }
}
