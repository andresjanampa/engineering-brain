using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class OutboundPolicyEvaluatorTests
{
    [Fact]
    public void Evaluate_NoFindingsReturnsFiveAllowedResultsInCatalogOrder()
    {
        var result = new OutboundPolicyEvaluator().Evaluate(
            OutboundAssessmentKind.Exact,
            "fingerprint",
            []);

        Assert.True(result.IsAllowed);
        Assert.Equal(OutboundPolicyOutcome.Allowed, result.OverallOutcome);
        Assert.Equal(
            SystemSecurityPolicyCatalog.Definitions.Select(definition => definition.PolicyId),
            result.Results.Select(policy => policy.PolicyId));
        Assert.All(result.Results, policy => Assert.Equal(OutboundPolicyOutcome.Allowed, policy.Outcome));
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Evaluate_OneFindingBlocksOnlyMatchingPolicyAndOverallAssessment()
    {
        var result = new OutboundPolicyEvaluator().Evaluate(
            OutboundAssessmentKind.Exact,
            null,
            [Finding(PolicyContentScope.Secrets)]);

        Assert.False(result.IsAllowed);
        Assert.Equal(OutboundPolicyOutcome.Blocked, result.OverallOutcome);
        Assert.Null(result.PayloadFingerprint);
        Assert.Equal(
            OutboundPolicyOutcome.Blocked,
            Assert.Single(result.Results, policy => policy.Category == PolicyContentScope.Secrets).Outcome);
        Assert.All(
            result.Results.Where(policy => policy.Category != PolicyContentScope.Secrets),
            policy => Assert.Equal(OutboundPolicyOutcome.Allowed, policy.Outcome));
    }

    [Fact]
    public void Evaluate_MultipleFindingsReportsAllFiveAtMostOnce()
    {
        var findings = SystemSecurityPolicyCatalog.Definitions
            .SelectMany(definition => new[] { Finding(definition.Category), Finding(definition.Category) })
            .ToArray();

        var result = new OutboundPolicyEvaluator().Evaluate(OutboundAssessmentKind.Projected, null, findings);

        Assert.Equal(5, result.Results.Count);
        Assert.Equal(5, result.Results.Select(policy => policy.PolicyId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(5, result.Diagnostics.Count);
        Assert.All(result.Results, policy =>
        {
            Assert.Equal(OutboundPolicyOutcome.Blocked, policy.Outcome);
            Assert.Equal(2, policy.FindingCount);
        });
    }

    [Fact]
    public void Evaluate_DuplicateMissingOrUnknownRequiredPolicyFailsClosed()
    {
        var valid = SystemSecurityPolicyCatalog.Definitions;
        var catalogs = new IReadOnlyList<SystemSecurityPolicyDefinition>[]
        {
            [valid[0], valid[0], .. valid.Skip(2)],
            [.. valid.Skip(1)],
            [.. valid, valid[0] with { PolicyId = "SYS_UNKNOWN", Category = PolicyContentScope.Unknown }],
            [valid[0] with { AllowedDiagnostic = "untrusted diagnostic" }, .. valid.Skip(1)]
        };

        foreach (var catalog in catalogs)
        {
            var result = new OutboundPolicyEvaluator(catalog).Evaluate(
                OutboundAssessmentKind.Exact,
                "fingerprint",
                []);

            Assert.Equal(OutboundPolicyOutcome.Blocked, result.OverallOutcome);
            Assert.False(result.IsAllowed);
            Assert.Null(result.PayloadFingerprint);
            Assert.Equal(["OUTBOUND_POLICY_CATALOG_INVALID"], result.Diagnostics);
        }
    }

    [Fact]
    public void Evaluate_DiagnosticsAreFixedBoundedAndContainNoTriggerValue()
    {
        const string prohibitedValue = "never-retain-this-secret";
        var findings = Enumerable.Repeat(Finding(PolicyContentScope.Secrets, 80), 4).ToArray();

        var result = new OutboundPolicyEvaluator().Evaluate(OutboundAssessmentKind.Exact, null, findings);

        Assert.True(result.Results.Count <= OutboundPolicyEvaluator.MaximumPolicyResults);
        Assert.True(result.Diagnostics.Count <= OutboundPolicyEvaluator.MaximumDiagnostics);
        var policy = Assert.Single(result.Results, value => value.Category == PolicyContentScope.Secrets);
        Assert.Equal(99, policy.FindingCount);
        Assert.True(policy.FindingCountCapped);
        Assert.DoesNotContain(prohibitedValue, string.Join('\n', result.Diagnostics), StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_NotRecordedCannotBeRequestedAsAComputedAssessment()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OutboundPolicyEvaluator().Evaluate(
            OutboundAssessmentKind.NotRecorded,
            null,
            []));
    }

    [Fact]
    public void Evaluate_AllowedAssessmentWithoutFingerprintFailsClosed()
    {
        var result = new OutboundPolicyEvaluator().Evaluate(OutboundAssessmentKind.Exact, null, []);

        Assert.Equal(OutboundPolicyOutcome.Blocked, result.OverallOutcome);
        Assert.False(result.IsAllowed);
        Assert.Null(result.PayloadFingerprint);
        Assert.Contains("OUTBOUND_REQUEST_FINGERPRINT_REQUIRED", result.Diagnostics);
    }

    private static OutboundInspectionFinding Finding(PolicyContentScope category, int count = 1) => new(
        category,
        category == PolicyContentScope.Secrets
            ? OutboundInspectionReasonCode.KnownSecretValue
            : OutboundInspectionReasonCode.InspectionFailure,
        OutboundTriggerKind.ContentPattern,
        count,
        false);
}
