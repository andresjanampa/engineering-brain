using System.Text.RegularExpressions;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed partial class ProjectMemoryValidator
{
    public ProjectMemoryIntegritySummary Validate(
        ProjectMemoryBuild build,
        RepositorySnapshot snapshot)
    {
        var duplicatePaths = build.Manifest.Notes
            .GroupBy(note => note.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Count(group => group.Count() > 1);
        var duplicateIdentities = build.Manifest.Notes
            .GroupBy(note => note.Identity, StringComparer.Ordinal)
            .Count(group => group.Count() > 1);
        var notePaths = build.Manifest.Notes
            .Select(note => NormalizePath(note.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projectIds = snapshot.Projects.Select(project => project.Id).ToHashSet(StringComparer.Ordinal);
        var entityIds = snapshot.Entities.Select(entity => entity.Id).ToHashSet(StringComparer.Ordinal);
        var invalidSources = build.Manifest.Notes.Count(note => note.Kind switch
        {
            KnowledgeNoteKind.Project => note.SourceId is null || !projectIds.Contains(note.SourceId),
            KnowledgeNoteKind.Component => note.SourceId is null || !entityIds.Contains(note.SourceId),
            _ => note.SourceId is not null
        });
        var metadataMismatches = CountMetadataMismatches(build.Manifest, snapshot)
            + CountNoteMetadataMismatches(build);
        var brokenLinks = 0;
        foreach (var note in build.Notes)
        {
            foreach (Match match in WikiLink().Matches(note.Content))
            {
                var target = NormalizePath(match.Groups[1].Value);
                if (!target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    target += ".md";
                }

                if (!notePaths.Contains(target))
                {
                    brokenLinks++;
                }
            }
        }

        return CreateSummary(
            duplicatePaths,
            duplicateIdentities,
            0,
            0,
            brokenLinks,
            invalidSources,
            metadataMismatches);
    }

    public async Task<ProjectMemoryIntegritySummary> ValidateStoredAsync(
        string location,
        ProjectMemoryManifest manifest,
        RepositorySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var notes = new List<KnowledgeNote>();
        var missing = 0;
        foreach (var entry in manifest.Notes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ResolvePath(location, entry.RelativePath);
            if (!File.Exists(path))
            {
                missing++;
                continue;
            }

            var content = await File.ReadAllTextAsync(path, cancellationToken);
            notes.Add(new KnowledgeNote(entry, content));
        }

        var managedPaths = manifest.Notes
            .Select(note => NormalizePath(note.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphaned = 0;
        if (Directory.Exists(location))
        {
            foreach (var path in Directory.EnumerateFiles(location, "*.md", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = NormalizePath(Path.GetRelativePath(location, path));
                if (managedPaths.Contains(relative))
                {
                    continue;
                }

                var content = await File.ReadAllTextAsync(path, cancellationToken);
                if (content.Contains("managed_by: engineering-brain", StringComparison.Ordinal))
                {
                    orphaned++;
                }
            }
        }

        var buildSummary = Validate(new ProjectMemoryBuild(manifest, notes), snapshot);
        return CreateSummary(
            buildSummary.DuplicatePaths,
            buildSummary.DuplicateIdentities,
            missing,
            orphaned,
            buildSummary.BrokenLinks,
            buildSummary.InvalidSourceReferences,
            buildSummary.MetadataMismatches);
    }

    public void ThrowIfInvalid(ProjectMemoryIntegritySummary summary)
    {
        if (!summary.IsValid)
        {
            throw new InvalidDataException(
                "Project Memory integrity validation failed: "
                + $"duplicate paths={summary.DuplicatePaths}, duplicate identities={summary.DuplicateIdentities}, "
                + $"missing={summary.MissingManagedNotes}, orphaned={summary.OrphanedManagedNotes}, "
                + $"broken links={summary.BrokenLinks}, invalid sources={summary.InvalidSourceReferences}, "
                + $"metadata mismatches={summary.MetadataMismatches}.");
        }
    }

    private static int CountMetadataMismatches(
        ProjectMemoryManifest manifest,
        RepositorySnapshot snapshot)
    {
        var branch = snapshot.Git.Branch ?? "(no branch)";
        var count = 0;
        count += manifest.KnowledgeSchemaVersion == ProjectMemoryManifestSerializer.CurrentKnowledgeSchemaVersion ? 0 : 1;
        count += manifest.RepositoryId.Equals(snapshot.Repository.Id, StringComparison.Ordinal) ? 0 : 1;
        count += manifest.Branch.Equals(branch, StringComparison.Ordinal) ? 0 : 1;
        count += manifest.BranchKey.Equals(KnowledgeIdentity.CreateBranchKey(branch), StringComparison.Ordinal) ? 0 : 1;
        count += manifest.SourceSnapshotSchema == snapshot.SchemaVersion ? 0 : 1;
        count += manifest.SourceAnalyzerVersion.Equals(snapshot.Analysis.AnalyzerVersion, StringComparison.Ordinal) ? 0 : 1;
        return count;
    }

    private static int CountNoteMetadataMismatches(ProjectMemoryBuild build)
    {
        var manifestEntries = build.Manifest.Notes
            .GroupBy(note => note.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var mismatches = 0;
        foreach (var note in build.Notes)
        {
            var entry = note.ManifestEntry;
            if (!manifestEntries.TryGetValue(entry.RelativePath, out var candidates)
                || candidates.Length != 1
                || candidates[0] != entry)
            {
                mismatches++;
            }

            var expectedLines = new List<string>
            {
                $"knowledge_schema: {build.Manifest.KnowledgeSchemaVersion}",
                $"type: {ToFrontmatterKind(entry.Kind)}",
                $"note_id: {KnowledgeIdentity.Quote(entry.Identity)}",
                $"repository_id: {KnowledgeIdentity.Quote(build.Manifest.RepositoryId)}",
                $"branch: {KnowledgeIdentity.Quote(build.Manifest.Branch)}",
                $"source_snapshot_schema: {build.Manifest.SourceSnapshotSchema}",
                $"analyzer_version: {KnowledgeIdentity.Quote(build.Manifest.SourceAnalyzerVersion)}",
                $"source_fingerprint: {KnowledgeIdentity.Quote(entry.SourceFingerprint)}",
                "managed_by: engineering-brain"
            };
            if (entry.SourceId is not null)
            {
                expectedLines.Add($"source_id: {KnowledgeIdentity.Quote(entry.SourceId)}");
            }

            var lines = KnowledgeIdentity.NormalizeLineEndings(note.Content)
                .Split('\n')
                .ToHashSet(StringComparer.Ordinal);
            if (expectedLines.Any(expected => !lines.Contains(expected))
                || !KnowledgeIdentity.ContentHash(note.Content).Equals(entry.ContentHash, StringComparison.Ordinal))
            {
                mismatches++;
            }
        }

        mismatches += build.Manifest.Notes.Count(entry =>
            !build.Notes.Any(note => note.ManifestEntry.RelativePath.Equals(
                entry.RelativePath,
                StringComparison.OrdinalIgnoreCase)));
        return mismatches;
    }

    private static ProjectMemoryIntegritySummary CreateSummary(
        int duplicatePaths,
        int duplicateIdentities,
        int missing,
        int orphaned,
        int brokenLinks,
        int invalidSources,
        int metadataMismatches) => new(
        duplicatePaths == 0
            && duplicateIdentities == 0
            && missing == 0
            && orphaned == 0
            && brokenLinks == 0
            && invalidSources == 0
            && metadataMismatches == 0,
        duplicatePaths,
        duplicateIdentities,
        missing,
        orphaned,
        brokenLinks,
        invalidSources,
        metadataMismatches);

    private static string ResolvePath(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(
            fullRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Managed note path escapes the branch knowledge directory.");
        }

        return fullPath;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string ToFrontmatterKind(KnowledgeNoteKind kind) => kind switch
    {
        KnowledgeNoteKind.RootIndex => "index",
        KnowledgeNoteKind.ArchitectureOverview => "architecture",
        KnowledgeNoteKind.Project => "project",
        KnowledgeNoteKind.Component => "component",
        KnowledgeNoteKind.Log => "log",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    [GeneratedRegex(@"\[\[([^\]|]+)(?:\|[^\]]+)?\]\]")]
    private static partial Regex WikiLink();
}
