using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ReviewedConceptSerializerTests
{
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
}
