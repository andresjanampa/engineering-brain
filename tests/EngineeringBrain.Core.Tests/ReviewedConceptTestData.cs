using EngineeringBrain.Core;

namespace EngineeringBrain.Core.Tests;

internal static class ReviewedConceptTestData
{
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
