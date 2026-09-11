using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class IncrementalChangeDetector
{
    public IReadOnlyList<FileChange> Detect(
        IReadOnlyList<ScannedFile> previousFiles,
        IReadOnlyList<ScannedFile> currentFiles,
        IReadOnlyList<GitRename>? gitRenames = null)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var previousByPath = previousFiles.ToDictionary(file => file.RelativePath, comparer);
        var currentByPath = currentFiles.ToDictionary(file => file.RelativePath, comparer);
        var changes = new List<FileChange>();

        foreach (var current in currentFiles)
        {
            if (!previousByPath.TryGetValue(current.RelativePath, out var previous))
            {
                changes.Add(new FileChange(
                    FileChangeKind.Added,
                    current.RelativePath,
                    null,
                    ChangeDetectionMethod.FileSystem,
                    null,
                    $"{current.RelativePath} exists only in the current repository state."));
                continue;
            }

            var comparison = Compare(previous, current);
            if (comparison is not null)
            {
                changes.Add(new FileChange(
                    FileChangeKind.Modified,
                    current.RelativePath,
                    previous.RelativePath,
                    comparison.Value.Method,
                    null,
                    comparison.Value.Reason));
            }
        }

        foreach (var previous in previousFiles.Where(file => !currentByPath.ContainsKey(file.RelativePath)))
        {
            changes.Add(new FileChange(
                FileChangeKind.Deleted,
                null,
                previous.RelativePath,
                ChangeDetectionMethod.FileSystem,
                null,
                $"{previous.RelativePath} exists only in the previous snapshot."));
        }

        ApplyProvenRenames(changes, gitRenames ?? [], comparer);
        return changes
            .OrderBy(change => change.CurrentPath ?? change.PreviousPath, comparer)
            .ThenBy(change => change.Kind)
            .ToArray();
    }

    private static (ChangeDetectionMethod Method, string Reason)? Compare(
        ScannedFile previous,
        ScannedFile current)
    {
        if (previous.ContentHash is not null && current.ContentHash is not null)
        {
            return previous.ContentHash.Equals(current.ContentHash, StringComparison.Ordinal)
                ? null
                : (ChangeDetectionMethod.ContentHash,
                    $"{current.RelativePath} content hash changed.");
        }

        if (previous.SizeBytes != current.SizeBytes
            || previous.LastWriteTimeUtc != current.LastWriteTimeUtc)
        {
            return (ChangeDetectionMethod.FileMetadata,
                $"{current.RelativePath} cannot be hashed safely and its file metadata changed.");
        }

        return null;
    }

    private static void ApplyProvenRenames(
        ICollection<FileChange> changes,
        IReadOnlyList<GitRename> gitRenames,
        StringComparer comparer)
    {
        foreach (var rename in gitRenames)
        {
            var deleted = changes.FirstOrDefault(change =>
                change.Kind == FileChangeKind.Deleted
                && change.PreviousPath is not null
                && comparer.Equals(change.PreviousPath, rename.PreviousPath));
            var added = changes.FirstOrDefault(change =>
                change.Kind == FileChangeKind.Added
                && change.CurrentPath is not null
                && comparer.Equals(change.CurrentPath, rename.CurrentPath));
            if (deleted is null || added is null)
            {
                continue;
            }

            changes.Remove(deleted);
            changes.Remove(added);
            changes.Add(new FileChange(
                FileChangeKind.Renamed,
                rename.CurrentPath,
                rename.PreviousPath,
                ChangeDetectionMethod.Git,
                null,
                $"Git reported a rename from {rename.PreviousPath} to {rename.CurrentPath}."));
        }
    }
}
