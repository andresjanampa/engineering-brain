using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class FakeLiveReasoningProvider : IReasoningProvider
{
    private readonly LiveEvaluationCase _item;
    private readonly RepositorySnapshot _snapshot;
    private readonly int _runNumber;
    private readonly string _interpretationEffort;
    private readonly string _analysisEffort;

    public FakeLiveReasoningProvider(
        LiveEvaluationCase item,
        RepositorySnapshot snapshot,
        int runNumber,
        string interpretationEffort,
        string analysisEffort)
    {
        _item = item;
        _snapshot = snapshot;
        _runNumber = runNumber;
        _interpretationEffort = interpretationEffort;
        _analysisEffort = analysisEffort;
    }

    public string Name => "Fake";

    public Task<ReasoningResult<T>> GenerateStructuredAsync<T>(
        ReasoningRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        object value = typeof(T) == typeof(InitiativeUnderstanding)
            ? _item.GoldenUnderstanding
            : typeof(T) == typeof(InitiativeAnalysis)
                ? CreateAnalysis()
                : throw new InvalidOperationException($"Fake live provider does not support {typeof(T).Name}.");
        var effort = request.Stage == ReasoningStage.InitiativeUnderstanding
            ? _interpretationEffort
            : _analysisEffort;
        return Task.FromResult(new ReasoningResult<T>(
            (T)value,
            new ReasoningCallUsage(
                request.Stage,
                Name,
                request.Model,
                request.EstimatedInputTokens,
                request.EstimatedInputTokens,
                _runNumber > 1 ? request.EstimatedInputTokens / 2 : 0,
                request.Stage == ReasoningStage.InitiativeUnderstanding ? 120 : 240,
                5,
                0,
                effort,
                request.Stage == ReasoningStage.ArchitectureAnalysis ? 60 : 20)));
    }

    private InitiativeAnalysis CreateAnalysis()
    {
        var entityIds = _item.Expected.RequiredEntities.Concat(_item.Expected.AcceptableEntities)
            .Select(FindEntityId).Where(value => value is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
        var projectIds = _item.Expected.RequiredProjects.Concat(_item.Expected.AcceptableProjects)
            .Select(FindProjectId).Where(value => value is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
        var decision = _item.Expected.AcceptableDecisionTypes.FirstOrDefault(RecommendationDecision.Create);
        IReadOnlyList<AnalysisRecommendation> recommendations = _item.Expected.ExpectedNeedsClarification
            ? []
            : [new AnalysisRecommendation(
                decision,
                _item.Description,
                "Deterministic fake live evaluation response.",
                decision == RecommendationDecision.Create ? EpistemicStatus.Proposal : EpistemicStatus.Fact,
                decision == RecommendationDecision.Create || entityIds.Length == 0 ? [] : [Evidence(entityIds[0])],
                [],
                [])];
        return new InitiativeAnalysis(
            _item.Expected.ExpectedNeedsClarification ? InitiativeAnalysisStatus.NeedsClarification : _item.Expected.ExpectedStatus,
            "Deterministic fake live evaluation analysis.",
            projectIds,
            entityIds,
            recommendations,
            [],
            _item.Expected.ExpectedNeedsClarification ? _item.GoldenUnderstanding.Unknowns : [],
            _item.Expected.ExpectedNeedsClarification ? ["Which trigger, recipients, and delivery channel are required?"] : [],
            "Fake output validates the live harness without making a network call.");
    }

    private EvidenceReference Evidence(string entityId)
    {
        var entity = _snapshot.Entities.Single(value => value.Id == entityId);
        return new EvidenceReference(
            EvidenceKind.Entity,
            _snapshot.Repository.Id,
            _snapshot.Git.Branch ?? "(no branch)",
            entity.Id,
            entity.ProjectId,
            null,
            null,
            null,
            entity.RelativeFilePath,
            entity.StartLine,
            entity.EndLine,
            entity.ResolutionLevel);
    }

    private string? FindEntityId(string value) => _snapshot.Entities.FirstOrDefault(entity =>
        entity.Id.Equals(value, StringComparison.OrdinalIgnoreCase)
        || entity.Name.Equals(value, StringComparison.OrdinalIgnoreCase)
        || entity.FullName.Equals(value, StringComparison.OrdinalIgnoreCase))?.Id;

    private string? FindProjectId(string value) => _snapshot.Projects.FirstOrDefault(project =>
        project.Id.Equals(value, StringComparison.OrdinalIgnoreCase)
        || project.Name.Equals(value, StringComparison.OrdinalIgnoreCase)
        || Path.GetFileNameWithoutExtension(project.RelativePath).Equals(value, StringComparison.OrdinalIgnoreCase))?.Id;
}
