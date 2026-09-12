using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class AnalysisEvidenceValidator
{
    private static readonly HashSet<CodeEntityType> Components =
    [
        CodeEntityType.Class,
        CodeEntityType.Interface,
        CodeEntityType.Record,
        CodeEntityType.Enum
    ];

    public IReadOnlyList<ValidatedRecommendation> Validate(
        InitiativeAnalysis analysis,
        RepositorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(snapshot);
        return analysis.Recommendations.Select(item => Validate(item, snapshot)).ToArray();
    }

    public ValidatedRecommendation Validate(
        AnalysisRecommendation recommendation,
        RepositorySnapshot snapshot)
    {
        var valid = new List<EvidenceReference>();
        var diagnostics = new List<string>();
        foreach (var evidence in recommendation.Evidence)
        {
            if (TryValidateEvidence(evidence, snapshot, out var diagnostic))
            {
                valid.Add(evidence);
            }
            else
            {
                diagnostics.Add(diagnostic);
            }
        }

        var entitiesById = snapshot.Entities.ToDictionary(entity => entity.Id, StringComparer.Ordinal);
        var validEntities = valid
            .Where(item => item.EntityId is not null && entitiesById.ContainsKey(item.EntityId))
            .Select(item => entitiesById[item.EntityId!])
            .ToArray();
        var hasExistingProject = valid.Any(item => item.ProjectId is not null
            && snapshot.Projects.Any(project => project.Id == item.ProjectId));
        if (recommendation.Decision == RecommendationDecision.Create
            && recommendation.EpistemicStatus != EpistemicStatus.Proposal)
        {
            diagnostics.Add("CREATE must be labeled as a proposal.");
        }

        var requiredEvidenceValid = recommendation.Decision switch
        {
            RecommendationDecision.Create => recommendation.EpistemicStatus == EpistemicStatus.Proposal,
            RecommendationDecision.Reuse => validEntities.Length > 0 && hasExistingProject,
            RecommendationDecision.Extend => validEntities.Any(entity =>
                valid.Any(item => item.EntityId == entity.Id
                    && item.RelativePath is not null
                    && PathMatches(entity, item.RelativePath))) && hasExistingProject,
            RecommendationDecision.AvoidModifying => validEntities.Any(entity => Components.Contains(entity.EntityType)),
            _ => false
        };

        if (!requiredEvidenceValid)
        {
            diagnostics.Add($"{recommendation.Decision} does not satisfy its required repository evidence contract.");
        }

        var status = recommendation.Decision == RecommendationDecision.Create
            && requiredEvidenceValid
            && diagnostics.Count == 0
            ? EvidenceValidationStatus.Proposal
            : requiredEvidenceValid && diagnostics.Count == 0
                ? EvidenceValidationStatus.Validated
                : valid.Count > 0
                    ? EvidenceValidationStatus.PartiallyValidated
                    : EvidenceValidationStatus.Invalid;
        return new ValidatedRecommendation(recommendation, status, valid, diagnostics);
    }

    private static bool TryValidateEvidence(
        EvidenceReference evidence,
        RepositorySnapshot snapshot,
        out string diagnostic)
    {
        var branch = snapshot.Git.Branch ?? "(no branch)";
        if (!string.Equals(evidence.RepositoryId, snapshot.Repository.Id, StringComparison.Ordinal)
            || !string.Equals(evidence.Branch, branch, StringComparison.Ordinal))
        {
            diagnostic = "Evidence repository or branch does not match the analyzed snapshot.";
            return false;
        }

        var entity = evidence.EntityId is null
            ? null
            : snapshot.Entities.FirstOrDefault(item => item.Id == evidence.EntityId);
        var project = evidence.ProjectId is null
            ? null
            : snapshot.Projects.FirstOrDefault(item => item.Id == evidence.ProjectId);
        var valid = evidence.Kind switch
        {
            EvidenceKind.Entity => entity is not null
                && ProjectMatches(entity, evidence.ProjectId)
                && OptionalPathMatches(entity, evidence.RelativePath)
                && OptionalLinesMatch(entity, evidence.StartLine, evidence.EndLine)
                && OptionalResolutionMatches(entity.ResolutionLevel, evidence.ResolutionLevel),
            EvidenceKind.Project => project is not null
                && (evidence.RelativePath is null
                    || project.RelativePath.Equals(evidence.RelativePath, StringComparison.OrdinalIgnoreCase)),
            EvidenceKind.Relation => RelationExists(evidence, snapshot),
            EvidenceKind.SourceLocation => entity is not null
                && evidence.RelativePath is not null
                && LocationExists(entity, evidence.RelativePath, evidence.StartLine, evidence.EndLine),
            _ => false
        };
        diagnostic = valid
            ? string.Empty
            : $"{evidence.Kind} evidence does not match the current snapshot.";
        return valid;
    }

    private static bool RelationExists(EvidenceReference evidence, RepositorySnapshot snapshot) =>
        evidence.SourceEntityId is not null
        && evidence.TargetEntityId is not null
        && evidence.RelationType is not null
        && snapshot.Relations.Any(relation =>
            relation.SourceEntityId == evidence.SourceEntityId
            && relation.TargetEntityId == evidence.TargetEntityId
            && relation.RelationType == evidence.RelationType
            && (evidence.RelativePath is null
                || relation.RelativeFilePath.Equals(evidence.RelativePath, StringComparison.OrdinalIgnoreCase))
            && (evidence.StartLine is null || relation.StartLine == evidence.StartLine)
            && (evidence.EndLine is null || relation.EndLine == evidence.EndLine)
            && OptionalResolutionMatches(relation.ResolutionLevel, evidence.ResolutionLevel));

    private static bool ProjectMatches(CodeEntity entity, string? projectId) =>
        projectId is null || entity.ProjectId == projectId;

    private static bool OptionalPathMatches(CodeEntity entity, string? path) =>
        path is null || PathMatches(entity, path);

    private static bool PathMatches(CodeEntity entity, string path) =>
        entity.RelativeFilePath.Equals(path, StringComparison.OrdinalIgnoreCase)
        || entity.AdditionalLocations.Any(location =>
            location.RelativeFilePath.Equals(path, StringComparison.OrdinalIgnoreCase));

    private static bool OptionalLinesMatch(CodeEntity entity, int? start, int? end) =>
        start is null && end is null
        || LocationExists(entity, entity.RelativeFilePath, start, end)
        || entity.AdditionalLocations.Any(location => LocationMatches(location, start, end));

    private static bool LocationExists(CodeEntity entity, string path, int? start, int? end) =>
        entity.RelativeFilePath.Equals(path, StringComparison.OrdinalIgnoreCase)
            && (start is null || entity.StartLine == start)
            && (end is null || entity.EndLine == end)
        || entity.AdditionalLocations.Any(location =>
            location.RelativeFilePath.Equals(path, StringComparison.OrdinalIgnoreCase)
            && LocationMatches(location, start, end));

    private static bool LocationMatches(SourceLocation location, int? start, int? end) =>
        (start is null || location.StartLine == start)
        && (end is null || location.EndLine == end);

    private static bool OptionalResolutionMatches(ResolutionLevel actual, ResolutionLevel? expected) =>
        expected is null || actual == expected;
}
