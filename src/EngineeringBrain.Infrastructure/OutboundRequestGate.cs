using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class OutboundSecurityException : IOException
{
    internal OutboundSecurityException(
        string code,
        string message,
        OutboundPolicyAssessment assessment)
        : base(message)
    {
        Code = code;
        Assessment = assessment;
    }

    public string Code { get; }

    public OutboundPolicyAssessment Assessment { get; }

    internal static OutboundSecurityException Blocked(OutboundPolicyAssessment assessment)
    {
        var code = assessment.Results.Any(result =>
            result.ReasonCode == OutboundInspectionReasonCode.ContextRepresentationMismatch)
            ? "OUTBOUND_CONTEXT_INVALID"
            : assessment.Diagnostics.Contains("OUTBOUND_POLICY_CATALOG_INVALID", StringComparer.Ordinal)
                ? "OUTBOUND_POLICY_CATALOG_INVALID"
                : "OUTBOUND_POLICY_BLOCKED";
        return new OutboundSecurityException(
            code,
            "Outbound request was blocked by local security policy. Prohibited values were not retained.",
            assessment);
    }

    internal static OutboundSecurityException IntegrityFailure(OutboundPolicyAssessment assessment) => new(
        "OUTBOUND_REQUEST_INTEGRITY_INVALID",
        "Approved outbound request integrity validation failed.",
        assessment);
}

public sealed class OutboundRequestGate
{
    private readonly OutboundContextGuard _guard;
    private readonly OutboundPolicyEvaluator _evaluator;

    public OutboundRequestGate(
        OutboundContextGuard? guard = null,
        OutboundPolicyEvaluator? evaluator = null)
    {
        _guard = guard ?? new OutboundContextGuard();
        _evaluator = evaluator ?? new OutboundPolicyEvaluator();
    }

    public OutboundPolicyAssessment AssessProjected(
        ReasoningRequest request,
        InitiativeContext? context = null) => Assess(
            request,
            context,
            OutboundAssessmentKind.Projected);

    public ApprovedReasoningRequest ApproveExact(
        ReasoningRequest request,
        InitiativeContext? context = null)
    {
        var assessment = Assess(request, context, OutboundAssessmentKind.Exact);
        if (!assessment.IsAllowed)
        {
            throw OutboundSecurityException.Blocked(assessment);
        }

        var approved = new ApprovedReasoningRequest(request, assessment);
        approved.EnsureIntegrity();
        return approved;
    }

    private OutboundPolicyAssessment Assess(
        ReasoningRequest request,
        InitiativeContext? context,
        OutboundAssessmentKind assessmentKind)
    {
        ArgumentNullException.ThrowIfNull(request);
        var findings = _guard.Inspect(request, context);
        if (findings.Count > 0)
        {
            return _evaluator.Evaluate(assessmentKind, null, findings);
        }

        var fingerprint = OutboundRequestFingerprint.Create(request);
        return _evaluator.Evaluate(assessmentKind, fingerprint, findings);
    }
}
