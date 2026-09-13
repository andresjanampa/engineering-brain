using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ReviewedConceptLifecycleServiceTests
{
    [Fact]
    public async Task RefreshAsync_RebindsRc401ReferenceAndRc402Fingerprint()
    {
        using var fixture = new RefreshFixture();
        await fixture.SeedStaleAsync();

        var result = await fixture.RefreshAsync();
        var refreshed = await fixture.LoadAsync();

        Assert.Equal(ReviewedConceptRefreshOutcome.Refreshed, result.Outcome);
        Assert.Equal(2, result.RecomputedAssignmentCount);
        Assert.Equal(1, result.ReboundSourceReferenceCount);
        Assert.All(refreshed.Declarations.SelectMany(item => item.Assignments), assignment =>
        {
            var evidence = fixture.Evidence.Components[assignment.EntityId];
            Assert.Equal($"{evidence.RelativePath}:{evidence.StartLine}", assignment.SourceReference);
            Assert.Equal(evidence.SourceFingerprint, assignment.SourceFingerprint);
        });
    }

    [Fact]
    public async Task RefreshAsync_Rc400BlocksAndPreservesCatalogBytes()
    {
        using var fixture = new RefreshFixture();
        await fixture.SeedMissingEntityAsync();
        var before = File.ReadAllBytes(fixture.CatalogPath);

        var result = await fixture.RefreshAsync();

        Assert.Equal(ReviewedConceptRefreshOutcome.Blocked, result.Outcome);
        Assert.Contains(result.Diagnostics, item => item.Code == "RCL300"
            && item.EntityId == "entity:missing");
        Assert.Equal(before, File.ReadAllBytes(fixture.CatalogPath));
    }

    [Fact]
    public async Task RefreshAsync_PreservesDeclarationsAssignmentsReviewsAndMigrationHistory()
    {
        using var fixture = new RefreshFixture();
        var original = await fixture.SeedSchemaTwoStaleAsync();

        var result = await fixture.RefreshAsync();
        var refreshed = await fixture.LoadAsync();

        Assert.Equal(ReviewedConceptRefreshOutcome.Refreshed, result.Outcome);
        Assert.Equal(original.SchemaVersion, refreshed.SchemaVersion);
        Assert.Equal(original.IdentityMigrations.Count, refreshed.IdentityMigrations.Count);
        Assert.Equal(original.IdentityMigrations[0] with { AffectedConceptIds = [] },
            refreshed.IdentityMigrations[0] with { AffectedConceptIds = [] });
        Assert.Equal(original.IdentityMigrations[0].AffectedConceptIds,
            refreshed.IdentityMigrations[0].AffectedConceptIds);
        Assert.Equal(
            original.Declarations.OrderBy(item => item.ConceptId, StringComparer.Ordinal)
                .Select(item => (item.ConceptId, item.Definition, item.Review, item.Provenance)),
            refreshed.Declarations.OrderBy(item => item.ConceptId, StringComparer.Ordinal)
                .Select(item => (item.ConceptId, item.Definition, item.Review, item.Provenance)));
    }

    [Fact]
    public async Task RefreshAsync_RequiresExactTransformedAssignmentSetAndValidResolution()
    {
        using var fixture = new RefreshFixture();
        var original = await fixture.SeedStaleAsync();
        var expected = original.Declarations
            .SelectMany(declaration => declaration.Assignments.Select(assignment =>
                (declaration.ConceptId, assignment.EntityId)))
            .OrderBy(item => item.ConceptId, StringComparer.Ordinal)
            .ThenBy(item => item.EntityId, StringComparer.Ordinal)
            .ToArray();

        var result = await fixture.RefreshAsync();
        var refreshed = await fixture.LoadAsync();
        var actual = refreshed.Declarations
            .SelectMany(declaration => declaration.Assignments.Select(assignment =>
                (declaration.ConceptId, assignment.EntityId)))
            .OrderBy(item => item.ConceptId, StringComparer.Ordinal)
            .ThenBy(item => item.EntityId, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
        Assert.Equal(original.Declarations.Count, result.DeclarationCount);
        Assert.Equal(original.Declarations.Sum(item => item.Assignments.Count), result.AssignmentCount);
        Assert.Equal(ReviewedConceptResolutionStatus.Valid,
            (await fixture.Service.GetStatusAsync("demo", fixture.BranchRoot, fixture.Evidence)).Status);
    }

    [Fact]
    public async Task RefreshAsync_RepeatedCurrentCatalogReturnsUnchangedWithoutTimestampChurn()
    {
        using var fixture = new RefreshFixture();
        await fixture.SeedCurrentAsync();
        var before = File.GetLastWriteTimeUtc(fixture.CatalogPath);

        var result = await fixture.RefreshAsync();

        Assert.Equal(ReviewedConceptRefreshOutcome.Unchanged, result.Outcome);
        Assert.Equal(before, File.GetLastWriteTimeUtc(fixture.CatalogPath));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task RefreshAsync_DirtyOrDetachedGitStateBlocksMutation(bool dirty, bool detached)
    {
        using var fixture = new RefreshFixture();
        await fixture.SeedStaleAsync();
        var before = File.ReadAllBytes(fixture.CatalogPath);
        var git = new GitInfo(true, detached ? "(detached HEAD)" : "main", "head", null, !dirty);

        var result = await fixture.RefreshAsync(analyzedGit: git);

        Assert.Equal(ReviewedConceptRefreshOutcome.Blocked, result.Outcome);
        Assert.Contains(result.Diagnostics, item => item.Code == "RCL200");
        Assert.Equal(before, File.ReadAllBytes(fixture.CatalogPath));
    }

    [Fact]
    public async Task RefreshAsync_WriterFingerprintConflictPreservesConcurrentWinner()
    {
        using var fixture = new RefreshFixture();
        await fixture.SeedStaleAsync();
        const string winner = "{\"winner\":true}";
        var git = new MutatingGitInfoProvider(fixture.CatalogPath, winner, fixture.Git);

        var result = await fixture.RefreshAsync(gitInfo: git);

        Assert.Equal(ReviewedConceptRefreshOutcome.Blocked, result.Outcome);
        Assert.Contains(result.Diagnostics, item => item.Code == "RCL401");
        Assert.Equal(winner, File.ReadAllText(fixture.CatalogPath));
    }

    [Fact]
    public async Task RemapAsync_ReplacesAllUsesAndRecordsDurableHumanReview()
    {
        using var fixture = new RefreshFixture();
        var original = await fixture.SeedMissingEntityAsync(useInBothDeclarations: true);
        var previousFingerprint = KnowledgeIdentity.ContentHash(
            ReviewedConceptSerializer.Serialize(original));

        var result = await fixture.RemapAsync(
            [new ReviewedConceptIdentityMapping("entity:missing", "entity:business-service")]);
        var remapped = await fixture.LoadAsync();

        Assert.Equal(ReviewedConceptRemapOutcome.Remapped, result.Outcome);
        Assert.Equal(2, result.RemappedAssignmentCount);
        Assert.DoesNotContain(remapped.Declarations.SelectMany(item => item.Assignments),
            item => item.EntityId == "entity:missing");
        var migration = Assert.Single(remapped.IdentityMigrations);
        Assert.Equal(fixture.Evidence.RepositoryId, migration.RepositoryId);
        Assert.Equal(fixture.Evidence.Branch, migration.Branch);
        Assert.Equal(fixture.Evidence.BranchKey, migration.BranchKey);
        Assert.Equal("entity:missing", migration.OldEntityId);
        Assert.Equal("entity:business-service", migration.NewEntityId);
        Assert.Equal(original.Declarations.Select(item => item.ConceptId).Order(StringComparer.Ordinal),
            migration.AffectedConceptIds);
        Assert.Equal(previousFingerprint, migration.PreviousCatalogFingerprint);
        var destination = fixture.Evidence.Components[migration.NewEntityId];
        Assert.Equal($"{destination.RelativePath}:{destination.StartLine}",
            migration.DestinationSourceReference);
        Assert.Equal(destination.SourceFingerprint, migration.DestinationSourceFingerprint);
        Assert.Equal("alice", migration.Review.Reviewer);
        Assert.Equal(fixture.Now, migration.Review.ReviewedAtUtc);
        Assert.Equal(ReviewedConceptSerializer.CreateIdentityMigrationFingerprint(migration),
            migration.Fingerprint);
    }

    [Fact]
    public async Task RemapAsync_UpgradesSchemaOneToSchemaTwoAtomically()
    {
        using var fixture = new RefreshFixture();
        await fixture.SeedMissingEntityAsync();

        var result = await fixture.RemapAsync(
            [new ReviewedConceptIdentityMapping("entity:missing", "entity:business-service")]);
        var remapped = await fixture.LoadAsync();

        Assert.Equal(ReviewedConceptRemapOutcome.Remapped, result.Outcome);
        Assert.Equal(2, remapped.SchemaVersion);
        Assert.Single(remapped.IdentityMigrations);
    }

    [Fact]
    public async Task RemapAsync_ExpectedTransformedIdentitySetEqualsReconstructedSet()
    {
        using var fixture = new RefreshFixture();
        var original = await fixture.SeedMissingEntityAsync(useInBothDeclarations: true);
        var expected = original.Declarations
            .SelectMany(declaration => declaration.Assignments.Select(assignment =>
                (declaration.ConceptId,
                    EntityId: assignment.EntityId == "entity:missing"
                        ? "entity:business-service"
                        : assignment.EntityId)))
            .OrderBy(item => item.ConceptId, StringComparer.Ordinal)
            .ThenBy(item => item.EntityId, StringComparer.Ordinal)
            .ToArray();

        await fixture.RemapAsync(
            [new ReviewedConceptIdentityMapping("entity:missing", "entity:business-service")]);
        var remapped = await fixture.LoadAsync();
        var actual = remapped.Declarations
            .SelectMany(declaration => declaration.Assignments.Select(assignment =>
                (declaration.ConceptId, assignment.EntityId)))
            .OrderBy(item => item.ConceptId, StringComparer.Ordinal)
            .ThenBy(item => item.EntityId, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task RemapAsync_UncoveredRc400BlocksWithoutWrite()
    {
        using var fixture = new RefreshFixture();
        await fixture.SeedMissingEntityAsync(addSecondMissing: true);
        var before = File.ReadAllBytes(fixture.CatalogPath);

        var result = await fixture.RemapAsync(
            [new ReviewedConceptIdentityMapping("entity:missing", "entity:business-service")]);

        Assert.Equal(ReviewedConceptRemapOutcome.Blocked, result.Outcome);
        Assert.Contains(result.Diagnostics, item => item.Code == "RCL310");
        Assert.Equal(before, File.ReadAllBytes(fixture.CatalogPath));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("incomplete")]
    public async Task RemapAsync_MissingOrIncompleteDestinationBlocksWithoutWrite(string scenario)
    {
        using var fixture = new RefreshFixture();
        await fixture.SeedMissingEntityAsync();
        var before = File.ReadAllBytes(fixture.CatalogPath);
        var destination = scenario == "missing" ? "entity:not-there" : "entity:business-service";
        var evidence = scenario == "incomplete"
            ? fixture.Evidence with
            {
                Components = fixture.Evidence.Components.ToDictionary(
                    item => item.Key,
                    item => item.Key == destination
                        ? item.Value with { SourceFingerprint = string.Empty }
                        : item.Value,
                    StringComparer.Ordinal)
            }
            : fixture.Evidence;

        var result = await fixture.RemapAsync(
            [new ReviewedConceptIdentityMapping("entity:missing", destination)], evidence: evidence);

        Assert.Equal(ReviewedConceptRemapOutcome.Blocked, result.Outcome);
        Assert.Contains(result.Diagnostics, item => item.Code == "RCL311");
        Assert.Equal(before, File.ReadAllBytes(fixture.CatalogPath));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("conflict")]
    [InlineData("chain")]
    [InlineData("unused")]
    public async Task RemapAsync_DuplicateConflictChainCycleOrUnusedMappingBlocks(string scenario)
    {
        using var fixture = new RefreshFixture();
        await fixture.SeedMissingEntityAsync(includeDestinationInDeclaration: scenario == "conflict");
        var before = File.ReadAllBytes(fixture.CatalogPath);
        IReadOnlyList<ReviewedConceptIdentityMapping> mappings = scenario switch
        {
            "duplicate" =>
            [
                new("entity:missing", "entity:business-service"),
                new("entity:missing", "entity:model")
            ],
            "chain" =>
            [
                new("entity:missing", "entity:other-missing"),
                new("entity:other-missing", "entity:business-service")
            ],
            "unused" =>
            [
                new("entity:missing", "entity:business-service"),
                new("entity:unused", "entity:model")
            ],
            _ => [new("entity:missing", "entity:business-service")]
        };

        var result = await fixture.RemapAsync(mappings);

        Assert.Equal(ReviewedConceptRemapOutcome.Blocked, result.Outcome);
        Assert.Equal(before, File.ReadAllBytes(fixture.CatalogPath));
    }

    [Fact]
    public async Task RemapAsync_AllowsOnlyRemainingRc401Rc402Diagnostics()
    {
        using var fixture = new RefreshFixture();
        await fixture.SeedMissingEntityAsync(leaveExistingAssignmentStale: true);

        var result = await fixture.RemapAsync(
            [new ReviewedConceptIdentityMapping("entity:missing", "entity:business-service")]);

        Assert.Equal(ReviewedConceptRemapOutcome.Remapped, result.Outcome);
        Assert.Equal(1, result.RemainingStaleAssignmentCount);
        Assert.All(result.Diagnostics, item =>
            Assert.Contains(item.Code, new[] { "RC401", "RC402" }));
    }

    [Fact]
    public async Task RemapAsync_PreservesReviewedSemanticsAndOriginalDeclarationReview()
    {
        using var fixture = new RefreshFixture();
        var original = await fixture.SeedMissingEntityAsync();

        await fixture.RemapAsync(
            [new ReviewedConceptIdentityMapping("entity:missing", "entity:business-service")]);
        var remapped = await fixture.LoadAsync();

        Assert.Equal(
            original.Declarations.OrderBy(item => item.ConceptId, StringComparer.Ordinal)
                .Select(item => (item.ConceptId, item.Definition, item.AnchorPolicy,
                    item.AnchorTokens, item.QualificationSupportTokens, item.ContextSupportTokens,
                    item.Provenance, item.Review)),
            remapped.Declarations.OrderBy(item => item.ConceptId, StringComparer.Ordinal)
                .Select(item => (item.ConceptId, item.Definition, item.AnchorPolicy,
                    item.AnchorTokens, item.QualificationSupportTokens, item.ContextSupportTokens,
                    item.Provenance, item.Review)));
    }

    [Fact]
    public async Task RemapAsync_GitOrCatalogRacePreservesPreviousOrConcurrentCatalog()
    {
        using var fixture = new RefreshFixture();
        await fixture.SeedMissingEntityAsync();
        const string winner = "{\"winner\":true}";
        var git = new MutatingGitInfoProvider(fixture.CatalogPath, winner, fixture.Git);

        var result = await fixture.RemapAsync(
            [new ReviewedConceptIdentityMapping("entity:missing", "entity:business-service")],
            gitInfo: git);

        Assert.Equal(ReviewedConceptRemapOutcome.Blocked, result.Outcome);
        Assert.Equal(winner, File.ReadAllText(fixture.CatalogPath));
    }

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
    public async Task PromoteAsync_SchemaOneRemainsSchemaOneWithoutHistory()
    {
        using var fixture = new PromotionFixture();
        await fixture.SetSourceCatalogAsync(fixture.SourceCatalog with
        {
            SchemaVersion = 1,
            IdentityMigrations = []
        });

        await fixture.PromoteAsync();
        var target = await fixture.LoadTargetAsync();

        Assert.Equal(1, target.SchemaVersion);
        Assert.Empty(target.IdentityMigrations);
    }

    [Fact]
    public async Task PromoteAsync_SchemaTwoPreservesMigrationHistoryExactly()
    {
        using var fixture = new PromotionFixture();
        var source = fixture.SourceCatalog with
        {
            SchemaVersion = 2,
            IdentityMigrations = [fixture.CreateHistoricalMigration()]
        };
        await fixture.SetSourceCatalogAsync(source);

        await fixture.PromoteAsync();
        var target = await fixture.LoadTargetAsync();

        Assert.Equal(2, target.SchemaVersion);
        var expected = Assert.Single(source.IdentityMigrations);
        var actual = Assert.Single(target.IdentityMigrations);
        Assert.Equal(expected with { AffectedConceptIds = [] },
            actual with { AffectedConceptIds = [] });
        Assert.Equal(expected.AffectedConceptIds, actual.AffectedConceptIds);
    }

    [Fact]
    public async Task PromoteAsync_DoesNotRewriteHistoricalMigrationBranchIdentity()
    {
        using var fixture = new PromotionFixture();
        var migration = fixture.CreateHistoricalMigration();
        await fixture.SetSourceCatalogAsync(fixture.SourceCatalog with
        {
            SchemaVersion = 2,
            IdentityMigrations = [migration]
        });

        await fixture.PromoteAsync();
        var promotedMigration = Assert.Single((await fixture.LoadTargetAsync()).IdentityMigrations);

        Assert.Equal(migration.RepositoryId, promotedMigration.RepositoryId);
        Assert.Equal(migration.Branch, promotedMigration.Branch);
        Assert.Equal(migration.BranchKey, promotedMigration.BranchKey);
        Assert.NotEqual(fixture.TargetEvidence.Branch, promotedMigration.Branch);
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
    public async Task PromoteAsync_EmptyBranchBlocksBeforeRead()
    {
        using var fixture = new PromotionFixture();
        await fixture.SeedTargetAsync();

        var result = await fixture.PromoteAsync(sourceBranch: " ");

        fixture.AssertTargetPreserved();
        AssertBlocked(result, "RCL100");
    }

    [Fact]
    public async Task PromoteAsync_SameBranchBlocksBeforeRead()
    {
        using var fixture = new PromotionFixture();
        await fixture.SeedTargetAsync();

        var result = await fixture.PromoteAsync(sourceBranch: "main");

        fixture.AssertTargetPreserved();
        AssertBlocked(result, "RCL100");
    }

    [Fact]
    public async Task PromoteAsync_ProductionDetachedHeadRepresentationBlocksBeforeRead()
    {
        using var fixture = new PromotionFixture();
        const string detachedHead = "(detached HEAD)";
        var detachedEvidence = fixture.TargetEvidence with
        {
            Branch = detachedHead,
            BranchKey = KnowledgeIdentity.CreateBranchKey(detachedHead)
        };

        var result = await fixture.PromoteAsync(
            targetBranch: detachedHead,
            targetEvidence: detachedEvidence,
            analyzedGit: new GitInfo(true, detachedHead, "target-head", null, true),
            gitInfo: new StaticGitInfoProvider(
                new GitInfo(true, detachedHead, "target-head", null, true)));

        Assert.False(File.Exists(fixture.TargetPath));
        AssertBlocked(result, "RCL200");
        Assert.All(result.Diagnostics, item => Assert.InRange(
            ReviewedConceptDiagnosticFormatter.Format(item).Length,
            1,
            ReviewedConceptDiagnosticFormatter.MaximumRenderedLength));
    }

    [Fact]
    public async Task PromoteAsync_NonCurrentTargetBlocksBeforeRead()
    {
        using var fixture = new PromotionFixture();
        await fixture.SeedTargetAsync();

        var result = await fixture.PromoteAsync(
            analyzedGit: new GitInfo(true, "feature/other", "target-head", null, true));

        fixture.AssertTargetPreserved();
        AssertBlocked(result, "RCL200");
    }

    [Fact]
    public async Task PromoteAsync_DirtyTargetBlocksBeforeRead()
    {
        using var fixture = new PromotionFixture();
        await fixture.SeedTargetAsync();

        var result = await fixture.PromoteAsync(
            analyzedGit: new GitInfo(true, "main", "target-head", null, false));

        fixture.AssertTargetPreserved();
        AssertBlocked(result, "RCL200");
    }

    [Fact]
    public async Task PromoteAsync_AbsentSourceBlocksWithoutCreatingTarget()
    {
        using var fixture = new PromotionFixture();
        await fixture.SeedTargetAsync();
        File.Delete(fixture.SourcePath);

        var result = await fixture.PromoteAsync();

        fixture.AssertTargetPreserved();
        AssertBlocked(result, "RCL101");
    }

    [Fact]
    public async Task PromoteAsync_MalformedSourceBlocksWithoutChangingTarget()
    {
        using var fixture = new PromotionFixture();
        await fixture.SeedTargetAsync();
        await File.WriteAllTextAsync(fixture.SourcePath, "{not-json");

        var result = await fixture.PromoteAsync();

        fixture.AssertTargetPreserved();
        AssertBlocked(result, "RCL102");
    }

    [Fact]
    public async Task PromoteAsync_InvalidExistingTargetBlocksWithoutOverwrite()
    {
        using var fixture = new PromotionFixture();
        await fixture.WriteTargetBytesAsync("{invalid-target");
        fixture.RememberTarget();

        var result = await fixture.PromoteAsync();

        fixture.AssertTargetPreserved();
        AssertBlocked(result, "RCL400");
    }

    [Fact]
    public async Task PromoteAsync_OneMissingEntityBlocksEntirePromotion()
    {
        using var fixture = new PromotionFixture();
        await fixture.SeedTargetAsync();
        await fixture.ReplaceFirstAssignmentAsync(
            new ReviewedConceptAssignment("entity:missing", "legacy/Missing.cs:1", "source-missing"));

        var result = await fixture.PromoteAsync();

        fixture.AssertTargetPreserved();
        AssertBlocked(result, "RCL300");
        Assert.Contains(result.Diagnostics, item => item.EntityId == "entity:missing");
    }

    [Fact]
    public async Task PromoteAsync_OneUnverifiableAssignmentPreservesExistingTarget()
    {
        using var fixture = new PromotionFixture();
        await fixture.SeedTargetAsync();
        const string incompleteId = "entity:incomplete";
        await fixture.ReplaceFirstAssignmentAsync(
            new ReviewedConceptAssignment(incompleteId, "legacy/Incomplete.cs:1", "source-incomplete"));
        var incomplete = fixture.TargetEvidence with
        {
            Components = fixture.TargetEvidence.Components
                .Append(new KeyValuePair<string, ComponentFingerprintEvidence>(
                    incompleteId,
                    new ComponentFingerprintEvidence(
                        incompleteId, "src/Incomplete.cs", 1, 2, "")))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
        };

        var result = await fixture.PromoteAsync(targetEvidence: incomplete);

        fixture.AssertTargetPreserved();
        AssertBlocked(result, "RCL301");
    }

    [Fact]
    public async Task PromoteAsync_BranchChangesBeforeWriteBlocksMutation()
    {
        using var fixture = new PromotionFixture();
        await fixture.SeedTargetAsync();
        await fixture.ChangeReviewedDefinitionAsync();

        var result = await fixture.PromoteAsync(gitInfo: new StaticGitInfoProvider(
            new GitInfo(true, "feature/changed", "target-head", null, true)));

        fixture.AssertTargetPreserved();
        AssertBlocked(result, "RCL201");
    }

    [Fact]
    public async Task PromoteAsync_HeadChangesBeforeWriteBlocksMutation()
    {
        using var fixture = new PromotionFixture();
        await fixture.SeedTargetAsync();
        await fixture.ChangeReviewedDefinitionAsync();

        var result = await fixture.PromoteAsync(gitInfo: new StaticGitInfoProvider(
            new GitInfo(true, "main", "changed-head", null, true)));

        fixture.AssertTargetPreserved();
        AssertBlocked(result, "RCL201");
    }

    [Fact]
    public async Task PromoteAsync_WorktreeBecomesDirtyBeforeWriteBlocksMutation()
    {
        using var fixture = new PromotionFixture();
        await fixture.SeedTargetAsync();
        await fixture.ChangeReviewedDefinitionAsync();

        var result = await fixture.PromoteAsync(gitInfo: new StaticGitInfoProvider(
            new GitInfo(true, "main", "target-head", null, false)));

        fixture.AssertTargetPreserved();
        AssertBlocked(result, "RCL201");
    }

    [Fact]
    public async Task PromoteAsync_DetachedHeadBeforeWriteBlocksMutation()
    {
        using var fixture = new PromotionFixture();
        await fixture.SeedTargetAsync();
        await fixture.ChangeReviewedDefinitionAsync();

        var result = await fixture.PromoteAsync(gitInfo: new StaticGitInfoProvider(
            new GitInfo(true, "(detached HEAD)", "target-head", null, true)));

        fixture.AssertTargetPreserved();
        AssertBlocked(result, "RCL201");
        Assert.All(result.Diagnostics, item => Assert.InRange(
            ReviewedConceptDiagnosticFormatter.Format(item).Length,
            1,
            ReviewedConceptDiagnosticFormatter.MaximumRenderedLength));
    }

    [Fact]
    public async Task PromoteAsync_WriterFingerprintConflictReturnsBlockedAndPreservesWinner()
    {
        using var fixture = new PromotionFixture();
        await fixture.SeedTargetAsync();
        await fixture.ChangeReviewedDefinitionAsync();
        await using var held = new FileStream(
            fixture.TargetPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var writer = new LocalReviewedConceptWriter(
            lockTimeout: TimeSpan.FromSeconds(2),
            lockRetryDelay: TimeSpan.FromMilliseconds(10));

        var promotion = fixture.PromoteAsync(writer: writer);
        await Task.Delay(100);
        var winner = "{\"winner\":true}";
        await File.WriteAllTextAsync(fixture.TargetPath, winner);
        await held.DisposeAsync();
        var result = await promotion;

        Assert.Equal(winner, await File.ReadAllTextAsync(fixture.TargetPath));
        AssertBlocked(result, "RCL401");
    }

    [Fact]
    public async Task PromoteAsync_NonCooperatingMutationDuringFinalGuardReturnsConflict()
    {
        using var fixture = new PromotionFixture();
        await fixture.SeedTargetAsync();
        await fixture.ChangeReviewedDefinitionAsync();
        const string winner = "{\"winner\":true}";

        var result = await fixture.PromoteAsync(gitInfo: new MutatingGitInfoProvider(
            fixture.TargetPath,
            winner,
            new GitInfo(true, "main", "target-head", null, true)));

        Assert.Equal(winner, await File.ReadAllTextAsync(fixture.TargetPath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(fixture.TargetPath)!, ".*.tmp"));
        AssertBlocked(result, "RCL401");
        await using var releasedLock = new FileStream(
            fixture.TargetPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task PromoteAsync_LockTimeoutReturnsBoundedDiagnostic()
    {
        using var fixture = new PromotionFixture();
        await fixture.SeedTargetAsync();
        await fixture.ChangeReviewedDefinitionAsync();
        await using var held = new FileStream(
            fixture.TargetPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var writer = new LocalReviewedConceptWriter(
            lockTimeout: TimeSpan.FromMilliseconds(100),
            lockRetryDelay: TimeSpan.FromMilliseconds(10));

        var result = await fixture.PromoteAsync(writer: writer);

        fixture.AssertTargetPreserved();
        AssertBlocked(result, "RCL401");
        Assert.All(result.Diagnostics, item => Assert.InRange(
            ReviewedConceptDiagnosticFormatter.Format(item).Length,
            1,
            ReviewedConceptDiagnosticFormatter.MaximumRenderedLength));
    }

    [Fact]
    public async Task Promotion_WritesOnlyTargetBranchSemanticDirectory()
    {
        using var fixture = new PromotionFixture();
        var before = fixture.ExternalFiles();

        await fixture.PromoteAsync();

        var added = fixture.ExternalFiles().Except(before, StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.NotEmpty(added);
        Assert.All(added, path => Assert.StartsWith(
            Path.GetDirectoryName(fixture.TargetPath)!,
            path,
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Promotion_DoesNotModifySourceCatalog()
    {
        using var fixture = new PromotionFixture();
        var bytes = await File.ReadAllBytesAsync(fixture.SourcePath);
        var timestamp = File.GetLastWriteTimeUtc(fixture.SourcePath);

        await fixture.PromoteAsync();

        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.SourcePath));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(fixture.SourcePath));
    }

    [Fact]
    public async Task Promotion_DoesNotCreateSnapshotMemoryOrEvaluationArtifacts()
    {
        using var fixture = new PromotionFixture();

        await fixture.PromoteAsync();

        Assert.DoesNotContain(fixture.ExternalFiles(), path =>
            path.Contains($"{Path.DirectorySeparatorChar}snapshots{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || path.Contains($"{Path.DirectorySeparatorChar}evaluations{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).Equals("manifest.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Promotion_DoesNotModifyFilesInsideRepositoryRoot()
    {
        using var fixture = new PromotionFixture();
        var before = fixture.RepositoryProjection();

        await fixture.PromoteAsync();

        Assert.Equal(before, fixture.RepositoryProjection());
    }

    [Fact]
    public async Task SourceCatalogFromDeletedBranchNameWorksWhenLocalArtifactExists()
    {
        using var fixture = new PromotionFixture();
        await fixture.RetargetSourceCatalogAsync("deleted/source");

        var result = await fixture.PromoteAsync(sourceBranch: "deleted/source");

        Assert.Equal(ReviewedConceptPromotionOutcome.Promoted, result.Outcome);
        Assert.Equal(25, result.DeclarationCount);
    }

    [Fact]
    public async Task SourceCatalogAbsentFailsWithoutGitOrNetworkLookup()
    {
        using var fixture = new PromotionFixture();
        File.Delete(fixture.SourcePath);
        var git = new CountingGitInfoProvider(new GitInfo(true, "main", "target-head", null, true));

        var result = await fixture.PromoteAsync(gitInfo: git);

        AssertBlocked(result, "RCL101");
        Assert.Equal(0, git.CallCount);
        Assert.False(File.Exists(fixture.TargetPath));
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

    private static void AssertBlocked(ReviewedConceptPromotionResult result, string code)
    {
        Assert.Equal(ReviewedConceptPromotionOutcome.Blocked, result.Outcome);
        Assert.Contains(result.Diagnostics, item => item.Code == code);
    }

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
        private byte[]? _rememberedTarget;
        private DateTime? _rememberedTimestamp;

        public PromotionFixture()
        {
            RepositoryRoot = Path.Combine(_root.Path, "repository");
            Directory.CreateDirectory(RepositoryRoot);
            File.WriteAllText(Path.Combine(RepositoryRoot, "README.md"), "# Test repository\n");
            SourceRoot = Path.Combine(_root.Path, "external", "source");
            TargetRoot = Path.Combine(_root.Path, "external", "target");
            TargetEvidence = CreateTargetEvidence();
            SourceCatalog = CreateSourceCatalog(TargetEvidence);
            WriteAsync(SourceRoot, ReviewedConceptSerializer.Serialize(SourceCatalog)).GetAwaiter().GetResult();
        }

        public string SourceRoot { get; }
        public string TargetRoot { get; }
        public string RepositoryRoot { get; }
        public string SourcePath => new LocalReviewedConceptStore().GetPath(SourceRoot);
        public string TargetPath => new LocalReviewedConceptStore().GetPath(TargetRoot);
        public ReviewedConceptCatalog SourceCatalog { get; }
        public ReviewedConceptEvidenceContext TargetEvidence { get; }

        public Task<ReviewedConceptPromotionResult> PromoteAsync(
            string sourceBranch = "feature/source",
            string targetBranch = "main",
            ReviewedConceptEvidenceContext? targetEvidence = null,
            GitInfo? analyzedGit = null,
            IGitInfoProvider? gitInfo = null,
            LocalReviewedConceptWriter? writer = null) =>
            new ReviewedConceptLifecycleService(
                writer: writer,
                gitInfo: gitInfo ?? new StaticGitInfoProvider(_git)).PromoteAsync(
                "demo",
                RepositoryRoot,
                sourceBranch,
                targetBranch,
                SourceRoot,
                TargetRoot,
                targetEvidence ?? TargetEvidence,
                analyzedGit ?? _git);

        public string[] ExternalFiles() => Directory.Exists(Path.Combine(_root.Path, "external"))
            ? Directory.GetFiles(Path.Combine(_root.Path, "external"), "*", SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

        public string[] RepositoryProjection() => Directory.GetFiles(
                RepositoryRoot, "*", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => $"{Path.GetRelativePath(RepositoryRoot, path)}|{KnowledgeIdentity.ContentHash(File.ReadAllText(path))}")
            .ToArray();

        public async Task SeedTargetAsync()
        {
            var result = await PromoteAsync();
            Assert.Equal(ReviewedConceptPromotionOutcome.Promoted, result.Outcome);
            RememberTarget();
        }

        public void RememberTarget()
        {
            _rememberedTarget = File.ReadAllBytes(TargetPath);
            _rememberedTimestamp = File.GetLastWriteTimeUtc(TargetPath);
        }

        public void AssertTargetPreserved()
        {
            Assert.NotNull(_rememberedTarget);
            Assert.Equal(_rememberedTarget, File.ReadAllBytes(TargetPath));
            Assert.Equal(_rememberedTimestamp, File.GetLastWriteTimeUtc(TargetPath));
        }

        public async Task WriteTargetBytesAsync(string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TargetPath)!);
            await File.WriteAllTextAsync(TargetPath, content);
        }

        public async Task ReplaceFirstAssignmentAsync(ReviewedConceptAssignment replacement)
        {
            var declaration = SourceCatalog.Declarations[0] with { Assignments = [replacement] };
            declaration = declaration with
            {
                Fingerprint = ReviewedConceptSerializer.CreateDeclarationFingerprint(declaration)
            };
            var changed = SourceCatalog with
            {
                Declarations = [declaration, .. SourceCatalog.Declarations.Skip(1)]
            };
            await WriteAsync(SourceRoot, ReviewedConceptSerializer.Serialize(changed));
        }

        public async Task ChangeReviewedDefinitionAsync()
        {
            var declaration = SourceCatalog.Declarations[0] with
            {
                Definition = SourceCatalog.Declarations[0].Definition + " Updated."
            };
            declaration = declaration with
            {
                Fingerprint = ReviewedConceptSerializer.CreateDeclarationFingerprint(declaration)
            };
            var changed = SourceCatalog with
            {
                Declarations = [declaration, .. SourceCatalog.Declarations.Skip(1)]
            };
            await WriteAsync(SourceRoot, ReviewedConceptSerializer.Serialize(changed));
        }

        public Task SetSourceCatalogAsync(ReviewedConceptCatalog catalog) =>
            WriteAsync(SourceRoot, ReviewedConceptSerializer.Serialize(catalog));

        public ReviewedConceptIdentityMigration CreateHistoricalMigration()
        {
            var draft = new ReviewedConceptIdentityMigration(
                TargetEvidence.RepositoryId,
                "feature/historical",
                KnowledgeIdentity.CreateBranchKey("feature/historical"),
                "entity:old-component",
                "entity:component-00",
                [SourceCatalog.Declarations[0].ConceptId],
                "historical-catalog-fingerprint",
                "src/Components/Component00.cs:10",
                "historical-destination-fingerprint",
                new ReviewedConceptReview(
                    "historical-reviewer",
                    1,
                    new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero)),
                string.Empty);
            return draft with
            {
                Fingerprint = ReviewedConceptSerializer.CreateIdentityMigrationFingerprint(draft)
            };
        }

        public async Task RetargetSourceCatalogAsync(string sourceBranch)
        {
            var changed = SourceCatalog with
            {
                Branch = sourceBranch,
                BranchKey = KnowledgeIdentity.CreateBranchKey(sourceBranch)
            };
            await WriteAsync(SourceRoot, ReviewedConceptSerializer.Serialize(changed));
        }

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

    private sealed class RefreshFixture : IDisposable
    {
        private readonly TemporaryDirectory _root = new();

        public RefreshFixture()
        {
            RepositoryRoot = Path.Combine(_root.Path, "repository");
            Directory.CreateDirectory(RepositoryRoot);
            File.WriteAllText(Path.Combine(RepositoryRoot, "README.md"), "# Test repository\n");
            BranchRoot = Path.Combine(_root.Path, "external", "main");
            Evidence = ReviewedConceptTestData.Evidence();
            Git = new GitInfo(true, Evidence.Branch, "head", null, true);
            Service = new ReviewedConceptLifecycleService(
                gitInfo: new StaticGitInfoProvider(Git),
                timeProvider: new FixedTimeProvider(Now));
        }

        public string RepositoryRoot { get; }
        public string BranchRoot { get; }
        public string CatalogPath => new LocalReviewedConceptStore().GetPath(BranchRoot);
        public ReviewedConceptEvidenceContext Evidence { get; }
        public GitInfo Git { get; }
        public DateTimeOffset Now { get; } = new(2026, 9, 13, 15, 30, 0, TimeSpan.Zero);
        public ReviewedConceptLifecycleService Service { get; }

        public Task<ReviewedConceptRefreshResult> RefreshAsync(
            GitInfo? analyzedGit = null,
            IGitInfoProvider? gitInfo = null) =>
            new ReviewedConceptLifecycleService(
                gitInfo: gitInfo ?? new StaticGitInfoProvider(Git),
                timeProvider: new FixedTimeProvider(Now)).RefreshAsync(
                "demo",
                RepositoryRoot,
                BranchRoot,
                Evidence,
                analyzedGit ?? Git);

        public Task<ReviewedConceptRemapResult> RemapAsync(
            IReadOnlyList<ReviewedConceptIdentityMapping> mappings,
            string reviewer = "alice",
            ReviewedConceptEvidenceContext? evidence = null,
            GitInfo? analyzedGit = null,
            IGitInfoProvider? gitInfo = null) =>
            new ReviewedConceptLifecycleService(
                gitInfo: gitInfo ?? new StaticGitInfoProvider(Git),
                timeProvider: new FixedTimeProvider(Now)).RemapAsync(
                "demo",
                RepositoryRoot,
                BranchRoot,
                evidence ?? Evidence,
                analyzedGit ?? Git,
                reviewer,
                mappings);

        public async Task<ReviewedConceptCatalog> SeedStaleAsync()
        {
            var catalog = ReviewedConceptTestData.Catalog();
            var first = catalog.Declarations[0] with
            {
                Assignments =
                [
                    catalog.Declarations[0].Assignments[0] with
                    {
                        SourceReference = "src/Old.cs:1",
                        SourceFingerprint = "stale"
                    }
                ]
            };
            first = ReviewedConceptTestData.WithFingerprint(first);
            var second = catalog.Declarations[1] with
            {
                Assignments =
                [
                    catalog.Declarations[1].Assignments[0] with { SourceFingerprint = "stale" }
                ]
            };
            second = ReviewedConceptTestData.WithFingerprint(second);
            catalog = catalog with { Declarations = [first, second] };
            await WriteAsync(BranchRoot, ReviewedConceptSerializer.Serialize(catalog));
            return catalog;
        }

        public async Task SeedCurrentAsync()
        {
            await WriteAsync(BranchRoot,
                ReviewedConceptSerializer.Serialize(ReviewedConceptTestData.Catalog()));
        }

        public async Task<ReviewedConceptCatalog> SeedMissingEntityAsync(
            bool useInBothDeclarations = false,
            bool addSecondMissing = false,
            bool includeDestinationInDeclaration = false,
            bool leaveExistingAssignmentStale = false)
        {
            var catalog = ReviewedConceptTestData.Catalog();
            var assignments = new List<ReviewedConceptAssignment>
            {
                new("entity:missing", "src/Missing.cs:1", "missing")
            };
            if (includeDestinationInDeclaration)
            {
                assignments.Add(ReviewedConceptTestData.Assignment("entity:business-service"));
            }

            var declaration = catalog.Declarations[0] with
            {
                Assignments = assignments
            };
            declaration = ReviewedConceptTestData.WithFingerprint(declaration);
            var second = catalog.Declarations[1];
            if (useInBothDeclarations)
            {
                second = ReviewedConceptTestData.WithFingerprint(second with
                {
                    Assignments = [new("entity:missing", "src/Missing.cs:1", "missing")]
                });
            }
            else if (addSecondMissing)
            {
                second = ReviewedConceptTestData.WithFingerprint(second with
                {
                    Assignments = [new("entity:other-missing", "src/Other.cs:1", "missing")]
                });
            }
            else if (leaveExistingAssignmentStale)
            {
                second = ReviewedConceptTestData.WithFingerprint(second with
                {
                    Assignments = [second.Assignments[0] with { SourceFingerprint = "stale" }]
                });
            }

            catalog = catalog with { Declarations = [declaration, second] };
            await WriteAsync(BranchRoot, ReviewedConceptSerializer.Serialize(catalog));
            return catalog;
        }

        public async Task<ReviewedConceptCatalog> SeedSchemaTwoStaleAsync()
        {
            var catalog = await SeedStaleAsync();
            var component = Evidence.Components["entity:business-service"];
            var migration = new ReviewedConceptIdentityMigration(
                catalog.RepositoryId,
                catalog.Branch,
                catalog.BranchKey,
                "entity:old",
                component.EntityId,
                [catalog.Declarations[0].ConceptId],
                "previous",
                $"{component.RelativePath}:{component.StartLine}",
                component.SourceFingerprint,
                new ReviewedConceptReview(
                    "reviewer",
                    1,
                    new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero)),
                "pending");
            migration = migration with
            {
                Fingerprint = ReviewedConceptSerializer.CreateIdentityMigrationFingerprint(migration)
            };
            catalog = catalog with { SchemaVersion = 2, IdentityMigrations = [migration] };
            await WriteAsync(BranchRoot, ReviewedConceptSerializer.Serialize(catalog));
            return catalog;
        }

        public async Task<ReviewedConceptCatalog> LoadAsync()
        {
            var loaded = await new LocalReviewedConceptStore().LoadAsync(BranchRoot);
            Assert.Equal(ReviewedConceptLoadStatus.Loaded, loaded.Status);
            return loaded.Catalog!;
        }

        public void Dispose() => _root.Dispose();
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

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class MutatingGitInfoProvider(
        string targetPath,
        string replacement,
        GitInfo info) : IGitInfoProvider
    {
        public async Task<GitInfo> GetInfoAsync(
            string repositoryRoot,
            CancellationToken cancellationToken = default)
        {
            await File.WriteAllTextAsync(targetPath, replacement, cancellationToken);
            return info;
        }
    }

    private sealed class CountingGitInfoProvider(GitInfo info) : IGitInfoProvider
    {
        public int CallCount { get; private set; }

        public Task<GitInfo> GetInfoAsync(
            string repositoryRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            return Task.FromResult(info);
        }
    }
}
