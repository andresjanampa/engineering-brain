using System.Text.Json;
using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class OutboundRequestGateTests
{
    [Fact]
    public void AssessProjected_ReturnsProjectedAssessmentWithoutTransportableRequest()
    {
        var assessment = new OutboundRequestGate().AssessProjected(Request());

        Assert.Equal(OutboundAssessmentKind.Projected, assessment.AssessmentKind);
        Assert.True(assessment.IsAllowed);
        Assert.NotNull(assessment.PayloadFingerprint);
        Assert.False(typeof(ApprovedReasoningRequest).IsAssignableFrom(assessment.GetType()));
    }

    [Fact]
    public void ApproveExact_AllowedPayloadReturnsExactApprovedRequest()
    {
        var approved = new OutboundRequestGate().ApproveExact(Request());

        Assert.Same(approved.Request, approved.Request);
        Assert.Equal(OutboundAssessmentKind.Exact, approved.Assessment.AssessmentKind);
        Assert.True(approved.Assessment.IsAllowed);
        Assert.Equal(OutboundRequestFingerprint.Create(approved.Request), approved.Assessment.PayloadFingerprint);
        approved.EnsureIntegrity();
    }

    [Fact]
    public void ApproveExact_BlockedPayloadThrowsAndRetainsSafeAssessment()
    {
        const string secret = "test-secret-value-123";
        var gate = new OutboundRequestGate(new OutboundContextGuard(new FixedSecretValueSource(secret)));

        var exception = Assert.Throws<OutboundSecurityException>(() =>
            gate.ApproveExact(Request(systemInstructions: $"Do not disclose {secret}")));

        Assert.Equal("OUTBOUND_POLICY_BLOCKED", exception.Code);
        Assert.Equal(OutboundPolicyOutcome.Blocked, exception.Assessment.OverallOutcome);
        Assert.Null(exception.Assessment.PayloadFingerprint);
        Assert.DoesNotContain(secret, exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(exception.Assessment), StringComparison.Ordinal);
    }

    [Fact]
    public void ApproveExact_ContextMismatchFailsClosed()
    {
        var segment = new ContextSegment(ContextSegmentKind.GraphEvidence, "represented", 1, "entity", 1, true);
        var context = new InitiativeContext("different", 1, [], [], [segment]);

        var exception = Assert.Throws<OutboundSecurityException>(() =>
            new OutboundRequestGate().ApproveExact(Request(userData: "different"), context));

        Assert.Equal("OUTBOUND_CONTEXT_INVALID", exception.Code);
        Assert.Contains(
            exception.Assessment.Results,
            result => result.ReasonCode == OutboundInspectionReasonCode.ContextRepresentationMismatch);
    }

    [Fact]
    public void ApproveExact_MultipleViolationsNeverCreatesRequest()
    {
        const string payload = "api_key=fake-secret-value C:\\Users\\person\\repo\\File.cs public class Leaked {";
        var transportAttempts = 0;

        Assert.Throws<OutboundSecurityException>(() =>
        {
            _ = new OutboundRequestGate().ApproveExact(Request(userData: payload));
            transportAttempts++;
        });

        Assert.Equal(0, transportAttempts);
    }

    [Fact]
    public void EnsureIntegrity_InternallyForgedFingerprintMismatchThrows()
    {
        var request = Request();
        var assessment = AllowedAssessment("forged-fingerprint");
        var forged = new ApprovedReasoningRequest(request, assessment);

        var exception = Assert.Throws<OutboundSecurityException>(forged.EnsureIntegrity);

        Assert.Equal("OUTBOUND_REQUEST_INTEGRITY_INVALID", exception.Code);
    }

    [Fact]
    public void EnsureIntegrity_RejectsTamperedAssessmentShape()
    {
        var request = Request();
        var assessment = AllowedAssessment(OutboundRequestFingerprint.Create(request)) with
        {
            Results = AllowedAssessment("ignored").Results.Take(4).ToArray()
        };
        var forged = new ApprovedReasoningRequest(request, assessment);

        Assert.Throws<OutboundSecurityException>(forged.EnsureIntegrity);
    }

    [Fact]
    public void ApprovedReasoningRequest_CopiesAssessmentCollections()
    {
        var request = Request();
        var results = AllowedAssessment(OutboundRequestFingerprint.Create(request)).Results.ToList();
        var diagnostics = new List<string>();
        var assessment = new OutboundPolicyAssessment(
            OutboundAssessmentKind.Exact,
            OutboundPolicyOutcome.Allowed,
            results,
            diagnostics,
            OutboundRequestFingerprint.Create(request));

        var approved = new ApprovedReasoningRequest(request, assessment);
        results.Clear();
        diagnostics.Add("changed");

        Assert.Equal(5, approved.Assessment.Results.Count);
        Assert.Empty(approved.Assessment.Diagnostics);
        approved.EnsureIntegrity();
    }

    [Fact]
    public void GateFailure_DoesNotExposeInputOrSecret()
    {
        const string secret = "never-render-this-secret-456";
        var gate = new OutboundRequestGate(new OutboundContextGuard(new FixedSecretValueSource(secret)));

        var exception = Assert.Throws<OutboundSecurityException>(() =>
            gate.ApproveExact(Request(userData: $"password={secret}")));

        var rendered = exception.Message + JsonSerializer.Serialize(exception.Assessment);
        Assert.DoesNotContain(secret, rendered, StringComparison.Ordinal);
        Assert.True(rendered.Length < 2048);
    }

    private static ReasoningRequest Request(
        string userData = "bounded repository facts",
        string systemInstructions = "Use only the selected evidence.") => new(
        ReasoningStage.ArchitectureAnalysis,
        "test-model",
        systemInstructions,
        userData,
        500,
        100);

    private static OutboundPolicyAssessment AllowedAssessment(string fingerprint) => new(
        OutboundAssessmentKind.Exact,
        OutboundPolicyOutcome.Allowed,
        SystemSecurityPolicyCatalog.Definitions.Select(definition => new OutboundPolicyResult(
            definition.PolicyId,
            definition.PolicyVersion,
            definition.Category,
            OutboundPolicyOutcome.Allowed,
            null,
            definition.AllowedDiagnostic,
            0,
            false)).ToArray(),
        [],
        fingerprint);

    private sealed class FixedSecretValueSource(params string[] values) : IOutboundSecretValueSource
    {
        public IReadOnlySet<string> GetValues() => new HashSet<string>(values, StringComparer.Ordinal);
    }
}
