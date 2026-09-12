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

    public async Task<SnapshotLoadResult> LoadLatestAsync(
        string repositoryId,
        CancellationToken cancellationToken = default)
    {
        var path = GetLatestPath(repositoryId);
        if (!File.Exists(path))
        {
            return new SnapshotLoadResult(
                SnapshotLoadStatus.NotFound,
                null,
                path,
                "No previous snapshot was found.");
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaElement)
                || !schemaElement.TryGetInt32(out var schemaVersion))
            {
                return new SnapshotLoadResult(
                    SnapshotLoadStatus.Corrupt,
                    null,
                    path,
                    "The previous snapshot has no valid schema version.");
            }

            if (schemaVersion != SnapshotJsonSerializer.CurrentSchemaVersion)
            {
                return new SnapshotLoadResult(
                    SnapshotLoadStatus.Incompatible,
                    null,
                    path,
                    $"Snapshot schema {schemaVersion} is incompatible with schema {SnapshotJsonSerializer.CurrentSchemaVersion}.");
            }

            return new SnapshotLoadResult(
                SnapshotLoadStatus.Loaded,
                SnapshotJsonSerializer.Deserialize(json),
                path,
                "A compatible previous snapshot was loaded.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or System.Text.Json.JsonException)
        {
            return new SnapshotLoadResult(
                SnapshotLoadStatus.Corrupt,
                null,
                path,
                "The previous snapshot could not be read reliably and will be regenerated.");
        }
    }

    public async Task<string> SaveAsync(
        RepositorySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var snapshotsDirectory = Path.GetDirectoryName(GetLatestPath(snapshot.Repository.Id))!;
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

    private string GetLatestPath(string repositoryId) => Path.Combine(
        _dataRoot,
        "repositories",
        repositoryId,
        "snapshots",
        "latest.json");
}
