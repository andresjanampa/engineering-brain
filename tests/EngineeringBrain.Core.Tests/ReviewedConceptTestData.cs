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

    public static ReviewedConceptLoadResult Loaded(ReviewedConceptCatalog catalog) => new(
        ReviewedConceptLoadStatus.Loaded,
        "reviewed-concepts.json",
        KnowledgeIdentity.ContentHash(ReviewedConceptSerializer.Serialize(catalog)),
        catalog,
        []);

    public static ReviewedConceptEvidenceContext Evidence()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var build = new ProjectMemoryBuilder().Build(snapshot);
        return ReviewedConceptEvidenceContext.FromMemory(Memory(snapshot, build.Manifest));
    }

    public static ComponentCandidate Candidate(
        string entityId,
        int score,
        string? name = null,
        IReadOnlyList<MatchReason>? reasons = null) => new(
        entityId,
        name ?? entityId.Split(':')[^1],
        $"Demo.{name ?? entityId.Split(':')[^1]}",
        "project:core",
        $"src/Core/{name ?? entityId.Split(':')[^1]}.cs",
        CodeEntityType.Class,
        ResolutionLevel.Semantic,
        score,
        false,
        reasons ?? [new MatchReason("member name", "analysis", score)]);

    public static ResolvedReviewedConcept Concept(
        string conceptId,
        ReviewedConceptAnchorPolicy policy = ReviewedConceptAnchorPolicy.NotRequired,
        IReadOnlyList<IReadOnlyList<string>>? anchors = null,
        IReadOnlyList<string>? qualificationSupport = null,
        IReadOnlyList<string>? contextSupport = null) => new(
        conceptId,
        policy,
        anchors ?? [],
        qualificationSupport ?? [],
        contextSupport ?? [],
        $"fingerprint-{conceptId}");

    public static ComponentConceptProfile Profile(
        string entityId,
        params ResolvedReviewedConcept[] concepts) => new(
        entityId,
        $"fingerprint-{entityId}",
        concepts);

    public static ComponentConceptProfile PersistenceProfile(string entityId) => Profile(
        entityId,
        Concept(
            "initiative-analysis-persistence",
            ReviewedConceptAnchorPolicy.Clear,
            [["initiative"]],
            ["persistence"],
            ["analysis"]));

    public static ReviewedConceptResolutionResult Resolution(params ComponentConceptProfile[] profiles) => new(
        ReviewedConceptResolutionStatus.Valid,
        "catalog-fingerprint",
        profiles,
        []);

    public static string CandidateProjection(ComponentCandidate candidate) => string.Join('|',
        candidate.EntityId,
        candidate.Score,
        candidate.GraphExpanded,
        string.Join(';', candidate.MatchReasons.Select(reason =>
            $"{reason.Signal}:{reason.MatchedValue}:{reason.Points}")));

    public static string ProjectProjection(ProjectCandidate candidate) => string.Join('|',
        candidate.ProjectId,
        candidate.Score,
        string.Join(';', candidate.MatchReasons.Select(reason =>
            $"{reason.Signal}:{reason.MatchedValue}:{reason.Points}")));

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
