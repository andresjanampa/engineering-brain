using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class GraphIntegrityValidator
{
    public GraphIntegritySummary Validate(
        IReadOnlyList<CodeEntity> entities,
        IReadOnlyList<CodeRelation> relations)
    {
        var duplicateEntityIds = entities
            .GroupBy(entity => entity.Id, StringComparer.Ordinal)
            .Count(group => group.Count() > 1);
        var duplicateRelations = relations
            .GroupBy(RelationKey.Create)
            .Count(group => group.Count() > 1);
        var entityIds = entities.Select(entity => entity.Id).ToHashSet(StringComparer.Ordinal);
        var danglingRelations = relations.Count(relation =>
            !entityIds.Contains(relation.SourceEntityId)
            || !entityIds.Contains(relation.TargetEntityId));

        return new GraphIntegritySummary(
            duplicateEntityIds == 0 && duplicateRelations == 0 && danglingRelations == 0,
            duplicateEntityIds,
            duplicateRelations,
            danglingRelations);
    }

    public void ThrowIfInvalid(GraphIntegritySummary summary)
    {
        if (!summary.IsValid)
        {
            throw new InvalidDataException(
                $"Graph integrity validation failed: {summary.DuplicateEntityIds} duplicate entity ID(s), "
                + $"{summary.DuplicateRelations} duplicate relation(s), {summary.DanglingRelations} dangling relation(s).");
        }
    }

    internal sealed record RelationKey(
        string SourceEntityId,
        string TargetEntityId,
        CodeRelationType RelationType)
    {
        public static RelationKey Create(CodeRelation relation) => new(
            relation.SourceEntityId,
            relation.TargetEntityId,
            relation.RelationType);
    }
}
