# Complete Security Policy Coverage V1 Design

## Status

Approved architecture for Milestone 15 on `feature/security-policy-coverage`, based on `main` at `49adef1e67af94ee8f72e669e050efbd04145ad4`.

## Goal

Make outbound security an enforced, deterministic, auditable boundary. No repository- or initiative-derived model input may reach a remote transport unless the exact request was inspected and approved immediately before transport.

V1 covers exactly five blocking categories:

1. `CompleteRepository`
2. `RawSnapshot`
3. `SourceBodies`
4. `Secrets`
5. `AbsoluteLocalPaths`

V1 does not silently sanitize or redact payloads. A prohibited or unclassifiable exact request is blocked.

## Current Gap

`InitiativeAnalysisService`, `RemoteContextPreviewService`, and `LiveEvaluationService` call `OutboundContextGuard`, but `IReasoningProvider` and `OpenAIReasoningProvider` accept a raw `ReasoningRequest`. Security therefore depends on each caller remembering to perform the check. Preview and execution also construct CALL #2 independently: preview uses deterministic interpretation while execution uses live CALL #1 output.

`SYS_REMOTE_COMPLETE_REPOSITORY` currently governs a model-declared recommendation after CALL #2. It does not inspect transport bytes. The remaining four categories have guard diagnostics but no explicit architectural policy identity.

Current code evidence:

- `src/EngineeringBrain.Infrastructure/InitiativeAnalysisService.cs`: guards CALL #1 at lines 67-76 and CALL #2 at lines 94-103, then passes newly constructed raw requests.
- `src/EngineeringBrain.Infrastructure/LiveEvaluationService.cs`: independently repeats guard/request logic at lines 194-255.
- `src/EngineeringBrain.Infrastructure/RemoteContextPreviewService.cs`: uses deterministic interpretation and a separately built CALL #2 at lines 74-127.
- `src/EngineeringBrain.Core/Abstractions.cs`: `IReasoningProvider` accepts raw `ReasoningRequest` at lines 42-48.
- `src/EngineeringBrain.Infrastructure/OpenAIReasoningProvider.cs`: maps raw system/user strings into SDK input items at lines 73-118.
- `src/EngineeringBrain.Infrastructure/OutboundContextGuard.cs`: current four-category heuristics and fixed codes are at lines 19-156; complete repository has no payload category.
- `src/EngineeringBrain.Infrastructure/PolicyComplianceValidator.cs`: the sole current policy is `SYS_REMOTE_COMPLETE_REPOSITORY` at lines 10-77 and runs as recommendation governance at lines 80-145.
- `src/EngineeringBrain.Infrastructure/LocalLiveEvaluationStore.cs`: current error redaction is pattern-limited at lines 144-147.

## Architectural Boundaries

### SystemSecurityPolicyCatalog

Declares five immutable system definitions in deterministic order:

- `SYS_REMOTE_COMPLETE_REPOSITORY`
- `SYS_REMOTE_RAW_SNAPSHOT`
- `SYS_REMOTE_SOURCE_BODIES`
- `SYS_REMOTE_SECRETS`
- `SYS_REMOTE_ABSOLUTE_PATHS`

Each definition contains policy ID, version `1`, `PolicyContentScope`, `PolicySeverity.Block`, and a fixed safe diagnostic. The existing complete-repository ID and recommendation behavior remain compatible.

### OutboundContextGuard

Inspects concrete content and structured context. It emits bounded `OutboundInspectionFinding` values and makes no recommendation-governance decision. Findings contain category, structured reason, trigger kind, and count only. They never retain matched content.

### OutboundPolicyEvaluator

Maps inspection findings to all five catalog definitions. It emits one result per policy in catalog order and calculates the overall outbound outcome. Missing, duplicate, or unknown required definitions fail closed.

### OutboundRequestGate

Owns exact-request approval. It inspects all model-visible dynamic text in `ReasoningRequest`, validates structured context representation, evaluates policies, and creates an `ApprovedReasoningRequest` only for an `Exact` and `Allowed` assessment. It calculates the canonical payload fingerprint only after the payload is allowed.

Projected assessment returns only `OutboundPolicyAssessment`; it never creates a transportable request.

### ApprovedReasoningRequest and provider boundary

`ApprovedReasoningRequest` and `IReasoningProvider` live in `EngineeringBrain.Infrastructure`. The approved type is public because callers and test providers must consume it, but its constructor is `internal`. `OutboundRequestGate` is the only production creation site. `EngineeringBrain.Infrastructure` grants `InternalsVisibleTo` only to `EngineeringBrain.Core.Tests`, allowing an adversarial integrity test to construct a deliberately invalid instance without exposing a production factory.

`IReasoningProvider.GenerateStructuredAsync<T>` accepts `ApprovedReasoningRequest`, not raw `ReasoningRequest`. `OpenAIReasoningProvider` verifies the assessment is exact and allowed and recomputes the fingerprint before constructing SDK options. Ordinary callers outside the Infrastructure assembly cannot directly construct a transportable request.

This is architectural misuse prevention inside the product, not a hostile-code sandbox. Reflection is used only to inspect the public API surface; it is not used to fabricate approval. An API-surface regression test confirms there is no public constructor and that the provider contract accepts only the approved type, while the friend test verifies an internally forged fingerprint mismatch is rejected.

### PolicyComplianceValidator

Recommendation governance remains separate. It evaluates model-declared remote actions against the same five policy definitions after evidence validation. It does not approve transport requests.

## Data Flow

```text
raw inputs
-> ReasoningRequest + structured outbound representation
-> OutboundContextGuard.Inspect
-> OutboundPolicyEvaluator.Evaluate
-> OutboundRequestGate.ApproveExact
-> ApprovedReasoningRequest
-> IReasoningProvider
-> OpenAI SDK transport
```

The provider may add fixed protocol metadata, response schema, and transport options. It may not add repository- or initiative-derived text that was absent from the approved request.

## Preview Semantics

`OutboundAssessmentKind` has `NotRecorded`, `Projected`, and `Exact`.

- CALL #1 is fully known before execution. CLI preparation creates one exact approved request, reports its safe assessment, and reuses that same object for execution.
- Offline CALL #2 preview is `Projected` because the live understanding does not yet exist. It uses the same context builder, token budgets, policy catalog, guard, and evaluator, but it does not claim byte equality.
- After CALL #1, execution builds the real CALL #2, creates an `Exact` assessment, and passes that exact approved request to the provider.
- Live evaluation uses the same gate. It does not retain its current independent raw-request send path.

Persisted output labels projected and exact assessments explicitly. Missing historical assessments are `NotRecorded`, never `Allowed`.

## Domain Model

Core adds these serializable result concepts:

- `OutboundAssessmentKind`: `NotRecorded`, `Projected`, `Exact`.
- `OutboundPolicyOutcome`: `NotRecorded`, `Allowed`, `Blocked`.
- `OutboundInspectionReasonCode`: fixed detector reasons.
- `OutboundTriggerKind`: `PayloadKind`, `ContentPattern`, `KnownSecretValue`, `SourceReference`, `ContextIntegrity`, `OperationalFailure`.
- `OutboundInspectionFinding`: scope/category, reason, trigger, bounded count.
- `OutboundPolicyResult`: policy ID/version/category/outcome/reason/safe diagnostic/finding count.
- `OutboundPolicyAssessment`: kind, overall outcome, ordered results, bounded diagnostics, payload fingerprint.

`ApprovedReasoningRequest` is an Infrastructure runtime type and is not a persisted schema.

## Canonical Fingerprint

`OutboundRequestFingerprint.Create(ReasoningRequest)` computes SHA-256 over versioned, length-prefixed UTF-8 fields in this order:

1. literal `engineering-brain/reasoning-request/v1`
2. numeric `ReasoningStage` encoded big-endian
3. `Model`
4. `SystemInstructions`
5. `UserData`
6. `MaximumOutputTokens` encoded big-endian

Every string is prefixed by its UTF-8 byte length encoded as a four-byte big-endian integer. No newline normalization, platform serialization, dictionaries, absolute paths, or `GetHashCode` participate. `EstimatedInputTokens` is excluded because it is local accounting metadata and is not transported.

Fixed provider response schemas and reasoning options are application-owned protocol metadata. Tests bind each stage/result type to the expected fixed schema and prove no repository-derived field is added.

## Detection Semantics

### CompleteRepository

Block:

- `ContextSegmentKind.CompleteRepository`;
- a structured repository envelope containing a repository identity and a `files` array whose entries contain both `path` and `content`;
- a recognized recursive file payload containing three or more distinct file-content boundaries.

Allow selected Project Memory notes, graph entities, graph relations, repository counts, and bounded evidence. Initiative prose that merely discusses repository upload is not itself a repository payload.

### RawSnapshot

Block `ContextSegmentKind.RawSnapshot` and a complete snapshot object containing `schemaVersion`, `repository`, `git`, `projects`, `entities`, and `relations`. Allow derived counts and selected entity/relation summaries.

### SourceBodies

Block `ContextSegmentKind.SourceBody` and high-confidence complete C-family or Python declaration/body shapes. Allow names, signatures without bodies, relative source references, and generated Project Memory facts. V1 has no allowed source-excerpt representation.

### Secrets

Combine existing high-confidence syntax checks with exact ordinal matching against a command-scoped in-memory secret set.

Environment variable names are normalized to uppercase with `-` converted to `_`. A value is eligible only when the name contains a token-boundary match for `SECRET`, `TOKEN`, `PASSWORD`, `PASSWD`, `API_KEY`, `APIKEY`, `ACCESS_KEY`, `PRIVATE_KEY`, or `CONNECTION_STRING`; the trimmed value has at least eight characters; and it is not one of `true`, `false`, `yes`, `no`, `on`, `off`, `none`, `null`, `development`, `production`, `staging`, `test`, `local`, `localhost`, `changeme`, `placeholder`, `example`, or `dummy`, case-insensitively.

Values are deduplicated with ordinal comparison, remain memory-only, and never enter findings, fingerprints, diagnostics, or artifacts. V1 does not claim to detect unknown, encoded, or deliberately obfuscated secrets.

### AbsoluteLocalPaths

Block drive-rooted Windows paths, UNC paths, `file://` URIs, and high-confidence Unix roots `/home`, `/Users`, `/tmp`, `/var`, `/etc`, `/opt`, `/srv`, `/workspace`, `/mnt`, and `/Volumes`. Structured source references must be non-rooted repository-relative paths with no `..` segment and no absolute URI. Allow `src/Foo.cs` and equivalent normalized repository-relative paths.

## Bounded Results

There are exactly five policy results per successful evaluation. Results follow catalog order. Each detector aggregates duplicate matches into one finding per category, caps `FindingCount` at `99`, and marks `FindingCountCapped` when more matches exist. Assessments contain at most five fixed diagnostics. No per-match text is retained. Blocked assessments have a null payload fingerprint so a secret-bearing request does not produce a persistent or user-visible secret hash.

## Failure Semantics

- Any prohibited finding: `Blocked`; provider invocation count remains zero.
- Malformed or unrepresented context: fail closed with fixed code `OUTBOUND_CONTEXT_INVALID`.
- Missing, duplicate, or unknown required policy: fail closed with `OUTBOUND_POLICY_CATALOG_INVALID`.
- Detector/evaluator/integrity failure: fail closed with a fixed safe diagnostic.
- Approved-request fingerprint mismatch: block before SDK construction.
- Cancellation: propagate; do not convert it into a policy result or retry.
- Remote disabled: retain existing authorization behavior.
- Provider unavailable: return a stable provider failure code and fixed bounded message; never expose underlying exception text.

## Provider Error Safety

`ReasoningProviderException` carries a stable `ReasoningProviderFailureCode`, safe message, stage, usage, duration, and retries. Its public/user-visible message is selected from fixed text. Raw provider exceptions are never concatenated into it.

`LiveEvaluationService`, local stores, and CLI render only the stable code and safe message. `LocalLiveEvaluationStore.Redact` remains defense in depth and additionally bounds/control-sanitizes output, but persisted safety cannot depend on regex redaction of arbitrary `exception.Message`.

## Schema Evolution

No repository snapshot, Project Memory, reviewed-concept, retrieval, or lifecycle schema changes.

New writes use:

- remote preview schema `2` instead of `1`;
- persisted initiative analysis schema `3` instead of `2`;
- live evaluation result schema `3` instead of `2`.

Readers accept the immediately preceding version and the current version. Legacy files with no structured outbound assessment normalize to `NotRecorded`; they are never interpreted as allowed. Existing files are not rewritten.

## Evaluation

`evaluations/security-outbound-suite.json` is deterministic and offline. It includes blocking and safe-control cases for all five categories, multiple simultaneous violations, malformed context, known secret values supplied by fixture-local injection, and provider-attempt assertions.

Acceptance metrics are operational:

- expected blocked versus actual blocked;
- false allowed count;
- false blocked count;
- diagnostic leakage count;
- provider invocation attempts for blocked cases.

Required result: 100% expected decisions, zero false allowed, zero diagnostic leaks, and zero blocked provider attempts.

## Compatibility and Protected Areas

The normal local-only path and explicit `--allow-remote` authorization remain unchanged. Retrieval ranking, Code Graph, Project Memory, reviewed concepts, A1/C1/S2/E2, graph expansion, project ranking, and lifecycle commands are outside scope.

The standard offline retrieval evaluation must remain 16/16 with Recall@5 and Recall@10 `1.000`, MRR `0.867`, reviewed concept status `Valid`, and 29 active profiles.

## Deferred

- allowed bounded source excerpts;
- arbitrary or semantic secret classification;
- archive/base64/encoded payload inspection;
- configurable project policy language;
- remote policy services;
- provider plugin framework;
- UI and policy authoring;
- automatic sanitization/redaction.
