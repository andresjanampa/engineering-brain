using System.Diagnostics;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class ProjectMemoryService
{
    private readonly ProjectMemoryBuilder _builder;
    private readonly LocalProjectMemoryStore _store;
    private readonly ProjectMemoryValidator _validator;

    public ProjectMemoryService(
        ProjectMemoryBuilder? builder = null,
        LocalProjectMemoryStore? store = null,
        ProjectMemoryValidator? validator = null)
    {
        _builder = builder ?? new ProjectMemoryBuilder();
        _store = store ?? new LocalProjectMemoryStore();
        _validator = validator ?? new ProjectMemoryValidator();
    }

    public async Task<ProjectMemorySyncResult> SyncAsync(
        RepositorySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var build = _builder.Build(snapshot);
        var plannedIntegrity = _validator.Validate(build, snapshot);
        _validator.ThrowIfInvalid(plannedIntegrity);
        var previous = await _store.LoadAsync(
            snapshot.Repository.Id,
            build.Manifest.BranchKey,
            cancellationToken);
        var previousManifest = IsReusable(previous.Manifest, build.Manifest)
            ? previous.Manifest
            : null;
        var mode = previous.Status == ProjectMemoryStoreStatus.NotFound
            ? ProjectMemorySyncMode.Initialize
            : previousManifest is null
                ? ProjectMemorySyncMode.Rebuild
                : ProjectMemorySyncMode.Incremental;
        var previousByPath = previousManifest?.Notes.ToDictionary(
                note => note.RelativePath,
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, ManagedKnowledgeNote>(StringComparer.OrdinalIgnoreCase);
        var notesToWrite = new List<KnowledgeNote>();
        var created = 0;
        var updated = 0;
        var reused = 0;

        foreach (var note in build.Notes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (previousByPath.TryGetValue(note.ManifestEntry.RelativePath, out var previousNote)
                && await LocalProjectMemoryStore.MatchesAsync(
                    previous.Location,
                    note,
                    previousNote,
                    cancellationToken))
            {
                reused++;
                continue;
            }

            notesToWrite.Add(note);
            if (File.Exists(Path.Combine(
                    previous.Location,
                    note.ManifestEntry.RelativePath.Replace('/', Path.DirectorySeparatorChar))))
            {
                updated++;
            }
            else
            {
                created++;
            }
        }

        var desiredPaths = build.Manifest.Notes
            .Select(note => note.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var notesToDelete = previousByPath.Values
            .Where(note => !desiredPaths.Contains(note.RelativePath))
            .OrderBy(note => note.RelativePath, StringComparer.Ordinal)
            .ToArray();
        await _store.ApplyAsync(
            build,
            previous,
            notesToWrite,
            notesToDelete,
            cancellationToken);
        var integrity = await _validator.ValidateStoredAsync(
            previous.Location,
            build.Manifest,
            snapshot,
            cancellationToken);
        _validator.ThrowIfInvalid(integrity);
        var metrics = new ProjectMemorySyncMetrics(
            build.Manifest.Notes.Count(note => note.Kind == KnowledgeNoteKind.Project),
            build.Manifest.Notes.Count(note => note.Kind == KnowledgeNoteKind.Component),
            build.Manifest.Notes.Count,
            created,
            updated,
            notesToDelete.Length,
            reused,
            stopwatch.ElapsedMilliseconds);
        return new ProjectMemorySyncResult(
            mode,
            previous.Location,
            build.Manifest,
            metrics,
            integrity,
            snapshot);
    }

    private static bool IsReusable(
        ProjectMemoryManifest? previous,
        ProjectMemoryManifest current) => previous is not null
        && previous.KnowledgeSchemaVersion == ProjectMemoryManifestSerializer.CurrentKnowledgeSchemaVersion
        && previous.RepositoryId.Equals(current.RepositoryId, StringComparison.Ordinal)
        && previous.Branch.Equals(current.Branch, StringComparison.Ordinal)
        && previous.BranchKey.Equals(current.BranchKey, StringComparison.Ordinal)
        && previous.SourceSnapshotSchema == current.SourceSnapshotSchema
        && previous.SourceAnalyzerVersion.Equals(current.SourceAnalyzerVersion, StringComparison.Ordinal);
}
