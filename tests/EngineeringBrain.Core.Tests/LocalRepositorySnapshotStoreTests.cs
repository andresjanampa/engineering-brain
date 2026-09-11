using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class LocalRepositorySnapshotStoreTests
{
    [Fact]
    public async Task LoadLatestAsync_ReturnsNotFoundWithoutFailing()
    {
        using var fixture = new StoreFixture();

        var result = await fixture.Store.LoadLatestAsync("missing");

        Assert.Equal(SnapshotLoadStatus.NotFound, result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task LoadLatestAsync_ClassifiesCorruptSnapshot()
    {
        using var fixture = new StoreFixture();
        fixture.WriteLatest("repo", "not-json");

        var result = await fixture.Store.LoadLatestAsync("repo");

        Assert.Equal(SnapshotLoadStatus.Corrupt, result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task LoadLatestAsync_ClassifiesIncompatibleSchema()
    {
        using var fixture = new StoreFixture();
        fixture.WriteLatest("repo", "{ \"schemaVersion\": 2 }");

        var result = await fixture.Store.LoadLatestAsync("repo");

        Assert.Equal(SnapshotLoadStatus.Incompatible, result.Status);
        Assert.Contains("incompatible", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoadLatestAsync_ClassifiesIncompleteCurrentSchemaAsCorrupt()
    {
        using var fixture = new StoreFixture();
        fixture.WriteLatest("repo", "{ \"schemaVersion\": 3 }");

        var result = await fixture.Store.LoadLatestAsync("repo");

        Assert.Equal(SnapshotLoadStatus.Corrupt, result.Status);
    }

    [Fact]
    public async Task SaveAsync_ReplacesLatestAndLeavesNoTemporaryFile()
    {
        using var fixture = new StoreFixture();
        var snapshot = SnapshotTestFactory.Create();

        var path = await fixture.Store.SaveAsync(snapshot);
        var loaded = await fixture.Store.LoadLatestAsync(snapshot.Repository.Id);

        Assert.Equal(SnapshotLoadStatus.Loaded, loaded.Status);
        Assert.Equal(snapshot.Repository.Id, loaded.Snapshot!.Repository.Id);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    private sealed class StoreFixture : IDisposable
    {
        public StoreFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"engineering-brain-store-{Guid.NewGuid():N}");
            Store = new LocalRepositorySnapshotStore(Root);
        }

        public string Root { get; }

        public LocalRepositorySnapshotStore Store { get; }

        public void WriteLatest(string repositoryId, string contents)
        {
            var path = Path.Combine(Root, "repositories", repositoryId, "snapshots", "latest.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
