using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class InitiativeAnalysisService
{
    private readonly IReasoningProvider _provider;
    private readonly InitiativeCandidateRetriever _retriever;
    private readonly InitiativeContextBuilder _contextBuilder;
    private readonly AnalysisEvidenceValidator _validator;
    private readonly LocalInitiativeAnalysisStore _store;
    private readonly TokenEstimator _estimator;
    private readonly TokenBudgetOptions _budget;
    private readonly OutboundContextGuard _outboundGuard;

    public InitiativeAnalysisService(
        IReasoningProvider provider,
        InitiativeCandidateRetriever? retriever = null,
        InitiativeContextBuilder? contextBuilder = null,
        AnalysisEvidenceValidator? validator = null,
        LocalInitiativeAnalysisStore? store = null,
        TokenEstimator? estimator = null,
        TokenBudgetOptions? budget = null,
        OutboundContextGuard? outboundGuard = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _retriever = retriever ?? new InitiativeCandidateRetriever();
        _estimator = estimator ?? new TokenEstimator();
        _budget = budget ?? new TokenBudgetOptions();
        _contextBuilder = contextBuilder ?? new InitiativeContextBuilder(estimator: _estimator, budget: _budget);
        _validator = validator ?? new AnalysisEvidenceValidator();
        _store = store ?? new LocalInitiativeAnalysisStore();
        _outboundGuard = outboundGuard ?? new OutboundContextGuard();
    }

    public async Task<InitiativeAnalysisResult> AnalyzeAsync(
        InitiativeAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
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

        _outboundGuard.ThrowIfInvalid(_outboundGuard.ValidateInitiative(request.InitiativeText));
        var understandingCall = await _provider.GenerateStructuredAsync<InitiativeUnderstanding>(
            new ReasoningRequest(
                ReasoningStage.InitiativeUnderstanding,
                request.InterpretationModel,
                InitiativeAnalysisPrompts.Understanding,
                request.InitiativeText,
                _budget.InitiativeOutputTokens,
                understandingInputTokens),
            cancellationToken);
        var retrieval = _retriever.Retrieve(
            understandingCall.Value,
            request.Memory.Manifest,
            request.Memory.SourceSnapshot);
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

        _outboundGuard.ThrowIfInvalid(_outboundGuard.Validate(context));
        var analysisCall = await _provider.GenerateStructuredAsync<InitiativeAnalysis>(
            new ReasoningRequest(
                ReasoningStage.ArchitectureAnalysis,
                request.ReasoningModel,
                InitiativeAnalysisPrompts.ArchitectureAnalysis,
                context.Content,
                _budget.ReasoningOutputTokens,
                reasoningInputTokens),
            cancellationToken);
        var validated = _validator.Validate(analysisCall.Value, request.Memory.SourceSnapshot);
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
            validated,
            usage,
            null);
        if (request.PersistResult)
        {
            var path = await _store.SaveAsync(result, cancellationToken);
            result = result with { SavedAnalysisPath = path };
        }

        return result;
    }
}
