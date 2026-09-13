using System.Text.Json;
using System.Text.Json.Nodes;
using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class RemoteContextPreviewTests
{
    [Fact]
    public async Task PreviewStore_NewArtifactWritesSchemaTwoAndAssessmentKinds()
    {
        using var memoryFixture = new InitiativeMemoryFixture();
        var memory = await memoryFixture.CreateMemoryAsync();
        var output = Path.Combine(Path.GetTempPath(), $"brain-preview-schema-{Guid.NewGuid():N}");
        try
        {
            var store = new RemoteContextPreviewStore(output);
            var preview = await new RemoteContextPreviewService(store: store).CreateAsync(
                "initiative.md",
                "Add business analyzer support.",
                memory,
                "model-a",
                "model-b",
                "low",
                "medium");

            var persisted = await store.LoadAsync(preview.ManifestPath);

            Assert.Equal(2, persisted.PreviewSchemaVersion);
            Assert.Equal(OutboundAssessmentKind.Exact, persisted.Call1PolicyAssessment!.AssessmentKind);
            Assert.Equal(OutboundAssessmentKind.Projected, persisted.Call2PolicyAssessment!.AssessmentKind);
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [Fact]
    public async Task PreviewStore_SchemaOneLoadsBothAssessmentsAsNotRecorded()
    {
        using var memoryFixture = new InitiativeMemoryFixture();
        var memory = await memoryFixture.CreateMemoryAsync();
        var output = Path.Combine(Path.GetTempPath(), $"brain-preview-legacy-{Guid.NewGuid():N}");
        try
        {
            var store = new RemoteContextPreviewStore(output);
            var preview = await new RemoteContextPreviewService(store: store).CreateAsync(
                "initiative.md",
                "Add business analyzer support.",
                memory,
                "model-a",
                "model-b",
                "low",
                "medium");
            var legacy = JsonNode.Parse(await File.ReadAllTextAsync(preview.ManifestPath))!.AsObject();
            legacy["previewSchemaVersion"] = 1;
            legacy.Remove("call1PolicyAssessment");
            legacy.Remove("call2PolicyAssessment");
            var legacyJson = legacy.ToJsonString();
            await File.WriteAllTextAsync(preview.ManifestPath, legacyJson);

            var persisted = await store.LoadAsync(preview.ManifestPath);

            Assert.Equal(OutboundAssessmentKind.NotRecorded, persisted.Call1PolicyAssessment!.AssessmentKind);
            Assert.Equal(OutboundAssessmentKind.NotRecorded, persisted.Call2PolicyAssessment!.AssessmentKind);
            Assert.False(persisted.Call1PolicyAssessment.IsAllowed);
            Assert.False(persisted.Call2PolicyAssessment.IsAllowed);
            Assert.Equal(legacyJson, await File.ReadAllTextAsync(preview.ManifestPath));
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [Fact]
    public async Task PrepareAsync_CallOneIsExactAndCallTwoIsProjected()
    {
        using var memoryFixture = new InitiativeMemoryFixture();
        var memory = await memoryFixture.CreateMemoryAsync();
        var service = new RemoteContextPreviewService();

        var preparation = await service.PrepareAsync(
            "initiative.md",
            "Add business analyzer support without network access.",
            memory,
            ReviewedConceptResolutionResult.Absent,
            "model-a",
            "model-b",
            "low",
            "medium");

        Assert.Equal(OutboundAssessmentKind.Exact, preparation.Preview.Call1PolicyAssessment.AssessmentKind);
        Assert.Equal(OutboundAssessmentKind.Projected, preparation.Preview.Call2PolicyAssessment.AssessmentKind);
        Assert.True(preparation.Preview.Call1PolicyAssessment.IsAllowed);
        Assert.True(preparation.Preview.Call2PolicyAssessment.IsAllowed);
    }

    [Fact]
    public async Task PrepareAsync_CallOneFingerprintMatchesApprovedRequest()
    {
        using var memoryFixture = new InitiativeMemoryFixture();
        var memory = await memoryFixture.CreateMemoryAsync();

        var preparation = await new RemoteContextPreviewService().PrepareAsync(
            "initiative.md",
            "Add business analyzer support without network access.",
            memory,
            ReviewedConceptResolutionResult.Absent,
            "model-a",
            "model-b",
            "low",
            "medium");

        Assert.Equal(
            preparation.ApprovedCall1.Assessment.PayloadFingerprint,
            preparation.Preview.Call1PolicyAssessment.PayloadFingerprint);
        Assert.Equal(
            OutboundRequestFingerprint.Create(preparation.ApprovedCall1.Request),
            preparation.Preview.Call1PolicyAssessment.PayloadFingerprint);
    }

    [Fact]
    public async Task PrepareAsync_ProjectedCallTwoCannotBeTransported()
    {
        using var memoryFixture = new InitiativeMemoryFixture();
        var memory = await memoryFixture.CreateMemoryAsync();

        var preparation = await new RemoteContextPreviewService().PrepareAsync(
            "initiative.md",
            "Add business analyzer support without network access.",
            memory,
            ReviewedConceptResolutionResult.Absent,
            "model-a",
            "model-b",
            "low",
            "medium");

        Assert.Equal(OutboundAssessmentKind.Projected, preparation.Preview.Call2PolicyAssessment.AssessmentKind);
        Assert.DoesNotContain(
            typeof(ApprovedReasoningRequest),
            typeof(RemoteContextPreview).GetProperties().Select(property => property.PropertyType));
        Assert.DoesNotContain(
            typeof(ApprovedReasoningRequest),
            typeof(RemoteAnalysisPreparation).GetProperties()
                .Where(property => property.Name != nameof(RemoteAnalysisPreparation.ApprovedCall1))
                .Select(property => property.PropertyType));
    }

    [Fact]
    public async Task PrepareAsync_UsesInjectedGateForBothCalls()
    {
        using var memoryFixture = new InitiativeMemoryFixture();
        var memory = await memoryFixture.CreateMemoryAsync();
        var call1Gate = CreateGate("fixture-call-one-secret");
        var call1Service = new RemoteContextPreviewService(gate: call1Gate);

        await Assert.ThrowsAsync<OutboundSecurityException>(() => call1Service.PrepareAsync(
            "initiative.md",
            "fixture-call-one-secret",
            memory,
            ReviewedConceptResolutionResult.Absent,
            "model-a",
            "model-b",
            "low",
            "medium"));

        const string call2Secret = "fixture-call-two-secret";
        var rootNote = memory.Manifest.Notes.Single(note => note.Kind == KnowledgeNoteKind.RootIndex);
        var rootNotePath = Path.Combine(memory.Location, rootNote.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        var content = await File.ReadAllTextAsync(rootNotePath) + Environment.NewLine + call2Secret;
        await File.WriteAllTextAsync(rootNotePath, content);
        var updatedNote = rootNote with { ContentHash = KnowledgeIdentity.ContentHash(content) };
        var updatedMemory = memory with
        {
            Manifest = memory.Manifest with
            {
                Notes = memory.Manifest.Notes.Select(note => note.Identity == rootNote.Identity ? updatedNote : note).ToArray()
            }
        };
        var call2Service = new RemoteContextPreviewService(gate: CreateGate(call2Secret));

        var call2Preparation = await call2Service.PrepareAsync(
            "initiative.md",
            "Add business analyzer support without network access.",
            updatedMemory,
            ReviewedConceptResolutionResult.Absent,
            "model-a",
            "model-b",
            "low",
            "medium");

        Assert.False(call2Preparation.Preview.Call2PolicyAssessment.IsAllowed);
        Assert.Contains(call2Preparation.Preview.Call2PolicyAssessment.Results, result =>
            result.Category == PolicyContentScope.Secrets
            && result.Outcome == OutboundPolicyOutcome.Blocked);
    }

    [Fact]
    public async Task PrepareAsync_BlockedCallOneCreatesNoManifest()
    {
        using var memoryFixture = new InitiativeMemoryFixture();
        var memory = await memoryFixture.CreateMemoryAsync();
        var output = Path.Combine(Path.GetTempPath(), $"brain-preview-blocked-{Guid.NewGuid():N}");
        try
        {
            var service = new RemoteContextPreviewService(
                gate: CreateGate("fixture-blocked-secret"),
                store: new RemoteContextPreviewStore(output));

            await Assert.ThrowsAsync<OutboundSecurityException>(() => service.PrepareAsync(
                "initiative.md",
                "fixture-blocked-secret",
                memory,
                ReviewedConceptResolutionResult.Absent,
                "model-a",
                "model-b",
                "low",
                "medium"));

            Assert.False(Directory.Exists(output));
        }
        finally
        {
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }

    [Fact]
    public async Task CreateAsync_BackwardCompatibleWrapperReturnsSafePreviewOnly()
    {
        using var memoryFixture = new InitiativeMemoryFixture();
        var memory = await memoryFixture.CreateMemoryAsync();

        var preview = await new RemoteContextPreviewService().CreateAsync(
            "initiative.md",
            "Add business analyzer support without network access.",
            memory,
            "model-a",
            "model-b",
            "low",
            "medium");

        Assert.IsType<RemoteContextPreview>(preview);
        Assert.DoesNotContain(
            typeof(RemoteContextPreview).GetProperties(),
            property => property.PropertyType == typeof(ApprovedReasoningRequest)
                || property.PropertyType == typeof(ReasoningRequest));
    }

    [Fact]
    public async Task Preview_IsOfflineDeterministicBoundedAndContainsNoPayloadText()
    {
        using var memoryFixture = new InitiativeMemoryFixture();
        var memory = await memoryFixture.CreateMemoryAsync();
        var output = Path.Combine(Path.GetTempPath(), $"brain-preview-{Guid.NewGuid():N}");
        try
        {
            var service = new RemoteContextPreviewService(store: new RemoteContextPreviewStore(output));
            const string initiative = "Add business analyzer support without network access.";
            var first = await service.CreateAsync("initiative.md", initiative, memory, "model-a", "model-b", "low", "medium");
            var second = await service.CreateAsync("initiative.md", initiative, memory, "model-a", "model-b", "low", "medium");

            Assert.Equal(first.Call1, second.Call1);
            Assert.Equal(first.Call2.SelectedEntityIds, second.Call2.SelectedEntityIds);
            Assert.Equal(first.Call2.SelectedNotePaths, second.Call2.SelectedNotePaths);
            Assert.Equal("Deterministic", first.Call1.InterpretationSource);
            Assert.True(first.WithinBudget);
            Assert.Equal(0, first.Security.SourceBodyFindings);
            Assert.Equal(0, first.Security.AbsolutePathFindings);
            Assert.Equal(0, first.Security.SecretFindings);
            Assert.Equal(0, first.Security.RawSnapshotFindings);
            Assert.DoesNotContain(first.Retrieval.Components.SelectMany(item => item.MatchReasons),
                item => item.Signal == "reviewed concept");
            Assert.True(first.Call2.ProjectNotes + first.Call2.ComponentNotes > 0);
            var json = await File.ReadAllTextAsync(first.ManifestPath);
            Assert.DoesNotContain(initiative, json, StringComparison.Ordinal);
            Assert.DoesNotContain(memory.SourceSnapshot.Repository.Root, json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("OPENAI_API_KEY", json, StringComparison.OrdinalIgnoreCase);
            using var document = JsonDocument.Parse(json);
            Assert.Equal(initiative.Length, document.RootElement.GetProperty("call1").GetProperty("characterCount").GetInt32());
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
    }

    [Fact]
    public void DeterministicInterpreter_DoesNotClaimModelReasoning()
    {
        var value = new DeterministicInitiativeInterpreter().Interpret("Agregar analizador de lenguaje");
        Assert.Contains("Deterministic preview", value.Summary, StringComparison.Ordinal);
        Assert.Contains("analyzer", value.SearchTerms);
    }

    [Fact]
    public void ReasoningEffortDefaultsAreProviderSpecific()
    {
        var options = OpenAIReasoningProviderOptions.Default;
        Assert.Equal("low", options.GetReasoningEffort(ReasoningStage.InitiativeUnderstanding));
        Assert.Equal("medium", options.GetReasoningEffort(ReasoningStage.ArchitectureAnalysis));
    }

    [Fact]
    public void ReasoningEffortEnvironmentOverridesAreIndependent()
    {
        var values = new Dictionary<string, string> {
            ["ENGINEERING_BRAIN_INTERPRETATION_REASONING_EFFORT"] = "medium",
            ["ENGINEERING_BRAIN_ANALYSIS_REASONING_EFFORT"] = "high"
        };
        var options = OpenAIReasoningProviderOptions.FromEnvironment(key => values.GetValueOrDefault(key));
        Assert.Equal("medium", options.GetReasoningEffort(ReasoningStage.InitiativeUnderstanding));
        Assert.Equal("high", options.GetReasoningEffort(ReasoningStage.ArchitectureAnalysis));
    }

    [Fact]
    public void InvalidReasoningEffortIsRejectedClearly()
    {
        var options = OpenAIReasoningProviderOptions.Default with { AnalysisReasoningEffort = "extreme" };
        Assert.Contains("Unsupported", Assert.Throws<ArgumentException>(() => options.GetReasoningEffort(ReasoningStage.ArchitectureAnalysis)).Message);
    }

    [Fact]
    public void UsageCanRecordReasoningEffort()
    {
        var usage = new ReasoningCallUsage(ReasoningStage.ArchitectureAnalysis, "fake", "model", 1, null, null, null, 1, 0, "medium");
        Assert.Equal("medium", usage.ReasoningEffort);
    }

    private static OutboundRequestGate CreateGate(params string[] secrets) => new(
        new OutboundContextGuard(new FixedSecretValueSource(secrets)),
        new OutboundPolicyEvaluator());

    private sealed class FixedSecretValueSource(params string[] values) : IOutboundSecretValueSource
    {
        public IReadOnlySet<string> GetValues() => values.ToHashSet(StringComparer.Ordinal);
    }
}
