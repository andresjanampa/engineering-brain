# ADR 0002: Project-aware semantic C# analysis

- Status: Accepted
- Date: 2026-09-10

## Context

Compiling every discovered `.cs` file into one synthetic compilation does not represent real solution boundaries. It loses evaluated project references, compiler options, target frameworks, conditional symbols, and the semantic distinction between identically named symbols in different projects.

Enterprise and legacy repositories also contain projects that cannot be loaded completely because an SDK, package, target, or corporate dependency is unavailable. Failing the whole scan would discard valid evidence from healthy projects.

## Decision

Discover every `.sln` and `.csproj`, then use Roslyn `MSBuildWorkspace` to obtain a real `Compilation` and `SemanticModel` per C# project. Projects and documents are deduplicated by normalized project path. Evaluated `ProjectReference` entries produce `ReferencesProject` relations.

Semantic entities use canonical Roslyn documentation identities scoped by deterministic project ID. This distinguishes overloads, generic arity, containing types, and projects. Partial declarations sharing one symbol become one entity with a deterministic primary location and additional source locations.

Every entity and relation records a categorical `ResolutionLevel`:

- `Exact`: evaluated project or solution structure;
- `Semantic`: resolved through Roslyn symbols;
- `Syntactic`: derived directly from syntax without semantic proof;
- `Unresolved`: reserved for explicitly unresolved information and diagnostics, not fabricated relationships.

## Fallback

If project loading throws, produces no compilation, or reports a `WorkspaceDiagnosticKind.Failure` that can be associated uniquely with that project, its compilation is not considered trustworthy. That project alone is analyzed syntactically. Base-type relationships are omitted because their targets cannot be proven.

Roslyn workspace diagnostics do not expose a structured project ID. Engineering Brain associates one only when the diagnostic contains a unique full path, repository-relative path, or unambiguous project filename. Ambiguous failures remain global diagnostics and do not degrade projects by guesswork.

An unattributed workspace failure leaves individual project modes unchanged but prevents the repository from claiming `FullSemantic`; the repository is labeled `PartialSemantic` until the global uncertainty is resolved.

## Consequences

- Healthy projects retain semantic analysis when another project is broken.
- A repository may correctly report `FullSemantic`, `PartialSemantic`, or `SyntaxFallback`.
- Cross-project inheritance and implementation can be proven through compilation and assembly identities.
- Analysis does not restore packages or download dependencies. Missing local prerequisites trigger fallback.
- Multi-target projects currently use the project configuration selected by `MSBuildWorkspace`; explicit per-target expansion remains future work.
