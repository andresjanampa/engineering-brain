# Reviewed Concept Refresh Design

## Problem

Reviewed concept assignments bind a reviewed semantic decision to an `EntityId`,
a repository-relative source reference, and a component fingerprint. Normal code
changes can make the source reference or fingerprint stale (`RC401`/`RC402`). A
renamed or otherwise identity-changing component can make the reviewed
`EntityId` disappear (`RC400`).

The existing lifecycle can promote reviewed decisions across distinct branches,
but it cannot repair the current branch: same-branch promotion is rejected and a
`ValidWithDiagnostics` target cannot be replaced. Manual JSON edits are not an
acceptable lifecycle mechanism.

## Decision

Add two current-branch lifecycle commands with separate authority:

```text
brain concepts remap --reviewer <reviewer> --map <old-entity-id> <new-entity-id> [--map ...] [--repo <path>]
brain concepts refresh [path]
```

`remap` records explicit human-reviewed identity continuity. It never discovers
or suggests a destination. `refresh` performs only deterministic evidence
maintenance for assignments whose exact `EntityId` still exists.

Do not relax `concepts promote`. Cross-branch promotion, identity migration, and
evidence refresh remain distinct operations.

## Durable Remap Audit

The existing declaration-level `ReviewedConceptReview` says who reviewed a
declaration and when. `ReviewedConceptProvenance` identifies the source of that
declaration. Mutating either field to encode `OldEntityId -> NewEntityId` would
erase or misrepresent its existing meaning. Existing metadata therefore cannot
record an exact remap safely by itself.

Reviewed concept catalog schema 2 adds a catalog-level migration ledger:

```csharp
public sealed record ReviewedConceptIdentityMigration(
    string RepositoryId,
    string Branch,
    string BranchKey,
    string OldEntityId,
    string NewEntityId,
    IReadOnlyList<string> AffectedConceptIds,
    string PreviousCatalogFingerprint,
    string DestinationSourceReference,
    string DestinationSourceFingerprint,
    ReviewedConceptReview Review,
    string Fingerprint);
```

`ReviewedConceptCatalog` gains `IdentityMigrations`. A migration record is
self-contained so later cross-branch promotion does not change where the review
occurred. `Review.Reviewer` comes only from the explicit `--reviewer` argument;
the process user is never inferred. `Review.Version` is 1 for a new migration
review. `ReviewedAtUtc` comes from an injected `TimeProvider`.

The migration fingerprint is deterministic canonical JSON over every migration
field except `Fingerprint`. `AffectedConceptIds` are unique and ordinal-sorted,
the timestamp is UTC, and line endings are normalized. The catalog fingerprint
covers the canonical migration ledger as well as the declarations.

Schema 1 remains readable and valid. Version-aware serialization must preserve
schema 1 canonical bytes exactly. `remap` upgrades a schema 1 catalog to schema
2 atomically. `refresh` preserves the input schema version. Promotion preserves
the source schema and migration history. No runtime resolver behavior depends on
the ledger.

## Identity Invariant

The implementation must not compare pre-remap and post-remap EntityId sets for
equality. The correct invariant is:

```text
expected transformed assignment identity set
    == reconstructed assignment identity set
```

For every declaration, the expected set is produced by applying each explicit
mapping to the original `(ConceptId, EntityId)` assignments. The reconstructed
catalog must match that set exactly and retain the original declaration and
assignment counts. This permits the authorized identity change while detecting
drops, additions, duplicate collapse, or application to the wrong concept.

## Remap Rules

The command operates only on the active, clean, non-detached branch and uses a
fresh transient repository analysis plus in-memory `ProjectMemoryBuilder.Build`
evidence.

Before mutation:

- The catalog must exist, deserialize, use canonical bytes, and pass structural,
  identity, declaration, assignment, and fingerprint integrity validation.
- Each old identity must be absent from current component evidence and must be
  responsible for an `RC400` assignment diagnostic.
- Every distinct `RC400` identity must be covered exactly once in one invocation.
- Every destination must exist in fresh evidence and expose a relative source
  path, positive start line, and non-empty source fingerprint.
- Old and new identities must differ. Old keys must be unique. A destination
  cannot also be an old key in the same request, preventing chains and cycles.
- Applying mappings must not duplicate an EntityId within a declaration.
- A mapping that affects no assignment is invalid.

One mapping applies to every declaration assignment containing its old identity.
The declaration's ConceptId, definition, anchors, support tokens, provenance,
and original declaration review remain unchanged. The mapped assignment receives
the exact destination EntityId, current source reference, and current source
fingerprint. A separate migration record captures the remap reviewer.

All mapping records in one command share the pre-write catalog fingerprint and
review timestamp. The operation recomputes affected declaration fingerprints,
validates the complete transformed identity set, appends canonical migration
records, and computes the schema 2 catalog fingerprint.

The post-remap catalog may be `ValidWithDiagnostics` only because unmapped
assignments still have `RC401` or `RC402`. Any remaining `RC400`, catalog error,
declaration error, structural assignment error, or other diagnostic blocks the
entire write.

`RC400` is never automatically rebound. The current artifact contains no
objective continuity proof between a missing EntityId and a replacement. Names,
paths, namespaces, and fingerprints are evidence for human review, not automatic
mapping authority.

## Refresh Rules

`refresh` uses the same clean-current-branch and fresh-evidence gates. Its source
is the current catalog, accepted only when artifact integrity and current branch
identity are valid.

For every assignment:

- Exact EntityId lookup is mandatory.
- Missing EntityId (`RC400`) blocks the complete operation.
- Current evidence replaces SourceReference and SourceFingerprint, repairing
  `RC401` and `RC402` deterministically.
- Incomplete target evidence blocks the complete operation.

Refresh preserves declarations, reviewed metadata, identity migration history,
and catalog schema. It updates current snapshot/analyzer envelope fields,
recomputes declaration/catalog fingerprints, and requires the reconstructed
catalog to resolve as `Valid` before writing.

## Atomicity And Failure Model

Both commands use `LocalReviewedConceptWriter.WriteAsync` with the loaded raw
content hash as `expectedCurrentFingerprint`. The existing exclusive sibling
lock, precondition checks, complete temp-file flush, final fingerprint check,
and atomic replacement remain the sole mutation path.

The final callback rechecks current branch, HEAD, non-detached state, and clean
worktree while the writer lock is held. Cancellation, Git drift, catalog drift,
invalid input, incomplete evidence, or any diagnostic outside the permitted
post-remap `RC401`/`RC402` set leaves the previous catalog byte-for-byte intact.

`refresh` returns `Unchanged` without rewriting when canonical output equals the
current catalog. `remap` is not implicitly idempotent: a repeated mapping whose
old identity is no longer present is blocked rather than guessed to have already
succeeded.

## API And Results

Add:

```csharp
public sealed record ReviewedConceptIdentityMapping(string OldEntityId, string NewEntityId);

public enum ReviewedConceptRemapOutcome { Remapped, Blocked }
public enum ReviewedConceptRefreshOutcome { Refreshed, Unchanged, Blocked }
```

`ReviewedConceptRemapResult` records repository/branch identity, path,
previous/new fingerprints, mapping/declaration/assignment/profile counts,
remaining stale count, and bounded diagnostics. `ReviewedConceptRefreshResult`
records the equivalent refresh counts plus rebound references and recomputed
fingerprints.

CLI exit codes follow existing lifecycle conventions:

- 0: Remapped, Refreshed, or Unchanged.
- 3: lifecycle safety or validation block.
- 2: invalid usage.
- 1: operational failure.
- 130: cancellation.

## Restoring Main

The supported workflow is:

```powershell
brain concepts status .
brain concepts remap --reviewer <reviewer> --map <old-id> <new-id> [--map ...] --repo .
brain concepts refresh .
brain concepts validate .
```

The first command identifies all RC400 old identities. A human must provide each
new identity after reviewing current source/snapshot evidence. Implementation of
the command does not authorize a particular mapping.

Successful completion must produce `Valid`, 25 declarations, 43 assignments,
zero stale/invalid assignments, and the expected resolved profile count. If any
destination cannot be proven, restoration stops without altering the catalog.

## Scope

Expected implementation changes are limited to reviewed concept models,
versioned serialization/validation, lifecycle orchestration, CLI parsing/output,
focused tests, README, and lifecycle ADR documentation.

Do not change `ReviewedConceptResolver` runtime behavior, retrieval, Project
Memory ownership, graph expansion, ranking, policies, provider behavior, or any
LLM integration.
