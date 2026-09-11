# ADR 0007: Deterministic lexical retrieval ranking

## Status

Accepted

## Problem

The first evaluation baseline had perfect entity Recall@10 but weaker prioritization: Recall@5 was 0.778, MRR was 0.514, and non-test initiatives contained a 0.325 ratio of test candidates in the top 10. The system needed better ordering, not a larger candidate set or a new retrieval technology.

## Decision

Improve deterministic lexical scoring before considering embeddings. Exact entity and project identities receive the strongest weight. Component names, related base/interface types, project identity, namespaces, paths, and members retain distinct field-specific contributions. Repeated member terms are deduplicated and their total contribution is bounded. A small corpus-derived rarity signal gives bounded preference to discriminating component-name terms.

Projects identified by a `.Tests` suffix or a `tests/` path receive a moderate penalty for non-test initiatives. Explicit test intent disables the penalty, and test candidates remain searchable. Exact identity still outweighs the penalty. Component score propagation into project ranking is bounded so component-ranking changes do not dominate otherwise stable project relevance.

Scores and match reasons are deterministic. Equal scores are ordered by stable full name and entity identity. Candidate and graph-expansion limits remain unchanged.

## Evaluation

The original ten cases remain the tuning and regression set. Six distinct holdout cases cover analyzer extensibility, graph integrity, incremental persistence, project dependents, Spanish outbound validation, and evaluation export. The harness reports tuning, holdout, and all-case metrics separately. Ordinary evaluation never rewrites the accepted baseline; updates require the explicit CLI option.

## Consequences

The ranking remains explainable, local, bounded, and independent of embeddings or network calls. Corpus rarity is repository-relative rather than a semantic similarity measure. Embeddings should be considered only if future tuning and holdout evidence shows that lexical recall or generalization is insufficient.
