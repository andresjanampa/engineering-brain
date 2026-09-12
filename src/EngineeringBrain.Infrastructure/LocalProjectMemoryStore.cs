using System.Text;
using System.Text.Json;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class LocalProjectMemoryStore
{
    private readonly string _dataRoot;

    public LocalProjectMemoryStore(string? dataRoot = null)
    {
        _dataRoot = dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".engineering-brain");
    }

    public string GetBranchLocation(string repositoryId, string branchKey) => Path.Combine(
        _dataRoot,
        "repositories",
        KnowledgeIdentity.CreateRepositoryKey(repositoryId),
        "knowledge",
        "branches",
        branchKey);

    internal async Task<ProjectMemoryStoreState> LoadAsync(
        string repositoryId,
        string branchKey,
        CancellationToken cancellationToken)
    {
        var location = GetBranchLocation(repositoryId, branchKey);
        var path = Path.Combine(location, "manifest.json");
        if (!File.Exists(path))
        {
            return new ProjectMemoryStoreState(ProjectMemoryStoreStatus.NotFound, location, null);
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("knowledgeSchemaVersion", out var schema)
                || !schema.TryGetInt32(out var schemaVersion)
                || schemaVersion != ProjectMemoryManifestSerializer.CurrentKnowledgeSchemaVersion)
            {
                return new ProjectMemoryStoreState(ProjectMemoryStoreStatus.Incompatible, location, null);
            }

            return new ProjectMemoryStoreState(
                ProjectMemoryStoreStatus.Loaded,
                location,
                ProjectMemoryManifestSerializer.Deserialize(json));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException
            or JsonException)
        {
            return new ProjectMemoryStoreState(ProjectMemoryStoreStatus.Corrupt, location, null);
        }
    }

    internal async Task ApplyAsync(
        ProjectMemoryBuild build,
        ProjectMemoryStoreState previous,
        IReadOnlyList<KnowledgeNote> notesToWrite,
        IReadOnlyList<ManagedKnowledgeNote> notesToDelete,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(previous.Location);
        foreach (var note in notesToWrite)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteAtomicallyAsync(
                ResolveManagedPath(previous.Location, note.ManifestEntry.RelativePath),
                note.Content,
                cancellationToken);
        }

        foreach (var note in notesToDelete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ResolveManagedPath(previous.Location, note.RelativePath);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        var manifestPath = Path.Combine(previous.Location, "manifest.json");
        var manifestJson = ProjectMemoryManifestSerializer.Serialize(build.Manifest);
        if (!File.Exists(manifestPath)
            || !string.Equals(
                await File.ReadAllTextAsync(manifestPath, cancellationToken),
                manifestJson,
                StringComparison.Ordinal))
        {
            await WriteAtomicallyAsync(manifestPath, manifestJson, cancellationToken);
        }

        RemoveEmptyManagedDirectories(previous.Location);
    }

    internal static async Task<bool> MatchesAsync(
        string location,
        KnowledgeNote desired,
        ManagedKnowledgeNote previous,
        CancellationToken cancellationToken)
    {
        if (!previous.SourceFingerprint.Equals(desired.ManifestEntry.SourceFingerprint, StringComparison.Ordinal)
            || !previous.ContentHash.Equals(desired.ManifestEntry.ContentHash, StringComparison.Ordinal))
        {
            return false;
        }

        var path = ResolveManagedPath(location, desired.ManifestEntry.RelativePath);
        if (!File.Exists(path))
        {
            return false;
        }

        var content = await File.ReadAllTextAsync(path, cancellationToken);
        return KnowledgeIdentity.ContentHash(content)
            .Equals(desired.ManifestEntry.ContentHash, StringComparison.Ordinal);
    }

    private static async Task WriteAtomicallyAsync(
        string destination,
        string content,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = Path.Combine(
            Path.GetDirectoryName(destination)!,
            $".{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                KnowledgeIdentity.NormalizeLineEndings(content),
                new UTF8Encoding(false),
                cancellationToken);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string ResolveManagedPath(string location, string relativePath)
    {
        var root = Path.GetFullPath(location);
        var path = Path.GetFullPath(Path.Combine(
            root,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Managed note path escapes the branch knowledge directory.");
        }

        return path;
    }

    private static void RemoveEmptyManagedDirectories(string location)
    {
        foreach (var name in new[] { "components", "projects", "architecture" })
        {
            var path = Path.Combine(location, name);
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
    }
}

internal enum ProjectMemoryStoreStatus
{
    Loaded,
    NotFound,
    Incompatible,
    Corrupt
}

internal sealed record ProjectMemoryStoreState(
    ProjectMemoryStoreStatus Status,
    string Location,
    ProjectMemoryManifest? Manifest);
