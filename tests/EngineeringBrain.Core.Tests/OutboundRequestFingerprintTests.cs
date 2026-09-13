using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class OutboundRequestFingerprintTests
{
    [Fact]
    public void Create_IsDeterministicForIdenticalRequest()
    {
        var request = Request();

        var first = OutboundRequestFingerprint.Create(request);
        var second = OutboundRequestFingerprint.Create(request with { });

        Assert.Equal(first, second);
        Assert.Equal(64, first.Length);
        Assert.Equal(first.ToLowerInvariant(), first);
    }

    [Fact]
    public void Create_ChangesForStageModelSystemUserOrMaximumOutput()
    {
        var request = Request();
        var baseline = OutboundRequestFingerprint.Create(request);

        Assert.NotEqual(baseline, OutboundRequestFingerprint.Create(request with { Stage = ReasoningStage.ArchitectureAnalysis }));
        Assert.NotEqual(baseline, OutboundRequestFingerprint.Create(request with { Model = "model-b" }));
        Assert.NotEqual(baseline, OutboundRequestFingerprint.Create(request with { SystemInstructions = "system-b" }));
        Assert.NotEqual(baseline, OutboundRequestFingerprint.Create(request with { UserData = "user-b" }));
        Assert.NotEqual(baseline, OutboundRequestFingerprint.Create(request with { MaximumOutputTokens = 513 }));
    }

    [Fact]
    public void Create_IgnoresEstimatedInputTokensBecauseTheyAreNotTransported()
    {
        var request = Request();

        Assert.Equal(
            OutboundRequestFingerprint.Create(request),
            OutboundRequestFingerprint.Create(request with { EstimatedInputTokens = 9999 }));
    }

    [Fact]
    public void Create_LengthPrefixesPreventFieldBoundaryCollision()
    {
        var first = Request() with { SystemInstructions = "a\nb", UserData = "c" };
        var second = Request() with { SystemInstructions = "a", UserData = "b\nc" };

        Assert.NotEqual(
            OutboundRequestFingerprint.Create(first),
            OutboundRequestFingerprint.Create(second));
    }

    [Fact]
    public void Create_PreservesExactLineEndings()
    {
        var request = Request();

        Assert.NotEqual(
            OutboundRequestFingerprint.Create(request with { UserData = "line one\nline two" }),
            OutboundRequestFingerprint.Create(request with { UserData = "line one\r\nline two" }));
    }

    [Fact]
    public void Create_RejectsNullRequest()
    {
        Assert.Throws<ArgumentNullException>(() => OutboundRequestFingerprint.Create(null!));
    }

    private static ReasoningRequest Request() => new(
        ReasoningStage.InitiativeUnderstanding,
        "model-a",
        "system-a",
        "user-a",
        512,
        100);
}
