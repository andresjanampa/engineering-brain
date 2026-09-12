using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class EvaluationMetricsTests
{
    [Fact]
    public void RecallAt5_IsFractionOfRequiredHits() =>
        Assert.Equal(0.5, EvaluationMetricsCalculator.RecallAt(["a", "x"], ["a", "b"], 5));

    [Fact]
    public void RecallAt10_FindsLateRequiredHit() =>
        Assert.Equal(1, EvaluationMetricsCalculator.RecallAt(["x", "x2", "x3", "x4", "x5", "a"], ["a"], 10));

    [Fact]
    public void PrecisionAt5_CountsRequiredAndAcceptable() =>
        Assert.Equal(0.4, EvaluationMetricsCalculator.PrecisionAt(["a", "b", "x", "y"], ["a"], ["b"], 5));

    [Fact]
    public void PrecisionAt10_UsesRequestedKAsDenominator() =>
        Assert.Equal(0.1, EvaluationMetricsCalculator.PrecisionAt(["a", "x"], ["a"], [], 10));

    [Fact]
    public void ReciprocalRank_ReturnsInverseFirstRelevantRank() =>
        Assert.Equal(0.25, EvaluationMetricsCalculator.ReciprocalRank(["x", "y", "z", "a"], ["a"]));

    [Fact]
    public void ProjectMrr_UsesSameExplainableFormula() =>
        Assert.Equal(0.5, EvaluationMetricsCalculator.ReciprocalRank(["other", "project"], ["project"]));

    [Fact]
    public void RequiredMiss_HasZeroRecall() =>
        Assert.Equal(0, EvaluationMetricsCalculator.RecallAt(["x"], ["required"], 10));

    [Fact]
    public void NoRequiredCandidates_IsNotAFailure() =>
        Assert.Equal(1, EvaluationMetricsCalculator.RecallAt([], [], 5));

    [Fact]
    public void AcceptableCandidate_DoesNotReduceRecall() =>
        Assert.Equal(1, EvaluationMetricsCalculator.RecallAt(["acceptable"], [], 5));

    [Fact]
    public void Median_IsDeterministicForOddAndEvenCounts()
    {
        Assert.Equal(2, EvaluationMetricsCalculator.Median([3, 1, 2]));
        Assert.Equal(2.5, EvaluationMetricsCalculator.Median([4, 1, 3, 2]));
    }

    [Fact]
    public void BaselineComparison_EqualHasNoRegression()
    {
        var aggregate = Aggregate();
        Assert.Empty(new EvaluationBaselineComparer().Compare(Baseline(aggregate), aggregate));
    }

    [Fact]
    public void BaselineComparison_CriticalDecreaseIsRegression()
    {
        var baseline = Aggregate();
        var current = baseline with { RecallAt10 = 0.5 };
        Assert.Contains(new EvaluationBaselineComparer().Compare(Baseline(baseline), current), item => item.Metric == "Recall@10");
    }

    [Fact]
    public void BaselineComparison_ImprovementIsNotRegression()
    {
        var baseline = Aggregate() with { RecallAt10 = 0.5 };
        var current = baseline with { RecallAt10 = 1 };
        Assert.Empty(new EvaluationBaselineComparer().Compare(Baseline(baseline), current));
    }

    [Fact]
    public void BaselineComparison_ContextGrowthIsRegression()
    {
        var baseline = Aggregate();
        var current = baseline with { AverageCall2Tokens = 1600 };
        Assert.Contains(new EvaluationBaselineComparer().Compare(Baseline(baseline), current), item => item.Metric.Contains("Average"));
    }

    [Fact]
    public void BaselineComparison_PerCaseRegressionCannotHideInAggregate()
    {
        var aggregate = Aggregate();
        var baseline = Baseline(aggregate) with
        {
            Cases = [new EvaluationBaselineCase("case", 1, 1, 1, 1000)]
        };
        var result = CaseResult("case", recallAt10: 0.5);
        Assert.Contains(new EvaluationBaselineComparer().Compare(baseline, aggregate, currentCases: [result]),
            item => item.Metric == "case Recall@10");
    }

    [Fact]
    public async Task SuiteLoader_LoadsVersionOneCorpus()
    {
        var path = FindRepositoryFile("evaluations", "suite.json");
        var suite = await new EvaluationSuiteSerializer().LoadAsync(path);
        Assert.Equal(1, suite.EvaluationSchemaVersion);
        Assert.Equal(10, suite.Cases.Count);
    }

    [Fact]
    public async Task SuiteLoader_RejectsInvalidSchema()
    {
        var root = Path.Combine(Path.GetTempPath(), $"brain-eval-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "initiative.md"), "safe");
            await File.WriteAllTextAsync(Path.Combine(root, "suite.json"), """
                {"evaluationSchemaVersion":99,"id":"bad","description":"bad","cases":[{"id":"one","description":"bad","initiativePath":"initiative.md","tags":[],"understanding":{"summary":"x","actors":[],"functionalRequirements":[],"businessRules":[],"dataRequirements":[],"integrations":[],"technicalCapabilities":[],"searchTerms":[],"constraints":[],"unknowns":[]},"expected":{"requiredEntities":[],"acceptableEntities":[],"negativeEntities":[],"requiredProjects":[],"acceptableProjects":[],"negativeProjects":[],"recommendationTypes":[],"evidenceEntities":[],"analysisStatus":"complete"}}]}
                """);
            await Assert.ThrowsAsync<InvalidDataException>(() => new EvaluationSuiteSerializer().LoadAsync(Path.Combine(root, "suite.json")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Baseline_IsOnlyWrittenByExplicitStoreCall()
    {
        var root = Path.Combine(Path.GetTempPath(), $"brain-baseline-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "baseline.json");
        try
        {
            Assert.Null(await EvaluationResultStore.LoadBaselineAsync(path));
            Assert.False(File.Exists(path));
            await EvaluationResultStore.SaveBaselineAsync(path, Baseline(Aggregate()));
            Assert.NotNull(await EvaluationResultStore.LoadBaselineAsync(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static EvaluationAggregateMetrics Aggregate() => new(
        1, 1, 1, 1, 0.5, 0.5, 1, 1, 1, 0, 0, 10, 5, 1000, 1000, 1000, 0, 1, 0, 0, 0, 0);

    private static EvaluationBaseline Baseline(EvaluationAggregateMetrics aggregate) => new(
        1, "suite", "analyzer", EvaluationHarness.RetrievalVersion, 1, aggregate, []);

    private static EvaluationCaseResult CaseResult(string id, double recallAt10) => new(
        id, [], true,
        new RetrievalEvaluationMetrics(1, recallAt10, 1, 1, 1, 1, 1, 0, 0, 0, false, 1, 1),
        new RecommendationEvaluationMetrics(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, false, false),
        new ContextEvaluationMetrics(1, 1000, 0, 0, 0, 1, 1, 0, 2, false),
        [], [], [], [], []);

    private static string FindRepositoryFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "EngineeringBrain.sln"))) current = current.Parent;
        return Path.Combine([current!.FullName, .. parts]);
    }
}
