# ADR 0004: Deterministic Project Memory

- Status: Accepted
- Date: 2026-09-10

## Context

Snapshot schema 3 is the objective machine-readable code graph, but its complete entity and relation collections are not efficient for human navigation or future context routing. Sending that graph or source repository wholesale to an LLM would be noisy, expensive, and unsafe.

## Decision

Derive compact, deterministic Markdown knowledge exclusively from a validated current snapshot. A memory sync first runs the repository analysis engine, then generates a root index, factual architecture overview, project notes, and notes for top-level classes, interfaces, records, and enums. Nested types and bounded member signatures remain inside their containing component note.

Project Memory has its own versioned manifest and knowledge schema. Managed notes carry repository, branch, snapshot schema, analyzer, source identity, resolution or analysis quality when applicable, and a stable source fingerprint. Collision-safe filenames and resolvable wiki links support hierarchical navigation.

## Source of truth

Source code and the validated snapshot/code graph outrank Project Memory. Memory is a reproducible projection of snapshot facts. Contradictory, corrupt, or incompatible memory is rebuilt or rejected; it is never used to override the graph.

## Isolation and incrementality

Knowledge is stored outside the analyzed repository and isolated by repository ID and a filesystem-safe, hash-suffixed branch key. Cross-branch reuse is not supported.

Fingerprints omit timestamps and elapsed durations. An unchanged note is reused without rewriting its file. Stale files are deleted only when a compatible prior manifest identifies them as managed; unmanaged files are preserved. Writes are atomic per file, and the complete planned and persisted memory is integrity-checked.

## Security

Generation reads snapshot metadata only. Notes contain bounded signatures and relative source locations, not method bodies, complete source files, diagnostic messages, secrets, credentials, or absolute source paths.

## Consequences

The result is local, human-readable, branch-aware knowledge suitable for future retrieval while remaining auditable against deterministic evidence. It intentionally does not infer architecture, capabilities, risks, or intent.

Future LLM-enriched knowledge will be layered on top of this deterministic truth and must not overwrite it.
