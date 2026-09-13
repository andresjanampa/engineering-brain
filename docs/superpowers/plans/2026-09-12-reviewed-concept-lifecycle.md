# Reviewed Concept Lifecycle V1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add safe, local, branch-aware `brain concepts status`, `validate`, and `promote` commands without changing retrieval behavior or Project Memory ownership.

**Architecture:** CLI orchestration obtains fresh objective evidence through `RepositoryAnalysisEngine` backed by an in-memory snapshot store, then derives component fingerprints with `ProjectMemoryBuilder.Build` without persisting snapshots or memory. `ReviewedConceptLifecycleService` reads and validates catalogs, reconstructs a complete target catalog from target evidence, and delegates the only mutation to a dedicated `LocalReviewedConceptWriter` protected by an exclusive sibling lock and atomic replacement.

**Tech Stack:** .NET 10, C# records, `System.Text.Json`, SHA-256 through `KnowledgeIdentity`, xUnit, Roslyn/MSBuild repository analysis, local filesystem only.

**Spec:** Approved Reviewed Concept Lifecycle design in the milestone prompt, extending `docs/decisions/0009-reviewed-concept-reranking.md` while preserving its ownership and retrieval invariants.

## Global Constraints

- Implement exactly `brain concepts status [path]`, `brain concepts validate [path]`, and `brain concepts promote <source-branch> <target-branch> [--repo <path>]`.
- Do not add UI, editing, concept generation, import/export, embeddings, schema redesign, retrieval redesign, or network/provider calls.
- Do not modify A1/C1/S2/E2, lexical weights, candidate eligibility, direct Top-K, graph scoring, or project scoring.
- Do not modify `ProjectMemoryService.cs`, `ProjectMemoryModels.cs`, `LocalProjectMemoryStore.cs`, `ConceptCandidateReranker.cs`, or `InitiativeCandidateRetriever.cs`. Stop and report an architectural mismatch if a task appears to require one of those files.
- `LocalReviewedConceptStore` remains read-only. Only `LocalReviewedConceptWriter` may mutate reviewed-concept artifacts.
- Status and validation must not mutate Git, repository snapshots, Project Memory, reviewed concepts, or evaluation artifacts.
- Promotion supports only a target branch that is the active, non-detached, clean checkout at `--repo`; it never runs checkout, switch, fetch, branch creation, or worktree creation.
- Source code for the historical source branch is not revalidated. A local branch-scoped source catalog is the authoritative reviewed input only after structural and fingerprint integrity checks.
- Every source assignment must resolve independently to current target evidence. One failure blocks the whole promotion; partial promotion is forbidden.
- Reconstruct every target-dependent envelope and assignment field from target evidence. Never inherit these fields from the source catalog.
- Existing invalid target catalogs block promotion. Existing valid targets may be replaced only after complete validation and a locked expected-fingerprint check.
- Diagnostics use `ReviewedConceptDiagnosticFormatter`; never render source bodies, secrets, raw malformed JSON, or absolute source-code paths.
- Use focused red/green TDD and one commit per implementation task. Do not push.
- Final acceptance: build green; at least 313 tests, zero failed/skipped; offline 16/16; Recall@5/10 1.000; MRR 0.867 with valid concepts; no ranking changes; clean Git state.

## File Map

**New production files**

- `src/EngineeringBrain.Core/ReviewedConceptLifecycleModels.cs`: non-persisted lifecycle status, promotion, and write-result contracts.
- `src/EngineeringBrain.Infrastructure/TransientRepositorySnapshotStore.cs`: in-memory `IRepositorySnapshotStore` used only to obtain fresh evidence without disk writes.
- `src/EngineeringBrain.Infrastructure/LocalReviewedConceptWriter.cs`: locked, canonical, atomic reviewed-catalog writer.
- `src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs`: read/validate/promote orchestration and target rebinding.

**New test files**

- `tests/EngineeringBrain.Core.Tests/TransientRepositorySnapshotStoreTests.cs`
- `tests/EngineeringBrain.Core.Tests/LocalReviewedConceptWriterTests.cs`
- `tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleServiceTests.cs`

**Modified production files**

- `src/EngineeringBrain.Core/ReviewedConceptModels.cs:89-125`: add evidence construction from immutable snapshot + manifest; existing `FromMemory` delegates to it.
- `src/EngineeringBrain.Infrastructure/ReviewedConceptSerializer.cs:13-130`: add one canonical catalog-fingerprint primitive.
- `src/EngineeringBrain.Infrastructure/ReviewedConceptValidator.cs:15-188`: expose integrity validation against an explicit expected catalog identity without resolving historical source code.
- `src/EngineeringBrain.Infrastructure/LocalReviewedConceptStore.cs:23-67`: preserve a readable artifact content hash when deserialization fails; no write API.
- `src/EngineeringBrain.Cli/Program.cs:9-86,164-192,501-509,930-968`: command dispatch, transient evidence composition, bounded output, and exit mapping.
- `README.md`: lifecycle command usage and safety constraints.
- `docs/architecture/README.md`: link ADR 0010 from the architecture decision list.

**New documentation**

- `docs/decisions/0010-reviewed-concept-lifecycle.md`: lifecycle ownership, source trust, current-target rule, atomicity, and deferred behavior.

---

### Task 1: Lifecycle Result Contracts

**Files:**
- Create: `src/EngineeringBrain.Core/ReviewedConceptLifecycleModels.cs`
- Test: `tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleModelTests.cs`

**Interfaces:**
- Consumes: `ReviewedConceptResolutionStatus`, `ReviewedConceptDiagnostic`.
- Produces: `ReviewedConceptCatalogIdentity`, `ReviewedConceptLifecycleStatusResult`, `ReviewedConceptPromotionOutcome`, `ReviewedConceptPromotionResult`, `ReviewedConceptWriteOutcome`, `ReviewedConceptWriteResult`.

- [ ] **Step 1: Write the failing contract tests**

Create tests that construct every result and assert the audit fields without serialization assumptions:

```csharp
[Fact]
public void PromotionResult_CarriesSourceTargetFingerprintsAndCounts()
{
    var result = new ReviewedConceptPromotionResult(
        ReviewedConceptPromotionOutcome.Promoted,
        "repository", "engineering-brain",
        "feature/source", "feature-source--key",
        "main", "main--key",
        "source.json", "target.json",
        "source-fingerprint", "previous-fingerprint", "new-fingerprint",
        25, 43, 29, 43, 25, 7, 0, []);

    Assert.Equal(ReviewedConceptPromotionOutcome.Promoted, result.Outcome);
    Assert.Equal(43, result.RecomputedAssignmentCount);
    Assert.Equal(0, result.RejectedOrStaleAssignmentCount);
    Assert.Equal("new-fingerprint", result.NewTargetCatalogFingerprint);
}

[Theory]
[InlineData(ReviewedConceptWriteOutcome.Created)]
[InlineData(ReviewedConceptWriteOutcome.Updated)]
[InlineData(ReviewedConceptWriteOutcome.Unchanged)]
public void WriteResult_ExposesOnlySuccessfulOutcomes(ReviewedConceptWriteOutcome outcome)
{
    var result = new ReviewedConceptWriteResult(outcome, "reviewed-concepts.json", "fingerprint");
    Assert.Equal(outcome, result.Outcome);
}
```

Also cover nullable counts for unreadable invalid catalogs and zero counts for absent catalogs.

- [ ] **Step 2: Run the focused tests and confirm red**

Run:

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~ReviewedConceptLifecycleModelTests
```

Expected: FAIL at compilation because the lifecycle model types do not exist.

- [ ] **Step 3: Add the minimal contracts**

Use these exact shapes:

```csharp
public sealed record ReviewedConceptCatalogIdentity(
    string RepositoryId,
    string Branch,
    string BranchKey);

public sealed record ReviewedConceptLifecycleStatusResult(
    string RepositoryId,
    string RepositoryName,
    string Branch,
    string BranchKey,
    string CatalogPath,
    ReviewedConceptResolutionStatus Status,
    string? CatalogFingerprint,
    int? DeclarationCount,
    int? AssignmentCount,
    int ResolvedProfileCount,
    int InvalidOrStaleAssignmentCount,
    IReadOnlyList<ReviewedConceptDiagnostic> Diagnostics);

public enum ReviewedConceptPromotionOutcome { Promoted, Unchanged, Blocked }
public enum ReviewedConceptWriteOutcome { Created, Updated, Unchanged }

public sealed record ReviewedConceptWriteResult(
    ReviewedConceptWriteOutcome Outcome,
    string Path,
    string Fingerprint);

public sealed record ReviewedConceptPromotionResult(
    ReviewedConceptPromotionOutcome Outcome,
    string RepositoryId,
    string RepositoryName,
    string SourceBranch,
    string SourceBranchKey,
    string TargetBranch,
    string TargetBranchKey,
    string SourceCatalogPath,
    string TargetCatalogPath,
    string? SourceCatalogFingerprint,
    string? PreviousTargetCatalogFingerprint,
    string? NewTargetCatalogFingerprint,
    int DeclarationCount,
    int AssignmentCount,
    int ActiveProfileCount,
    int RecomputedAssignmentCount,
    int RecomputedDeclarationFingerprintCount,
    int ReboundSourceReferenceCount,
    int RejectedOrStaleAssignmentCount,
    IReadOnlyList<ReviewedConceptDiagnostic> Diagnostics);
```

These are runtime contracts, not JSON schemas. Do not modify snapshot, evaluation, Project Memory, or reviewed-catalog schema versions.

- [ ] **Step 4: Run focused and Core tests**

Run:

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~ReviewedConceptLifecycleModelTests
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore
```

Expected: focused tests PASS; existing Core tests PASS with zero skipped.

- [ ] **Step 5: Commit the contracts**

```powershell
git add src/EngineeringBrain.Core/ReviewedConceptLifecycleModels.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleModelTests.cs
git commit -m "feat: define reviewed concept lifecycle results"
```

---

### Task 2: Fresh Transient Evidence

**Files:**
- Create: `src/EngineeringBrain.Infrastructure/TransientRepositorySnapshotStore.cs`
- Create: `tests/EngineeringBrain.Core.Tests/TransientRepositorySnapshotStoreTests.cs`
- Modify: `src/EngineeringBrain.Core/ReviewedConceptModels.cs:89-125`
- Modify: `tests/EngineeringBrain.Core.Tests/ReviewedConceptModelTests.cs`

**Interfaces:**
- Consumes: `IRepositorySnapshotStore`, `RepositorySnapshot`, `ProjectMemoryBuilder.Build`.
- Produces: `TransientRepositorySnapshotStore`; `ReviewedConceptEvidenceContext.FromSnapshot(RepositorySnapshot, ProjectMemoryManifest)`.

- [ ] **Step 1: Write failing in-memory-store and evidence-equivalence tests**

```csharp
[Fact]
public async Task SaveAndLoadAsync_RemainInMemory()
{
    var store = new TransientRepositorySnapshotStore();
    var snapshot = ProjectMemoryTestFactory.Create();

    var path = await store.SaveAsync(snapshot);
    var loaded = await store.LoadLatestAsync(snapshot.Repository.Id);

    Assert.Equal("(transient)", path);
    Assert.Equal(SnapshotLoadStatus.Loaded, loaded.Status);
    Assert.Same(snapshot, loaded.Snapshot);
}

[Fact]
public void FromSnapshot_MatchesEvidenceFromMemory()
{
    var snapshot = ProjectMemoryTestFactory.Create();
    var build = new ProjectMemoryBuilder().Build(snapshot);

    var direct = ReviewedConceptEvidenceContext.FromSnapshot(snapshot, build.Manifest);
    var fromMemory = ReviewedConceptEvidenceContext.FromMemory(
        ReviewedConceptTestData.Memory(snapshot, build.Manifest));

    Assert.Equal(fromMemory.RepositoryId, direct.RepositoryId);
    Assert.Equal(fromMemory.Branch, direct.Branch);
    Assert.Equal(fromMemory.BranchKey, direct.BranchKey);
    Assert.Equal(fromMemory.SourceSnapshotSchema, direct.SourceSnapshotSchema);
    Assert.Equal(fromMemory.SourceAnalyzerVersion, direct.SourceAnalyzerVersion);
    Assert.Equal(
        fromMemory.Components.OrderBy(item => item.Key).ToArray(),
        direct.Components.OrderBy(item => item.Key).ToArray());
}
```

Add a test using a temporary directory sentinel and assert that transient save/load creates no files or directories there.

- [ ] **Step 2: Run the focused tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~TransientRepositorySnapshotStoreTests|FullyQualifiedName~ReviewedConceptModelTests"
```

Expected: FAIL because the transient store and `FromSnapshot` do not exist.

- [ ] **Step 3: Implement the in-memory store and evidence overload**

Implement a dictionary keyed by repository ID. `LoadLatestAsync` returns `NotFound` before save and `Loaded` afterward; `SaveAsync` stores only in memory and returns `"(transient)"`. Both methods must honor an already-cancelled token.

Refactor the existing factory without changing its output:

```csharp
public static ReviewedConceptEvidenceContext FromMemory(ProjectMemorySyncResult memory)
{
    ArgumentNullException.ThrowIfNull(memory);
    return FromSnapshot(memory.SourceSnapshot, memory.Manifest);
}

public static ReviewedConceptEvidenceContext FromSnapshot(
    RepositorySnapshot snapshot,
    ProjectMemoryManifest manifest)
{
    ArgumentNullException.ThrowIfNull(snapshot);
    ArgumentNullException.ThrowIfNull(manifest);
    var entities = snapshot.Entities.ToDictionary(item => item.Id, StringComparer.Ordinal);
    var components = manifest.Notes
        .Where(note => note.Kind == KnowledgeNoteKind.Component && note.SourceId is not null)
        .Where(note => entities.ContainsKey(note.SourceId!))
        .OrderBy(note => note.SourceId, StringComparer.Ordinal)
        .ToDictionary(
            note => note.SourceId!,
            note => new ComponentFingerprintEvidence(
                note.SourceId!,
                entities[note.SourceId!].RelativeFilePath,
                entities[note.SourceId!].StartLine,
                entities[note.SourceId!].EndLine,
                note.SourceFingerprint),
            StringComparer.Ordinal);
    return new ReviewedConceptEvidenceContext(
        manifest.RepositoryId, manifest.Branch, manifest.BranchKey,
        manifest.SourceSnapshotSchema, manifest.SourceAnalyzerVersion, components);
}
```

The store is a dependency passed to the existing `RepositoryAnalysisEngine`; do not modify that engine or its persistence contract.

- [ ] **Step 4: Run focused and related tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~TransientRepositorySnapshotStoreTests|FullyQualifiedName~ReviewedConceptModelTests|FullyQualifiedName~RepositoryAnalysisEngineTests"
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore
```

Expected: all selected and Core tests PASS; no filesystem artifacts are created by the transient store.

- [ ] **Step 5: Commit transient evidence support**

```powershell
git add src/EngineeringBrain.Infrastructure/TransientRepositorySnapshotStore.cs src/EngineeringBrain.Core/ReviewedConceptModels.cs tests/EngineeringBrain.Core.Tests/TransientRepositorySnapshotStoreTests.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptModelTests.cs
git commit -m "feat: build reviewed concept evidence transiently"
```

---

### Task 3: Source Artifact Integrity and Catalog Fingerprints

**Files:**
- Modify: `src/EngineeringBrain.Infrastructure/ReviewedConceptSerializer.cs:13-130`
- Modify: `src/EngineeringBrain.Infrastructure/ReviewedConceptValidator.cs:15-188`
- Modify: `src/EngineeringBrain.Infrastructure/LocalReviewedConceptStore.cs:23-67`
- Modify: `tests/EngineeringBrain.Core.Tests/ReviewedConceptSerializerTests.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/ReviewedConceptValidatorTests.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/LocalReviewedConceptStoreTests.cs`

**Interfaces:**
- Consumes: `ReviewedConceptCatalogIdentity` from Task 1.
- Produces: `ReviewedConceptSerializer.CreateCatalogFingerprint(ReviewedConceptCatalog)` and `ReviewedConceptValidator.ValidateIntegrity(ReviewedConceptCatalog, ReviewedConceptCatalogIdentity)`.

- [ ] **Step 1: Write failing integrity tests**

Add exact tests:

```csharp
[Fact]
public void CreateCatalogFingerprint_IsCanonicalAndDeterministic()
{
    var catalog = ReviewedConceptTestData.Catalog();
    var expected = KnowledgeIdentity.ContentHash(ReviewedConceptSerializer.Serialize(catalog));
    Assert.Equal(expected, ReviewedConceptSerializer.CreateCatalogFingerprint(catalog));
}

[Fact]
public void ValidateIntegrity_UsesExpectedSourceIdentityWithoutResolvingSourceCode()
{
    var catalog = ReviewedConceptTestData.Catalog();
    var evidence = ReviewedConceptTestData.Evidence();
    var identity = new ReviewedConceptCatalogIdentity(
        evidence.RepositoryId, catalog.Branch, catalog.BranchKey);

    var result = new ReviewedConceptValidator().ValidateIntegrity(catalog, identity);

    Assert.True(result.CatalogIsValid);
    Assert.Empty(result.Diagnostics);
}
```

Also add integrity tests for repository mismatch, branch mismatch, branch-key mismatch, non-positive historical snapshot schema (`RC109`), blank historical analyzer (`RC110`), duplicate ConceptId, malformed direct models, invalid declaration fingerprint, null nested members, and invalid assignment structure. Add separate current-evidence `Validate` tests proving analyzer/schema mismatch still fails. Confirm direct invalid models return deterministic diagnostics and never throw.

Change malformed-store expectations so valid bytes that fail deserialization still return their bounded content fingerprint, while an I/O failure may return null.

- [ ] **Step 2: Run tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptSerializerTests|FullyQualifiedName~ReviewedConceptValidatorTests|FullyQualifiedName~LocalReviewedConceptStoreTests"
```

Expected: FAIL because the new fingerprint and integrity methods do not exist and invalid load currently loses its content hash.

- [ ] **Step 3: Implement one canonical fingerprint primitive and shared validation core**

```csharp
public static string CreateCatalogFingerprint(ReviewedConceptCatalog catalog) =>
    KnowledgeIdentity.ContentHash(Serialize(catalog));

public ReviewedConceptValidationResult Validate(
    ReviewedConceptCatalog catalog,
    ReviewedConceptEvidenceContext evidence)
{
    var integrity = ValidateIntegrity(catalog, new ReviewedConceptCatalogIdentity(
        evidence.RepositoryId, evidence.Branch, evidence.BranchKey));
    if (!integrity.CatalogIsValid)
    {
        return integrity;
    }

    var diagnostics = integrity.Diagnostics.ToList();
    AddCatalogError(catalog.SourceSnapshotSchema != evidence.SourceSnapshotSchema,
        "RC104", "Reviewed concept snapshot schema does not match current evidence.", diagnostics);
    AddCatalogError(!string.Equals(catalog.SourceAnalyzerVersion, evidence.SourceAnalyzerVersion,
            StringComparison.Ordinal),
        "RC105", "Reviewed concept analyzer version does not match current evidence.", diagnostics);
    return diagnostics.Any(item => item.Scope == ReviewedConceptDiagnosticScope.Catalog)
        ? new ReviewedConceptValidationResult(false, [], diagnostics)
        : integrity with { Diagnostics = diagnostics };
}

public ReviewedConceptValidationResult ValidateIntegrity(
    ReviewedConceptCatalog catalog,
    ReviewedConceptCatalogIdentity expected)
{
    ArgumentNullException.ThrowIfNull(catalog);
    ArgumentNullException.ThrowIfNull(expected);
    var diagnostics = ValidateEnvelopeIntegrity(catalog, expected);
    if (diagnostics.Count > 0)
    {
        return new ReviewedConceptValidationResult(false, [], diagnostics);
    }

    return ValidateDeclarationsAndAssignments(catalog, diagnostics);
}
```

Extract the existing lines 28-71 into `ValidateDeclarationsAndAssignments`; its ordering, local exclusion, duplicate checks, and diagnostic codes remain byte-for-byte equivalent. Split current envelope checks so integrity owns RC100-RC103 and RC106-RC108, uses RC109 for a non-positive historical snapshot schema and RC110 for blank historical analyzer metadata, while current-evidence validation adds RC104/RC105.

Do not invoke `ReviewedConceptResolver` for the historical source catalog. Integrity proves artifact structure and hashes, not historical source-code correspondence. A historical source analyzer/snapshot version need not equal the target version; it must only be structurally valid. The normal `Validate(catalog, currentEvidence)` path retains exact RC104/RC105 compatibility checks.

In `LocalReviewedConceptStore.LoadAsync`, compute `ContentHash` immediately after reading text, before deserialization, and return it on parse/structure failure. Keep the store read-only.

- [ ] **Step 4: Run focused, resolver, and model tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptSerializerTests|FullyQualifiedName~ReviewedConceptValidatorTests|FullyQualifiedName~ReviewedConceptResolverTests|FullyQualifiedName~LocalReviewedConceptStoreTests"
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore
```

Expected: PASS; existing validation granularity remains catalog-wide for envelope errors and local for declaration/assignment errors.

- [ ] **Step 5: Commit integrity support**

```powershell
git add src/EngineeringBrain.Infrastructure/ReviewedConceptSerializer.cs src/EngineeringBrain.Infrastructure/ReviewedConceptValidator.cs src/EngineeringBrain.Infrastructure/LocalReviewedConceptStore.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptSerializerTests.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptValidatorTests.cs tests/EngineeringBrain.Core.Tests/LocalReviewedConceptStoreTests.cs
git commit -m "feat: validate reviewed concept artifact integrity"
```

---

### Task 4: Atomic Writer with a Real Single-Writer Lock

**Files:**
- Create: `src/EngineeringBrain.Infrastructure/LocalReviewedConceptWriter.cs`
- Create: `tests/EngineeringBrain.Core.Tests/LocalReviewedConceptWriterTests.cs`

**Interfaces:**
- Consumes: `LocalReviewedConceptStore.GetPath`, `ReviewedConceptSerializer.Serialize/CreateCatalogFingerprint`, Task 1 write-result contracts.
- Produces:

```csharp
public sealed class ReviewedConceptWriteConflictException : IOException
{
    public ReviewedConceptWriteConflictException(string message) : base(message) { }
}

public sealed class LocalReviewedConceptWriter
{
    public LocalReviewedConceptWriter(
        LocalReviewedConceptStore? paths = null,
        TimeSpan? lockTimeout = null,
        TimeSpan? lockRetryDelay = null);

    public Task<ReviewedConceptWriteResult> WriteAsync(
        string branchKnowledgeLocation,
        ReviewedConceptCatalog catalog,
        string? expectedCurrentFingerprint,
        Func<CancellationToken, Task> validateBeforeCommit,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1: Write failing writer tests**

Cover these named cases:

```csharp
[Fact] public async Task WriteAsync_AbsentTargetCreatesCanonicalArtifactAtomically();
[Fact] public async Task WriteAsync_ExistingExpectedTargetUpdatesAtomically();
[Fact] public async Task WriteAsync_IdenticalContentReturnsUnchangedWithoutTimestampChurn();
[Fact] public async Task WriteAsync_UnexpectedExistingTargetThrowsConflictAndPreservesBytes();
[Fact] public async Task WriteAsync_ExpectedFingerprintMismatchThrowsConflictAndPreservesBytes();
[Fact] public async Task WriteAsync_HeldSiblingLockTimesOutAndPreservesTarget();
[Fact] public async Task WriteAsync_ConcurrentCooperatingWritersAllowOneWinnerAndOneConflict();
[Fact] public async Task WriteAsync_ExceptionReleasesLockForNextWriter();
[Fact] public async Task WriteAsync_CancellationCleansOwnTemporaryFileAndPreservesTarget();
[Fact] public async Task WriteAsync_RejectsEscapingBranchLocation();
```

For the lock test, open `<catalog-path>.lock` with `FileShare.None`, use a 100 ms test timeout, and assert a bounded `ReviewedConceptWriteConflictException`. Start concurrent writers with the same expected fingerprint and assert exactly one update succeeds while the delayed writer observes a conflict. For cleanup, assert no `.*.tmp` files remain and a second writer can acquire the lock after the first operation exits. Pass `static _ => Task.CompletedTask` as the guard in ordinary writer tests; add a test proving a throwing guard runs under the lock immediately before move and preserves the old target.

- [ ] **Step 2: Run writer tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~LocalReviewedConceptWriterTests
```

Expected: FAIL because `LocalReviewedConceptWriter` does not exist.

- [ ] **Step 3: Implement lock, precondition, flush, and atomic move**

Use a persistent sibling lock file opened with `FileMode.OpenOrCreate`, `FileAccess.ReadWrite`, and `FileShare.None`. The file may remain empty; correctness is ownership of the exclusive handle, and release means disposing that handle. Retry on sharing `IOException` until the bounded timeout, honoring cancellation between retries.

While holding the lock:

1. Read and hash the current target if present.
2. Require absence when `expectedCurrentFingerprint` is null; otherwise require exact ordinal equality.
3. Serialize canonically and return `Unchanged` before writing when hashes match.
4. Write UTF-8 without BOM to a uniquely named sibling temp file opened with `FileMode.CreateNew` and `FileOptions.WriteThrough`.
5. Flush the stream completely.
6. Await `validateBeforeCommit` while still holding the lock.
7. Recheck the expected target fingerprint under the same lock, then immediately call `File.Move(temp, target, overwrite: true)`.
8. Delete only this invocation's temp file in `finally`.

Do not expose delete, arbitrary path write, or general CRUD methods. The lock protects all cooperating production writers; the final fingerprint check detects a non-cooperating write during validation before replacement. Do not claim portable compare-and-swap protection against an arbitrary external write after that final read.

- [ ] **Step 4: Run writer and store tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LocalReviewedConceptWriterTests|FullyQualifiedName~LocalReviewedConceptStoreTests"
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore
```

Expected: PASS; target bytes survive every conflict/failure test; `Unchanged` preserves timestamp.

- [ ] **Step 5: Commit the writer**

```powershell
git add src/EngineeringBrain.Infrastructure/LocalReviewedConceptWriter.cs tests/EngineeringBrain.Core.Tests/LocalReviewedConceptWriterTests.cs
git commit -m "feat: write reviewed concepts atomically"
```

---

### Task 5: Read-Only Status and Validation Service

**Files:**
- Create: `src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs`
- Create: `tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleServiceTests.cs`

**Interfaces:**
- Consumes: reader, validator, resolver, `ReviewedConceptEvidenceContext`.
- Produces:

```csharp
public sealed class ReviewedConceptLifecycleService
{
    public ReviewedConceptLifecycleService(
        LocalReviewedConceptStore? reader = null,
        LocalReviewedConceptWriter? writer = null,
        ReviewedConceptValidator? validator = null,
        ReviewedConceptResolver? resolver = null,
        IGitInfoProvider? gitInfo = null);

    public Task<ReviewedConceptLifecycleStatusResult> GetStatusAsync(
        string repositoryName,
        string branchKnowledgeLocation,
        ReviewedConceptEvidenceContext evidence,
        CancellationToken cancellationToken = default);

    public Task<ReviewedConceptLifecycleStatusResult> ValidateAsync(
        string repositoryName,
        string branchKnowledgeLocation,
        ReviewedConceptEvidenceContext evidence,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1: Write failing status/validation tests**

Add tests named:

```csharp
[Fact] public async Task GetStatusAsync_AbsentReturnsZeroCountsAndAbsent();
[Fact] public async Task GetStatusAsync_ValidReportsCatalogCountsFingerprintAndProfiles();
[Fact] public async Task GetStatusAsync_MalformedReportsInvalidWithoutThrowing();
[Fact] public async Task GetStatusAsync_StaleAssignmentReportsValidWithDiagnostics();
[Fact] public async Task ValidateAsync_BranchMismatchReportsInvalid();
[Fact] public async Task ValidateAsync_RepositoryMismatchReportsInvalid();
[Fact] public async Task ValidateAsync_DeclarationFingerprintMismatchReportsValidWithDiagnostics();
[Fact] public async Task ValidateAsync_NonCanonicalCatalogReportsValidWithDiagnosticsAndRawFingerprint();
[Fact] public async Task ValidateAsync_DirectStructurallyInvalidModelCannotThrow();
```

Assert `InvalidOrStaleAssignmentCount` counts assignment-scope diagnostics, malformed counts are null, absent counts are zero, and normal IDs remain present in diagnostics.

- [ ] **Step 2: Run lifecycle service tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~ReviewedConceptLifecycleServiceTests
```

Expected: FAIL because the lifecycle service does not exist.

- [ ] **Step 3: Implement one inspection path used by both methods**

`GetStatusAsync` and `ValidateAsync` must call one private `InspectAsync` implementation:

```csharp
private async Task<ReviewedConceptLifecycleStatusResult> InspectAsync(
    string repositoryName,
    string branchKnowledgeLocation,
    ReviewedConceptEvidenceContext evidence,
    CancellationToken cancellationToken)
{
    var load = await _reader.LoadAsync(branchKnowledgeLocation, cancellationToken);
    if (load.Status == ReviewedConceptLoadStatus.Absent)
    {
        return CreateStatus(repositoryName, evidence, load.Path,
            ReviewedConceptResolutionResult.Absent, 0, 0);
    }

    if (load.Status == ReviewedConceptLoadStatus.Invalid)
    {
        var invalid = new ReviewedConceptResolutionResult(
            ReviewedConceptResolutionStatus.Invalid, load.ContentHash, [], load.Diagnostics);
        return CreateStatus(repositoryName, evidence, load.Path, invalid, null, null);
    }

    var validation = _validator.Validate(load.Catalog!, evidence);
    var resolution = _resolver.Resolve(load, validation, evidence);
    var canonicalFingerprint = ReviewedConceptSerializer.CreateCatalogFingerprint(load.Catalog);
    if (!string.Equals(load.ContentHash, canonicalFingerprint, StringComparison.Ordinal))
    {
        var diagnostics = resolution.Diagnostics.Append(new ReviewedConceptDiagnostic(
            "RCL103",
            AnalysisDiagnosticSeverity.Warning,
            ReviewedConceptDiagnosticScope.Catalog,
            "Reviewed concept catalog serialization is not canonical.")).ToArray();
        resolution = resolution with
        {
            Status = resolution.Status == ReviewedConceptResolutionStatus.Valid
                ? ReviewedConceptResolutionStatus.ValidWithDiagnostics
                : resolution.Status,
            Diagnostics = diagnostics
        };
    }
    return CreateStatus(repositoryName, evidence, load.Path, resolution,
        load.Catalog!.Declarations.Count,
        load.Catalog.Declarations.Sum(item => item.Assignments.Count));
}

private static ReviewedConceptLifecycleStatusResult CreateStatus(
    string repositoryName,
    ReviewedConceptEvidenceContext evidence,
    string catalogPath,
    ReviewedConceptResolutionResult resolution,
    int? declarationCount,
    int? assignmentCount) => new(
        evidence.RepositoryId,
        repositoryName,
        evidence.Branch,
        evidence.BranchKey,
        catalogPath,
        resolution.Status,
        resolution.CatalogFingerprint,
        declarationCount,
        assignmentCount,
        resolution.Profiles.Count,
        resolution.Diagnostics.Count(item =>
            item.Scope == ReviewedConceptDiagnosticScope.Assignment),
        resolution.Diagnostics);
```

For a loaded catalog, declaration/assignment counts describe the source artifact, profile count describes active resolution, and diagnostics are ordered by scope, ConceptId, EntityId, and code. Do not write or create directories.

- [ ] **Step 4: Run focused and reviewed-concept suites**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleServiceTests|FullyQualifiedName~ReviewedConceptValidatorTests|FullyQualifiedName~ReviewedConceptResolverTests|FullyQualifiedName~LocalReviewedConceptStoreTests"
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore
```

Expected: PASS; absent inspection leaves its branch root nonexistent.

- [ ] **Step 5: Commit status and validation**

```powershell
git add src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleServiceTests.cs
git commit -m "feat: inspect and validate reviewed concept catalogs"
```

---

### Task 6: All-or-Nothing Promotion and Target Rebinding

**Files:**
- Modify: `src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleServiceTests.cs`

**Interfaces:**
- Consumes: source/target branch roots, target evidence, source integrity validation, atomic writer.
- Produces:

```csharp
public Task<ReviewedConceptPromotionResult> PromoteAsync(
    string repositoryName,
    string repositoryRoot,
    string sourceBranch,
    string targetBranch,
    string sourceBranchKnowledgeLocation,
    string targetBranchKnowledgeLocation,
    ReviewedConceptEvidenceContext targetEvidence,
    GitInfo analyzedGit,
    CancellationToken cancellationToken = default);
```

- [ ] **Step 1: Write failing successful-promotion tests**

Create a deterministic representative catalog with 25 declarations and 43 assignments over 29 target component IDs, then add:

```csharp
[Fact] public async Task PromoteAsync_FeatureToMainPreservesTwentyFiveReviewedDeclarations();
[Fact] public async Task PromoteAsync_ReconstructsEntireEnvelopeFromTargetEvidence();
[Fact] public async Task PromoteAsync_RebindsEverySourceReferenceFromTargetEvidence();
[Fact] public async Task PromoteAsync_RecomputesAllAssignmentAndDeclarationFingerprints();
[Fact] public async Task PromoteAsync_PreservesReviewedSemanticAndAuditMetadata();
[Fact] public async Task PromoteAsync_ResultResolvesValidWithTwentyNineProfiles();
[Fact] public async Task PromoteAsync_RepeatedIdenticalPromotionReturnsUnchangedWithoutChurn();
```

The source fixture must intentionally use a different branch, BranchKey, source fingerprints, source references, historical snapshot schema/analyzer envelope values, and declaration fingerprints. Source integrity accepts structurally valid historical analyzer metadata; assert no target-dependent source value survives except where independently equal by target evidence.

- [ ] **Step 2: Run promotion tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleServiceTests&Name~PromoteAsync"
```

Expected: FAIL because `PromoteAsync` is absent.

- [ ] **Step 3: Implement pure reconstruction before any write**

Follow this exact order inside `PromoteAsync`:

```csharp
var sourceLoad = await _reader.LoadAsync(sourceBranchKnowledgeLocation, cancellationToken);
var previousTarget = await _reader.LoadAsync(targetBranchKnowledgeLocation, cancellationToken);
var sourceIdentity = new ReviewedConceptCatalogIdentity(
    targetEvidence.RepositoryId,
    sourceBranch,
    KnowledgeIdentity.CreateBranchKey(sourceBranch));
var sourceValidation = _validator.ValidateIntegrity(sourceLoad.Catalog!, sourceIdentity);
var canonicalSourceFingerprint = ReviewedConceptSerializer.CreateCatalogFingerprint(sourceLoad.Catalog);
if (!string.Equals(sourceLoad.ContentHash, canonicalSourceFingerprint, StringComparison.Ordinal))
{
    diagnostics.Add(new ReviewedConceptDiagnostic(
        "RCL103", AnalysisDiagnosticSeverity.Error,
        ReviewedConceptDiagnosticScope.Catalog,
        "Source reviewed concept catalog serialization is not canonical."));
}

var assignments = new List<ReviewedConceptAssignment>();
foreach (var sourceAssignment in declaration.Assignments)
{
    if (!targetEvidence.Components.TryGetValue(sourceAssignment.EntityId, out var target))
    {
        diagnostics.Add(new ReviewedConceptDiagnostic(
            "RCL300",
            AnalysisDiagnosticSeverity.Error,
            ReviewedConceptDiagnosticScope.Assignment,
            "Reviewed assignment entity is absent from target evidence.",
            declaration.ConceptId,
            sourceAssignment.EntityId));
        continue;
    }

    assignments.Add(new ReviewedConceptAssignment(
        sourceAssignment.EntityId,
        $"{target.RelativePath}:{target.StartLine}",
        target.SourceFingerprint));
}

if (diagnostics.Count > 0)
{
    return new ReviewedConceptPromotionResult(
        ReviewedConceptPromotionOutcome.Blocked,
        targetEvidence.RepositoryId, repositoryName,
        sourceBranch, sourceIdentity.BranchKey,
        targetBranch, targetEvidence.BranchKey,
        sourceLoad.Path, _reader.GetPath(targetBranchKnowledgeLocation),
        sourceLoad.ContentHash, previousTarget.ContentHash, null,
        sourceLoad.Catalog!.Declarations.Count,
        sourceLoad.Catalog.Declarations.Sum(item => item.Assignments.Count),
        0, 0, 0, 0, diagnostics.Count, diagnostics);
}

var draft = declaration with { Assignments = assignments.ToArray(), Fingerprint = string.Empty };
var rebuilt = draft with
{
    Fingerprint = ReviewedConceptSerializer.CreateDeclarationFingerprint(draft)
};
```

Construct `ReviewedConceptCatalog` envelope values exclusively from `targetEvidence` and `targetBranch`. Serialize, validate, and resolve it in memory. Require source declaration count, source assignment count, candidate validated counts, and resolved assignments to agree. Require zero diagnostics and `ReviewedConceptResolutionStatus.Valid` before invoking the writer.

Count every assignment fingerprint and declaration fingerprint as recomputed. Count `ReboundSourceReferenceCount` only where target reference differs from source. Preserve `Provenance` and `Review` exactly; they describe the reviewed decision and are not target source evidence.

- [ ] **Step 4: Run promotion, serializer, validator, and resolver tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleServiceTests|FullyQualifiedName~ReviewedConceptSerializerTests|FullyQualifiedName~ReviewedConceptValidatorTests|FullyQualifiedName~ReviewedConceptResolverTests"
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore
```

Expected: PASS; successful result is `Promoted` or `Unchanged`, status of the written candidate is `Valid`, and rejected count is zero.

- [ ] **Step 5: Commit target rebinding**

```powershell
git add src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleServiceTests.cs
git commit -m "feat: promote reviewed concepts across branches"
```

---

### Task 7: Promotion Safety Gates and Failure Atomicity

**Files:**
- Modify: `src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleServiceTests.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/LocalReviewedConceptWriterTests.cs`

**Interfaces:**
- Consumes: `IGitInfoProvider.GetInfoAsync`, analyzed `GitInfo`, writer expected-fingerprint precondition.
- Produces: deterministic blocked promotion diagnostics using existing `ReviewedConceptDiagnostic` and formatter.

- [ ] **Step 1: Write every blocking regression before implementation**

Add tests named:

```csharp
[Fact] public async Task PromoteAsync_EmptyBranchBlocksBeforeRead();
[Fact] public async Task PromoteAsync_SameBranchBlocksBeforeRead();
[Fact] public async Task PromoteAsync_DetachedHeadBlocksBeforeRead();
[Fact] public async Task PromoteAsync_NonCurrentTargetBlocksBeforeRead();
[Fact] public async Task PromoteAsync_DirtyTargetBlocksBeforeRead();
[Fact] public async Task PromoteAsync_AbsentSourceBlocksWithoutCreatingTarget();
[Fact] public async Task PromoteAsync_MalformedSourceBlocksWithoutChangingTarget();
[Fact] public async Task PromoteAsync_InvalidExistingTargetBlocksWithoutOverwrite();
[Fact] public async Task PromoteAsync_OneMissingEntityBlocksEntirePromotion();
[Fact] public async Task PromoteAsync_OneUnverifiableAssignmentPreservesExistingTarget();
[Fact] public async Task PromoteAsync_BranchChangesBeforeWriteBlocksMutation();
[Fact] public async Task PromoteAsync_HeadChangesBeforeWriteBlocksMutation();
[Fact] public async Task PromoteAsync_WorktreeBecomesDirtyBeforeWriteBlocksMutation();
[Fact] public async Task PromoteAsync_WriterFingerprintConflictReturnsBlockedAndPreservesWinner();
[Fact] public async Task PromoteAsync_LockTimeoutReturnsBoundedDiagnostic();
```

For every test, seed an existing target byte array and timestamp, invoke the failure, and assert both remain identical. Assert diagnostics identify ConceptId/EntityId for assignment failures but contain no source body or malformed JSON.

- [ ] **Step 2: Run safety tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleServiceTests&Name~Blocks|FullyQualifiedName~ReviewedConceptLifecycleServiceTests&Name~Preserves|FullyQualifiedName~ReviewedConceptLifecycleServiceTests&Name~Conflict"
```

Expected: one or more tests FAIL because all safety gates and the final Git recheck are not yet implemented.

- [ ] **Step 3: Implement ordered gates and the final Git recheck**

Use bounded lifecycle diagnostic codes in a dedicated `RCL` range while retaining existing scopes:

- `RCL100`: invalid branch arguments or same branch.
- `RCL101`: source catalog absent.
- `RCL102`: source catalog invalid or contains diagnostics.
- `RCL103`: catalog bytes are not canonical production serialization.
- `RCL200`: target is detached, non-current, or dirty.
- `RCL201`: branch/HEAD/clean state changed before write.
- `RCL300`: target EntityId is absent from component evidence.
- `RCL301`: target component evidence is incomplete.
- `RCL400`: target catalog is invalid.
- `RCL401`: lock or expected-fingerprint conflict.

Check initial `analyzedGit` before source loading. Pass a `validateBeforeCommit` callback to `_writer.WriteAsync`; the writer invokes it while holding the catalog lock after temp-file flush, then rechecks the expected target fingerprint immediately before atomic move. The callback calls injected `IGitInfoProvider.GetInfoAsync(repositoryRoot)` and requires the same target branch, same HEAD, and `IsWorkingTreeClean == true`. Define an internal `ReviewedConceptTargetChangedException : InvalidOperationException` in `ReviewedConceptLifecycleService.cs`; the callback throws it with a fixed safe message when Git state differs. Catch `ReviewedConceptWriteConflictException` as `RCL401` and `ReviewedConceptTargetChangedException` as `RCL201`; propagate cancellation and unrelated operational I/O failures to the CLI's operational path.

Source `ValidWithDiagnostics` is blocking for promotion even though runtime may use valid siblings. Promotion cannot silently drop reviewed declarations or assignments.

- [ ] **Step 4: Run all lifecycle, writer, and diagnostic tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleServiceTests|FullyQualifiedName~LocalReviewedConceptWriterTests|FullyQualifiedName~ReviewedConceptDiagnosticFormatterTests"
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore
```

Expected: PASS; all blocked outcomes perform zero target mutation and all diagnostics fit `ReviewedConceptDiagnosticFormatter.MaximumRenderedLength`.

- [ ] **Step 5: Commit safety gates**

```powershell
git add src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleServiceTests.cs tests/EngineeringBrain.Core.Tests/LocalReviewedConceptWriterTests.cs
git commit -m "fix: make concept promotion all or nothing"
```

---

### Task 8: CLI Commands, Exit Codes, and Bounded Output

**Files:**
- Modify: `src/EngineeringBrain.Cli/Program.cs:9-86,164-192,501-509,930-968`
- Modify: `src/EngineeringBrain.Core/ReviewedConceptLifecycleModels.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleModelTests.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/ReviewedConceptDiagnosticFormatterTests.cs`

**Interfaces:**
- Consumes: lifecycle service, transient store, `ProjectMemoryBuilder`, `LocalProjectMemoryStore.GetBranchLocation`, `RepositoryRootLocator`, `GitInfoProvider`.
- Produces: exact V1 command syntax and exit-code mapping.

- [ ] **Step 1: Write failing exit/output mapping tests**

Add this small command-policy helper to `ReviewedConceptLifecycleModels.cs` and test it directly:

```csharp
[Theory]
[InlineData(ReviewedConceptResolutionStatus.Valid, 0)]
[InlineData(ReviewedConceptResolutionStatus.ValidWithDiagnostics, 3)]
[InlineData(ReviewedConceptResolutionStatus.Invalid, 4)]
[InlineData(ReviewedConceptResolutionStatus.Absent, 5)]
public void ValidationExitCode_IsStable(ReviewedConceptResolutionStatus status, int expected);
```

The implementation signature is:

```csharp
public static class ReviewedConceptLifecycleExitCode
{
    public static int ForValidation(ReviewedConceptResolutionStatus status) => status switch
    {
        ReviewedConceptResolutionStatus.Valid => 0,
        ReviewedConceptResolutionStatus.ValidWithDiagnostics => 3,
        ReviewedConceptResolutionStatus.Invalid => 4,
        ReviewedConceptResolutionStatus.Absent => 5,
        _ => 4
    };
}
```

Add formatter tests that render status and blocked-promotion diagnostics containing newline, carriage return, tab, very long ConceptId/EntityId, and normal identities. Assert one line and bounded length using the existing diagnostic formatter.

- [ ] **Step 2: Run focused tests and confirm red**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleServiceTests|FullyQualifiedName~ReviewedConceptDiagnosticFormatterTests"
```

Expected: FAIL because lifecycle exit mapping is absent.

- [ ] **Step 3: Implement command dispatch and transient composition**

Dispatch `concepts` before the legacy scan/memory branch:

```csharp
if (args[0].Equals("concepts", StringComparison.OrdinalIgnoreCase))
{
    return await RunConceptsAsync(args);
}
```

Add a repository-analysis overload that preserves existing callers:

```csharp
private static async Task<RepositoryAnalysisResult> AnalyzeRepositoryAsync(
    string path,
    CancellationToken cancellationToken,
    IRepositorySnapshotStore? snapshotStore = null)
{
    IRepositoryScanner scanner = new RepositoryScanner();
    var git = new GitInfoProvider();
    IReadOnlyList<ILanguageAnalyzer> analyzers = [new CSharpAnalyzer()];
    snapshotStore ??= new LocalRepositorySnapshotStore();
    return await new RepositoryAnalysisEngine(scanner, git, analyzers, snapshotStore, git)
        .ScanAsync(path, cancellationToken);
}
```

Lifecycle commands pass a new `TransientRepositorySnapshotStore`, build `ProjectMemoryBuilder.Build(scan.Snapshot)` in memory, then call `ReviewedConceptEvidenceContext.FromSnapshot`. Derive source/target locations with the existing `LocalProjectMemoryStore.GetBranchLocation` and production `KnowledgeIdentity.CreateBranchKey`; do not call `ProjectMemoryService.SyncAsync`.

Parsing rules:

- `status` and `validate`: zero or one positional path; reject flags.
- `promote`: exactly two branch names and optional single `--repo <path>` pair; reject missing values, duplicates, and unknown flags.
- Resolve paths with `RepositoryRootLocator` before analysis.

Output catalog location only as lifecycle operational metadata. Render every diagnostic through `ReviewedConceptDiagnosticFormatter.Format` and never interpolate raw exception data containing artifact contents.

- [ ] **Step 4: Run CLI smoke checks without promotion**

```powershell
dotnet build EngineeringBrain.sln --no-restore
dotnet run --project src/EngineeringBrain.Cli --no-build -- concepts status .
dotnet run --project src/EngineeringBrain.Cli --no-build -- concepts validate .
dotnet run --project src/EngineeringBrain.Cli --no-build -- concepts promote main main --repo .
```

Expected: build PASS; status exits `0`; validate exits according to the current branch artifact; same-branch promote exits `2` without writing. Confirm snapshot and Project Memory timestamps are unchanged across status/validate.

- [ ] **Step 5: Run full tests and commit CLI behavior**

```powershell
dotnet test EngineeringBrain.sln --no-restore
git add src/EngineeringBrain.Cli/Program.cs src/EngineeringBrain.Core/ReviewedConceptLifecycleModels.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleModelTests.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptDiagnosticFormatterTests.cs
git commit -m "feat: add reviewed concept lifecycle commands"
```

Expected: all tests PASS with zero skipped.

---

### Task 9: End-to-End Lifecycle Safety and Ownership Regression

**Files:**
- Modify: `tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleServiceTests.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/ProjectMemoryServiceTests.cs:65-93`
- Modify: `tests/EngineeringBrain.Core.Tests/LocalReviewedConceptStoreTests.cs`

**Interfaces:**
- Consumes: completed service/writer/CLI-independent production APIs.
- Produces: end-to-end proof of branch isolation, no tracked writes, deterministic promotion, and unchanged Project Memory ownership.

- [ ] **Step 1: Add failing integration tests**

Add deterministic tests:

```csharp
[Fact] public async Task Promotion_WritesOnlyTargetBranchSemanticDirectory();
[Fact] public async Task Promotion_DoesNotModifySourceCatalog();
[Fact] public async Task Promotion_DoesNotCreateSnapshotMemoryOrEvaluationArtifacts();
[Fact] public async Task Promotion_DoesNotModifyFilesInsideRepositoryRoot();
[Fact] public async Task ProjectMemorySync_RemainsUnawareOfLifecycleWriterArtifacts();
[Fact] public async Task SourceCatalogFromDeletedBranchNameWorksWhenLocalArtifactExists();
[Fact] public async Task SourceCatalogAbsentFailsWithoutGitOrNetworkLookup();
```

Generate the 25/43/29 catalog deterministically in test code so no branch-specific fingerprint fixture is checked in. The repository-root test snapshots relative paths, content hashes, and Git porcelain output before and after promotion. Configure the lifecycle branch roots under a separate temporary data root. Assert no `git`, network, or provider operation is required beyond the injected read-only Git state provider.

Strengthen the existing Project Memory test at lines 65-93 so it preserves semantic artifact bytes and timestamp through initialize, incremental sync, stale managed-note deletion, and rebuild.

- [ ] **Step 2: Run the integration subset and confirm any missing proof fails**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleServiceTests|FullyQualifiedName~ProjectMemoryServiceTests|FullyQualifiedName~LocalReviewedConceptStoreTests"
```

Expected: new tests FAIL until any missing isolation checks are added to test fixtures or production boundaries.

- [ ] **Step 3: Add only the minimum fixture/boundary adjustments**

Keep all lifecycle writes rooted in caller-supplied branch knowledge locations. Do not add concept references to Project Memory models/services and do not make `LocalReviewedConceptStore` writable. If tests expose a production ownership leak requiring a forbidden file, stop and report rather than changing that file.

- [ ] **Step 4: Run ownership and complete test suites**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycleServiceTests|FullyQualifiedName~LocalReviewedConceptWriterTests|FullyQualifiedName~ProjectMemoryServiceTests"
dotnet test EngineeringBrain.sln --no-restore
```

Expected: at least 313 tests PASS, zero failed, zero skipped.

- [ ] **Step 5: Prove forbidden files are untouched and commit tests**

```powershell
git diff --exit-code main -- src/EngineeringBrain.Core/ProjectMemoryModels.cs src/EngineeringBrain.Infrastructure/ProjectMemoryService.cs src/EngineeringBrain.Infrastructure/LocalProjectMemoryStore.cs src/EngineeringBrain.Infrastructure/ConceptCandidateReranker.cs src/EngineeringBrain.Infrastructure/InitiativeCandidateRetriever.cs
git add tests/EngineeringBrain.Core.Tests/ReviewedConceptLifecycleServiceTests.cs tests/EngineeringBrain.Core.Tests/ProjectMemoryServiceTests.cs tests/EngineeringBrain.Core.Tests/LocalReviewedConceptStoreTests.cs
git commit -m "test: verify reviewed concept lifecycle isolation"
```

Expected: forbidden-file diff command exits `0`; the deterministic fixture remains test code rather than a branch-bound JSON artifact.

---

### Task 10: ADR and User Documentation

**Files:**
- Create: `docs/decisions/0010-reviewed-concept-lifecycle.md`
- Modify: `README.md`
- Modify: `docs/architecture/README.md`
- Modify: `docs/superpowers/plans/2026-09-12-reviewed-concept-lifecycle.md` only to mark completed checkboxes during execution; do not alter planned behavior.

**Interfaces:**
- Consumes: final implemented syntax, outcomes, and exit codes.
- Produces: concise operational and architectural documentation.

- [ ] **Step 1: Write a failing documentation contract check**

Run before editing:

```powershell
rg -n "concepts status|concepts validate|concepts promote|LocalReviewedConceptWriter|current checked-out branch|exclusive.*lock" README.md docs/decisions docs/architecture
```

Expected: command/lifecycle documentation is absent and the command exits nonzero or returns incomplete matches.

- [ ] **Step 2: Write ADR 0010**

Document these decisions explicitly:

- Runtime reader remains read-only; lifecycle writer is the sole mutation boundary.
- Status/validate use transient fresh evidence and do not persist snapshots or Project Memory.
- Source trust is artifact-integrity plus human authority, not historical source-code revalidation.
- Target must be the current clean branch; another worktree is supported via `--repo`.
- Every assignment is rebound and proven against target evidence; no partial promotion.
- Exclusive sibling lock covers target precondition read, expected hash comparison, and atomic move.
- Invalid existing targets block; no force repair.
- No receipt, checkout, fetch, editor, import/export, retrieval, or schema work in V1.

- [ ] **Step 3: Update README and architecture index**

Add the three exact command forms, output states, validate exit codes, promotion safety rules, and local artifact path. State that promotion mutates only the external reviewed artifact and that normal analysis remains read-only. Link ADR 0010 from the architecture index.

- [ ] **Step 4: Verify documentation is complete and bounded**

```powershell
rg -n "concepts status|concepts validate|concepts promote|LocalReviewedConceptWriter|current checked-out branch|exclusive.*lock|no partial" README.md docs/decisions/0010-reviewed-concept-lifecycle.md docs/architecture/README.md
git diff --check
```

Expected: every concept is present; `git diff --check` emits no errors.

- [ ] **Step 5: Commit documentation**

```powershell
git add README.md docs/architecture/README.md docs/decisions/0010-reviewed-concept-lifecycle.md docs/superpowers/plans/2026-09-12-reviewed-concept-lifecycle.md
git commit -m "docs: document reviewed concept lifecycle"
```

---

### Task 11: Final Build, Evaluation, and Repository Verification

**Files:**
- Verify only; do not modify production behavior or evaluation fixtures.

**Interfaces:**
- Consumes: all prior tasks.
- Produces: final acceptance evidence.

- [ ] **Step 1: Verify focused lifecycle tests from a clean build**

```powershell
dotnet build EngineeringBrain.sln --no-restore
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptLifecycle|FullyQualifiedName~LocalReviewedConceptWriter|FullyQualifiedName~TransientRepositorySnapshotStore"
```

Expected: build has 0 errors and 0 warnings; all lifecycle tests PASS with zero skipped.

- [ ] **Step 2: Run the full suite and record exact counts**

```powershell
dotnet test EngineeringBrain.sln --no-restore
```

Expected: at least 313 tests, 0 failed, 0 skipped.

- [ ] **Step 3: Validate CLI read-only behavior**

Record hashes/timestamps for the repository snapshot, Project Memory manifest/notes, reviewed catalog, and latest evaluation artifact. Then run:

```powershell
dotnet run --project src/EngineeringBrain.Cli --no-build -- concepts status .
dotnet run --project src/EngineeringBrain.Cli --no-build -- concepts validate .
```

Expected: status exits `0`; validate reports the current catalog state; all recorded files retain identical hashes/timestamps.

- [ ] **Step 4: Run an isolated successful promotion and failure preservation check**

Use a temporary data root through direct lifecycle-service integration tests, not the user's real catalog. Confirm `Created`, second run `Unchanged`, and a missing EntityId leaves the existing target unchanged. Do not create or edit a real reviewed catalog during this verification step.

- [ ] **Step 5: Run the standard offline evaluation with the already-valid current-branch catalog**

```powershell
dotnet run --project src/EngineeringBrain.Cli --no-build -- eval .
```

Expected:

```text
Cases: 16
Passed: 16
Regressions: 0
Recall@5: 1.000
Recall@10: 1.000
MRR: 0.867
ReviewedConceptStatus: Valid
ReviewedConceptProfileCount: 29
```

If the implementation branch has no valid local catalog, use `concepts promote` once from the existing local `main` catalog to the current clean implementation branch, report the external target path/fingerprint, then run evaluation. This is an explicit local lifecycle acceptance operation, never a Git change. Do not alter fixtures or metrics to force the expected result.

- [ ] **Step 6: Confirm retrieval and ownership invariants**

```powershell
git diff --exit-code main -- src/EngineeringBrain.Infrastructure/ConceptCandidateReranker.cs src/EngineeringBrain.Infrastructure/InitiativeCandidateRetriever.cs src/EngineeringBrain.Infrastructure/ProjectMemoryService.cs src/EngineeringBrain.Core/ProjectMemoryModels.cs src/EngineeringBrain.Infrastructure/LocalProjectMemoryStore.cs
rg -n "OpenAI|HttpClient|embedding|vector" src/EngineeringBrain.Core/ReviewedConceptLifecycleModels.cs src/EngineeringBrain.Infrastructure/ReviewedConceptLifecycleService.cs src/EngineeringBrain.Infrastructure/LocalReviewedConceptWriter.cs src/EngineeringBrain.Infrastructure/TransientRepositorySnapshotStore.cs
```

Expected: forbidden-file diff exits `0`; search returns no lifecycle network/vector dependency.

- [ ] **Step 7: Final diff and clean-state review**

```powershell
git diff --check
git status --short --branch
git log --oneline main..HEAD
```

Expected: no whitespace errors, no untracked scratch/build artifacts, and a clean working tree after the task commits. Do not squash and do not push.

## Final Self-Review Checklist

- Every V1 command and exit status is assigned to a task.
- `status` and `validate` use a transient snapshot store and in-memory Project Memory build; no persistent sync occurs.
- Source integrity never claims historical source-code revalidation.
- Target envelope fields and assignment evidence are created only from target evidence.
- One failed assignment blocks the writer; no valid sibling is silently promoted alone.
- Existing invalid target catalogs block before mutation.
- The exclusive lock spans precondition read, comparison, flush, final Git validation, second comparison, and atomic move.
- `Unchanged` performs no write and preserves timestamp.
- Result type names and signatures are consistent across all tasks.
- Runtime reader remains read-only and Project Memory/retrieval files remain untouched.
- No schema, retrieval, provider, policy, UI, import/export, or embedding work entered the plan.
