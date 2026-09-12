using System.Text;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class LocalRepositorySnapshotStore : IRepositorySnapshotStore
{
    private readonly string _dataRoot;

    public LocalRepositorySnapshotStore(string? dataRoot = null)
    {
        _dataRoot = dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".engineering-brain");
    }

    public async Task<string> SaveAsync(
        RepositorySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var snapshotsDirectory = Path.Combine(
            _dataRoot,
            "repositories",
            snapshot.Repository.Id,
            "snapshots");
        Directory.CreateDirectory(snapshotsDirectory);

        var destination = Path.Combine(snapshotsDirectory, "latest.json");
        var temporary = Path.Combine(snapshotsDirectory, $".{Guid.NewGuid():N}.tmp");
        var json = SnapshotJsonSerializer.Serialize(snapshot);

        try
        {
            await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false), cancellationToken);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return destination;
    }
}
