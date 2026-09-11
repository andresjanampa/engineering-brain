using EngineeringBrain.Core;

namespace EngineeringBrain.Core.Tests;

internal static class SnapshotTestFactory
{
    public static RepositorySnapshot Create(
        string repositoryId = "repo",
        string? branch = "main",
        IReadOnlyList<ScannedFile>? files = null,
        IReadOnlyList<ProjectInfo>? projects = null,
        IReadOnlyList<CodeEntity>? entities = null,
        IReadOnlyList<CodeRelation>? relations = null) => new(
        3,
        DateTimeOffset.UtcNow,
        new RepositoryInfo(repositoryId, "demo", "/demo"),
        new GitInfo(branch is not null, branch, "abc", null, true),
        files ?? [],
        [],
        projects ?? [],
        entities ?? [],
        relations ?? [],
        new AnalysisSummary(AnalysisMode.FullSemantic, "test-v3", projects?.Count ?? 0, projects?.Count ?? 0, 0),
        [],
        new IncrementalAnalysisSummary(
            ScanExecutionMode.Full,
            false,
            "test",
            [],
            [],
            [],
            projects?.Select(project => project.RelativePath).ToArray() ?? [],
            [],
            new ScanPerformanceMetrics(files?.Count ?? 0, files?.Count ?? 0, projects?.Count ?? 0, projects?.Count ?? 0, 0, 0, entities?.Count ?? 0, 1),
            new GraphIntegritySummary(true, 0, 0, 0)));

    public static ProjectInfo Project(string name) => new(
        $"project:{name}",
        name,
        $"{name}/{name}.csproj",
        "C#",
        ["net10.0"],
        [],
        [$"{name}/{name}.cs"],
        ProjectAnalysisMode.Semantic,
        []);

    public static CodeEntity ProjectEntity(ProjectInfo project) => new(
        project.Id,
        project.Name,
        project.Name,
        CodeEntityType.Project,
        "C#",
        project.RelativePath,
        1,
        3,
        project.Id,
        ResolutionLevel.Exact,
        []);

    public static CodeEntity ClassEntity(ProjectInfo project, string name) => new(
        $"entity:{project.Name}:{name}",
        name,
        $"{project.Name}.{name}",
        CodeEntityType.Class,
        "C#",
        $"{project.Name}/{name}.cs",
        1,
        1,
        project.Id,
        ResolutionLevel.Semantic,
        []);
}
