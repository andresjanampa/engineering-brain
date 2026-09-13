using System.Collections.Concurrent;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class TransientRepositorySnapshotStore : IRepositorySnapshotStore
{
    private const string TransientPath = "(transient)";
    private readonly ConcurrentDictionary<string, RepositorySnapshot> _snapshots =
        new(StringComparer.Ordinal);

    public Task<SnapshotLoadResult> LoadLatestAsync(
        string repositoryId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryId);

        return Task.FromResult(_snapshots.TryGetValue(repositoryId, out var snapshot)
            ? new SnapshotLoadResult(
                SnapshotLoadStatus.Loaded,
                snapshot,
                TransientPath,
                "A compatible transient snapshot was loaded.")
            : new SnapshotLoadResult(
                SnapshotLoadStatus.NotFound,
                null,
                TransientPath,
                "No previous transient snapshot was found."));
    }

    public Task<string> SaveAsync(
        RepositorySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(snapshot);
        _snapshots[snapshot.Repository.Id] = snapshot;
        return Task.FromResult(TransientPath);
    }
}
