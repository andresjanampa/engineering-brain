using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

internal static class ReviewedConceptTestData
{
    public static ReviewedConceptCatalog Catalog(bool reversed = false)
    {
        var evidence = Evidence();
        var declarations = new[]
        {
            WithFingerprint(Declaration("provider-boundary", "entity:business-service")),
            WithFingerprint(Declaration("initiative-analysis-persistence", "entity:model"))
        };
        return new ReviewedConceptCatalog(
            1,
            evidence.RepositoryId,
            evidence.Branch,
            evidence.BranchKey,
            evidence.SourceSnapshotSchema,
            evidence.SourceAnalyzerVersion,
            "v2",
            reversed ? declarations.Reverse().ToArray() : declarations);
    }

    public static ReviewedConceptDeclaration Declaration(
        string conceptId = "provider-boundary",
        string entityId = "entity:business-service") => new(
        conceptId,
        $"Reviewed definition for {conceptId}.\nSecond line.",
        ReviewedConceptAnchorPolicy.Clear,
        [[conceptId.Split('-')[0]]],
        [conceptId.Split('-')[^1]],
        [],
        [Assignment(entityId)],
        new ReviewedConceptProvenance("research/v2.json", "source-hash"),
        new ReviewedConceptReview(
            "engineering-brain-reviewed-research",
            1,
            new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero)),
        "pending");

    public static ReviewedConceptAssignment Assignment(string entityId) => new(
        entityId,
        $"{Evidence().Components[entityId].RelativePath}:{Evidence().Components[entityId].StartLine}",
        Evidence().Components[entityId].SourceFingerprint);

    public static ReviewedConceptDeclaration WithFingerprint(ReviewedConceptDeclaration declaration) =>
        declaration with
        {
            Fingerprint = ReviewedConceptSerializer.CreateDeclarationFingerprint(declaration)
        };

    public static ReviewedConceptEvidenceContext Evidence()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var build = new ProjectMemoryBuilder().Build(snapshot);
        return ReviewedConceptEvidenceContext.FromMemory(Memory(snapshot, build.Manifest));
    }

    public static ProjectMemorySyncResult Memory(
        RepositorySnapshot snapshot,
        ProjectMemoryManifest manifest) => new(
        ProjectMemorySyncMode.Initialize,
        Path.Combine(Path.GetTempPath(), "engineering-brain-tests", manifest.BranchKey),
        manifest,
        new ProjectMemorySyncMetrics(
            manifest.Notes.Count(note => note.Kind == KnowledgeNoteKind.Project),
            manifest.Notes.Count(note => note.Kind == KnowledgeNoteKind.Component),
            manifest.Notes.Count,
            manifest.Notes.Count,
            0,
            0,
            0,
            0),
        new ProjectMemoryIntegritySummary(true, 0, 0, 0, 0, 0, 0, 0),
        snapshot);
}
