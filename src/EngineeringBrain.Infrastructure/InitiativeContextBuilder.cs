using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class InitiativeContextBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly ProjectMemoryReader _reader;
    private readonly TokenEstimator _estimator;
    private readonly TokenBudgetOptions _budget;

    public InitiativeContextBuilder(
        ProjectMemoryReader? reader = null,
        TokenEstimator? estimator = null,
        TokenBudgetOptions? budget = null)
    {
        _reader = reader ?? new ProjectMemoryReader();
        _estimator = estimator ?? new TokenEstimator();
        _budget = budget ?? new TokenBudgetOptions();
    }

    public async Task<InitiativeContext> BuildAsync(
        InitiativeUnderstanding understanding,
        CandidateRetrievalResult retrieval,
        ProjectMemorySyncResult memory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(understanding);
        ArgumentNullException.ThrowIfNull(retrieval);
        ArgumentNullException.ThrowIfNull(memory);

        var segments = new List<ContextSegment>();
        var included = new List<string>();
        var pruned = new List<string>();
        AddSegment(
            segments,
            ContextSegmentKind.InitiativeUnderstanding,
            JsonSerializer.Serialize(understanding, JsonOptions),
            "initiative-understanding",
            0,
            mandatory: true);
        AddSegment(
            segments,
            ContextSegmentKind.RepositoryIdentity,
            BuildRepositoryFacts(memory.SourceSnapshot),
            memory.SourceSnapshot.Repository.Id,
            0,
            mandatory: true);

        var rootNotes = memory.Manifest.Notes
            .Where(note => note.Kind is KnowledgeNoteKind.RootIndex or KnowledgeNoteKind.ArchitectureOverview)
            .OrderBy(note => note.Kind)
            .ToArray();
        await AddNotesWithinBudgetAsync(
            segments,
            included,
            pruned,
            rootNotes,
            memory,
            _budget.RootAndArchitectureTokens,
            required: true,
            cancellationToken);

        var notesBySource = memory.Manifest.Notes
            .Where(note => note.SourceId is not null)
            .GroupBy(note => note.SourceId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var projectNotes = retrieval.Projects
            .Select(candidate => notesBySource.GetValueOrDefault(candidate.ProjectId))
            .Where(note => note is not null)
            .Cast<ManagedKnowledgeNote>()
            .ToArray();
        await AddNotesWithinBudgetAsync(
            segments,
            included,
            pruned,
            projectNotes,
            memory,
            _budget.ProjectNotesTokens,
            required: false,
            cancellationToken);

        var componentNotes = retrieval.Components
            .Select(candidate => notesBySource.GetValueOrDefault(candidate.EntityId))
            .Where(note => note is not null)
            .Cast<ManagedKnowledgeNote>()
            .ToArray();
        await AddNotesWithinBudgetAsync(
            segments,
            included,
            pruned,
            componentNotes,
            memory,
            _budget.ComponentNotesTokens,
            required: false,
            cancellationToken);

        var graph = BuildGraphEvidence(retrieval, memory.SourceSnapshot);
        if (_estimator.Estimate(graph) <= _budget.GraphEvidenceTokens)
        {
            AddSegment(
                segments,
                ContextSegmentKind.GraphEvidence,
                graph,
                "selected-graph-evidence",
                0,
                mandatory: true);
        }

        var content = ContextSegmentRenderer.Render(segments);
        while (_estimator.Estimate(content) > _budget.MaximumReasoningInputTokens)
        {
            var optional = segments.FindLastIndex(segment => !segment.Mandatory);
            if (optional < 0)
            {
                throw new InvalidDataException(
                    $"Mandatory initiative context exceeds the {_budget.MaximumReasoningInputTokens} token hard limit.");
            }

            var removedPath = GetNotePath(segments[optional].Content);
            if (removedPath is not null)
            {
                included.Remove(removedPath);
                if (!pruned.Contains(removedPath, StringComparer.Ordinal))
                {
                    pruned.Add(removedPath);
                }
            }

            segments.RemoveAt(optional);
            content = ContextSegmentRenderer.Render(segments);
        }

        return new InitiativeContext(
            content,
            _estimator.Estimate(content),
            included,
            pruned,
            segments);
    }

    private async Task AddNotesWithinBudgetAsync(
        ICollection<ContextSegment> segments,
        ICollection<string> included,
        ICollection<string> pruned,
        IReadOnlyList<ManagedKnowledgeNote> notes,
        ProjectMemorySyncResult memory,
        int maximumTokens,
        bool required,
        CancellationToken cancellationToken)
    {
        var remaining = maximumTokens;
        for (var index = 0; index < notes.Count; index++)
        {
            var note = notes[index];
            var content = await _reader.ReadManagedNoteAsync(memory, note, cancellationToken);
            var formatted = $"NOTE: {note.RelativePath}\n{content}";
            var tokens = _estimator.Estimate(formatted);
            if (tokens > remaining)
            {
                if (required)
                {
                    throw new InvalidDataException($"Required managed note exceeds its token budget: {note.RelativePath}");
                }

                pruned.Add(note.RelativePath);
                continue;
            }

            var kind = note.Kind switch
            {
                KnowledgeNoteKind.RootIndex => ContextSegmentKind.RootIndex,
                KnowledgeNoteKind.ArchitectureOverview => ContextSegmentKind.ArchitectureOverview,
                KnowledgeNoteKind.Project => ContextSegmentKind.ProjectNote,
                KnowledgeNoteKind.Component => ContextSegmentKind.ComponentNote,
                _ => throw new InvalidDataException($"Knowledge note kind {note.Kind} cannot enter remote context.")
            };
            segments.Add(new ContextSegment(
                kind,
                formatted,
                tokens,
                note.SourceId ?? note.Identity,
                index + 1,
                required));
            included.Add(note.RelativePath);
            remaining -= tokens;
        }
    }

    private static string BuildRepositoryFacts(RepositorySnapshot snapshot)
    {
        var clean = snapshot.Git.IsWorkingTreeClean switch
        {
            true => "clean",
            false => "dirty",
            _ => "unknown"
        };
        return string.Join('\n',
            $"repositoryId: {snapshot.Repository.Id}",
            $"repositoryName: {snapshot.Repository.Name}",
            $"branch: {snapshot.Git.Branch ?? "(no branch)"}",
            $"commit: {snapshot.Git.HeadCommit ?? "n/a"}",
            $"workingTree: {clean}",
            $"snapshotSchema: {snapshot.SchemaVersion}",
            $"analysisMode: {snapshot.Analysis.Mode}",
            $"projects: {snapshot.Projects.Count}",
            $"entities: {snapshot.Entities.Count}",
            $"relations: {snapshot.Relations.Count}");
    }

    private static string BuildGraphEvidence(
        CandidateRetrievalResult retrieval,
        RepositorySnapshot snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Selected candidates are exhaustive snapshot identities; scores are deterministic.");
        foreach (var project in retrieval.Projects)
        {
            builder.Append("PROJECT | ").Append(project.ProjectId).Append(" | ")
                .Append(project.RelativePath).Append(" | score=").Append(project.Score).AppendLine();
        }

        foreach (var component in retrieval.Components)
        {
            builder.Append("ENTITY | ").Append(component.EntityId).Append(" | ")
                .Append(component.FullName).Append(" | project=").Append(component.ProjectId)
                .Append(" | path=").Append(component.RelativePath)
                .Append(" | resolution=").Append(component.ResolutionLevel)
                .Append(" | score=").Append(component.Score).AppendLine();
        }

        foreach (var relation in retrieval.Relations)
        {
            builder.Append("RELATION | ").Append(relation.SourceEntityId).Append(" | ")
                .Append(relation.RelationType).Append(" | ").Append(relation.TargetEntityId)
                .Append(" | path=").Append(relation.RelativePath)
                .Append(" | lines=").Append(relation.StartLine).Append('-').Append(relation.EndLine)
                .Append(" | resolution=").Append(relation.ResolutionLevel).AppendLine();
        }

        var selectedProjectIds = retrieval.Projects.Select(project => project.ProjectId).ToHashSet(StringComparer.Ordinal);
        var projectEntities = snapshot.Entities
            .Where(entity => entity.EntityType == CodeEntityType.Project && selectedProjectIds.Contains(entity.Id))
            .ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        foreach (var relation in snapshot.Relations
                     .Where(relation => relation.RelationType == CodeRelationType.ReferencesProject)
                     .Where(relation => projectEntities.ContainsKey(relation.SourceEntityId)
                         || projectEntities.ContainsKey(relation.TargetEntityId))
                     .OrderBy(relation => relation.SourceEntityId, StringComparer.Ordinal)
                     .ThenBy(relation => relation.TargetEntityId, StringComparer.Ordinal))
        {
            builder.Append("PROJECT_RELATION | ").Append(relation.SourceEntityId).Append(" | ReferencesProject | ")
                .Append(relation.TargetEntityId).Append(" | resolution=").Append(relation.ResolutionLevel).AppendLine();
        }

        if (retrieval.Components.Count == 0)
        {
            builder.AppendLine("NO_STRONG_COMPONENT_CANDIDATES");
        }

        return builder.ToString();
    }

    private void AddSegment(
        ICollection<ContextSegment> segments,
        ContextSegmentKind kind,
        string content,
        string? sourceIdentity,
        int rank,
        bool mandatory) => segments.Add(new ContextSegment(
            kind,
            content,
            _estimator.Estimate(content),
            sourceIdentity,
            rank,
            mandatory));

    private static string? GetNotePath(string content)
    {
        const string prefix = "NOTE: ";
        if (!content.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var end = content.IndexOf('\n');
        return end < 0 ? content[prefix.Length..] : content[prefix.Length..end];
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
