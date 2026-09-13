using System.Reflection;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ReasoningProviderBoundaryTests
{
    [Fact]
    public void ProviderContract_AcceptsOnlyApprovedReasoningRequest()
    {
        var method = typeof(IReasoningProvider).GetMethod("GenerateStructuredAsync")!;

        Assert.Equal(typeof(ApprovedReasoningRequest), method.GetParameters()[0].ParameterType);
        Assert.DoesNotContain(typeof(IReasoningProvider).GetMethods(), methodInfo =>
            methodInfo.GetParameters().Any(parameter => parameter.ParameterType == typeof(ReasoningRequest)));
    }

    [Fact]
    public void ApprovedReasoningRequest_HasNoPublicConstructorOrFactory()
    {
        var type = typeof(ApprovedReasoningRequest);

        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.DoesNotContain(
            type.GetMethods(BindingFlags.Public | BindingFlags.Static),
            method => method.ReturnType == type);
    }

    [Fact]
    public async Task OpenAIProvider_RejectsInternallyForgedApprovalBeforeClientInvocation()
    {
        var request = Request();
        var approved = new OutboundRequestGate().ApproveExact(request);
        var forged = new ApprovedReasoningRequest(
            request with { UserData = "different model-visible content" },
            approved.Assessment);
        var provider = new OpenAIReasoningProvider("sk-fixture-key-not-for-network");

        var exception = await Assert.ThrowsAsync<OutboundSecurityException>(() =>
            provider.GenerateStructuredAsync<InitiativeUnderstanding>(forged));

        Assert.Equal("OUTBOUND_REQUEST_INTEGRITY_INVALID", exception.Code);
    }

    [Fact]
    public void OpenAIRequestMapping_UsesOnlyApprovedSystemAndUserText()
    {
        var request = Request() with
        {
            SystemInstructions = "fixed system instructions",
            UserData = "selected bounded facts"
        };
        var approved = new OutboundRequestGate().ApproveExact(request);
        var provider = new OpenAIReasoningProvider("sk-fixture-key-not-for-network");

        var payload = provider.MapTransportPayload<InitiativeUnderstanding>(approved);

        Assert.Equal(request.Model, payload.Model);
        Assert.Equal(request.MaximumOutputTokens, payload.MaximumOutputTokens);
        Assert.Equal(request.SystemInstructions, payload.SystemInstructions);
        Assert.Equal(request.UserData, payload.UserData);
    }

    [Fact]
    public void OpenAIRequestMapping_AddsOnlyFixedProtocolMetadata()
    {
        var request = Request();
        var approved = new OutboundRequestGate().ApproveExact(request);
        var provider = new OpenAIReasoningProvider("sk-fixture-key-not-for-network");

        var payload = provider.MapTransportPayload<InitiativeUnderstanding>(approved);

        Assert.Equal("low", payload.ReasoningEffort);
        Assert.Equal("initiative_understanding", payload.ResponseSchemaName);
        Assert.Equal(
            ReasoningJsonSchema.For<InitiativeUnderstanding>().ToString(),
            payload.ResponseSchema.ToString());
        Assert.Equal(
            [
                "Model",
                "MaximumOutputTokens",
                "SystemInstructions",
                "UserData",
                "ReasoningEffort",
                "ResponseSchemaName",
                "ResponseSchema"
            ],
            typeof(OpenAITransportPayload).GetProperties().Select(property => property.Name).ToArray());
    }

    private static ReasoningRequest Request() => new(
        ReasoningStage.InitiativeUnderstanding,
        "model-a",
        "Explain architecture using only supplied evidence.",
        "Add bounded analysis support.",
        500,
        50);
}
