namespace EngineeringBrain.Core;

public enum OutboundAssessmentKind
{
    NotRecorded,
    Projected,
    Exact
}

public enum OutboundPolicyOutcome
{
    NotRecorded,
    Allowed,
    Blocked
}

public enum OutboundTriggerKind
{
    PayloadKind,
    ContentPattern,
    KnownSecretValue,
    SourceReference,
    ContextIntegrity,
    OperationalFailure
}

public enum OutboundInspectionReasonCode
{
    CompleteRepositoryPayload,
    RawSnapshotPayload,
    SourceBodyPayload,
    SecretSyntax,
    KnownSecretValue,
    AbsoluteLocalPath,
    InvalidSourceReference,
    ContextRepresentationMismatch,
    PolicyCatalogInvalid,
    InspectionFailure,
    RequestIntegrityMismatch
}

public sealed record OutboundInspectionFinding(
    PolicyContentScope Category,
    OutboundInspectionReasonCode ReasonCode,
    OutboundTriggerKind TriggerKind,
    int FindingCount,
    bool FindingCountCapped);

public sealed record OutboundPolicyResult(
    string PolicyId,
    int PolicyVersion,
    PolicyContentScope Category,
    OutboundPolicyOutcome Outcome,
    OutboundInspectionReasonCode? ReasonCode,
    string SafeDiagnostic,
    int FindingCount,
    bool FindingCountCapped);

public sealed record OutboundPolicyAssessment(
    OutboundAssessmentKind AssessmentKind,
    OutboundPolicyOutcome OverallOutcome,
    IReadOnlyList<OutboundPolicyResult> Results,
    IReadOnlyList<string> Diagnostics,
    string? PayloadFingerprint)
{
    public bool IsAllowed => AssessmentKind is not OutboundAssessmentKind.NotRecorded
        && OverallOutcome == OutboundPolicyOutcome.Allowed;

    public static OutboundPolicyAssessment NotRecorded { get; } = new(
        OutboundAssessmentKind.NotRecorded,
        OutboundPolicyOutcome.NotRecorded,
        [],
        [],
        null);
}
