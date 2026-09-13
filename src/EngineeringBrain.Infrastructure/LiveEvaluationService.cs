using System.Security.Cryptography;
using System.Text;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class LiveEvaluationService
{
    private readonly InitiativeCandidateRetriever _retriever;
    private readonly InitiativeContextBuilder _contextBuilder;
    private readonly AnalysisEvidenceValidator _validator;
    private readonly PolicyComplianceValidator _policyValidator;
    private readonly TokenEstimator _estimator;
    private readonly TokenBudgetOptions _budget;
    private readonly OutboundRequestGate _gate;
    private readonly LiveEvaluationMetricCalculator _metrics;
    private readonly LocalLiveEvaluationStore _store;
    private readonly Func<DateTimeOffset> _clock;

    public LiveEvaluationService(
        InitiativeCandidateRetriever? retriever = null,
        InitiativeContextBuilder? contextBuilder = null,
        AnalysisEvidenceValidator? validator = null,
        PolicyComplianceValidator? policyValidator = null,
        TokenEstimator? estimator = null,
        TokenBudgetOptions? budget = null,
        OutboundRequestGate? gate = null,
        LiveEvaluationMetricCalculator? metrics = null,
        LocalLiveEvaluationStore? store = null,
        Func<DateTimeOffset>? clock = null)
    {
        _retriever = retriever ?? new InitiativeCandidateRetriever();
        _estimator = estimator ?? new TokenEstimator();
        _budget = budget ?? new TokenBudgetOptions();
        _contextBuilder = contextBuilder ?? new InitiativeContextBuilder(estimator: _estimator, budget: _budget);
        _validator = validator ?? new AnalysisEvidenceValidator();
        _policyValidator = policyValidator ?? new PolicyComplianceValidator();
        _gate = gate ?? new OutboundRequestGate();
        _metrics = metrics ?? new LiveEvaluationMetricCalculator();
        _store = store ?? new LocalLiveEvaluationStore();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<LiveEvaluationRun> RunAsync(
        LiveEvaluationPlan plan,
        string suitePath,
        ProjectMemorySyncResult memory,
        string providerName,
        Func<LiveEvaluationCase, int, IReasoningProvider> providerFactory,
        string interpretationModel,
        string reasoningModel,
        string interpretationEffort,
        string analysisEffort,
        LivePricingCatalog? pricing = null,
        CancellationToken cancellationToken = default) => await RunAsync(
        plan,
        suitePath,
        memory,
        ReviewedConceptResolutionResult.Absent,
        providerName,
        providerFactory,
        interpretationModel,
        reasoningModel,
        interpretationEffort,
        analysisEffort,
        pricing,
        cancellationToken);

    public async Task<LiveEvaluationRun> RunAsync(
        LiveEvaluationPlan plan,
        string suitePath,
        ProjectMemorySyncResult memory,
        ReviewedConceptResolutionResult reviewedConcepts,
        string providerName,
        Func<LiveEvaluationCase, int, IReasoningProvider> providerFactory,
        string interpretationModel,
        string reasoningModel,
        string interpretationEffort,
        string analysisEffort,
        LivePricingCatalog? pricing = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(reviewedConcepts);
        ArgumentNullException.ThrowIfNull(providerFactory);
        var started = _clock().ToUniversalTime();
        var snapshot = memory.SourceSnapshot;
        var branch = snapshot.Git.Branch ?? "(no branch)";
        var runId = CreateRunId(started, snapshot.Repository.Id, branch, snapshot.Git.HeadCommit,
            plan.Suite.Id, providerName, interpretationModel, reasoningModel, plan.Runs);
        var directory = _store.GetRunDirectory(snapshot.Repository.Id, runId);
        var results = new List<LiveEvaluationCaseResult>();
        var run = CreateRun(results);

        foreach (var item in plan.Cases)
        for (var runNumber = 1; runNumber <= plan.Runs; runNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await EvaluateCaseAsync(item, runNumber, suitePath, memory,
                reviewedConcepts,
                providerName, providerFactory, interpretationModel, reasoningModel,
                interpretationEffort, analysisEffort, cancellationToken);
            results.Add(result);
            run = await _store.SaveAsync(CreateRun(results), cancellationToken);
        }
        return run;

        LiveEvaluationRun CreateRun(IReadOnlyList<LiveEvaluationCaseResult> current)
        {
            Func<ReasoningCallUsage, decimal?>? estimateCost = pricing is null ? null : pricing.Estimate;
            var aggregate = LiveEvaluationMetricCalculator.Aggregate(current, estimateCost);
            return new LiveEvaluationRun(
                LocalLiveEvaluationStore.CurrentResultSchemaVersion,
                runId,
                started,
                plan.Suite.Id,
                plan.Suite.LiveEvaluationSchemaVersion,
                snapshot.Repository.Id,
                snapshot.Repository.Name,
                branch,
                snapshot.Git.HeadCommit,
                providerName,
                interpretationModel,
                reasoningModel,
                interpretationEffort,
                analysisEffort,
                plan.Runs,
                plan.ExpectedLogicalCalls,
                current.ToArray(),
                aggregate,
                LiveConsistencyCalculator.Calculate(current),
                directory,
                Path.Combine(directory, "summary.json"),
                Path.Combine(directory, "review.md"),
                reviewedConcepts.Status,
                reviewedConcepts.CatalogFingerprint,
                reviewedConcepts.Profiles.Count);
        }
    }

    public static string CreateRunId(
        DateTimeOffset timestamp,
        string repositoryId,
        string branch,
        string? commit,
        string suiteId,
        string provider,
        string interpretationModel,
        string reasoningModel,
        int runs)
    {
        var stamp = timestamp.ToUniversalTime().ToString("yyyyMMddTHHmmssfffZ");
        var identity = string.Join('\n', repositoryId, branch, commit ?? "n/a", suiteId,
            provider, interpretationModel, reasoningModel, runs.ToString(System.Globalization.CultureInfo.InvariantCulture), stamp);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant()[..12];
        return $"{stamp}--{hash}";
    }

    private async Task<LiveEvaluationCaseResult> EvaluateCaseAsync(
        LiveEvaluationCase item,
        int runNumber,
        string suitePath,
        ProjectMemorySyncResult memory,
        ReviewedConceptResolutionResult reviewedConcepts,
        string providerName,
        Func<LiveEvaluationCase, int, IReasoningProvider> providerFactory,
        string interpretationModel,
        string reasoningModel,
        string interpretationEffort,
        string analysisEffort,
        CancellationToken cancellationToken)
    {
        var suiteRoot = Path.GetDirectoryName(Path.GetFullPath(suitePath))!;
        var initiativePath = LiveEvaluationSuiteSerializer.ResolveWithin(suiteRoot, item.InitiativePath);
        var initiativeFileName = Path.GetFileName(initiativePath);
        var initiative = string.Empty;
        var initiativeHash = string.Empty;
        InitiativeUnderstanding? understanding = null;
        LiveUnderstandingMetrics? understandingMetrics = null;
        CandidateRetrievalResult? retrieval = null;
        LiveRetrievalComparison? retrievalComparison = null;
        LiveContextMetrics? contextMetrics = null;
        InitiativeAnalysis? analysis = null;
        IReadOnlyList<GovernedRecommendation> recommendations = [];
        PolicyOutcome? policyOutcome = null;
        LiveCall2Metrics? call2Metrics = null;
        LivePolicyMetrics? policyMetrics = null;
        var usage = new List<ReasoningCallUsage>();
        var security = new List<OutboundValidationResult>();
        var outboundAssessments = new List<OutboundPolicyAssessment>();
        var failedEstimate = 0;
        try
        {
            initiative = await File.ReadAllTextAsync(initiativePath, cancellationToken);
            initiativeHash = LocalInitiativeAnalysisStore.CreateContentHash(initiative);
            var call1Estimate = _estimator.Estimate(initiative) + _estimator.Estimate(InitiativeAnalysisPrompts.Understanding);
            if (call1Estimate > _budget.MaximumInitiativeInputTokens)
                throw new InvalidDataException("Live initiative exceeds the configured input limit.");
            failedEstimate = call1Estimate;
            var call1Request = new ReasoningRequest(
                ReasoningStage.InitiativeUnderstanding,
                interpretationModel,
                InitiativeAnalysisPrompts.Understanding,
                initiative,
                _budget.InitiativeOutputTokens,
                call1Estimate);
            var approvedCall1 = Approve(call1Request);
            var provider = providerFactory(item, runNumber);
            var call1 = await InvokeAsync<InitiativeUnderstanding>(
                provider,
                approvedCall1,
                cancellationToken);
            usage.Add(call1.Usage);
            understanding = call1.Value;
            understandingMetrics = _metrics.EvaluateUnderstanding(item, understanding, memory.SourceSnapshot);

            var goldenRetrieval = _retriever.Retrieve(
                item.GoldenUnderstanding,
                memory.Manifest,
                memory.SourceSnapshot,
                reviewedConcepts.Profiles);
            retrieval = _retriever.Retrieve(
                understanding,
                memory.Manifest,
                memory.SourceSnapshot,
                reviewedConcepts.Profiles);
            retrievalComparison = LiveEvaluationMetricCalculator.CompareRetrieval(
                _metrics.EvaluateRetrieval(goldenRetrieval, item.RepositoryExpectations),
                _metrics.EvaluateRetrieval(retrieval, item.RepositoryExpectations));
            var context = await _contextBuilder.BuildAsync(understanding, retrieval, memory, cancellationToken);
            contextMetrics = new LiveContextMetrics(
                context.EstimatedTokens,
                context.Segments.Count(value => value.Kind == ContextSegmentKind.ProjectNote),
                context.Segments.Count(value => value.Kind == ContextSegmentKind.ComponentNote),
                retrieval.Relations.Count,
                retrieval.Components.Count,
                context.PrunedNotePaths.Count);
            var call2Estimate = context.EstimatedTokens + _estimator.Estimate(InitiativeAnalysisPrompts.ArchitectureAnalysis);
            if (call2Estimate > _budget.MaximumReasoningInputTokens)
                throw new InvalidDataException("Live reasoning context exceeds the configured input limit.");
            failedEstimate = call2Estimate;
            var call2Request = new ReasoningRequest(
                ReasoningStage.ArchitectureAnalysis,
                reasoningModel,
                InitiativeAnalysisPrompts.ArchitectureAnalysis,
                context.Content,
                _budget.ReasoningOutputTokens,
                call2Estimate);
            var approvedCall2 = Approve(call2Request, context);
            var call2 = await InvokeAsync<InitiativeAnalysis>(
                provider,
                approvedCall2,
                cancellationToken);
            usage.Add(call2.Usage);
            analysis = call2.Value;
            var validated = _validator.Validate(analysis, memory.SourceSnapshot);
            var governance = _policyValidator.Evaluate(validated);
            recommendations = governance.Recommendations;
            policyOutcome = governance.Outcome;
            call2Metrics = _metrics.EvaluateAnalysis(item, analysis, recommendations, memory.SourceSnapshot);
            policyMetrics = LiveEvaluationMetricCalculator.EvaluatePolicy(item.PolicyExpectations, governance);
            return Result(LiveEvaluationExecutionStatus.Succeeded, null, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OutboundSecurityException exception)
        {
            return Result(
                LiveEvaluationExecutionStatus.SecurityBlocked,
                exception.Code,
                exception.Message);
        }
        catch (ReasoningProviderException exception)
        {
            usage.Add(FailedUsage(exception));
            return Result(
                exception.StructuredOutputFailure
                    ? LiveEvaluationExecutionStatus.StructuredOutputFailure
                    : LiveEvaluationExecutionStatus.ProviderFailure,
                exception.StructuredOutputFailure ? "StructuredOutputFailure" : "ProviderFailure",
                exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or ArgumentException)
        {
            return Result(LiveEvaluationExecutionStatus.ExecutionFailed, "ExecutionFailed", exception.Message);
        }

        ReasoningCallUsage FailedUsage(ReasoningProviderException exception) => new(
            exception.Stage,
            providerName,
            exception.Stage == ReasoningStage.InitiativeUnderstanding ? interpretationModel : reasoningModel,
            failedEstimate,
            exception.ActualInputTokens,
            exception.CachedInputTokens,
            exception.ActualOutputTokens,
            exception.DurationMilliseconds,
            exception.Retries,
            exception.Stage == ReasoningStage.InitiativeUnderstanding ? interpretationEffort : analysisEffort,
            exception.ReasoningTokens);

        LiveEvaluationCaseResult Result(LiveEvaluationExecutionStatus status, string? category, string? error) => new(
            item.Id,
            runNumber,
            status,
            initiativeFileName,
            initiativeHash,
            understanding,
            understandingMetrics,
            retrieval,
            retrievalComparison,
            contextMetrics,
            analysis,
            recommendations,
            policyOutcome,
            call2Metrics,
            policyMetrics,
            usage.ToArray(),
            security.ToArray(),
            category,
            LocalLiveEvaluationStore.Redact(error),
            new LiveHumanReview(null, null, null, null, null, null, null),
            outboundAssessments.ToArray());

        ApprovedReasoningRequest Approve(ReasoningRequest request, InitiativeContext? context = null)
        {
            try
            {
                var approved = _gate.ApproveExact(request, context);
                outboundAssessments.Add(approved.Assessment);
                security.Add(ToLegacyValidation(approved.Assessment));
                return approved;
            }
            catch (OutboundSecurityException exception)
            {
                outboundAssessments.Add(exception.Assessment);
                security.Add(ToLegacyValidation(exception.Assessment));
                throw;
            }
        }
    }

    private static OutboundValidationResult ToLegacyValidation(OutboundPolicyAssessment assessment)
    {
        var blocked = assessment.Results
            .Where(result => result.Outcome == OutboundPolicyOutcome.Blocked)
            .ToArray();
        return new OutboundValidationResult(
            assessment.IsAllowed,
            Count(PolicyContentScope.SourceBodies),
            Count(PolicyContentScope.Secrets),
            Count(PolicyContentScope.AbsoluteLocalPaths),
            Count(PolicyContentScope.RawSnapshot),
            assessment.Diagnostics.ToArray());

        int Count(PolicyContentScope category) => blocked
            .Where(result => result.Category == category)
            .Sum(result => result.FindingCount);
    }

    private static async Task<ReasoningResult<T>> InvokeAsync<T>(
        IReasoningProvider provider,
        ApprovedReasoningRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await provider.GenerateStructuredAsync<T>(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ReasoningProviderException)
        {
            throw;
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new ReasoningProviderException(request.Request.Stage, true, 0, 0,
                $"Structured output failed validation. {exception.Message}");
        }
        catch (Exception exception)
        {
            throw new ReasoningProviderException(request.Request.Stage, false, 0, 0,
                $"Provider execution failed. {exception.Message}");
        }
    }
}
