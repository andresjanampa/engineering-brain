# Engineering Brain

Engineering Brain is a local-first engineering intelligence platform. Its goal is to understand a software repository from evidence in code and Git, preserve that knowledge over time, and eventually support impact analysis and implementation planning without sending entire repositories to an LLM.

## Current status

This repository contains the project-aware, incremental foundation, deterministic Project Memory, the first evidence-validated initiative-analysis workflow, and an offline evaluation harness. Deterministic lexical retrieval uses field-specific scoring, bounded term-rarity and member signals, and a contextual test-project penalty before any model call. The `brain scan` workflow scans a repository locally and writes a structured snapshot containing:

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

`brain memory sync` first ensures that snapshot is current, then derives compact Markdown knowledge from the validated graph. Knowledge is isolated by repository and branch, carries source provenance and stable fingerprints, and is only rewritten when its relevant source facts change. Generated notes live outside the analyzed repository at:

```text
~/.engineering-brain/repositories/<repository-id>/knowledge/branches/<branch-key>/
```

Project Memory is a navigable projection, not a source of truth. The snapshot and code graph always take precedence.

Reviewed semantic concepts are an optional, separate local layer used by retrieval version `lexical-graph-concept-v1`. A reviewed catalog is read only from the current repository and branch location:

```text
~/.engineering-brain/repositories/<repository-id>/knowledge/branches/<branch-key>/semantic/reviewed-concepts.json
```

`brain memory sync` never creates, edits, or deletes this file. A missing or invalid catalog falls back to the exact lexical path; an invalid declaration or stale assignment is excluded locally and reported through safe diagnostics, yielding `ValidWithDiagnostics` when usable siblings remain. `AnchorTokens` and `QualificationSupportTokens` must qualify a clear reviewed concept before `ContextSupportTokens` can contribute to the bounded score. Concepts only rerank candidates that already have positive lexical evidence, add at most six points, preserve exact-identity precedence, and never affect project ranking or graph-expanded candidates. Catalog definitions and reviewer prose are not copied into generated Markdown or CALL #2 context.

`brain analyze` adds two bounded reasoning stages. The first converts an initiative into structured requirements. Deterministic retrieval then ranks exhaustive snapshot entities and graph relations, builds a compact context from selected Project Memory, and asks a second model for structured architecture recommendations. Every recommendation is subsequently validated against the current repository and branch evidence; invented identities and relations remain explicitly invalid.

`brain eval` benchmarks deterministic retrieval, context selection, token budgets, and the evidence contract against a versioned, non-sensitive golden corpus. It is offline and reuses one validated snapshot and Project Memory sync for the whole suite.

`brain eval-live` is a separate, explicitly authorized harness for measuring the real provider pipeline. Live evaluation schema 2 separates initiative-only understanding expectations, repository retrieval expectations, architectural analysis expectations, and local policy expectations. Capability and unknown-topic expectations use exact normalized tokens by default; a fixture may declare reviewed lexical alternatives, and a match requires every token from one alternative to occur in one reported item. Alternatives are never inferred. Policy metrics are calculated from governed recommendations after evidence validation. Live observations do not update retrieval weights or the offline baseline, and historical result artifacts are never rewritten.

## Requirements

- .NET SDK 10
- Git available on `PATH` for branch and commit metadata (scanning also works without Git)

Scanning and Project Memory require no API key, external service, database, container, or network request. Initiative analysis currently supports the OpenAI provider and reads its credential only from `OPENAI_API_KEY`.

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

## Synchronize Project Memory

During development:

```powershell
dotnet run --project src/EngineeringBrain.Cli -- memory sync .
```

After publishing or installing the executable as `brain`:

```powershell
brain memory sync [path]
```

The first sync initializes `manifest.json`, a root index, an architecture overview, project notes, and bounded top-level component notes. Later syncs compare deterministic source fingerprints, reuse unchanged notes, remove only stale managed notes, and preserve unmanaged files.

## Analyze an initiative

Inspect the planned outbound metadata without an API key, authorization, or network call:

```powershell
dotnet run --project src/EngineeringBrain.Cli -- analyze evaluations/cases/python-analyzer/initiative.md --repo . --preview
```

Preview uses a clearly labeled deterministic interpretation. It prints counts, token estimates, security findings, selected candidate totals, and the external manifest path; initiative and context text are not printed or persisted in the manifest.

Set the provider credential in the process environment, then explicitly authorize remote reasoning:

```powershell
$env:OPENAI_API_KEY = "..."
dotnet run --project src/EngineeringBrain.Cli -- analyze initiative.md --repo . --allow-remote
```

After publishing or installing:

```powershell
brain analyze initiative.md --repo . --allow-remote
```

`--repo` defaults to the current directory. Models can be changed independently with `--interpretation-model` and `--reasoning-model`, or through `ENGINEERING_BRAIN_INTERPRETATION_MODEL` and `ENGINEERING_BRAIN_REASONING_MODEL`. Defaults are `gpt-5.6-luna` and `gpt-5.6-sol`.

Reasoning effort defaults to `low` for interpretation and `medium` for architecture analysis. Configure it with `--interpretation-effort` / `--analysis-effort`, or `ENGINEERING_BRAIN_INTERPRETATION_REASONING_EFFORT` / `ENGINEERING_BRAIN_ANALYSIS_REASONING_EFFORT`. Supported values are `low`, `medium`, and `high`; usage reports record the selected value.

The command first refreshes the snapshot and branch-specific Project Memory. Call 1 sends only the initiative text and structural instructions. Call 2 sends the structured understanding, compact repository/Git facts, selected managed notes, relative evidence paths, identities, and selected graph relations. It does not send repository files, source bodies, the complete snapshot, `.env` or configuration contents, credentials, connection strings, or secrets. Responses API storage is disabled. Structured local analysis records are stored outside the repository under the repository and branch identity; they contain the initiative content hash and filename, not its full text.

`--allow-remote` is mandatory and non-interactive so local and CI behavior is explicit. Merely defining `OPENAI_API_KEY` never makes `scan` or `memory sync` use the network.

The Python-analyzer initiative above is the safe manual live-smoke fixture. A live smoke is never automatic and requires both `OPENAI_API_KEY` and `--allow-remote`; the command prints its validated preview before calling the provider.

## Evaluate retrieval and context

Run the versioned suite offline:

```powershell
dotnet run --project src/EngineeringBrain.Cli -- eval .
```

The report separates tuning, holdout, and all-case metrics, including entity Recall/Precision at 5 and 10, MRR, project Recall@3/MRR, test-candidate noise at 5 and 10, category breakdowns, context token distribution, evidence validation, clarification behavior, and regressions. A structured runtime result is written outside the repository under `~/.engineering-brain`; it contains metrics and identities, not prompts or source context.

Create or deliberately accept a new baseline only through:

```powershell
dotnet run --project src/EngineeringBrain.Cli -- eval . --update-baseline
```

Normal evaluation never rewrites `evaluations/baseline.json`. Regression thresholds cover critical retrieval decreases, increased test noise, and excessive context growth. Exit code `0` means the golden suite and baseline pass, `3` means an evaluation contract or regression failed, and `1` means execution failed.

## Evaluate the live model pipeline

Inspect the bounded five-case plan without a key or network access:

```powershell
dotnet run --project src/EngineeringBrain.Cli -- eval-live . --preview
```

Exercise the complete harness offline with deterministic provider responses:

```powershell
dotnet run --project src/EngineeringBrain.Cli -- eval-live . --fake-provider
```

A billable run requires both `OPENAI_API_KEY` and explicit authorization:

```powershell
$env:OPENAI_API_KEY = "..."
dotnet run --project src/EngineeringBrain.Cli -- eval-live . --allow-remote
```

The default plan runs five safe, versioned initiatives with CALL #1 on `gpt-5.6-luna` at low effort and CALL #2 on `gpt-5.6-sol` at medium effort: 10 logical provider calls, with at most one retry per call. `--case <id>` focuses a case and `--runs 1-3` enables explicit repeatability measurement; the hard command limit remains 10 logical calls. The pre-run summary shows cases, runs, models, efforts, maximum input, attempts, authorization, repository, and branch before any provider call.

Results are written outside the repository under `~/.engineering-brain/repositories/<repository-id>/evaluations/live/<run-id>/`. `summary.json` and per-case JSON contain automatic quality, retrieval, evidence, usage, latency, retry, consistency, and security metrics. `review.md` contains the structured outputs and empty 1-5 fields for initiative understanding, architectural relevance, recommendation usefulness, evidence discipline, uncertainty handling, and actionability. Provider errors are redacted, credentials and SDK request objects are never persisted, and optional local pricing can be supplied with `--pricing <pricing.json>`; no prices are built into the product.

Use `eval-live . --preview`, then `eval-live . --fake-provider`, before authorizing a real run. Live evaluation is intended for this repository and its non-sensitive fixture corpus during this iteration; it never sends source bodies, secrets, absolute source paths, or a raw snapshot.

## Not implemented yet

This foundation does not yet include public API fingerprints, method-level incremental analysis, multi-target-framework expansion, a complete call graph, dependency-injection resolution, source-body retrieval, embeddings, semantic/vector search, automatic implementation, a UI, or complete impact analysis. Initiative retrieval is lexical and graph-bounded; it finds integration candidates, not guaranteed implementation locations. Unsupported or unresolved relationships are omitted instead of guessed. Analysis never runs `dotnet restore` on a target repository; projects that require unavailable local dependencies degrade gracefully.

See [the initial architecture decision](docs/decisions/0001-local-first-evidence-first.md), [the project-aware analysis decision](docs/decisions/0002-project-aware-semantic-analysis.md), [the incremental analysis decision](docs/decisions/0003-incremental-analysis.md), [the deterministic Project Memory decision](docs/decisions/0004-deterministic-project-memory.md), [the initiative-analysis decision](docs/decisions/0005-llm-initiative-analysis.md), [the evaluation and preview decision](docs/decisions/0006-evaluation-and-remote-preview.md), [the lexical ranking decision](docs/decisions/0007-retrieval-ranking.md), [the live model evaluation decision](docs/decisions/0008-live-model-evaluation.md), [the reviewed concept reranking decision](docs/decisions/0009-reviewed-concept-reranking.md), and [the architecture overview](docs/architecture/README.md) for the boundaries that guide future work.
