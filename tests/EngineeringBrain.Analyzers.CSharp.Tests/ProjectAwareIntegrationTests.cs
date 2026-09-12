using EngineeringBrain.Core;

namespace EngineeringBrain.Analyzers.CSharp.Tests;

public sealed class ProjectAwareIntegrationTests
{
    [Fact]
    public async Task Solution_ProjectReferenceAndCrossProjectImplementationAreSemantic()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSdkProject("ProjectA/ProjectA.csproj");
        repository.Write("ProjectA/Contracts.cs", """
            namespace Shared;
            public interface IService<T> { }
            public class Duplicate { }
            """);
        repository.WriteSdkProject("ProjectB/ProjectB.csproj", "..\\ProjectA\\ProjectA.csproj");
        repository.Write("ProjectB/Service.cs", """
            using Shared;
            namespace Application;
            public class Service : IService<string> { }
            public class Duplicate { }
            """);
        repository.WriteSolution(
            "Sample.sln",
            ("ProjectA", "ProjectA\\ProjectA.csproj", "11111111-1111-1111-1111-111111111111"),
            ("ProjectB", "ProjectB\\ProjectB.csproj", "22222222-2222-2222-2222-222222222222"));

        var result = await repository.AnalyzeAsync();

        Assert.Equal(AnalysisMode.FullSemantic, result.Analysis.Mode);
        Assert.Equal(2, result.Analysis.SemanticProjects);
        Assert.Equal(0, result.Analysis.FallbackProjects);
        var projectA = Assert.Single(result.Projects, project => project.Name == "ProjectA");
        var projectB = Assert.Single(result.Projects, project => project.Name == "ProjectB");
        Assert.Equal(["net10.0"], projectA.TargetFrameworks);
        Assert.Contains("ProjectA/ProjectA.csproj", projectB.ProjectReferences);

        var projectReference = Assert.Single(result.Relations, relation =>
            relation.SourceEntityId == projectB.Id
            && relation.TargetEntityId == projectA.Id
            && relation.RelationType == CodeRelationType.ReferencesProject);
        Assert.Equal(ResolutionLevel.Exact, projectReference.ResolutionLevel);

        var service = Assert.Single(result.Entities, entity => entity is
        { Name: "Service", EntityType: CodeEntityType.Class });
        var contract = Assert.Single(result.Entities, entity => entity is
        { Name: "IService", EntityType: CodeEntityType.Interface });
        var implementation = Assert.Single(result.Relations, relation =>
            relation.SourceEntityId == service.Id
            && relation.TargetEntityId == contract.Id
            && relation.RelationType == CodeRelationType.Implements);
        Assert.Equal(ResolutionLevel.Semantic, implementation.ResolutionLevel);

        var duplicates = result.Entities
            .Where(entity => entity is { Name: "Duplicate", EntityType: CodeEntityType.Class })
            .ToArray();
        Assert.Equal(2, duplicates.Length);
        Assert.NotEqual(duplicates[0].ProjectId, duplicates[1].ProjectId);
        Assert.NotEqual(duplicates[0].Id, duplicates[1].Id);
    }

    [Fact]
    public async Task BrokenProjectFallsBackWithoutPreventingValidProject()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSdkProject("Valid/Valid.csproj");
        repository.Write("Valid/ValidService.cs", "namespace Valid; public class ValidService { }");
        repository.Write("Broken/Broken.csproj", "<Project Sdk=\"Missing.Sdk\"><PropertyGroup>");
        repository.Write("Broken/Legacy.cs", "namespace Legacy; public class LegacyService : MissingBase { }");

        var result = await repository.AnalyzeAsync();

        Assert.Equal(AnalysisMode.PartialSemantic, result.Analysis.Mode);
        Assert.Equal(1, result.Analysis.SemanticProjects);
        Assert.Equal(1, result.Analysis.FallbackProjects);
        Assert.Equal(ProjectAnalysisMode.Semantic, result.Projects.Single(project => project.Name == "Valid").AnalysisMode);
        Assert.Equal(ProjectAnalysisMode.SyntaxFallback, result.Projects.Single(project => project.Name == "Broken").AnalysisMode);
        Assert.Contains(result.Entities, entity => entity is
        { Name: "ValidService", ResolutionLevel: ResolutionLevel.Semantic });
        Assert.Contains(result.Entities, entity => entity is
        { Name: "LegacyService", ResolutionLevel: ResolutionLevel.Syntactic });
        Assert.DoesNotContain(result.Relations, relation => relation.RelationType == CodeRelationType.Inherits);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.ProjectPath == "Broken/Broken.csproj"
            && diagnostic.Code == "CSHARP_PROJECT_LOAD_FAILED");
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "CSHARP_FALLBACK_BASE_TYPES_OMITTED");
    }

    [Fact]
    public async Task MultipleSolutionsDoNotDuplicateSharedProjectsOrDocuments()
    {
        using var repository = new TemporaryRepository();
        repository.WriteSdkProject("A/A.csproj");
        repository.Write("A/Alpha.cs", "namespace A; public class Alpha { }");
        repository.WriteSdkProject("B/B.csproj");
        repository.Write("B/Beta.cs", "namespace B; public class Beta { }");
        repository.WriteSolution(
            "All.sln",
            ("A", "A\\A.csproj", "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"),
            ("B", "B\\B.csproj", "BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB"));
        repository.WriteSolution(
            "Secondary.sln",
            ("B", "B\\B.csproj", "BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB"));

        var result = await repository.AnalyzeAsync();

        Assert.Equal(2, result.Projects.Count);
        Assert.Single(result.Entities, entity => entity is
        { Name: "Alpha", EntityType: CodeEntityType.Class });
        Assert.Single(result.Entities, entity => entity is
        { Name: "Beta", EntityType: CodeEntityType.Class });
        Assert.Single(result.Projects.Single(project => project.Name == "B").Documents);
    }
}
