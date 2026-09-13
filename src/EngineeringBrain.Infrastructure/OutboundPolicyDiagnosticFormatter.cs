using System.Text;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public static class OutboundPolicyDiagnosticFormatter
{
    public const int MaximumLineLength = 512;

    private static readonly IReadOnlySet<string> KnownDiagnostics = BuildKnownDiagnostics();

    public static IReadOnlyList<string> Format(OutboundPolicyAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(assessment);

        var lines = new List<string>
        {
            SafeLine($"Assessment: {assessment.AssessmentKind}")
        };

        foreach (var definition in SystemSecurityPolicyCatalog.Definitions)
        {
            var result = assessment.Results.FirstOrDefault(candidate =>
                candidate.PolicyId.Equals(definition.PolicyId, StringComparison.Ordinal)
                && candidate.Category == definition.Category);
            if (result is null)
            {
                continue;
            }

            var diagnostic = result.Outcome switch
            {
                OutboundPolicyOutcome.Allowed => definition.AllowedDiagnostic,
                OutboundPolicyOutcome.Blocked => definition.BlockedDiagnostic,
                _ => definition.UnknownDiagnostic
            };
            lines.Add(SafeLine($"{definition.PolicyId} v{definition.PolicyVersion}: {result.Outcome} - {diagnostic}"));
        }

        foreach (var diagnostic in assessment.Diagnostics.Take(OutboundPolicyEvaluator.MaximumDiagnostics))
        {
            var safeDiagnostic = KnownDiagnostics.Contains(diagnostic)
                ? diagnostic
                : "OUTBOUND_POLICY_DIAGNOSTIC_INVALID";
            lines.Add(SafeLine($"Diagnostic: {safeDiagnostic}"));
        }

        lines.Add(SafeLine($"Overall: {assessment.OverallOutcome}"));
        return lines.AsReadOnly();
    }

    private static string SafeLine(string value)
    {
        var builder = new StringBuilder(Math.Min(value.Length, MaximumLineLength));
        foreach (var character in value)
        {
            if (builder.Length == MaximumLineLength)
            {
                break;
            }

            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        return builder.ToString();
    }

    private static IReadOnlySet<string> BuildKnownDiagnostics()
    {
        var diagnostics = new HashSet<string>(StringComparer.Ordinal)
        {
            "OUTBOUND_POLICY_CATALOG_INVALID",
            "OUTBOUND_POLICY_EVALUATION_FAILED",
            "OUTBOUND_REQUEST_FINGERPRINT_REQUIRED"
        };
        foreach (var definition in SystemSecurityPolicyCatalog.Definitions)
        {
            diagnostics.Add(definition.AllowedDiagnostic);
            diagnostics.Add(definition.BlockedDiagnostic);
            diagnostics.Add(definition.UnknownDiagnostic);
        }

        return diagnostics;
    }
}
