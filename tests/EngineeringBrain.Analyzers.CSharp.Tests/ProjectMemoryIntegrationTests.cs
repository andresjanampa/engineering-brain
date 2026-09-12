using EngineeringBrain.Analyzers.CSharp;
using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Analyzers.CSharp.Tests;

public sealed class ProjectMemoryIntegrationTests
{
    [Fact]
    public async Task SemanticSnapshotSynchronizesIncrementallyAndRemovesStaleComponentNote()
    {
        using var repository = new TemporaryRepository();
        var dataRoot = Path.Combine(Path.GetTempPath(), $"engineering-brain-memory-integration-{Guid.NewGuid():N}");
        try
        {
            repository.WriteSdkProject("Core/Core.csproj");
            repository.Write(
                "Core/Services.cs",
                "namespace Core; public interface IService { void Run(); } public class Service : IService { public void Run() { } } public record Model(int Id);");
            repository.WriteSdkProject("Business/Business.csproj", "..\\Core\\Core.csproj");
            repository.Write(
                "Business/BusinessService.cs",
                "using Core; namespace Business; public class BusinessService : IService { public void Run() { } }");
            repository.WriteSolution(
                "Sample.sln",
                ("Core", "Core\\Core.csproj", "11111111-1111-1111-1111-111111111111"),
                ("Business", "Business\\Business.csproj", "22222222-2222-2222-2222-222222222222"));

            var engine = new RepositoryAnalysisEngine(
                new RepositoryScanner(),
                new BranchGitInfoProvider(),
                [new CSharpAnalyzer()],
                new LocalRepositorySnapshotStore(dataRoot));
            var memory = new ProjectMemoryService(store: new LocalProjectMemoryStore(dataRoot));

            var firstAnalysis = await engine.ScanAsync(repository.Root);
            var first = await memory.SyncAsync(firstAnalysis.Snapshot);
            var timestamps = first.Manifest.Notes.ToDictionary(
                note => note.RelativePath,
                note => File.GetLastWriteTimeUtc(Resolve(first.Location, note.RelativePath)),
                StringComparer.OrdinalIgnoreCase);

            var unchangedAnalysis = await engine.ScanAsync(repository.Root);
            var unchanged = await memory.SyncAsync(unchangedAnalysis.Snapshot);

            Assert.Equal(ProjectMemorySyncMode.Initialize, first.Mode);
            Assert.Equal(2, first.Metrics.ProjectNotes);
            Assert.Equal(4, first.Metrics.ComponentNotes);
            Assert.Equal(ProjectMemorySyncMode.Incremental, unchanged.Mode);
            Assert.Equal(0, unchanged.Metrics.Created);
            Assert.Equal(0, unchanged.Metrics.Updated);
            Assert.Equal(0, unchanged.Metrics.Deleted);
            Assert.Equal(unchanged.Metrics.TotalManagedNotes, unchanged.Metrics.Reused);
            Assert.All(unchanged.Manifest.Notes, note => Assert.Equal(
                timestamps[note.RelativePath],
                File.GetLastWriteTimeUtc(Resolve(unchanged.Location, note.RelativePath))));
            Assert.Contains(firstAnalysis.Snapshot.Relations, relation =>
                relation.RelationType == CodeRelationType.ReferencesProject);
            Assert.Contains(firstAnalysis.Snapshot.Relations, relation =>
                relation.RelationType == CodeRelationType.Implements
                && relation.ResolutionLevel == ResolutionLevel.Semantic);

            var businessProject = unchanged.Manifest.Notes.Single(note =>
                note.Kind == KnowledgeNoteKind.Project && note.RelativePath.Contains("Business", StringComparison.Ordinal));
            var businessComponent = unchanged.Manifest.Notes.Single(note =>
                note.Kind == KnowledgeNoteKind.Component && note.RelativePath.Contains("BusinessService", StringComparison.Ordinal));
            var businessProjectTimestamp = File.GetLastWriteTimeUtc(Resolve(unchanged.Location, businessProject.RelativePath));
            var businessComponentTimestamp = File.GetLastWriteTimeUtc(Resolve(unchanged.Location, businessComponent.RelativePath));
            repository.Write(
                "Core/Services.cs",
                "namespace Core; public interface IService { void Run(); } public class Service : IService { public int Version => 2; public void Run() { } } public record Model(int Id);");

            var changedAnalysis = await engine.ScanAsync(repository.Root);
            var changed = await memory.SyncAsync(changedAnalysis.Snapshot);

            Assert.True(changed.Metrics.Updated > 0);
            Assert.Equal(businessProjectTimestamp, File.GetLastWriteTimeUtc(Resolve(changed.Location, businessProject.RelativePath)));
            Assert.Equal(businessComponentTimestamp, File.GetLastWriteTimeUtc(Resolve(changed.Location, businessComponent.RelativePath)));

            var modelNote = changed.Manifest.Notes.Single(note =>
                note.Kind == KnowledgeNoteKind.Component && note.RelativePath.Contains("Model", StringComparison.Ordinal));
            repository.Write(
                "Core/Services.cs",
                "namespace Core; public interface IService { void Run(); } public class Service : IService { public int Version => 2; public void Run() { } }");

            var deletedAnalysis = await engine.ScanAsync(repository.Root);
            var deleted = await memory.SyncAsync(deletedAnalysis.Snapshot);

            Assert.Equal(1, deleted.Metrics.Deleted);
            Assert.False(File.Exists(Resolve(deleted.Location, modelNote.RelativePath)));
            Assert.True(deleted.Integrity.IsValid);
        }
        finally
        {
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    private static string Resolve(string root, string relativePath) => Path.Combine(
        root,
        relativePath.Replace('/', Path.DirectorySeparatorChar));

    private sealed class BranchGitInfoProvider : IGitInfoProvider
    {
        public Task<GitInfo> GetInfoAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitInfo(true, "feature/memory-test", "abc123", null, false));
    }
}
