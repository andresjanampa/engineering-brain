using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class InitiativeAnalysisServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_NormalInitiativeUsesTwoStructuredStagesAndCapturesUsage()
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync();
        var understanding = InitiativeAnalysisTestData.Understanding("business", "execute");
        var evidence = InitiativeAnalysisTestData.EntityEvidence(memory.SourceSnapshot, "entity:business-service");
        var analysis = InitiativeAnalysisTestData.Analysis(
            InitiativeAnalysisTestData.Recommendation(RecommendationDecision.Reuse, [evidence]));
        var provider = Provider(understanding, analysis);

        var result = await Service(provider, fixture.Root).AnalyzeAsync(Request(memory, "Add business execution."));

        Assert.Equal(2, provider.Requests.Count);
        Assert.Equal(ReasoningStage.InitiativeUnderstanding, provider.Requests[0].Stage);
        Assert.Equal(ReasoningStage.ArchitectureAnalysis, provider.Requests[1].Stage);
        Assert.Equal(2, result.Usage.Calls.Count);
        var recommendation = Assert.Single(result.Recommendations);
        Assert.Equal(EvidenceValidationStatus.Validated, recommendation.ValidatedRecommendation.ValidationStatus);
        Assert.Equal(RecommendationDisposition.Accepted, recommendation.Disposition);
        Assert.Equal(PolicyOutcome.Allowed, result.PolicyOutcome);
    }

    [Fact]
    public async Task AnalyzeAsync_UnrelatedInitiativeSupportsNoCandidatesAndCreateProposal()
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync();
        var understanding = InitiativeAnalysisTestData.Understanding("quantumflux");
        var analysis = InitiativeAnalysisTestData.Analysis(
            InitiativeAnalysisTestData.Recommendation(RecommendationDecision.Create, []));
        var provider = Provider(understanding, analysis);

        var result = await Service(provider, fixture.Root).AnalyzeAsync(Request(memory, "Add quantumflux capability."));

        Assert.Empty(result.Retrieval.Components);
        Assert.Contains("NO_STRONG_COMPONENT_CANDIDATES", provider.Requests[1].UserData, StringComparison.Ordinal);
        Assert.Equal(EvidenceValidationStatus.Proposal, result.Recommendations[0].ValidatedRecommendation.ValidationStatus);
        Assert.Equal(RecommendationDisposition.NeedsReview, result.Recommendations[0].Disposition);
    }

    [Fact]
    public async Task AnalyzeAsync_IncompleteInitiativeCanReturnNeedsClarification()
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync();
        var understanding = InitiativeAnalysisTestData.Understanding("unknown") with { Unknowns = ["Scope is missing."] };
        var analysis = InitiativeAnalysisTestData.Analysis() with
        {
            Status = InitiativeAnalysisStatus.NeedsClarification,
            ClarifyingQuestions = ["What behavior is required?"]
        };
        var provider = Provider(understanding, analysis);

        var result = await Service(provider, fixture.Root).AnalyzeAsync(Request(memory, "Improve it."));

        Assert.Equal(InitiativeAnalysisStatus.NeedsClarification, result.Analysis.Status);
        Assert.NotEmpty(result.Analysis.ClarifyingQuestions);
    }

    [Theory]
    [InlineData(RecommendationDecision.Reuse, EvidenceValidationStatus.Validated)]
    [InlineData(RecommendationDecision.Extend, EvidenceValidationStatus.Validated)]
    [InlineData(RecommendationDecision.AvoidModifying, EvidenceValidationStatus.Validated)]
    [InlineData(RecommendationDecision.Create, EvidenceValidationStatus.Proposal)]
    public async Task AnalyzeAsync_ValidatesEachDecision(
        RecommendationDecision decision,
        EvidenceValidationStatus expected)
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync();
        var evidence = decision == RecommendationDecision.Create
            ? Array.Empty<EvidenceReference>()
            : [InitiativeAnalysisTestData.EntityEvidence(memory.SourceSnapshot, "entity:service")];
        var provider = Provider(
            InitiativeAnalysisTestData.Understanding("core"),
            InitiativeAnalysisTestData.Analysis(InitiativeAnalysisTestData.Recommendation(decision, evidence)));

        var result = await Service(provider, fixture.Root).AnalyzeAsync(Request(memory, "Extend core."));

        Assert.Equal(expected, result.Recommendations[0].ValidatedRecommendation.ValidationStatus);
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotRepairFabricatedEvidence()
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync();
        var fabricated = InitiativeAnalysisTestData.EntityEvidence(memory.SourceSnapshot, "entity:service") with
        {
            EntityId = "entity:not-real"
        };
        var provider = Provider(
            InitiativeAnalysisTestData.Understanding("core"),
            InitiativeAnalysisTestData.Analysis(
                InitiativeAnalysisTestData.Recommendation(RecommendationDecision.Reuse, [fabricated])));

        var result = await Service(provider, fixture.Root).AnalyzeAsync(Request(memory, "Reuse core."));

        Assert.Equal(EvidenceValidationStatus.Invalid, result.Recommendations[0].ValidatedRecommendation.ValidationStatus);
        Assert.Equal("entity:not-real", result.Recommendations[0].ValidatedRecommendation.Recommendation.Evidence[0].EntityId);
    }

    [Fact]
    public async Task AnalyzeAsync_ContextSelectionIsDeterministic()
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync();
        var provider = Provider(
            InitiativeAnalysisTestData.Understanding("business", "execute"),
            InitiativeAnalysisTestData.Analysis(InitiativeAnalysisTestData.Recommendation(RecommendationDecision.Create, [])));
        var service = Service(provider, fixture.Root);

        var first = await service.AnalyzeAsync(Request(memory, "Add business execution."));
        var second = await service.AnalyzeAsync(Request(memory, "Add business execution."));

        Assert.Equal(first.Context.Content, second.Context.Content);
        Assert.Equal(first.AnalysisId, second.AnalysisId);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsEmptyAndOversizedInitiativesBeforeProviderCall()
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync();
        var provider = Provider(InitiativeAnalysisTestData.Understanding("core"), InitiativeAnalysisTestData.Analysis());
        var service = Service(provider, fixture.Root);

        await Assert.ThrowsAsync<ArgumentException>(() => service.AnalyzeAsync(Request(memory, " ")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.AnalyzeAsync(Request(memory, new string('x', 40_001))));
        Assert.Empty(provider.Requests);
    }

    [Fact]
    public async Task AnalyzeAsync_GovernsAfterEvidenceValidationAndPersistsSchemaTwo()
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync();
        var action = new PolicyRelevantAction(
            PolicyActionOperation.RemoteTransmission,
            PolicyActionBoundary.Remote,
            PolicyContentScope.CompleteRepository,
            PolicyAuthorizationMode.Explicit,
            null,
            null);
        var original = InitiativeAnalysisTestData.Recommendation(RecommendationDecision.Create, [], [action]);
        var provider = Provider(
            InitiativeAnalysisTestData.Understanding("repository upload"),
            InitiativeAnalysisTestData.Analysis(original));
        var store = new LocalInitiativeAnalysisStore(fixture.Root);
        var service = new InitiativeAnalysisService(provider, store: store);

        var result = await service.AnalyzeAsync(Request(memory, "Upload the repository.") with { PersistResult = true });
        var persisted = await store.LoadAsync(result.SavedAnalysisPath!);

        Assert.Equal(2, persisted.SchemaVersion);
        Assert.Equal(PolicyOutcome.Blocked, persisted.PolicyOutcome);
        var persistedRecommendation = Assert.Single(persisted.Recommendations);
        Assert.Equal(RecommendationDisposition.Rejected, persistedRecommendation.Disposition);
        Assert.Equal(original.Decision, persistedRecommendation.ValidatedRecommendation.Recommendation.Decision);
        Assert.Equal(original.Subject, persistedRecommendation.ValidatedRecommendation.Recommendation.Subject);
        Assert.Equal(
            Assert.Single(original.PolicyRelevantActions),
            Assert.Single(persistedRecommendation.ValidatedRecommendation.Recommendation.PolicyRelevantActions));
        Assert.Equal(2, provider.Requests.Count);
    }

    [Fact]
    public async Task AnalysisStore_RejectsHistoricalSchemaBeforeDeserialization()
    {
        using var fixture = new InitiativeMemoryFixture();
        var path = Path.Combine(fixture.Root, "historical.json");
        Directory.CreateDirectory(fixture.Root);
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":1}");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new LocalInitiativeAnalysisStore(fixture.Root).LoadAsync(path));

        Assert.Contains("schema 1 is unsupported", error.Message, StringComparison.Ordinal);
    }

    private static InitiativeAnalysisService Service(FakeReasoningProvider provider, string root) => new(
        provider,
        store: new LocalInitiativeAnalysisStore(root));

    private static InitiativeAnalysisRequest Request(ProjectMemorySyncResult memory, string initiative) => new(
        "initiative.md",
        initiative,
        memory,
        "interpretation-model",
        "reasoning-model",
        PersistResult: false);

    private static FakeReasoningProvider Provider(
        InitiativeUnderstanding understanding,
        InitiativeAnalysis analysis) => new((request, type) =>
        type == typeof(InitiativeUnderstanding) ? understanding : analysis);
}
