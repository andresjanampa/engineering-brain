# Engineering Brain

Engineering Brain is a local-first engineering intelligence platform. Its goal is to understand a software repository from evidence in code and Git, preserve that knowledge over time, and eventually support impact analysis and implementation planning without sending entire repositories to an LLM.

## Current status

This repository contains the project-aware, incremental MVP foundation. The `brain scan` workflow scans a repository locally and writes a structured snapshot containing:

- repository and current Git metadata;
- discovered files, sizes, and content hashes where safe and reasonable;
- technology statistics for C#, JavaScript, TypeScript, SQL, Razor, JSON, XML, YAML, Python, Java, solutions, and MSBuild projects;
- C#/.NET solutions, projects, target frameworks, project documents, and evaluated project references;
- namespaces, classes, interfaces, records, enums, methods, constructors, and properties;
- containment, project-reference, inheritance, and interface-implementation relations;
- file and line evidence plus a categorical resolution level for every entity and relation;
- repository and per-project analysis modes with bounded, sanitized diagnostics.
- an explainable file delta, affected and reused projects, reuse metrics, and graph-integrity results.

For C#, Engineering Brain discovers all `.sln` and `.csproj` files, loads each project through Roslyn's `MSBuildWorkspace`, and uses its real `Compilation` and `SemanticModel` when project loading is trustworthy. Projects present in multiple solutions are analyzed once. If one project cannot be loaded reliably, only that project falls back to syntax analysis; successfully loaded projects remain semantic.

After a compatible first scan, Engineering Brain compares the current filesystem with `latest.json` using existing content hashes and safe file metadata, with local Git evidence used for branch, commit, and proven rename information. Changed projects and their transitive dependents are reanalyzed; demonstrably unaffected projects are reused. Branch, solution, and global build-configuration changes conservatively trigger a full semantic reanalysis. A no-change scan reuses every project without invoking Roslyn.

Resolution levels describe how a fact was obtained: `Exact` for evaluated project structure, `Semantic` for Roslyn symbol resolution, `Syntactic` for syntax-tree evidence, and `Unresolved` for information that must not be represented as a resolved relationship. Overall analysis is labeled `FullSemantic`, `PartialSemantic`, or `SyntaxFallback`.

Generated snapshots contain derived metadata, never source file contents. By default they are saved outside the analyzed repository at:

```text
~/.engineering-brain/repositories/<repository-id>/snapshots/latest.json
```

## Requirements

- .NET SDK 10
- Git available on `PATH` for branch and commit metadata (scanning also works without Git)

No API key, external service, database, container, or network request is required.

## Build and test

```powershell
dotnet build EngineeringBrain.sln
dotnet test EngineeringBrain.sln
```

## Scan a repository

During development:

```powershell
dotnet run --project src/EngineeringBrain.Cli -- scan .
```

After publishing or installing the executable as `brain`:

```powershell
brain scan [path]
```

When `path` is omitted, the current directory is used. When invoked inside a Git working tree, the scanner resolves the repository root before analysis.

## Not implemented yet

This foundation does not yet include public API fingerprints, method-level incremental analysis, multi-target-framework expansion, a complete call graph, dependency-injection resolution, impact recommendations, initiative parsing, retrieval, project-memory generation, a UI, or any LLM integration. Unsupported or unresolved relationships are omitted instead of guessed. Analysis never runs `dotnet restore` on a target repository; projects that require unavailable local dependencies degrade gracefully.

See [the initial architecture decision](docs/decisions/0001-local-first-evidence-first.md), [the project-aware analysis decision](docs/decisions/0002-project-aware-semantic-analysis.md), [the incremental analysis decision](docs/decisions/0003-incremental-analysis.md), and [the architecture overview](docs/architecture/README.md) for the boundaries that guide future work.
