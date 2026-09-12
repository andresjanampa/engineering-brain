using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ReviewedConceptVocabularyV2Tests
{
    [Fact]
    public void FrozenVocabularyV2Catalog_PreservesReviewedCountsAndPersistenceRoles()
    {
        var catalog = ReviewedConceptSerializer.Deserialize(
            File.ReadAllText(TestDataPath("reviewed-concepts-v2.json")));

        Assert.Equal(25, catalog.Declarations.Count);
        Assert.Equal(43, catalog.Declarations.Sum(item => item.Assignments.Count));
        Assert.Equal(29, catalog.Declarations.SelectMany(item => item.Assignments)
            .Select(item => item.EntityId).Distinct(StringComparer.Ordinal).Count());

        var persistence = catalog.Declarations.Single(item =>
            item.ConceptId == "initiative-analysis-persistence");
        Assert.Equal(["initiative"], Assert.Single(persistence.AnchorTokens));
        Assert.Equal(["persistence"], persistence.QualificationSupportTokens);
        Assert.Equal(["analysis"], persistence.ContextSupportTokens);
    }

    [Fact]
    public void FrozenVocabularyV2Catalog_ValidatesResolvesAndSerializesDeterministically()
    {
        var json = File.ReadAllText(TestDataPath("reviewed-concepts-v2.json"));
        var catalog = ReviewedConceptSerializer.Deserialize(json);
        var components = catalog.Declarations
            .SelectMany(item => item.Assignments)
            .GroupBy(item => item.EntityId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => Evidence(group.First()),
                StringComparer.Ordinal);
        var evidence = new ReviewedConceptEvidenceContext(
            catalog.RepositoryId,
            catalog.Branch,
            catalog.BranchKey,
            catalog.SourceSnapshotSchema,
            catalog.SourceAnalyzerVersion,
            components);

        var validation = new ReviewedConceptValidator().Validate(catalog, evidence);
        var resolution = new ReviewedConceptResolver().Resolve(
            new ReviewedConceptLoadResult(
                ReviewedConceptLoadStatus.Loaded,
                "reviewed-concepts.json",
                KnowledgeIdentity.ContentHash(json),
                catalog,
                []),
            validation,
            evidence);

        var mismatch = catalog.Declarations
            .Select(declaration => new
            {
                declaration.ConceptId,
                Stored = declaration.Fingerprint,
                Computed = ReviewedConceptSerializer.CreateDeclarationFingerprint(declaration)
            })
            .FirstOrDefault(item => !item.Stored.Equals(item.Computed, StringComparison.Ordinal));
        Assert.True(mismatch is null,
            mismatch is null ? string.Empty : $"{mismatch.ConceptId}: {mismatch.Stored} != {mismatch.Computed}");
        Assert.True(validation.CatalogIsValid);
        Assert.Empty(validation.Diagnostics);
        Assert.Equal(ReviewedConceptResolutionStatus.Valid, resolution.Status);
        Assert.Empty(resolution.Diagnostics);
        Assert.Equal(29, resolution.Profiles.Count);
        Assert.All(catalog.Declarations, declaration => Assert.Equal(
            declaration.Fingerprint,
            ReviewedConceptSerializer.CreateDeclarationFingerprint(declaration)));
        Assert.Equal(
            ReviewedConceptSerializer.Serialize(catalog),
            ReviewedConceptSerializer.Serialize(ReviewedConceptSerializer.Deserialize(
                ReviewedConceptSerializer.Serialize(catalog))));
    }

    private static ComponentFingerprintEvidence Evidence(ReviewedConceptAssignment assignment)
    {
        var separator = assignment.SourceReference.LastIndexOf(':');
        var path = assignment.SourceReference[..separator];
        var line = int.Parse(assignment.SourceReference[(separator + 1)..],
            System.Globalization.CultureInfo.InvariantCulture);
        return new ComponentFingerprintEvidence(
            assignment.EntityId,
            path,
            line,
            line,
            assignment.SourceFingerprint);
    }

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
