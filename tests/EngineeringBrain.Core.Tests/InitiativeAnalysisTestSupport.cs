using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

internal sealed class FakeReasoningProvider : IReasoningProvider
{
    private readonly Func<ReasoningRequest, Type, object> _response;

    public FakeReasoningProvider(Func<ReasoningRequest, Type, object> response)
    {
        _response = response;
    }

    public string Name => "Fake";

    public List<ReasoningRequest> Requests { get; } = [];

    public List<ApprovedReasoningRequest> ApprovedRequests { get; } = [];

    public Task<ReasoningResult<T>> GenerateStructuredAsync<T>(
        ApprovedReasoningRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request.EnsureIntegrity();
        ApprovedRequests.Add(request);
        var raw = request.Request;
        Requests.Add(raw);
        return Task.FromResult(new ReasoningResult<T>(
            (T)_response(raw, typeof(T)),
            new ReasoningCallUsage(
                raw.Stage,
                Name,
                raw.Model,
                raw.EstimatedInputTokens,
                raw.EstimatedInputTokens - 1,
                0,
                42,
                1,
                0)));
    }
}

internal sealed class InitiativeMemoryFixture : IDisposable
{
    public InitiativeMemoryFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), $"engineering-brain-initiative-{Guid.NewGuid():N}");
    }

    public string Root { get; }

    public async Task<ProjectMemorySyncResult> CreateMemoryAsync(RepositorySnapshot? snapshot = null) =>
        await new ProjectMemoryService(store: new LocalProjectMemoryStore(Root))
            .SyncAsync(snapshot ?? ProjectMemoryTestFactory.Create());

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}

internal static class InitiativeAnalysisTestData
{
    public static InitiativeUnderstanding Understanding(params string[] terms) => new(
        "Add an architecture capability.",
        [],
        ["Support the requested capability."],
        [],
        [],
        [],
        terms,
        terms,
        [],
        []);

    public static InitiativeAnalysis Analysis(params AnalysisRecommendation[] recommendations) => new(
        InitiativeAnalysisStatus.Complete,
        "Architecture analysis",
        recommendations.SelectMany(item => item.Evidence).Where(item => item.ProjectId is not null)
            .Select(item => item.ProjectId!).Distinct(StringComparer.Ordinal).ToArray(),
        recommendations.SelectMany(item => item.Evidence).Where(item => item.EntityId is not null)
            .Select(item => item.EntityId!).Distinct(StringComparer.Ordinal).ToArray(),
        recommendations,
        [],
        [],
        [],
        "Confidence is limited to validated static-analysis evidence.");

    public static EvidenceReference EntityEvidence(
        RepositorySnapshot snapshot,
        string entityId,
        string? path = null)
    {
        var entity = snapshot.Entities.Single(item => item.Id == entityId);
        return new EvidenceReference(
            EvidenceKind.Entity,
            snapshot.Repository.Id,
            snapshot.Git.Branch ?? "(no branch)",
            entity.Id,
            entity.ProjectId,
            null,
            null,
            null,
            path ?? entity.RelativeFilePath,
            entity.StartLine,
            entity.EndLine,
            entity.ResolutionLevel);
    }

    public static AnalysisRecommendation Recommendation(
        RecommendationDecision decision,
        IReadOnlyList<EvidenceReference> evidence,
        IReadOnlyList<PolicyRelevantAction>? policyRelevantActions = null) => new(
        decision,
        "Subject",
        "Reason grounded in selected evidence.",
        decision == RecommendationDecision.Create ? EpistemicStatus.Proposal : EpistemicStatus.Inference,
        evidence,
        ["Potential impact remains bounded by the current graph."],
        [],
        policyRelevantActions ?? []);
}
