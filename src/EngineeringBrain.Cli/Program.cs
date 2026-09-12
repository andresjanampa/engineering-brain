using EngineeringBrain.Analyzers.CSharp;
using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

return await BrainCli.RunAsync(args);

internal static class BrainCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            WriteUsage();
            return 0;
        }

        var isScan = args[0].Equals("scan", StringComparison.OrdinalIgnoreCase);
        var isMemorySync = args.Length >= 2
            && args[0].Equals("memory", StringComparison.OrdinalIgnoreCase)
            && args[1].Equals("sync", StringComparison.OrdinalIgnoreCase);
        var isAnalyze = args[0].Equals("analyze", StringComparison.OrdinalIgnoreCase);
        if (isAnalyze)
        {
            return await RunAnalyzeAsync(args);
        }
        if (args[0].Equals("eval", StringComparison.OrdinalIgnoreCase))
        {
            return await RunEvaluationAsync(args);
        }
        if (args[0].Equals("eval-live", StringComparison.OrdinalIgnoreCase))
        {
            return await RunLiveEvaluationAsync(args);
        }

        var maximumArguments = isMemorySync ? 3 : 2;
        if ((!isScan && !isMemorySync) || args.Length > maximumArguments)
        {
            Console.Error.WriteLine("Invalid command or too many arguments.");
            WriteUsage();
            return 2;
        }

        var path = isMemorySync
            ? args.Length == 3 ? args[2] : Environment.CurrentDirectory
            : args.Length == 2 ? args[1] : Environment.CurrentDirectory;
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            IRepositoryScanner scanner = new RepositoryScanner();
            var git = new GitInfoProvider();
            IReadOnlyList<ILanguageAnalyzer> analyzers = [new CSharpAnalyzer()];
            IRepositorySnapshotStore store = new LocalRepositorySnapshotStore();
            var engine = new RepositoryAnalysisEngine(scanner, git, analyzers, store, git);
            var result = await engine.ScanAsync(path, cancellation.Token);

            if (isMemorySync)
            {
                var memory = await new ProjectMemoryService().SyncAsync(
                    result.Snapshot,
                    cancellation.Token);
                WriteMemorySummary(memory);
            }
            else
            {
                WriteSummary(result.Snapshot, result.SnapshotPath);
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Command cancelled.");
            return 130;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Command failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunAnalyzeAsync(string[] args)
    {
        if (!TryParseAnalyze(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            WriteUsage();
            return 2;
        }

        using var cancellation = CreateCancellationSource();
        try
        {
            if (!File.Exists(options.InitiativePath))
            {
                throw new FileNotFoundException("Initiative file was not found.", options.InitiativePath);
            }

            var initiative = await File.ReadAllTextAsync(options.InitiativePath, cancellation.Token);
            var scan = await AnalyzeRepositoryAsync(options.RepositoryPath, cancellation.Token);
            var memory = await new ProjectMemoryService().SyncAsync(scan.Snapshot, cancellation.Token);
            var reviewedConcepts = await ResolveReviewedConceptsAsync(memory, cancellation.Token);
            WriteReviewedConceptDiagnostics(reviewedConcepts);
            var providerOptions = OpenAIReasoningProviderOptions.FromEnvironment() with
            {
                InterpretationReasoningEffort = options.InterpretationEffort,
                AnalysisReasoningEffort = options.AnalysisEffort
            };
            var interpretationEffort = providerOptions.GetReasoningEffort(ReasoningStage.InitiativeUnderstanding);
            var analysisEffort = providerOptions.GetReasoningEffort(ReasoningStage.ArchitectureAnalysis);
            var preview = await new RemoteContextPreviewService().CreateAsync(
                options.InitiativePath, initiative, memory, reviewedConcepts,
                options.InterpretationModel, options.ReasoningModel,
                interpretationEffort, analysisEffort, cancellation.Token);
            WritePreview(preview);
            if (options.Preview)
            {
                return preview.WithinBudget && preview.Security.IsValid ? 0 : 3;
            }

            var apiKey = RemoteReasoningAuthorization.RequireOpenAIApiKey(options.AllowRemote);
            Console.WriteLine();
            Console.WriteLine("Remote reasoning: explicitly authorized");
            Console.WriteLine("Call 1 sends only the initiative text. Call 2 sends selected Project Memory and evidence metadata.");
            Console.WriteLine("Source bodies, repository files, environment files, credentials, and the full snapshot are not sent.");
            IReasoningProvider provider = new OpenAIReasoningProvider(
                apiKey,
                providerOptions);
            var service = new InitiativeAnalysisService(provider);
            var result = await service.AnalyzeAsync(
                new InitiativeAnalysisRequest(
                    options.InitiativePath,
                    initiative,
                    memory,
                    options.InterpretationModel,
                    options.ReasoningModel),
                reviewedConcepts,
                cancellation.Token);
            WriteAnalysis(result);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Command cancelled.");
            return 130;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException
            or InvalidDataException)
        {
            Console.Error.WriteLine($"Command failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<ReviewedConceptResolutionResult> ResolveReviewedConceptsAsync(
        ProjectMemorySyncResult memory,
        CancellationToken cancellationToken)
    {
        var load = await new LocalReviewedConceptStore().LoadAsync(memory.Location, cancellationToken);
        if (load.Status == ReviewedConceptLoadStatus.Absent)
        {
            return ReviewedConceptResolutionResult.Absent;
        }

        if (load.Status == ReviewedConceptLoadStatus.Invalid)
        {
            return new ReviewedConceptResolutionResult(
                ReviewedConceptResolutionStatus.Invalid,
                load.ContentHash,
                [],
                load.Diagnostics);
        }

        var evidence = ReviewedConceptEvidenceContext.FromMemory(memory);
        var validation = new ReviewedConceptValidator().Validate(load.Catalog!, evidence);
        return new ReviewedConceptResolver().Resolve(load, validation, evidence);
    }

    private static void WriteReviewedConceptDiagnostics(ReviewedConceptResolutionResult result)
    {
        foreach (var diagnostic in result.Diagnostics)
        {
            var identity = string.Join(' ', new[] { diagnostic.ConceptId, diagnostic.EntityId }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            var message = diagnostic.Message.Length <= 240
                ? diagnostic.Message
                : diagnostic.Message[..240];
            Console.Error.WriteLine(
                $"Reviewed concept {diagnostic.Severity} {diagnostic.Code}"
                + (identity.Length == 0 ? string.Empty : $" [{identity}]")
                + $": {message}");
        }
    }

    private static CancellationTokenSource CreateCancellationSource()
    {
        var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        return cancellation;
    }

    private static bool TryParseAnalyze(
        string[] args,
        out AnalyzeOptions options,
        out string error)
    {
        options = null!;
        error = string.Empty;
        if (args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            error = "Analyze requires an initiative Markdown file.";
            return false;
        }

        var initiativePath = Path.GetFullPath(args[1]);
        var repositoryPath = Environment.CurrentDirectory;
        var allowRemote = false;
        var preview = false;
        var interpretationModel = Environment.GetEnvironmentVariable("ENGINEERING_BRAIN_INTERPRETATION_MODEL")
            ?? "gpt-5.6-luna";
        var reasoningModel = Environment.GetEnvironmentVariable("ENGINEERING_BRAIN_REASONING_MODEL")
            ?? "gpt-5.6-sol";
        var defaults = OpenAIReasoningProviderOptions.FromEnvironment();
        var interpretationEffort = defaults.InterpretationReasoningEffort;
        var analysisEffort = defaults.AnalysisReasoningEffort;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--allow-remote":
                    allowRemote = true;
                    break;
                case "--preview":
                    preview = true;
                    break;
                case "--repo" when index + 1 < args.Length:
                    repositoryPath = Path.GetFullPath(args[++index]);
                    break;
                case "--interpretation-model" when index + 1 < args.Length:
                    interpretationModel = args[++index];
                    break;
                case "--reasoning-model" when index + 1 < args.Length:
                    reasoningModel = args[++index];
                    break;
                case "--interpretation-effort" when index + 1 < args.Length:
                    interpretationEffort = args[++index];
                    break;
                case "--analysis-effort" when index + 1 < args.Length:
                    analysisEffort = args[++index];
                    break;
                default:
                    error = $"Unknown or incomplete analyze option: {args[index]}";
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(interpretationModel) || string.IsNullOrWhiteSpace(reasoningModel))
        {
            error = "Reasoning model names cannot be empty.";
            return false;
        }

        options = new AnalyzeOptions(
            initiativePath,
            repositoryPath,
            allowRemote,
            preview,
            interpretationModel,
            reasoningModel,
            interpretationEffort,
            analysisEffort);
        return true;
    }

    private static async Task<int> RunEvaluationAsync(string[] args)
    {
        var updateBaseline = args.Contains("--update-baseline", StringComparer.OrdinalIgnoreCase);
        var positional = args.Skip(1).Where(value => !value.StartsWith("--", StringComparison.Ordinal)).ToArray();
        if (positional.Length > 1 || args.Skip(1).Any(value => value.StartsWith("--", StringComparison.Ordinal)
            && !value.Equals("--update-baseline", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("Usage: brain eval [repository-or-suite-path] [--update-baseline]");
            return 2;
        }

        using var cancellation = CreateCancellationSource();
        try
        {
            var input = Path.GetFullPath(positional.SingleOrDefault() ?? Environment.CurrentDirectory);
            var suitePath = File.Exists(input) ? input : Path.Combine(input, "evaluations", "suite.json");
            var repositoryPath = File.Exists(input)
                ? new RepositoryRootLocator().Locate(Path.GetDirectoryName(input)!)
                : input;
            var serializer = new EvaluationSuiteSerializer();
            var suite = await serializer.LoadAsync(suitePath, cancellation.Token);
            var scan = await AnalyzeRepositoryAsync(repositoryPath, cancellation.Token);
            var memory = await new ProjectMemoryService().SyncAsync(scan.Snapshot, cancellation.Token);
            var harness = new EvaluationHarness();
            var cases = await harness.EvaluateAsync(suite, suitePath, memory, cancellation.Token);
            var splits = EvaluationHarness.Grouped(cases);
            var aggregate = splits.All;
            var baselinePath = Path.Combine(Path.GetDirectoryName(suitePath)!, "baseline.json");
            var baseline = await EvaluationResultStore.LoadBaselineAsync(baselinePath, cancellation.Token);
            var expectedBaselineCases = baseline?.Splits is null ? splits.Tuning.Cases : splits.All.Cases;
            if (baseline is not null && (baseline.EvaluationSchemaVersion != suite.EvaluationSchemaVersion
                || !baseline.SuiteId.Equals(suite.Id, StringComparison.Ordinal)
                || baseline.CaseCount != expectedBaselineCases))
            {
                throw new InvalidDataException("Evaluation baseline is incompatible with the current suite schema, identity, or case count.");
            }
            var regressions = baseline is null || updateBaseline
                ? []
                : new EvaluationBaselineComparer().CompareGrouped(baseline, splits, cases);
            var result = new EvaluationRunResult(
                suite.EvaluationSchemaVersion, suite.Id, scan.Snapshot.Repository.Id,
                scan.Snapshot.Git.Branch ?? "(no branch)", scan.Snapshot.Analysis.AnalyzerVersion,
                EvaluationHarness.RetrievalVersion, DateTimeOffset.UtcNow, aggregate, splits,
                EvaluationHarness.Categories(cases), cases, regressions,
                updateBaseline ? "Updated explicitly" : baseline is null ? "Missing" : "Compared",
                string.Empty);
            if (updateBaseline)
            {
                var created = new EvaluationBaseline(result.EvaluationSchemaVersion, result.SuiteId,
                    result.AnalyzerVersion, result.RetrievalVersion, cases.Count, aggregate,
                    cases.Select(item => new EvaluationBaselineCase(item.Id, item.Retrieval.RecallAt10,
                        item.Retrieval.MeanReciprocalRank, item.Retrieval.ProjectMeanReciprocalRank,
                        item.Context.EstimatedCall2Tokens, item.Split)).ToArray(), splits);
                await EvaluationResultStore.SaveBaselineAsync(baselinePath, created, cancellation.Token);
            }
            var resultStore = new EvaluationResultStore();
            var resultPath = await resultStore.SaveResultAsync(result, cancellation.Token);
            result = result with { ResultPath = resultPath };
            WriteEvaluation(result, baselinePath);
            return result.Regressions.Count == 0 && aggregate.Passed == aggregate.Cases
                && aggregate.FabricatedEntitiesAccepted == 0 && aggregate.BudgetViolations == 0 ? 0 : 3;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Command cancelled.");
            return 130;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or InvalidDataException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"Evaluation failed: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunLiveEvaluationAsync(string[] args)
    {
        if (!TryParseLiveEvaluation(args, out var options, out var error))
        {
            Console.Error.WriteLine(error);
            WriteUsage();
            return 2;
        }

        using var cancellation = CreateCancellationSource();
        try
        {
            var suitePath = File.Exists(options.InputPath)
                ? options.InputPath
                : Path.Combine(options.InputPath, "evaluations", "live-suite.json");
            var repositoryPath = File.Exists(options.InputPath)
                ? new RepositoryRootLocator().Locate(Path.GetDirectoryName(options.InputPath)!)
                : options.InputPath;
            var suite = await new LiveEvaluationSuiteSerializer().LoadAsync(suitePath, cancellation.Token);
            var plan = LiveEvaluationPlanner.Create(suite, options.Runs, options.CaseIds);
            var providerOptions = OpenAIReasoningProviderOptions.FromEnvironment() with
            {
                InterpretationReasoningEffort = options.InterpretationEffort,
                AnalysisReasoningEffort = options.AnalysisEffort
            };
            var interpretationEffort = providerOptions.GetReasoningEffort(ReasoningStage.InitiativeUnderstanding);
            var analysisEffort = providerOptions.GetReasoningEffort(ReasoningStage.ArchitectureAnalysis);
            string? apiKey = null;
            if (!options.Preview)
                apiKey = LiveEvaluationAuthorization.Authorize(options.FakeProvider, options.AllowRemote);

            var scan = await AnalyzeRepositoryAsync(repositoryPath, cancellation.Token);
            var memory = await new ProjectMemoryService().SyncAsync(scan.Snapshot, cancellation.Token);
            WriteLivePreRun(plan, scan.Snapshot, options, providerOptions.MaximumRetries,
                interpretationEffort, analysisEffort);
            if (options.Preview) return 0;

            LivePricingCatalog? pricing = options.PricingPath is null
                ? null
                : await LivePricingCatalogLoader.LoadAsync(options.PricingPath, cancellation.Token);
            var providerName = options.FakeProvider ? "Fake" : "OpenAI";
            IReasoningProvider Factory(LiveEvaluationCase item, int runNumber) => options.FakeProvider
                ? new FakeLiveReasoningProvider(item, memory.SourceSnapshot, runNumber, interpretationEffort, analysisEffort)
                : new OpenAIReasoningProvider(apiKey!, providerOptions);
            var result = await new LiveEvaluationService().RunAsync(
                plan, suitePath, memory, providerName, Factory,
                options.InterpretationModel, options.ReasoningModel,
                interpretationEffort, analysisEffort, pricing, cancellation.Token);
            WriteLiveEvaluation(result);
            return result.Aggregate.CasesFailed == 0
                && result.Aggregate.FabricatedEntitiesAccepted == 0
                && result.Aggregate.SourceBodyOutbound == 0
                && result.Aggregate.SecretOutbound == 0
                && result.Aggregate.AbsolutePathOutbound == 0
                && result.Aggregate.RawSnapshotOutbound == 0
                && result.Aggregate.BlockedRecommendationEscapeCount == 0 ? 0 : 3;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Command cancelled.");
            return 130;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException or InvalidDataException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"Live evaluation failed: {exception.Message}");
            return 1;
        }
    }

    private static bool TryParseLiveEvaluation(
        string[] args,
        out LiveEvaluationOptions options,
        out string error)
    {
        var input = Environment.CurrentDirectory;
        var positionalSeen = false;
        var allowRemote = false;
        var fakeProvider = false;
        var preview = false;
        var runs = 1;
        var caseIds = new List<string>();
        string? pricingPath = null;
        var interpretationModel = Environment.GetEnvironmentVariable("ENGINEERING_BRAIN_INTERPRETATION_MODEL") ?? "gpt-5.6-luna";
        var reasoningModel = Environment.GetEnvironmentVariable("ENGINEERING_BRAIN_REASONING_MODEL") ?? "gpt-5.6-sol";
        var defaults = OpenAIReasoningProviderOptions.FromEnvironment();
        var interpretationEffort = defaults.InterpretationReasoningEffort;
        var analysisEffort = defaults.AnalysisReasoningEffort;
        error = string.Empty;
        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--allow-remote": allowRemote = true; break;
                case "--fake-provider": fakeProvider = true; break;
                case "--preview": preview = true; break;
                case "--runs" when index + 1 < args.Length && int.TryParse(args[++index], out runs): break;
                case "--case" when index + 1 < args.Length: caseIds.Add(args[++index]); break;
                case "--pricing" when index + 1 < args.Length: pricingPath = Path.GetFullPath(args[++index]); break;
                case "--interpretation-model" when index + 1 < args.Length: interpretationModel = args[++index]; break;
                case "--reasoning-model" when index + 1 < args.Length: reasoningModel = args[++index]; break;
                case "--interpretation-effort" when index + 1 < args.Length: interpretationEffort = args[++index]; break;
                case "--analysis-effort" when index + 1 < args.Length: analysisEffort = args[++index]; break;
                default:
                    if (!args[index].StartsWith("--", StringComparison.Ordinal) && !positionalSeen)
                    {
                        input = Path.GetFullPath(args[index]);
                        positionalSeen = true;
                        break;
                    }
                    error = $"Unknown, duplicate, or incomplete eval-live option: {args[index]}";
                    options = null!;
                    return false;
            }
        }
        if (preview && (allowRemote || fakeProvider))
        {
            error = "--preview cannot be combined with --allow-remote or --fake-provider.";
            options = null!;
            return false;
        }
        if (interpretationModel.Contains("astra", StringComparison.OrdinalIgnoreCase)
            || reasoningModel.Contains("astra", StringComparison.OrdinalIgnoreCase))
        {
            error = "Astra models are outside the live evaluation policy for this iteration.";
            options = null!;
            return false;
        }
        options = new LiveEvaluationOptions(input, allowRemote, fakeProvider, preview, runs, caseIds,
            pricingPath, interpretationModel, reasoningModel, interpretationEffort, analysisEffort);
        return true;
    }

    private static async Task<RepositoryAnalysisResult> AnalyzeRepositoryAsync(string path, CancellationToken cancellationToken)
    {
        IRepositoryScanner scanner = new RepositoryScanner();
        var git = new GitInfoProvider();
        IReadOnlyList<ILanguageAnalyzer> analyzers = [new CSharpAnalyzer()];
        IRepositorySnapshotStore snapshotStore = new LocalRepositorySnapshotStore();
        return await new RepositoryAnalysisEngine(scanner, git, analyzers, snapshotStore, git)
            .ScanAsync(path, cancellationToken);
    }

    private static void WritePreview(RemoteContextPreview preview)
    {
        Console.WriteLine("Engineering Brain");
        WriteSection("Remote Context Preview");
        Console.WriteLine($"Initiative file: {preview.InitiativeFileName}");
        Console.WriteLine($"Characters: {preview.Call1.CharacterCount}");
        Console.WriteLine($"Interpretation source: {preview.Call1.InterpretationSource}");

        WriteSection("CALL #1");
        Console.WriteLine("Would send: initiative text and structural instructions");
        Console.WriteLine("Would NOT send: repository, Project Memory, source, or snapshot");
        Console.WriteLine($"Model: {preview.InterpretationModel}");
        Console.WriteLine($"Reasoning effort: {preview.InterpretationReasoningEffort}");
        Console.WriteLine($"Estimated input tokens: {preview.Call1.EstimatedTokens}");

        WriteSection("CALL #2");
        Console.WriteLine($"Model: {preview.ReasoningModel}");
        Console.WriteLine($"Reasoning effort: {preview.AnalysisReasoningEffort}");
        Console.WriteLine($"Projects selected: {preview.Call2.SelectedProjectIds.Count}");
        Console.WriteLine($"Components selected: {preview.Call2.SelectedEntityIds.Count}");
        Console.WriteLine($"Relations selected: {preview.Call2.SelectedRelations}");
        Console.WriteLine($"Project notes: {preview.Call2.ProjectNotes}");
        Console.WriteLine($"Component notes: {preview.Call2.ComponentNotes}");
        Console.WriteLine($"Estimated input tokens: {preview.Call2.EstimatedTokens}");

        WriteSection("Security");
        Console.WriteLine($"Source bodies: {preview.Security.SourceBodyFindings}");
        Console.WriteLine($"Absolute paths: {preview.Security.AbsolutePathFindings}");
        Console.WriteLine($"Secrets detected: {preview.Security.SecretFindings}");
        Console.WriteLine($"Full snapshot embedded: {(preview.Security.RawSnapshotFindings == 0 ? "NO" : "YES")}");

        WriteSection("Budget");
        Console.WriteLine($"Estimated CALL #2: {preview.Call2.EstimatedTokens}");
        Console.WriteLine($"Hard limit: {preview.HardTokenLimit}");
        Console.WriteLine($"Utilization: {preview.Call2.EstimatedTokens * 100d / preview.HardTokenLimit:F1}%");
        Console.WriteLine($"Status: {(preview.WithinBudget ? "within budget" : "exceeded")}");
        Console.WriteLine($"Manifest: {preview.ManifestPath}");
    }

    private static void WriteLivePreRun(
        LiveEvaluationPlan plan,
        RepositorySnapshot snapshot,
        LiveEvaluationOptions options,
        int maximumRetries,
        string interpretationEffort,
        string analysisEffort)
    {
        Console.WriteLine("Engineering Brain Live Evaluation");
        WriteSection("Pre-run Summary");
        Console.WriteLine($"Cases: {plan.Cases.Count}");
        Console.WriteLine($"Runs per case: {plan.Runs}");
        Console.WriteLine($"Expected logical calls: {plan.ExpectedLogicalCalls}");
        Console.WriteLine($"Maximum provider attempts with retries: {plan.ExpectedLogicalCalls * (maximumRetries + 1)}");
        Console.WriteLine($"CALL #1: {options.InterpretationModel} / {interpretationEffort}");
        Console.WriteLine($"CALL #2: {options.ReasoningModel} / {analysisEffort}");
        Console.WriteLine($"Estimated hard maximum input: {plan.EstimatedMaximumInputTokens}");
        Console.WriteLine($"Remote authorization: {(options.Preview ? "preview only" : options.FakeProvider ? "fake provider; network disabled" : "enabled")}");
        Console.WriteLine($"Repository: {snapshot.Repository.Name}");
        Console.WriteLine($"Branch: {snapshot.Git.Branch ?? "(no branch)"}");
    }

    private static void WriteLiveEvaluation(LiveEvaluationRun result)
    {
        var aggregate = result.Aggregate;
        var retrieval = LiveEvaluationMetricCalculator.SummarizeRetrieval(result.Cases);
        WriteSection("Live Results");
        Console.WriteLine($"Run: {result.RunId}");
        Console.WriteLine($"Cases succeeded/failed: {aggregate.CasesSucceeded}/{aggregate.CasesFailed}");
        Console.WriteLine($"Security blocked: {aggregate.SecurityBlocked}; structured failures: {aggregate.StructuredOutputFailures}");
        Console.WriteLine($"CALL #1 capability hit rate: {aggregate.CapabilityHitRate:F3}");
        Console.WriteLine($"Unknown detection accuracy: {aggregate.UnknownDetectionAccuracy:F3}");
        Console.WriteLine($"Golden retrieval Recall@5/10/MRR: {FormatRetrieval(retrieval?.Golden)}");
        Console.WriteLine($"Live retrieval Recall@5/10/MRR: {FormatRetrieval(retrieval?.Actual)}");
        Console.WriteLine($"Retrieval delta Recall@5/10/MRR: {(retrieval is null ? "n/a/n/a/n/a" : $"{retrieval.RecallAt5Delta:F3}/{retrieval.RecallAt10Delta:F3}/{retrieval.MeanReciprocalRankDelta:F3}")}");
        Console.WriteLine($"CALL #2 decision hit rate: {aggregate.ExpectedDecisionHitRate:F3}");
        Console.WriteLine($"Evidence validation: {aggregate.EvidenceValidationRate:F3}; invalid: {aggregate.InvalidEvidence}; fabricated accepted: {aggregate.FabricatedEntitiesAccepted}");
        Console.WriteLine($"Analysis status accuracy: {aggregate.AnalysisStatusAccuracy:F3}");
        Console.WriteLine($"Policy activation/outcome accuracy: {aggregate.PolicyActivationAccuracy:F3}/{aggregate.PolicyOutcomeAccuracy:F3}; blocked recommendation escapes: {aggregate.BlockedRecommendationEscapeCount}");
        Console.WriteLine($"Context tokens average/median/max: {aggregate.AverageContextTokens:F1}/{aggregate.MedianContextTokens:F1}/{aggregate.MaximumContextTokens}");
        Console.WriteLine($"Usage calls/attempts/input/cached/output: {aggregate.Usage.LogicalCalls}/{aggregate.Usage.ProviderAttempts}/{aggregate.Usage.ActualInputTokens}/{aggregate.Usage.CachedInputTokens}/{aggregate.Usage.ActualOutputTokens}");
        Console.WriteLine($"Duration: {aggregate.Usage.DurationMilliseconds} ms; retries: {aggregate.Usage.Retries}; cost USD: {(aggregate.Usage.EstimatedCostUsd?.ToString("F6") ?? "n/a")}");
        Console.WriteLine($"Consistency status/decision/entity/capability/evidence: {result.Consistency.StatusAgreement:F3}/{result.Consistency.DecisionAgreement:F3}/{result.Consistency.EntityReferenceOverlap:F3}/{result.Consistency.CapabilityOverlap:F3}/{result.Consistency.EvidenceValidityAgreement:F3}");
        Console.WriteLine($"Outbound source/secrets/absolute/raw snapshot: {aggregate.SourceBodyOutbound}/{aggregate.SecretOutbound}/{aggregate.AbsolutePathOutbound}/{aggregate.RawSnapshotOutbound}");
        WriteSection("Artifacts");
        Console.WriteLine($"Summary: {result.SummaryPath}");
        Console.WriteLine($"Review: {result.ReviewPath}");
    }

    private static string FormatRetrieval(LiveRetrievalMetrics? metrics) => metrics is null
        ? "n/a/n/a/n/a"
        : $"{metrics.RecallAt5:F3}/{metrics.RecallAt10:F3}/{metrics.MeanReciprocalRank:F3}";

    private static void WriteEvaluation(EvaluationRunResult result, string baselinePath)
    {
        Console.WriteLine("Engineering Brain Evaluation");
        WriteSection("Suite");
        Console.WriteLine($"Cases: {result.Aggregate.Cases}");
        Console.WriteLine($"Passed: {result.Aggregate.Passed}");
        Console.WriteLine($"Regressions: {result.Regressions.Count}");
        Console.WriteLine($"Baseline: {result.BaselineStatus} ({baselinePath})");
        WriteAggregate("Tuning Metrics", result.Splits.Tuning);
        WriteAggregate("Holdout Metrics", result.Splits.Holdout);
        WriteAggregate("All Metrics", result.Splits.All);
        WriteSection("Per Category");
        foreach (var category in result.Categories)
            Console.WriteLine($"{category.Category}: cases={category.Cases}; entity cases={category.EntityRetrievalCases}; "
                + $"Recall@10={(category.EntityRetrievalCases == 0 ? "n/a" : category.RecallAt10.ToString("F3"))}; "
                + $"MRR={(category.EntityRetrievalCases == 0 ? "n/a" : category.MeanReciprocalRank.ToString("F3"))}; "
                + $"project MRR={(category.ProjectRetrievalCases == 0 ? "n/a" : category.ProjectMeanReciprocalRank.ToString("F3"))}");
        var failed = result.Cases.Where(item => !item.Passed).ToArray();
        if (failed.Length > 0)
        {
            WriteSection("Failed Cases");
            foreach (var item in failed)
                Console.WriteLine($"{item.Id}: {string.Join("; ", item.Diagnostics)}");
        }
        if (result.Regressions.Count > 0)
        {
            WriteSection("Regressions");
            foreach (var item in result.Regressions)
                Console.WriteLine($"{item.Metric}: {item.Baseline:F3} -> {item.Current:F3}. {item.Reason}");
        }
        WriteSection("Result");
        Console.WriteLine(result.ResultPath);
    }

    private static void WriteAggregate(string title, EvaluationAggregateMetrics metrics)
    {
        WriteSection(title);
        Console.WriteLine($"Cases: {metrics.Cases}; passed: {metrics.Passed}");
        Console.WriteLine($"Recall@5: {metrics.RecallAt5:F3}; Recall@10: {metrics.RecallAt10:F3}");
        Console.WriteLine($"Precision@5: {metrics.PrecisionAt5:F3}; Precision@10: {metrics.PrecisionAt10:F3}; MRR: {metrics.MeanReciprocalRank:F3}");
        Console.WriteLine($"Project Recall@3: {metrics.ProjectRecallAt3:F3}; Project MRR: {metrics.ProjectMeanReciprocalRank:F3}");
        Console.WriteLine($"Non-test TestCandidateRatio@5: {metrics.NonTestCaseTestCandidateRatioAt5:F3}; @10: {metrics.NonTestCaseTestCandidateRatioAt10:F3}");
        Console.WriteLine($"Test-relevant TestCandidateRatio@5: {metrics.TestRelevantCaseTestCandidateRatioAt5:F3}; @10: {metrics.TestRelevantCaseTestCandidateRatioAt10:F3}");
        Console.WriteLine($"Average candidates: {metrics.AverageCandidateCount:F1}; selected components: {metrics.AverageSelectedComponents:F1}");
        Console.WriteLine($"CALL #2 tokens average/median/max: {metrics.AverageCall2Tokens:F1}/{metrics.MedianCall2Tokens:F1}/{metrics.MaximumCall2Tokens}");
        Console.WriteLine($"Budget violations: {metrics.BudgetViolations}; evidence validation: {metrics.EvidenceValidationRate:F3}; invalid evidence: {metrics.InvalidEvidenceCount}");
        Console.WriteLine($"Fabricated entity accepted: {metrics.FabricatedEntitiesAccepted}; NeedsClarification expected/actual: {metrics.NeedsClarificationExpected}/{metrics.NeedsClarificationActual}");
    }

    private static void WriteAnalysis(InitiativeAnalysisResult result)
    {
        Console.WriteLine("Engineering Brain");
        WriteSection("Initiative Analysis");
        Console.WriteLine($"Repository: {result.Repository.Name}");
        Console.WriteLine($"Branch: {result.Git.Branch ?? "(no branch)"}");
        Console.WriteLine($"Commit: {result.Git.HeadCommit ?? "n/a"}");
        Console.WriteLine($"Working tree: {FormatWorkingTree(result.Git)}");
        Console.WriteLine($"Status: {result.Analysis.Status}");
        Console.WriteLine($"Policy outcome: {result.PolicyOutcome}");
        Console.WriteLine($"Summary: {result.Analysis.Summary}");

        WriteSection("Initiative Understanding");
        Console.WriteLine(result.Understanding.Summary);
        WriteValues("Capabilities", result.Understanding.TechnicalCapabilities);
        WriteValues("Unknowns", result.Understanding.Unknowns);

        WriteSection("Deterministic Retrieval");
        Console.WriteLine($"Projects considered: {result.Retrieval.ProjectsConsidered}");
        Console.WriteLine($"Projects selected: {result.Retrieval.Projects.Count}");
        Console.WriteLine($"Components considered: {result.Retrieval.ComponentsConsidered}");
        Console.WriteLine($"Components selected: {result.Retrieval.Components.Count}");
        foreach (var candidate in result.Retrieval.Components)
        {
            var reasons = string.Join(", ", candidate.MatchReasons.Select(reason => $"{reason.Signal}:{reason.MatchedValue} (+{reason.Points})"));
            Console.WriteLine($"- {candidate.FullName} | score {candidate.Score} | {reasons}");
        }

        WriteSection("Recommendations");
        foreach (var governed in result.Recommendations)
        {
            var recommendation = governed.ValidatedRecommendation;
            Console.WriteLine($"{recommendation.Recommendation.Decision}: {recommendation.Recommendation.Subject}");
            Console.WriteLine($"Evidence: {recommendation.ValidationStatus}; epistemic: {recommendation.Recommendation.EpistemicStatus}");
            Console.WriteLine($"Final disposition: {governed.Disposition}");
            Console.WriteLine($"Reason: {recommendation.Recommendation.Reason}");
            foreach (var policy in governed.PolicyResults)
            {
                Console.WriteLine(
                    $"- Policy: {policy.PolicyId} v{policy.PolicyVersion}; {policy.ComplianceStatus}; severity={policy.Severity}; source={policy.Source}");
            }
            foreach (var evidence in recommendation.ValidEvidence)
            {
                Console.WriteLine($"- {FormatEvidence(evidence)}");
            }
            foreach (var diagnostic in recommendation.ValidationDiagnostics)
            {
                Console.WriteLine($"- Validation: {diagnostic}");
            }
        }

        WriteSection("Risks And Questions");
        WriteValues("Risks", result.Analysis.Risks);
        WriteValues("Unknowns", result.Analysis.Unknowns);
        WriteValues("Questions", result.Analysis.ClarifyingQuestions);
        Console.WriteLine($"Confidence: {result.Analysis.OverallConfidenceExplanation}");

        WriteSection("Token Usage");
        Console.WriteLine($"Selected context estimate: {result.Context.EstimatedTokens}");
        foreach (var call in result.Usage.Calls)
        {
            Console.WriteLine(
                $"{call.Stage}: provider={call.Provider}; model={call.Model}; estimated input={call.EstimatedInputTokens}; "
                + $"actual input={FormatUsage(call.ActualInputTokens)}; cached={FormatUsage(call.CachedInputTokens)}; "
                + $"output={FormatUsage(call.ActualOutputTokens)}; duration={call.DurationMilliseconds} ms; retries={call.Retries}; "
                + $"reasoning effort={call.ReasoningEffort ?? "n/a"}");
        }

        WriteSection("Analysis Record");
        Console.WriteLine(result.SavedAnalysisPath ?? "Not persisted");
    }

    private static string FormatEvidence(EvidenceReference evidence)
    {
        var identity = evidence.EntityId ?? evidence.ProjectId
            ?? (evidence.SourceEntityId is not null
                ? $"{evidence.SourceEntityId} {evidence.RelationType} {evidence.TargetEntityId}"
                : evidence.Kind.ToString());
        var location = evidence.RelativePath is null
            ? string.Empty
            : evidence.StartLine is null
                ? $" at {evidence.RelativePath}"
                : $" at {evidence.RelativePath}:{evidence.StartLine}-{evidence.EndLine}";
        return $"{evidence.Kind}: {identity}{location} ({evidence.ResolutionLevel?.ToString() ?? "n/a"})";
    }

    private static string FormatUsage(int? tokens) => tokens?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unavailable";

    private static void WriteValues(string label, IReadOnlyList<string> values)
    {
        Console.WriteLine($"{label}: {(values.Count == 0 ? "none" : string.Join("; ", values))}");
    }

    private static void WriteMemorySummary(ProjectMemorySyncResult result)
    {
        Console.WriteLine("Engineering Brain");
        WriteSection("Project Memory");
        Console.WriteLine($"Repository: {result.SourceSnapshot.Repository.Name}");
        Console.WriteLine($"Branch: {result.Manifest.Branch}");
        Console.WriteLine($"Mode: {result.Mode}");
        Console.WriteLine($"Snapshot: schema {result.Manifest.SourceSnapshotSchema}");
        Console.WriteLine($"Knowledge: schema {result.Manifest.KnowledgeSchemaVersion}");

        WriteSection("Source");
        Console.WriteLine($"Changes: {result.SourceSnapshot.Incremental.Metrics.ChangedFiles}");
        Console.WriteLine($"Projects: {result.SourceSnapshot.Projects.Count}");
        Console.WriteLine($"Entities: {result.SourceSnapshot.Entities.Count}");
        Console.WriteLine($"Relations: {result.SourceSnapshot.Relations.Count}");

        WriteSection("Memory");
        Console.WriteLine($"Project notes: {result.Metrics.ProjectNotes}");
        Console.WriteLine($"Component notes: {result.Metrics.ComponentNotes}");
        Console.WriteLine($"Managed notes: {result.Metrics.TotalManagedNotes}");
        Console.WriteLine($"Created: {result.Metrics.Created}");
        Console.WriteLine($"Updated: {result.Metrics.Updated}");
        Console.WriteLine($"Deleted: {result.Metrics.Deleted}");
        Console.WriteLine($"Reused: {result.Metrics.Reused}");
        Console.WriteLine($"Elapsed: {result.Metrics.ElapsedMilliseconds} ms");

        WriteSection("Integrity");
        Console.WriteLine(result.Integrity.IsValid ? "Valid" : "Invalid");

        WriteSection("Location");
        Console.WriteLine(result.Location);
    }

    private static void WriteSummary(RepositorySnapshot snapshot, string snapshotPath)
    {
        Console.WriteLine("Engineering Brain");
        WriteSection("Repository");
        Console.WriteLine($"Name: {snapshot.Repository.Name}");
        Console.WriteLine($"Root: {snapshot.Repository.Root}");
        Console.WriteLine($"Branch: {snapshot.Git.Branch ?? "not a Git repository"}");
        Console.WriteLine($"Commit: {snapshot.Git.HeadCommit ?? "n/a"}");
        Console.WriteLine($"Working tree: {FormatWorkingTree(snapshot.Git)}");

        WriteSection("Scan");
        Console.WriteLine($"Mode: {snapshot.Incremental.Mode}");
        Console.WriteLine($"Previous snapshot: {(snapshot.Incremental.PreviousSnapshotFound ? "found" : "not found")}");
        if (snapshot.Incremental.FullScanReason is not null)
        {
            Console.WriteLine($"Reason: {snapshot.Incremental.FullScanReason}");
        }

        WriteSection("Changes");
        foreach (var kind in Enum.GetValues<FileChangeKind>())
        {
            Console.WriteLine($"{kind}: {snapshot.Incremental.Changes.Count(change => change.Kind == kind)}");
        }
        foreach (var change in snapshot.Incremental.Changes.Take(10))
        {
            var path = change.Kind == FileChangeKind.Renamed
                ? $"{change.PreviousPath} -> {change.CurrentPath}"
                : change.CurrentPath ?? change.PreviousPath ?? "unknown";
            var project = change.ProjectPath is null ? string.Empty : $"; project: {change.ProjectPath}";
            Console.WriteLine($"- {change.Kind}: {path} ({change.DetectionMethod}{project})");
        }
        if (snapshot.Incremental.Changes.Count > 10)
        {
            Console.WriteLine($"Additional changes saved in snapshot: {snapshot.Incremental.Changes.Count - 10}");
        }

        WriteSection("Files");
        Console.WriteLine($"Total: {snapshot.Files.Count}");

        WriteSection("Languages");
        foreach (var language in snapshot.Languages)
        {
            Console.WriteLine($"{language.Language}: {language.FileCount}");
        }

        WriteSection("Projects");
        Console.WriteLine($"Detected: {snapshot.Analysis.DetectedProjects}");
        Console.WriteLine($"Semantic: {snapshot.Analysis.SemanticProjects}");
        Console.WriteLine($"Fallback: {snapshot.Analysis.FallbackProjects}");
        Console.WriteLine($"Reanalyzed: {snapshot.Incremental.Metrics.ProjectsAnalyzed}");
        Console.WriteLine($"Reused: {snapshot.Incremental.Metrics.ProjectsReused}");

        WriteProjectList("Directly affected", snapshot.Incremental.DirectlyAffectedProjects);
        WriteProjectList("Transitively affected", snapshot.Incremental.TransitivelyAffectedProjects);
        WriteProjectList("Reuse", snapshot.Incremental.ReusedProjects);

        WriteSection("C# Analysis");
        Console.WriteLine($"Mode: {snapshot.Analysis.Mode}");
        Console.WriteLine($"Analyzer: {snapshot.Analysis.AnalyzerVersion}");
        Console.WriteLine($"Entities: {snapshot.Entities.Count}");
        Console.WriteLine($"Relations: {snapshot.Relations.Count}");
        Console.WriteLine($"Diagnostics: {snapshot.Diagnostics.Count}");

        WriteSection("Incremental Metrics");
        Console.WriteLine($"Files: {snapshot.Incremental.Metrics.TotalFiles}");
        Console.WriteLine($"Changed files: {snapshot.Incremental.Metrics.ChangedFiles}");
        Console.WriteLine($"Entities reused: {snapshot.Incremental.Metrics.EntitiesReused}");
        Console.WriteLine($"Entities regenerated: {snapshot.Incremental.Metrics.EntitiesRegenerated}");
        Console.WriteLine($"Elapsed: {snapshot.Incremental.Metrics.ElapsedMilliseconds} ms");
        Console.WriteLine($"Graph integrity: {(snapshot.Incremental.GraphIntegrity.IsValid ? "valid" : "invalid")}");

        WriteSection("Architecture");
        foreach (var type in Enum.GetValues<CodeEntityType>())
        {
            var count = snapshot.Entities.Count(entity => entity.EntityType == type);
            if (count > 0)
            {
                Console.WriteLine($"{GetEntityLabel(type)}: {count}");
            }
        }

        WriteSection("Relations");
        var reportedRelationTypes = new[]
        {
            CodeRelationType.Contains,
            CodeRelationType.ReferencesProject,
            CodeRelationType.Inherits,
            CodeRelationType.Implements
        };
        foreach (var relationType in reportedRelationTypes)
        {
            Console.WriteLine($"{relationType}: {snapshot.Relations.Count(relation => relation.RelationType == relationType)}");
        }

        var relevantDiagnostics = snapshot.Diagnostics
            .Where(diagnostic => diagnostic.Severity != AnalysisDiagnosticSeverity.Information
                || diagnostic.Code.Contains("LOAD", StringComparison.Ordinal))
            .Take(10)
            .ToArray();
        if (relevantDiagnostics.Length > 0)
        {
            WriteSection("Project Load Diagnostics");
            foreach (var diagnostic in relevantDiagnostics)
            {
                Console.WriteLine($"{diagnostic.ProjectPath ?? "Repository"}: {diagnostic.Message}");
            }

            if (snapshot.Diagnostics.Count > relevantDiagnostics.Length)
            {
                Console.WriteLine($"Additional diagnostics saved in snapshot: {snapshot.Diagnostics.Count - relevantDiagnostics.Length}");
            }
        }

        WriteSection("Snapshot");
        Console.WriteLine($"Saved to: {snapshotPath}");
    }

    private static string FormatWorkingTree(GitInfo git) => git.IsWorkingTreeClean switch
    {
        true => "clean",
        false => "dirty",
        null => "n/a"
    };

    private static string GetEntityLabel(CodeEntityType type) => type switch
    {
        CodeEntityType.Class => "Classes",
        CodeEntityType.Property => "Properties",
        _ => $"{type}s"
    };

    private static void WriteProjectList(string title, IReadOnlyList<string> projects)
    {
        if (projects.Count == 0)
        {
            return;
        }

        WriteSection(title);
        foreach (var project in projects)
        {
            Console.WriteLine(project);
        }
    }

    private static void WriteSection(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
        Console.WriteLine(new string('-', 40));
    }

    private static void WriteUsage()
    {
        Console.WriteLine("Engineering Brain");
        Console.WriteLine();
        Console.WriteLine("Usage: brain scan [path]");
        Console.WriteLine("       brain memory sync [path]");
        Console.WriteLine("       brain eval [repository-or-suite-path] [--update-baseline]");
        Console.WriteLine("       brain eval-live [repository-or-live-suite] [--preview | --fake-provider | --allow-remote]");
        Console.WriteLine("           [--case <id>] [--runs <1-3>] [--pricing <pricing.json>]");
        Console.WriteLine("           [--interpretation-model <model>] [--reasoning-model <model>]");
        Console.WriteLine("           [--interpretation-effort <low|medium|high>] [--analysis-effort <low|medium|high>]");
        Console.WriteLine("       brain analyze <initiative.md> [--repo <path>] [--preview | --allow-remote]");
        Console.WriteLine("           [--interpretation-model <model>] [--reasoning-model <model>]");
        Console.WriteLine("           [--interpretation-effort <low|medium|high>] [--analysis-effort <low|medium|high>]");
        Console.WriteLine("If path is omitted, the current directory is scanned.");
    }

    private sealed record AnalyzeOptions(
        string InitiativePath,
        string RepositoryPath,
        bool AllowRemote,
        bool Preview,
        string InterpretationModel,
        string ReasoningModel,
        string InterpretationEffort,
        string AnalysisEffort);

    private sealed record LiveEvaluationOptions(
        string InputPath,
        bool AllowRemote,
        bool FakeProvider,
        bool Preview,
        int Runs,
        IReadOnlyList<string> CaseIds,
        string? PricingPath,
        string InterpretationModel,
        string ReasoningModel,
        string InterpretationEffort,
        string AnalysisEffort);
}
