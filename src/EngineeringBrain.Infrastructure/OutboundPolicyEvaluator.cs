using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class OutboundPolicyEvaluator
{
    public const int MaximumPolicyResults = 5;
    public const int MaximumDiagnostics = 5;

    private readonly IReadOnlyList<SystemSecurityPolicyDefinition> _definitions;

    public OutboundPolicyEvaluator(IEnumerable<SystemSecurityPolicyDefinition>? definitions = null)
    {
        _definitions = Array.AsReadOnly((definitions ?? SystemSecurityPolicyCatalog.Definitions).ToArray());
    }

    public OutboundPolicyAssessment Evaluate(
        OutboundAssessmentKind assessmentKind,
        string? payloadFingerprint,
        IReadOnlyList<OutboundInspectionFinding> findings)
    {
        if (assessmentKind == OutboundAssessmentKind.NotRecorded)
        {
            throw new ArgumentOutOfRangeException(nameof(assessmentKind), assessmentKind, "A computed assessment cannot be NotRecorded.");
        }

        try
        {
            if (!HasValidCatalog())
            {
                return Failure(assessmentKind, "OUTBOUND_POLICY_CATALOG_INVALID");
            }

            ArgumentNullException.ThrowIfNull(findings);
            var knownCategories = SystemSecurityPolicyCatalog.Definitions
                .Select(definition => definition.Category)
                .ToHashSet();
            if (findings.Any(finding => !knownCategories.Contains(finding.Category)))
            {
                return Failure(assessmentKind, "OUTBOUND_POLICY_EVALUATION_FAILED");
            }

            var results = _definitions.Select(definition => Evaluate(definition, findings)).ToArray();
            var blocked = results.Any(result => result.Outcome == OutboundPolicyOutcome.Blocked);
            if (!blocked && string.IsNullOrEmpty(payloadFingerprint))
            {
                return Failure(assessmentKind, "OUTBOUND_REQUEST_FINGERPRINT_REQUIRED");
            }

            return new OutboundPolicyAssessment(
                assessmentKind,
                blocked ? OutboundPolicyOutcome.Blocked : OutboundPolicyOutcome.Allowed,
                results,
                blocked
                    ? results.Where(result => result.Outcome == OutboundPolicyOutcome.Blocked)
                        .Select(result => result.SafeDiagnostic)
                        .Take(MaximumDiagnostics)
                        .ToArray()
                    : [],
                blocked ? null : payloadFingerprint);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Failure(assessmentKind, "OUTBOUND_POLICY_EVALUATION_FAILED");
        }
    }

    private bool HasValidCatalog()
    {
        if (_definitions.Count != MaximumPolicyResults)
        {
            return false;
        }

        for (var index = 0; index < SystemSecurityPolicyCatalog.Definitions.Count; index++)
        {
            var expected = SystemSecurityPolicyCatalog.Definitions[index];
            var actual = _definitions[index];
            if (actual != expected)
            {
                return false;
            }
        }

        return _definitions.Select(definition => definition.PolicyId).Distinct(StringComparer.Ordinal).Count() == MaximumPolicyResults
            && _definitions.Select(definition => definition.Category).Distinct().Count() == MaximumPolicyResults;
    }

    private static OutboundPolicyResult Evaluate(
        SystemSecurityPolicyDefinition definition,
        IReadOnlyList<OutboundInspectionFinding> findings)
    {
        var matching = findings.Where(finding => finding.Category == definition.Category).ToArray();
        if (matching.Length == 0)
        {
            return new OutboundPolicyResult(
                definition.PolicyId,
                definition.PolicyVersion,
                definition.Category,
                OutboundPolicyOutcome.Allowed,
                null,
                definition.AllowedDiagnostic,
                0,
                false);
        }

        var total = matching.Aggregate(0L, (current, finding) => current + Math.Max(0, finding.FindingCount));
        var capped = matching.Any(finding => finding.FindingCountCapped) || total > 99;
        return new OutboundPolicyResult(
            definition.PolicyId,
            definition.PolicyVersion,
            definition.Category,
            OutboundPolicyOutcome.Blocked,
            matching[0].ReasonCode,
            definition.BlockedDiagnostic,
            (int)Math.Min(99, total),
            capped);
    }

    private static OutboundPolicyAssessment Failure(OutboundAssessmentKind assessmentKind, string diagnostic) => new(
        assessmentKind,
        OutboundPolicyOutcome.Blocked,
        [],
        [diagnostic],
        null);
}
