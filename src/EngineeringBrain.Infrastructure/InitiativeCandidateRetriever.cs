using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed record CandidateRetrievalOptions(
    int MaximumProjects = 4,
    int MaximumDirectComponents = 12,
    int MaximumExpandedComponents = 5,
    int GraphDepth = 1);

public sealed record RetrievalScoringOptions(
    int ExactFullName = 48,
    int ExactComponentName = 32,
    int ComponentNameToken = 8,
    int RelatedTypeToken = 5,
    int ProjectIdentityToken = 4,
    int NamespaceToken = 3,
    int PathToken = 2,
    int MemberToken = 2,
    int MaximumMemberContribution = 6,
    int MaximumRarityContribution = 8,
    int TestCandidatePenalty = 10,
    int ExactProjectAlias = 10,
    int ProjectAliasToken = 3,
    int MaximumProjectComponentContribution = 12,
    int GraphExpansion = 1);

public sealed class TermRarityIndex
{
    private readonly IReadOnlyDictionary<string, int> _documentFrequency;
    private readonly int _documentCount;
    private readonly int _maximumPerTerm;

    private TermRarityIndex(IReadOnlyDictionary<string, int> documentFrequency, int documentCount, int maximumPerTerm)
    {
        _documentFrequency = documentFrequency;
        _documentCount = documentCount;
        _maximumPerTerm = maximumPerTerm;
    }

    public static TermRarityIndex Create(IEnumerable<IEnumerable<string>> documents, int maximumPerTerm = 4)
    {
        if (maximumPerTerm < 0) throw new ArgumentOutOfRangeException(nameof(maximumPerTerm));
        var ordered = documents.Select(document => document.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()).ToArray();
        var frequency = ordered.SelectMany(document => document)
            .GroupBy(term => term, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return new TermRarityIndex(frequency, ordered.Length, maximumPerTerm);
    }

    public int Contribution(string term)
    {
        if (_documentCount == 0 || _maximumPerTerm == 0 || !_documentFrequency.TryGetValue(term, out var frequency)) return 0;
        var discrimination = Math.Log((_documentCount + 1d) / (frequency + 1d)) / Math.Log(_documentCount + 1d);
        return Math.Clamp((int)Math.Round(discrimination * _maximumPerTerm, MidpointRounding.AwayFromZero), 0, _maximumPerTerm);
    }
}

public static class TestCandidateClassifier
{
    private static readonly HashSet<string> TestIntentTerms = new(StringComparer.Ordinal)
    {
        "coverage", "integration", "prueba", "pruebas", "qa", "test", "testing", "tests", "unit"
    };

    public static bool IsTestProject(ProjectInfo project) =>
        project.Name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
        || HasTestsPathSegment(project.RelativePath);

    public static bool IsTestIntent(IEnumerable<string> normalizedTerms) => normalizedTerms.Any(TestIntentTerms.Contains);

    private static bool HasTestsPathSegment(string path)
    {
        var normalized = $"/{path.Replace('\\', '/').Trim('/')}";
        return normalized.Contains("/tests/", StringComparison.OrdinalIgnoreCase);
    }
}

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
    private readonly RetrievalScoringOptions _scoring;

    public InitiativeCandidateRetriever(
        InitiativeTermNormalizer? normalizer = null,
        CandidateRetrievalOptions? options = null,
        RetrievalScoringOptions? scoring = null)
    {
        _normalizer = normalizer ?? new InitiativeTermNormalizer();
        _options = options ?? new CandidateRetrievalOptions();
        _scoring = scoring ?? new RetrievalScoringOptions();
        if (_options.GraphDepth is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "Retrieval supports graph depth 0 or 1.");
        if (_scoring.MaximumMemberContribution < 0 || _scoring.MaximumRarityContribution < 0
            || _scoring.MaximumProjectComponentContribution < 0 || _scoring.TestCandidatePenalty < 0)
            throw new ArgumentOutOfRangeException(nameof(scoring), "Scoring bounds and penalties cannot be negative.");
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

        var queryValues = GetQueryValues(understanding);
        var terms = _normalizer.Tokenize(queryValues);
        var testIntent = TestCandidateClassifier.IsTestIntent(terms);
        var entitiesById = snapshot.Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var projectsById = snapshot.Projects.ToDictionary(project => project.Id, StringComparer.Ordinal);
        var childrenByParent = snapshot.Relations
            .Where(relation => relation.RelationType == CodeRelationType.Contains)
            .GroupBy(relation => relation.SourceEntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.TargetEntityId).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var parentByChild = snapshot.Relations
            .Where(relation => relation.RelationType == CodeRelationType.Contains)
            .GroupBy(relation => relation.TargetEntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.SourceEntityId, StringComparer.Ordinal).First().SourceEntityId, StringComparer.Ordinal);
        var components = snapshot.Entities
            .Where(entity => ComponentTypes.Contains(entity.EntityType))
            .Where(entity => !IsNested(entity, parentByChild, entitiesById))
            .OrderBy(entity => entity.Id, StringComparer.Ordinal)
            .ToArray();
        var metadata = components.ToDictionary(
            component => component.Id,
            component => BuildMetadata(component, snapshot, entitiesById, projectsById, childrenByParent, parentByChild),
            StringComparer.Ordinal);
        var rarity = TermRarityIndex.Create(metadata.Values.Select(value => value.AllTokens));

        var direct = components
            .Select(component => ScoreComponent(component, metadata[component.Id], terms, queryValues, rarity, projectsById, testIntent))
            .Where(candidate => candidate.MatchReasons.Any(reason => reason.Points > 0))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.FullName, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.EntityId, StringComparer.Ordinal)
            .Take(_options.MaximumDirectComponents)
            .ToList();

        if (_options.GraphDepth == 1 && _options.MaximumExpandedComponents > 0)
            AddGraphExpansion(direct, snapshot, entitiesById, components);

        var componentProjectScores = direct
            .GroupBy(candidate => candidate.ProjectId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(item => item.Score), StringComparer.Ordinal);
        var projects = snapshot.Projects
            .Select(project => ScoreProject(project, terms, queryValues, snapshot, componentProjectScores))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.RelativePath, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.ProjectId, StringComparer.Ordinal)
            .Take(_options.MaximumProjects)
            .ToArray();
        var selectedIds = direct.Select(candidate => candidate.EntityId).ToHashSet(StringComparer.Ordinal);
        var relations = snapshot.Relations
            .Where(relation => selectedIds.Contains(relation.SourceEntityId) && selectedIds.Contains(relation.TargetEntityId))
            .Where(relation => relation.RelationType is CodeRelationType.Implements or CodeRelationType.Inherits or CodeRelationType.References)
            .OrderBy(relation => relation.SourceEntityId, StringComparer.Ordinal)
            .ThenBy(relation => relation.RelationType)
            .ThenBy(relation => relation.TargetEntityId, StringComparer.Ordinal)
            .Select(relation => new CandidateGraphRelation(relation.SourceEntityId, relation.TargetEntityId,
                relation.RelationType, relation.RelativeFilePath, relation.StartLine, relation.EndLine, relation.ResolutionLevel))
            .ToArray();
        return new CandidateRetrievalResult(snapshot.Projects.Count, components.Length, projects, direct, relations);
    }

    private ComponentCandidate ScoreComponent(
        CodeEntity component,
        ComponentMetadata metadata,
        IReadOnlyList<string> terms,
        IReadOnlyList<string> queryValues,
        TermRarityIndex rarity,
        IReadOnlyDictionary<string, ProjectInfo> projectsById,
        bool testIntent)
    {
        var reasons = new List<MatchReason>();
        var exactIdentity = false;
        if (IdentityMentioned(queryValues, component.FullName))
        {
            reasons.Add(new MatchReason("exact full name", component.FullName, _scoring.ExactFullName));
            exactIdentity = true;
        }
        else if (IdentityMentioned(queryValues, component.Name))
        {
            reasons.Add(new MatchReason("exact component name", component.Name, _scoring.ExactComponentName));
            exactIdentity = true;
        }

        if (!exactIdentity)
            AddTokenMatches(reasons, terms, metadata.NameTokens, "component name", _scoring.ComponentNameToken);
        AddTokenMatches(reasons, terms, metadata.RelatedTypeTokens, "base/interface", _scoring.RelatedTypeToken);
        AddTokenMatches(reasons, terms, metadata.ProjectTokens, "project identity", _scoring.ProjectIdentityToken);
        AddTokenMatches(reasons, terms, metadata.NamespaceTokens, "namespace", _scoring.NamespaceToken);
        AddTokenMatches(reasons, terms, metadata.PathTokens, "path", _scoring.PathToken);
        AddBoundedTokenMatches(reasons, terms, metadata.MemberTokens, "member name", _scoring.MemberToken, _scoring.MaximumMemberContribution);
        if (!exactIdentity) AddRarity(reasons, terms, metadata.NameTokens, rarity);

        if (!testIntent && component.ProjectId is not null
            && projectsById.TryGetValue(component.ProjectId, out var project)
            && TestCandidateClassifier.IsTestProject(project))
            reasons.Add(new MatchReason("non-test initiative penalty", project.Name, -_scoring.TestCandidatePenalty));

        return new ComponentCandidate(component.Id, component.Name, component.FullName, component.ProjectId ?? string.Empty,
            component.RelativeFilePath, component.EntityType, component.ResolutionLevel, reasons.Sum(reason => reason.Points), false,
            OrderReasons(reasons));
    }

    private ComponentMetadata BuildMetadata(
        CodeEntity component,
        RepositorySnapshot snapshot,
        IReadOnlyDictionary<string, CodeEntity> entitiesById,
        IReadOnlyDictionary<string, ProjectInfo> projectsById,
        IReadOnlyDictionary<string, string[]> childrenByParent,
        IReadOnlyDictionary<string, string> parentByChild)
    {
        var name = _normalizer.Tokenize(component.Name);
        var fullName = _normalizer.Tokenize(component.FullName);
        var ns = _normalizer.Tokenize(FindNamespace(component, parentByChild, entitiesById) ?? string.Empty);
        var path = _normalizer.Tokenize(component.RelativeFilePath);
        IReadOnlyList<string> project = component.ProjectId is not null && projectsById.TryGetValue(component.ProjectId, out var projectInfo)
            ? _normalizer.Tokenize([projectInfo.Name, Path.GetFileNameWithoutExtension(projectInfo.RelativePath)])
            : [];
        var members = childrenByParent.GetValueOrDefault(component.Id, [])
            .Where(entitiesById.ContainsKey)
            .SelectMany(id => _normalizer.Tokenize(entitiesById[id].Name))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var related = snapshot.Relations
            .Where(relation => relation.RelationType is CodeRelationType.Implements or CodeRelationType.Inherits)
            .Where(relation => relation.SourceEntityId == component.Id || relation.TargetEntityId == component.Id)
            .Select(relation => relation.SourceEntityId == component.Id ? relation.TargetEntityId : relation.SourceEntityId)
            .Where(entitiesById.ContainsKey)
            .SelectMany(id => _normalizer.Tokenize(entitiesById[id].FullName))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var all = name.Concat(fullName).Concat(ns).Concat(path).Concat(project).Concat(members).Concat(related)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new ComponentMetadata(name, ns, path, project, members, related, all);
    }

    private ProjectCandidate ScoreProject(ProjectInfo project, IReadOnlyList<string> terms, IReadOnlyList<string> queryValues,
        RepositorySnapshot snapshot, IReadOnlyDictionary<string, int> componentScores)
    {
        var reasons = new List<MatchReason>();
        var aliases = new[] { project.Name, Path.GetFileNameWithoutExtension(project.RelativePath), project.RelativePath }
            .Concat(snapshot.Entities.Where(entity => entity.ProjectId == project.Id
                && entity.EntityType is CodeEntityType.Project or CodeEntityType.Namespace)
                .SelectMany(entity => new[] { entity.Name, entity.FullName }))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var exact = aliases.FirstOrDefault(alias => IdentityMentioned(queryValues, alias));
        if (exact is not null) reasons.Add(new MatchReason("exact project alias", exact, _scoring.ExactProjectAlias));
        AddTokenMatches(reasons, terms, aliases.SelectMany(_normalizer.Tokenize), "project alias", _scoring.ProjectAliasToken);
        if (componentScores.TryGetValue(project.Id, out var componentScore))
        {
            var contribution = Math.Min(_scoring.MaximumProjectComponentContribution, Math.Max(1, componentScore / 2));
            reasons.Add(new MatchReason("contains selected component", project.Name, contribution));
        }
        return new ProjectCandidate(project.Id, project.Name, project.RelativePath, reasons.Sum(reason => reason.Points), OrderReasons(reasons));
    }

    private void AddGraphExpansion(List<ComponentCandidate> candidates, RepositorySnapshot snapshot,
        IReadOnlyDictionary<string, CodeEntity> entitiesById, IReadOnlyList<CodeEntity> components)
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
            .OrderBy(item => item.Id, StringComparer.Ordinal).Take(_options.MaximumExpandedComponents)
            .Select(item =>
            {
                var entity = entitiesById[item.Id];
                var neighbor = entitiesById.GetValueOrDefault(item.Neighbor)?.FullName ?? item.Neighbor;
                return new ComponentCandidate(entity.Id, entity.Name, entity.FullName, entity.ProjectId ?? string.Empty,
                    entity.RelativeFilePath, entity.EntityType, entity.ResolutionLevel, _scoring.GraphExpansion, true,
                    [new MatchReason($"graph {item.Relation.RelationType}", neighbor, _scoring.GraphExpansion)]);
            });
        candidates.AddRange(expanded);
    }

    private void AddRarity(ICollection<MatchReason> reasons, IReadOnlyList<string> terms,
        IReadOnlyList<string> candidateTokens, TermRarityIndex rarity)
    {
        var remaining = _scoring.MaximumRarityContribution;
        foreach (var term in terms.Where(candidateTokens.Contains).Order(StringComparer.Ordinal))
        {
            var points = Math.Min(remaining, rarity.Contribution(term));
            if (points <= 0) continue;
            reasons.Add(new MatchReason("component name rarity", term, points));
            remaining -= points;
            if (remaining == 0) break;
        }
    }

    private static void AddTokenMatches(ICollection<MatchReason> reasons, IReadOnlyList<string> terms,
        IEnumerable<string> candidateTokens, string signal, int points)
    {
        var tokens = candidateTokens.ToHashSet(StringComparer.Ordinal);
        foreach (var term in terms.Where(tokens.Contains)) reasons.Add(new MatchReason(signal, term, points));
    }

    private static void AddBoundedTokenMatches(ICollection<MatchReason> reasons, IReadOnlyList<string> terms,
        IEnumerable<string> candidateTokens, string signal, int points, int maximumContribution)
    {
        var tokens = candidateTokens.ToHashSet(StringComparer.Ordinal);
        var remaining = maximumContribution;
        foreach (var term in terms.Where(tokens.Contains).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var contribution = Math.Min(points, remaining);
            if (contribution <= 0) break;
            reasons.Add(new MatchReason(signal, term, contribution));
            remaining -= contribution;
        }
    }

    private static IReadOnlyList<MatchReason> OrderReasons(IEnumerable<MatchReason> reasons) => reasons
        .OrderByDescending(reason => reason.Points)
        .ThenBy(reason => reason.Signal, StringComparer.Ordinal)
        .ThenBy(reason => reason.MatchedValue, StringComparer.Ordinal)
        .ToArray();

    private static IReadOnlyList<string> GetQueryValues(InitiativeUnderstanding understanding) =>
        understanding.SearchTerms.Concat(understanding.TechnicalCapabilities)
            .Concat(understanding.FunctionalRequirements).Append(understanding.Summary)
            .Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();

    private bool IdentityMentioned(IEnumerable<string> values, string identity)
    {
        var identityTokens = _normalizer.Tokenize(identity);
        if (identityTokens.Count == 0) return false;
        var normalizedIdentity = string.Join(' ', identityTokens);
        foreach (var value in values)
        {
            if (_normalizer.NormalizePhrase(value).Equals(normalizedIdentity, StringComparison.Ordinal)) return true;
            if (identityTokens.Count < 2) continue;
            var index = value.IndexOf(identity, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var before = index == 0 || !char.IsLetterOrDigit(value[index - 1]);
            var end = index + identity.Length;
            var after = end == value.Length || !char.IsLetterOrDigit(value[end]);
            if (before && after) return true;
        }
        return false;
    }

    private static bool IsNested(CodeEntity entity, IReadOnlyDictionary<string, string> parentByChild,
        IReadOnlyDictionary<string, CodeEntity> entitiesById) =>
        parentByChild.TryGetValue(entity.Id, out var parentId) && entitiesById.TryGetValue(parentId, out var parent)
        && ComponentTypes.Contains(parent.EntityType);

    private static string? FindNamespace(CodeEntity entity, IReadOnlyDictionary<string, string> parentByChild,
        IReadOnlyDictionary<string, CodeEntity> entitiesById)
    {
        var current = entity.Id;
        while (parentByChild.TryGetValue(current, out var parentId) && entitiesById.TryGetValue(parentId, out var parent))
        {
            if (parent.EntityType == CodeEntityType.Namespace) return parent.FullName;
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
            throw new InvalidDataException("Project Memory is stale or belongs to another repository or branch.");
    }

    private sealed record ComponentMetadata(
        IReadOnlyList<string> NameTokens,
        IReadOnlyList<string> NamespaceTokens,
        IReadOnlyList<string> PathTokens,
        IReadOnlyList<string> ProjectTokens,
        IReadOnlyList<string> MemberTokens,
        IReadOnlyList<string> RelatedTypeTokens,
        IReadOnlyList<string> AllTokens);
}
