using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class RepositoryAnalysisEngineTests
{
    [Fact]
    public async Task ScanAsync_FirstScanIsFullAndSecondUnchangedScanReusesEverything()
    {
        using var fixture = new EngineFixture();

        var first = await fixture.Engine.ScanAsync(fixture.RepositoryRoot);
        var second = await fixture.Engine.ScanAsync(fixture.RepositoryRoot);

        Assert.Equal(ScanExecutionMode.Full, first.Snapshot.Incremental.Mode);
        Assert.False(first.Snapshot.Incremental.PreviousSnapshotFound);
        Assert.Equal(ScanExecutionMode.Incremental, second.Snapshot.Incremental.Mode);
        Assert.Empty(second.Snapshot.Incremental.Changes);
        Assert.Equal(0, second.Snapshot.Incremental.Metrics.ProjectsAnalyzed);
        Assert.Equal(1, second.Snapshot.Incremental.Metrics.ProjectsReused);
        Assert.Equal(first.Snapshot.Entities.Count, second.Snapshot.Incremental.Metrics.EntitiesReused);
        Assert.Equal(1, fixture.Analyzer.InvocationCount);
    }

    [Fact]
    public async Task ScanAsync_DirtyWorkingTreeAtSameHeadDetectsModifiedFile()
    {
        using var fixture = new EngineFixture();
        await fixture.Engine.ScanAsync(fixture.RepositoryRoot);
        fixture.Write("A/Type.cs", "public class Type { public int Value => 2; }");
        fixture.Git.Info = fixture.Git.Info with { IsWorkingTreeClean = false };

        var result = await fixture.Engine.ScanAsync(fixture.RepositoryRoot);

        Assert.Equal(ScanExecutionMode.Incremental, result.Snapshot.Incremental.Mode);
        Assert.Contains(result.Snapshot.Incremental.Changes, change =>
            change.Kind == FileChangeKind.Modified && change.CurrentPath == "A/Type.cs");
        Assert.Equal(["A/A.csproj"], result.Snapshot.Incremental.ReanalyzedProjects);
    }

    [Fact]
    public async Task ScanAsync_SameBranchAndNewerCommitCanRemainIncremental()
    {
        using var fixture = new EngineFixture();
        await fixture.Engine.ScanAsync(fixture.RepositoryRoot);
        fixture.Write("A/Type.cs", "public class Type { public int Value => 3; }");
        fixture.Git.Info = fixture.Git.Info with { HeadCommit = "def" };

        var result = await fixture.Engine.ScanAsync(fixture.RepositoryRoot);

        Assert.Equal(ScanExecutionMode.Incremental, result.Snapshot.Incremental.Mode);
        Assert.Single(result.Snapshot.Incremental.ReanalyzedProjects);
    }

    [Fact]
    public async Task ScanAsync_DifferentBranchForcesFullAnalysis()
    {
        using var fixture = new EngineFixture();
        await fixture.Engine.ScanAsync(fixture.RepositoryRoot);
        fixture.Git.Info = fixture.Git.Info with { Branch = "feature/other" };

        var result = await fixture.Engine.ScanAsync(fixture.RepositoryRoot);

        Assert.Equal(ScanExecutionMode.Full, result.Snapshot.Incremental.Mode);
        Assert.Contains("branch changed", result.Snapshot.Incremental.FullScanReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, fixture.Analyzer.InvocationCount);
    }

    [Fact]
    public async Task ScanAsync_CorruptSnapshotRegeneratesWithoutCrashing()
    {
        using var fixture = new EngineFixture();
        var scan = await new RepositoryScanner().ScanAsync(fixture.RepositoryRoot);
        fixture.WriteLatest(scan.Repository.Id, "broken");

        var result = await fixture.Engine.ScanAsync(fixture.RepositoryRoot);

        Assert.Equal(ScanExecutionMode.Full, result.Snapshot.Incremental.Mode);
        Assert.True(result.Snapshot.Incremental.PreviousSnapshotFound);
        Assert.Contains("could not be read", result.Snapshot.Incremental.FullScanReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanAsync_IncompatibleSnapshotRegeneratesWithoutCrashing()
    {
        using var fixture = new EngineFixture();
        var scan = await new RepositoryScanner().ScanAsync(fixture.RepositoryRoot);
        fixture.WriteLatest(scan.Repository.Id, "{ \"schemaVersion\": 2 }");

        var result = await fixture.Engine.ScanAsync(fixture.RepositoryRoot);

        Assert.Equal(ScanExecutionMode.Full, result.Snapshot.Incremental.Mode);
        Assert.Contains("incompatible", result.Snapshot.Incremental.FullScanReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScanAsync_UntrackedAddedSourceIsDetectedFromFilesystem()
    {
        using var fixture = new EngineFixture();
        await fixture.Engine.ScanAsync(fixture.RepositoryRoot);
        fixture.Write("A/NewService.cs", "public class NewService { }");
        fixture.Git.Info = fixture.Git.Info with { IsWorkingTreeClean = false };

        var result = await fixture.Engine.ScanAsync(fixture.RepositoryRoot);

        Assert.Contains(result.Snapshot.Incremental.Changes, change =>
            change.Kind == FileChangeKind.Added
            && change.CurrentPath == "A/NewService.cs"
            && change.DetectionMethod == ChangeDetectionMethod.FileSystem);
        Assert.Contains(result.Snapshot.Entities, entity => entity.RelativeFilePath == "A/NewService.cs");
    }

    [Fact]
    public async Task ScanAsync_FailedIncrementalAnalysisPreservesPreviousLatest()
    {
        using var fixture = new EngineFixture();
        var first = await fixture.Engine.ScanAsync(fixture.RepositoryRoot);
        var previousJson = File.ReadAllText(first.SnapshotPath);
        fixture.Write("A/Type.cs", "public class Changed { }");
        fixture.Analyzer.ThrowOnAnalyze = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Engine.ScanAsync(fixture.RepositoryRoot));

        Assert.Equal(previousJson, File.ReadAllText(first.SnapshotPath));
        Assert.Equal(SnapshotLoadStatus.Loaded, (await fixture.Store.LoadLatestAsync(first.Snapshot.Repository.Id)).Status);
    }

    private sealed class EngineFixture : IDisposable
    {
        private readonly string _dataRoot;

        public EngineFixture()
        {
            RepositoryRoot = Path.Combine(Path.GetTempPath(), $"engineering-brain-engine-{Guid.NewGuid():N}");
            _dataRoot = Path.Combine(Path.GetTempPath(), $"engineering-brain-data-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RepositoryRoot);
            Write("A/A.csproj", "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            Write("A/Type.cs", "public class Type { }");
            Analyzer = new FakeAnalyzer();
            Git = new FakeGitInfoProvider();
            Store = new LocalRepositorySnapshotStore(_dataRoot);
            Engine = new RepositoryAnalysisEngine(
                new RepositoryScanner(),
                Git,
                [Analyzer],
                Store);
        }

        public string RepositoryRoot { get; }

        public FakeAnalyzer Analyzer { get; }

        public FakeGitInfoProvider Git { get; }

        public LocalRepositorySnapshotStore Store { get; }

        public RepositoryAnalysisEngine Engine { get; }

        public void Write(string relativePath, string contents)
        {
            var path = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        public void WriteLatest(string repositoryId, string contents)
        {
            var path = Path.Combine(_dataRoot, "repositories", repositoryId, "snapshots", "latest.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        public void Dispose()
        {
            Directory.Delete(RepositoryRoot, recursive: true);
            if (Directory.Exists(_dataRoot))
            {
                Directory.Delete(_dataRoot, recursive: true);
            }
        }
    }

    private sealed class FakeGitInfoProvider : IGitInfoProvider
    {
        public GitInfo Info { get; set; } = new(true, "main", "abc", null, true);

        public Task<GitInfo> GetInfoAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
            Task.FromResult(Info);
    }

    private sealed class FakeAnalyzer : ILanguageAnalyzer
    {
        public string Language => "C#";

        public string Version => "fake-v3";

        public int InvocationCount { get; private set; }

        public bool ThrowOnAnalyze { get; set; }

        public Task<LanguageAnalysisResult> AnalyzeAsync(
            LanguageAnalysisRequest request,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            if (ThrowOnAnalyze)
            {
                throw new InvalidOperationException("Injected analysis failure.");
            }

            var included = request.IncludedProjectPaths?.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var projects = request.Files
                .Where(file => file.Extension == ".csproj")
                .Where(file => included is null || included.Contains(file.RelativePath))
                .Select(file => new ProjectInfo(
                    $"project:{file.RelativePath}",
                    Path.GetFileNameWithoutExtension(file.RelativePath),
                    file.RelativePath,
                    "C#",
                    ["net10.0"],
                    [],
                    request.Files.Where(source => source.Extension == ".cs"
                            && source.RelativePath.StartsWith(
                                $"{Path.GetDirectoryName(file.RelativePath)?.Replace('\\', '/')}/",
                                StringComparison.OrdinalIgnoreCase))
                        .Select(source => source.RelativePath)
                        .ToArray(),
                    ProjectAnalysisMode.Semantic,
                    []))
                .ToArray();
            var entities = projects.SelectMany(project =>
            {
                var projectEntity = SnapshotTestFactory.ProjectEntity(project);
                return new[] { projectEntity }
                    .Concat(project.Documents.Select(path => new CodeEntity(
                        $"entity:{project.Id}:{path}",
                        Path.GetFileNameWithoutExtension(path),
                        Path.GetFileNameWithoutExtension(path),
                        CodeEntityType.Class,
                        "C#",
                        path,
                        1,
                        1,
                        project.Id,
                        ResolutionLevel.Semantic,
                        [])));
            }).ToArray();
            var relations = projects.SelectMany(project =>
            {
                var projectEntity = entities.Single(entity => entity.Id == project.Id);
                return entities.Where(entity => entity.ProjectId == project.Id && entity.Id != project.Id)
                    .Select(entity => new CodeRelation(
                        projectEntity.Id,
                        entity.Id,
                        CodeRelationType.Contains,
                        entity.RelativeFilePath,
                        1,
                        1,
                        ResolutionLevel.Semantic));
            }).ToArray();
            return Task.FromResult(new LanguageAnalysisResult(
                entities,
                relations,
                projects,
                new AnalysisSummary(AnalysisMode.FullSemantic, Version, projects.Length, projects.Length, 0),
                []));
        }
    }
}
