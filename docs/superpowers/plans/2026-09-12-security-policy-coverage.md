# Complete Security Policy Coverage V1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Enforce five explicit outbound BLOCK policies so no repository- or initiative-derived model input reaches a remote provider unless the exact request was deterministically inspected and approved.

**Architecture:** A shared immutable system catalog defines policy identity while `OutboundContextGuard` detects concrete facts and `OutboundPolicyEvaluator` makes outbound decisions. `OutboundRequestGate` is the only production creator of non-publicly-constructible `ApprovedReasoningRequest` objects; providers accept that approved type, projected previews never produce transport authority, and exact assessments travel with the exact request sent.

**Tech Stack:** .NET 10, C# records/classes, `System.Text.Json`, SHA-256, generated regular expressions, xUnit, local filesystem only.

**Spec:** `docs/superpowers/specs/2026-09-12-security-policy-coverage-design.md`

## Global Constraints

- Cover exactly `CompleteRepository`, `RawSnapshot`, `SourceBodies`, `Secrets`, and `AbsoluteLocalPaths`; every V1 policy has severity `Block`.
- Keep `SYS_REMOTE_COMPLETE_REPOSITORY` unchanged and add `SYS_REMOTE_RAW_SNAPSHOT`, `SYS_REMOTE_SOURCE_BODIES`, `SYS_REMOTE_SECRETS`, and `SYS_REMOTE_ABSOLUTE_PATHS`.
- Do not add sanitization, redaction of outbound payloads, configurable policy language, semantic secret classification, archive/encoded inspection, source excerpt support, UI, remote policy services, provider plugins, embeddings, or network calls in tests.
- Inspect `SystemInstructions`, `UserData`, structured context, and every other repository- or initiative-derived model-visible field.
- `Projected` assessment never creates an `ApprovedReasoningRequest`; only an allowed `Exact` assessment does.
- `ApprovedReasoningRequest` has no public constructor or public factory. Its internal constructor is callable only from the Infrastructure assembly and the test friend assembly; `OutboundRequestGate` is the sole production creation site.
- Known secret values remain command-scoped and memory-only. Never persist, log, fingerprint, or render a secret value.
- Multiple detections aggregate into at most five policy results and five fixed diagnostics in catalog order. Per-category finding counts cap at `99` with an explicit capped flag.
- Any violation, malformed/unrepresented context, missing/unknown policy, inspection/evaluation failure, or approved-request integrity failure blocks transport. Cancellation propagates.
- Keep outbound payload security distinct from post-response recommendation governance, though both use the same policy definitions.
- Do not modify Code Graph, Project Memory, reviewed concept lifecycle, retrieval, A1/C1/S2/E2, lexical scoring, project ranking, or graph expansion.
- Do not modify `ProjectMemoryModels.cs`, `ProjectMemoryService.cs`, `LocalProjectMemoryStore.cs`, `LocalReviewedConceptWriter.cs`, `ReviewedConceptLifecycleService.cs`, `ConceptCandidateReranker.cs`, or `InitiativeCandidateRetriever.cs`.
- Preserve local-only operation and explicit `--allow-remote` authorization.
- Use test-first red/green development and one focused commit per task. Do not push.
- Final acceptance is fully offline: build with zero warnings/errors; more than 394 tests with zero failed/skipped; retrieval 16/16, Recall@5/10 `1.000`, MRR `0.867`; 29 reviewed profiles; outbound suite 100% expected decisions, zero false allowed, zero diagnostic leaks, and zero blocked provider calls.

## File Map

**New production files**

- `src/EngineeringBrain.Core/OutboundSecurityModels.cs`: serializable category findings, policy results, assessment kind/outcome, and legacy `NotRecorded` semantics.
- `src/EngineeringBrain.Infrastructure/SystemSecurityPolicyCatalog.cs`: five immutable system policy definitions in stable order.
- `src/EngineeringBrain.Infrastructure/OutboundRequestFingerprint.cs`: canonical length-prefixed SHA-256 request fingerprint.
- `src/EngineeringBrain.Infrastructure/EnvironmentOutboundSecretValueSource.cs`: focused in-memory environment-secret selection.
- `src/EngineeringBrain.Infrastructure/OutboundPolicyEvaluator.cs`: deterministic finding-to-policy evaluation.
- `src/EngineeringBrain.Infrastructure/OutboundRequestGate.cs`: projected assessment, exact approval, and fail-closed orchestration.
- `src/EngineeringBrain.Infrastructure/ApprovedReasoningRequest.cs`: immutable transport capability with internal construction.
- `src/EngineeringBrain.Infrastructure/IReasoningProvider.cs`: approved-request-only provider contract moved out of Core.
- `src/EngineeringBrain.Infrastructure/SafeReasoningProviderInvoker.cs`: safe provider exception boundary.
- `src/EngineeringBrain.Infrastructure/OutboundPolicyDiagnosticFormatter.cs`: bounded single-line policy output.
- `src/EngineeringBrain.Infrastructure/Properties/AssemblyInfo.cs`: friend access only for `EngineeringBrain.Core.Tests` adversarial integrity tests.

**New test/evaluation files**

- `tests/EngineeringBrain.Core.Tests/SystemSecurityPolicyCatalogTests.cs`
- `tests/EngineeringBrain.Core.Tests/OutboundSecurityModelTests.cs`
- `tests/EngineeringBrain.Core.Tests/OutboundRequestFingerprintTests.cs`
- `tests/EngineeringBrain.Core.Tests/EnvironmentOutboundSecretValueSourceTests.cs`
- `tests/EngineeringBrain.Core.Tests/OutboundPolicyEvaluatorTests.cs`
- `tests/EngineeringBrain.Core.Tests/OutboundRequestGateTests.cs`
- `tests/EngineeringBrain.Core.Tests/ReasoningProviderBoundaryTests.cs`
- `tests/EngineeringBrain.Core.Tests/SafeReasoningProviderInvokerTests.cs`
- `tests/EngineeringBrain.Core.Tests/OutboundSecurityEvaluationTests.cs`
- `tests/EngineeringBrain.Core.Tests/OutboundSecurityArchitectureTests.cs`
- `evaluations/security-outbound-suite.json`

**Modified production files**

- `src/EngineeringBrain.Core/Abstractions.cs`: remove the raw-request provider interface after its Infrastructure replacement is compiled.
- `src/EngineeringBrain.Core/InitiativeAnalysisModels.cs`: add `CompleteRepository` segment kind and outbound assessments to runtime analysis results.
- `src/EngineeringBrain.Core/EvaluationModels.cs`: retain legacy counters and add projected/exact policy assessment fields.
- `src/EngineeringBrain.Core/LiveEvaluationModels.cs`: persist exact outbound assessments and complete-repository aggregate count.
- `src/EngineeringBrain.Infrastructure/PolicyComplianceValidator.cs`: instantiate recommendation policies from the shared five-rule catalog.
- `src/EngineeringBrain.Infrastructure/OutboundContextGuard.cs`: return bounded structured findings and harden five detectors.
- `src/EngineeringBrain.Infrastructure/RemoteContextPreviewService.cs`: prepare exact CALL #1 plus projected CALL #2 and persist schema 2.
- `src/EngineeringBrain.Infrastructure/InitiativeAnalysisService.cs`: reuse prepared CALL #1, approve actual CALL #2, and record exact assessments.
- `src/EngineeringBrain.Infrastructure/LiveEvaluationService.cs`: remove direct raw sends and use the same request gate/invoker.
- `src/EngineeringBrain.Infrastructure/OpenAIReasoningProvider.cs`: accept only approved requests and verify integrity before SDK construction.
- `src/EngineeringBrain.Infrastructure/FakeLiveReasoningProvider.cs`: consume the approved provider contract.
- `src/EngineeringBrain.Infrastructure/ReasoningProviderException.cs`: stable provider failure codes and fixed safe messages.
- `src/EngineeringBrain.Infrastructure/LocalInitiativeAnalysisStore.cs`: schema 3 writes and schema 2 compatibility.
- `src/EngineeringBrain.Infrastructure/LocalLiveEvaluationStore.cs`: schema 3 writes, schema 2 compatibility, and bounded defense-in-depth formatting.
- `src/EngineeringBrain.Cli/Program.cs`: compose one gate/secret snapshot, pass preparations, and print policy tables.
- Existing Core and analyzer test providers: accept approved requests and read `request.Request`.

**Documentation**

- `docs/decisions/0011-complete-security-policy-coverage.md`
- `README.md`
- `docs/architecture/README.md`

---

### Task 1: Five-Rule System Security Catalog and Recommendation Governance

**Files:**
- Create: `src/EngineeringBrain.Infrastructure/SystemSecurityPolicyCatalog.cs`
- Create: `tests/EngineeringBrain.Core.Tests/SystemSecurityPolicyCatalogTests.cs`
- Modify: `src/EngineeringBrain.Infrastructure/PolicyComplianceValidator.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/PolicyComplianceValidatorTests.cs`

**Interfaces:**
- Consumes: `PolicyContentScope`, `PolicySeverity`, `PolicyComplianceResult`, `IRecommendationPolicy`.
- Produces: `SystemSecurityPolicyDefinition`; `SystemSecurityPolicyCatalog.Definitions`; compatibility constants on `SystemPolicyCatalog`.

- [ ] **Step 1: Write the failing catalog and governance tests**

Add tests named:

```csharp
[Fact] public void Definitions_ContainFiveUniquePoliciesInStableOrder();
[Fact] public void Definitions_MapEveryProhibitedScopeToBlockVersionOne();
[Theory]
[InlineData(PolicyContentScope.CompleteRepository, "SYS_REMOTE_COMPLETE_REPOSITORY")]
[InlineData(PolicyContentScope.RawSnapshot, "SYS_REMOTE_RAW_SNAPSHOT")]
[InlineData(PolicyContentScope.SourceBodies, "SYS_REMOTE_SOURCE_BODIES")]
[InlineData(PolicyContentScope.Secrets, "SYS_REMOTE_SECRETS")]
[InlineData(PolicyContentScope.AbsoluteLocalPaths, "SYS_REMOTE_ABSOLUTE_PATHS")]
public void Evaluate_RemoteProhibitedScopeIsRejectedByMatchingPolicy(
    PolicyContentScope scope,
    string policyId);
```

Keep the existing complete-repository assertions and update assertions that expected one total result to select the result by `PolicyId`. Add a bounded-facts test asserting all five results are compliant or not applicable and the recommendation remains accepted.

- [ ] **Step 2: Run focused tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~SystemSecurityPolicyCatalogTests|FullyQualifiedName~PolicyComplianceValidatorTests"
```

Expected: FAIL at compilation because `SystemSecurityPolicyCatalog` and four policy IDs do not exist.

- [ ] **Step 3: Implement the immutable catalog and parameterized recommendation policy**

Use these exact public shapes:

```csharp
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
    public static IReadOnlyList<SystemSecurityPolicyDefinition> Definitions { get; }
}
```

Initialize `Definitions` once with `Array.AsReadOnly`, in the order above. Refactor `PolicyComplianceValidator` to create one `RemoteProhibitedContentPolicy` per definition. Preserve `SystemPolicyCatalog.RemoteCompleteRepositoryId` as an alias to the new constant and expose the other four aliases for fixture compatibility. Do not add transport inspection to this validator.

- [ ] **Step 4: Run focused and related governance tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~SystemSecurityPolicyCatalogTests|FullyQualifiedName~PolicyComplianceValidatorTests|FullyQualifiedName~LiveEvaluationTests&Name~Policy"
```

Expected: PASS; the old complete-repository policy still rejects the same action and every new scope activates only its matching policy.

- [ ] **Step 5: Commit the catalog boundary**

```powershell
git add src/EngineeringBrain.Infrastructure/SystemSecurityPolicyCatalog.cs src/EngineeringBrain.Infrastructure/PolicyComplianceValidator.cs tests/EngineeringBrain.Core.Tests/SystemSecurityPolicyCatalogTests.cs tests/EngineeringBrain.Core.Tests/PolicyComplianceValidatorTests.cs
git commit -m "feat: define complete outbound security policies"
```

---

### Task 2: Structured Outbound Security Result Models

**Files:**
- Create: `src/EngineeringBrain.Core/OutboundSecurityModels.cs`
- Create: `tests/EngineeringBrain.Core.Tests/OutboundSecurityModelTests.cs`

**Interfaces:**
- Consumes: `PolicyContentScope`.
- Produces: assessment, finding, policy-result, reason, trigger, and outcome contracts used by every later task.

- [ ] **Step 1: Write failing model contract tests**

Add tests that construct all enum values and records, then verify `OutboundPolicyAssessment.NotRecorded` is not allowed:

```csharp
[Fact]
public void NotRecorded_IsExplicitAndNeverAllowed()
{
    var value = OutboundPolicyAssessment.NotRecorded;
    Assert.Equal(OutboundAssessmentKind.NotRecorded, value.AssessmentKind);
    Assert.Equal(OutboundPolicyOutcome.NotRecorded, value.OverallOutcome);
    Assert.False(value.IsAllowed);
    Assert.Empty(value.Results);
}

[Fact]
public void Finding_CarriesNoTriggeringContent()
{
    var properties = typeof(OutboundInspectionFinding).GetProperties().Select(p => p.Name).ToArray();
    Assert.DoesNotContain("Content", properties);
    Assert.DoesNotContain("Value", properties);
    Assert.DoesNotContain("Path", properties);
}
```

Also test that five ordered allowed results produce `IsAllowed == true`, while one blocked result makes it false.

- [ ] **Step 2: Run focused tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~OutboundSecurityModelTests
```

Expected: FAIL because the outbound security model types do not exist.

- [ ] **Step 3: Add exact result contracts**

Define:

```csharp
public enum OutboundAssessmentKind { NotRecorded, Projected, Exact }
public enum OutboundPolicyOutcome { NotRecorded, Allowed, Blocked }
public enum OutboundTriggerKind
{
    PayloadKind, ContentPattern, KnownSecretValue, SourceReference,
    ContextIntegrity, OperationalFailure
}
public enum OutboundInspectionReasonCode
{
    CompleteRepositoryPayload, RawSnapshotPayload, SourceBodyPayload,
    SecretSyntax, KnownSecretValue, AbsoluteLocalPath,
    InvalidSourceReference, ContextRepresentationMismatch,
    PolicyCatalogInvalid, InspectionFailure, RequestIntegrityMismatch
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
        OutboundAssessmentKind.NotRecorded, OutboundPolicyOutcome.NotRecorded, [], [], null);
}
```

Do not add raw evidence text fields.

- [ ] **Step 4: Run focused and Core model tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~OutboundSecurityModelTests|FullyQualifiedName~InitiativeAnalysisModel"
```

Expected: PASS with deterministic enum serialization under the repository's existing JSON options.

- [ ] **Step 5: Commit the result contracts**

```powershell
git add src/EngineeringBrain.Core/OutboundSecurityModels.cs tests/EngineeringBrain.Core.Tests/OutboundSecurityModelTests.cs
git commit -m "feat: define outbound policy assessment models"
```

---

### Task 3: Canonical Provider-Request Fingerprinting

**Files:**
- Create: `src/EngineeringBrain.Infrastructure/OutboundRequestFingerprint.cs`
- Create: `tests/EngineeringBrain.Core.Tests/OutboundRequestFingerprintTests.cs`

**Interfaces:**
- Consumes: `ReasoningRequest`.
- Produces: `OutboundRequestFingerprint.Create(ReasoningRequest)`.

- [ ] **Step 1: Write failing fingerprint tests**

Add exact tests:

```csharp
[Fact] public void Create_IsDeterministicForIdenticalRequest();
[Fact] public void Create_ChangesForStageModelSystemUserOrMaximumOutput();
[Fact] public void Create_IgnoresEstimatedInputTokensBecauseTheyAreNotTransported();
[Fact] public void Create_LengthPrefixesPreventFieldBoundaryCollision();
[Fact] public void Create_PreservesExactLineEndings();
```

The boundary-collision test compares `SystemInstructions="a\nb", UserData="c"` with `SystemInstructions="a", UserData="b\nc"` and requires different hashes.

- [ ] **Step 2: Run focused tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~OutboundRequestFingerprintTests
```

Expected: FAIL because `OutboundRequestFingerprint` does not exist.

- [ ] **Step 3: Implement versioned length-prefixed hashing**

Use SHA-256 and big-endian primitives:

```csharp
public static string Create(ReasoningRequest request)
{
    ArgumentNullException.ThrowIfNull(request);
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    AppendString(hash, "engineering-brain/reasoning-request/v1");
    AppendInt32(hash, (int)request.Stage);
    AppendString(hash, request.Model);
    AppendString(hash, request.SystemInstructions);
    AppendString(hash, request.UserData);
    AppendInt32(hash, request.MaximumOutputTokens);
    return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
}
```

`AppendString` hashes a four-byte big-endian UTF-8 byte count followed by the exact UTF-8 bytes. `AppendInt32` hashes exactly four big-endian bytes. Do not normalize line endings and do not include `EstimatedInputTokens`.

- [ ] **Step 4: Run focused fingerprint tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~OutboundRequestFingerprintTests
```

Expected: PASS; all six model-visible/configuration changes alter the fingerprint and accounting-only changes do not.

- [ ] **Step 5: Commit deterministic fingerprinting**

```powershell
git add src/EngineeringBrain.Infrastructure/OutboundRequestFingerprint.cs tests/EngineeringBrain.Core.Tests/OutboundRequestFingerprintTests.cs
git commit -m "feat: fingerprint exact outbound requests"
```

---

### Task 4: Command-Scoped Known Secret Values

**Files:**
- Create: `src/EngineeringBrain.Infrastructure/EnvironmentOutboundSecretValueSource.cs`
- Create: `tests/EngineeringBrain.Core.Tests/EnvironmentOutboundSecretValueSourceTests.cs`

**Interfaces:**
- Produces: `IOutboundSecretValueSource.GetValues()` and `EnvironmentOutboundSecretValueSource`.
- Consumed later by: `OutboundContextGuard` and one command-scoped gate composed by CLI/live evaluation.

- [ ] **Step 1: Write failing secret-selection tests**

Use an injected dictionary reader and add:

```csharp
[Theory]
[InlineData("APP_SECRET")]
[InlineData("ACCESS_TOKEN")]
[InlineData("DB_PASSWORD")]
[InlineData("SERVICE_PASSWD")]
[InlineData("OPENAI_API_KEY")]
[InlineData("LEGACY_APIKEY")]
[InlineData("AWS_ACCESS_KEY")]
[InlineData("SIGNING_PRIVATE_KEY")]
[InlineData("DB_CONNECTION_STRING")]
public void GetValues_IncludesHighConfidenceSecretNames(string name);

[Theory]
[InlineData("PATH", "C:\\tools")]
[InlineData("TOKENIZER_MODE", "something-long")]
[InlineData("APP_SECRET", "short")]
[InlineData("APP_SECRET", "development")]
[InlineData("APP_SECRET", "placeholder")]
public void GetValues_ExcludesUnsafeFalsePositiveInputs(string name, string value);
```

Also assert exact ordinal deduplication, defensive copies, and that no returned diagnostic or `ToString()` contains a selected value.

- [ ] **Step 2: Run focused tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~EnvironmentOutboundSecretValueSourceTests
```

Expected: FAIL because the abstraction and implementation do not exist.

- [ ] **Step 3: Implement the frozen selection rules**

Use:

```csharp
public interface IOutboundSecretValueSource
{
    IReadOnlySet<string> GetValues();
}

public sealed class EnvironmentOutboundSecretValueSource : IOutboundSecretValueSource
{
    public const int MinimumSecretLength = 8;
    public IReadOnlySet<string> GetValues();
}
```

Snapshot the selected values once in the constructor so one command observes one stable secret set. Normalize names to uppercase and replace `-` with `_`. Include only token-boundary matches for `SECRET`, `TOKEN`, `PASSWORD`, `PASSWD`, `API_KEY`, `APIKEY`, `ACCESS_KEY`, `PRIVATE_KEY`, or `CONNECTION_STRING`. Exclude null/blank, values shorter than eight characters, and the case-insensitive literals `true`, `false`, `yes`, `no`, `on`, `off`, `none`, `null`, `development`, `production`, `staging`, `test`, `local`, `localhost`, `changeme`, `placeholder`, `example`, and `dummy`. Return a defensive ordinal set and never expose variable names.

- [ ] **Step 4: Run focused and authorization tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~EnvironmentOutboundSecretValueSourceTests|FullyQualifiedName~RemoteReasoningAuthorization"
```

Expected: PASS; authorization still requires explicit consent and secret discovery causes no remote operation.

- [ ] **Step 5: Commit the memory-only secret source**

```powershell
git add src/EngineeringBrain.Infrastructure/EnvironmentOutboundSecretValueSource.cs tests/EngineeringBrain.Core.Tests/EnvironmentOutboundSecretValueSourceTests.cs
git commit -m "feat: detect command scoped outbound secrets"
```

---

### Task 5: Structured Five-Category Payload Inspection

**Files:**
- Modify: `src/EngineeringBrain.Core/InitiativeAnalysisModels.cs`
- Modify: `src/EngineeringBrain.Infrastructure/OutboundContextGuard.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/OutboundContextGuardTests.cs`

**Interfaces:**
- Consumes: `ReasoningRequest`, optional `InitiativeContext`, `IOutboundSecretValueSource`.
- Produces: `ContextSegmentKind.CompleteRepository`; `OutboundContextGuard.Inspect(ReasoningRequest, InitiativeContext?)` returning ordered, bounded findings.

- [ ] **Step 1: Add failing detector positives and safe controls**

Add tests named:

```csharp
[Fact] public void Inspect_CompleteRepositorySegmentIsBlocked();
[Fact] public void Inspect_RepositoryFilesEnvelopeIsBlocked();
[Fact] public void Inspect_BoundedProjectMemoryAndGraphFactsAreAllowed();
[Fact] public void Inspect_CompleteRawSnapshotObjectIsBlocked();
[Fact] public void Inspect_SelectedEntityRelationSummaryIsAllowed();
[Fact] public void Inspect_ExplicitSourceBodyIsBlocked();
[Fact] public void Inspect_SignatureAndRelativeReferenceAreAllowed();
[Fact] public void Inspect_KnownSecretValueInSystemInstructionsIsBlockedWithoutRetention();
[Fact] public void Inspect_HighConfidenceSecretSyntaxIsBlocked();
[Fact] public void Inspect_CommonNonSecretTextIsAllowed();
[Theory]
[InlineData("C:\\Users\\person\\repo\\File.cs")]
[InlineData("\\\\server\\share\\repo\\File.cs")]
[InlineData("/home/person/repo/File.cs")]
[InlineData("file:///C:/repo/File.cs")]
public void Inspect_AbsoluteMachinePathIsBlocked(string value);
[Fact] public void Inspect_RelativeRepositoryPathIsAllowed();
[Fact] public void Inspect_TraversalSourceReferenceIsBlocked();
[Fact] public void Inspect_ContextContentMismatchFailsClosed();
```

Keep the legacy `ValidateInitiative`/`Validate` tests during migration and assert their summaries agree with structured findings.

- [ ] **Step 2: Run detector tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~OutboundContextGuardTests
```

Expected: FAIL because complete-repository kind, `Inspect`, known-value matching, UNC/file URI/traversal checks, and structured findings are absent.

- [ ] **Step 3: Implement inspection without policy decisions**

Add `ContextSegmentKind.CompleteRepository` after `RawSnapshot` to avoid renumbering existing values. Implement:

```csharp
public IReadOnlyList<OutboundInspectionFinding> Inspect(
    ReasoningRequest request,
    InitiativeContext? context = null)
```

Add `public OutboundContextGuard(IOutboundSecretValueSource? secretValues = null)` and snapshot `secretValues?.GetValues()` into a private ordinal set. Inspect `request.SystemInstructions` and `request.UserData`. If `context` is present, require `request.UserData == context.Content == ContextSegmentRenderer.Render(context.Segments)` using ordinal comparison. Inspect segment kinds and validate each included note path as a non-rooted, non-URI, no-`..` repository-relative path.

Recognize a complete repository JSON envelope only when the root has repository identity plus a `files` array and at least one file object has both `path` and `content`; recognize repeated text payloads only when at least three distinct `BEGIN_FILE ... END_FILE` boundaries exist. Recognize a raw snapshot only when one JSON object contains all six properties `schemaVersion`, `repository`, `git`, `projects`, `entities`, and `relations`. For untyped source-body defense in depth, retain the current C# patterns, add C-family declaration/method text containing an opening brace plus at least one body line, and add Python `class`/`def` headers followed by at least one indented non-comment line; signatures without those body lines remain allowed. Retain existing high-confidence secret patterns and add exact ordinal known-secret matching. Add UNC, `file://`, and the Unix roots frozen in the spec.

Aggregate one finding per category in `SystemSecurityPolicyCatalog` order, cap each count at `99`, and set `FindingCountCapped` when needed. No regex match/value enters a result.

- [ ] **Step 4: Run focused and existing context-security tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~OutboundContextGuardTests|FullyQualifiedName~InitiativeAnalysisSecurityTests|FullyQualifiedName~ProjectMemoryBuilderTests"
```

Expected: PASS; safe signatures, selected evidence, and repository-relative paths remain accepted.

- [ ] **Step 5: Commit the detector hardening**

```powershell
git add src/EngineeringBrain.Core/InitiativeAnalysisModels.cs src/EngineeringBrain.Infrastructure/OutboundContextGuard.cs tests/EngineeringBrain.Core.Tests/OutboundContextGuardTests.cs
git commit -m "feat: inspect all prohibited outbound categories"
```

---

### Task 6: Deterministic Outbound Policy Evaluation

**Files:**
- Create: `src/EngineeringBrain.Infrastructure/OutboundPolicyEvaluator.cs`
- Create: `tests/EngineeringBrain.Core.Tests/OutboundPolicyEvaluatorTests.cs`

**Interfaces:**
- Consumes: `SystemSecurityPolicyDefinition`, `OutboundInspectionFinding`, `OutboundAssessmentKind`, payload fingerprint.
- Produces: `OutboundPolicyEvaluator.Evaluate(...)` returning exactly five ordered `OutboundPolicyResult` values.

- [ ] **Step 1: Write failing evaluator tests**

Add:

```csharp
[Fact] public void Evaluate_NoFindingsReturnsFiveAllowedResultsInCatalogOrder();
[Fact] public void Evaluate_OneFindingBlocksOnlyMatchingPolicyAndOverallAssessment();
[Fact] public void Evaluate_MultipleFindingsReportsAllFiveAtMostOnce();
[Fact] public void Evaluate_DuplicateMissingOrUnknownRequiredPolicyFailsClosed();
[Fact] public void Evaluate_DiagnosticsAreFixedBoundedAndContainNoTriggerValue();
[Fact] public void Evaluate_NotRecordedCannotBeRequestedAsAComputedAssessment();
```

Inject catalog definitions into the evaluator for invalid-catalog tests. Assert `Results.Count <= 5`, `Diagnostics.Count <= 5`, stable order, and exact fixed reason codes.

- [ ] **Step 2: Run focused tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~OutboundPolicyEvaluatorTests
```

Expected: FAIL because `OutboundPolicyEvaluator` does not exist.

- [ ] **Step 3: Implement catalog validation and policy mapping**

Use:

```csharp
public sealed class OutboundPolicyEvaluator
{
    public const int MaximumPolicyResults = 5;
    public const int MaximumDiagnostics = 5;

    public OutboundPolicyAssessment Evaluate(
        OutboundAssessmentKind assessmentKind,
        string? payloadFingerprint,
        IReadOnlyList<OutboundInspectionFinding> findings);
}
```

Reject `NotRecorded` as an evaluation kind. Validate that the catalog contains each required policy ID and prohibited category exactly once, with version `1` and severity `Block`. For a valid catalog, emit one result per definition: `Blocked` when a matching finding exists and `Allowed` otherwise. Use only catalog-owned fixed diagnostic text. Allowed assessments require a non-null fingerprint; blocked and invalid-catalog assessments require a null fingerprint so unsafe payloads do not create persistent hashes. Invalid catalog returns a blocked assessment with `OUTBOUND_POLICY_CATALOG_INVALID` and no transport approval possibility.

- [ ] **Step 4: Run focused, model, and catalog tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~OutboundPolicyEvaluatorTests|FullyQualifiedName~OutboundSecurityModelTests|FullyQualifiedName~SystemSecurityPolicyCatalogTests"
```

Expected: PASS with exactly five deterministic results for every valid evaluation.

- [ ] **Step 5: Commit policy evaluation**

```powershell
git add src/EngineeringBrain.Infrastructure/OutboundPolicyEvaluator.cs tests/EngineeringBrain.Core.Tests/OutboundPolicyEvaluatorTests.cs
git commit -m "feat: evaluate outbound security policies"
```

---

### Task 7: Unforgeable Exact Request Approval Gate

**Files:**
- Create: `src/EngineeringBrain.Infrastructure/ApprovedReasoningRequest.cs`
- Create: `src/EngineeringBrain.Infrastructure/OutboundRequestGate.cs`
- Create: `src/EngineeringBrain.Infrastructure/Properties/AssemblyInfo.cs`
- Create: `tests/EngineeringBrain.Core.Tests/OutboundRequestGateTests.cs`
- Create: `tests/EngineeringBrain.Core.Tests/ReasoningProviderBoundaryTests.cs`

**Interfaces:**
- Consumes: `OutboundContextGuard.Inspect`, `OutboundPolicyEvaluator.Evaluate`, `OutboundRequestFingerprint.Create`.
- Produces: projected assessments, exact approved requests, and integrity verification.

- [ ] **Step 1: Write failing approval and API-boundary tests**

Add tests:

```csharp
[Fact] public void ApprovedReasoningRequest_HasNoPublicConstructorOrFactory();
[Fact] public void AssessProjected_ReturnsProjectedAssessmentWithoutTransportableRequest();
[Fact] public void ApproveExact_AllowedPayloadReturnsExactApprovedRequest();
[Fact] public void ApproveExact_BlockedPayloadThrowsAndRetainsSafeAssessment();
[Fact] public void ApproveExact_ContextMismatchFailsClosed();
[Fact] public void ApproveExact_MultipleViolationsNeverCreatesRequest();
[Fact] public void EnsureIntegrity_InternallyForgedFingerprintMismatchThrows();
[Fact] public void GateFailure_DoesNotExposeInputOrSecret();
```

The API test may use reflection only to assert that public constructors/static factories are absent. Use friend-assembly access to call the internal constructor for the fingerprint-mismatch test; do not invoke constructors through reflection.

- [ ] **Step 2: Run focused tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~OutboundRequestGateTests|FullyQualifiedName~ReasoningProviderBoundaryTests"
```

Expected: FAIL because the gate, approved type, security exception, and friend boundary do not exist.

- [ ] **Step 3: Implement approval authority and fail-closed integrity**

Use these signatures:

```csharp
public sealed class ApprovedReasoningRequest
{
    internal ApprovedReasoningRequest(ReasoningRequest request, OutboundPolicyAssessment assessment);
    public ReasoningRequest Request { get; }
    public OutboundPolicyAssessment Assessment { get; }
    internal void EnsureIntegrity();
}

public sealed class OutboundSecurityException : InvalidDataException
{
    public string Code { get; }
    public OutboundPolicyAssessment Assessment { get; }
}

public sealed class OutboundRequestGate
{
    public OutboundRequestGate(
        OutboundContextGuard? guard = null,
        OutboundPolicyEvaluator? evaluator = null);

    public OutboundPolicyAssessment AssessProjected(
        ReasoningRequest request,
        InitiativeContext? context = null);

    public ApprovedReasoningRequest ApproveExact(
        ReasoningRequest request,
        InitiativeContext? context = null);
}
```

Copy policy result and diagnostic collections before storing them. `ApproveExact` inspects and evaluates first with a null fingerprint. If blocked, throw a fixed-message `OutboundSecurityException` and never hash the unsafe request. If allowed, compute the fingerprint, produce the final allowed assessment, and construct the approved request. `EnsureIntegrity` recomputes the fingerprint and validates exact kind, allowed outcome, five unique expected IDs, and no blocked result. Add only `[assembly: InternalsVisibleTo("EngineeringBrain.Core.Tests")]` to Infrastructure.

- [ ] **Step 4: Run gate, evaluator, detector, and fingerprint tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~OutboundRequestGateTests|FullyQualifiedName~ReasoningProviderBoundaryTests|FullyQualifiedName~OutboundPolicyEvaluatorTests|FullyQualifiedName~OutboundContextGuardTests|FullyQualifiedName~OutboundRequestFingerprintTests"
```

Expected: PASS; blocked inputs create no approved object, and a deliberately corrupted internal instance fails integrity.

- [ ] **Step 5: Commit the approval boundary**

```powershell
git add src/EngineeringBrain.Infrastructure/ApprovedReasoningRequest.cs src/EngineeringBrain.Infrastructure/OutboundRequestGate.cs src/EngineeringBrain.Infrastructure/Properties/AssemblyInfo.cs tests/EngineeringBrain.Core.Tests/OutboundRequestGateTests.cs tests/EngineeringBrain.Core.Tests/ReasoningProviderBoundaryTests.cs
git commit -m "feat: gate exact outbound reasoning requests"
```

---

### Task 8: Projected Preview and Exact CALL #1 Preparation

**Files:**
- Modify: `src/EngineeringBrain.Core/EvaluationModels.cs`
- Modify: `src/EngineeringBrain.Infrastructure/RemoteContextPreviewService.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/RemoteContextPreviewTests.cs`

**Interfaces:**
- Consumes: `OutboundRequestGate`, existing interpreter/retriever/context builder/budgets.
- Produces: `RemoteAnalysisPreparation` containing public safe preview data plus the exact approved CALL #1 request for in-process reuse.

- [ ] **Step 1: Write failing preview-semantics tests**

Add:

```csharp
[Fact] public async Task PrepareAsync_CallOneIsExactAndCallTwoIsProjected();
[Fact] public async Task PrepareAsync_CallOneFingerprintMatchesApprovedRequest();
[Fact] public async Task PrepareAsync_ProjectedCallTwoCannotBeTransported();
[Fact] public async Task PrepareAsync_UsesInjectedGateForBothCalls();
[Fact] public async Task PrepareAsync_BlockedCallOneCreatesNoManifest();
[Fact] public async Task CreateAsync_BackwardCompatibleWrapperReturnsSafePreviewOnly();
```

Assert the persisted manifest contains no initiative text, approved raw request, secret value, absolute repository root, or Project Memory note body beyond existing safe metadata.

- [ ] **Step 2: Run preview tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~RemoteContextPreviewTests
```

Expected: FAIL because preparation and projected/exact assessment fields do not exist.

- [ ] **Step 3: Implement explicit preparation semantics**

Add runtime shape in `RemoteContextPreviewService.cs`:

```csharp
public sealed record RemoteAnalysisPreparation(
    RemoteContextPreview Preview,
    ApprovedReasoningRequest ApprovedCall1);

public Task<RemoteAnalysisPreparation> PrepareAsync(
    string initiativeFileName,
    string initiative,
    ProjectMemorySyncResult memory,
    ReviewedConceptResolutionResult reviewedConcepts,
    string interpretationModel,
    string reasoningModel,
    string interpretationEffort,
    string analysisEffort,
    CancellationToken cancellationToken = default);
```

Build the CALL #1 `ReasoningRequest` once and call `ApproveExact`. Build deterministic preview CALL #2 exactly as today, construct its raw request, and call `AssessProjected`. Add `Call1PolicyAssessment` and `Call2PolicyAssessment` to `RemoteContextPreview`; retain the old `Security` summary temporarily for schema-1 compatibility. Existing `CreateAsync` delegates to `PrepareAsync` and returns only `.Preview`.

- [ ] **Step 4: Run preview, gate, and budget tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~RemoteContextPreviewTests|FullyQualifiedName~OutboundRequestGateTests|FullyQualifiedName~InitiativeContextBuilderTests"
```

Expected: PASS; CALL #2 is visibly projected and no test requires it to equal later live CALL #2 bytes.

- [ ] **Step 5: Commit preview preparation**

```powershell
git add src/EngineeringBrain.Core/EvaluationModels.cs src/EngineeringBrain.Infrastructure/RemoteContextPreviewService.cs tests/EngineeringBrain.Core.Tests/RemoteContextPreviewTests.cs
git commit -m "feat: distinguish projected and exact remote previews"
```

---

### Task 9: Approved-Request-Only Provider Contract

**Files:**
- Create: `src/EngineeringBrain.Infrastructure/IReasoningProvider.cs`
- Modify: `src/EngineeringBrain.Core/Abstractions.cs`
- Modify: `src/EngineeringBrain.Infrastructure/OpenAIReasoningProvider.cs`
- Modify: `src/EngineeringBrain.Infrastructure/FakeLiveReasoningProvider.cs`
- Modify: `src/EngineeringBrain.Infrastructure/InitiativeAnalysisService.cs`
- Modify: `src/EngineeringBrain.Infrastructure/LiveEvaluationService.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/InitiativeAnalysisTestSupport.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/LiveEvaluationTests.cs`
- Modify: `tests/EngineeringBrain.Analyzers.CSharp.Tests/InitiativeAnalysisDogfoodTests.cs`
- Modify: all test-local `IReasoningProvider` implementations returned by `rg -n "IReasoningProvider|GenerateStructuredAsync" tests src`.

**Interfaces:**
- Consumes: `ApprovedReasoningRequest`, `OutboundRequestGate`.
- Produces: the only provider interface accepted by production orchestration.

- [ ] **Step 1: Add failing provider-contract assertions**

Extend `ReasoningProviderBoundaryTests`:

```csharp
[Fact]
public void ProviderContract_AcceptsOnlyApprovedReasoningRequest()
{
    var method = typeof(IReasoningProvider).GetMethod("GenerateStructuredAsync")!;
    Assert.Equal(typeof(ApprovedReasoningRequest), method.GetParameters()[0].ParameterType);
    Assert.DoesNotContain(typeof(IReasoningProvider).GetMethods(), methodInfo =>
        methodInfo.GetParameters().Any(parameter => parameter.ParameterType == typeof(ReasoningRequest)));
}

[Fact] public async Task OpenAIProvider_RejectsInternallyForgedApprovalBeforeClientInvocation();
[Fact] public void OpenAIRequestMapping_UsesOnlyApprovedSystemAndUserText();
[Fact] public void OpenAIRequestMapping_AddsOnlyFixedProtocolMetadata();
```

Construct `OpenAIReasoningProvider` with a syntactically valid fixture key and pass an internally forged invalid approved request. `request.EnsureIntegrity()` must execute before SDK option construction or client invocation, so the test deterministically throws without making a network call.

- [ ] **Step 2: Run boundary tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~ReasoningProviderBoundaryTests
```

Expected: FAIL because the provider interface still accepts raw `ReasoningRequest`.

- [ ] **Step 3: Migrate the interface and every caller in one compile-safe change**

Create in Infrastructure:

```csharp
public interface IReasoningProvider
{
    string Name { get; }
    Task<ReasoningResult<T>> GenerateStructuredAsync<T>(
        ApprovedReasoningRequest request,
        CancellationToken cancellationToken = default);
}
```

Remove only the old interface declaration from Core. Update every provider implementation to call `request.EnsureIntegrity()` first and then read `var raw = request.Request`. In `OpenAIReasoningProvider.cs`, introduce an internal pure `OpenAITransportPayload` mapper containing only model, maximum output tokens, exact approved system text, exact approved user text, and fixed stage/result-type protocol metadata; SDK options are built solely from that mapper. Update all production services to create raw requests and immediately pass them through `OutboundRequestGate.ApproveExact` before provider invocation. Update test fakes to record `ApprovedReasoningRequest` and expose `request.Request` when assertions need the old fields. There must be no `GenerateStructuredAsync<T>(ReasoningRequest` occurrence after this step.

- [ ] **Step 4: Build and run all provider/service tests**

```powershell
dotnet build EngineeringBrain.sln --no-restore
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~ReasoningProviderBoundaryTests|FullyQualifiedName~InitiativeAnalysisServiceTests|FullyQualifiedName~LiveEvaluationTests"
dotnet test tests/EngineeringBrain.Analyzers.CSharp.Tests/EngineeringBrain.Analyzers.CSharp.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~InitiativeAnalysisDogfoodTests
rg -n "GenerateStructuredAsync<.*ReasoningRequest|IReasoningProvider" src tests
```

Expected: build and tests PASS; search shows provider declarations/callers but no raw-request provider signature.

- [ ] **Step 5: Commit the provider boundary migration**

```powershell
git add src/EngineeringBrain.Core/Abstractions.cs src/EngineeringBrain.Infrastructure/IReasoningProvider.cs src/EngineeringBrain.Infrastructure/OpenAIReasoningProvider.cs src/EngineeringBrain.Infrastructure/FakeLiveReasoningProvider.cs src/EngineeringBrain.Infrastructure/InitiativeAnalysisService.cs src/EngineeringBrain.Infrastructure/LiveEvaluationService.cs tests/EngineeringBrain.Core.Tests/InitiativeAnalysisTestSupport.cs tests/EngineeringBrain.Core.Tests/LiveEvaluationTests.cs tests/EngineeringBrain.Analyzers.CSharp.Tests/InitiativeAnalysisDogfoodTests.cs tests/EngineeringBrain.Core.Tests/ReasoningProviderBoundaryTests.cs
git commit -m "refactor: require approved requests at provider boundary"
```

---

### Task 10: Normal Analysis Reuses Exact CALL #1 and Records Exact CALL #2

**Files:**
- Modify: `src/EngineeringBrain.Core/InitiativeAnalysisModels.cs`
- Modify: `src/EngineeringBrain.Infrastructure/InitiativeAnalysisService.cs`
- Modify: `src/EngineeringBrain.Cli/Program.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/InitiativeAnalysisServiceTests.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/InitiativeAnalysisSecurityTests.cs`

**Interfaces:**
- Consumes: `RemoteAnalysisPreparation.ApprovedCall1`, `OutboundRequestGate`.
- Produces: two exact assessments on `InitiativeAnalysisResult` and object-identity reuse of prepared CALL #1.

- [ ] **Step 1: Write failing exact-flow tests**

Add:

```csharp
[Fact] public async Task AnalyzeAsync_ReusesThePreparedApprovedCallOneInstance();
[Fact] public async Task AnalyzeAsync_PreparedCallOneMismatchFailsBeforeProvider();
[Fact] public async Task AnalyzeAsync_ActualCallTwoIsExactNotProjected();
[Fact] public async Task AnalyzeAsync_BlockedCallTwoMakesZeroSecondProviderCalls();
[Fact] public async Task AnalyzeAsync_RecordsTwoExactAssessmentsInCallOrder();
[Fact] public async Task AnalyzeAsync_WithoutPreparationStillApprovesBothCalls();
```

The recording provider stores approved object references. Assert CALL #1 is `Assert.Same(preparation.ApprovedCall1, provider.Requests[0])` and CALL #2 fingerprint equals the provider-captured request.

- [ ] **Step 2: Run focused analysis tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~InitiativeAnalysisServiceTests|FullyQualifiedName~InitiativeAnalysisSecurityTests"
```

Expected: new tests FAIL because analysis cannot receive the prepared CALL #1 and results do not record assessments.

- [ ] **Step 3: Integrate preparation without weakening direct service use**

Add an overload:

```csharp
public Task<InitiativeAnalysisResult> AnalyzeAsync(
    InitiativeAnalysisRequest request,
    ReviewedConceptResolutionResult reviewedConcepts,
    ApprovedReasoningRequest? preparedCall1,
    CancellationToken cancellationToken = default);
```

Existing overloads delegate with `preparedCall1: null` and approve CALL #1 themselves. When supplied, reconstruct the expected raw CALL #1 locally, compare its canonical fingerprint with the approved request, call `EnsureIntegrity`, and reject mismatches before invoking the provider. Build and approve actual CALL #2 after live understanding. Add `IReadOnlyList<OutboundPolicyAssessment> OutboundPolicyAssessments` to `InitiativeAnalysisResult` and store the two exact assessments in call order. In CLI, construct one `EnvironmentOutboundSecretValueSource`, one `OutboundContextGuard`, one `OutboundPolicyEvaluator`, and one `OutboundRequestGate`; inject that gate into preview and analysis, call `PrepareAsync`, and pass `preparation.ApprovedCall1` into analysis.

- [ ] **Step 4: Run analysis, preview, and CLI-adjacent tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~InitiativeAnalysisServiceTests|FullyQualifiedName~InitiativeAnalysisSecurityTests|FullyQualifiedName~RemoteContextPreviewTests"
dotnet test tests/EngineeringBrain.Analyzers.CSharp.Tests/EngineeringBrain.Analyzers.CSharp.Tests.csproj --no-restore --filter FullyQualifiedName~InitiativeAnalysisDogfoodTests
```

Expected: PASS; normal direct service use remains safe and CLI execution reuses the preview-approved CALL #1 object.

- [ ] **Step 5: Commit exact normal orchestration**

```powershell
git add src/EngineeringBrain.Core/InitiativeAnalysisModels.cs src/EngineeringBrain.Infrastructure/InitiativeAnalysisService.cs src/EngineeringBrain.Cli/Program.cs tests/EngineeringBrain.Core.Tests/InitiativeAnalysisServiceTests.cs tests/EngineeringBrain.Core.Tests/InitiativeAnalysisSecurityTests.cs tests/EngineeringBrain.Analyzers.CSharp.Tests/InitiativeAnalysisDogfoodTests.cs
git commit -m "feat: enforce exact requests in initiative analysis"
```

---

### Task 11: Live Evaluation Uses the Same Exact Security Boundary

**Files:**
- Modify: `src/EngineeringBrain.Core/LiveEvaluationModels.cs`
- Modify: `src/EngineeringBrain.Infrastructure/LiveEvaluationService.cs`
- Modify: `src/EngineeringBrain.Infrastructure/LiveEvaluationMetrics.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/LiveEvaluationTests.cs`

**Interfaces:**
- Consumes: approved-request provider contract, shared gate, structured assessments.
- Produces: exact assessment persistence per live call and complete-repository outbound aggregate count.

- [ ] **Step 1: Write failing live-boundary tests**

Add:

```csharp
[Fact] public async Task ExecuteAsync_RecordsExactAssessmentForEveryProviderCall();
[Fact] public async Task ExecuteAsync_BlockedCallOneNeverCreatesProvider();
[Fact] public async Task ExecuteAsync_BlockedCallTwoDoesNotInvokeSecondCall();
[Fact] public async Task ExecuteAsync_UsesSameCatalogGuardAndEvaluatorAsNormalAnalysis();
[Fact] public async Task Aggregate_CountsCompleteRepositoryAndAllExistingCategories();
```

Replace assertions based only on `OutboundValidationResult` with policy-ID assertions while retaining legacy counters for old artifact compatibility.

- [ ] **Step 2: Run live tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~LiveEvaluationTests
```

Expected: FAIL because live result models lack structured assessments and complete-repository aggregate data.

- [ ] **Step 3: Remove duplicated raw-request security flow**

Inject one `OutboundRequestGate` into `LiveEvaluationService`. CLI live-evaluation composition creates one command-scoped `EnvironmentOutboundSecretValueSource` and injects the resulting gate into the service. For each call, construct raw request, call `ApproveExact`, append the approved assessment, and invoke the provider with that object. Catch `OutboundSecurityException` as `SecurityBlocked` using only `Code` and fixed diagnostics. Add `IReadOnlyList<OutboundPolicyAssessment> OutboundPolicyAssessments` to `LiveEvaluationCaseResult` and `CompleteRepositoryOutbound` to `LiveEvaluationAggregate`. Derive all five aggregate counts from blocked policy results; preserve old summary fields for schema-2 reads.

- [ ] **Step 4: Run live, gate, and metric tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LiveEvaluationTests|FullyQualifiedName~OutboundRequestGateTests|FullyQualifiedName~EvaluationMetricsTests"
```

Expected: PASS; every successful provider call has one exact allowed assessment and blocked calls are absent from provider usage.

- [ ] **Step 5: Commit live boundary integration**

```powershell
git add src/EngineeringBrain.Core/LiveEvaluationModels.cs src/EngineeringBrain.Infrastructure/LiveEvaluationService.cs src/EngineeringBrain.Infrastructure/LiveEvaluationMetrics.cs tests/EngineeringBrain.Core.Tests/LiveEvaluationTests.cs
git commit -m "feat: enforce outbound policies in live evaluation"
```

---

### Task 12: Safe Provider Failure Boundary

**Files:**
- Create: `src/EngineeringBrain.Infrastructure/SafeReasoningProviderInvoker.cs`
- Create: `tests/EngineeringBrain.Core.Tests/SafeReasoningProviderInvokerTests.cs`
- Modify: `src/EngineeringBrain.Infrastructure/ReasoningProviderException.cs`
- Modify: `src/EngineeringBrain.Infrastructure/OpenAIReasoningProvider.cs`
- Modify: `src/EngineeringBrain.Infrastructure/InitiativeAnalysisService.cs`
- Modify: `src/EngineeringBrain.Infrastructure/LiveEvaluationService.cs`
- Modify: `src/EngineeringBrain.Infrastructure/LocalLiveEvaluationStore.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/LiveEvaluationTests.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/InitiativeAnalysisSecurityTests.cs`

**Interfaces:**
- Consumes: approved provider calls.
- Produces: `ReasoningProviderFailureCode`, fixed safe provider failures, and one shared invocation boundary.

- [ ] **Step 1: Write failing provider-leak regression tests**

Create a provider that throws `InvalidOperationException` with each value and test both normal analysis and live evaluation:

```csharp
[Theory]
[InlineData("sk-fakeapikey123456789")]
[InlineData("C:\\Users\\person\\private\\file.cs")]
[InlineData("/home/person/private/file.cs")]
[InlineData("Server=db;User Id=admin;Password=fixture-secret")]
public async Task InvokeAsync_ArbitraryProviderMessageNeverEscapes(string unsafeMessage);

[Fact] public async Task InvokeAsync_CancellationPropagatesUnchanged();
[Fact] public async Task InvokeAsync_ReasoningProviderFailureRetainsUsageButNotRawCause();
[Fact] public async Task LivePersistence_BoundsAndSanitizesDefenseInDepth();
[Fact] public async Task FailedInitiativeAnalysis_DoesNotPersistAnAnalysisArtifact();
```

Assert exception messages, persisted case JSON, live summary JSON, live review Markdown, and any formatter output do not contain the injected string or its path/credential fragments.

- [ ] **Step 2: Run focused tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~SafeReasoningProviderInvokerTests|FullyQualifiedName~InitiativeAnalysisSecurityTests|FullyQualifiedName~LiveEvaluationTests&Name~Provider"
```

Expected: FAIL because live evaluation currently concatenates arbitrary `exception.Message` and no shared safe invoker exists.

- [ ] **Step 3: Implement fixed provider failures and remove raw-message propagation**

Define:

```csharp
public enum ReasoningProviderFailureCode
{
    Timeout,
    InvalidStructuredOutput,
    TransportFailure,
    NoValidResult
}

public sealed class SafeReasoningProviderInvoker
{
    public Task<ReasoningResult<T>> InvokeAsync<T>(
        IReasoningProvider provider,
        ApprovedReasoningRequest request,
        CancellationToken cancellationToken = default);
}
```

`ReasoningProviderException` accepts a failure code rather than arbitrary message text and derives `Message` from a fixed switch. Preserve stage, usage, duration, retries, and structured-output classification. `SafeReasoningProviderInvoker` rethrows cancellation, passes through already-safe `ReasoningProviderException`, maps JSON failures to `InvalidStructuredOutput`, and maps every other exception to `TransportFailure` without retaining its message. Both orchestration services use this invoker. `OpenAIReasoningProvider` also uses fixed failure codes. Bound/control-sanitize `LocalLiveEvaluationStore.Redact` output to 512 single-line characters as defense in depth.

- [ ] **Step 4: Run all provider and persistence tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~SafeReasoningProviderInvokerTests|FullyQualifiedName~InitiativeAnalysisSecurityTests|FullyQualifiedName~LiveEvaluationTests|FullyQualifiedName~OpenAIReasoningProvider"
```

Expected: PASS; none of the four injected unsafe messages appears in exceptions or artifacts, usage fields remain available, and failed normal analysis creates no persisted result.

- [ ] **Step 5: Commit safe provider errors**

```powershell
git add src/EngineeringBrain.Infrastructure/SafeReasoningProviderInvoker.cs src/EngineeringBrain.Infrastructure/ReasoningProviderException.cs src/EngineeringBrain.Infrastructure/OpenAIReasoningProvider.cs src/EngineeringBrain.Infrastructure/InitiativeAnalysisService.cs src/EngineeringBrain.Infrastructure/LiveEvaluationService.cs src/EngineeringBrain.Infrastructure/LocalLiveEvaluationStore.cs tests/EngineeringBrain.Core.Tests/SafeReasoningProviderInvokerTests.cs tests/EngineeringBrain.Core.Tests/LiveEvaluationTests.cs tests/EngineeringBrain.Core.Tests/InitiativeAnalysisSecurityTests.cs
git commit -m "fix: prevent provider diagnostics from leaking sensitive data"
```

---

### Task 13: Compatible Outbound Assessment Schema Evolution

**Files:**
- Modify: `src/EngineeringBrain.Core/EvaluationModels.cs`
- Modify: `src/EngineeringBrain.Core/LiveEvaluationModels.cs`
- Modify: `src/EngineeringBrain.Infrastructure/RemoteContextPreviewService.cs`
- Modify: `src/EngineeringBrain.Infrastructure/LocalInitiativeAnalysisStore.cs`
- Modify: `src/EngineeringBrain.Infrastructure/LocalLiveEvaluationStore.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/RemoteContextPreviewTests.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/InitiativeAnalysisServiceTests.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/LiveEvaluationTests.cs`

**Interfaces:**
- Consumes: structured projected/exact assessments from Tasks 8, 10, and 11.
- Produces: preview schema 2, initiative-analysis schema 3, live-result schema 3, and explicit legacy `NotRecorded` normalization.

- [ ] **Step 1: Add failing new-write and legacy-read tests**

Add:

```csharp
[Fact] public async Task PreviewStore_NewArtifactWritesSchemaTwoAndAssessmentKinds();
[Fact] public async Task PreviewStore_SchemaOneLoadsBothAssessmentsAsNotRecorded();
[Fact] public async Task InitiativeStore_NewArtifactWritesSchemaThreeWithTwoExactAssessments();
[Fact] public async Task InitiativeStore_SchemaTwoLoadsAssessmentAsNotRecorded();
[Fact] public async Task LiveStore_NewArtifactWritesSchemaThreeWithExactAssessments();
[Fact] public async Task LiveStore_SchemaTwoLoadsAssessmentAsNotRecorded();
[Fact] public async Task LegacyMissingAssessmentIsNeverInterpretedAsAllowed();
```

Construct legacy JSON inline from the smallest valid schema-1/schema-2 DTOs using repository serializers. Do not depend on user-local artifacts.

- [ ] **Step 2: Run serializer tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~RemoteContextPreviewTests|FullyQualifiedName~InitiativeAnalysisServiceTests&Name~Schema|FullyQualifiedName~LiveEvaluationTests&Name~Schema"
```

Expected: FAIL because stores currently accept only their current exact version, preview has no loader, and new assessment fields are absent.

- [ ] **Step 3: Implement versioned readers without rewriting historical files**

Set:

```csharp
RemoteContextPreviewStore.CurrentSchemaVersion = 2;
LocalInitiativeAnalysisStore.CurrentSchemaVersion = 3;
LocalLiveEvaluationStore.CurrentResultSchemaVersion = 3;
```

Add `Task<PersistedRemoteContextPreview> RemoteContextPreviewStore.LoadAsync(string path, CancellationToken cancellationToken = default)`. Preview accepts versions `1` and `2`; initiative analysis accepts `2` and `3`; live evaluation accepts `2` and `3`. Persist nullable assessment fields at the end of DTO constructors for old JSON compatibility, then normalize missing fields to `OutboundPolicyAssessment.NotRecorded` in returned runtime objects. Never synthesize `Allowed` from old guard counters. New writes always include structured assessments and current version. Do not rewrite files on load.

- [ ] **Step 4: Run compatibility and persistence suites**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~RemoteContextPreviewTests|FullyQualifiedName~InitiativeAnalysisServiceTests|FullyQualifiedName~LiveEvaluationTests"
```

Expected: PASS; old and new artifacts read, old timestamps/bytes stay unchanged, and all missing historical assessments report `NotRecorded`.

- [ ] **Step 5: Commit compatible schema versions**

```powershell
git add src/EngineeringBrain.Core/EvaluationModels.cs src/EngineeringBrain.Core/LiveEvaluationModels.cs src/EngineeringBrain.Infrastructure/RemoteContextPreviewService.cs src/EngineeringBrain.Infrastructure/LocalInitiativeAnalysisStore.cs src/EngineeringBrain.Infrastructure/LocalLiveEvaluationStore.cs tests/EngineeringBrain.Core.Tests/RemoteContextPreviewTests.cs tests/EngineeringBrain.Core.Tests/InitiativeAnalysisServiceTests.cs tests/EngineeringBrain.Core.Tests/LiveEvaluationTests.cs
git commit -m "feat: persist versioned outbound policy assessments"
```

---

### Task 14: Bounded CLI Policy Reporting

**Files:**
- Create: `src/EngineeringBrain.Infrastructure/OutboundPolicyDiagnosticFormatter.cs`
- Modify: `src/EngineeringBrain.Cli/Program.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/OutboundSecurityModelTests.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/InitiativeAnalysisSecurityTests.cs`

**Interfaces:**
- Consumes: `RemoteContextPreview` projected/exact assessments and actual `InitiativeAnalysisResult` assessments.
- Produces: existing-command policy tables with no new command.

- [ ] **Step 1: Write failing bounded-format tests**

Add focused formatter and CLI-adjacent tests:

```csharp
[Fact] public void Format_AssessmentShowsPolicyIdOutcomeAndAssessmentKind();
[Fact] public void Format_NotRecordedNeverRendersAllowed();
[Fact] public void Format_DiagnosticsAreSingleLineAndBounded();
[Fact] public async Task PreviewOutput_LabelsCallTwoProjected();
[Fact] public async Task AnalyzeOutput_LabelsBothActualCallsExact();
```

Use only fixed policy IDs and safe diagnostics in expected output.

- [ ] **Step 2: Run formatter/CLI-adjacent tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~OutboundSecurityModelTests|FullyQualifiedName~InitiativeAnalysisSecurityTests&Name~Output"
```

Expected: FAIL because current CLI prints four counters and cannot distinguish projected, exact, or not-recorded policy results.

- [ ] **Step 3: Integrate policy tables into existing output**

Implement `OutboundPolicyDiagnosticFormatter.Format(OutboundPolicyAssessment)` with a maximum rendered line length of 512, control-character replacement, and catalog-owned policy IDs/messages only. Add one helper in `Program.cs`:

```csharp
private static void WriteOutboundAssessment(string label, OutboundPolicyAssessment assessment)
```

Print label, assessment kind, each policy ID and outcome in catalog order, then overall outcome. Update preview output to say `CALL #2 projection` and never imply byte identity. Update successful analyze output to show actual CALL #1/CALL #2 exact checks. Update live summary to include all five aggregate categories. Continue rendering the manifest path only where the existing command already does so.

- [ ] **Step 4: Run CLI-related tests and offline preview smoke test**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~OutboundSecurityModelTests|FullyQualifiedName~RemoteContextPreviewTests|FullyQualifiedName~InitiativeAnalysisSecurityTests|FullyQualifiedName~LiveEvaluationTests"
dotnet build EngineeringBrain.sln --no-restore
```

Expected: PASS with zero warnings; no command needs network access.

- [ ] **Step 5: Commit bounded reporting**

```powershell
git add src/EngineeringBrain.Infrastructure/OutboundPolicyDiagnosticFormatter.cs src/EngineeringBrain.Cli/Program.cs src/EngineeringBrain.Core/OutboundSecurityModels.cs tests/EngineeringBrain.Core.Tests/OutboundSecurityModelTests.cs tests/EngineeringBrain.Core.Tests/InitiativeAnalysisSecurityTests.cs
git commit -m "feat: report outbound policy assessments"
```

---

### Task 15: Deterministic Offline Outbound Security Evaluation

**Files:**
- Create: `evaluations/security-outbound-suite.json`
- Create: `tests/EngineeringBrain.Core.Tests/OutboundSecurityEvaluationTests.cs`
- Create: `tests/EngineeringBrain.Core.Tests/OutboundSecurityArchitectureTests.cs`

**Interfaces:**
- Consumes: gate, injected known-secret source, provider contract, policy results.
- Produces: deterministic acceptance metrics and architectural regression checks without a live provider.

- [ ] **Step 1: Write the frozen fixture and failing evaluator tests**

Create schema `1` with these exact case IDs:

```text
complete-repository-blocked
bounded-evidence-allowed
raw-snapshot-blocked
derived-snapshot-evidence-allowed
source-body-blocked
signature-reference-allowed
known-environment-secret-blocked
bearer-token-blocked
common-environment-label-allowed
windows-path-blocked
unc-path-blocked
unix-path-blocked
file-uri-blocked
relative-path-allowed
traversal-reference-blocked
multiple-violations-blocked
```

Each case declares stage, assessment kind, system/user fixture text, optional structured segments, fixture-local known secret values, expected outcome, and expected blocked policy IDs. Use only invented values. Add tests:

```csharp
[Fact] public async Task Suite_ProducesOneHundredPercentExpectedDecisions();
[Fact] public async Task Suite_HasZeroFalseAllowedFalseBlockedOrDiagnosticLeaks();
[Fact] public async Task Suite_BlockedCasesMakeZeroProviderCalls();
[Fact] public void Architecture_ProviderContractHasNoRawReasoningRequestParameter();
[Fact] public void Architecture_ApprovedRequestHasNoPublicCreationPath();
[Fact] public void Architecture_AllProductionProviderCallsUseApprovedRequest();
```

The last test scans production `.cs` files under the discovered repository root and permits `new ApprovedReasoningRequest` only in `OutboundRequestGate.cs`. It is a deterministic architecture assertion, not a runtime reflection bypass.

- [ ] **Step 2: Run the outbound evaluation and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~OutboundSecurityEvaluationTests|FullyQualifiedName~OutboundSecurityArchitectureTests"
```

Expected: FAIL until the fixture loader, all expected categories, provider-attempt accounting, and production-call scan are wired correctly.

- [ ] **Step 3: Implement the test-local deterministic evaluator**

Keep fixture DTOs and metrics in `OutboundSecurityEvaluationTests.cs`; do not add a production CLI command. For each case, create `ReasoningRequest`/`InitiativeContext`, inject a fixed `IOutboundSecretValueSource`, call projected assessment or exact approval as declared, and call a counting fake provider only when approval succeeds. Compute:

```csharp
public sealed record SecurityMetrics(
    int Cases,
    int ExpectedBlocked,
    int ActualBlocked,
    int FalseAllowed,
    int FalseBlocked,
    int DiagnosticLeaks,
    int BlockedProviderInvocations);
```

Assert `Cases == 16`, all expected decisions match, and the last four error metrics are zero. A diagnostic leak means any fixture `sensitiveValues` entry or prohibited absolute-path literal occurs in exception, diagnostic, or serialized assessment output.

- [ ] **Step 4: Run outbound evaluation, full Core tests, and protected-file checks**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~OutboundSecurityEvaluationTests|FullyQualifiedName~OutboundSecurityArchitectureTests"
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore
git diff --exit-code main -- src/EngineeringBrain.Core/ProjectMemoryModels.cs src/EngineeringBrain.Infrastructure/ProjectMemoryService.cs src/EngineeringBrain.Infrastructure/LocalProjectMemoryStore.cs src/EngineeringBrain.Infrastructure/LocalReviewedConceptWriter.cs src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs src/EngineeringBrain.Infrastructure/ConceptCandidateReranker.cs src/EngineeringBrain.Infrastructure/InitiativeCandidateRetriever.cs
```

Expected: outbound metrics are 100%/0/0/0/0, Core tests pass, and protected-file diff exits `0`.

- [ ] **Step 5: Commit the offline security evaluation**

```powershell
git add evaluations/security-outbound-suite.json tests/EngineeringBrain.Core.Tests/OutboundSecurityEvaluationTests.cs tests/EngineeringBrain.Core.Tests/OutboundSecurityArchitectureTests.cs
git commit -m "test: add deterministic outbound security evaluation"
```

---

### Task 16: ADR, User Documentation, and Final Verification

**Files:**
- Create: `docs/decisions/0011-complete-security-policy-coverage.md`
- Modify: `README.md`
- Modify: `docs/architecture/README.md`
- Include: `docs/superpowers/specs/2026-09-12-security-policy-coverage-design.md`
- Include: `docs/superpowers/plans/2026-09-12-security-policy-coverage.md`

**Interfaces:**
- Consumes: all implemented behavior and acceptance evidence.
- Produces: concise architectural/user documentation and clean final verification.

- [ ] **Step 1: Run the failing documentation contract search**

```powershell
rg -n "SYS_REMOTE_RAW_SNAPSHOT|ApprovedReasoningRequest|Projected|Exact|security-outbound-suite|NotRecorded" README.md docs/decisions docs/architecture
```

Expected: nonzero or incomplete results because ADR 0011 and user-facing policy coverage documentation do not exist.

- [ ] **Step 2: Write ADR 0011 and bounded README updates**

ADR 0011 must document: five explicit BLOCK policies; shared catalog but separate outbound/recommendation evaluators; exact approval authority; non-public construction; complete-request fingerprinting; projected versus exact semantics; known-secret limits; safe provider failures; compatible artifact version bumps; and deferred behavior. README must state that pre-run CALL #2 is projected, actual sends receive an exact check, and historical assessments may be `NotRecorded`. Link ADR 0011 from `docs/architecture/README.md`.

- [ ] **Step 3: Verify documentation and commit it with the planning artifacts**

```powershell
rg -n "SYS_REMOTE_COMPLETE_REPOSITORY|SYS_REMOTE_RAW_SNAPSHOT|SYS_REMOTE_SOURCE_BODIES|SYS_REMOTE_SECRETS|SYS_REMOTE_ABSOLUTE_PATHS|ApprovedReasoningRequest|Projected|Exact|NotRecorded" README.md docs/decisions/0011-complete-security-policy-coverage.md docs/architecture/README.md
git diff --check
git add README.md docs/architecture/README.md docs/decisions/0011-complete-security-policy-coverage.md docs/superpowers/specs/2026-09-12-security-policy-coverage-design.md docs/superpowers/plans/2026-09-12-security-policy-coverage.md
git commit -m "docs: document complete outbound security coverage"
```

Expected: every required term is documented, `git diff --check` is empty, and only documentation/planning files enter this commit.

- [ ] **Step 4: Run fresh build and complete tests**

```powershell
dotnet build EngineeringBrain.sln --no-restore
dotnet test EngineeringBrain.sln --no-build --no-restore
```

Expected: build has 0 errors and 0 warnings; more than 394 tests pass with 0 failed and 0 skipped.

- [ ] **Step 5: Run deterministic outbound and retrieval evaluations**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-build --no-restore --filter "FullyQualifiedName~OutboundSecurityEvaluationTests|FullyQualifiedName~OutboundSecurityArchitectureTests"
dotnet run --project src/EngineeringBrain.Cli --no-build -- concepts status .
dotnet run --project src/EngineeringBrain.Cli --no-build -- concepts validate .
dotnet run --project src/EngineeringBrain.Cli --no-build -- eval .
```

Expected outbound: 16/16 expected decisions, false allowed `0`, false blocked `0`, diagnostic leaks `0`, blocked provider calls `0`. Status/validate must report `Valid` with 29 profiles. If the branch catalog is absent, first run `dotnet run --project src/EngineeringBrain.Cli --no-build -- concepts promote main feature/security-policy-coverage --repo .` after confirming the Git worktree is clean, then repeat status/validate; if the source is invalid or promotion is blocked, stop rather than forcing the artifact. Expected retrieval: cases `16`, passed `16`, regressions `0`, Recall@5 `1.000`, Recall@10 `1.000`, MRR `0.867`, reviewed status `Valid`, profiles `29`.

- [ ] **Step 6: Verify protected boundaries and clean repository state**

```powershell
git diff --exit-code main -- src/EngineeringBrain.Core/ProjectMemoryModels.cs src/EngineeringBrain.Infrastructure/ProjectMemoryService.cs src/EngineeringBrain.Infrastructure/LocalProjectMemoryStore.cs src/EngineeringBrain.Infrastructure/LocalReviewedConceptWriter.cs src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs src/EngineeringBrain.Infrastructure/ConceptCandidateReranker.cs src/EngineeringBrain.Infrastructure/InitiativeCandidateRetriever.cs
rg -n "GenerateStructuredAsync<.*ReasoningRequest|new ApprovedReasoningRequest" src
git diff --check
git status --short --branch
git log --oneline main..HEAD
```

Expected: protected-file diff exits `0`; no raw provider signature exists; the only approved-request construction is in `OutboundRequestGate.cs`; no whitespace errors or scratch artifacts exist; working tree is clean after commits. Do not squash and do not push.

## Final Self-Review Checklist

- All five policy IDs exist exactly once in stable order and preserve the existing complete-repository identity.
- Recommendation governance and concrete outbound security share definitions but remain separate evaluators.
- `ApprovedReasoningRequest` has no public constructor/factory and is created only by `OutboundRequestGate` in production.
- No production provider accepts a raw `ReasoningRequest`.
- The complete request's dynamic system and user text is inspected and fingerprinted.
- Fingerprints use versioned, length-prefixed exact UTF-8 bytes and exclude only non-transport accounting metadata.
- Projected assessments never create transport authority; exact approved objects are the objects sent.
- CALL #1 is reused from preparation where available; projected CALL #2 is never claimed to equal live CALL #2.
- Known secret values obey the frozen name/value rules and never enter results or artifacts.
- Provider exception text cannot reach CLI, initiative results, live JSON, or review Markdown.
- Preview schema 1, initiative schema 2, and live schema 2 remain readable as `NotRecorded`; new versions are 2/3/3.
- Multiple findings produce no more than five ordered results or diagnostics and zero provider calls.
- The outbound evaluation is deterministic and needs no network/provider call.
- Retrieval, Code Graph, Project Memory, reviewed concepts, lifecycle, A1/C1/S2/E2, graph expansion, and project ranking remain untouched.
- Full build/tests/evaluations and all acceptance checks are executable offline with no placeholders.
