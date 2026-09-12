using System.Text;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class ProjectMemoryBuilder
{
    public const int MaximumComponentsPerProjectNote = 100;
    public const int MaximumMembersPerComponentNote = 40;
    public const int MaximumLocationsPerComponentNote = 10;
    public const int MaximumDiagnosticsPerProjectNote = 10;

    private static readonly HashSet<CodeEntityType> ComponentTypes =
    [
        CodeEntityType.Class,
        CodeEntityType.Interface,
        CodeEntityType.Record,
        CodeEntityType.Enum
    ];

    public ProjectMemoryBuild Build(RepositorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.SchemaVersion != SnapshotJsonSerializer.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Project Memory requires snapshot schema {SnapshotJsonSerializer.CurrentSchemaVersion}; found {snapshot.SchemaVersion}.");
        }

        if (!snapshot.Incremental.GraphIntegrity.IsValid)
        {
            throw new InvalidDataException("Project Memory requires a snapshot with a valid code graph.");
        }

        var branch = snapshot.Git.Branch ?? "(no branch)";
        var branchKey = KnowledgeIdentity.CreateBranchKey(branch);
        var entitiesById = snapshot.Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var projectEntities = snapshot.Entities
            .Where(entity => entity.EntityType == CodeEntityType.Project)
            .ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var parentByEntity = snapshot.Relations
            .Where(relation => relation.RelationType == CodeRelationType.Contains)
            .GroupBy(relation => relation.TargetEntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().SourceEntityId, StringComparer.Ordinal);
        var topLevelComponents = snapshot.Entities
            .Where(entity => ComponentTypes.Contains(entity.EntityType))
            .Where(entity => !IsNestedComponent(entity, parentByEntity, entitiesById))
            .OrderBy(entity => entity.FullName, StringComparer.Ordinal)
            .ThenBy(entity => entity.Id, StringComparer.Ordinal)
            .ToArray();
        var projectPaths = snapshot.Projects.ToDictionary(
            project => project.Id,
            project => $"projects/{KnowledgeIdentity.CreateNoteFileName(project.Name, project.Id)}",
            StringComparer.Ordinal);
        var componentPaths = topLevelComponents.ToDictionary(
            entity => entity.Id,
            entity => $"components/{KnowledgeIdentity.CreateNoteFileName(entity.Name, entity.Id)}",
            StringComparer.Ordinal);
        var notes = new List<KnowledgeNote>();

        notes.Add(CreateNote(
            snapshot,
            KnowledgeNoteKind.RootIndex,
            "root-index",
            "index.md",
            null,
            null,
            null,
            BuildRootIndex(snapshot, branch, topLevelComponents.Length, projectPaths)));
        notes.Add(CreateNote(
            snapshot,
            KnowledgeNoteKind.ArchitectureOverview,
            "architecture-overview",
            "architecture/overview.md",
            null,
            null,
            null,
            BuildArchitectureOverview(snapshot, branch, projectPaths, projectEntities)));

        foreach (var project in snapshot.Projects.OrderBy(project => project.RelativePath, StringComparer.Ordinal))
        {
            var components = topLevelComponents
                .Where(entity => entity.ProjectId == project.Id)
                .OrderBy(entity => entity.FullName, StringComparer.Ordinal)
                .ThenBy(entity => entity.Id, StringComparer.Ordinal)
                .ToArray();
            notes.Add(CreateNote(
                snapshot,
                KnowledgeNoteKind.Project,
                $"project:{project.Id}",
                projectPaths[project.Id],
                project.Id,
                project.AnalysisMode.ToString(),
                null,
                BuildProjectNote(snapshot, project, components, projectPaths, componentPaths)));
        }

        foreach (var component in topLevelComponents)
        {
            notes.Add(CreateNote(
                snapshot,
                KnowledgeNoteKind.Component,
                $"component:{component.Id}",
                componentPaths[component.Id],
                component.Id,
                null,
                component.ResolutionLevel.ToString(),
                BuildComponentNote(
                    snapshot,
                    component,
                    projectPaths,
                    componentPaths,
                    parentByEntity,
                    entitiesById)));
        }

        var sourceFingerprint = KnowledgeIdentity.Fingerprint(notes
            .OrderBy(note => note.ManifestEntry.RelativePath, StringComparer.Ordinal)
            .Select(note => note.ManifestEntry.SourceFingerprint)
            .ToArray());
        notes.Add(CreateNote(
            snapshot,
            KnowledgeNoteKind.Log,
            "knowledge-log",
            "log.md",
            null,
            null,
            null,
            BuildLog(snapshot, branch, sourceFingerprint, notes.Count + 1)));

        var orderedNotes = notes.OrderBy(note => note.ManifestEntry.RelativePath, StringComparer.Ordinal).ToArray();
        var manifest = new ProjectMemoryManifest(
            ProjectMemoryManifestSerializer.CurrentKnowledgeSchemaVersion,
            snapshot.Repository.Id,
            snapshot.Repository.Name,
            branch,
            branchKey,
            snapshot.SchemaVersion,
            snapshot.Analysis.AnalyzerVersion,
            snapshot.Git.HeadCommit,
            orderedNotes.Select(note => note.ManifestEntry).ToArray());
        return new ProjectMemoryBuild(manifest, orderedNotes);
    }

    private static KnowledgeNote CreateNote(
        RepositorySnapshot snapshot,
        KnowledgeNoteKind kind,
        string identity,
        string relativePath,
        string? sourceId,
        string? analysisMode,
        string? resolution,
        string body)
    {
        body = EnsureSingleTrailingLineFeed(body);
        var branch = snapshot.Git.Branch ?? "(no branch)";
        var fingerprint = KnowledgeIdentity.Fingerprint(
            kind.ToString(),
            snapshot.Repository.Id,
            branch,
            snapshot.SchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            snapshot.Analysis.AnalyzerVersion,
            sourceId,
            analysisMode,
            resolution,
            body);
        var frontmatter = new List<string>
        {
            "---",
            $"knowledge_schema: {ProjectMemoryManifestSerializer.CurrentKnowledgeSchemaVersion}",
            $"type: {ToFrontmatterKind(kind)}",
            $"note_id: {KnowledgeIdentity.Quote(identity)}",
            $"repository_id: {KnowledgeIdentity.Quote(snapshot.Repository.Id)}",
            $"branch: {KnowledgeIdentity.Quote(branch)}",
            $"source_snapshot_schema: {snapshot.SchemaVersion}",
            $"analyzer_version: {KnowledgeIdentity.Quote(snapshot.Analysis.AnalyzerVersion)}"
        };
        if (sourceId is not null)
        {
            frontmatter.Add($"source_id: {KnowledgeIdentity.Quote(sourceId)}");
        }

        if (analysisMode is not null)
        {
            frontmatter.Add($"analysis_mode: {KnowledgeIdentity.Quote(analysisMode)}");
        }

        if (resolution is not null)
        {
            frontmatter.Add($"resolution: {KnowledgeIdentity.Quote(resolution)}");
        }

        frontmatter.Add($"source_fingerprint: {KnowledgeIdentity.Quote(fingerprint)}");
        frontmatter.Add("managed_by: engineering-brain");
        frontmatter.Add("---");
        var content = string.Join('\n', frontmatter) + "\n\n" + body;
        var entry = new ManagedKnowledgeNote(
            identity,
            kind,
            relativePath.Replace('\\', '/'),
            sourceId,
            fingerprint,
            KnowledgeIdentity.ContentHash(content));
        return new KnowledgeNote(entry, content);
    }

    private static string BuildRootIndex(
        RepositorySnapshot snapshot,
        string branch,
        int componentCount,
        IReadOnlyDictionary<string, string> projectPaths)
    {
        var lines = new List<string>
        {
            "# Engineering Brain Knowledge",
            string.Empty,
            $"Repository: `{Inline(snapshot.Repository.Name)}`",
            $"Branch: `{Inline(branch)}`",
            $"Analysis: `{snapshot.Analysis.Mode}`",
            string.Empty,
            "## Architecture",
            string.Empty,
            "- [[architecture/overview|Architecture overview]]",
            string.Empty,
            "## Projects",
            string.Empty
        };
        foreach (var project in snapshot.Projects.OrderBy(project => project.RelativePath, StringComparer.Ordinal))
        {
            lines.Add($"- {Link(projectPaths[project.Id], project.Name)}");
        }

        lines.AddRange(
        [
            string.Empty,
            "## Statistics",
            string.Empty,
            $"- Projects: {snapshot.Projects.Count}",
            $"- Components: {componentCount}",
            $"- Entities: {snapshot.Entities.Count}",
            $"- Relations: {snapshot.Relations.Count}",
            string.Empty,
            "Component notes are indexed from their project notes."
        ]);
        return Join(lines);
    }

    private static string BuildArchitectureOverview(
        RepositorySnapshot snapshot,
        string branch,
        IReadOnlyDictionary<string, string> projectPaths,
        IReadOnlyDictionary<string, CodeEntity> projectEntities)
    {
        var lines = new List<string>
        {
            "# Architecture Overview",
            string.Empty,
            $"Repository: `{Inline(snapshot.Repository.Name)}`",
            $"Branch: `{Inline(branch)}`",
            $"Commit: `{Inline(snapshot.Git.HeadCommit ?? "n/a")}`",
            $"Analysis: `{snapshot.Analysis.Mode}`",
            $"Analyzer: `{Inline(snapshot.Analysis.AnalyzerVersion)}`",
            string.Empty,
            "## Project Dependencies",
            string.Empty
        };
        var dependencies = snapshot.Relations
            .Where(relation => relation.RelationType == CodeRelationType.ReferencesProject)
            .Where(relation => projectEntities.ContainsKey(relation.SourceEntityId)
                && projectEntities.ContainsKey(relation.TargetEntityId))
            .OrderBy(relation => projectEntities[relation.SourceEntityId].Name, StringComparer.Ordinal)
            .ThenBy(relation => projectEntities[relation.TargetEntityId].Name, StringComparer.Ordinal)
            .ToArray();
        if (dependencies.Length == 0)
        {
            lines.Add("- None detected.");
        }
        else
        {
            foreach (var relation in dependencies)
            {
                var source = projectEntities[relation.SourceEntityId];
                var target = projectEntities[relation.TargetEntityId];
                lines.Add($"- {Link(projectPaths[source.Id], source.Name)} -> {Link(projectPaths[target.Id], target.Name)} (`{relation.ResolutionLevel}`)");
            }
        }

        lines.AddRange([string.Empty, "## Languages", string.Empty]);
        foreach (var language in snapshot.Languages.OrderByDescending(item => item.FileCount).ThenBy(item => item.Language, StringComparer.Ordinal).Take(20))
        {
            lines.Add($"- {language.Language}: {language.FileCount} files, {language.TotalBytes} bytes");
        }

        lines.AddRange([string.Empty, "## Entity Statistics", string.Empty]);
        foreach (var group in snapshot.Entities.GroupBy(entity => entity.EntityType).OrderBy(group => group.Key))
        {
            lines.Add($"- {group.Key}: {group.Count()}");
        }

        lines.AddRange([string.Empty, "## Relation Statistics", string.Empty]);
        foreach (var group in snapshot.Relations.GroupBy(relation => relation.RelationType).OrderBy(group => group.Key))
        {
            lines.Add($"- {group.Key}: {group.Count()}");
        }

        lines.AddRange([string.Empty, "## Diagnostics", string.Empty, $"Total: {snapshot.Diagnostics.Count}"]);
        foreach (var group in snapshot.Diagnostics
                     .GroupBy(diagnostic => new { diagnostic.Code, diagnostic.Severity })
                     .OrderBy(group => group.Key.Severity)
                     .ThenBy(group => group.Key.Code, StringComparer.Ordinal)
                     .Take(10))
        {
            lines.Add($"- `{group.Key.Code}` ({group.Key.Severity}): {group.Count()}");
        }

        if (snapshot.Diagnostics.Select(item => new { item.Code, item.Severity }).Distinct().Count() > 10)
        {
            lines.Add("- Additional diagnostic categories omitted.");
        }

        lines.AddRange([string.Empty, "## Projects", string.Empty]);
        foreach (var project in snapshot.Projects.OrderBy(project => project.RelativePath, StringComparer.Ordinal))
        {
            lines.Add($"- {Link(projectPaths[project.Id], project.Name)}");
        }

        return Join(lines);
    }

    private static string BuildProjectNote(
        RepositorySnapshot snapshot,
        ProjectInfo project,
        IReadOnlyList<CodeEntity> components,
        IReadOnlyDictionary<string, string> projectPaths,
        IReadOnlyDictionary<string, string> componentPaths)
    {
        var projectsByPath = snapshot.Projects.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        var referencedBy = snapshot.Projects
            .Where(candidate => candidate.ProjectReferences.Contains(project.RelativePath, StringComparer.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var entities = snapshot.Entities.Where(entity => entity.ProjectId == project.Id).ToArray();
        var lines = new List<string>
        {
            $"# {Heading(project.Name)}",
            string.Empty,
            $"Path: `{Inline(project.RelativePath)}`",
            $"Language: `{Inline(project.Language)}`",
            $"Analysis: `{project.AnalysisMode}`",
            $"Documents: {project.Documents.Count}",
            string.Empty,
            "## References",
            string.Empty
        };
        var references = project.ProjectReferences
            .Where(projectsByPath.ContainsKey)
            .Select(path => projectsByPath[path])
            .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
            .ToArray();
        AddProjectLinks(lines, references, projectPaths);
        lines.AddRange([string.Empty, "## Referenced By", string.Empty]);
        AddProjectLinks(lines, referencedBy, projectPaths);
        lines.AddRange([string.Empty, "## Components", string.Empty]);
        foreach (var component in components.Take(MaximumComponentsPerProjectNote))
        {
            lines.Add($"- {Link(componentPaths[component.Id], component.FullName)} (`{component.EntityType}`)");
        }

        if (components.Count > MaximumComponentsPerProjectNote)
        {
            lines.Add($"- Additional components: {components.Count - MaximumComponentsPerProjectNote}");
        }

        lines.AddRange([string.Empty, "## Statistics", string.Empty]);
        foreach (var group in entities.GroupBy(entity => entity.EntityType).OrderBy(group => group.Key))
        {
            lines.Add($"- {group.Key}: {group.Count()}");
        }

        lines.AddRange([string.Empty, "## Diagnostics", string.Empty, $"Total: {project.Diagnostics.Count}"]);
        foreach (var diagnostic in project.Diagnostics
                     .OrderBy(item => item.Severity)
                     .ThenBy(item => item.Code, StringComparer.Ordinal)
                     .Take(MaximumDiagnosticsPerProjectNote))
        {
            lines.Add($"- `{diagnostic.Code}` ({diagnostic.Severity})");
        }

        if (project.Diagnostics.Count > MaximumDiagnosticsPerProjectNote)
        {
            lines.Add($"- Additional diagnostics: {project.Diagnostics.Count - MaximumDiagnosticsPerProjectNote}");
        }

        lines.AddRange([string.Empty, $"Evidence: snapshot schema {snapshot.SchemaVersion}."]);
        return Join(lines);
    }

    private static string BuildComponentNote(
        RepositorySnapshot snapshot,
        CodeEntity component,
        IReadOnlyDictionary<string, string> projectPaths,
        IReadOnlyDictionary<string, string> componentPaths,
        IReadOnlyDictionary<string, string> parentByEntity,
        IReadOnlyDictionary<string, CodeEntity> entitiesById)
    {
        var project = snapshot.Projects.Single(item => item.Id == component.ProjectId);
        var namespaceName = FindNamespace(component, parentByEntity, entitiesById);
        var members = snapshot.Relations
            .Where(relation => relation.RelationType == CodeRelationType.Contains
                && relation.SourceEntityId == component.Id)
            .Select(relation => entitiesById[relation.TargetEntityId])
            .Where(entity => entity.EntityType is CodeEntityType.Method
                or CodeEntityType.Property
                or CodeEntityType.Constructor
                || ComponentTypes.Contains(entity.EntityType))
            .OrderBy(entity => entity.EntityType)
            .ThenBy(entity => entity.FullName, StringComparer.Ordinal)
            .ThenBy(entity => entity.Id, StringComparer.Ordinal)
            .ToArray();
        var typeRelations = snapshot.Relations
            .Where(relation => relation.SourceEntityId == component.Id
                && relation.RelationType is CodeRelationType.Inherits or CodeRelationType.Implements)
            .Where(relation => entitiesById.ContainsKey(relation.TargetEntityId))
            .OrderBy(relation => relation.RelationType)
            .ThenBy(relation => entitiesById[relation.TargetEntityId].FullName, StringComparer.Ordinal)
            .ToArray();
        var locations = new[]
            {
                new SourceLocation(component.RelativeFilePath, component.StartLine, component.EndLine)
            }
            .Concat(component.AdditionalLocations)
            .OrderBy(location => location.RelativeFilePath, StringComparer.Ordinal)
            .ThenBy(location => location.StartLine)
            .ToArray();
        var lines = new List<string>
        {
            $"# {Heading(component.Name)}",
            string.Empty,
            $"Type: `{component.EntityType}`",
            $"Full name: `{Inline(component.FullName)}`",
            $"Project: {Link(projectPaths[project.Id], project.Name)}",
            $"Namespace: `{Inline(namespaceName ?? "(global)")}`",
            $"Resolution: `{component.ResolutionLevel}`",
            string.Empty,
            "## Type Relationships",
            string.Empty
        };
        if (typeRelations.Length == 0)
        {
            lines.Add("- None detected.");
        }
        else
        {
            foreach (var relation in typeRelations)
            {
                var target = entitiesById[relation.TargetEntityId];
                var targetText = componentPaths.TryGetValue(target.Id, out var targetPath)
                    ? Link(targetPath, target.FullName)
                    : $"`{Inline(target.FullName)}`";
                lines.Add($"- {relation.RelationType}: {targetText} (`{relation.ResolutionLevel}`)");
            }
        }

        lines.AddRange([string.Empty, "## Members", string.Empty, $"Total: {members.Length}"]);
        foreach (var member in members.Take(MaximumMembersPerComponentNote))
        {
            lines.Add($"- {member.EntityType}: `{Inline(member.FullName)}`");
        }

        if (members.Length > MaximumMembersPerComponentNote)
        {
            lines.Add($"- Additional members: {members.Length - MaximumMembersPerComponentNote}");
        }

        lines.AddRange([string.Empty, "## Source Evidence", string.Empty]);
        foreach (var location in locations.Take(MaximumLocationsPerComponentNote))
        {
            lines.Add($"- `{Inline(location.RelativeFilePath)}` lines {location.StartLine}-{location.EndLine}");
        }

        if (locations.Length > MaximumLocationsPerComponentNote)
        {
            lines.Add($"- Additional locations: {locations.Length - MaximumLocationsPerComponentNote}");
        }

        return Join(lines);
    }

    private static string BuildLog(
        RepositorySnapshot snapshot,
        string branch,
        string sourceFingerprint,
        int managedNotes) => Join(
    [
        "# Knowledge Log",
        string.Empty,
        "Current deterministic synchronization source:",
        string.Empty,
        $"- Repository: `{Inline(snapshot.Repository.Name)}`",
        $"- Branch: `{Inline(branch)}`",
        $"- Commit: `{Inline(snapshot.Git.HeadCommit ?? "n/a")}`",
        $"- Source fingerprint: `{sourceFingerprint}`",
        $"- Managed notes: {managedNotes}",
        string.Empty,
        "Operational timestamps are omitted so unchanged knowledge remains stable."
    ]);

    private static bool IsNestedComponent(
        CodeEntity entity,
        IReadOnlyDictionary<string, string> parentByEntity,
        IReadOnlyDictionary<string, CodeEntity> entitiesById) =>
        parentByEntity.TryGetValue(entity.Id, out var parentId)
        && entitiesById.TryGetValue(parentId, out var parent)
        && ComponentTypes.Contains(parent.EntityType);

    private static string? FindNamespace(
        CodeEntity entity,
        IReadOnlyDictionary<string, string> parentByEntity,
        IReadOnlyDictionary<string, CodeEntity> entitiesById)
    {
        var currentId = entity.Id;
        while (parentByEntity.TryGetValue(currentId, out var parentId)
            && entitiesById.TryGetValue(parentId, out var parent))
        {
            if (parent.EntityType == CodeEntityType.Namespace)
            {
                return parent.FullName;
            }

            currentId = parent.Id;
        }

        return null;
    }

    private static void AddProjectLinks(
        ICollection<string> lines,
        IReadOnlyList<ProjectInfo> projects,
        IReadOnlyDictionary<string, string> projectPaths)
    {
        if (projects.Count == 0)
        {
            lines.Add("- None.");
            return;
        }

        foreach (var project in projects.Take(50))
        {
            lines.Add($"- {Link(projectPaths[project.Id], project.Name)}");
        }

        if (projects.Count > 50)
        {
            lines.Add($"- Additional projects: {projects.Count - 50}");
        }
    }

    private static string Link(string path, string alias) =>
        $"[[{KnowledgeIdentity.WikiTarget(path)}|{WikiAlias(alias)}]]";

    private static string WikiAlias(string value) => value.Replace('|', '-').Replace(']', ')');

    private static string Heading(string value) => value.Replace("\r", " ").Replace("\n", " ").Trim();

    private static string Inline(string value) => value.Replace('`', '\'').Replace("\r", " ").Replace("\n", " ");

    private static string Join(IEnumerable<string> lines) => EnsureSingleTrailingLineFeed(string.Join('\n', lines));

    private static string EnsureSingleTrailingLineFeed(string value) =>
        KnowledgeIdentity.NormalizeLineEndings(value).TrimEnd('\n') + "\n";

    private static string ToFrontmatterKind(KnowledgeNoteKind kind) => kind switch
    {
        KnowledgeNoteKind.RootIndex => "index",
        KnowledgeNoteKind.ArchitectureOverview => "architecture",
        KnowledgeNoteKind.Project => "project",
        KnowledgeNoteKind.Component => "component",
        KnowledgeNoteKind.Log => "log",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
