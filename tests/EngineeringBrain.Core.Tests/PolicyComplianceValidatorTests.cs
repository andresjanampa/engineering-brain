using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class PolicyComplianceValidatorTests
{
    [Fact]
    public void Evaluate_RemoteCompleteRepositoryIsBlockedAndOriginalIsPreserved()
    {
        var original = Recommendation(
            EvidenceValidationStatus.Validated,
            Action(PolicyContentScope.CompleteRepository));

        var result = new PolicyComplianceValidator().Evaluate([original]);

        var governed = Assert.Single(result.Recommendations);
        var policy = Assert.Single(
            governed.PolicyResults,
            value => value.PolicyId == SystemPolicyCatalog.RemoteCompleteRepositoryId);
        Assert.Equal(SystemPolicyCatalog.RemoteCompleteRepositoryId, policy.PolicyId);
        Assert.Equal(1, policy.PolicyVersion);
        Assert.Equal(PolicySourceKind.System, policy.Source);
        Assert.Equal(PolicySeverity.Block, policy.Severity);
        Assert.Equal(PolicyComplianceStatus.Violated, policy.ComplianceStatus);
        Assert.Equal("Engineering Brain", policy.Provenance.Authority);
        Assert.Equal(RecommendationDisposition.Rejected, governed.Disposition);
        Assert.Equal(PolicyOutcome.Blocked, result.Outcome);
        Assert.Same(original, governed.ValidatedRecommendation);
    }

    [Fact]
    public void Evaluate_BoundedRemoteFactsAreCompliantAndAccepted()
    {
        var result = Evaluate(
            EvidenceValidationStatus.Validated,
            Action(PolicyContentScope.BoundedFacts, PolicyAuthorizationMode.Explicit));

        Assert.Equal(5, Assert.Single(result.Recommendations).PolicyResults.Count);
        Assert.All(Assert.Single(result.Recommendations).PolicyResults, policy =>
            Assert.Equal(PolicyComplianceStatus.Compliant, policy.ComplianceStatus));
        Assert.Equal(RecommendationDisposition.Accepted, Disposition(result));
        Assert.Equal(PolicyOutcome.Allowed, result.Outcome);
    }

    [Fact]
    public void Evaluate_UnrelatedActionIsNotApplicable()
    {
        var action = new PolicyRelevantAction(
            PolicyActionOperation.ModifyComponent,
            PolicyActionBoundary.Local,
            PolicyContentScope.None,
            PolicyAuthorizationMode.NotApplicable,
            "entity:component",
            "project:core");

        var result = Evaluate(EvidenceValidationStatus.Validated, action);

        Assert.All(Assert.Single(result.Recommendations).PolicyResults, policy =>
            Assert.Equal(PolicyComplianceStatus.NotApplicable, policy.ComplianceStatus));
        Assert.Equal(RecommendationDisposition.Accepted, Disposition(result));
    }

    [Fact]
    public void Evaluate_RelevantUnknownNeedsReview()
    {
        var result = Evaluate(EvidenceValidationStatus.Validated, Action(PolicyContentScope.Unknown));

        Assert.Equal(PolicyComplianceStatus.Unknown, Policy(result));
        Assert.Equal(RecommendationDisposition.NeedsReview, Disposition(result));
        Assert.Equal(PolicyOutcome.Unknown, result.Outcome);
    }

    [Fact]
    public void Evaluate_IrrelevantUnknownDoesNotForceReview()
    {
        var action = new PolicyRelevantAction(
            PolicyActionOperation.ModifyComponent,
            PolicyActionBoundary.Local,
            PolicyContentScope.None,
            PolicyAuthorizationMode.Unknown,
            "entity:component",
            null);

        var result = Evaluate(EvidenceValidationStatus.Validated, action);

        Assert.All(Assert.Single(result.Recommendations).PolicyResults, policy =>
            Assert.Equal(PolicyComplianceStatus.NotApplicable, policy.ComplianceStatus));
        Assert.Equal(RecommendationDisposition.Accepted, Disposition(result));
        Assert.Equal(PolicyOutcome.Allowed, result.Outcome);
    }

    [Fact]
    public void Evaluate_ProposalViolationIsRejectedButCompliantProposalNeedsReview()
    {
        var blocked = Evaluate(EvidenceValidationStatus.Proposal, Action(PolicyContentScope.CompleteRepository));
        var compliant = Evaluate(EvidenceValidationStatus.Proposal, Action(PolicyContentScope.BoundedFacts));

        Assert.Equal(RecommendationDisposition.Rejected, Disposition(blocked));
        Assert.Equal(RecommendationDisposition.NeedsReview, Disposition(compliant));
    }

    [Fact]
    public void Evaluate_InvalidAndPartiallyValidatedEvidenceNeedReviewWithoutBlock()
    {
        var invalid = Evaluate(EvidenceValidationStatus.Invalid);
        var partial = Evaluate(EvidenceValidationStatus.PartiallyValidated);

        Assert.Equal(RecommendationDisposition.NeedsReview, Disposition(invalid));
        Assert.Equal(RecommendationDisposition.NeedsReview, Disposition(partial));
    }

    [Fact]
    public void Evaluate_WarnViolationIsAcceptedAndAggregatesWarning()
    {
        var validator = new PolicyComplianceValidator([new FixedPolicy(PolicySeverity.Warn, PolicyComplianceStatus.Violated)]);

        var result = validator.Evaluate([Recommendation(EvidenceValidationStatus.Validated)]);

        Assert.Equal(RecommendationDisposition.Accepted, Disposition(result));
        Assert.Equal(PolicyOutcome.Warning, result.Outcome);
    }

    [Fact]
    public void Evaluate_EmptyActionsAreValidAndAllowed()
    {
        var result = Evaluate(EvidenceValidationStatus.Validated);

        Assert.Empty(result.Recommendations[0].ValidatedRecommendation.Recommendation.PolicyRelevantActions);
        Assert.All(Assert.Single(result.Recommendations).PolicyResults, policy =>
            Assert.Equal(PolicyComplianceStatus.NotApplicable, policy.ComplianceStatus));
        Assert.Equal(RecommendationDisposition.Accepted, Disposition(result));
        Assert.Equal(PolicyOutcome.Allowed, result.Outcome);
    }

    [Fact]
    public void Evaluate_BlockTakesPrecedenceAndUnknownPrecedesWarning()
    {
        var recommendation = Recommendation(EvidenceValidationStatus.Validated);
        var blocked = new PolicyComplianceValidator([
            new FixedPolicy(PolicySeverity.Warn, PolicyComplianceStatus.Violated),
            new FixedPolicy(PolicySeverity.Block, PolicyComplianceStatus.Unknown),
            new FixedPolicy(PolicySeverity.Block, PolicyComplianceStatus.Violated)
        ]).Evaluate([recommendation]);
        var unknown = new PolicyComplianceValidator([
            new FixedPolicy(PolicySeverity.Warn, PolicyComplianceStatus.Violated),
            new FixedPolicy(PolicySeverity.Block, PolicyComplianceStatus.Unknown)
        ]).Evaluate([recommendation]);

        Assert.Equal(PolicyOutcome.Blocked, blocked.Outcome);
        Assert.Equal(RecommendationDisposition.Rejected, Disposition(blocked));
        Assert.Equal(PolicyOutcome.Unknown, unknown.Outcome);
        Assert.Equal(RecommendationDisposition.NeedsReview, Disposition(unknown));
    }

    [Theory]
    [InlineData(PolicyContentScope.CompleteRepository, "SYS_REMOTE_COMPLETE_REPOSITORY")]
    [InlineData(PolicyContentScope.RawSnapshot, "SYS_REMOTE_RAW_SNAPSHOT")]
    [InlineData(PolicyContentScope.SourceBodies, "SYS_REMOTE_SOURCE_BODIES")]
    [InlineData(PolicyContentScope.Secrets, "SYS_REMOTE_SECRETS")]
    [InlineData(PolicyContentScope.AbsoluteLocalPaths, "SYS_REMOTE_ABSOLUTE_PATHS")]
    public void Evaluate_RemoteProhibitedScopeIsRejectedByMatchingPolicy(
        PolicyContentScope scope,
        string policyId)
    {
        var result = Evaluate(EvidenceValidationStatus.Validated, Action(scope));
        var recommendation = Assert.Single(result.Recommendations);
        var matching = Assert.Single(recommendation.PolicyResults, policy => policy.PolicyId == policyId);

        Assert.Equal(PolicyComplianceStatus.Violated, matching.ComplianceStatus);
        Assert.Equal(RecommendationDisposition.Rejected, recommendation.Disposition);
        Assert.Equal(PolicyOutcome.Blocked, result.Outcome);
        Assert.All(
            recommendation.PolicyResults.Where(policy => policy.PolicyId != policyId),
            policy => Assert.Equal(PolicyComplianceStatus.Compliant, policy.ComplianceStatus));
    }

    private static PolicyGovernanceResult Evaluate(
        EvidenceValidationStatus status,
        params PolicyRelevantAction[] actions) =>
        new PolicyComplianceValidator().Evaluate([Recommendation(status, actions)]);

    private static ValidatedRecommendation Recommendation(
        EvidenceValidationStatus status,
        params PolicyRelevantAction[] actions)
    {
        var recommendation = new AnalysisRecommendation(
            RecommendationDecision.Reuse,
            "Subject",
            "Free-form text is not used for policy enforcement.",
            status == EvidenceValidationStatus.Proposal ? EpistemicStatus.Proposal : EpistemicStatus.Inference,
            [],
            [],
            [],
            actions);
        return new ValidatedRecommendation(recommendation, status, [], []);
    }

    private static PolicyRelevantAction Action(
        PolicyContentScope contentScope,
        PolicyAuthorizationMode authorization = PolicyAuthorizationMode.Unknown) => new(
        PolicyActionOperation.RemoteTransmission,
        PolicyActionBoundary.Remote,
        contentScope,
        authorization,
        null,
        null);

    private static PolicyComplianceStatus Policy(PolicyGovernanceResult result) =>
        Assert.Single(
            Assert.Single(result.Recommendations).PolicyResults,
            policy => policy.PolicyId == SystemPolicyCatalog.RemoteCompleteRepositoryId).ComplianceStatus;

    private static RecommendationDisposition Disposition(PolicyGovernanceResult result) =>
        Assert.Single(result.Recommendations).Disposition;

    private sealed class FixedPolicy(PolicySeverity severity, PolicyComplianceStatus status) : IRecommendationPolicy
    {
        public PolicyComplianceResult Evaluate(AnalysisRecommendation recommendation) => new(
            $"TEST_{severity}_{status}",
            1,
            PolicySourceKind.System,
            severity,
            status,
            "Test-only policy result.",
            new PolicyProvenance("Tests", nameof(FixedPolicy), null, null, null, null, null));
    }
}
