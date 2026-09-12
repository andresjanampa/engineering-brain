using EngineeringBrain.Core;

namespace EngineeringBrain.Core.Tests;

internal static class ReviewedConceptTestData
{
    public static ReviewedConceptCatalog Catalog(bool reversed = false)
    {
        var declarations = new[]
        {
            Declaration("provider-boundary", "entity:business-service"),
            Declaration("initiative-analysis-persistence", "entity:model")
        };
        return new ReviewedConceptCatalog(
            1,
            "repository:test",
            "feature/project-memory",
            "feature-project-memory--branch",
            3,
            "test-analyzer-v3",
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
        entityId == "entity:model" ? "src/Core/Model.cs:1" : "src/Business/BusinessService.cs:1",
        $"fingerprint-{entityId}");

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
