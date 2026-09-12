# ADR 0009: Reviewed concept reranking

- Status: Accepted
- Date: 2026-09-12

## Context

The Code Graph records objective extracted structure, while lexical retrieval cannot always connect functional initiative language to implementation responsibilities. Generated Project Memory remains a deterministic projection of graph evidence and must not become the owner of reviewed semantic judgments.

## Decision

Store reviewed concepts in a separate, read-only, repository- and branch-scoped artifact at `knowledge/branches/<branch-key>/semantic/reviewed-concepts.json`. CLI orchestration loads, validates, and resolves it once against immutable snapshot and component fingerprints, then passes one `ReviewedConceptResolutionResult` to preview, analysis, offline evaluation, and live evaluation. Project Memory sync does not create, mutate, or delete the artifact.

Catalog-envelope errors disable the catalog. An invalid declaration excludes only that declaration; an invalid, missing, or stale assignment excludes only that assignment. Remaining profiles continue with status `ValidWithDiagnostics`. Missing or unusable catalogs fall back to lexical retrieval with bounded safe diagnostics.

Retrieval version `lexical-graph-concept-v1` retains lexical candidate generation and the frozen P1 phrase coverage. For clear A1 declarations, C1 additionally requires a complete `AnchorTokens` group and one `QualificationSupportTokens` match. Only after qualification may matched concept tokens, including `ContextSupportTokens`, contribute S2 at two points per token with a six-point aggregate component cap. E2 preserves lexical ordering when concept evidence would reverse stronger exact-identity evidence.

Concepts rerank only positive lexical component candidates before the direct Top-K cutoff. They cannot create candidates, add project score, score graph-expanded candidates, alter graph traversal, or change lexical weights. Resolved profiles contain identity and qualification metadata only; definitions, provenance prose, and reviewer metadata do not enter generated Markdown or CALL #2 context.

## Consequences

The Code Graph remains objective and Project Memory remains generated deterministic knowledge. Reviewed declarations are independently auditable by source hashes, declaration fingerprints, reviewer metadata, branch identity, and current component fingerprints. A stale assignment degrades locally instead of suppressing useful reviewed knowledge.

With no catalog, scores, ordering, selected entities, reasons, project ranking, and graph expansion remain equivalent to `lexical-graph-v2`. Offline baseline acceptance remains explicit: the 16-case suite must preserve Recall@5 and Recall@10 at 1.000 with no regressions before this reranker is considered valid.

This decision adds no embeddings, aliases, stemming, network behavior, provider calls, authoring CLI, UI, policy changes, or new source-content exposure.
