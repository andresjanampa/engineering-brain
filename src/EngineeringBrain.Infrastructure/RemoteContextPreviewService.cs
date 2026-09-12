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
    private readonly OutboundContextGuard _guard;
    private readonly TokenEstimator _estimator;
    private readonly TokenBudgetOptions _budget;
    private readonly RemoteContextPreviewStore _store;

    public RemoteContextPreviewService(
        DeterministicInitiativeInterpreter? interpreter = null,
        InitiativeCandidateRetriever? retriever = null,
        InitiativeContextBuilder? contextBuilder = null,
        OutboundContextGuard? guard = null,
        TokenEstimator? estimator = null,
        TokenBudgetOptions? budget = null,
        RemoteContextPreviewStore? store = null)
    {
        _interpreter = interpreter ?? new DeterministicInitiativeInterpreter();
        _retriever = retriever ?? new InitiativeCandidateRetriever();
        _estimator = estimator ?? new TokenEstimator();
        _budget = budget ?? new TokenBudgetOptions();
        _contextBuilder = contextBuilder ?? new InitiativeContextBuilder(estimator: _estimator, budget: _budget);
        _guard = guard ?? new OutboundContextGuard();
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
        CancellationToken cancellationToken = default) => await CreateAsync(
        initiativeFileName,
        initiative,
        memory,
        ReviewedConceptResolutionResult.Absent,
        interpretationModel,
        reasoningModel,
        interpretationEffort,
        analysisEffort,
        cancellationToken);

    public async Task<RemoteContextPreview> CreateAsync(
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
        var call1Security = _guard.ValidateInitiative(initiative);
        _guard.ThrowIfInvalid(call1Security);
        var understanding = _interpreter.Interpret(initiative);
        var retrieval = _retriever.Retrieve(
            understanding,
            memory.Manifest,
            memory.SourceSnapshot,
            reviewedConcepts.Profiles);
        var context = await _contextBuilder.BuildAsync(understanding, retrieval, memory, cancellationToken);
        var call2Security = _guard.Validate(context);
        _guard.ThrowIfInvalid(call2Security);
        var security = Combine(call1Security, call2Security);
        var call1Tokens = _estimator.Estimate(initiative) + _estimator.Estimate(InitiativeAnalysisPrompts.Understanding);
        var call2Tokens = context.EstimatedTokens + _estimator.Estimate(InitiativeAnalysisPrompts.ArchitectureAnalysis);
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
            retrieval);
        var path = await _store.SaveAsync(result, cancellationToken);
        return result with { ManifestPath = path };
    }

    private static OutboundValidationResult Combine(OutboundValidationResult first, OutboundValidationResult second)
    {
        var codes = first.DiagnosticCodes.Concat(second.DiagnosticCodes).Distinct(StringComparer.Ordinal).ToArray();
        return new OutboundValidationResult(codes.Length == 0,
            first.SourceBodyFindings + second.SourceBodyFindings,
            first.SecretFindings + second.SecretFindings,
            first.AbsolutePathFindings + second.AbsolutePathFindings,
            first.RawSnapshotFindings + second.RawSnapshotFindings,
            codes);
    }
}

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
