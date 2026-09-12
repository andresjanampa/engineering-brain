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
    public void GroupedMetrics_ReportTuningHoldoutAndAllSeparately()
    {
        var tuning = CaseResult("tuning", recallAt10: 1, EvaluationSplit.Tuning);
        var holdout = CaseResult("holdout", recallAt10: 0, EvaluationSplit.Holdout);

        var grouped = EvaluationHarness.Grouped([tuning, holdout]);

        Assert.Equal(1, grouped.Tuning.Cases);
        Assert.Equal(1, grouped.Tuning.RecallAt10);
        Assert.Equal(1, grouped.Holdout.Cases);
        Assert.Equal(0, grouped.Holdout.RecallAt10);
        Assert.Equal(2, grouped.All.Cases);
        Assert.Equal(0.5, grouped.All.RecallAt10);
    }

    [Fact]
    public void GroupedBaselineComparison_UsesCasesFromTheCorrectSplit()
    {
        var aggregate = Aggregate();
        var baseline = Baseline(aggregate) with
        {
            CaseCount = 2,
            Cases =
            [
                new EvaluationBaselineCase("tuning", 1, 1, 1, 1000, EvaluationSplit.Tuning),
                new EvaluationBaselineCase("holdout", 1, 1, 1, 1000, EvaluationSplit.Holdout)
            ],
            Splits = new(aggregate, aggregate, aggregate)
        };
        EvaluationCaseResult[] cases =
        [
            CaseResult("tuning", 1, EvaluationSplit.Tuning),
            CaseResult("holdout", 0, EvaluationSplit.Holdout)
        ];
        var current = new EvaluationGroupedMetrics(aggregate, aggregate with { RecallAt10 = 0 }, aggregate);

        var regressions = new EvaluationBaselineComparer().CompareGrouped(baseline, current, cases);

        Assert.Contains(regressions, item => item.Metric == "HOLDOUT Recall@10");
        Assert.Contains(regressions, item => item.Metric == "HOLDOUT holdout Recall@10");
        Assert.DoesNotContain(regressions, item => item.Metric.Contains("tuning Recall@10", StringComparison.Ordinal));
    }

    [Fact]
    public void GroupedMetrics_AreDeterministicAcrossCaseOrder()
    {
        var first = CaseResult("first", 1, EvaluationSplit.Tuning);
        var second = CaseResult("second", 0, EvaluationSplit.Holdout);

        Assert.Equal(EvaluationHarness.Grouped([first, second]), EvaluationHarness.Grouped([second, first]));
    }

    [Fact]
    public async Task SuiteLoader_LoadsVersionOneCorpus()
    {
        var path = FindRepositoryFile("evaluations", "suite.json");
        var suite = await new EvaluationSuiteSerializer().LoadAsync(path);
        Assert.Equal(1, suite.EvaluationSchemaVersion);
        Assert.Equal(16, suite.Cases.Count);
        Assert.Equal(10, suite.Cases.Count(item => item.Split == EvaluationSplit.Tuning));
        Assert.Equal(6, suite.Cases.Count(item => item.Split == EvaluationSplit.Holdout));
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

    [Fact]
    public async Task BaselineStore_PreservesHoldoutSplitAndDoesNotRewriteOnLoad()
    {
        var root = Path.Combine(Path.GetTempPath(), $"brain-baseline-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "baseline.json");
        var aggregate = Aggregate();
        var baseline = Baseline(aggregate) with
        {
            Cases = [new EvaluationBaselineCase("holdout", 1, 1, 1, 1000, EvaluationSplit.Holdout)],
            Splits = new(aggregate, aggregate, aggregate)
        };
        try
        {
            await EvaluationResultStore.SaveBaselineAsync(path, baseline);
            var before = await File.ReadAllTextAsync(path);

            var loaded = await EvaluationResultStore.LoadBaselineAsync(path);

            Assert.Equal(EvaluationSplit.Holdout, Assert.Single(loaded!.Cases).Split);
            Assert.NotNull(loaded.Splits);
            Assert.Equal(before, await File.ReadAllTextAsync(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static EvaluationAggregateMetrics Aggregate() => new(
        Cases: 1, Passed: 1, RecallAt5: 1, RecallAt10: 1, PrecisionAt5: 0.5, PrecisionAt10: 0.5,
        MeanReciprocalRank: 1, ProjectRecallAt3: 1, ProjectMeanReciprocalRank: 1,
        NonTestCaseTestCandidateRatioAt5: 0, NonTestCaseTestCandidateRatioAt10: 0,
        TestRelevantCaseTestCandidateRatioAt5: 0, TestRelevantCaseTestCandidateRatioAt10: 0,
        AverageCandidateCount: 10, AverageSelectedComponents: 5, AverageCall2Tokens: 1000,
        MedianCall2Tokens: 1000, MaximumCall2Tokens: 1000, BudgetViolations: 0,
        EvidenceValidationRate: 1, InvalidEvidenceCount: 0, FabricatedEntitiesAccepted: 0,
        NeedsClarificationExpected: 0, NeedsClarificationActual: 0);

    private static EvaluationBaseline Baseline(EvaluationAggregateMetrics aggregate) => new(
        1, "suite", "analyzer", EvaluationHarness.RetrievalVersion, 1, aggregate, []);

    private static EvaluationCaseResult CaseResult(
        string id,
        double recallAt10,
        EvaluationSplit split = EvaluationSplit.Tuning) => new(
        id, [], split, true,
        new RetrievalEvaluationMetrics(1, recallAt10, 1, 1, 1, 1, 1, 0, 0, 0, 0, false, 1, 1),
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
