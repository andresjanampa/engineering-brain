using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ProjectMemoryServiceTests
{
    [Fact]
    public async Task SyncAsync_InitializesKnowledgeAndSecondSyncReusesWithoutRewritingNotes()
    {
        using var fixture = new MemoryFixture();
        var snapshot = ProjectMemoryTestFactory.Create();

        var first = await fixture.Service.SyncAsync(snapshot);
        var timestamps = first.Manifest.Notes.ToDictionary(
            note => note.RelativePath,
            note => File.GetLastWriteTimeUtc(Path.Combine(first.Location, Native(note.RelativePath))),
            StringComparer.OrdinalIgnoreCase);
        var second = await fixture.Service.SyncAsync(snapshot);

        Assert.Equal(ProjectMemorySyncMode.Initialize, first.Mode);
        Assert.Equal(first.Metrics.TotalManagedNotes, first.Metrics.Created);
        Assert.Equal(2, first.Metrics.ProjectNotes);
        Assert.Equal(4, first.Metrics.ComponentNotes);
        Assert.True(first.Integrity.IsValid);
        Assert.Equal(ProjectMemorySyncMode.Incremental, second.Mode);
        Assert.Equal(0, second.Metrics.Created);
        Assert.Equal(0, second.Metrics.Updated);
        Assert.Equal(0, second.Metrics.Deleted);
        Assert.Equal(second.Metrics.TotalManagedNotes, second.Metrics.Reused);
        Assert.All(second.Manifest.Notes, note => Assert.Equal(
            timestamps[note.RelativePath],
            File.GetLastWriteTimeUtc(Path.Combine(second.Location, Native(note.RelativePath)))));
    }

    [Fact]
    public async Task SyncAsync_ChangedComponentDoesNotRewriteUnrelatedProjectOrComponent()
    {
        using var fixture = new MemoryFixture();
        var snapshot = ProjectMemoryTestFactory.Create();
        var first = await fixture.Service.SyncAsync(snapshot);
        var businessNotes = first.Manifest.Notes
            .Where(note => note.SourceId is "project:business" or "entity:business-service")
            .ToDictionary(
                note => note.RelativePath,
                note => File.ReadAllText(Path.Combine(first.Location, Native(note.RelativePath))));
        var service = snapshot.Entities.Single(entity => entity.Id == "entity:service");
        var changed = service with { FullName = "Demo.Core.Service<T>" };
        snapshot = snapshot with
        {
            Entities = snapshot.Entities.Select(entity => entity.Id == service.Id ? changed : entity).ToArray()
        };

        var result = await fixture.Service.SyncAsync(snapshot);

        Assert.True(result.Metrics.Updated > 0);
        Assert.All(businessNotes, pair => Assert.Equal(
            pair.Value,
            File.ReadAllText(Path.Combine(result.Location, Native(pair.Key)))));
        Assert.Contains(result.Manifest.Notes, note =>
            note.SourceId == service.Id
            && note.SourceFingerprint != first.Manifest.Notes.Single(old => old.SourceId == service.Id).SourceFingerprint);
    }

    [Fact]
    public async Task SyncAsync_DeletesOnlyManifestManagedStaleNotesAndPreservesUserFile()
    {
        using var fixture = new MemoryFixture();
        var snapshot = ProjectMemoryTestFactory.Create();
        var first = await fixture.Service.SyncAsync(snapshot);
        var modelNote = first.Manifest.Notes.Single(note => note.SourceId == "entity:model");
        var userFile = Path.Combine(first.Location, "human-notes.md");
        File.WriteAllText(userFile, "# Human note\n");
        var reviewedConceptPath = Path.Combine(first.Location, "semantic", "reviewed-concepts.json");
        Directory.CreateDirectory(Path.GetDirectoryName(reviewedConceptPath)!);
        var reviewedConceptBytes = "{\"managedBy\":\"human-review\"}"u8.ToArray();
        File.WriteAllBytes(reviewedConceptPath, reviewedConceptBytes);
        var reviewedConceptTimestamp = File.GetLastWriteTimeUtc(reviewedConceptPath);
        snapshot = snapshot with
        {
            Entities = snapshot.Entities.Where(entity => entity.Id != "entity:model").ToArray(),
            Relations = snapshot.Relations.Where(relation =>
                relation.SourceEntityId != "entity:model" && relation.TargetEntityId != "entity:model").ToArray()
        };

        var result = await fixture.Service.SyncAsync(snapshot);

        Assert.Equal(1, result.Metrics.Deleted);
        Assert.False(File.Exists(Path.Combine(result.Location, Native(modelNote.RelativePath))));
        Assert.True(File.Exists(userFile));
        Assert.Equal(reviewedConceptBytes, File.ReadAllBytes(reviewedConceptPath));
        Assert.Equal(reviewedConceptTimestamp, File.GetLastWriteTimeUtc(reviewedConceptPath));
        Assert.True(result.Integrity.IsValid);
    }

    [Fact]
    public async Task ProjectMemorySync_RemainsUnawareOfLifecycleWriterArtifacts()
    {
        using var fixture = new MemoryFixture();
        var snapshot = ProjectMemoryTestFactory.Create();
        var initialized = await fixture.Service.SyncAsync(snapshot);
        var semanticPath = Path.Combine(initialized.Location, "semantic", "reviewed-concepts.json");
        Directory.CreateDirectory(Path.GetDirectoryName(semanticPath)!);
        var semanticBytes = "{\"managedBy\":\"reviewed-concept-lifecycle\"}"u8.ToArray();
        await File.WriteAllBytesAsync(semanticPath, semanticBytes);
        var timestamp = File.GetLastWriteTimeUtc(semanticPath);

        var incremental = await fixture.Service.SyncAsync(snapshot);
        var withoutModel = snapshot with
        {
            Entities = snapshot.Entities.Where(item => item.Id != "entity:model").ToArray(),
            Relations = snapshot.Relations.Where(item =>
                item.SourceEntityId != "entity:model" && item.TargetEntityId != "entity:model").ToArray()
        };
        await fixture.Service.SyncAsync(withoutModel);
        var manifestPath = Path.Combine(initialized.Location, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, (await File.ReadAllTextAsync(manifestPath)).Replace(
            "\"knowledgeSchemaVersion\": 1",
            "\"knowledgeSchemaVersion\": 999",
            StringComparison.Ordinal));
        var rebuilt = await fixture.Service.SyncAsync(snapshot);

        Assert.Equal(ProjectMemorySyncMode.Incremental, incremental.Mode);
        Assert.Equal(ProjectMemorySyncMode.Rebuild, rebuilt.Mode);
        Assert.Equal(semanticBytes, await File.ReadAllBytesAsync(semanticPath));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(semanticPath));
    }

    [Fact]
    public async Task SyncAsync_AddedAndDeletedProjectCreateAndRemoveItsManagedNotes()
    {
        using var fixture = new MemoryFixture();
        var complete = ProjectMemoryTestFactory.Create();
        var withoutBusiness = RemoveProject(complete, "project:business");

        var first = await fixture.Service.SyncAsync(withoutBusiness);
        var added = await fixture.Service.SyncAsync(complete);
        var deleted = await fixture.Service.SyncAsync(withoutBusiness);

        Assert.Equal(4, first.Metrics.ProjectNotes + first.Metrics.ComponentNotes);
        Assert.Equal(2, added.Metrics.Created);
        Assert.Equal(2, deleted.Metrics.Deleted);
        Assert.DoesNotContain(deleted.Manifest.Notes, note =>
            note.SourceId is "project:business" or "entity:business-service");
    }

    [Fact]
    public async Task SyncAsync_IsolatesBranchesUnderDifferentSafeDirectories()
    {
        using var fixture = new MemoryFixture();

        var first = await fixture.Service.SyncAsync(ProjectMemoryTestFactory.Create("feature/A"));
        var second = await fixture.Service.SyncAsync(ProjectMemoryTestFactory.Create("feature/B"));

        Assert.NotEqual(first.Location, second.Location);
        Assert.True(File.Exists(Path.Combine(first.Location, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(second.Location, "manifest.json")));
        Assert.NotEqual(first.Manifest.BranchKey, second.Manifest.BranchKey);
    }

    [Fact]
    public async Task SyncAsync_IsolatesRepositoriesWithFilesystemUnsafeIdentifiers()
    {
        using var fixture = new MemoryFixture();
        var firstSnapshot = ProjectMemoryTestFactory.Create();
        var secondSnapshot = firstSnapshot with
        {
            Repository = firstSnapshot.Repository with { Id = "repository:other" }
        };

        var first = await fixture.Service.SyncAsync(firstSnapshot);
        var second = await fixture.Service.SyncAsync(secondSnapshot);

        Assert.NotEqual(first.Location, second.Location);
        Assert.True(File.Exists(Path.Combine(first.Location, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(second.Location, "manifest.json")));
        Assert.Equal(firstSnapshot.Repository.Id, first.Manifest.RepositoryId);
        Assert.Equal(secondSnapshot.Repository.Id, second.Manifest.RepositoryId);
    }

    [Fact]
    public async Task SyncAsync_IncompatibleKnowledgeSchemaRebuildsSafely()
    {
        using var fixture = new MemoryFixture();
        var snapshot = ProjectMemoryTestFactory.Create();
        var first = await fixture.Service.SyncAsync(snapshot);
        var unmanaged = Path.Combine(first.Location, "curated.md");
        File.WriteAllText(unmanaged, "curated");
        var manifestPath = Path.Combine(first.Location, "manifest.json");
        File.WriteAllText(manifestPath, File.ReadAllText(manifestPath)
            .Replace("\"knowledgeSchemaVersion\": 1", "\"knowledgeSchemaVersion\": 999", StringComparison.Ordinal));

        var result = await fixture.Service.SyncAsync(snapshot);

        Assert.Equal(ProjectMemorySyncMode.Rebuild, result.Mode);
        Assert.True(result.Integrity.IsValid);
        Assert.True(File.Exists(unmanaged));
        Assert.Equal(1, result.Manifest.KnowledgeSchemaVersion);
    }

    [Fact]
    public async Task ValidateStoredAsync_DetectsMissingManagedNote()
    {
        using var fixture = new MemoryFixture();
        var snapshot = ProjectMemoryTestFactory.Create();
        var result = await fixture.Service.SyncAsync(snapshot);
        var note = result.Manifest.Notes.First();
        File.Delete(Path.Combine(result.Location, Native(note.RelativePath)));

        var integrity = await new ProjectMemoryValidator().ValidateStoredAsync(
            result.Location,
            result.Manifest,
            snapshot);

        Assert.False(integrity.IsValid);
        Assert.Equal(1, integrity.MissingManagedNotes);
    }

    [Fact]
    public void Validate_DetectsBrokenLinkDuplicateIdentityAndRepositoryMismatch()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var build = new ProjectMemoryBuilder().Build(snapshot);
        var first = build.Notes[0];
        var broken = first with { Content = first.Content + "\n[[components/missing|Missing]]\n" };
        var duplicateEntry = build.Manifest.Notes[1] with
        {
            Identity = first.ManifestEntry.Identity,
            RelativePath = first.ManifestEntry.RelativePath
        };
        var manifest = build.Manifest with
        {
            RepositoryId = "other-repository",
            Notes = build.Manifest.Notes.Concat([duplicateEntry]).ToArray()
        };
        var tampered = new ProjectMemoryBuild(
            manifest,
            build.Notes.Select(note => note == first ? broken : note).ToArray());

        var integrity = new ProjectMemoryValidator().Validate(tampered, snapshot);

        Assert.False(integrity.IsValid);
        Assert.Equal(1, integrity.DuplicatePaths);
        Assert.Equal(1, integrity.DuplicateIdentities);
        Assert.Equal(1, integrity.BrokenLinks);
        Assert.True(integrity.MetadataMismatches > 0);
    }

    [Fact]
    public void Validate_DetectsManagedNoteWhoseFrontmatterNoLongerMatchesManifest()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var build = new ProjectMemoryBuilder().Build(snapshot);
        var original = build.Notes[0];
        var content = original.Content.Replace(
            "managed_by: engineering-brain",
            "managed_by: someone-else",
            StringComparison.Ordinal);
        var entry = original.ManifestEntry with { ContentHash = KnowledgeIdentity.ContentHash(content) };
        var notes = build.Notes.Select(note => note == original ? new KnowledgeNote(entry, content) : note).ToArray();
        var manifest = build.Manifest with
        {
            Notes = build.Manifest.Notes.Select(note => note == original.ManifestEntry ? entry : note).ToArray()
        };

        var integrity = new ProjectMemoryValidator().Validate(
            new ProjectMemoryBuild(manifest, notes),
            snapshot);

        Assert.False(integrity.IsValid);
        Assert.True(integrity.MetadataMismatches > 0);
    }

    private static RepositorySnapshot RemoveProject(RepositorySnapshot snapshot, string projectId) => snapshot with
    {
        Projects = snapshot.Projects.Where(project => project.Id != projectId).ToArray(),
        Entities = snapshot.Entities.Where(entity => entity.ProjectId != projectId).ToArray(),
        Relations = snapshot.Relations.Where(relation =>
            snapshot.Entities.Any(entity => entity.Id == relation.SourceEntityId && entity.ProjectId != projectId)
            && snapshot.Entities.Any(entity => entity.Id == relation.TargetEntityId && entity.ProjectId != projectId)).ToArray()
    };

    private static string Native(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    private sealed class MemoryFixture : IDisposable
    {
        public MemoryFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"engineering-brain-memory-{Guid.NewGuid():N}");
            Service = new ProjectMemoryService(store: new LocalProjectMemoryStore(Root));
        }

        public string Root { get; }

        public ProjectMemoryService Service { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
