# ADR 0003: Conservative project-level incremental analysis

- Status: Accepted
- Date: 2026-09-10

## Context

Rebuilding the complete semantic graph on every scan does not scale to large repositories. The schema 2 snapshot already records stable entity IDs, project references, Git state, and content hashes, but the scan did not use that evidence for reuse.

## Decision

Compare a compatible previous snapshot with the current filesystem. Content hashes prove changes for analyzable files; safe size and modification-time metadata compare files that policy forbids hashing. Local Git augments this evidence with branch, commit, and rename facts, but is never the only change source.

Map changed C# and project files to their owning projects. Reanalyze directly affected projects and all transitive dependents in the union of the previous and current `ReferencesProject` graph. Reuse only current projects outside that affected set. Granularity is deliberately project-level: a project change invalidates dependents without attempting to classify public API impact.

Merge by removing every entity and relation owned by invalidated or deleted projects, adding regenerated results, deduplicating relations, and rejecting duplicate entity IDs or dangling internal relations before persistence. A no-change scan invokes no language analyzer.

## Conservative boundaries

- A branch change triggers a full scan; cross-branch reuse is not supported.
- A solution change triggers a full scan so discovery and membership are rebuilt.
- Changes to `Directory.Build.*`, `Directory.Packages.props`, `global.json`, or `NuGet.Config` trigger a full semantic reanalysis.
- An unmapped changed C# file triggers a full scan.
- Corrupt, incompatible, foreign, or otherwise untrusted snapshots are ignored and regenerated.
- Renames are represented only when Git reports them; otherwise they remain delete plus add.

## Persistence

Schema 3 persists the explainable change set, affected/reused projects, reuse metrics, graph-integrity result, and safe file timestamps. A new snapshot is serialized to a temporary file only after successful analysis and validation, then moved over `latest.json`. A failed scan leaves the previous snapshot intact.

## Consequences

Incremental reuse is deterministic, local, and intentionally conservative. Some changes will reanalyze more projects than strictly necessary. Public API and symbol fingerprints may later narrow invalidation, but method-level incremental analysis is outside this decision.
