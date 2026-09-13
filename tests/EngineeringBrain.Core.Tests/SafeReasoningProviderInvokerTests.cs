using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class SafeReasoningProviderInvokerTests
{
    [Theory]
    [InlineData("sk-fakeapikey123456789")]
    [InlineData("C:\\Users\\person\\private\\file.cs")]
    [InlineData("/home/person/private/file.cs")]
    [InlineData("Server=db;User Id=admin;Password=fixture-secret")]
    public async Task InvokeAsync_ArbitraryProviderMessageNeverEscapes(string unsafeMessage)
    {
        var provider = new ThrowingProvider(new InvalidOperationException(unsafeMessage));
        var approved = ApprovedRequest();

        var exception = await Assert.ThrowsAsync<ReasoningProviderException>(() =>
            new SafeReasoningProviderInvoker().InvokeAsync<InitiativeUnderstanding>(provider, approved));

        Assert.Equal(ReasoningProviderFailureCode.TransportFailure, exception.FailureCode);
        Assert.DoesNotContain(unsafeMessage, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(unsafeMessage, exception.ToString(), StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public async Task InvokeAsync_CancellationPropagatesUnchanged()
    {
        var cancellation = new OperationCanceledException("fixture cancellation");
        var provider = new ThrowingProvider(cancellation);

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new SafeReasoningProviderInvoker().InvokeAsync<InitiativeUnderstanding>(provider, ApprovedRequest()));

        Assert.Same(cancellation, actual);
    }

    [Fact]
    public async Task InvokeAsync_ReasoningProviderFailureRetainsUsageButNotRawCause()
    {
        var safe = new ReasoningProviderException(
            ReasoningProviderFailureCode.Timeout,
            ReasoningStage.InitiativeUnderstanding,
            17,
            2,
            actualInputTokens: 30,
            cachedInputTokens: 10,
            actualOutputTokens: 5,
            reasoningTokens: 3);
        var provider = new ThrowingProvider(safe);

        var actual = await Assert.ThrowsAsync<ReasoningProviderException>(() =>
            new SafeReasoningProviderInvoker().InvokeAsync<InitiativeUnderstanding>(provider, ApprovedRequest()));

        Assert.Same(safe, actual);
        Assert.Equal(ReasoningProviderFailureCode.Timeout, actual.FailureCode);
        Assert.Equal(30, actual.ActualInputTokens);
        Assert.Equal(10, actual.CachedInputTokens);
        Assert.Equal(5, actual.ActualOutputTokens);
        Assert.Equal(3, actual.ReasoningTokens);
        Assert.Null(actual.InnerException);
    }

    [Fact]
    public void LivePersistence_BoundsAndSanitizesDefenseInDepth()
    {
        var unsafeMessage = "api_key=sk-fakeapikey123456789\r\nInjected line\t" + new string('x', 800);

        var safe = LocalLiveEvaluationStore.Redact(unsafeMessage)!;

        Assert.DoesNotContain("sk-fakeapikey123456789", safe, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', safe);
        Assert.DoesNotContain('\n', safe);
        Assert.DoesNotContain('\t', safe);
        Assert.True(safe.Length <= 512);
    }

    private static ApprovedReasoningRequest ApprovedRequest() => new OutboundRequestGate().ApproveExact(new ReasoningRequest(
        ReasoningStage.InitiativeUnderstanding,
        "model-a",
        InitiativeAnalysisPrompts.Understanding,
        "Add bounded analysis support.",
        500,
        50));

    private sealed class ThrowingProvider(Exception exception) : IReasoningProvider
    {
        public string Name => "Throwing";

        public Task<ReasoningResult<T>> GenerateStructuredAsync<T>(
            ApprovedReasoningRequest request,
            CancellationToken cancellationToken = default) => throw exception;
    }
}
