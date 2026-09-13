using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed record SystemSecurityPolicyDefinition(
    string PolicyId,
    int PolicyVersion,
    PolicyContentScope Category,
    PolicySeverity Severity,
    string AllowedDiagnostic,
    string BlockedDiagnostic,
    string UnknownDiagnostic);

public static class SystemSecurityPolicyCatalog
{
    public const string RemoteCompleteRepositoryId = "SYS_REMOTE_COMPLETE_REPOSITORY";
    public const string RemoteRawSnapshotId = "SYS_REMOTE_RAW_SNAPSHOT";
    public const string RemoteSourceBodiesId = "SYS_REMOTE_SOURCE_BODIES";
    public const string RemoteSecretsId = "SYS_REMOTE_SECRETS";
    public const string RemoteAbsolutePathsId = "SYS_REMOTE_ABSOLUTE_PATHS";

    public static IReadOnlyList<SystemSecurityPolicyDefinition> Definitions { get; } =
        Array.AsReadOnly<SystemSecurityPolicyDefinition>(
        [
            Definition(RemoteCompleteRepositoryId, PolicyContentScope.CompleteRepository, "complete-repository content"),
            Definition(RemoteRawSnapshotId, PolicyContentScope.RawSnapshot, "raw repository snapshots"),
            Definition(RemoteSourceBodiesId, PolicyContentScope.SourceBodies, "source bodies"),
            Definition(RemoteSecretsId, PolicyContentScope.Secrets, "secrets"),
            Definition(RemoteAbsolutePathsId, PolicyContentScope.AbsoluteLocalPaths, "absolute local paths")
        ]);

    private static SystemSecurityPolicyDefinition Definition(
        string policyId,
        PolicyContentScope category,
        string description) => new(
            policyId,
            1,
            category,
            PolicySeverity.Block,
            $"The outbound content does not include {description}.",
            $"The outbound content includes prohibited {description}.",
            $"The outbound content could not be classified for {description}.");
}
