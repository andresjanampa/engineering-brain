# ADR 0011: Complete security policy coverage

- Status: Accepted
- Date: 2026-09-13

## Context

Explicit remote authorization controls whether Engineering Brain may call a provider, but authorization alone does not prove that the exact model-visible request is safe. The outbound boundary must independently prevent complete repositories, raw snapshots, source bodies, secrets, and absolute local paths from leaving the machine. Previewed context also cannot be treated as proof about a later request whose exact bytes do not yet exist.

## Decision

Define five version-one system BLOCK policies in one immutable catalog:

- `SYS_REMOTE_COMPLETE_REPOSITORY`
- `SYS_REMOTE_RAW_SNAPSHOT`
- `SYS_REMOTE_SOURCE_BODIES`
- `SYS_REMOTE_SECRETS`
- `SYS_REMOTE_ABSOLUTE_PATHS`

Recommendation governance and concrete outbound inspection share these identities but remain separate evaluators. Recommendation governance asks whether a proposed action is permitted; outbound security asks whether the concrete request may leave the machine.

`OutboundRequestGate` inspects the complete dynamic system instructions, user data, and represented structured context. Only an allowed `Exact` assessment can create the non-publicly-constructible `ApprovedReasoningRequest` accepted by providers. The approved request carries a SHA-256 fingerprint over versioned, length-prefixed exact UTF-8 fields; only accounting-only estimated input tokens are excluded. Providers verify integrity before mapping transport payloads.

Pre-run CALL #2 checks are `Projected` and never grant transport authority. Actual CALL #1 and CALL #2 sends receive `Exact` checks. CALL #1 approval prepared before retrieval is reused when its request remains identical.

Known-secret values are captured once per command from high-confidence environment-variable names. Values are trimmed, must contain at least eight characters, and exclude fixed common labels and placeholders. This is a bounded exact-value defense plus high-confidence syntax detection, not arbitrary secret classification. Secret values and triggering source/path text are not retained in findings, diagnostics, fingerprints for blocked requests, or persisted results.

Provider exceptions cross the boundary only as fixed failure codes and messages. Preview artifacts now use schema 2, initiative analyses schema 3, and live results schema 3. Preview schema 1, initiative schema 2, and live schema 2 remain readable; missing historical assessments become `NotRecorded`, which is never interpreted as allowed.

The deterministic `evaluations/security-outbound-suite.json` covers all five categories, safe controls, multiple violations, provider-attempt prevention, and diagnostic non-disclosure without a network call.

## Consequences

No production transport accepts a raw `ReasoningRequest`. Multiple violations yield at most five catalog-ordered policy results and diagnostics, and any malformed context, inspection failure, catalog failure, or integrity mismatch fails closed.

V1 deliberately does not redact or sanitize outbound payloads, inspect archives or arbitrary encodings, classify secrets semantically, support source excerpts, add configurable policies, or change retrieval, graph, Project Memory, or reviewed-concept behavior. Those capabilities require separate evidence and design.
