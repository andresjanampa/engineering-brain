using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class InitiativeAnalysisService
{
    private readonly IReasoningProvider _provider;
    private readonly InitiativeCandidateRetriever _retriever;
    private readonly InitiativeContextBuilder _contextBuilder;
    private readonly AnalysisEvidenceValidator _validator;
    private readonly PolicyComplianceValidator _policyValidator;
    private readonly LocalInitiativeAnalysisStore _store;
    private readonly TokenEstimator _estimator;
    private readonly TokenBudgetOptions _budget;
    private readonly OutboundRequestGate _outboundGate;

    public InitiativeAnalysisService(
        IReasoningProvider provider,
        InitiativeCandidateRetriever? retriever = null,
        InitiativeContextBuilder? contextBuilder = null,
        AnalysisEvidenceValidator? validator = null,
        PolicyComplianceValidator? policyValidator = null,
        LocalInitiativeAnalysisStore? store = null,
        TokenEstimator? estimator = null,
        TokenBudgetOptions? budget = null,
        OutboundRequestGate? outboundGate = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _retriever = retriever ?? new InitiativeCandidateRetriever();
        _estimator = estimator ?? new TokenEstimator();
        _budget = budget ?? new TokenBudgetOptions();
        _contextBuilder = contextBuilder ?? new InitiativeContextBuilder(estimator: _estimator, budget: _budget);
        _validator = validator ?? new AnalysisEvidenceValidator();
        _policyValidator = policyValidator ?? new PolicyComplianceValidator();
        _store = store ?? new LocalInitiativeAnalysisStore();
        _outboundGate = outboundGate ?? new OutboundRequestGate();
    }

    public async Task<InitiativeAnalysisResult> AnalyzeAsync(
        InitiativeAnalysisRequest request,
        CancellationToken cancellationToken = default) => await AnalyzeAsync(
        request,
        ReviewedConceptResolutionResult.Absent,
        preparedCall1: null,
        cancellationToken);

    public async Task<InitiativeAnalysisResult> AnalyzeAsync(
        InitiativeAnalysisRequest request,
        ReviewedConceptResolutionResult reviewedConcepts,
        CancellationToken cancellationToken = default) => await AnalyzeAsync(
        request,
        reviewedConcepts,
        preparedCall1: null,
        cancellationToken);

    public async Task<InitiativeAnalysisResult> AnalyzeAsync(
        InitiativeAnalysisRequest request,
        ReviewedConceptResolutionResult reviewedConcepts,
        ApprovedReasoningRequest? preparedCall1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reviewedConcepts);
        if (string.IsNullOrWhiteSpace(request.InitiativeText))
        {
            throw new ArgumentException("Initiative file is empty.", nameof(request));
        }

        var initiativeTokens = _estimator.Estimate(request.InitiativeText);
        var understandingInputTokens = initiativeTokens + _estimator.Estimate(InitiativeAnalysisPrompts.Understanding);
        if (understandingInputTokens > _budget.MaximumInitiativeInputTokens)
        {
            throw new ArgumentException(
                $"Initiative exceeds the {_budget.MaximumInitiativeInputTokens} estimated token limit.",
                nameof(request));
        }

        var understandingRequest = new ReasoningRequest(
                ReasoningStage.InitiativeUnderstanding,
                request.InterpretationModel,
                InitiativeAnalysisPrompts.Understanding,
                request.InitiativeText,
                _budget.InitiativeOutputTokens,
                understandingInputTokens);
        var approvedCall1 = preparedCall1 ?? _outboundGate.ApproveExact(understandingRequest);
        if (preparedCall1 is not null)
        {
            preparedCall1.EnsureIntegrity();
            var expectedFingerprint = OutboundRequestFingerprint.Create(understandingRequest);
            if (!expectedFingerprint.Equals(preparedCall1.Assessment.PayloadFingerprint, StringComparison.Ordinal))
            {
                throw OutboundSecurityException.IntegrityFailure(preparedCall1.Assessment);
            }
        }

        var understandingCall = await _provider.GenerateStructuredAsync<InitiativeUnderstanding>(
            approvedCall1,
            cancellationToken);
        var retrieval = _retriever.Retrieve(
            understandingCall.Value,
            request.Memory.Manifest,
            request.Memory.SourceSnapshot,
            reviewedConcepts.Profiles);
        var context = await _contextBuilder.BuildAsync(
            understandingCall.Value,
            retrieval,
            request.Memory,
            cancellationToken);
        var reasoningInputTokens = context.EstimatedTokens + _estimator.Estimate(InitiativeAnalysisPrompts.ArchitectureAnalysis);
        if (reasoningInputTokens > _budget.MaximumReasoningInputTokens)
        {
            throw new InvalidDataException(
                $"Reasoning input exceeds the {_budget.MaximumReasoningInputTokens} estimated token hard limit.");
        }

        var analysisRequest = new ReasoningRequest(
                ReasoningStage.ArchitectureAnalysis,
                request.ReasoningModel,
                InitiativeAnalysisPrompts.ArchitectureAnalysis,
                context.Content,
                _budget.ReasoningOutputTokens,
                reasoningInputTokens);
        var approvedCall2 = _outboundGate.ApproveExact(analysisRequest, context);
        var analysisCall = await _provider.GenerateStructuredAsync<InitiativeAnalysis>(
            approvedCall2,
            cancellationToken);
        var validated = _validator.Validate(analysisCall.Value, request.Memory.SourceSnapshot);
        var governance = _policyValidator.Evaluate(validated);
        var calls = new[] { understandingCall.Usage, analysisCall.Usage };
        var usage = new InitiativeAnalysisUsage(
            calls,
            calls.Sum(item => item.EstimatedInputTokens),
            calls.Sum(item => item.ActualInputTokens ?? 0),
            calls.Sum(item => item.CachedInputTokens ?? 0),
            calls.Sum(item => item.ActualOutputTokens ?? 0));
        var branch = request.Memory.SourceSnapshot.Git.Branch ?? "(no branch)";
        var contentHash = LocalInitiativeAnalysisStore.CreateContentHash(request.InitiativeText);
        var analysisId = LocalInitiativeAnalysisStore.CreateAnalysisId(
            request.Memory.SourceSnapshot.Repository.Id,
            branch,
            contentHash);
        var result = new InitiativeAnalysisResult(
            analysisId,
            Path.GetFileName(request.InitiativeFileName),
            contentHash,
            request.Memory.SourceSnapshot.Repository,
            request.Memory.SourceSnapshot.Git,
            understandingCall.Value,
            retrieval,
            context,
            analysisCall.Value,
            governance.Recommendations,
            governance.Outcome,
            usage,
            null,
            [approvedCall1.Assessment, approvedCall2.Assessment]);
        if (request.PersistResult)
        {
            var path = await _store.SaveAsync(result, cancellationToken);
            result = result with { SavedAnalysisPath = path };
        }

        return result;
    }
}
