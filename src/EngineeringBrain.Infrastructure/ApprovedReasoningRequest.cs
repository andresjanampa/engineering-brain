using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class ApprovedReasoningRequest
{
    internal ApprovedReasoningRequest(
        ReasoningRequest request,
        OutboundPolicyAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(assessment);
        Request = request;
        Assessment = assessment with
        {
            Results = Array.AsReadOnly(assessment.Results.ToArray()),
            Diagnostics = Array.AsReadOnly(assessment.Diagnostics.ToArray())
        };
    }

    public ReasoningRequest Request { get; }

    public OutboundPolicyAssessment Assessment { get; }

    internal void EnsureIntegrity()
    {
        var definitions = SystemSecurityPolicyCatalog.Definitions;
        var validResults = Assessment.Results.Count == definitions.Count
            && Assessment.Results.Select(result => result.PolicyId).Distinct(StringComparer.Ordinal).Count() == definitions.Count
            && Assessment.Results.Select((result, index) => IsExpectedAllowedResult(result, definitions[index])).All(value => value);
        var expectedFingerprint = OutboundRequestFingerprint.Create(Request);
        if (Assessment.AssessmentKind != OutboundAssessmentKind.Exact
            || Assessment.OverallOutcome != OutboundPolicyOutcome.Allowed
            || !Assessment.IsAllowed
            || !validResults
            || Assessment.Diagnostics.Count != 0
            || string.IsNullOrEmpty(Assessment.PayloadFingerprint)
            || !Assessment.PayloadFingerprint.Equals(expectedFingerprint, StringComparison.Ordinal))
        {
            throw OutboundSecurityException.IntegrityFailure(Assessment);
        }
    }

    private static bool IsExpectedAllowedResult(
        OutboundPolicyResult result,
        SystemSecurityPolicyDefinition definition) =>
        result.PolicyId.Equals(definition.PolicyId, StringComparison.Ordinal)
        && result.PolicyVersion == definition.PolicyVersion
        && result.Category == definition.Category
        && result.Outcome == OutboundPolicyOutcome.Allowed
        && result.ReasonCode is null
        && result.SafeDiagnostic.Equals(definition.AllowedDiagnostic, StringComparison.Ordinal)
        && result.FindingCount == 0
        && !result.FindingCountCapped;
}
