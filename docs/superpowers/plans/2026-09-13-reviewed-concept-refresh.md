# Reviewed Concept Remap And Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add auditable explicit EntityId migration and deterministic current-branch evidence refresh so stale reviewed concept catalogs can return to `Valid` without manual JSON edits.

**Architecture:** Schema 2 adds a canonical identity-migration ledger while schema 1 remains byte-compatible and readable. `ReviewedConceptLifecycleService` owns remap/refresh orchestration, obtains only explicit mappings and fresh immutable evidence, and delegates every mutation to the existing atomic `LocalReviewedConceptWriter`.

**Tech Stack:** .NET 10, C#, System.Text.Json, xUnit, existing repository analysis and reviewed concept lifecycle primitives.

**Spec:** `docs/superpowers/specs/2026-09-13-reviewed-concept-refresh-design.md`

## Global Constraints

- Do not modify retrieval, ranking, graph expansion, Project Memory ownership, policies, providers, or LLM behavior.
- Do not change `ReviewedConceptResolver` runtime behavior.
- `LocalReviewedConceptStore` remains read-only and `LocalReviewedConceptWriter` remains the sole mutation authority.
- No similarity-based remapping, candidate suggestions, silent assignment loss, partial writes, checkout, fetch, switch, network, or provider calls.
- RC400 always requires an explicit human mapping. Refresh repairs only evidence for exact existing EntityIds.
- Preserve all reviewed declaration semantics and original declaration review/provenance metadata.
- Use RED -> minimal implementation -> GREEN -> related suite -> focused local commit for each task.

## File Map

- Modify `src/EngineeringBrain.Core/ReviewedConceptModels.cs`: schema 2 migration ledger model.
- Modify `src/EngineeringBrain.Core/ReviewedConceptLifecycleModels.cs`: mapping, remap, and refresh contracts.
- Modify `src/EngineeringBrain.Infrastructure/ReviewedConceptSerializer.cs`: version-aware schema 1/2 canonical serialization and migration fingerprints.
- Modify `src/EngineeringBrain.Infrastructure/ReviewedConceptValidator.cs`: schema compatibility and migration-ledger validation.
- Modify `src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs`: shared deterministic reconstruction, remap, refresh, and safety gates.
- Modify `src/EngineeringBrain.Cli/Program.cs`: command parsing, transient evidence orchestration, bounded output, and exit codes.
- Modify focused reviewed concept test files only.
- Modify `README.md`, `docs/decisions/0010-reviewed-concept-lifecycle.md`, and architecture documentation only where command discovery requires it.

---

### Task 1: Versioned Auditable Migration Ledger

**Files:**
- Modify: `src/EngineeringBrain.Core/ReviewedConceptModels.cs`
- Modify: `src/EngineeringBrain.Infrastructure/ReviewedConceptSerializer.cs`
- Test: `tests/EngineeringBrain.Core.Tests/ReviewedConceptModelTests.cs`
- Test: `tests/EngineeringBrain.Core.Tests/ReviewedConceptSerializerTests.cs`

**Interfaces:**
- Produces: `ReviewedConceptIdentityMigration`, schema 1/2 serialization, and `CreateIdentityMigrationFingerprint`.
- Preserves: byte-equivalent canonical schema 1 serialization and existing declaration fingerprints.

- [ ] **Step 1: Add failing schema compatibility and audit tests**

Add tests proving:

```csharp
[Fact] public void Deserialize_SchemaOneDefaultsToEmptyIdentityMigrationLedger();
[Fact] public void Serialize_SchemaOneRemainsByteCompatibleWithExistingFixture();
[Fact] public void SchemaTwo_RoundTripsCanonicalIdentityMigrationLedger();
[Fact] public void IdentityMigrationFingerprint_IsStableAcrossAffectedConceptOrder();
[Fact] public void IdentityMigrationFingerprint_ChangesForOldNewReviewerOrEvidenceChange();
```

Use the checked-in schema 1 fixture as the byte-compatibility input. Build a
schema 2 catalog containing one record with repository/branch identity, old/new
IDs, two affected ConceptIds in reverse order, previous catalog fingerprint,
destination evidence, `ReviewedConceptReview`, and fingerprint.

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptModelTests|FullyQualifiedName~ReviewedConceptSerializerTests"
```

Expected: FAIL because schema 2 ledger models and serialization do not exist.

- [ ] **Step 3: Implement the minimal version-aware model and serializer**

Add the migration record from the spec and an empty-default `IdentityMigrations`
property to `ReviewedConceptCatalog`. Set latest schema to 2 and supported schema
range to 1..2. Serialize schema 1 through a projection that omits
`identityMigrations`; serialize schema 2 with the ledger. Canonicalize migration
records by UTC timestamp, old ID, new ID, and fingerprint; canonicalize affected
ConceptIds ordinally. Compute each migration fingerprint from canonical JSON that
excludes its own fingerprint.

- [ ] **Step 4: Run GREEN and related tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptModelTests|FullyQualifiedName~ReviewedConceptSerializerTests|FullyQualifiedName~LocalReviewedConceptStoreTests"
```

Expected: PASS, including existing schema 1 fingerprint tests.

- [ ] **Step 5: Commit**

```powershell
git add src/EngineeringBrain.Core/ReviewedConceptModels.cs src/EngineeringBrain.Infrastructure/ReviewedConceptSerializer.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptModelTests.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptSerializerTests.cs
git commit -m "feat: record reviewed concept identity migrations"
```

### Task 2: Validate Schema 2 Migration History

**Files:**
- Modify: `src/EngineeringBrain.Infrastructure/ReviewedConceptValidator.cs`
- Test: `tests/EngineeringBrain.Core.Tests/ReviewedConceptValidatorTests.cs`

**Interfaces:**
- Consumes: schema 1/2 catalogs and `CreateIdentityMigrationFingerprint`.
- Produces: deterministic catalog-scoped diagnostics for malformed migration history.

- [ ] **Step 1: Add failing validator tests**

Add tests proving schema 1 and schema 2 are accepted and that schema 2 rejects:

```csharp
[Theory]
[InlineData("missing-old")]
[InlineData("missing-new")]
[InlineData("same-identity")]
[InlineData("missing-reviewer")]
[InlineData("missing-previous-fingerprint")]
[InlineData("absolute-destination-reference")]
[InlineData("bad-fingerprint")]
public void ValidateIntegrity_InvalidIdentityMigrationIsCatalogInvalid(string failure);

[Fact] public void ValidateIntegrity_DuplicateMigrationFingerprintIsInvalid();
[Fact] public void ValidateIntegrity_UnsortedInputCanonicalizesWithoutChangingMeaning();
```

Assert bounded deterministic diagnostics and no raw source data.

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptValidatorTests"
```

Expected: FAIL because schema 2 and ledger validation are unsupported.

- [ ] **Step 3: Implement minimal validation**

Accept schema 1 and 2. For schema 2 require a non-null ledger and complete
repository/branch identity, distinct old/new IDs, non-empty unique affected
ConceptIds, non-empty previous/destination fingerprints, normalized relative
destination reference, valid `ReviewedConceptReview`, and exact migration
fingerprint. Reject duplicate migration fingerprints. Do not add any resolver
behavior or infer current identity continuity from history.

- [ ] **Step 4: Run GREEN and related suites**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptValidatorTests|FullyQualifiedName~ReviewedConceptSerializerTests|FullyQualifiedName~ReviewedConceptResolverTests"
```

Expected: PASS; resolver results remain unchanged for equivalent declarations.

- [ ] **Step 5: Commit**

```powershell
git add src/EngineeringBrain.Infrastructure/ReviewedConceptValidator.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptValidatorTests.cs
git commit -m "feat: validate reviewed identity migration history"
```

### Task 3: Define Remap And Refresh Lifecycle Contracts

**Files:**
- Modify: `src/EngineeringBrain.Core/ReviewedConceptLifecycleModels.cs`
- Test: `tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleModelTests.cs`

**Interfaces:**
- Produces: `ReviewedConceptIdentityMapping`, remap/refresh outcomes and results.

- [ ] **Step 1: Add failing model tests**

Add tests proving the result contracts expose previous/new fingerprints,
declaration/assignment/profile counts, mapping/rebind counts, and diagnostics.
Add exit-code tests proving Remapped/Refreshed/Unchanged map to 0 and Blocked to 3.

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleModelTests"
```

Expected: FAIL because the contracts are absent.

- [ ] **Step 3: Implement the contracts**

Use these exact outcome names:

```csharp
public enum ReviewedConceptRemapOutcome { Remapped, Blocked }
public enum ReviewedConceptRefreshOutcome { Refreshed, Unchanged, Blocked }
public sealed record ReviewedConceptIdentityMapping(string OldEntityId, string NewEntityId);
```

Keep operation results free of source bodies and raw artifacts. Add dedicated
exit-code helpers rather than changing validation exit semantics.

- [ ] **Step 4: Run GREEN**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleModelTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/EngineeringBrain.Core/ReviewedConceptLifecycleModels.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleModelTests.cs
git commit -m "feat: define concept remap and refresh results"
```

### Task 4: Deterministic Current-Branch Refresh

**Files:**
- Modify: `src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs`
- Test: `tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleServiceTests.cs`

**Interfaces:**
- Produces: `RefreshAsync(...)` from the spec.
- Consumes: fresh `ReviewedConceptEvidenceContext`, current Git information, and the existing writer.

- [ ] **Step 1: Add failing refresh tests**

Add focused tests:

```csharp
[Fact] public async Task RefreshAsync_RebindsRc401ReferenceAndRc402Fingerprint();
[Fact] public async Task RefreshAsync_Rc400BlocksAndPreservesCatalogBytes();
[Fact] public async Task RefreshAsync_PreservesDeclarationsAssignmentsReviewsAndMigrationHistory();
[Fact] public async Task RefreshAsync_RequiresExactTransformedAssignmentSetAndValidResolution();
[Fact] public async Task RefreshAsync_RepeatedCurrentCatalogReturnsUnchangedWithoutTimestampChurn();
[Fact] public async Task RefreshAsync_DirtyDetachedOrChangedGitStateBlocksMutation();
[Fact] public async Task RefreshAsync_WriterConflictPreservesConcurrentWinner();
```

Include a schema 1 refresh case and a schema 2 case with migration history.

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleServiceTests&Name~RefreshAsync"
```

Expected: FAIL because `RefreshAsync` is absent.

- [ ] **Step 3: Implement minimal refresh orchestration**

Extract a private pure reconstruction helper from the existing promotion loop.
For refresh, require canonical integrity and current catalog identity, look up
every assignment by exact EntityId, rebuild source reference/fingerprint from
fresh evidence, preserve schema and migration history, update target evidence
envelope fields, and recompute declaration/catalog fingerprints. Compare the
expected transformed `(ConceptId, EntityId)` set with the reconstructed set and
require final resolution `Valid` before calling the writer.

Reuse the existing initial/final Git gates, expected content hash, lock, and
atomic writer. RC400 and incomplete evidence block before write.

- [ ] **Step 4: Run GREEN and lifecycle suite**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleServiceTests&Name~RefreshAsync"
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleServiceTests|FullyQualifiedName~LocalReviewedConceptWriterTests"
```

Expected: PASS; existing promotion behavior remains green.

- [ ] **Step 5: Commit**

```powershell
git add src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleServiceTests.cs
git commit -m "feat: refresh reviewed concept evidence"
```

### Task 5: Explicit Audited EntityId Remap

**Files:**
- Modify: `src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs`
- Test: `tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleServiceTests.cs`

**Interfaces:**
- Produces: `RemapAsync(...)` accepting reviewer plus all explicit mappings.
- Consumes: schema 2 migration fingerprinting and the shared reconstruction/safety path.

- [ ] **Step 1: Add failing remap tests**

Add focused tests proving:

```csharp
[Fact] public async Task RemapAsync_ReplacesAllUsesAndRecordsDurableHumanReview();
[Fact] public async Task RemapAsync_UpgradesSchemaOneToSchemaTwoAtomically();
[Fact] public async Task RemapAsync_ExpectedTransformedIdentitySetEqualsReconstructedSet();
[Fact] public async Task RemapAsync_UncoveredRc400BlocksWithoutWrite();
[Fact] public async Task RemapAsync_MissingOrIncompleteDestinationBlocksWithoutWrite();
[Fact] public async Task RemapAsync_DuplicateConflictChainCycleOrUnusedMappingBlocks();
[Fact] public async Task RemapAsync_AllowsOnlyRemainingRc401Rc402Diagnostics();
[Fact] public async Task RemapAsync_PreservesReviewedSemanticsAndOriginalDeclarationReview();
[Fact] public async Task RemapAsync_GitOrCatalogRacePreservesPreviousOrConcurrentCatalog();
```

Inject `TimeProvider` and assert the migration record contains the current
repository/branch identity, exact old/new IDs, sorted affected ConceptIds,
previous raw catalog fingerprint, destination evidence, supplied reviewer,
fixed UTC time, and valid fingerprint.

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleServiceTests&Name~RemapAsync"
```

Expected: FAIL because `RemapAsync` is absent.

- [ ] **Step 3: Implement minimal remap orchestration**

Require non-empty reviewer and mappings. Load and integrity-check the current
catalog. Determine the complete distinct RC400 identity set from exact missing
component lookups, require exact coverage by mapping keys, and validate every
destination against fresh evidence. Apply mappings globally, reject conflicts,
construct the expected transformed per-declaration identity set, and compare it
with reconstructed assignments. Rebind mapped assignments to destination
evidence, append one canonical audit record per mapping, recompute affected
declaration fingerprints, and emit schema 2.

Permit final `ValidWithDiagnostics` only when every remaining diagnostic is
assignment-scoped RC401 or RC402. Write once through the existing atomic writer.

- [ ] **Step 4: Run GREEN and related suites**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleServiceTests&Name~RemapAsync"
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleServiceTests|FullyQualifiedName~ReviewedConceptValidatorTests|FullyQualifiedName~LocalReviewedConceptWriterTests"
```

Expected: PASS with byte-for-byte target preservation in every failure test.

- [ ] **Step 5: Commit**

```powershell
git add src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleServiceTests.cs
git commit -m "feat: remap reviewed concept identities"
```

### Task 6: Preserve Migration History During Promotion

**Files:**
- Modify: `src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs`
- Test: `tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleServiceTests.cs`

**Interfaces:**
- Preserves: schema version and migration ledger during existing cross-branch promotion.

- [ ] **Step 1: Add failing promotion compatibility tests**

```csharp
[Fact] public async Task PromoteAsync_SchemaOneRemainsSchemaOneWithoutHistory();
[Fact] public async Task PromoteAsync_SchemaTwoPreservesMigrationHistoryExactly();
[Fact] public async Task PromoteAsync_DoesNotRewriteHistoricalMigrationBranchIdentity();
```

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleServiceTests&Name~PromoteAsync_Schema"
```

Expected: at least the schema 2 history test FAILS because promotion currently
constructs a catalog from `CurrentSchemaVersion` without history.

- [ ] **Step 3: Preserve source schema and ledger**

Build the target envelope with `sourceLoad.Catalog.SchemaVersion` and preserve
canonical historical migration records verbatim. Continue recomputing only
target-dependent assignment evidence and declaration/catalog fingerprints.

- [ ] **Step 4: Run GREEN and full lifecycle tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleServiceTests"
```

Expected: PASS with existing 25/43/29 promotion assertions unchanged.

- [ ] **Step 5: Commit**

```powershell
git add src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleServiceTests.cs
git commit -m "fix: preserve concept migration history during promotion"
```

### Task 7: CLI Remap And Refresh Commands

**Files:**
- Modify: `src/EngineeringBrain.Cli/Program.cs`
- Test: `tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleModelTests.cs`
- Test: `tests/EngineeringBrain.Core.Tests/ReviewedConceptDiagnosticFormatterTests.cs`

**Interfaces:**
- Consumes: lifecycle remap/refresh APIs.
- Produces: exact command parsing, transient evidence orchestration, bounded output, and stable exits.

- [ ] **Step 1: Add failing parsing/result tests**

Cover:

```text
brain concepts refresh
brain concepts refresh .
brain concepts remap --reviewer alice --map old new --repo .
brain concepts remap --reviewer alice --map old1 new1 --map old2 new2 --repo .
```

Reject missing reviewer, missing map values, duplicate old IDs, unknown options,
duplicate `--repo`, positional ambiguity, and paths/IDs beginning with options.
Assert output contains counts and fingerprints but no source bodies or absolute
source evidence paths. Catalog path remains permitted operational metadata.

- [ ] **Step 2: Run RED**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleModelTests|FullyQualifiedName~ReviewedConceptDiagnosticFormatterTests"
dotnet run --project src/EngineeringBrain.Cli --no-build -- concepts refresh --unknown
```

Expected: tests or smoke command FAIL because commands are absent.

- [ ] **Step 3: Implement CLI orchestration**

Extend `RunConceptsAsync` with `refresh` and `remap`. Reuse
`RepositoryRootLocator`, `TransientRepositorySnapshotStore`, repository scan,
`ProjectMemoryBuilder.Build`, `ReviewedConceptEvidenceContext.FromSnapshot`, and
production branch-key location derivation. Do not call Project Memory sync.

Return 0 for Remapped/Refreshed/Unchanged, 3 for Blocked, 2 for usage, 1 for
operational failure, and 130 for cancellation. Render all diagnostics through
`ReviewedConceptDiagnosticFormatter`.

- [ ] **Step 4: Run GREEN, CLI smoke tests, and related suite**

```powershell
dotnet build EngineeringBrain.sln --no-restore
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycle"
dotnet run --project src/EngineeringBrain.Cli --no-build -- concepts status .
dotnet run --project src/EngineeringBrain.Cli --no-build -- concepts refresh --unknown
```

Expected: build/tests PASS; status is read-only; invalid refresh usage exits 2.
Do not execute a real remap until the operator supplies reviewed identities.

- [ ] **Step 5: Commit**

```powershell
git add src/EngineeringBrain.Cli/Program.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleModelTests.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptDiagnosticFormatterTests.cs
git commit -m "feat: expose concept remap and refresh commands"
```

### Task 8: Documentation, Regression Verification, And Main Repair Gate

**Files:**
- Modify: `README.md`
- Modify: `docs/decisions/0010-reviewed-concept-lifecycle.md`
- Modify: `docs/architecture/README.md`

**Interfaces:**
- Documents: command authority, schema compatibility, RC400 human gate, RC401/RC402 refresh, and atomicity.

- [ ] **Step 1: Update concise lifecycle documentation**

Document exact syntax, exits, schema 1/2 compatibility, migration ledger fields,
the transformed-identity-set invariant, and the recovery sequence. State that
`remap` never suggests identities and `refresh` never changes identities.

- [ ] **Step 2: Run documentation and protected-boundary checks**

```powershell
rg -n "concepts remap|concepts refresh|RC400|identity migration" README.md docs/decisions/0010-reviewed-concept-lifecycle.md docs/architecture/README.md
git diff 1046edb -- src/EngineeringBrain.Core/ProjectMemoryModels.cs src/EngineeringBrain.Core/ProjectMemoryService.cs src/EngineeringBrain.Infrastructure/LocalProjectMemoryStore.cs src/EngineeringBrain.Core/ConceptCandidateReranker.cs src/EngineeringBrain.Core/InitiativeCandidateRetriever.cs src/EngineeringBrain.Infrastructure/ReviewedConceptResolver.cs
```

Expected: documentation contains the lifecycle rules; protected-file diff is empty.

- [ ] **Step 3: Run final implementation verification**

```powershell
dotnet build EngineeringBrain.sln --no-restore
dotnet test EngineeringBrain.sln --no-build --no-restore
dotnet run --project src/EngineeringBrain.Cli --no-build -- eval .
git diff --check
git status --short
```

Expected before operational repair: build and tests pass with zero skipped;
offline evaluation remains 16/16 with Recall@5/10 1.000 and no ranking changes
caused by lifecycle code. The current external main catalog remains untouched.

- [ ] **Step 4: Commit documentation**

```powershell
git add README.md docs/decisions/0010-reviewed-concept-lifecycle.md docs/architecture/README.md
git commit -m "docs: document reviewed concept repair lifecycle"
```

- [ ] **Step 5: Stop for the required human identity decision**

Run `brain concepts status .`, report every RC400 ConceptId/old EntityId, and
stop. Do not infer replacements. The operator must explicitly provide every
old/new mapping and reviewer value before the external catalog may be changed.

- [ ] **Step 6: After explicit authorization, repair external main atomically**

Execute one `brain concepts remap` command containing every authorized mapping,
then run `brain concepts refresh .` and `brain concepts validate .`. Do not edit
JSON directly. Require final `Valid`, 25 declarations, 43 assignments, zero
stale/invalid assignments, and the expected resolved profile count. Run the
offline evaluation again and confirm the repository working tree remains clean.

## Final Self-Review Checklist

- Schema 1 canonical bytes and existing declaration fingerprints remain stable.
- Schema 2 contains an exact durable old/new identity decision with reviewer,
  time, branch context, affected concepts, previous catalog hash, destination
  evidence, and deterministic record fingerprint.
- Expected transformed assignment identities exactly equal reconstructed identities.
- Every RC400 is explicitly mapped or the complete operation blocks.
- RC401/RC402 refresh never changes EntityId.
- No failure writes a reduced or partial catalog.
- Existing writer lock and final Git/catalog rechecks cover every mutation.
- Runtime resolver, retrieval, Project Memory, graph, ranking, policies, and
  providers remain unchanged.
- Main repair occurs only after separate explicit mapping authorization.
