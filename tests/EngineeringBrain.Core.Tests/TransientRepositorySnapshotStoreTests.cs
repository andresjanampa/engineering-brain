using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class TransientRepositorySnapshotStoreTests
{
    [Fact]
    public async Task LoadLatestAsync_BeforeSaveReturnsNotFound()
    {
        var result = await new TransientRepositorySnapshotStore().LoadLatestAsync("repository:test");

        Assert.Equal(SnapshotLoadStatus.NotFound, result.Status);
        Assert.Null(result.Snapshot);
        Assert.Equal("(transient)", result.Path);
    }

    [Fact]
    public async Task SaveAndLoadAsync_RemainInMemory()
    {
        var store = new TransientRepositorySnapshotStore();
        var snapshot = ProjectMemoryTestFactory.Create();

        var path = await store.SaveAsync(snapshot);
        var loaded = await store.LoadLatestAsync(snapshot.Repository.Id);

        Assert.Equal("(transient)", path);
        Assert.Equal(SnapshotLoadStatus.Loaded, loaded.Status);
        Assert.Same(snapshot, loaded.Snapshot);
    }

    [Fact]
    public async Task SaveAndLoadAsync_CreateNoFilesystemArtifacts()
    {
        var sentinel = Path.Combine(Path.GetTempPath(), $"engineering-brain-transient-{Guid.NewGuid():N}");
        var store = new TransientRepositorySnapshotStore();
        var snapshot = ProjectMemoryTestFactory.Create();

        await store.SaveAsync(snapshot);
        await store.LoadLatestAsync(snapshot.Repository.Id);

        Assert.False(Directory.Exists(sentinel));
        Assert.False(File.Exists(sentinel));
    }

    [Fact]
    public async Task Operations_HonorAlreadyCancelledToken()
    {
        var store = new TransientRepositorySnapshotStore();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => store.SaveAsync(ProjectMemoryTestFactory.Create(), cancellation.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => store.LoadLatestAsync("repository:test", cancellation.Token));
    }
}
