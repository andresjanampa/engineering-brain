namespace EngineeringBrain.Core;

public enum KnowledgeNoteKind
{
    RootIndex,
    ArchitectureOverview,
    Project,
    Component,
    Log
}

public enum ProjectMemorySyncMode
{
    Initialize,
    Incremental,
    Rebuild
}

public sealed record ManagedKnowledgeNote(
    string Identity,
    KnowledgeNoteKind Kind,
    string RelativePath,
    string? SourceId,
    string SourceFingerprint,
    string ContentHash);

public sealed record ProjectMemoryManifest(
    int KnowledgeSchemaVersion,
    string RepositoryId,
    string RepositoryName,
    string Branch,
    string BranchKey,
    int SourceSnapshotSchema,
    string SourceAnalyzerVersion,
    string? SourceCommit,
    IReadOnlyList<ManagedKnowledgeNote> Notes);

public sealed record KnowledgeNote(
    ManagedKnowledgeNote ManifestEntry,
    string Content);

public sealed record ProjectMemoryBuild(
    ProjectMemoryManifest Manifest,
    IReadOnlyList<KnowledgeNote> Notes);

public sealed record ProjectMemoryIntegritySummary(
    bool IsValid,
    int DuplicatePaths,
    int DuplicateIdentities,
    int MissingManagedNotes,
    int OrphanedManagedNotes,
    int BrokenLinks,
    int InvalidSourceReferences,
    int MetadataMismatches);

public sealed record ProjectMemorySyncMetrics(
    int ProjectNotes,
    int ComponentNotes,
    int TotalManagedNotes,
    int Created,
    int Updated,
    int Deleted,
    int Reused,
    long ElapsedMilliseconds);

public sealed record ProjectMemorySyncResult(
    ProjectMemorySyncMode Mode,
    string Location,
    ProjectMemoryManifest Manifest,
    ProjectMemorySyncMetrics Metrics,
    ProjectMemoryIntegritySummary Integrity,
    RepositorySnapshot SourceSnapshot);
