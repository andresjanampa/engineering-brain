# ADR 0010: Reviewed concept lifecycle

- Status: Accepted
- Date: 2026-09-12

## Context

Reviewed concept reranking stores human-reviewed semantic decisions outside Git and isolates them by repository and branch. After a feature branch is integrated, preserving those decisions on another branch previously required a manual operational migration. Blindly copying the artifact is unsafe because branch identity, source references, and component fingerprints are target-dependent.

## Decision

Add `brain concepts status`, `brain concepts validate`, and `brain concepts promote`. Status and validation obtain fresh repository evidence through a transient snapshot store and build the deterministic Project Memory projection in memory. They do not persist a snapshot, synchronize Project Memory, or mutate the reviewed catalog.

The runtime `LocalReviewedConceptStore` remains read-only. `LocalReviewedConceptWriter` is the sole lifecycle mutation boundary. It writes only the branch-scoped `semantic/reviewed-concepts.json` artifact and exposes no general edit or delete API. Project Memory services remain unaware of reviewed concept lifecycle.

A source catalog is trusted as a human-reviewed decision artifact only after structural, declaration-fingerprint, and canonical-content integrity validation. Promotion does not claim to reconstruct or revalidate historical source code. The source branch need not be checked out or still exist in Git when its local branch-scoped artifact exists.

The target must be the current checked-out branch of the repository passed to the command, with a stable HEAD and a clean working tree. Another existing worktree can be selected with `--repo`. The command never checks out, switches, creates, or fetches a branch.

Promotion rebuilds the target envelope from fresh target evidence. Every EntityId must exist, every source reference is rebound, and every source, declaration, and catalog fingerprint is recomputed. The complete candidate is validated and resolved in memory before writing. One missing or stale assignment blocks the operation; no partial promotion occurs. An invalid existing target blocks replacement, and V1 has no force mode.

The writer holds an exclusive sibling lock while it reads the target precondition, compares the expected fingerprint, writes and flushes a unique sibling temporary file, verifies final Git state, rechecks the target precondition, and atomically replaces the target. The lock serializes cooperating lifecycle writers, and the final fingerprint check detects non-cooperating changes during Git validation. It does not claim portable compare-and-swap protection against an arbitrary external write after the final read. Conflicts, cancellation, validation failures, or changed Git state leave the previous target untouched. Identical content returns `Unchanged` without a rewrite or timestamp change.

## Consequences

Lifecycle output records source and target identities, before/after fingerprints, concept and assignment counts, active profiles, recomputed evidence, and bounded diagnostics. The catalog remains local, repository-aware, branch-aware, auditable, and independent from generated Project Memory.

V1 deliberately adds no receipt store, editor, UI, import/export, automatic concept generation, checkout/fetch automation, retrieval changes, schema changes, embeddings, or provider calls. A later milestone may add these only for a demonstrated use case.
