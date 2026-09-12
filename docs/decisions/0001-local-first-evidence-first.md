# ADR 0001: Local-first, evidence-first analysis

- Status: Accepted
- Date: 2026-09-10

## Context

Engineering Brain will analyze repositories that may contain proprietary code and sensitive configuration. Reliable implementation advice also requires more than prose generated from broad repository dumps: conclusions need traceable facts about files, symbols, dependencies, and Git state.

## Decision

Begin with deterministic static analysis and persist only derived metadata in local JSON snapshots. Source code remains in the analyzed repository. Snapshots are stored under the user's profile rather than modifying the target repository.

Roslyn provides syntax and semantic evidence for the first C# analyzer. Relationships are emitted only when they can be resolved correctly. The domain depends on an analyzer contract rather than Roslyn, keeping other languages and future reasoning providers independent.

The structured code graph and the future project-memory wiki are separate. The graph represents extracted facts; the wiki represents derived context, decisions, and patterns and must cite evidence.

## Consequences

- The foundation needs no API key, external service, database, or network access.
- Sensitive source is not copied or sent to an LLM.
- Future LLM context can be narrowly retrieved from evidence instead of containing a full repository.
- Initial analysis is intentionally incomplete for relationships requiring full project-system or cross-project resolution.
- Incremental analysis can build on stable entity IDs, file hashes, branch, and commit metadata without changing the core boundary.
