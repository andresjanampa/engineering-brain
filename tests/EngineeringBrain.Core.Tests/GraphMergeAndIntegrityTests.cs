using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class GraphMergeAndIntegrityTests
{
    [Fact]
    public void Merge_RemovesStaleEntitiesAndRetainsIndependentProject()
    {
        var projectA = SnapshotTestFactory.Project("A");
        var projectB = SnapshotTestFactory.Project("B");
        var projectAEntity = SnapshotTestFactory.ProjectEntity(projectA);
        var projectBEntity = SnapshotTestFactory.ProjectEntity(projectB);
        var stale = SnapshotTestFactory.ClassEntity(projectA, "Deleted");
        var retained = SnapshotTestFactory.ClassEntity(projectB, "Retained");
        var previous = SnapshotTestFactory.Create(
            projects: [projectA, projectB],
            entities: [projectAEntity, projectBEntity, stale, retained],
            relations:
            [
                Contains(projectAEntity, stale),
                Contains(projectBEntity, retained)
            ]);
        var replacement = SnapshotTestFactory.ClassEntity(projectA, "Replacement");
        var regenerated = new LanguageAnalysisResult(
            [projectAEntity, replacement],
            [Contains(projectAEntity, replacement)],
            [projectA],
            new AnalysisSummary(AnalysisMode.FullSemantic, "test-v3", 1, 1, 0),
            []);

        var result = new GraphMerger().Merge(
            previous,
            regenerated,
            [projectA.RelativePath],
            [projectA.RelativePath, projectB.RelativePath],
            "test-v3");

        Assert.DoesNotContain(result.Entities, entity => entity.Id == stale.Id);
        Assert.Contains(result.Entities, entity => entity.Id == replacement.Id);
        Assert.Contains(result.Entities, entity => entity.Id == retained.Id);
        Assert.Equal(2, result.ReusedEntities);
        Assert.Equal(2, result.RegeneratedEntities);
        Assert.True(new GraphIntegrityValidator().Validate(result.Entities, result.Relations).IsValid);
    }

    [Fact]
    public void Merge_DeduplicatesRelations()
    {
        var project = SnapshotTestFactory.Project("A");
        var projectEntity = SnapshotTestFactory.ProjectEntity(project);
        var type = SnapshotTestFactory.ClassEntity(project, "Type");
        var relation = Contains(projectEntity, type);
        var previous = SnapshotTestFactory.Create(
            projects: [project],
            entities: [projectEntity, type],
            relations: [relation]);
        var regenerated = new LanguageAnalysisResult(
            [],
            [relation, relation],
            [],
            new AnalysisSummary(AnalysisMode.SyntaxFallback, "test-v3", 0, 0, 0),
            []);

        var result = new GraphMerger().Merge(
            previous,
            regenerated,
            [],
            [project.RelativePath],
            "test-v3");

        Assert.Single(result.Relations);
    }

    [Fact]
    public void Validate_DetectsDuplicateEntityIdsAndDanglingRelations()
    {
        var project = SnapshotTestFactory.Project("A");
        var entity = SnapshotTestFactory.ProjectEntity(project);
        var dangling = new CodeRelation(
            entity.Id,
            "missing",
            CodeRelationType.Contains,
            project.RelativePath,
            1,
            1,
            ResolutionLevel.Exact);

        var result = new GraphIntegrityValidator().Validate([entity, entity], [dangling, dangling]);

        Assert.False(result.IsValid);
        Assert.Equal(1, result.DuplicateEntityIds);
        Assert.Equal(1, result.DuplicateRelations);
        Assert.Equal(2, result.DanglingRelations);
    }

    [Fact]
    public void Merge_PreservesUnchangedSolutionMembershipForRegeneratedProject()
    {
        var project = SnapshotTestFactory.Project("A");
        var projectEntity = SnapshotTestFactory.ProjectEntity(project);
        var solution = new CodeEntity(
            "solution:id",
            "Demo",
            "Demo",
            CodeEntityType.Solution,
            "C#",
            "Demo.sln",
            1,
            1,
            null,
            ResolutionLevel.Exact,
            []);
        var membership = new CodeRelation(
            solution.Id,
            projectEntity.Id,
            CodeRelationType.Contains,
            "Demo.sln",
            1,
            1,
            ResolutionLevel.Exact);
        var previous = SnapshotTestFactory.Create(
            projects: [project],
            entities: [solution, projectEntity],
            relations: [membership]);
        var regenerated = new LanguageAnalysisResult(
            [projectEntity],
            [],
            [project],
            new AnalysisSummary(AnalysisMode.FullSemantic, "test-v3", 1, 1, 0),
            []);

        var result = new GraphMerger().Merge(
            previous,
            regenerated,
            [project.RelativePath],
            [project.RelativePath],
            "test-v3");

        Assert.Contains(membership, result.Relations);
    }

    private static CodeRelation Contains(CodeEntity source, CodeEntity target) => new(
        source.Id,
        target.Id,
        CodeRelationType.Contains,
        target.RelativeFilePath,
        target.StartLine,
        target.EndLine,
        target.ResolutionLevel);
}
