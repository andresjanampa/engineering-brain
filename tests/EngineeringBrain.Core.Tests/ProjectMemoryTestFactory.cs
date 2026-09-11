using EngineeringBrain.Core;

namespace EngineeringBrain.Core.Tests;

internal static class ProjectMemoryTestFactory
{
    public static RepositorySnapshot Create(string branch = "feature/project-memory")
    {
        var core = Project("project:core", "Core", "src/Core/Core.csproj", []);
        var business = Project("project:business", "Business", "src/Business/Business.csproj", [core.RelativePath]);
        var coreProject = Entity(core.Id, "Core", "Core", CodeEntityType.Project, core.RelativePath, core.Id, ResolutionLevel.Exact);
        var businessProject = Entity(business.Id, "Business", "Business", CodeEntityType.Project, business.RelativePath, business.Id, ResolutionLevel.Exact);
        var coreNamespace = Entity("entity:ns-core", "Demo.Core", "Demo.Core", CodeEntityType.Namespace, "src/Core/Services.cs", core.Id);
        var businessNamespace = Entity("entity:ns-business", "Demo.Business", "Demo.Business", CodeEntityType.Namespace, "src/Business/BusinessService.cs", business.Id);
        var contract = Entity("entity:contract", "IService", "Demo.Core.IService", CodeEntityType.Interface, "src/Core/Services.cs", core.Id);
        var service = Entity("entity:service", "Service", "Demo.Core.Service", CodeEntityType.Class, "src/Core/Services.cs", core.Id) with
        {
            AdditionalLocations = [new SourceLocation("src/Core/Service.Part2.cs", 1, 4)]
        };
        var model = Entity("entity:model", "Model", "Demo.Core.Model", CodeEntityType.Record, "src/Core/Model.cs", core.Id);
        var nested = Entity("entity:nested", "Nested", "Demo.Core.Service.Nested", CodeEntityType.Class, "src/Core/Services.cs", core.Id);
        var run = Entity("entity:run", "Run", "Demo.Core.Service.Run(int)", CodeEntityType.Method, "src/Core/Services.cs", core.Id);
        var name = Entity("entity:name", "Name", "Demo.Core.Service.Name", CodeEntityType.Property, "src/Core/Services.cs", core.Id);
        var businessService = Entity("entity:business-service", "BusinessService", "Demo.Business.BusinessService", CodeEntityType.Class, "src/Business/BusinessService.cs", business.Id);
        var execute = Entity("entity:execute", "Execute", "Demo.Business.BusinessService.Execute()", CodeEntityType.Method, "src/Business/BusinessService.cs", business.Id);
        var entities = new[]
        {
            coreProject, businessProject, coreNamespace, businessNamespace, contract, service, model,
            nested, run, name, businessService, execute
        };
        var relations = new[]
        {
            Contains(coreProject, coreNamespace),
            Contains(businessProject, businessNamespace),
            Contains(coreNamespace, contract),
            Contains(coreNamespace, service),
            Contains(coreNamespace, model),
            Contains(service, nested),
            Contains(service, run),
            Contains(service, name),
            Contains(businessNamespace, businessService),
            Contains(businessService, execute),
            Relation(businessProject, coreProject, CodeRelationType.ReferencesProject, ResolutionLevel.Exact),
            Relation(service, contract, CodeRelationType.Implements, ResolutionLevel.Semantic),
            Relation(businessService, contract, CodeRelationType.Implements, ResolutionLevel.Semantic)
        };
        return new RepositorySnapshot(
            3,
            new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero),
            new RepositoryInfo("repository:test", "demo", "C:\\private\\demo"),
            new GitInfo(true, branch, "abc123", null, true),
            [],
            [new LanguageStatistics("C#", 5, 100)],
            [core, business],
            entities,
            relations,
            new AnalysisSummary(AnalysisMode.FullSemantic, "test-analyzer-v3", 2, 2, 0),
            [],
            new IncrementalAnalysisSummary(
                ScanExecutionMode.Incremental,
                true,
                null,
                [],
                [],
                [],
                [],
                [core.RelativePath, business.RelativePath],
                new ScanPerformanceMetrics(5, 0, 2, 0, 2, entities.Length, 0, 1),
                new GraphIntegritySummary(true, 0, 0, 0)));
    }

    public static ProjectInfo Project(
        string id,
        string name,
        string path,
        IReadOnlyList<string> references) => new(
        id,
        name,
        path,
        "C#",
        ["net10.0"],
        references,
        [],
        ProjectAnalysisMode.Semantic,
        []);

    public static CodeEntity Entity(
        string id,
        string name,
        string fullName,
        CodeEntityType type,
        string path,
        string? projectId,
        ResolutionLevel resolution = ResolutionLevel.Semantic) => new(
        id,
        name,
        fullName,
        type,
        "C#",
        path,
        1,
        5,
        projectId,
        resolution,
        []);

    public static CodeRelation Contains(CodeEntity source, CodeEntity target) =>
        Relation(source, target, CodeRelationType.Contains, target.ResolutionLevel);

    public static CodeRelation Relation(
        CodeEntity source,
        CodeEntity target,
        CodeRelationType type,
        ResolutionLevel resolution) => new(
        source.Id,
        target.Id,
        type,
        target.RelativeFilePath,
        target.StartLine,
        target.EndLine,
        resolution);
}
