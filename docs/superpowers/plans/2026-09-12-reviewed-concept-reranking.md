# Reviewed Concept Reranking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a local, reviewed, branch-scoped semantic layer that can apply the frozen Vocabulary V2 + A1 + C1 + E2 + S2 candidate as bounded reranking over positive lexical component candidates.

**Architecture:** Reviewed concepts live in a read-only artifact beside, but outside the ownership of, generated Project Memory. CLI orchestration loads, validates, and resolves the artifact once into a dedicated runtime result, then passes the same immutable profiles to preview, analysis, offline evaluation, and live evaluation. `InitiativeCandidateRetriever` remains the lexical candidate generator and delegates only the positive lexical pool to a pure concept reranker before the direct Top-K cutoff.

**Tech Stack:** .NET 10, C# records, `System.Text.Json`, SHA-256 through the existing `KnowledgeIdentity`, xUnit, Roslyn-generated repository snapshots, deterministic Project Memory.

**Spec:** The user-approved production architecture in this plan's **Approved Design Contract** section; Task 12 records it permanently in `docs/decisions/0009-reviewed-concept-reranking.md`.

## Global Constraints

- Keep the Code Graph and repository snapshot free of reviewed semantic declarations.
- Keep `ProjectMemoryService` and `LocalProjectMemoryStore` unaware of reviewed concept loading, validation, resolution, mutation, and cleanup.
- Do not modify `ProjectMemoryModels.cs`, `ProjectMemoryService.cs`, `LocalProjectMemoryStore.cs`, snapshot schemas, Project Memory schema 1, policies, prompts, models, or provider behavior.
- `reviewed-concepts.json` is read-only; no CLI authoring/import command is part of this milestone.
- Never perform a provider or network call while implementing or verifying this plan.
- Absence of the artifact must preserve lexical-graph-v2 scores, ordering, match reasons, project ranking, and graph expansion.
- Invalid catalog envelope disables the entire catalog; an invalid declaration excludes only that declaration; an invalid or stale assignment excludes only that assignment.
- Concepts may rerank only candidates that already have positive lexical evidence.
- Concepts never score graph-expanded candidates and never contribute points to project ranking.
- Preserve A1 reviewed anchors, P1 phrase qualification, C1 responsibility support, S2 at +2 per matched concept token with a per-component cap of +6, and E2 exact-identity precedence.
- Do not add stemming, aliases, embeddings, dependencies, lexical weights, or new graph behavior.
- Use `dotnet ... --no-restore` for all build and test commands.
- Commit each implementation task locally at its stated boundary; do not push.

## Approved Design Contract

The runtime artifact is located at:

```text
~/.engineering-brain/
  repositories/<repository-key>/
    knowledge/branches/<branch-key>/
      semantic/reviewed-concepts.json
```

`LocalReviewedConceptStore` reads this file from an already resolved branch knowledge location. It never creates, writes, moves, or deletes the artifact. CLI orchestration builds immutable objective evidence from `ProjectMemorySyncResult.Manifest` and `ProjectMemorySyncResult.SourceSnapshot`; this does not make Project Memory responsible for the reviewed layer.

The production flow is:

```text
repository scan
-> generated Project Memory sync
-> reviewed artifact load
-> envelope/declaration validation
-> assignment resolution against immutable component evidence
-> initiative understanding
-> lexical-positive component scoring
-> reviewed concept qualification and bounded reranking
-> direct Top-K
-> existing graph expansion
-> lexical-only project ranking
-> CALL #2 context
```

The orchestration composition points are currently:

- `src/EngineeringBrain.Cli/Program.cs:88-140` for preview plus actual initiative analysis.
- `src/EngineeringBrain.Cli/Program.cs:244-304` for deterministic offline evaluation.
- `src/EngineeringBrain.Cli/Program.cs:319-374` for live/fake evaluation.

Downstream retrieval call sites are:

- `src/EngineeringBrain.Infrastructure/InitiativeAnalysisService.cs:39-76`.
- `src/EngineeringBrain.Infrastructure/RemoteContextPreviewService.cs:55-69`.
- `src/EngineeringBrain.Infrastructure/EvaluationServices.cs:191-233`.
- `src/EngineeringBrain.Infrastructure/LiveEvaluationService.cs:129-191`.

## File Map

Create:

- `src/EngineeringBrain.Core/ReviewedConceptModels.cs`: artifact, validation, evidence, resolved-profile, diagnostic, and resolution-result records.
- `src/EngineeringBrain.Infrastructure/ReviewedConceptSerializer.cs`: deterministic schema-1 JSON and declaration fingerprints.
- `src/EngineeringBrain.Infrastructure/LocalReviewedConceptStore.cs`: read-only, path-safe artifact loading.
- `src/EngineeringBrain.Infrastructure/ReviewedConceptValidator.cs`: envelope and localized declaration/assignment validation.
- `src/EngineeringBrain.Infrastructure/ReviewedConceptResolver.cs`: localized assignment-to-evidence resolution.
- `src/EngineeringBrain.Infrastructure/ConceptCandidateReranker.cs`: pure A1/C1/P1/S2/E2 reranking.
- `tests/EngineeringBrain.Core.Tests/ReviewedConceptTestData.cs`: reusable catalogs, evidence, profiles, and candidates.
- `tests/EngineeringBrain.Core.Tests/ReviewedConceptModelTests.cs`.
- `tests/EngineeringBrain.Core.Tests/ReviewedConceptSerializerTests.cs`.
- `tests/EngineeringBrain.Core.Tests/LocalReviewedConceptStoreTests.cs`.
- `tests/EngineeringBrain.Core.Tests/ReviewedConceptValidatorTests.cs`.
- `tests/EngineeringBrain.Core.Tests/ReviewedConceptResolverTests.cs`.
- `tests/EngineeringBrain.Core.Tests/ConceptCandidateRerankerTests.cs`.
- `tests/EngineeringBrain.Core.Tests/TestData/reviewed-concepts-v2.json`: reviewed migration fixture matching the production schema.
- `tests/EngineeringBrain.Core.Tests/ReviewedConceptVocabularyV2Tests.cs`.
- `docs/decisions/0009-reviewed-concept-reranking.md`.

Modify:

- `src/EngineeringBrain.Infrastructure/InitiativeCandidateRetriever.cs:5-26,79-175,250-292`.
- `src/EngineeringBrain.Infrastructure/InitiativeAnalysisService.cs:17-76`.
- `src/EngineeringBrain.Infrastructure/RemoteContextPreviewService.cs:27-103`.
- `src/EngineeringBrain.Infrastructure/EvaluationServices.cs:191-233`.
- `src/EngineeringBrain.Infrastructure/LiveEvaluationService.cs:7-79,129-191`.
- `src/EngineeringBrain.Cli/Program.cs:88-140,244-304,319-374`.
- `tests/EngineeringBrain.Core.Tests/InitiativeCandidateRetrieverTests.cs:8-308`.
- `tests/EngineeringBrain.Core.Tests/InitiativeAnalysisServiceTests.cs:9-193`.
- `tests/EngineeringBrain.Core.Tests/RemoteContextPreviewTests.cs:10-41`.
- `tests/EngineeringBrain.Core.Tests/EvaluationHarnessTests.cs:9-96`.
- `tests/EngineeringBrain.Core.Tests/LiveEvaluationTests.cs` around existing retrieval-comparison tests.
- `tests/EngineeringBrain.Core.Tests/ProjectMemoryServiceTests.cs:65-87` only to strengthen the ownership regression test.
- `tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj` only if the reviewed catalog fixture needs explicit copy metadata.
- `README.md` retrieval and local-storage sections.

Explicitly unchanged:

- `src/EngineeringBrain.Core/ProjectMemoryModels.cs`.
- `src/EngineeringBrain.Infrastructure/ProjectMemoryService.cs`.
- `src/EngineeringBrain.Infrastructure/LocalProjectMemoryStore.cs`.
- `src/EngineeringBrain.Infrastructure/ProjectMemoryBuilder.cs`.
- `src/EngineeringBrain.Infrastructure/ProjectMemoryManifestSerializer.cs`.
- `src/EngineeringBrain.Infrastructure/InitiativeTermNormalizer.cs`.
- All snapshot, policy, prompt, and provider schemas.

---

### Task 1: Core Reviewed-Concept Domain and Evidence Models

**Files:**
- Create: `src/EngineeringBrain.Core/ReviewedConceptModels.cs`
- Create: `tests/EngineeringBrain.Core.Tests/ReviewedConceptTestData.cs`
- Create: `tests/EngineeringBrain.Core.Tests/ReviewedConceptModelTests.cs`

**Interfaces:**
- Consumes: `AnalysisDiagnosticSeverity`, `ProjectMemorySyncResult`, `KnowledgeNoteKind`, and `RepositorySnapshot` from Core.
- Produces: all immutable contracts used by Tasks 2-10.

- [ ] **Step 1: Write the failing model and evidence-context tests**

Create tests named:

```csharp
[Fact]
public void ResolutionResult_AbsentContainsNoProfilesOrDiagnostics()
{
    var result = ReviewedConceptResolutionResult.Absent;

    Assert.Equal(ReviewedConceptResolutionStatus.Absent, result.Status);
    Assert.Null(result.CatalogFingerprint);
    Assert.Empty(result.Profiles);
    Assert.Empty(result.Diagnostics);
}

[Fact]
public void EvidenceContext_FromMemoryUsesOnlyManagedComponentFingerprints()
{
    var snapshot = ProjectMemoryTestFactory.Create();
    var build = new ProjectMemoryBuilder().Build(snapshot);
    var memory = ReviewedConceptTestData.Memory(snapshot, build.Manifest);

    var evidence = ReviewedConceptEvidenceContext.FromMemory(memory);

    Assert.Equal(snapshot.Repository.Id, evidence.RepositoryId);
    Assert.Equal(snapshot.Git.Branch, evidence.Branch);
    Assert.Equal(build.Manifest.BranchKey, evidence.BranchKey);
    Assert.Equal(
        build.Manifest.Notes.Count(note => note.Kind == KnowledgeNoteKind.Component),
        evidence.Components.Count);
    Assert.All(evidence.Components.Values, item =>
        Assert.False(string.IsNullOrWhiteSpace(item.SourceFingerprint)));
}
```

`ReviewedConceptTestData.Memory` must construct `ProjectMemorySyncResult` using existing Core records and a synthetic location; it must not touch the filesystem.

- [ ] **Step 2: Run the focused tests and confirm the expected compile failure**

Run:

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~ReviewedConceptModelTests
```

Expected: FAIL with CS0246 errors for `ReviewedConceptResolutionResult` and `ReviewedConceptEvidenceContext`.

- [ ] **Step 3: Add the minimal immutable model set**

Define these exact public contracts:

```csharp
namespace EngineeringBrain.Core;

public enum ReviewedConceptAnchorPolicy { Clear, Ambiguous, NotRequired }
public enum ReviewedConceptLoadStatus { Absent, Loaded, Invalid }
public enum ReviewedConceptResolutionStatus { Absent, Valid, ValidWithDiagnostics, Invalid }
public enum ReviewedConceptDiagnosticScope { Catalog, Declaration, Assignment }

public sealed record ReviewedConceptCatalog(
    int SchemaVersion,
    string RepositoryId,
    string Branch,
    string BranchKey,
    int SourceSnapshotSchema,
    string SourceAnalyzerVersion,
    string VocabularyVersion,
    IReadOnlyList<ReviewedConceptDeclaration> Declarations);

public sealed record ReviewedConceptDeclaration(
    string ConceptId,
    string Definition,
    ReviewedConceptAnchorPolicy AnchorPolicy,
    IReadOnlyList<IReadOnlyList<string>> AnchorTokens,
    IReadOnlyList<string> QualificationSupportTokens,
    IReadOnlyList<string> ContextSupportTokens,
    IReadOnlyList<ReviewedConceptAssignment> Assignments,
    ReviewedConceptProvenance Provenance,
    ReviewedConceptReview Review,
    string Fingerprint);

public sealed record ReviewedConceptAssignment(
    string EntityId,
    string SourceReference,
    string SourceFingerprint);

public sealed record ReviewedConceptProvenance(string SourceReference, string SourceHash);

public sealed record ReviewedConceptReview(
    string Reviewer,
    int Version,
    DateTimeOffset ReviewedAtUtc);

public sealed record ResolvedReviewedConcept(
    string ConceptId,
    string Definition,
    ReviewedConceptAnchorPolicy AnchorPolicy,
    IReadOnlyList<IReadOnlyList<string>> AnchorTokens,
    IReadOnlyList<string> QualificationSupportTokens,
    IReadOnlyList<string> ContextSupportTokens,
    string DeclarationFingerprint);

public sealed record ComponentConceptProfile(
    string EntityId,
    string SourceFingerprint,
    IReadOnlyList<ResolvedReviewedConcept> Concepts);

public sealed record ComponentFingerprintEvidence(
    string EntityId,
    string RelativePath,
    int StartLine,
    int EndLine,
    string SourceFingerprint);

public sealed record ReviewedConceptEvidenceContext(
    string RepositoryId,
    string Branch,
    string BranchKey,
    int SourceSnapshotSchema,
    string SourceAnalyzerVersion,
    IReadOnlyDictionary<string, ComponentFingerprintEvidence> Components)
{
    public static ReviewedConceptEvidenceContext FromMemory(ProjectMemorySyncResult memory)
    {
        ArgumentNullException.ThrowIfNull(memory);
        var entities = memory.SourceSnapshot.Entities.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var components = memory.Manifest.Notes
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
            memory.Manifest.RepositoryId,
            memory.Manifest.Branch,
            memory.Manifest.BranchKey,
            memory.Manifest.SourceSnapshotSchema,
            memory.Manifest.SourceAnalyzerVersion,
            components);
    }
}

public sealed record ReviewedConceptDiagnostic(
    string Code,
    AnalysisDiagnosticSeverity Severity,
    ReviewedConceptDiagnosticScope Scope,
    string Message,
    string? ConceptId = null,
    string? EntityId = null);

public sealed record ReviewedConceptLoadResult(
    ReviewedConceptLoadStatus Status,
    string Path,
    string? ContentHash,
    ReviewedConceptCatalog? Catalog,
    IReadOnlyList<ReviewedConceptDiagnostic> Diagnostics);

public sealed record ReviewedConceptValidationResult(
    bool CatalogIsValid,
    IReadOnlyList<ReviewedConceptDeclaration> Declarations,
    IReadOnlyList<ReviewedConceptDiagnostic> Diagnostics);

public sealed record ReviewedConceptResolutionResult(
    ReviewedConceptResolutionStatus Status,
    string? CatalogFingerprint,
    IReadOnlyList<ComponentConceptProfile> Profiles,
    IReadOnlyList<ReviewedConceptDiagnostic> Diagnostics)
{
    public static ReviewedConceptResolutionResult Absent { get; } =
        new(ReviewedConceptResolutionStatus.Absent, null, [], []);
}
```

Do not add any property to `ProjectMemorySyncResult`.

- [ ] **Step 4: Run the focused and Core suites**

Run:

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~ReviewedConceptModelTests
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore
```

Expected: focused tests PASS; existing Core suite PASS with zero skipped tests.

- [ ] **Step 5: Commit the domain contracts**

```powershell
git add src/EngineeringBrain.Core/ReviewedConceptModels.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptTestData.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptModelTests.cs
git commit -m "feat: add reviewed concept domain contracts"
```

---

### Task 2: Deterministic Schema-1 Serialization and Fingerprints

**Files:**
- Create: `src/EngineeringBrain.Infrastructure/ReviewedConceptSerializer.cs`
- Create: `tests/EngineeringBrain.Core.Tests/ReviewedConceptSerializerTests.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/ReviewedConceptTestData.cs`

**Interfaces:**
- Consumes: Task 1 catalog and declaration records; `KnowledgeIdentity.ContentHash` and `KnowledgeIdentity.Fingerprint`.
- Produces: `ReviewedConceptSerializer.CurrentSchemaVersion`, `Serialize`, `Deserialize`, `Canonicalize`, and `CreateDeclarationFingerprint`.

- [ ] **Step 1: Write failing serializer tests**

Cover these exact behaviors:

```csharp
[Fact]
public void Serialize_IsDeterministicAcrossInputOrderingAndLineEndings()
{
    var first = ReviewedConceptTestData.Catalog(reversed: false);
    var second = ReviewedConceptTestData.Catalog(reversed: true);

    Assert.Equal(
        ReviewedConceptSerializer.Serialize(first),
        ReviewedConceptSerializer.Serialize(second));
}

[Fact]
public void DeclarationFingerprint_ExcludesStoredFingerprintAndIncludesReviewAndAssignments()
{
    var declaration = ReviewedConceptTestData.Declaration();
    var first = ReviewedConceptSerializer.CreateDeclarationFingerprint(declaration with { Fingerprint = "ignored-a" });
    var second = ReviewedConceptSerializer.CreateDeclarationFingerprint(declaration with { Fingerprint = "ignored-b" });
    var changed = ReviewedConceptSerializer.CreateDeclarationFingerprint(
        declaration with { Review = declaration.Review with { Version = declaration.Review.Version + 1 } });

    Assert.Equal(first, second);
    Assert.NotEqual(first, changed);
}

[Fact]
public void Deserialize_MalformedJsonThrowsSafeInvalidDataException()
{
    var exception = Assert.Throws<InvalidDataException>(() =>
        ReviewedConceptSerializer.Deserialize("{not-json"));

    Assert.Equal("Reviewed concept JSON is invalid.", exception.Message);
}
```

Also round-trip all fields and assert output has LF line endings and one trailing LF.

- [ ] **Step 2: Run tests and confirm the missing serializer failure**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~ReviewedConceptSerializerTests
```

Expected: FAIL with CS0103/CS0246 for `ReviewedConceptSerializer`.

- [ ] **Step 3: Implement deterministic canonicalization**

Use `System.Text.Json` with camelCase, case-insensitive reads, indented writes, and camelCase enum strings. `Canonicalize` must:

```csharp
public static ReviewedConceptCatalog Canonicalize(ReviewedConceptCatalog catalog) => catalog with
{
    Declarations = catalog.Declarations
        .Select(declaration => declaration with
        {
            AnchorTokens = declaration.AnchorTokens
                .Select(group => (IReadOnlyList<string>)group.Order(StringComparer.Ordinal).ToArray())
                .OrderBy(group => string.Join('\n', group), StringComparer.Ordinal)
                .ToArray(),
            QualificationSupportTokens = declaration.QualificationSupportTokens
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            ContextSupportTokens = declaration.ContextSupportTokens
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            Assignments = declaration.Assignments
                .OrderBy(item => item.EntityId, StringComparer.Ordinal)
                .ThenBy(item => item.SourceReference, StringComparer.Ordinal)
                .ToArray()
        })
        .OrderBy(item => item.ConceptId, StringComparer.Ordinal)
        .ToArray()
};
```

`CreateDeclarationFingerprint` must call `KnowledgeIdentity.Fingerprint` over canonical fields in this order: ConceptId, Definition, AnchorPolicy, flattened anchor groups, qualification tokens, context tokens, ordered assignment triples, provenance fields, reviewer, review version using invariant culture, and UTC review timestamp in `O` format. Never include `Fingerprint` itself.

`Deserialize` performs JSON syntax/deserialization only. Schema and semantic validation remain Task 4 responsibilities.

- [ ] **Step 4: Run focused and serialization-related tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptSerializerTests|FullyQualifiedName~ProjectMemoryBuilderTests"
```

Expected: all selected tests PASS and serialization remains deterministic.

- [ ] **Step 5: Commit serializer behavior**

```powershell
git add src/EngineeringBrain.Infrastructure/ReviewedConceptSerializer.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptSerializerTests.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptTestData.cs
git commit -m "feat: serialize reviewed concept catalogs"
```

---

### Task 3: Read-Only Local Reviewed Concept Store

**Files:**
- Create: `src/EngineeringBrain.Infrastructure/LocalReviewedConceptStore.cs`
- Create: `tests/EngineeringBrain.Core.Tests/LocalReviewedConceptStoreTests.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/ProjectMemoryServiceTests.cs:65-87`

**Interfaces:**
- Consumes: `ReviewedConceptSerializer` and Task 1 load models.
- Produces: `GetPath(string branchKnowledgeLocation)` and `LoadAsync(string branchKnowledgeLocation, CancellationToken)`.

- [ ] **Step 1: Write failing path, absence, malformed-input, and ownership tests**

Add tests that assert:

```csharp
[Fact]
public async Task LoadAsync_AbsentArtifactReturnsAbsentWithoutCreatingDirectories()
{
    using var fixture = new TemporaryDirectory();
    var branchRoot = Path.Combine(fixture.Path, "branch");

    var result = await new LocalReviewedConceptStore().LoadAsync(branchRoot);

    Assert.Equal(ReviewedConceptLoadStatus.Absent, result.Status);
    Assert.False(Directory.Exists(branchRoot));
}

[Fact]
public async Task LoadAsync_MalformedArtifactReturnsInvalidSafeDiagnostic()
{
    using var fixture = new TemporaryDirectory();
    var path = new LocalReviewedConceptStore().GetPath(fixture.Path);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllTextAsync(path, "{not-json");

    var result = await new LocalReviewedConceptStore().LoadAsync(fixture.Path);

    Assert.Equal(ReviewedConceptLoadStatus.Invalid, result.Status);
    Assert.Null(result.Catalog);
    Assert.Single(result.Diagnostics, item => item.Code == "RC001");
    Assert.DoesNotContain("not-json", result.Diagnostics[0].Message, StringComparison.Ordinal);
}
```

Strengthen `SyncAsync_DeletesOnlyManifestManagedStaleNotesAndPreservesUserFile` by creating `semantic/reviewed-concepts.json` before the second sync, recording its bytes and timestamp, then asserting both remain unchanged afterward.

- [ ] **Step 2: Run tests and confirm the store is missing**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LocalReviewedConceptStoreTests|FullyQualifiedName~SyncAsync_DeletesOnlyManifestManagedStaleNotesAndPreservesUserFile"
```

Expected: new store tests FAIL to compile; the strengthened memory test compiles independently and continues proving unmanaged preservation.

- [ ] **Step 3: Implement read-only loading**

Implement exactly:

```csharp
public sealed class LocalReviewedConceptStore
{
    public string GetPath(string branchKnowledgeLocation);

    public Task<ReviewedConceptLoadResult> LoadAsync(
        string branchKnowledgeLocation,
        CancellationToken cancellationToken = default);
}
```

`GetPath` resolves `<full branch root>/semantic/reviewed-concepts.json` and rejects path escape. `LoadAsync` must:

- Return `Absent` without creating any directory when the file does not exist.
- Read with the cancellation token.
- Deserialize and return `Loaded` with `KnowledgeIdentity.ContentHash(rawJson)`.
- Rethrow `OperationCanceledException`.
- Convert `IOException`, `UnauthorizedAccessException`, and `InvalidDataException` into `Invalid` with catalog-scoped diagnostic `RC001` and no source content or absolute source path in the message.
- Expose no save/delete method.

- [ ] **Step 4: Run focused and Project Memory tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~LocalReviewedConceptStoreTests|FullyQualifiedName~ProjectMemoryServiceTests"
```

Expected: all selected tests PASS; memory sync leaves semantic artifact bytes and timestamp unchanged.

- [ ] **Step 5: Commit the independent store**

```powershell
git add src/EngineeringBrain.Infrastructure/LocalReviewedConceptStore.cs tests/EngineeringBrain.Core.Tests/LocalReviewedConceptStoreTests.cs tests/EngineeringBrain.Core.Tests/ProjectMemoryServiceTests.cs
git commit -m "feat: load reviewed concepts from local branch storage"
```

---

### Task 4: Catalog and Localized Declaration Validation

**Files:**
- Create: `src/EngineeringBrain.Infrastructure/ReviewedConceptValidator.cs`
- Create: `tests/EngineeringBrain.Core.Tests/ReviewedConceptValidatorTests.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/ReviewedConceptTestData.cs`

**Interfaces:**
- Consumes: `ReviewedConceptCatalog`, `ReviewedConceptEvidenceContext`, `ReviewedConceptSerializer.CreateDeclarationFingerprint`, `InitiativeTermNormalizer`.
- Produces: `ReviewedConceptValidationResult Validate(catalog, evidence)`.

- [ ] **Step 1: Write failing tests for failure granularity**

Create separate tests for:

- Unsupported schema, repository mismatch, branch mismatch, branch-key mismatch, snapshot-schema mismatch, analyzer mismatch, and duplicate `ConceptId`: `CatalogIsValid == false`, no declarations returned, catalog-scoped error.
- Invalid declaration fingerprint, blank definition, invalid reviewer/version, invalid token, overlapping qualification/context token, invalid anchor policy configuration: only that declaration is excluded.
- Structurally invalid assignment fields: only that assignment is removed from its otherwise valid declaration.
- A valid sibling declaration and valid sibling assignment remain present whenever another declaration/assignment is excluded.

Use this representative granularity assertion:

```csharp
[Fact]
public void Validate_InvalidDeclarationAndAssignmentAreLocalized()
{
    var evidence = ReviewedConceptTestData.Evidence();
    var valid = ReviewedConceptTestData.WithFingerprint(
        ReviewedConceptTestData.Declaration("valid-concept", "entity:business-service"));
    var invalidDeclaration = ReviewedConceptTestData.WithFingerprint(
        ReviewedConceptTestData.Declaration("broken-concept", "entity:model")) with
        { Definition = " " };
    var mixed = ReviewedConceptTestData.WithFingerprint(
        ReviewedConceptTestData.Declaration("mixed-concept", "entity:business-service") with
        {
            Assignments =
            [
                ReviewedConceptTestData.Assignment("entity:business-service"),
                new ReviewedConceptAssignment("", "src/invalid.cs:1", "fingerprint")
            ]
        });

    var result = new ReviewedConceptValidator().Validate(
        ReviewedConceptTestData.Catalog([valid, invalidDeclaration, mixed]), evidence);

    Assert.True(result.CatalogIsValid);
    Assert.DoesNotContain(result.Declarations, item => item.ConceptId == "broken-concept");
    Assert.Single(result.Declarations.Single(item => item.ConceptId == "mixed-concept").Assignments);
    Assert.Contains(result.Diagnostics, item => item.Scope == ReviewedConceptDiagnosticScope.Declaration);
    Assert.Contains(result.Diagnostics, item => item.Scope == ReviewedConceptDiagnosticScope.Assignment);
}
```

- [ ] **Step 2: Run tests and confirm validator absence**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~ReviewedConceptValidatorTests
```

Expected: FAIL with CS0246 for `ReviewedConceptValidator`.

- [ ] **Step 3: Implement deterministic validation rules**

Implement:

```csharp
public sealed partial class ReviewedConceptValidator
{
    public ReviewedConceptValidationResult Validate(
        ReviewedConceptCatalog catalog,
        ReviewedConceptEvidenceContext evidence);
}
```

Catalog errors use codes `RC100`-`RC106` and disable the whole catalog. Duplicate ConceptIds use ordinal identity and code `RC107`.

Declaration validation uses `RC200`-`RC207` and excludes only the declaration. Enforce:

- `ConceptId` matches `^[a-z0-9]+(?:-[a-z0-9]+)*$`.
- Definition, provenance reference/hash, reviewer are nonblank; review version is positive; review timestamp has a UTC offset.
- Stored fingerprint equals deterministic recomputation.
- Every metadata token normalizes to exactly itself as one `InitiativeTermNormalizer` token.
- Every metadata token exists among normalized ConceptId tokens.
- Qualification and context support sets are disjoint.
- `Clear` requires at least one nonempty anchor group and at least one qualification support token.
- `NotRequired` requires no anchor groups.
- `Ambiguous` may retain reviewed anchor groups, but C1 will use P1 unchanged.

Assignment structural errors use `RC300`-`RC303` and remove only that assignment. Require nonblank EntityId, repository-relative normalized source reference, nonblank source fingerprint, and no duplicate EntityId inside a declaration.

Order valid declarations by ConceptId and assignments by EntityId before returning.

- [ ] **Step 4: Run focused and normalizer tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptValidatorTests|FullyQualifiedName~InitiativeCandidateRetrieverTests.Retrieve_Normalizes"
```

Expected: all selected tests PASS; no normalization behavior changes.

- [ ] **Step 5: Commit validation**

```powershell
git add src/EngineeringBrain.Infrastructure/ReviewedConceptValidator.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptValidatorTests.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptTestData.cs
git commit -m "feat: validate reviewed concept catalogs"
```

---

### Task 5: Localized Assignment Resolution Against Objective Evidence

**Files:**
- Create: `src/EngineeringBrain.Infrastructure/ReviewedConceptResolver.cs`
- Create: `tests/EngineeringBrain.Core.Tests/ReviewedConceptResolverTests.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/ReviewedConceptTestData.cs`

**Interfaces:**
- Consumes: load result, validation result, and immutable evidence context.
- Produces: `ReviewedConceptResolutionResult Resolve(load, validation, evidence)`.

- [ ] **Step 1: Write failing resolver tests**

Cover:

- `Absent` load produces the singleton absent result.
- Invalid load or invalid envelope produces `Invalid`, empty profiles, and diagnostics.
- Missing entity, source-reference mismatch, and source-fingerprint mismatch remove only that assignment and emit assignment-scoped diagnostics.
- Valid sibling assignments in the same declaration and in other declarations remain active.
- Any localized diagnostic yields `ValidWithDiagnostics`; a completely valid catalog yields `Valid`.
- Profiles, concepts, and diagnostics have stable ordinal order.

Use this stale-localization test:

```csharp
[Fact]
public void Resolve_StaleAssignmentIsExcludedWhileValidSiblingRemainsActive()
{
    var evidence = ReviewedConceptTestData.Evidence();
    var declaration = ReviewedConceptTestData.WithFingerprint(
        ReviewedConceptTestData.Declaration("provider-boundary", "entity:business-service") with
        {
            Assignments =
            [
                ReviewedConceptTestData.Assignment("entity:business-service"),
                ReviewedConceptTestData.Assignment("entity:model") with
                    { SourceFingerprint = "stale" }
            ]
        });
    var catalog = ReviewedConceptTestData.Catalog([declaration]);
    var validation = new ReviewedConceptValidator().Validate(catalog, evidence);
    var load = ReviewedConceptTestData.Loaded(catalog);

    var result = new ReviewedConceptResolver().Resolve(load, validation, evidence);

    Assert.Equal(ReviewedConceptResolutionStatus.ValidWithDiagnostics, result.Status);
    Assert.Single(result.Profiles, item => item.EntityId == "entity:business-service");
    Assert.DoesNotContain(result.Profiles, item => item.EntityId == "entity:model");
    Assert.Contains(result.Diagnostics, item => item.Code == "RC402" && item.EntityId == "entity:model");
}
```

- [ ] **Step 2: Run tests and confirm resolver absence**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~ReviewedConceptResolverTests
```

Expected: FAIL with CS0246 for `ReviewedConceptResolver`.

- [ ] **Step 3: Implement localized resolution**

Implement:

```csharp
public sealed class ReviewedConceptResolver
{
    public ReviewedConceptResolutionResult Resolve(
        ReviewedConceptLoadResult load,
        ReviewedConceptValidationResult validation,
        ReviewedConceptEvidenceContext evidence);
}
```

Resolution rules:

- Preserve loader and validator diagnostics.
- Return no profiles for `Absent`, invalid load, or invalid catalog envelope.
- For each surviving assignment, require matching EntityId, normalized relative source path with optional exact line suffix, and ordinal source fingerprint equality.
- Emit `RC400` for missing entity evidence, `RC401` for source-reference mismatch, and `RC402` for stale fingerprint.
- Exclude only the failing assignment.
- Group surviving concepts by EntityId; use the evidence fingerprint in `ComponentConceptProfile`.
- Convert declarations to `ResolvedReviewedConcept` without assignments, provenance prose, or reviewer identity. Preserve declaration fingerprint for audit match reasons.
- Set status to `ValidWithDiagnostics` whenever declaration or assignment diagnostics exist, even if some profiles survive.
- Use `load.ContentHash` as `CatalogFingerprint`.

- [ ] **Step 4: Run focused validator/resolver suites**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptValidatorTests|FullyQualifiedName~ReviewedConceptResolverTests"
```

Expected: all selected tests PASS, including valid sibling retention.

- [ ] **Step 5: Commit resolver behavior**

```powershell
git add src/EngineeringBrain.Infrastructure/ReviewedConceptResolver.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptResolverTests.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptTestData.cs
git commit -m "feat: resolve reviewed concepts against current evidence"
```

---

### Task 6: Pure A1/C1/P1/S2/E2 Concept Reranker

**Files:**
- Create: `src/EngineeringBrain.Infrastructure/ConceptCandidateReranker.cs`
- Create: `tests/EngineeringBrain.Core.Tests/ConceptCandidateRerankerTests.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/ReviewedConceptTestData.cs`

**Interfaces:**
- Consumes: positive lexical `ComponentCandidate` values, normalized query terms, and resolved component profiles.
- Produces: reranked copies with bounded `reviewed concept` match reasons.

- [ ] **Step 1: Write failing qualification and scoring tests**

Mandatory tests:

```csharp
[Fact]
public void Rerank_ClearAnchorRequiresQualificationSupport()
{
    var candidate = ReviewedConceptTestData.Candidate("entity:store", score: 26);
    var profile = ReviewedConceptTestData.PersistenceProfile(candidate.EntityId);

    var result = new ConceptCandidateReranker().Rerank(
        [candidate], ["analysis", "initiative"], [profile]);

    Assert.Equal(26, result[0].Score);
    Assert.DoesNotContain(result[0].MatchReasons, reason => reason.Signal == "reviewed concept");
}

[Fact]
public void Rerank_ContextSupportContributesOnlyAfterValidQualification()
{
    var candidate = ReviewedConceptTestData.Candidate("entity:store", score: 26);
    var profile = ReviewedConceptTestData.PersistenceProfile(candidate.EntityId);

    var result = new ConceptCandidateReranker().Rerank(
        [candidate], ["analysis", "initiative", "persistence"], [profile]);

    Assert.Equal(32, result[0].Score);
    Assert.Contains(result[0].MatchReasons,
        reason => reason.Signal == "reviewed concept" && reason.Points == 6);
}
```

Also test:

- P1 thresholds: 1/1, 2/2, at least 2/3, at least 3 for 4+ tokens.
- Every token in one anchor group is required; any complete group qualifies.
- `Ambiguous` and `NotRequired` use P1 without C1.
- S2 totals +2 per matched ConceptId token and caps at +6 across all concepts on one component.
- Duplicate concept/profile input cannot double-count.
- Exact full-name and exact component-name lexical candidates cannot be overtaken by a lower-base nonexact candidate solely because of concept points.
- Equal final scores use FullName then EntityId ordinal ordering.
- Empty profiles return identical candidate values, reasons, and order.

- [ ] **Step 2: Run tests and confirm reranker absence**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~ConceptCandidateRerankerTests
```

Expected: FAIL with CS0246 for `ConceptCandidateReranker`.

- [ ] **Step 3: Implement the pure reranker**

Define:

```csharp
public sealed class ConceptCandidateReranker
{
    public const int PointsPerMatchedToken = 2;
    public const int MaximumCandidateContribution = 6;

    public IReadOnlyList<ComponentCandidate> Rerank(
        IReadOnlyList<ComponentCandidate> lexicalCandidates,
        IReadOnlyList<string> normalizedQueryTerms,
        IReadOnlyList<ComponentConceptProfile> profiles);
}
```

Qualification algorithm:

```csharp
conceptTokens = normalizer.Tokenize(concept.ConceptId);
matchedConceptTokens = conceptTokens intersect queryTerms;
p1 = conceptTokens.Count switch
{
    1 => matched == 1,
    2 => matched == 2,
    3 => matched >= 2,
    _ => matched >= 3
};
anchorMatched = any anchor group where every token is in queryTerms;
qualificationSupportMatched = any QualificationSupportToken in queryTerms;
qualified = concept.AnchorPolicy switch
{
    Clear => p1 && anchorMatched && qualificationSupportMatched,
    Ambiguous => p1,
    NotRequired => p1
};
```

Only after `qualified` is true, score every matched ConceptId token, including context-support tokens. Sum all qualified concept contributions per component and cap the aggregate at +6. Add one ordered `MatchReason` per contributing concept using:

```text
Signal: reviewed concept
MatchedValue: <ConceptId>@<first 12 chars of DeclarationFingerprint>
Points: actual contribution retained under the aggregate cap
```

E2 comparator:

- Derive exact tier from existing `exact full name` and `exact component name` reasons.
- A stronger-base exact candidate stays ahead of a nonexact concept candidate.
- Between exact candidates, full-name tier precedes component-name tier when scores would otherwise reverse that identity evidence.
- Otherwise sort by final score descending, FullName ordinal, EntityId ordinal.

Do not mutate input candidates or profiles.

- [ ] **Step 4: Run reranker and existing lexical tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ConceptCandidateRerankerTests|FullyQualifiedName~InitiativeCandidateRetrieverTests"
```

Expected: all selected tests PASS; existing lexical tests remain unchanged.

- [ ] **Step 5: Commit the pure reranker**

```powershell
git add src/EngineeringBrain.Infrastructure/ConceptCandidateReranker.cs tests/EngineeringBrain.Core.Tests/ConceptCandidateRerankerTests.cs tests/EngineeringBrain.Core.Tests/ReviewedConceptTestData.cs
git commit -m "feat: add bounded reviewed concept reranking"
```

---

### Task 7: Integrate Reranking Into Direct Component Selection

**Files:**
- Modify: `src/EngineeringBrain.Infrastructure/InitiativeCandidateRetriever.cs:79-175,250-292`
- Modify: `tests/EngineeringBrain.Core.Tests/InitiativeCandidateRetrieverTests.cs:8-308`

**Interfaces:**
- Consumes: optional `IReadOnlyList<ComponentConceptProfile>` and Task 6 reranker.
- Produces: existing `CandidateRetrievalResult` with concept-adjusted direct components and unchanged public schema.

- [ ] **Step 1: Write failing integration and backward-compatibility tests**

Add these exact categories:

```csharp
[Fact]
public void Retrieve_NoProfilesIsEquivalentToLexicalGraphV2()
{
    var snapshot = ProjectMemoryTestFactory.Create();
    var manifest = new ProjectMemoryBuilder().Build(snapshot).Manifest;
    var understanding = InitiativeAnalysisTestData.Understanding("core business execute");
    var retriever = new InitiativeCandidateRetriever();

    var baseline = retriever.Retrieve(understanding, manifest, snapshot);
    var explicitEmpty = retriever.Retrieve(understanding, manifest, snapshot, []);

    Assert.Equal(
        baseline.Components.Select(ReviewedConceptTestData.CandidateProjection),
        explicitEmpty.Components.Select(ReviewedConceptTestData.CandidateProjection));
    Assert.Equal(
        baseline.Projects.Select(ReviewedConceptTestData.ProjectProjection),
        explicitEmpty.Projects.Select(ReviewedConceptTestData.ProjectProjection));
    Assert.Equal(baseline.Relations, explicitEmpty.Relations);
}

[Fact]
public void Retrieve_ConceptCannotIntroduceZeroLexicalCandidate()
{
    var snapshot = AddComponent(ProjectMemoryTestFactory.Create(),
        "entity:concept-only", "OpaqueWorker", "Demo.Core.OpaqueWorker", "project:core", []);
    var profile = ReviewedConceptTestData.Profile(
        "entity:concept-only", ReviewedConceptTestData.Concept("remote-authorization"));

    var result = Retrieve("remote authorization", snapshot, [profile]);

    Assert.DoesNotContain(result.Components, item => item.EntityId == "entity:concept-only");
}
```

Add tests proving:

- A positive lexical candidate below the direct cutoff can be promoted into Top-K.
- A graph-expanded candidate receives only the existing `graph Implements`/`graph Inherits` score and no reviewed-concept reason.
- Project candidates are exactly equal with and without profiles, including scores and match reasons.
- Existing graph expansion and limits remain unchanged with empty profiles.
- E2 holds through the integrated retrieval path.
- `LocalInitiativeAnalysisStore`-shaped candidate with `initiative-analysis-persistence` does not receive concept points for query `initiative analysis`.
- A known-good `initiative persistence` query does receive the bounded concept reason.

- [ ] **Step 2: Run the retriever tests and confirm signature/behavior failures**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~InitiativeCandidateRetrieverTests
```

Expected: FAIL because the four-argument overload does not exist and reranking assertions are unmet.

- [ ] **Step 3: Refactor selection without changing lexical scoring**

Add an optional constructor dependency and overload:

```csharp
public InitiativeCandidateRetriever(
    InitiativeTermNormalizer? normalizer = null,
    CandidateRetrievalOptions? options = null,
    RetrievalScoringOptions? scoring = null,
    ConceptCandidateReranker? conceptReranker = null);

public CandidateRetrievalResult Retrieve(
    InitiativeUnderstanding understanding,
    ProjectMemoryManifest manifest,
    RepositorySnapshot snapshot,
    IReadOnlyList<ComponentConceptProfile> conceptProfiles);
```

Keep the existing three-argument method and delegate it to the new method with `[]`.

Inside retrieval:

1. Build and score the complete component universe exactly as today.
2. Retain only candidates having at least one positive lexical match reason; negative test penalties do not erase eligibility.
3. Build `lexicalDirectForProjects` by applying the existing lexical sort and direct limit, then run existing graph expansion on a copy.
4. Pass the full positive lexical pool to `ConceptCandidateReranker` and apply the direct limit to its result.
5. Run graph expansion only after reranked direct Top-K selection.
6. Compute project component propagation from `lexicalDirectForProjects`, never from concept-adjusted scores or selection.
7. Build component relations from the reranked-plus-expanded selected IDs as today.

Do not modify `ScoreComponent`, lexical weights, metadata fields, query construction, graph depth, or public result records.

- [ ] **Step 4: Run focused retrieval and context tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~InitiativeCandidateRetrieverTests|FullyQualifiedName~InitiativeContextBuilderTests"
```

Expected: all selected tests PASS; empty-profile results match existing retrieval exactly.

- [ ] **Step 5: Commit retrieval integration**

```powershell
git add src/EngineeringBrain.Infrastructure/InitiativeCandidateRetriever.cs tests/EngineeringBrain.Core.Tests/InitiativeCandidateRetrieverTests.cs
git commit -m "feat: integrate concepts into direct candidate reranking"
```

---

### Task 8: Resolve Once for Preview and Actual Initiative Analysis

**Files:**
- Modify: `src/EngineeringBrain.Cli/Program.cs:88-140`
- Modify: `src/EngineeringBrain.Infrastructure/InitiativeAnalysisService.cs:39-76`
- Modify: `src/EngineeringBrain.Infrastructure/RemoteContextPreviewService.cs:55-103`
- Modify: `tests/EngineeringBrain.Core.Tests/InitiativeAnalysisServiceTests.cs:9-193`
- Modify: `tests/EngineeringBrain.Core.Tests/RemoteContextPreviewTests.cs:10-41`

**Interfaces:**
- Consumes: dedicated `ReviewedConceptResolutionResult`; no service loads the artifact.
- Produces: overloads that explicitly accept one pre-resolved runtime result.

- [ ] **Step 1: Write failing downstream reuse tests**

Add service tests that create one in-memory `ReviewedConceptResolutionResult`, do not create any semantic artifact on disk, pass that same result to preview and analysis, and assert both retrieval outputs contain the same concept match value and selected EntityId.

Use overload shapes:

```csharp
await preview.CreateAsync(
    "initiative.md", initiative, memory, concepts,
    "model-a", "model-b", "low", "medium");

await analysis.AnalyzeAsync(request, concepts);
```

Also call existing overloads without a resolution and assert they remain lexical-only. This proves downstream services cannot reload or mutate the catalog independently.

- [ ] **Step 2: Run service tests and confirm overload failures**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~InitiativeAnalysisServiceTests|FullyQualifiedName~RemoteContextPreviewTests"
```

Expected: FAIL because the resolution-aware overloads do not exist.

- [ ] **Step 3: Add explicit overloads and CLI composition**

Keep every existing public overload. Add:

```csharp
Task<InitiativeAnalysisResult> AnalyzeAsync(
    InitiativeAnalysisRequest request,
    ReviewedConceptResolutionResult reviewedConcepts,
    CancellationToken cancellationToken = default);

Task<RemoteContextPreview> CreateAsync(
    string initiativeFileName,
    string initiative,
    ProjectMemorySyncResult memory,
    ReviewedConceptResolutionResult reviewedConcepts,
    string interpretationModel,
    string reasoningModel,
    string interpretationEffort,
    string analysisEffort,
    CancellationToken cancellationToken = default);
```

Existing overloads delegate with `ReviewedConceptResolutionResult.Absent`. Resolution-aware overloads pass only `reviewedConcepts.Profiles` to the retriever.

In `Program.RunAnalyzeAsync`, immediately after memory sync:

```csharp
var reviewedConcepts = await ResolveReviewedConceptsAsync(memory, cancellation.Token);
WriteReviewedConceptDiagnostics(reviewedConcepts);
```

Implement one private composition helper in `Program`:

```csharp
private static async Task<ReviewedConceptResolutionResult> ResolveReviewedConceptsAsync(
    ProjectMemorySyncResult memory,
    CancellationToken cancellationToken)
{
    var load = await new LocalReviewedConceptStore().LoadAsync(memory.Location, cancellationToken);
    if (load.Status == ReviewedConceptLoadStatus.Absent)
        return ReviewedConceptResolutionResult.Absent;
    if (load.Status == ReviewedConceptLoadStatus.Invalid)
        return new ReviewedConceptResolutionResult(
            ReviewedConceptResolutionStatus.Invalid, load.ContentHash, [], load.Diagnostics);

    var evidence = ReviewedConceptEvidenceContext.FromMemory(memory);
    var validation = new ReviewedConceptValidator().Validate(load.Catalog!, evidence);
    return new ReviewedConceptResolver().Resolve(load, validation, evidence);
}
```

Pass the same object to preview and actual analysis. Print only safe diagnostic code, severity, ConceptId/EntityId when present, and bounded message; do not print catalog contents or absolute paths.

- [ ] **Step 4: Run service tests and CLI build**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~InitiativeAnalysisServiceTests|FullyQualifiedName~RemoteContextPreviewTests"
dotnet build src/EngineeringBrain.Cli/EngineeringBrain.Cli.csproj --no-restore
```

Expected: selected tests PASS; CLI builds with zero warnings/errors; no provider call is made by tests.

- [ ] **Step 5: Commit orchestration reuse**

```powershell
git add src/EngineeringBrain.Cli/Program.cs src/EngineeringBrain.Infrastructure/InitiativeAnalysisService.cs src/EngineeringBrain.Infrastructure/RemoteContextPreviewService.cs tests/EngineeringBrain.Core.Tests/InitiativeAnalysisServiceTests.cs tests/EngineeringBrain.Core.Tests/RemoteContextPreviewTests.cs
git commit -m "feat: reuse reviewed concept resolution across analysis"
```

---

### Task 9: Offline and Live Evaluation Plumbing

**Files:**
- Modify: `src/EngineeringBrain.Cli/Program.cs:244-304,319-374`
- Modify: `src/EngineeringBrain.Infrastructure/EvaluationServices.cs:191-233`
- Modify: `src/EngineeringBrain.Infrastructure/LiveEvaluationService.cs:7-79,129-191`
- Modify: `tests/EngineeringBrain.Core.Tests/EvaluationHarnessTests.cs:9-96`
- Modify: `tests/EngineeringBrain.Core.Tests/LiveEvaluationTests.cs` around existing retrieval-comparison tests

**Interfaces:**
- Consumes: one pre-resolved result per offline run or live run.
- Produces: deterministic evaluation of the same candidate profiles used in production retrieval.

- [ ] **Step 1: Write failing evaluation-plumbing tests**

Add tests proving:

- `EvaluationHarness.EvaluateAsync(..., reviewedConcepts, cancellationToken)` applies a known-good promotion.
- Existing `EvaluateAsync(..., cancellationToken)` remains lexical-only.
- `LiveEvaluationService.RunAsync` passes the same profiles to both golden and live retrieval inside every case/run.
- Fake-provider live evaluation requires no artifact reload and no network.
- Invalid/diagnostic resolution does not fail either harness when `Profiles` is empty.

For the live test, use existing `FakeLiveReasoningProvider` and assert the golden/live retrieval candidates carry an identical `reviewed concept` `MatchedValue` containing the declaration fingerprint prefix.

- [ ] **Step 2: Run focused evaluation tests and confirm missing overloads**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~EvaluationHarnessTests|FullyQualifiedName~LiveEvaluationTests"
```

Expected: FAIL because evaluation methods do not accept `ReviewedConceptResolutionResult`.

- [ ] **Step 3: Add explicit evaluation overloads and composition**

Keep existing overloads delegating with `Absent`. Add `ReviewedConceptResolutionResult reviewedConcepts` immediately after the `ProjectMemorySyncResult memory` parameter in the new overloads.

Offline CLI flow:

```csharp
var memory = await new ProjectMemoryService().SyncAsync(scan.Snapshot, cancellation.Token);
var reviewedConcepts = await ResolveReviewedConceptsAsync(memory, cancellation.Token);
WriteReviewedConceptDiagnostics(reviewedConcepts);
var cases = await harness.EvaluateAsync(suite, suitePath, memory, reviewedConcepts, cancellation.Token);
```

Live CLI flow resolves once after memory sync and after the plan-only early return, then passes the result to `LiveEvaluationService.RunAsync`. `LiveEvaluationService` must pass the same profile array to both line-186 golden retrieval and line-187 live retrieval.

Change `EvaluationHarness.RetrievalVersion` from `lexical-graph-v2` to `lexical-graph-concept-v1`. Do not update `evaluations/baseline.json` in this task; normal evaluation will compare behavior explicitly in Task 11.

- [ ] **Step 4: Run focused evaluation tests and fake-provider smoke test**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~EvaluationHarnessTests|FullyQualifiedName~LiveEvaluationTests"
dotnet run --project src/EngineeringBrain.Cli --no-build -- eval-live . --preview
```

Expected: tests PASS; preview exits 0 and reports its plan without authorizing or calling a provider.

- [ ] **Step 5: Commit evaluation plumbing**

```powershell
git add src/EngineeringBrain.Cli/Program.cs src/EngineeringBrain.Infrastructure/EvaluationServices.cs src/EngineeringBrain.Infrastructure/LiveEvaluationService.cs tests/EngineeringBrain.Core.Tests/EvaluationHarnessTests.cs tests/EngineeringBrain.Core.Tests/LiveEvaluationTests.cs
git commit -m "feat: evaluate reviewed concept reranking consistently"
```

---

### Task 10: Migrate the Frozen Reviewed Vocabulary V2 Catalog

**Files:**
- Create: `tests/EngineeringBrain.Core.Tests/TestData/reviewed-concepts-v2.json`
- Create: `tests/EngineeringBrain.Core.Tests/ReviewedConceptVocabularyV2Tests.cs`
- Modify: `tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj` only if JSON copy metadata is required by the existing test runner
- External local artifact: `<memory.Location>/semantic/reviewed-concepts.json` (never add this generated branch artifact to Git)

**Interfaces:**
- Consumes: frozen V2 definitions/assignments, A1 metadata, current self-scan entities, and current component-note fingerprints.
- Produces: one schema-1 catalog with 25 declarations, 29 assigned components, and 43 assignments.

- [ ] **Step 1: Verify immutable migration inputs before creating the catalog**

Run:

```powershell
Get-FileHash -Algorithm SHA256 'C:\Users\Usuario\.codex\visualizations\2026\09\10\01a08d6a-944d-73e0-9b2a-b64f42cec4f6\reviewed-concept-vocabulary-v2-research-results.json'
Get-FileHash -Algorithm SHA256 'C:\Users\Usuario\.codex\visualizations\2026\09\10\01a08d6a-944d-73e0-9b2a-b64f42cec4f6\anchor-aware-concept-qualification-research\a1-classification.json'
```

Expected hashes:

```text
C10AC0049558EDC8F30BFE267E60C505CDFB9165FE5B3941B572D6CCFD6D63C3
30E835703D376320949FE65212E1CF6906E52AD37AD8C2FF1E44DDD60FEA310F
```

If either differs, stop this task and report the input mismatch; do not reinterpret the metadata.

- [ ] **Step 2: Write the failing migration-contract test**

The test must load `TestData/reviewed-concepts-v2.json` and assert:

```csharp
[Fact]
public void FrozenVocabularyV2Catalog_PreservesReviewedCountsAndPersistenceRoles()
{
    var catalog = ReviewedConceptSerializer.Deserialize(
        File.ReadAllText(TestDataPath("reviewed-concepts-v2.json")));

    Assert.Equal(25, catalog.Declarations.Count);
    Assert.Equal(43, catalog.Declarations.Sum(item => item.Assignments.Count));
    Assert.Equal(29, catalog.Declarations.SelectMany(item => item.Assignments)
        .Select(item => item.EntityId).Distinct(StringComparer.Ordinal).Count());

    var persistence = catalog.Declarations.Single(item =>
        item.ConceptId == "initiative-analysis-persistence");
    Assert.Equal(["initiative"], Assert.Single(persistence.AnchorTokens));
    Assert.Equal(["persistence"], persistence.QualificationSupportTokens);
    Assert.Equal(["analysis"], persistence.ContextSupportTokens);
}
```

Also validate and resolve the complete fixture against an evidence context built from its assignments; assert zero catalog/declaration errors, zero stale assignments, deterministic fingerprints, and stable serialization.

- [ ] **Step 3: Run the test and confirm the missing fixture failure**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter FullyQualifiedName~ReviewedConceptVocabularyV2Tests
```

Expected: FAIL because `TestData/reviewed-concepts-v2.json` does not exist.

- [ ] **Step 4: Materialize the reviewed schema-1 catalog without inventing metadata**

Use these exact mappings:

- Definitions: `vocabularyV2.definitions` from the frozen V2 artifact.
- Assignments: map each `vocabularyV2.assignments` FullName to the unique current snapshot EntityId.
- Anchor policy/groups and A1 support: `concepts` from the frozen A1 classification.
- Default migration: A1 `supportTokens` become `QualificationSupportTokens`; `ContextSupportTokens` is empty.
- Approved correction only: for `initiative-analysis-persistence`, qualification support is `persistence` and context support is `analysis`.
- Assignment `SourceReference`: `<relative entity path>:<start line>`.
- Assignment `SourceFingerprint`: current component-note fingerprint for that EntityId.
- Provenance source references and hashes: the two immutable source paths/hashes from Step 1.
- Reviewer: `engineering-brain-reviewed-research`; review version `1`; timestamp equal to the frozen V2 `frozenAtUtc` converted to UTC.
- Declaration fingerprints: only `ReviewedConceptSerializer.CreateDeclarationFingerprint` output.

First run:

```powershell
dotnet run --project src/EngineeringBrain.Cli --no-build -- scan .
dotnet run --project src/EngineeringBrain.Cli --no-build -- memory sync .
```

Build the test fixture using the current snapshot and manifest, serialize it through `ReviewedConceptSerializer`, then copy the same bytes to `<memory.Location>/semantic/reviewed-concepts.json`. This is an explicit one-time local migration, not application behavior. Do not add a production import API or retain a migration script in the repository.

- [ ] **Step 5: Run migration, validator, resolver, and false-promotion tests**

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~ReviewedConceptVocabularyV2Tests|FullyQualifiedName~ReviewedConceptValidatorTests|FullyQualifiedName~ReviewedConceptResolverTests|FullyQualifiedName~Rerank_ClearAnchorRequiresQualificationSupport"
```

Expected: all selected tests PASS; counts are 25/29/43; `initiative + analysis` does not qualify persistence.

- [ ] **Step 6: Commit only the portable migration fixture and its contract test**

```powershell
git add tests/EngineeringBrain.Core.Tests/TestData/reviewed-concepts-v2.json tests/EngineeringBrain.Core.Tests/ReviewedConceptVocabularyV2Tests.cs tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj
git commit -m "test: preserve reviewed vocabulary v2 migration"
```

Do not stage or commit the external branch-scoped artifact.

---

### Task 11: Complete Regression and Offline Evaluation Verification

**Files:**
- No source-file changes expected.
- External outputs remain under the current user profile's `.engineering-brain` store.

**Interfaces:**
- Consumes: completed implementation and migrated local catalog.
- Produces: verification evidence only.

- [ ] **Step 1: Prove lexical-only backward compatibility in isolation**

Temporarily rename the external `reviewed-concepts.json` outside the repository, run the focused equivalence test, then restore the exact same file and hash.

```powershell
dotnet test tests/EngineeringBrain.Core.Tests/EngineeringBrain.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~Retrieve_NoProfilesIsEquivalentToLexicalGraphV2|FullyQualifiedName~ProjectRanking|FullyQualifiedName~GraphExpansion"
```

Expected: PASS; lexical scores, ordering, selected entities, reasons, project ranking, and graph expansion are unchanged.

- [ ] **Step 2: Run build and full tests**

```powershell
dotnet build EngineeringBrain.sln --no-restore
dotnet test EngineeringBrain.sln --no-restore --no-build
```

Expected: build has 0 warnings and 0 errors; all tests pass with 0 skipped.

- [ ] **Step 3: Run the standard offline evaluation with the migrated catalog**

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
Budget violations: 0
Fabricated entity accepted: 0
```

Do not use `--update-baseline`. If the retrieval-version identifier alone is reported, preserve the existing baseline until explicit human approval.

- [ ] **Step 4: Run offline-only preview and ownership checks**

```powershell
dotnet run --project src/EngineeringBrain.Cli --no-build -- analyze evaluations/cases/python-analyzer/initiative.md --repo . --preview
dotnet run --project src/EngineeringBrain.Cli --no-build -- eval-live . --preview
```

Expected: both commands exit without provider authorization or network access; the semantic artifact remains byte-for-byte unchanged.

- [ ] **Step 5: Review repository state**

```powershell
git diff --check
git status --short
git log -12 --oneline
```

Expected: no whitespace errors, no uncommitted source changes, no generated memory/snapshot artifacts inside the repository, and one local commit per completed implementation task. Do not create an additional verification-only commit.

---

### Task 12: ADR and README

**Files:**
- Create: `docs/decisions/0009-reviewed-concept-reranking.md`
- Modify: `README.md` retrieval and local-storage sections

**Interfaces:**
- Consumes: verified implementation behavior and final type names.
- Produces: concise ownership, artifact, failure-granularity, scoring, and compatibility documentation.

- [ ] **Step 1: Write the failing documentation contract check**

Run before writing:

```powershell
rg -n "reviewed-concepts.json|ValidWithDiagnostics|QualificationSupportTokens|lexical-graph-concept-v1" README.md docs/decisions
```

Expected: no complete documentation of the new semantic layer.

- [ ] **Step 2: Write ADR 0009**

Document these decisions explicitly:

- Code Graph remains objective evidence.
- Reviewed concepts are human-reviewed semantic knowledge in a separate branch/repository artifact.
- Project Memory supplies immutable fingerprints but owns none of the reviewed lifecycle.
- Catalog, declaration, and assignment failures have different granularity.
- Concepts rerank only positive lexical direct candidates.
- A1/C1/P1/S2/E2 semantics and the qualification/context support distinction.
- No project bonus, graph-expanded score, candidate generation, embeddings, or remote behavior.
- Absence and invalid-catalog lexical fallback.
- Retrieval-version change and evaluation evidence required before baseline acceptance.

- [ ] **Step 3: Update README with operator-visible behavior**

Add the exact local artifact path, state that it is read-only and never generated by memory sync, explain safe diagnostics and lexical fallback, and state that reviewed concept prose is not sent to CALL #2. Keep the section concise and link ADR 0009 from the existing ADR list.

- [ ] **Step 4: Verify docs and full suite**

```powershell
rg -n "reviewed-concepts.json|ValidWithDiagnostics|QualificationSupportTokens|lexical-graph-concept-v1" README.md docs/decisions/0009-reviewed-concept-reranking.md
dotnet build EngineeringBrain.sln --no-restore
dotnet test EngineeringBrain.sln --no-restore --no-build
dotnet run --project src/EngineeringBrain.Cli --no-build -- eval .
git diff --check
```

Expected: required terms are documented; build has 0 warnings/errors; all tests and 16/16 offline cases pass; Recall@5/10 remains 1.000; no whitespace errors.

- [ ] **Step 5: Commit documentation**

```powershell
git add README.md docs/decisions/0009-reviewed-concept-reranking.md
git commit -m "docs: record reviewed concept reranking architecture"
```

## Plan Self-Review Record

- Spec coverage: all ownership, artifact, localized failure, scoring, orchestration, compatibility, migration, test, and documentation requirements map to Tasks 1-12.
- Type consistency: the same `ReviewedConceptResolutionResult`, `ComponentConceptProfile`, `ReviewedConceptEvidenceContext`, and resolution-aware overloads are used throughout.
- Failure granularity: Task 4 localizes declaration and structural-assignment validation; Task 5 localizes missing/stale evidence assignments; only loader/envelope failures disable the full catalog.
- Ownership: no task modifies `ProjectMemoryModels.cs`, `ProjectMemoryService.cs`, or `LocalProjectMemoryStore.cs`; Task 3 adds only a regression test proving the existing store leaves `semantic/` untouched.
- Backward compatibility: Tasks 6, 7, 8, 9, and 11 explicitly test absent/empty-profile equivalence, existing overloads, project ranking, graph expansion, scores, ordering, entities, and match reasons.
- Security: no reviewed declaration prose enters Project Memory notes or CALL #2; diagnostics are bounded and source content is never logged.
- Scope: no authoring UI/CLI, schema changes outside reviewed-concept schema 1, policies, prompts, providers, aliases, stemming, embeddings, dependencies, or weight changes are included.
