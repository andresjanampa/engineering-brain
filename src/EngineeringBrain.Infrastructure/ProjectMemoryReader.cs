using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class ProjectMemoryReader
{
    public async Task<string> ReadManagedNoteAsync(
        ProjectMemorySyncResult memory,
        ManagedKnowledgeNote note,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(note);
        if (!memory.Manifest.Notes.Any(candidate => candidate.Identity == note.Identity
            && candidate.RelativePath.Equals(note.RelativePath, StringComparison.Ordinal)))
        {
            throw new InvalidDataException("The requested note is not managed by the current manifest.");
        }

        var path = Resolve(memory.Location, note.RelativePath);
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"Managed note is missing: {note.RelativePath}");
        }

        var content = await File.ReadAllTextAsync(path, cancellationToken);
        if (!KnowledgeIdentity.ContentHash(content).Equals(note.ContentHash, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Managed note content hash is invalid: {note.RelativePath}");
        }

        return content;
    }

    private static string Resolve(string location, string relativePath)
    {
        var root = Path.GetFullPath(location);
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Managed note path escapes the branch knowledge directory.");
        }

        return path;
    }
}
