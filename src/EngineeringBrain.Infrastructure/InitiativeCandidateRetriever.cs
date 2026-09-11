using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed record CandidateRetrievalOptions(
    int MaximumProjects = 4,
    int MaximumDirectComponents = 12,
    int MaximumExpandedComponents = 5,
    int GraphDepth = 1);

public sealed class InitiativeCandidateRetriever
{
    private static readonly HashSet<CodeEntityType> ComponentTypes =
    [
        CodeEntityType.Class,
        CodeEntityType.Interface,
        CodeEntityType.Record,
        CodeEntityType.Enum
    ];

    private readonly InitiativeTermNormalizer _normalizer;
    private readonly CandidateRetrievalOptions _options;

    public InitiativeCandidateRetriever(
        InitiativeTermNormalizer? normalizer = null,
        CandidateRetrievalOptions? options = null)
    {
        _normalizer = normalizer ?? new InitiativeTermNormalizer();
        _options = options ?? new CandidateRetrievalOptions();
        if (_options.GraphDepth is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Iteration 5 supports graph depth 0 or 1.");
        }
    }

    public CandidateRetrievalResult Retrieve(
        InitiativeUnderstanding understanding,
        ProjectMemoryManifest manifest,
        RepositorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(understanding);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(snapshot);
        EnsureCompatible(manifest, snapshot);

        var terms = _normalizer.Tokenize(
            understanding.SearchTerms,
            understanding.TechnicalCapabilities,
            understanding.FunctionalRequirements,
            [understanding.Summary]);
        var entitiesById = snapshot.Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var childrenByParent = snapshot.Relations
            .Where(relation => relation.RelationType == CodeRelationType.Contains)
            .GroupBy(relation => relation.SourceEntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.TargetEntityId).ToArray(), StringComparer.Ordinal);
        var parentByChild = snapshot.Relations
            .Where(relation => relation.RelationType == CodeRelationType.Contains)
            .GroupBy(relation => relation.TargetEntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().SourceEntityId, StringComparer.Ordinal);
        var components = snapshot.Entities
            .Where(entity => ComponentTypes.Contains(entity.EntityType))
            .Where(entity => !IsNested(entity, parentByChild, entitiesById))
            .OrderBy(entity => entity.Id, StringComparer.Ordinal)
            .ToArray();

        var direct = components
            .Select(component => ScoreComponent(component, terms, snapshot, entitiesById, childrenByParent, parentByChild))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.FullName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.EntityId, StringComparer.Ordinal)
            .Take(_options.MaximumDirectComponents)
            .ToList();

        if (_options.GraphDepth == 1 && _options.MaximumExpandedComponents > 0)
        {
            AddGraphExpansion(direct, snapshot, entitiesById, components);
        }

        var componentProjectScores = direct
            .GroupBy(candidate => candidate.ProjectId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(item => item.Score), StringComparer.Ordinal);
        var projects = snapshot.Projects
            .Select(project => ScoreProject(project, terms, snapshot, componentProjectScores))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.RelativePath, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.ProjectId, StringComparer.Ordinal)
            .Take(_options.MaximumProjects)
            .ToArray();
        var selectedIds = direct.Select(candidate => candidate.EntityId).ToHashSet(StringComparer.Ordinal);
        var relations = snapshot.Relations
            .Where(relation => selectedIds.Contains(relation.SourceEntityId)
                && selectedIds.Contains(relation.TargetEntityId))
            .Where(relation => relation.RelationType is CodeRelationType.Implements
                or CodeRelationType.Inherits
                or CodeRelationType.References)
            .OrderBy(relation => relation.SourceEntityId, StringComparer.Ordinal)
            .ThenBy(relation => relation.RelationType)
            .ThenBy(relation => relation.TargetEntityId, StringComparer.Ordinal)
            .Select(relation => new CandidateGraphRelation(
                relation.SourceEntityId,
                relation.TargetEntityId,
                relation.RelationType,
                relation.RelativeFilePath,
                relation.StartLine,
                relation.EndLine,
                relation.ResolutionLevel))
            .ToArray();
        return new CandidateRetrievalResult(
            snapshot.Projects.Count,
            components.Length,
            projects,
            direct,
            relations);
    }

    private ComponentCandidate ScoreComponent(
        CodeEntity component,
        IReadOnlyList<string> terms,
        RepositorySnapshot snapshot,
        IReadOnlyDictionary<string, CodeEntity> entitiesById,
        IReadOnlyDictionary<string, string[]> childrenByParent,
        IReadOnlyDictionary<string, string> parentByChild)
    {
        var reasons = new List<MatchReason>();
        var nameTokens = _normalizer.Tokenize(component.Name).ToHashSet(StringComparer.Ordinal);
        var fullNameTokens = _normalizer.Tokenize(component.FullName).ToHashSet(StringComparer.Ordinal);
        var namespaceTokens = _normalizer.Tokenize(FindNamespace(component, parentByChild, entitiesById) ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        var memberTokens = childrenByParent.GetValueOrDefault(component.Id, [])
            .Where(entitiesById.ContainsKey)
            .SelectMany(id => _normalizer.Tokenize(entitiesById[id].Name))
            .ToHashSet(StringComparer.Ordinal);
        var relatedTokens = snapshot.Relations
            .Where(relation => relation.SourceEntityId == component.Id
                && relation.RelationType is CodeRelationType.Implements or CodeRelationType.Inherits)
            .Where(relation => entitiesById.ContainsKey(relation.TargetEntityId))
            .SelectMany(relation => _normalizer.Tokenize(entitiesById[relation.TargetEntityId].FullName))
            .ToHashSet(StringComparer.Ordinal);
        var queryPhrase = string.Join(' ', terms);
        var componentPhrase = _normalizer.NormalizePhrase(component.Name);
        if (componentPhrase.Length > 0 && queryPhrase.Contains(componentPhrase, StringComparison.Ordinal))
        {
            reasons.Add(new MatchReason("exact component name", component.Name, 12));
        }

        AddTokenMatches(reasons, terms, nameTokens, "component name", 5);
        AddTokenMatches(reasons, terms, memberTokens, "member name", 4);
        AddTokenMatches(reasons, terms, namespaceTokens, "namespace", 3);
        AddTokenMatches(reasons, terms, relatedTokens, "base/interface", 3);
        AddTokenMatches(reasons, terms, fullNameTokens.Except(nameTokens), "full name", 2);
        return new ComponentCandidate(
            component.Id,
            component.Name,
            component.FullName,
            component.ProjectId ?? string.Empty,
            component.RelativeFilePath,
            component.EntityType,
            component.ResolutionLevel,
            reasons.Sum(reason => reason.Points),
            false,
            reasons.OrderByDescending(reason => reason.Points).ThenBy(reason => reason.MatchedValue, StringComparer.Ordinal).ToArray());
    }

    private ProjectCandidate ScoreProject(
        ProjectInfo project,
        IReadOnlyList<string> terms,
        RepositorySnapshot snapshot,
        IReadOnlyDictionary<string, int> componentScores)
    {
        var reasons = new List<MatchReason>();
        var aliases = new[]
            {
                project.Name,
                Path.GetFileNameWithoutExtension(project.RelativePath),
                project.RelativePath
            }
            .Concat(snapshot.Entities
                .Where(entity => entity.ProjectId == project.Id
                    && entity.EntityType is CodeEntityType.Project or CodeEntityType.Namespace)
                .SelectMany(entity => new[] { entity.Name, entity.FullName }))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var aliasTokens = aliases.SelectMany(_normalizer.Tokenize).ToHashSet(StringComparer.Ordinal);
        var queryPhrase = string.Join(' ', terms);
        foreach (var alias in aliases.Order(StringComparer.Ordinal))
        {
            var phrase = _normalizer.NormalizePhrase(alias);
            if (phrase.Length > 0 && queryPhrase.Contains(phrase, StringComparison.Ordinal))
            {
                reasons.Add(new MatchReason("exact project alias", alias, 10));
                break;
            }
        }

        AddTokenMatches(reasons, terms, aliasTokens, "project alias", 3);
        if (componentScores.TryGetValue(project.Id, out var componentScore))
        {
            reasons.Add(new MatchReason("contains selected component", project.Name, Math.Max(1, componentScore / 2)));
        }

        return new ProjectCandidate(
            project.Id,
            project.Name,
            project.RelativePath,
            reasons.Sum(reason => reason.Points),
            reasons.OrderByDescending(reason => reason.Points).ThenBy(reason => reason.MatchedValue, StringComparer.Ordinal).ToArray());
    }

    private void AddGraphExpansion(
        List<ComponentCandidate> candidates,
        RepositorySnapshot snapshot,
        IReadOnlyDictionary<string, CodeEntity> entitiesById,
        IReadOnlyList<CodeEntity> components)
    {
        var componentIds = components.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var directIds = candidates.Select(item => item.EntityId).ToHashSet(StringComparer.Ordinal);
        var expanded = snapshot.Relations
            .Where(relation => relation.RelationType is CodeRelationType.Implements or CodeRelationType.Inherits)
            .Where(relation => directIds.Contains(relation.SourceEntityId) || directIds.Contains(relation.TargetEntityId))
            .Select(relation => directIds.Contains(relation.SourceEntityId)
                ? (Id: relation.TargetEntityId, Relation: relation, Neighbor: relation.SourceEntityId)
                : (Id: relation.SourceEntityId, Relation: relation, Neighbor: relation.TargetEntityId))
            .Where(item => !directIds.Contains(item.Id) && componentIds.Contains(item.Id) && entitiesById.ContainsKey(item.Id))
            .OrderBy(item => item.Id, StringComparer.Ordinal)
            .Take(_options.MaximumExpandedComponents)
            .Select(item =>
            {
                var entity = entitiesById[item.Id];
                var neighbor = entitiesById.GetValueOrDefault(item.Neighbor)?.FullName ?? item.Neighbor;
                return new ComponentCandidate(
                    entity.Id,
                    entity.Name,
                    entity.FullName,
                    entity.ProjectId ?? string.Empty,
                    entity.RelativeFilePath,
                    entity.EntityType,
                    entity.ResolutionLevel,
                    1,
                    true,
                    [new MatchReason($"graph {item.Relation.RelationType}", neighbor, 1)]);
            });
        candidates.AddRange(expanded);
    }

    private static void AddTokenMatches(
        ICollection<MatchReason> reasons,
        IReadOnlyList<string> terms,
        IEnumerable<string> candidateTokens,
        string signal,
        int points)
    {
        var tokens = candidateTokens.ToHashSet(StringComparer.Ordinal);
        foreach (var term in terms.Where(tokens.Contains))
        {
            reasons.Add(new MatchReason(signal, term, points));
        }
    }

    private static bool IsNested(
        CodeEntity entity,
        IReadOnlyDictionary<string, string> parentByChild,
        IReadOnlyDictionary<string, CodeEntity> entitiesById) =>
        parentByChild.TryGetValue(entity.Id, out var parentId)
        && entitiesById.TryGetValue(parentId, out var parent)
        && ComponentTypes.Contains(parent.EntityType);

    private static string? FindNamespace(
        CodeEntity entity,
        IReadOnlyDictionary<string, string> parentByChild,
        IReadOnlyDictionary<string, CodeEntity> entitiesById)
    {
        var current = entity.Id;
        while (parentByChild.TryGetValue(current, out var parentId)
            && entitiesById.TryGetValue(parentId, out var parent))
        {
            if (parent.EntityType == CodeEntityType.Namespace)
            {
                return parent.FullName;
            }

            current = parent.Id;
        }

        return null;
    }

    private static void EnsureCompatible(ProjectMemoryManifest manifest, RepositorySnapshot snapshot)
    {
        var branch = snapshot.Git.Branch ?? "(no branch)";
        if (!manifest.RepositoryId.Equals(snapshot.Repository.Id, StringComparison.Ordinal)
            || !manifest.Branch.Equals(branch, StringComparison.Ordinal)
            || manifest.SourceSnapshotSchema != snapshot.SchemaVersion
            || !manifest.SourceAnalyzerVersion.Equals(snapshot.Analysis.AnalyzerVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Project Memory is stale or belongs to another repository or branch.");
        }
    }
}
