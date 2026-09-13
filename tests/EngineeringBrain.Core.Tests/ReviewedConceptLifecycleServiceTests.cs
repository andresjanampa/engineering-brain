using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ReviewedConceptLifecycleServiceTests
{
    [Fact]
    public async Task PromoteAsync_FeatureToMainPreservesTwentyFiveReviewedDeclarations()
    {
        using var fixture = new PromotionFixture();

        var result = await fixture.PromoteAsync();
        var target = await fixture.LoadTargetAsync();

        Assert.Equal(ReviewedConceptPromotionOutcome.Promoted, result.Outcome);
        Assert.Equal(25, result.DeclarationCount);
        Assert.Equal(43, result.AssignmentCount);
        Assert.Equal(25, target.Declarations.Count);
    }

    [Fact]
    public async Task PromoteAsync_ReconstructsEntireEnvelopeFromTargetEvidence()
    {
        using var fixture = new PromotionFixture();

        await fixture.PromoteAsync();
        var target = await fixture.LoadTargetAsync();

        Assert.Equal(fixture.TargetEvidence.RepositoryId, target.RepositoryId);
        Assert.Equal(fixture.TargetEvidence.Branch, target.Branch);
        Assert.Equal(fixture.TargetEvidence.BranchKey, target.BranchKey);
        Assert.Equal(fixture.TargetEvidence.SourceSnapshotSchema, target.SourceSnapshotSchema);
        Assert.Equal(fixture.TargetEvidence.SourceAnalyzerVersion, target.SourceAnalyzerVersion);
        Assert.NotEqual(fixture.SourceCatalog.Branch, target.Branch);
        Assert.NotEqual(fixture.SourceCatalog.SourceAnalyzerVersion, target.SourceAnalyzerVersion);
    }

    [Fact]
    public async Task PromoteAsync_RebindsEverySourceReferenceFromTargetEvidence()
    {
        using var fixture = new PromotionFixture();

        var result = await fixture.PromoteAsync();
        var target = await fixture.LoadTargetAsync();

        Assert.Equal(43, result.ReboundSourceReferenceCount);
        foreach (var assignment in target.Declarations.SelectMany(item => item.Assignments))
        {
            var evidence = fixture.TargetEvidence.Components[assignment.EntityId];
            Assert.Equal($"{evidence.RelativePath}:{evidence.StartLine}", assignment.SourceReference);
            Assert.False(assignment.SourceReference.StartsWith("legacy/", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task PromoteAsync_RecomputesAllAssignmentAndDeclarationFingerprints()
    {
        using var fixture = new PromotionFixture();

        var result = await fixture.PromoteAsync();
        var target = await fixture.LoadTargetAsync();

        Assert.Equal(43, result.RecomputedAssignmentCount);
        Assert.Equal(25, result.RecomputedDeclarationFingerprintCount);
        Assert.All(target.Declarations, declaration => Assert.Equal(
            ReviewedConceptSerializer.CreateDeclarationFingerprint(declaration),
            declaration.Fingerprint));
        Assert.All(target.Declarations.SelectMany(item => item.Assignments), assignment => Assert.Equal(
            fixture.TargetEvidence.Components[assignment.EntityId].SourceFingerprint,
            assignment.SourceFingerprint));
        Assert.Equal(ReviewedConceptSerializer.CreateCatalogFingerprint(target), result.NewTargetCatalogFingerprint);
    }

    [Fact]
    public async Task PromoteAsync_PreservesReviewedSemanticAndAuditMetadata()
    {
        using var fixture = new PromotionFixture();

        await fixture.PromoteAsync();
        var target = await fixture.LoadTargetAsync();

        foreach (var source in fixture.SourceCatalog.Declarations)
        {
            var promoted = target.Declarations.Single(item => item.ConceptId == source.ConceptId);
            Assert.Equal(source.Definition, promoted.Definition);
            Assert.Equal(source.AnchorPolicy, promoted.AnchorPolicy);
            Assert.Equal(source.AnchorTokens, promoted.AnchorTokens);
            Assert.Equal(source.QualificationSupportTokens, promoted.QualificationSupportTokens);
            Assert.Equal(source.ContextSupportTokens, promoted.ContextSupportTokens);
            Assert.Equal(source.Provenance, promoted.Provenance);
            Assert.Equal(source.Review, promoted.Review);
        }
    }

    [Fact]
    public async Task PromoteAsync_ResultResolvesValidWithTwentyNineProfiles()
    {
        using var fixture = new PromotionFixture();

        var result = await fixture.PromoteAsync();

        Assert.Equal(29, result.ActiveProfileCount);
        Assert.Equal(0, result.RejectedOrStaleAssignmentCount);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task PromoteAsync_RepeatedIdenticalPromotionReturnsUnchangedWithoutChurn()
    {
        using var fixture = new PromotionFixture();
        await fixture.PromoteAsync();
        var path = new LocalReviewedConceptStore().GetPath(fixture.TargetRoot);
        var before = File.GetLastWriteTimeUtc(path);

        var repeated = await fixture.PromoteAsync();

        Assert.Equal(ReviewedConceptPromotionOutcome.Unchanged, repeated.Outcome);
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public async Task GetStatusAsync_AbsentReturnsZeroCountsAndAbsent()
    {
        using var fixture = new TemporaryDirectory(create: false);

        var result = await Service().GetStatusAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.Absent, result.Status);
        Assert.Equal(0, result.DeclarationCount);
        Assert.Equal(0, result.AssignmentCount);
        Assert.Equal(0, result.ResolvedProfileCount);
        Assert.False(Directory.Exists(fixture.Path));
    }

    [Fact]
    public async Task GetStatusAsync_ValidReportsCatalogCountsFingerprintAndProfiles()
    {
        using var fixture = new TemporaryDirectory();
        var catalog = ReviewedConceptTestData.Catalog();
        await WriteAsync(fixture.Path, ReviewedConceptSerializer.Serialize(catalog));

        var result = await Service().GetStatusAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.Valid, result.Status);
        Assert.Equal(2, result.DeclarationCount);
        Assert.Equal(2, result.AssignmentCount);
        Assert.Equal(2, result.ResolvedProfileCount);
        Assert.Equal(ReviewedConceptSerializer.CreateCatalogFingerprint(catalog), result.CatalogFingerprint);
    }

    [Fact]
    public async Task GetStatusAsync_MalformedReportsInvalidWithoutThrowing()
    {
        using var fixture = new TemporaryDirectory();
        await WriteAsync(fixture.Path, "{not-json");

        var result = await Service().GetStatusAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.Invalid, result.Status);
        Assert.Null(result.DeclarationCount);
        Assert.Null(result.AssignmentCount);
        Assert.Equal(KnowledgeIdentity.ContentHash("{not-json"), result.CatalogFingerprint);
    }

    [Fact]
    public async Task GetStatusAsync_StaleAssignmentReportsValidWithDiagnostics()
    {
        using var fixture = new TemporaryDirectory();
        var catalog = ReviewedConceptTestData.Catalog();
        var stale = catalog.Declarations[0] with
        {
            Assignments =
            [
                catalog.Declarations[0].Assignments[0] with { SourceFingerprint = "stale" }
            ]
        };
        stale = ReviewedConceptTestData.WithFingerprint(stale);
        catalog = catalog with { Declarations = [stale, catalog.Declarations[1]] };
        await WriteAsync(fixture.Path, ReviewedConceptSerializer.Serialize(catalog));

        var result = await Service().GetStatusAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.ValidWithDiagnostics, result.Status);
        Assert.Equal(1, result.InvalidOrStaleAssignmentCount);
        Assert.Contains(result.Diagnostics, item => item.Code == "RC402");
        Assert.Single(result.Diagnostics[0].Message.Split('\n'));
    }

    [Fact]
    public async Task ValidateAsync_BranchMismatchReportsInvalid()
    {
        using var fixture = new TemporaryDirectory();
        var catalog = ReviewedConceptTestData.Catalog() with { Branch = "feature/other" };
        await WriteAsync(fixture.Path, ReviewedConceptSerializer.Serialize(catalog));

        var result = await Service().ValidateAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.Invalid, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "RC102");
    }

    [Fact]
    public async Task ValidateAsync_RepositoryMismatchReportsInvalid()
    {
        using var fixture = new TemporaryDirectory();
        var catalog = ReviewedConceptTestData.Catalog() with { RepositoryId = "repository:other" };
        await WriteAsync(fixture.Path, ReviewedConceptSerializer.Serialize(catalog));

        var result = await Service().ValidateAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.Invalid, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "RC101");
    }

    [Fact]
    public async Task ValidateAsync_DeclarationFingerprintMismatchReportsValidWithDiagnostics()
    {
        using var fixture = new TemporaryDirectory();
        var catalog = ReviewedConceptTestData.Catalog();
        var changed = catalog.Declarations[0] with { Definition = "Changed without re-fingerprinting." };
        catalog = catalog with { Declarations = [changed, catalog.Declarations[1]] };
        await WriteAsync(fixture.Path, ReviewedConceptSerializer.Serialize(catalog));

        var result = await Service().ValidateAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.ValidWithDiagnostics, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "RC204");
        Assert.Equal(1, result.ResolvedProfileCount);
    }

    [Fact]
    public async Task ValidateAsync_NonCanonicalCatalogReportsValidWithDiagnosticsAndRawFingerprint()
    {
        using var fixture = new TemporaryDirectory();
        var json = " \r\n" + ReviewedConceptSerializer.Serialize(ReviewedConceptTestData.Catalog());
        await WriteAsync(fixture.Path, json);

        var result = await Service().ValidateAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.ValidWithDiagnostics, result.Status);
        Assert.Equal(KnowledgeIdentity.ContentHash(json), result.CatalogFingerprint);
        Assert.Contains(result.Diagnostics, item => item.Code == "RCL103");
    }

    [Fact]
    public async Task ValidateAsync_DirectStructurallyInvalidModelCannotThrow()
    {
        using var fixture = new TemporaryDirectory();
        await WriteAsync(fixture.Path, "{\"schemaVersion\":1}");

        var result = await Service().ValidateAsync("demo", fixture.Path, Evidence());

        Assert.Equal(ReviewedConceptResolutionStatus.Invalid, result.Status);
        Assert.Contains(result.Diagnostics, item => item.Code == "RC001");
    }

    private static ReviewedConceptLifecycleService Service() => new();

    private static ReviewedConceptEvidenceContext Evidence() => ReviewedConceptTestData.Evidence();

    private static async Task WriteAsync(string branchRoot, string content)
    {
        var path = new LocalReviewedConceptStore().GetPath(branchRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(bool create = true)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"engineering-brain-lifecycle-{Guid.NewGuid():N}");
            if (create)
            {
                Directory.CreateDirectory(Path);
            }
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class PromotionFixture : IDisposable
    {
        private readonly TemporaryDirectory _root = new();
        private readonly GitInfo _git = new(true, "main", "target-head", null, true);

        public PromotionFixture()
        {
            SourceRoot = Path.Combine(_root.Path, "source");
            TargetRoot = Path.Combine(_root.Path, "target");
            TargetEvidence = CreateTargetEvidence();
            SourceCatalog = CreateSourceCatalog(TargetEvidence);
            WriteAsync(SourceRoot, ReviewedConceptSerializer.Serialize(SourceCatalog)).GetAwaiter().GetResult();
        }

        public string SourceRoot { get; }
        public string TargetRoot { get; }
        public ReviewedConceptCatalog SourceCatalog { get; }
        public ReviewedConceptEvidenceContext TargetEvidence { get; }

        public Task<ReviewedConceptPromotionResult> PromoteAsync() =>
            new ReviewedConceptLifecycleService(
                gitInfo: new StaticGitInfoProvider(_git)).PromoteAsync(
                "demo",
                _root.Path,
                "feature/source",
                "main",
                SourceRoot,
                TargetRoot,
                TargetEvidence,
                _git);

        public async Task<ReviewedConceptCatalog> LoadTargetAsync()
        {
            var loaded = await new LocalReviewedConceptStore().LoadAsync(TargetRoot);
            Assert.Equal(ReviewedConceptLoadStatus.Loaded, loaded.Status);
            return loaded.Catalog!;
        }

        public void Dispose() => _root.Dispose();

        private static ReviewedConceptEvidenceContext CreateTargetEvidence()
        {
            var components = Enumerable.Range(0, 29).ToDictionary(
                index => $"entity:component-{index:D2}",
                index => new ComponentFingerprintEvidence(
                    $"entity:component-{index:D2}",
                    $"src/Components/Component{index:D2}.cs",
                    index + 10,
                    index + 20,
                    $"target-fingerprint-{index:D2}"),
                StringComparer.Ordinal);
            return new ReviewedConceptEvidenceContext(
                "repository:test",
                "main",
                KnowledgeIdentity.CreateBranchKey("main"),
                3,
                "target-analyzer-v3",
                components);
        }

        private static ReviewedConceptCatalog CreateSourceCatalog(
            ReviewedConceptEvidenceContext targetEvidence)
        {
            var assignmentIndex = 0;
            var declarations = new List<ReviewedConceptDeclaration>();
            for (var declarationIndex = 0; declarationIndex < 25; declarationIndex++)
            {
                var assignmentCount = declarationIndex < 18 ? 2 : 1;
                var assignments = new List<ReviewedConceptAssignment>();
                for (var local = 0; local < assignmentCount; local++)
                {
                    var entityIndex = assignmentIndex++ % targetEvidence.Components.Count;
                    assignments.Add(new ReviewedConceptAssignment(
                        $"entity:component-{entityIndex:D2}",
                        $"legacy/Component{entityIndex:D2}.cs:1",
                        $"source-fingerprint-{entityIndex:D2}"));
                }

                var draft = new ReviewedConceptDeclaration(
                    $"concept-{declarationIndex:D2}",
                    $"Reviewed responsibility {declarationIndex:D2}.",
                    ReviewedConceptAnchorPolicy.NotRequired,
                    [],
                    [],
                    [],
                    assignments,
                    new ReviewedConceptProvenance(
                        $"research/concept-{declarationIndex:D2}.json",
                        $"review-source-{declarationIndex:D2}"),
                    new ReviewedConceptReview(
                        "reviewer",
                        1,
                        new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)),
                    string.Empty);
                declarations.Add(draft with
                {
                    Fingerprint = ReviewedConceptSerializer.CreateDeclarationFingerprint(draft)
                });
            }

            return new ReviewedConceptCatalog(
                ReviewedConceptSerializer.CurrentSchemaVersion,
                targetEvidence.RepositoryId,
                "feature/source",
                KnowledgeIdentity.CreateBranchKey("feature/source"),
                2,
                "historical-analyzer-v2",
                "v2",
                declarations);
        }
    }

    private sealed class StaticGitInfoProvider(GitInfo info) : IGitInfoProvider
    {
        public Task<GitInfo> GetInfoAsync(
            string repositoryRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(info);
        }
    }
}
