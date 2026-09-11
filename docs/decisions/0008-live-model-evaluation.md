# ADR 0008: Live model evaluation

- Status: Accepted
- Date: 2026-09-11

## Context

Deterministic retrieval has a stable tuning/holdout baseline, but that does not establish the quality of real model interpretation or architecture reasoning. CALL #1 can damage retrieval through weak requirements or search terms, while CALL #2 can return unhelpful recommendations or unsupported evidence even when retrieval is sound.

## Decision

Keep `brain eval` as the offline regression harness and add an opt-in `brain eval-live` harness. Evaluate CALL #1 independently against structured human expectations, compare retrieval from golden and live understandings, then evaluate CALL #2 after deterministic evidence validation. Automatic metrics cover field and capability coverage, ambiguity, retrieval deltas, decisions, references, evidence, usage, latency, retries, and cross-run consistency. Architectural usefulness remains an explicit human review using a six-part 1-5 rubric.

The default provider configuration remains `gpt-5.6-luna` with low reasoning effort for CALL #1 and `gpt-5.6-sol` with medium effort for CALL #2. Live results are observations and never automatically change retrieval weights or the offline baseline.

## Safety

During this iteration dogfooding is limited to Engineering Brain and five versioned, non-sensitive initiatives. A remote run requires both `OPENAI_API_KEY` and `--allow-remote`; preview and fake-provider modes perform no provider call. The command is bounded to five cases, three explicit runs per selected case, and ten logical calls total, and displays the planned calls and maximum input before execution.

`OutboundContextGuard` runs immediately before every provider call. CALL #1 contains only the untrusted initiative and structural instructions. CALL #2 contains only bounded, provenance-bearing context segments. Source bodies, secrets, absolute source paths, raw snapshots, credentials, and provider request objects are excluded. Failed and partial case results are persisted with redacted errors outside the repository.

## Consequences

Live runs produce a schema-versioned summary, per-case JSON, and editable review Markdown under the repository-scoped local data store. Provider-reported actual, cached, output, and available reasoning tokens remain distinct from estimates. Optional cost estimation uses an external pricing file because prices are not product constants. Real model quality and consistency can now be measured without weakening deterministic evidence or turning billable experiments into automatic tests.
