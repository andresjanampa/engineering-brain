using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class DeterministicInitiativeInterpreter
{
    private readonly InitiativeTermNormalizer _normalizer = new();

    public InitiativeUnderstanding Interpret(string initiative)
    {
        if (string.IsNullOrWhiteSpace(initiative))
        {
            throw new ArgumentException("Initiative file is empty.", nameof(initiative));
        }

        var terms = _normalizer.Tokenize(initiative);
        return new InitiativeUnderstanding(
            "Deterministic preview interpretation derived from normalized initiative terms.",
            [], [], [], [], [], terms, terms, [],
            ["Preview does not infer requirements that require model reasoning."]);
    }
}

public sealed class RemoteContextPreviewService
{
    private readonly DeterministicInitiativeInterpreter _interpreter;
    private readonly InitiativeCandidateRetriever _retriever;
    private readonly InitiativeContextBuilder _contextBuilder;
    private readonly OutboundRequestGate _gate;
    private readonly TokenEstimator _estimator;
    private readonly TokenBudgetOptions _budget;
    private readonly RemoteContextPreviewStore _store;

    public RemoteContextPreviewService(
        DeterministicInitiativeInterpreter? interpreter = null,
        InitiativeCandidateRetriever? retriever = null,
        InitiativeContextBuilder? contextBuilder = null,
        OutboundRequestGate? gate = null,
        TokenEstimator? estimator = null,
        TokenBudgetOptions? budget = null,
        RemoteContextPreviewStore? store = null)
    {
        _interpreter = interpreter ?? new DeterministicInitiativeInterpreter();
        _retriever = retriever ?? new InitiativeCandidateRetriever();
        _estimator = estimator ?? new TokenEstimator();
        _budget = budget ?? new TokenBudgetOptions();
        _contextBuilder = contextBuilder ?? new InitiativeContextBuilder(estimator: _estimator, budget: _budget);
        _gate = gate ?? new OutboundRequestGate();
        _store = store ?? new RemoteContextPreviewStore();
    }

    public async Task<RemoteContextPreview> CreateAsync(
        string initiativeFileName,
        string initiative,
        ProjectMemorySyncResult memory,
        string interpretationModel,
        string reasoningModel,
        string interpretationEffort,
        string analysisEffort,
        CancellationToken cancellationToken = default) => (await PrepareAsync(
        initiativeFileName,
        initiative,
        memory,
        ReviewedConceptResolutionResult.Absent,
        interpretationModel,
        reasoningModel,
        interpretationEffort,
        analysisEffort,
        cancellationToken)).Preview;

    public async Task<RemoteContextPreview> CreateAsync(
        string initiativeFileName,
        string initiative,
        ProjectMemorySyncResult memory,
        ReviewedConceptResolutionResult reviewedConcepts,
        string interpretationModel,
        string reasoningModel,
        string interpretationEffort,
        string analysisEffort,
        CancellationToken cancellationToken = default) => (await PrepareAsync(
        initiativeFileName,
        initiative,
        memory,
        reviewedConcepts,
        interpretationModel,
        reasoningModel,
        interpretationEffort,
        analysisEffort,
        cancellationToken)).Preview;

    public async Task<RemoteAnalysisPreparation> PrepareAsync(
        string initiativeFileName,
        string initiative,
        ProjectMemorySyncResult memory,
        ReviewedConceptResolutionResult reviewedConcepts,
        string interpretationModel,
        string reasoningModel,
        string interpretationEffort,
        string analysisEffort,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reviewedConcepts);
        var call1Tokens = _estimator.Estimate(initiative) + _estimator.Estimate(InitiativeAnalysisPrompts.Understanding);
        var call1Request = new ReasoningRequest(
            ReasoningStage.InitiativeUnderstanding,
            interpretationModel,
            InitiativeAnalysisPrompts.Understanding,
            initiative,
            _budget.InitiativeOutputTokens,
            call1Tokens);
        var approvedCall1 = _gate.ApproveExact(call1Request);
        var understanding = _interpreter.Interpret(initiative);
        var retrieval = _retriever.Retrieve(
            understanding,
            memory.Manifest,
            memory.SourceSnapshot,
            reviewedConcepts.Profiles);
        var context = await _contextBuilder.BuildAsync(understanding, retrieval, memory, cancellationToken);
        var call2Tokens = context.EstimatedTokens + _estimator.Estimate(InitiativeAnalysisPrompts.ArchitectureAnalysis);
        var call2Request = new ReasoningRequest(
            ReasoningStage.ArchitectureAnalysis,
            reasoningModel,
            InitiativeAnalysisPrompts.ArchitectureAnalysis,
            context.Content,
            _budget.ReasoningOutputTokens,
            call2Tokens);
        var call2Assessment = _gate.AssessProjected(call2Request, context);
        var security = ToLegacySummary(approvedCall1.Assessment, call2Assessment);
        var projectNotes = context.Segments.Where(segment => segment.Kind == ContextSegmentKind.ProjectNote).ToArray();
        var componentNotes = context.Segments.Where(segment => segment.Kind == ContextSegmentKind.ComponentNote).ToArray();
        var result = new RemoteContextPreview(
            1,
            Path.GetFileName(initiativeFileName),
            memory.SourceSnapshot.Repository.Id,
            memory.SourceSnapshot.Git.Branch ?? "(no branch)",
            interpretationModel,
            reasoningModel,
            interpretationEffort,
            analysisEffort,
            new PreviewCall1Manifest(LocalInitiativeAnalysisStore.CreateContentHash(initiative), initiative.Length, call1Tokens, "Deterministic"),
            new PreviewCall2Manifest(
                retrieval.Projects.Select(item => item.ProjectId).ToArray(),
                retrieval.Components.Select(item => item.EntityId).ToArray(),
                context.IncludedNotePaths,
                retrieval.Relations.Count,
                projectNotes.Length,
                componentNotes.Length,
                call2Tokens,
                false),
            security,
            _budget.MaximumReasoningInputTokens,
            call1Tokens <= _budget.MaximumInitiativeInputTokens && call2Tokens <= _budget.MaximumReasoningInputTokens,
            string.Empty,
            retrieval,
            approvedCall1.Assessment,
            call2Assessment);
        var path = await _store.SaveAsync(result, cancellationToken);
        return new RemoteAnalysisPreparation(result with { ManifestPath = path }, approvedCall1);
    }

    private static OutboundValidationResult ToLegacySummary(params OutboundPolicyAssessment[] assessments)
    {
        var results = assessments.SelectMany(assessment => assessment.Results).ToArray();
        var diagnostics = assessments.SelectMany(assessment => assessment.Diagnostics)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new OutboundValidationResult(
            assessments.All(assessment => assessment.IsAllowed),
            Count(PolicyContentScope.SourceBodies),
            Count(PolicyContentScope.Secrets),
            Count(PolicyContentScope.AbsoluteLocalPaths),
            Count(PolicyContentScope.RawSnapshot),
            diagnostics);

        int Count(PolicyContentScope category) => results
            .Where(result => result.Category == category && result.Outcome == OutboundPolicyOutcome.Blocked)
            .Sum(result => result.FindingCount);
    }
}

public sealed record RemoteAnalysisPreparation(
    RemoteContextPreview Preview,
    ApprovedReasoningRequest ApprovedCall1);

public sealed record PersistedRemoteContextPreview(
    int PreviewSchemaVersion,
    string InitiativeFileName,
    string RepositoryId,
    string Branch,
    string InterpretationModel,
    string ReasoningModel,
    string InterpretationReasoningEffort,
    string AnalysisReasoningEffort,
    PreviewCall1Manifest Call1,
    PreviewCall2Manifest Call2,
    OutboundValidationResult Security,
    int HardTokenLimit,
    bool WithinBudget);

public sealed class RemoteContextPreviewStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();
    private readonly string _dataRoot;
    public RemoteContextPreviewStore(string? dataRoot = null) => _dataRoot = dataRoot ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".engineering-brain");

    public async Task<string> SaveAsync(RemoteContextPreview preview, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(_dataRoot, "repositories", KnowledgeIdentity.CreateRepositoryKey(preview.RepositoryId),
            "previews", "branches", KnowledgeIdentity.CreateBranchKey(preview.Branch));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{preview.Call1.InitiativeHash}.json");
        var persisted = new PersistedRemoteContextPreview(preview.PreviewSchemaVersion, preview.InitiativeFileName,
            preview.RepositoryId, preview.Branch, preview.InterpretationModel, preview.ReasoningModel,
            preview.InterpretationReasoningEffort, preview.AnalysisReasoningEffort, preview.Call1, preview.Call2,
            preview.Security, preview.HardTokenLimit, preview.WithinBudget);
        var json = JsonSerializer.Serialize(persisted, JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        await File.WriteAllTextAsync(path, json, new UTF8Encoding(false), cancellationToken);
        return path;
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
