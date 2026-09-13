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
        ApprovedReasoningRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request.EnsureIntegrity();
        var raw = request.Request;
        object value = typeof(T) == typeof(InitiativeUnderstanding)
            ? _item.GoldenUnderstanding
            : typeof(T) == typeof(InitiativeAnalysis)
                ? CreateAnalysis()
                : throw new InvalidOperationException($"Fake live provider does not support {typeof(T).Name}.");
        var effort = raw.Stage == ReasoningStage.InitiativeUnderstanding
            ? _interpretationEffort
            : _analysisEffort;
        return Task.FromResult(new ReasoningResult<T>(
            (T)value,
            new ReasoningCallUsage(
                raw.Stage,
                Name,
                raw.Model,
                raw.EstimatedInputTokens,
                raw.EstimatedInputTokens,
                _runNumber > 1 ? raw.EstimatedInputTokens / 2 : 0,
                raw.Stage == ReasoningStage.InitiativeUnderstanding ? 120 : 240,
                5,
                0,
                effort,
                raw.Stage == ReasoningStage.ArchitectureAnalysis ? 60 : 20)));
    }

    private InitiativeAnalysis CreateAnalysis()
    {
        var entityIds = _item.RepositoryExpectations.RequiredEntities.Concat(_item.RepositoryExpectations.AcceptableEntities)
            .Select(FindEntityId).Where(value => value is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
        var projectIds = _item.RepositoryExpectations.RequiredProjects.Concat(_item.RepositoryExpectations.AcceptableProjects)
            .Select(FindProjectId).Where(value => value is not null).Cast<string>().Distinct(StringComparer.Ordinal).ToArray();
        var recommendations = new List<AnalysisRecommendation>();
        if (_item.AnalysisExpectations.AcceptableDecisionTypes.Count > 0)
        {
            var decision = _item.AnalysisExpectations.AcceptableDecisionTypes[0];
            recommendations.Add(new AnalysisRecommendation(
                decision,
                _item.Description,
                "Deterministic fake live evaluation response.",
                decision == RecommendationDecision.Create ? EpistemicStatus.Proposal : EpistemicStatus.Fact,
                decision == RecommendationDecision.Create || entityIds.Length == 0 ? [] : [Evidence(entityIds[0])],
                [],
                [],
                []));
        }

        if (_item.PolicyExpectations.Activations.Any(expectation => expectation.ExpectedActive
            && expectation.PolicyId == SystemPolicyCatalog.RemoteCompleteRepositoryId))
        {
            recommendations.Add(new AnalysisRecommendation(
                RecommendationDecision.Create,
                "Remote complete-repository transmission capability",
                "Deterministic fake action for exercising local policy governance.",
                EpistemicStatus.Proposal,
                [],
                [],
                [],
                [new PolicyRelevantAction(
                    PolicyActionOperation.RemoteTransmission,
                    PolicyActionBoundary.Remote,
                    PolicyContentScope.CompleteRepository,
                    PolicyAuthorizationMode.Automatic,
                    null,
                    null)]));
        }

        var status = _item.AnalysisExpectations.AcceptableStatuses
            .FirstOrDefault(InitiativeAnalysisStatus.Complete);
        var questions = _item.AnalysisExpectations.ExpectedClarificationTopics
            .Select(topic => $"What requirements apply to {topic}?")
            .ToArray();
        return new InitiativeAnalysis(
            status,
            "Deterministic fake live evaluation analysis.",
            projectIds,
            entityIds,
            recommendations,
            [],
            status == InitiativeAnalysisStatus.NeedsClarification ? _item.GoldenUnderstanding.Unknowns : [],
            questions,
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
