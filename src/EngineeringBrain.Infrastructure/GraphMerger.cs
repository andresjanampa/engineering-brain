using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed record GraphMergeResult(
    IReadOnlyList<ProjectInfo> Projects,
    IReadOnlyList<CodeEntity> Entities,
    IReadOnlyList<CodeRelation> Relations,
    AnalysisSummary Analysis,
    IReadOnlyList<AnalysisDiagnostic> Diagnostics,
    int ReusedEntities,
    int RegeneratedEntities);

public sealed class GraphMerger
{
    public GraphMergeResult Merge(
        RepositorySnapshot previous,
        LanguageAnalysisResult regenerated,
        IReadOnlyCollection<string> invalidatedProjectPaths,
        IReadOnlyCollection<string> currentProjectPaths,
        string analyzerVersion)
    {
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var invalidatedPaths = invalidatedProjectPaths.ToHashSet(pathComparer);
        var currentPaths = currentProjectPaths.ToHashSet(pathComparer);
        var invalidatedProjectIds = previous.Projects
            .Where(project => invalidatedPaths.Contains(project.RelativePath)
                || !currentPaths.Contains(project.RelativePath))
            .Select(project => project.Id)
            .ToHashSet(StringComparer.Ordinal);
        var invalidatedEntityIds = previous.Entities
            .Where(entity => entity.ProjectId is not null
                && invalidatedProjectIds.Contains(entity.ProjectId))
            .Select(entity => entity.Id)
            .ToHashSet(StringComparer.Ordinal);

        var projects = previous.Projects
            .Where(project => currentPaths.Contains(project.RelativePath)
                && !invalidatedPaths.Contains(project.RelativePath))
            .ToDictionary(project => project.RelativePath, pathComparer);
        foreach (var project in regenerated.Projects)
        {
            projects[project.RelativePath] = project;
        }

        var retainedEntities = previous.Entities
            .Where(entity => !invalidatedEntityIds.Contains(entity.Id))
            .ToArray();
        var entities = retainedEntities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        foreach (var entity in regenerated.Entities)
        {
            entities[entity.Id] = entity;
        }

        var solutionEntityIds = previous.Entities
            .Where(entity => entity.EntityType == CodeEntityType.Solution)
            .Select(entity => entity.Id)
            .ToHashSet(StringComparer.Ordinal);
        var currentProjectEntityIds = entities.Values
            .Where(entity => entity.EntityType == CodeEntityType.Project)
            .Select(entity => entity.Id)
            .ToHashSet(StringComparer.Ordinal);
        var relations = previous.Relations
            .Where(relation =>
                (!invalidatedEntityIds.Contains(relation.SourceEntityId)
                    && !invalidatedEntityIds.Contains(relation.TargetEntityId))
                || IsStableSolutionMembership(relation, solutionEntityIds, currentProjectEntityIds))
            .Concat(regenerated.Relations)
            .GroupBy(GraphIntegrityValidator.RelationKey.Create)
            .Select(group => group
                .OrderBy(relation => relation.ResolutionLevel)
                .ThenBy(relation => relation.RelativeFilePath, pathComparer)
                .ThenBy(relation => relation.StartLine)
                .First())
            .Where(relation => entities.ContainsKey(relation.SourceEntityId)
                && entities.ContainsKey(relation.TargetEntityId))
            .OrderBy(relation => relation.RelativeFilePath, pathComparer)
            .ThenBy(relation => relation.StartLine)
            .ThenBy(relation => relation.RelationType)
            .ToArray();

        var reusedPaths = projects.Keys.Except(regenerated.Projects.Select(project => project.RelativePath), pathComparer)
            .ToHashSet(pathComparer);
        var diagnostics = previous.Diagnostics
            .Where(diagnostic => diagnostic.ProjectPath is not null
                && reusedPaths.Contains(diagnostic.ProjectPath))
            .Concat(regenerated.Diagnostics)
            .GroupBy(diagnostic => new
            {
                diagnostic.Code,
                diagnostic.Severity,
                diagnostic.Message,
                diagnostic.ProjectPath
            })
            .Select(group => group.First())
            .ToArray();
        var orderedProjects = projects.Values.OrderBy(project => project.RelativePath, pathComparer).ToArray();
        var semanticCount = orderedProjects.Count(project => project.AnalysisMode == ProjectAnalysisMode.Semantic);
        var fallbackCount = orderedProjects.Length - semanticCount;
        var hasGlobalWarning = diagnostics.Any(diagnostic =>
            diagnostic.ProjectPath is null
            && diagnostic.Severity == AnalysisDiagnosticSeverity.Warning);
        var mode = semanticCount == orderedProjects.Length && orderedProjects.Length > 0 && !hasGlobalWarning
            ? AnalysisMode.FullSemantic
            : semanticCount > 0
                ? AnalysisMode.PartialSemantic
                : AnalysisMode.SyntaxFallback;

        return new GraphMergeResult(
            orderedProjects,
            entities.Values
                .OrderBy(entity => entity.RelativeFilePath, pathComparer)
                .ThenBy(entity => entity.StartLine)
                .ThenBy(entity => entity.EntityType)
                .ToArray(),
            relations,
            new AnalysisSummary(mode, analyzerVersion, orderedProjects.Length, semanticCount, fallbackCount),
            diagnostics,
            retainedEntities.Length,
            regenerated.Entities.Count);
    }

    private static bool IsStableSolutionMembership(
        CodeRelation relation,
        IReadOnlySet<string> solutionEntityIds,
        IReadOnlySet<string> projectEntityIds) =>
        relation.RelationType == CodeRelationType.Contains
        && solutionEntityIds.Contains(relation.SourceEntityId)
        && projectEntityIds.Contains(relation.TargetEntityId);
}
