# Architecture

Engineering Brain separates deterministic repository facts from future derived knowledge.

```text
CLI
 |-- repository scan, Git metadata, and snapshot comparison (Infrastructure)
 |-- language analysis (language-specific analyzers)
 |-- snapshot persistence (Infrastructure)
 `-- project-memory synchronization (Infrastructure)

Core
 |-- domain records
 `-- implementation-agnostic contracts
```

`EngineeringBrain.Core` owns snapshot concepts and contracts without depending on Roslyn, Git, filesystem implementations, or an LLM provider. `EngineeringBrain.Infrastructure` discovers files, reads Git metadata, and stores snapshots. Each language analyzer implements `ILanguageAnalyzer`; the first implementation uses Roslyn and `MSBuildWorkspace` for project-aware C# analysis.

The C# pipeline is:

```text
solution/project discovery
    -> MSBuild project loading and deduplication
    -> per-project Compilation and SemanticModel
    -> semantic entity and relationship extraction
    -> per-project syntax fallback when loading is not trustworthy
```

Project failures are isolated. Workspace failures are associated with a project only when its path or unique project filename is present in the diagnostic; ambiguous failures remain repository diagnostics and do not silently degrade unrelated projects.

The structured code graph records objective facts. Deterministic Project Memory projects validated snapshot facts into compact Markdown root, architecture, project, and top-level component notes. Every managed note carries provenance and a stable source fingerprint. Memory is isolated by repository and branch, and can cite the graph and source evidence, but it cannot replace code or the snapshot as the source of truth. Future LLM-enriched knowledge will be layered above this deterministic foundation.

Incremental scans compare schema-compatible snapshots, resolve changed files to projects, invalidate transitive dependents, selectively invoke analyzers, merge reusable graph partitions, and validate IDs and relation endpoints before atomically replacing `latest.json`.

Snapshot schema 3 adds safe file timestamps, file changes, project reuse decisions, performance metrics, and graph-integrity results to the existing evidence contract.

Project Memory has an independent knowledge schema. Its manifest controls incremental reuse and stale managed-note deletion; unmanaged files are outside its ownership. Notes use deterministic LF output, collision-safe filenames, bounded collections, relative source evidence, and resolvable wiki links.

Reviewed semantic concepts remain a separate human-reviewed layer. Runtime loading is read-only; cross-branch lifecycle operations are defined by [ADR 0010](../decisions/0010-reviewed-concept-lifecycle.md) and do not change Project Memory ownership or retrieval semantics.
