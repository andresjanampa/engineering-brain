using System.Text.Json;
using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class LiveEvaluationTests
{
    [Fact]
    public async Task LiveSuite_LoadsFiveSafeCases()
    {
        var suite = await new LiveEvaluationSuiteSerializer().LoadAsync(FindRepositoryFile("evaluations", "live-suite.json"));

        Assert.Equal(1, suite.LiveEvaluationSchemaVersion);
        Assert.Equal(5, suite.Cases.Count);
        Assert.All(suite.Cases, item => Assert.StartsWith("live-", item.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void LiveAuthorization_RequiresExplicitRemoteOptIn()
    {
        var read = false;
        var error = Assert.Throws<InvalidOperationException>(() => LiveEvaluationAuthorization.Authorize(false, false, _ =>
        {
            read = true;
            return "secret";
        }));
        Assert.False(read);
        Assert.Contains("eval-live", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveAuthorization_RequiresApiKey()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            LiveEvaluationAuthorization.Authorize(false, true, _ => null));
        Assert.Contains("OPENAI_API_KEY", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveAuthorization_FakeProviderNeedsNoKeyAndCannotMixModes()
    {
        Assert.Null(LiveEvaluationAuthorization.Authorize(true, false, _ => throw new InvalidOperationException()));
        Assert.Throws<InvalidOperationException>(() => LiveEvaluationAuthorization.Authorize(true, true));
    }

    [Fact]
    public void LivePlan_LimitsCasesRunsAndLogicalCalls()
    {
        var item = Case("one");
        var five = Suite([item, Case("two"), Case("three"), Case("four"), Case("five")]);

        Assert.Equal(10, LiveEvaluationPlanner.Create(five).ExpectedLogicalCalls);
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveEvaluationPlanner.Create(five, 4));
        Assert.Throws<InvalidDataException>(() => LiveEvaluationPlanner.Create(five, 3));
        Assert.Equal(6, LiveEvaluationPlanner.Create(five, 3, ["one"]).ExpectedLogicalCalls);
    }

    [Fact]
    public void UnderstandingMetrics_CompareCapabilitiesUnknownsAndSearchTerms()
    {
        var item = Case("case") with
        {
            GoldenUnderstanding = Understanding("business", "execute") with { Unknowns = ["delivery channel"] },
            Expected = Expectations() with
            {
                RequiredCapabilities = ["business execution"],
                ExpectedNeedsClarification = true
            }
        };
        var actual = Understanding("business", "execute") with
        {
            TechnicalCapabilities = ["business execution"],
            Unknowns = ["delivery channel"],
            SearchTerms = ["business", "execute", "unmappednoise"]
        };

        var metrics = new LiveEvaluationMetricCalculator().EvaluateUnderstanding(item, actual, ProjectMemoryTestFactory.Create());

        Assert.Equal(1, metrics.RequiredCapabilityHitRate);
        Assert.True(metrics.UnknownsDetected);
        Assert.Contains("unmappednoise", metrics.PotentiallyHarmfulSearchTerms);
    }

    [Fact]
    public async Task GoldenVsLiveRetrieval_ReportsInterpretationDamage()
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync();
        var item = Case("case");
        var calculator = new LiveEvaluationMetricCalculator();
        var retriever = new InitiativeCandidateRetriever();
        var golden = calculator.EvaluateRetrieval(
            retriever.Retrieve(item.GoldenUnderstanding, memory.Manifest, memory.SourceSnapshot), item.Expected);
        var actual = calculator.EvaluateRetrieval(
            retriever.Retrieve(Understanding("quantumflux"), memory.Manifest, memory.SourceSnapshot), item.Expected);

        var comparison = LiveEvaluationMetricCalculator.CompareRetrieval(golden, actual);

        Assert.Equal(1, comparison.Golden.RecallAt10);
        Assert.Equal(0, comparison.Actual.RecallAt10);
        Assert.True(comparison.MeanReciprocalRankDelta < 0);
    }

    [Fact]
    public async Task FakeRun_AggregatesUsageAndPersistsReviewWithoutKey()
    {
        using var fixture = await Fixture.CreateAsync();

        var result = await fixture.RunAsync(FakeFactory(fixture));

        Assert.Equal(2, result.Aggregate.Usage.LogicalCalls);
        Assert.Equal(2, result.Aggregate.Usage.ProviderAttempts);
        Assert.True(result.Aggregate.Usage.ActualInputTokens > 0);
        Assert.Equal(80, result.Aggregate.Usage.ReasoningTokens);
        Assert.True(File.Exists(result.SummaryPath));
        Assert.True(File.Exists(result.ReviewPath));
        var review = await File.ReadAllTextAsync(result.ReviewPath);
        Assert.Contains("Initiative understanding [1-5]:", review, StringComparison.Ordinal);
        Assert.DoesNotContain("OPENAI_API_KEY", review, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderFailure_IsRecordedAndPreviousCaseIsPreserved()
    {
        using var fixture = await Fixture.CreateAsync([Case("first"), Case("second")]);

        var result = await fixture.RunAsync((item, run) => item.Id == "first"
            ? new FakeLiveReasoningProvider(item, fixture.Memory.SourceSnapshot, run, "low", "medium")
            : new ThrowingProvider(structured: false, retries: 1));

        Assert.Equal(2, result.Cases.Count);
        Assert.Equal(LiveEvaluationExecutionStatus.Succeeded, result.Cases[0].Status);
        Assert.Equal(LiveEvaluationExecutionStatus.ProviderFailure, result.Cases[1].Status);
        Assert.Equal(1, result.Aggregate.Usage.Retries);
        Assert.Equal(4, result.Aggregate.Usage.ProviderAttempts);
        Assert.Equal(30, result.Cases[1].Usage.Single().ActualInputTokens);
        Assert.Equal(2, result.Cases[1].Usage.Single().ReasoningTokens);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(result.ResultDirectory, "cases"), "*.json").Length);
    }

    [Fact]
    public async Task StructuredOutputFailure_IsExplicitAndCounted()
    {
        using var fixture = await Fixture.CreateAsync();

        var result = await fixture.RunAsync((_, _) => new ThrowingProvider(structured: true));

        Assert.Equal(LiveEvaluationExecutionStatus.StructuredOutputFailure, result.Cases[0].Status);
        Assert.Equal(1, result.Aggregate.StructuredOutputFailures);
        Assert.Equal("StructuredOutputFailure", result.Cases[0].ErrorCategory);
    }

    [Fact]
    public async Task SecurityBlocked_PreventsProviderCall()
    {
        using var fixture = await Fixture.CreateAsync(initiative: "api_key=abcdefghijklmnop");
        var provider = new CountingProvider();

        var result = await fixture.RunAsync((_, _) => provider);

        Assert.Equal(LiveEvaluationExecutionStatus.SecurityBlocked, result.Cases[0].Status);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(1, result.Aggregate.SecurityBlocked);
        Assert.Empty(result.Cases[0].Usage);
    }

    [Fact]
    public async Task InvalidEvidence_RemainsInvalidAndNeverCountsAsFabricatedAccepted()
    {
        using var fixture = await Fixture.CreateAsync();
        var analysis = Analysis("entity:not-real");

        var result = await fixture.RunAsync((item, run) => new ScriptedProvider(item.GoldenUnderstanding, analysis));

        Assert.Equal(1, result.Aggregate.InvalidEvidence);
        Assert.Equal(0, result.Aggregate.FabricatedEntitiesAccepted);
        Assert.Equal(EvidenceValidationStatus.Invalid, result.Cases[0].Recommendations[0].ValidationStatus);
    }

    [Fact]
    public async Task ProviderErrors_AreRedactedBeforePersistence()
    {
        using var fixture = await Fixture.CreateAsync();
        const string secret = "supersecretvalue";

        var result = await fixture.RunAsync((_, _) => new RawThrowingProvider($"api_key={secret}"));

        var stored = string.Join('\n', Directory.GetFiles(result.ResultDirectory, "*", SearchOption.AllDirectories)
            .Select(File.ReadAllText));
        Assert.DoesNotContain(secret, stored, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", stored, StringComparison.Ordinal);
    }

    [Fact]
    public void Call2Metrics_CheckDecisionClarificationAndEvidence()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var item = Case("case");
        var evidence = InitiativeAnalysisTestData.EntityEvidence(snapshot, "entity:business-service");
        var recommendation = InitiativeAnalysisTestData.Recommendation(RecommendationDecision.Reuse, [evidence]);
        var analysis = InitiativeAnalysisTestData.Analysis(recommendation) with
        {
            RelevantEntityIds = ["entity:business-service"],
            RelevantProjectIds = ["project:business"]
        };
        var validated = new AnalysisEvidenceValidator().Validate(analysis, snapshot);

        var metrics = new LiveEvaluationMetricCalculator().EvaluateAnalysis(item, analysis, validated, snapshot);

        Assert.True(metrics.ExpectedDecisionTypePresent);
        Assert.Equal(1, metrics.EvidenceValidationRate);
        Assert.Empty(metrics.MissingRelevantEntities);
        Assert.Empty(metrics.MissingRelevantProjects);
    }

    [Fact]
    public void NeedsClarificationComparison_RequiresExpectedStatusAndRelevantQuestion()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var item = Case("ambiguous") with
        {
            GoldenUnderstanding = Understanding("notification") with { Unknowns = ["delivery channel"] },
            Expected = Expectations() with { ExpectedNeedsClarification = true, ExpectedStatus = InitiativeAnalysisStatus.NeedsClarification }
        };
        var analysis = InitiativeAnalysisTestData.Analysis() with
        {
            Status = InitiativeAnalysisStatus.NeedsClarification,
            ClarifyingQuestions = ["Which delivery channel is required?"]
        };

        var metrics = new LiveEvaluationMetricCalculator().EvaluateAnalysis(item, analysis, [], snapshot);

        Assert.True(metrics.NeedsClarificationCorrect);
        Assert.True(metrics.ClarifyingQuestionsRelevant);
    }

    [Fact]
    public void ConsistencyMetrics_UseSetOverlapInsteadOfTextEquality()
    {
        var first = SuccessfulResult("case", 1, [RecommendationDecision.Reuse], ["entity:a"]);
        var second = SuccessfulResult("case", 2, [RecommendationDecision.Reuse], ["entity:a", "entity:b"]);

        var metrics = LiveConsistencyCalculator.Calculate([first, second]);

        Assert.Equal(1, metrics.StatusAgreement);
        Assert.Equal(1, metrics.DecisionAgreement);
        Assert.Equal(0.5, metrics.EntityReferenceOverlap);
        Assert.Equal(1, metrics.EvidenceValidityAgreement);
    }

    [Fact]
    public async Task FixedClock_ProducesDeterministicOfflineRunAndArtifact()
    {
        using var fixture = await Fixture.CreateAsync();
        var first = await fixture.RunAsync(FakeFactory(fixture));
        var firstJson = await File.ReadAllTextAsync(first.SummaryPath);
        var second = await fixture.RunAsync(FakeFactory(fixture));

        Assert.Equal(first.RunId, second.RunId);
        Assert.Equal(firstJson, await File.ReadAllTextAsync(second.SummaryPath));
    }

    [Fact]
    public void Pricing_UsesActualAndCachedTokensWithoutHardcodedModelPrices()
    {
        var catalog = new LivePricingCatalog(1, [new LiveModelPricing("Fake", "model-a", 2, 1, 4)]);
        var usage = new ReasoningCallUsage(ReasoningStage.InitiativeUnderstanding, "Fake", "model-a",
            200, 100, 40, 20, 1, 0);

        Assert.Equal(0.00024m, catalog.Estimate(usage));
        Assert.Null(catalog.Estimate(usage with { Model = "unknown" }));
    }

    private static Func<LiveEvaluationCase, int, IReasoningProvider> FakeFactory(Fixture fixture) =>
        (item, run) => new FakeLiveReasoningProvider(item, fixture.Memory.SourceSnapshot, run, "low", "medium");

    private static LiveEvaluationSuite Suite(IReadOnlyList<LiveEvaluationCase> cases) => new(1, "live-suite", "safe", cases);

    private static LiveEvaluationCase Case(string id) => new(
        id,
        "Business service evaluation",
        "initiative.md",
        Understanding("BusinessService", "Execute"),
        Expectations());

    private static LiveEvaluationExpectations Expectations() => new(
        ["BusinessService"],
        ["Execute"],
        InitiativeAnalysisStatus.Complete,
        [RecommendationDecision.Reuse],
        ["Demo.Business.BusinessService"],
        [],
        ["Business"],
        [],
        false);

    private static InitiativeUnderstanding Understanding(params string[] terms) =>
        InitiativeAnalysisTestData.Understanding(terms);

    private static InitiativeAnalysis Analysis(string entityId) => new(
        InitiativeAnalysisStatus.Complete,
        "analysis",
        [],
        [entityId],
        [new AnalysisRecommendation(
            RecommendationDecision.Reuse,
            "fabricated",
            "invalid evidence test",
            EpistemicStatus.Fact,
            [new EvidenceReference(EvidenceKind.Entity, "repo-1", "main", entityId, "project:business",
                null, null, null, "src/Business/BusinessService.cs", 1, 2, ResolutionLevel.Semantic)],
            [], [])],
        [], [], [], "test");

    private static LiveEvaluationCaseResult SuccessfulResult(
        string caseId,
        int run,
        IReadOnlyList<RecommendationDecision> decisions,
        IReadOnlyList<string> entities)
    {
        var analysis = InitiativeAnalysisTestData.Analysis() with
        {
            RelevantEntityIds = entities,
            Recommendations = decisions.Select(value => InitiativeAnalysisTestData.Recommendation(value, [])).ToArray()
        };
        return new LiveEvaluationCaseResult(
            caseId, run, LiveEvaluationExecutionStatus.Succeeded, "initiative.md", "hash",
            Understanding("business"), null, null, null, null, analysis, [], null, [], [], null, null,
            new LiveHumanReview(null, null, null, null, null, null, null));
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "EngineeringBrain.sln"))) current = current.Parent;
        return Path.Combine([current!.FullName, .. parts]);
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(string root, string suitePath, ProjectMemorySyncResult memory, LiveEvaluationSuite suite)
        {
            Root = root;
            SuitePath = suitePath;
            Memory = memory;
            Suite = suite;
        }

        public string Root { get; }
        public string SuitePath { get; }
        public ProjectMemorySyncResult Memory { get; }
        public LiveEvaluationSuite Suite { get; }

        public static async Task<Fixture> CreateAsync(
            IReadOnlyList<LiveEvaluationCase>? cases = null,
            string initiative = "Add business execution.")
        {
            var root = Path.Combine(Path.GetTempPath(), $"brain-live-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "initiative.md"), initiative);
            var suitePath = Path.Combine(root, "live-suite.json");
            await File.WriteAllTextAsync(suitePath, "{}");
            var memory = await new ProjectMemoryService(store: new LocalProjectMemoryStore(Path.Combine(root, "memory")))
                .SyncAsync(ProjectMemoryTestFactory.Create());
            return new Fixture(root, suitePath, memory, Suite(cases ?? [Case("case")]));
        }

        public Task<LiveEvaluationRun> RunAsync(Func<LiveEvaluationCase, int, IReasoningProvider> factory)
        {
            var service = new LiveEvaluationService(
                store: new LocalLiveEvaluationStore(Path.Combine(Root, "data")),
                clock: () => new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero));
            return service.RunAsync(LiveEvaluationPlanner.Create(Suite), SuitePath, Memory,
                "Fake", factory, "model-a", "model-b", "low", "medium");
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }

    private sealed class ThrowingProvider(bool structured, int retries = 0) : IReasoningProvider
    {
        public string Name => "Fake";
        public Task<ReasoningResult<T>> GenerateStructuredAsync<T>(ReasoningRequest request, CancellationToken cancellationToken = default) =>
            throw new ReasoningProviderException(request.Stage, structured, 10, retries, "safe failure", 30, 10, 5, 2);
    }

    private sealed class RawThrowingProvider(string message) : IReasoningProvider
    {
        public string Name => "Fake";
        public Task<ReasoningResult<T>> GenerateStructuredAsync<T>(ReasoningRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(message);
    }

    private sealed class CountingProvider : IReasoningProvider
    {
        public string Name => "Fake";
        public int Calls { get; private set; }
        public Task<ReasoningResult<T>> GenerateStructuredAsync<T>(ReasoningRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("should not be called");
        }
    }

    private sealed class ScriptedProvider(InitiativeUnderstanding understanding, InitiativeAnalysis analysis) : IReasoningProvider
    {
        public string Name => "Fake";
        public Task<ReasoningResult<T>> GenerateStructuredAsync<T>(ReasoningRequest request, CancellationToken cancellationToken = default)
        {
            object value = typeof(T) == typeof(InitiativeUnderstanding) ? understanding : analysis;
            return Task.FromResult(new ReasoningResult<T>((T)value,
                new ReasoningCallUsage(request.Stage, Name, request.Model, request.EstimatedInputTokens,
                    request.EstimatedInputTokens, 0, 10, 1, 0)));
        }
    }
}
