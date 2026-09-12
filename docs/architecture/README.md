# Architecture

Engineering Brain separates deterministic repository facts from future derived knowledge.

```text
CLI
 |-- repository scan and Git metadata (Infrastructure)
 |-- language analysis (language-specific analyzers)
 `-- snapshot persistence (Infrastructure)

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

The structured code graph records objective facts. A future project-memory wiki will contain derived architecture, capabilities, initiatives, decisions, and risks. Project memory can cite the graph and source evidence, but it cannot replace code as the source of truth.

Snapshot schema 2 includes branch, commit, timestamp, files, hashes, projects, analysis modes, diagnostics, entities, relations, and categorical resolution levels so a later iteration can compare repository states and selectively re-analyze changed files.
