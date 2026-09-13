using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class OutboundSecurityModelTests
{
    [Fact]
    public void Format_AssessmentShowsPolicyIdOutcomeAndAssessmentKind()
    {
        var assessment = new OutboundPolicyEvaluator().Evaluate(
            OutboundAssessmentKind.Exact,
            "fingerprint",
            []);

        var lines = OutboundPolicyDiagnosticFormatter.Format(assessment);

        Assert.Equal("Assessment: Exact", lines[0]);
        Assert.Contains(lines, line => line.Contains(
            "SYS_REMOTE_COMPLETE_REPOSITORY v1: Allowed",
            StringComparison.Ordinal));
        Assert.Equal("Overall: Allowed", lines[^1]);
    }

    [Fact]
    public void Format_NotRecordedNeverRendersAllowed()
    {
        var lines = OutboundPolicyDiagnosticFormatter.Format(OutboundPolicyAssessment.NotRecorded);

        Assert.Equal(["Assessment: NotRecorded", "Overall: NotRecorded"], lines);
        Assert.DoesNotContain(lines, line => line.Contains("Allowed", StringComparison.Ordinal));
    }

    [Fact]
    public void Format_DiagnosticsAreSingleLineAndBounded()
    {
        var assessment = new OutboundPolicyAssessment(
            OutboundAssessmentKind.Exact,
            OutboundPolicyOutcome.Blocked,
            [],
            [$"OUTBOUND_POLICY_EVALUATION_FAILED\r\n\t{new string('x', 1024)}"],
            null);

        var lines = OutboundPolicyDiagnosticFormatter.Format(assessment);

        Assert.All(lines, line =>
        {
            Assert.DoesNotContain('\r', line);
            Assert.DoesNotContain('\n', line);
            Assert.DoesNotContain('\t', line);
            Assert.InRange(line.Length, 1, OutboundPolicyDiagnosticFormatter.MaximumLineLength);
        });
    }

    [Fact]
    public void NotRecorded_IsExplicitAndNeverAllowed()
    {
        var value = OutboundPolicyAssessment.NotRecorded;

        Assert.Equal(OutboundAssessmentKind.NotRecorded, value.AssessmentKind);
        Assert.Equal(OutboundPolicyOutcome.NotRecorded, value.OverallOutcome);
        Assert.False(value.IsAllowed);
        Assert.Empty(value.Results);
        Assert.Empty(value.Diagnostics);
        Assert.Null(value.PayloadFingerprint);
    }

    [Fact]
    public void Finding_CarriesNoTriggeringContent()
    {
        var properties = typeof(OutboundInspectionFinding).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain("Content", properties);
        Assert.DoesNotContain("Value", properties);
        Assert.DoesNotContain("Path", properties);
    }

    [Fact]
    public void AllowedResults_AreAllowedOnlyWhenEveryPolicyAllows()
    {
        var results = Enum.GetValues<PolicyContentScope>()
            .Where(scope => scope is PolicyContentScope.CompleteRepository
                or PolicyContentScope.RawSnapshot
                or PolicyContentScope.SourceBodies
                or PolicyContentScope.Secrets
                or PolicyContentScope.AbsoluteLocalPaths)
            .Select(scope => Result(scope, OutboundPolicyOutcome.Allowed))
            .ToArray();

        var allowed = new OutboundPolicyAssessment(
            OutboundAssessmentKind.Exact,
            OutboundPolicyOutcome.Allowed,
            results,
            [],
            "fingerprint");
        var blocked = allowed with
        {
            OverallOutcome = OutboundPolicyOutcome.Blocked,
            Results = [.. results[..4], Result(PolicyContentScope.AbsoluteLocalPaths, OutboundPolicyOutcome.Blocked)]
        };

        Assert.True(allowed.IsAllowed);
        Assert.False(blocked.IsAllowed);
    }

    [Fact]
    public void Contracts_RepresentAllFrozenEnumValuesAndStructuredFields()
    {
        Assert.Equal(3, Enum.GetValues<OutboundAssessmentKind>().Length);
        Assert.Equal(3, Enum.GetValues<OutboundPolicyOutcome>().Length);
        Assert.Equal(6, Enum.GetValues<OutboundTriggerKind>().Length);
        Assert.Equal(11, Enum.GetValues<OutboundInspectionReasonCode>().Length);

        var finding = new OutboundInspectionFinding(
            PolicyContentScope.Secrets,
            OutboundInspectionReasonCode.KnownSecretValue,
            OutboundTriggerKind.KnownSecretValue,
            99,
            true);

        Assert.Equal(99, finding.FindingCount);
        Assert.True(finding.FindingCountCapped);
    }

    private static OutboundPolicyResult Result(
        PolicyContentScope category,
        OutboundPolicyOutcome outcome) => new(
        $"POLICY_{category}",
        1,
        category,
        outcome,
        outcome == OutboundPolicyOutcome.Blocked ? OutboundInspectionReasonCode.InspectionFailure : null,
        "Safe diagnostic.",
        outcome == OutboundPolicyOutcome.Blocked ? 1 : 0,
        false);
}
