namespace EngineeringBrain.Core;

public enum ReviewedConceptAnchorPolicy
{
    Clear,
    Ambiguous,
    NotRequired
}

public enum ReviewedConceptLoadStatus
{
    Absent,
    Loaded,
    Invalid
}

public enum ReviewedConceptResolutionStatus
{
    Absent,
    Valid,
    ValidWithDiagnostics,
    Invalid
}

public enum ReviewedConceptDiagnosticScope
{
    Catalog,
    Declaration,
    Assignment
}

public sealed record ReviewedConceptCatalog(
    int SchemaVersion,
    string RepositoryId,
    string Branch,
    string BranchKey,
    int SourceSnapshotSchema,
    string SourceAnalyzerVersion,
    string VocabularyVersion,
    IReadOnlyList<ReviewedConceptDeclaration> Declarations);

public sealed record ReviewedConceptDeclaration(
    string ConceptId,
    string Definition,
    ReviewedConceptAnchorPolicy AnchorPolicy,
    IReadOnlyList<IReadOnlyList<string>> AnchorTokens,
    IReadOnlyList<string> QualificationSupportTokens,
    IReadOnlyList<string> ContextSupportTokens,
    IReadOnlyList<ReviewedConceptAssignment> Assignments,
    ReviewedConceptProvenance Provenance,
    ReviewedConceptReview Review,
    string Fingerprint);

public sealed record ReviewedConceptAssignment(
    string EntityId,
    string SourceReference,
    string SourceFingerprint);

public sealed record ReviewedConceptProvenance(
    string SourceReference,
    string SourceHash);

public sealed record ReviewedConceptReview(
    string Reviewer,
    int Version,
    DateTimeOffset ReviewedAtUtc);

public sealed record ResolvedReviewedConcept(
    string ConceptId,
    ReviewedConceptAnchorPolicy AnchorPolicy,
    IReadOnlyList<IReadOnlyList<string>> AnchorTokens,
    IReadOnlyList<string> QualificationSupportTokens,
    IReadOnlyList<string> ContextSupportTokens,
    string DeclarationFingerprint);

public sealed record ComponentConceptProfile(
    string EntityId,
    string SourceFingerprint,
    IReadOnlyList<ResolvedReviewedConcept> Concepts);

public sealed record ComponentFingerprintEvidence(
    string EntityId,
    string RelativePath,
    int StartLine,
    int EndLine,
    string SourceFingerprint);

public sealed record ReviewedConceptEvidenceContext(
    string RepositoryId,
    string Branch,
    string BranchKey,
    int SourceSnapshotSchema,
    string SourceAnalyzerVersion,
    IReadOnlyDictionary<string, ComponentFingerprintEvidence> Components)
{
    public static ReviewedConceptEvidenceContext FromMemory(ProjectMemorySyncResult memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        var entities = memory.SourceSnapshot.Entities
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        var components = memory.Manifest.Notes
            .Where(note => note.Kind == KnowledgeNoteKind.Component && note.SourceId is not null)
            .Where(note => entities.ContainsKey(note.SourceId!))
            .OrderBy(note => note.SourceId, StringComparer.Ordinal)
            .ToDictionary(
                note => note.SourceId!,
                note => new ComponentFingerprintEvidence(
                    note.SourceId!,
                    entities[note.SourceId!].RelativeFilePath,
                    entities[note.SourceId!].StartLine,
                    entities[note.SourceId!].EndLine,
                    note.SourceFingerprint),
                StringComparer.Ordinal);

        return new ReviewedConceptEvidenceContext(
            memory.Manifest.RepositoryId,
            memory.Manifest.Branch,
            memory.Manifest.BranchKey,
            memory.Manifest.SourceSnapshotSchema,
            memory.Manifest.SourceAnalyzerVersion,
            components);
    }
}

public sealed record ReviewedConceptDiagnostic(
    string Code,
    AnalysisDiagnosticSeverity Severity,
    ReviewedConceptDiagnosticScope Scope,
    string Message,
    string? ConceptId = null,
    string? EntityId = null);

public sealed record ReviewedConceptLoadResult(
    ReviewedConceptLoadStatus Status,
    string Path,
    string? ContentHash,
    ReviewedConceptCatalog? Catalog,
    IReadOnlyList<ReviewedConceptDiagnostic> Diagnostics);

public sealed record ReviewedConceptValidationResult(
    bool CatalogIsValid,
    IReadOnlyList<ReviewedConceptDeclaration> Declarations,
    IReadOnlyList<ReviewedConceptDiagnostic> Diagnostics);

public sealed record ReviewedConceptResolutionResult(
    ReviewedConceptResolutionStatus Status,
    string? CatalogFingerprint,
    IReadOnlyList<ComponentConceptProfile> Profiles,
    IReadOnlyList<ReviewedConceptDiagnostic> Diagnostics)
{
    public static ReviewedConceptResolutionResult Absent { get; } =
        new(ReviewedConceptResolutionStatus.Absent, null, [], []);
}
