using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public interface IRecommendationPolicy
{
    PolicyComplianceResult Evaluate(AnalysisRecommendation recommendation);
}

public static class SystemPolicyCatalog
{
    public const string RemoteCompleteRepositoryId = SystemSecurityPolicyCatalog.RemoteCompleteRepositoryId;
    public const string RemoteRawSnapshotId = SystemSecurityPolicyCatalog.RemoteRawSnapshotId;
    public const string RemoteSourceBodiesId = SystemSecurityPolicyCatalog.RemoteSourceBodiesId;
    public const string RemoteSecretsId = SystemSecurityPolicyCatalog.RemoteSecretsId;
    public const string RemoteAbsolutePathsId = SystemSecurityPolicyCatalog.RemoteAbsolutePathsId;

    public static IReadOnlyList<IRecommendationPolicy> Policies { get; } =
        Array.AsReadOnly<IRecommendationPolicy>(SystemSecurityPolicyCatalog.Definitions
            .Select(definition => new RemoteProhibitedContentPolicy(definition))
            .ToArray());

    private sealed class RemoteProhibitedContentPolicy(SystemSecurityPolicyDefinition definition)
        : IRecommendationPolicy
    {
        private static readonly PolicyProvenance Provenance = new(
            "Engineering Brain",
            nameof(SystemPolicyCatalog),
            null,
            null,
            null,
            null,
            null);

        public PolicyComplianceResult Evaluate(AnalysisRecommendation recommendation)
        {
            var actions = recommendation.PolicyRelevantActions
                .Where(action => action.Operation == PolicyActionOperation.RemoteTransmission)
                .ToArray();
            if (actions.Length == 0)
            {
                return Result(
                    PolicyComplianceStatus.NotApplicable,
                    "The recommendation does not declare a remote-transmission action.");
            }

            if (actions.Any(action => action.Boundary == PolicyActionBoundary.Remote
                && action.ContentScope == definition.Category))
            {
                return Result(PolicyComplianceStatus.Violated, definition.BlockedDiagnostic);
            }

            if (actions.Any(action => action.Boundary == PolicyActionBoundary.Unknown
                || action.Boundary == PolicyActionBoundary.Remote
                && action.ContentScope == PolicyContentScope.Unknown))
            {
                return Result(PolicyComplianceStatus.Unknown, definition.UnknownDiagnostic);
            }

            if (actions.Any(action => action.Boundary == PolicyActionBoundary.Remote))
            {
                return Result(PolicyComplianceStatus.Compliant, definition.AllowedDiagnostic);
            }

            return Result(
                PolicyComplianceStatus.NotApplicable,
                "The declared transmission does not cross a remote boundary.");
        }

        private PolicyComplianceResult Result(PolicyComplianceStatus status, string diagnostic) => new(
            definition.PolicyId,
            definition.PolicyVersion,
            PolicySourceKind.System,
            definition.Severity,
            status,
            diagnostic,
            Provenance);
    }
}

public sealed class PolicyComplianceValidator
{
    private readonly IReadOnlyList<IRecommendationPolicy> _policies;

    public PolicyComplianceValidator(IEnumerable<IRecommendationPolicy>? policies = null)
    {
        _policies = policies?.ToArray() ?? SystemPolicyCatalog.Policies;
    }

    public PolicyGovernanceResult Evaluate(IReadOnlyList<ValidatedRecommendation> recommendations)
    {
        ArgumentNullException.ThrowIfNull(recommendations);
        var governed = recommendations.Select(Evaluate).ToArray();
        return new PolicyGovernanceResult(governed, Aggregate(governed));
    }

    public GovernedRecommendation Evaluate(ValidatedRecommendation recommendation)
    {
        ArgumentNullException.ThrowIfNull(recommendation);
        var results = _policies.Select(policy => policy.Evaluate(recommendation.Recommendation)).ToArray();
        return new GovernedRecommendation(recommendation, results, Disposition(recommendation, results));
    }

    private static RecommendationDisposition Disposition(
        ValidatedRecommendation recommendation,
        IReadOnlyList<PolicyComplianceResult> results)
    {
        if (results.Any(IsBlockingViolation))
        {
            return RecommendationDisposition.Rejected;
        }

        if (recommendation.ValidationStatus != EvidenceValidationStatus.Validated
            || results.Any(result => result.ComplianceStatus == PolicyComplianceStatus.Unknown))
        {
            return RecommendationDisposition.NeedsReview;
        }

        return RecommendationDisposition.Accepted;
    }

    private static PolicyOutcome Aggregate(IReadOnlyList<GovernedRecommendation> recommendations)
    {
        var results = recommendations.SelectMany(recommendation => recommendation.PolicyResults).ToArray();
        if (results.Any(IsBlockingViolation))
        {
            return PolicyOutcome.Blocked;
        }

        if (results.Any(result => result.ComplianceStatus == PolicyComplianceStatus.Unknown))
        {
            return PolicyOutcome.Unknown;
        }

        if (results.Any(result => result.Severity == PolicySeverity.Warn
            && result.ComplianceStatus == PolicyComplianceStatus.Violated))
        {
            return PolicyOutcome.Warning;
        }

        return PolicyOutcome.Allowed;
    }

    private static bool IsBlockingViolation(PolicyComplianceResult result) =>
        result.Severity == PolicySeverity.Block
        && result.ComplianceStatus == PolicyComplianceStatus.Violated;
}
