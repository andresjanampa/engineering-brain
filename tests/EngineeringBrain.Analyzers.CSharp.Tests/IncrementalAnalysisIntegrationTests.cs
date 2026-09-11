using EngineeringBrain.Analyzers.CSharp;
using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Analyzers.CSharp.Tests;

public sealed class IncrementalAnalysisIntegrationTests
{
    [Fact]
    public async Task ProjectLevelIncrementalAnalysisReusesIndependentGraphAndRemovesStaleEntities()
    {
        using var repository = new TemporaryRepository();
        var dataRoot = Path.Combine(Path.GetTempPath(), $"engineering-brain-integration-data-{Guid.NewGuid():N}");
        try
        {
            repository.WriteSdkProject("Core/Core.csproj");
            repository.Write("Core/Services.cs", "namespace Core; public interface IService { } public class Service : IService { }");
            repository.WriteSdkProject("Business/Business.csproj", "..\\Core\\Core.csproj");
            repository.Write("Business/BusinessService.cs", "using Core; namespace Business; public class BusinessService : IService { }");
            repository.WriteSdkProject("Independent/Independent.csproj");
            repository.Write("Independent/IndependentType.cs", "namespace Independent; public class IndependentType { }");
            repository.WriteSolution(
                "Sample.sln",
                ("Core", "Core\\Core.csproj", "11111111-1111-1111-1111-111111111111"),
                ("Business", "Business\\Business.csproj", "22222222-2222-2222-2222-222222222222"),
                ("Independent", "Independent\\Independent.csproj", "33333333-3333-3333-3333-333333333333"));
            var git = new NoGitInfoProvider();
            var engine = new RepositoryAnalysisEngine(
                new RepositoryScanner(),
                git,
                [new CSharpAnalyzer()],
                new LocalRepositorySnapshotStore(dataRoot));

            var first = await engine.ScanAsync(repository.Root);
            var second = await engine.ScanAsync(repository.Root);

            Assert.Equal(ScanExecutionMode.Full, first.Snapshot.Incremental.Mode);
            Assert.Equal(ScanExecutionMode.Incremental, second.Snapshot.Incremental.Mode);
            Assert.Equal(0, second.Snapshot.Incremental.Metrics.ProjectsAnalyzed);
            Assert.Equal(3, second.Snapshot.Incremental.Metrics.ProjectsReused);

            var independentBefore = Assert.Single(second.Snapshot.Entities, entity => entity.Name == "IndependentType");
            repository.Write(
                "Core/Services.cs",
                "namespace Core; public interface IService { } public class Service : IService { public int Version => 2; }");
            var modified = await engine.ScanAsync(repository.Root);

            Assert.Equal(["Core/Core.csproj"], modified.Snapshot.Incremental.DirectlyAffectedProjects);
            Assert.Equal(["Business/Business.csproj"], modified.Snapshot.Incremental.TransitivelyAffectedProjects);
            Assert.Equal(2, modified.Snapshot.Incremental.Metrics.ProjectsAnalyzed);
            Assert.Equal(["Independent/Independent.csproj"], modified.Snapshot.Incremental.ReusedProjects);
            Assert.Contains(modified.Snapshot.Entities, entity => entity.Id == independentBefore.Id);
            Assert.Contains(modified.Snapshot.Relations, relation =>
                relation.RelationType == CodeRelationType.Implements
                && relation.ResolutionLevel == ResolutionLevel.Semantic);

            repository.WriteSdkProject("Business/Business.csproj");
            var referenceChanged = await engine.ScanAsync(repository.Root);

            Assert.Equal(["Business/Business.csproj"], referenceChanged.Snapshot.Incremental.DirectlyAffectedProjects);
            Assert.DoesNotContain(referenceChanged.Snapshot.Relations, relation =>
                relation.RelationType == CodeRelationType.ReferencesProject);

            repository.Delete("Core/Services.cs");
            var deleted = await engine.ScanAsync(repository.Root);

            Assert.DoesNotContain(deleted.Snapshot.Entities, entity => entity.Name is "IService" or "Service");
            Assert.True(deleted.Snapshot.Incremental.GraphIntegrity.IsValid);
            Assert.Equal(0, deleted.Snapshot.Incremental.GraphIntegrity.DanglingRelations);
            Assert.Equal(0, deleted.Snapshot.Incremental.GraphIntegrity.DuplicateEntityIds);
            Assert.Equal(0, deleted.Snapshot.Incremental.GraphIntegrity.DuplicateRelations);
        }
        finally
        {
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    private sealed class NoGitInfoProvider : IGitInfoProvider
    {
        public Task<GitInfo> GetInfoAsync(string repositoryRoot, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GitInfo(false, null, null, null, null));
    }
}
