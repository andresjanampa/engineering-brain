using System.Text.Json;
using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class RemoteContextPreviewTests
{
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
}
