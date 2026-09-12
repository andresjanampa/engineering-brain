using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class EvaluationHarnessTests
{
    [Fact]
    public async Task RequiredEntityHit_PassesRetrievalContract()
    {
        var result = await EvaluateAsync(Expected(required: ["Demo.Business.BusinessService"]), "BusinessService");
        Assert.Equal(1, result.Retrieval.RecallAt5);
        Assert.Empty(result.MissingRequiredEntities);
    }

    [Fact]
    public async Task RequiredEntityMiss_IsReported()
    {
        var result = await EvaluateAsync(Expected(required: ["Missing.Component"]), "BusinessService");
        Assert.Equal(0, result.Retrieval.RecallAt10);
        Assert.Contains("Missing.Component", result.MissingRequiredEntities);
    }

    [Fact]
    public async Task AcceptableEntityWithoutRequiredEntity_DoesNotFail()
    {
        var result = await EvaluateAsync(Expected(acceptable: ["Demo.Business.BusinessService"]), "BusinessService");
        Assert.True(result.Passed);
    }

    [Fact]
    public async Task NegativeCandidateAtRankOne_DominatesAndFails()
    {
        var result = await EvaluateAsync(Expected(negative: ["Demo.Business.BusinessService"]), "BusinessService");
        Assert.True(result.Retrieval.NegativeCandidateDominates);
        Assert.False(result.Passed);
    }

    [Fact]
    public async Task TestCandidateNoise_IsCounted()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var project = snapshot.Projects.Single(item => item.Id == "project:business");
        snapshot = snapshot with
        {
            Projects = snapshot.Projects.Select(item => item.Id == project.Id
                ? item with { Name = "Business.Tests", RelativePath = "tests/Business.Tests/Business.Tests.csproj" }
                : item).ToArray(),
            Entities = snapshot.Entities.Select(item => item.ProjectId == project.Id
                ? item with { RelativeFilePath = $"tests/Business.Tests/{Path.GetFileName(item.RelativeFilePath)}" }
                : item).ToArray()
        };
        var result = await EvaluateAsync(Expected(required: ["Demo.Business.BusinessService"]), "BusinessService", snapshot);
        Assert.True(result.Retrieval.TestCandidatesAt5 > 0);
        Assert.True(result.Retrieval.TestCandidateRatioAt10 > 0);
    }

    [Fact]
    public async Task EvaluationRankingAndMatchReasons_AreDeterministic()
    {
        var first = await EvaluateAsync(Expected(required: ["Demo.Business.BusinessService"]), "BusinessService execute");
        var second = await EvaluateAsync(Expected(required: ["Demo.Business.BusinessService"]), "BusinessService execute");
        Assert.Equal(first.TopComponents.Select(item => (item.Identity, item.Score)), second.TopComponents.Select(item => (item.Identity, item.Score)));
        Assert.Equal(first.TopComponents.SelectMany(item => item.MatchReasons), second.TopComponents.SelectMany(item => item.MatchReasons));
    }

    [Fact]
    public async Task AggregateAndCategoryMetrics_AreDerivedFromCases()
    {
        var result = await EvaluateAsync(Expected(required: ["Demo.Business.BusinessService"]), "BusinessService");
        var aggregate = EvaluationHarness.Aggregate([result]);
        var categories = EvaluationHarness.Categories([result]);
        Assert.Equal(1, aggregate.Cases);
        Assert.Equal(result.Retrieval.RecallAt10, aggregate.RecallAt10);
        Assert.Contains(categories, item => item.Category == "unit");
    }

    private static async Task<EvaluationCaseResult> EvaluateAsync(
        EvaluationExpectations expected,
        string term,
        RepositorySnapshot? snapshot = null)
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync(snapshot);
        var suiteRoot = Path.Combine(Path.GetTempPath(), $"brain-harness-{Guid.NewGuid():N}");
        Directory.CreateDirectory(suiteRoot);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(suiteRoot, "initiative.md"), term);
            var item = new EvaluationCase("case", "case", "initiative.md", ["unit"], EvaluationSplit.Tuning,
                InitiativeAnalysisTestData.Understanding(term), expected);
            var suite = new EvaluationSuite(1, "suite", "suite", [item]);
            return Assert.Single(await new EvaluationHarness().EvaluateAsync(suite, Path.Combine(suiteRoot, "suite.json"), memory));
        }
        finally { Directory.Delete(suiteRoot, true); }
    }

    private static EvaluationExpectations Expected(
        IReadOnlyList<string>? required = null,
        IReadOnlyList<string>? acceptable = null,
        IReadOnlyList<string>? negative = null) => new(
            required ?? [], acceptable ?? [], negative ?? [], [], [], [], [], [], InitiativeAnalysisStatus.Complete);
}
