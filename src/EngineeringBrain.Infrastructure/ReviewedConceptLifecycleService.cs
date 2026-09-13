using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class ReviewedConceptLifecycleService
{
    private readonly LocalReviewedConceptStore _reader;
    private readonly LocalReviewedConceptWriter _writer;
    private readonly ReviewedConceptValidator _validator;
    private readonly ReviewedConceptResolver _resolver;
    private readonly IGitInfoProvider _gitInfo;

    public ReviewedConceptLifecycleService(
        LocalReviewedConceptStore? reader = null,
        LocalReviewedConceptWriter? writer = null,
        ReviewedConceptValidator? validator = null,
        ReviewedConceptResolver? resolver = null,
        IGitInfoProvider? gitInfo = null)
    {
        _reader = reader ?? new LocalReviewedConceptStore();
        _writer = writer ?? new LocalReviewedConceptWriter(_reader);
        _validator = validator ?? new ReviewedConceptValidator();
        _resolver = resolver ?? new ReviewedConceptResolver();
        _gitInfo = gitInfo ?? new GitInfoProvider();
    }

    public Task<ReviewedConceptLifecycleStatusResult> GetStatusAsync(
        string repositoryName,
        string branchKnowledgeLocation,
        ReviewedConceptEvidenceContext evidence,
        CancellationToken cancellationToken = default) =>
        InspectAsync(repositoryName, branchKnowledgeLocation, evidence, cancellationToken);

    public Task<ReviewedConceptLifecycleStatusResult> ValidateAsync(
        string repositoryName,
        string branchKnowledgeLocation,
        ReviewedConceptEvidenceContext evidence,
        CancellationToken cancellationToken = default) =>
        InspectAsync(repositoryName, branchKnowledgeLocation, evidence, cancellationToken);

    public async Task<ReviewedConceptPromotionResult> PromoteAsync(
        string repositoryName,
        string repositoryRoot,
        string sourceBranch,
        string targetBranch,
        string sourceBranchKnowledgeLocation,
        string targetBranchKnowledgeLocation,
        ReviewedConceptEvidenceContext targetEvidence,
        GitInfo analyzedGit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(targetEvidence);
        ArgumentNullException.ThrowIfNull(analyzedGit);

        var targetCatalogPath = _reader.GetPath(targetBranchKnowledgeLocation);
        if (string.IsNullOrWhiteSpace(sourceBranch)
            || string.IsNullOrWhiteSpace(targetBranch)
            || string.Equals(sourceBranch, targetBranch, StringComparison.Ordinal))
        {
            return BlockedResult(
                repositoryName, targetEvidence, sourceBranch, targetBranch,
                string.Empty, targetCatalogPath, null, null,
                [Diagnostic("RCL100", ReviewedConceptDiagnosticScope.Catalog,
                    "Source and target branches must be distinct non-empty branch names.")]);
        }

        var sourceBranchKey = KnowledgeIdentity.CreateBranchKey(sourceBranch);
        if (!analyzedGit.IsRepository
            || string.IsNullOrWhiteSpace(analyzedGit.Branch)
            || !string.Equals(analyzedGit.Branch, targetBranch, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(analyzedGit.HeadCommit)
            || analyzedGit.IsWorkingTreeClean != true
            || !string.Equals(targetEvidence.Branch, targetBranch, StringComparison.Ordinal)
            || !string.Equals(targetEvidence.BranchKey,
                KnowledgeIdentity.CreateBranchKey(targetBranch), StringComparison.Ordinal))
        {
            return BlockedResult(
                repositoryName, targetEvidence, sourceBranch, targetBranch,
                _reader.GetPath(sourceBranchKnowledgeLocation), targetCatalogPath,
                null, null,
                [Diagnostic("RCL200", ReviewedConceptDiagnosticScope.Catalog,
                    "Target must be the current clean non-detached repository branch.")]);
        }

        var sourceLoad = await _reader.LoadAsync(sourceBranchKnowledgeLocation, cancellationToken);
        if (sourceLoad.Status == ReviewedConceptLoadStatus.Absent)
        {
            return BlockedResult(
                repositoryName, targetEvidence, sourceBranch, targetBranch,
                sourceLoad.Path, targetCatalogPath, null, null,
                [Diagnostic("RCL101", ReviewedConceptDiagnosticScope.Catalog,
                    "Source reviewed concept catalog is absent.")]);
        }

        if (sourceLoad.Status != ReviewedConceptLoadStatus.Loaded || sourceLoad.Catalog is null)
        {
            return BlockedResult(
                repositoryName, targetEvidence, sourceBranch, targetBranch,
                sourceLoad.Path, targetCatalogPath, sourceLoad.ContentHash, null,
                sourceLoad.Diagnostics.Append(Diagnostic(
                    "RCL102", ReviewedConceptDiagnosticScope.Catalog,
                    "Source reviewed concept catalog is invalid.")));
        }

        var previousTarget = await _reader.LoadAsync(targetBranchKnowledgeLocation, cancellationToken);
        if (previousTarget.Status == ReviewedConceptLoadStatus.Invalid)
        {
            return BlockedResult(
                repositoryName, targetEvidence, sourceBranch, targetBranch,
                sourceLoad.Path, previousTarget.Path, sourceLoad.ContentHash, previousTarget.ContentHash,
                previousTarget.Diagnostics.Append(Diagnostic(
                    "RCL400", ReviewedConceptDiagnosticScope.Catalog,
                    "Existing target reviewed concept catalog is invalid.")),
                sourceLoad.Catalog);
        }

        if (previousTarget.Status == ReviewedConceptLoadStatus.Loaded)
        {
            var existingValidation = _validator.Validate(previousTarget.Catalog!, targetEvidence);
            var existingResolution = _resolver.Resolve(previousTarget, existingValidation, targetEvidence);
            var canonicalTarget = ReviewedConceptSerializer.CreateCatalogFingerprint(previousTarget.Catalog!);
            if (existingResolution.Status is not ReviewedConceptResolutionStatus.Valid
                || !string.Equals(previousTarget.ContentHash, canonicalTarget, StringComparison.Ordinal))
            {
                return BlockedResult(
                    repositoryName, targetEvidence, sourceBranch, targetBranch,
                    sourceLoad.Path, previousTarget.Path, sourceLoad.ContentHash, previousTarget.ContentHash,
                    existingResolution.Diagnostics.Append(Diagnostic(
                        "RCL400", ReviewedConceptDiagnosticScope.Catalog,
                        "Existing target reviewed concept catalog is not fully valid.")),
                    sourceLoad.Catalog);
            }
        }

        var sourceIdentity = new ReviewedConceptCatalogIdentity(
            targetEvidence.RepositoryId,
            sourceBranch,
            sourceBranchKey);
        var sourceValidation = _validator.ValidateIntegrity(sourceLoad.Catalog, sourceIdentity);
        var canonicalSourceFingerprint = ReviewedConceptSerializer.CreateCatalogFingerprint(sourceLoad.Catalog);
        if (!string.Equals(sourceLoad.ContentHash, canonicalSourceFingerprint, StringComparison.Ordinal))
        {
            return BlockedResult(
                repositoryName, targetEvidence, sourceBranch, targetBranch,
                sourceLoad.Path, previousTarget.Path, sourceLoad.ContentHash, previousTarget.ContentHash,
                [Diagnostic("RCL103", ReviewedConceptDiagnosticScope.Catalog,
                    "Source reviewed concept catalog serialization is not canonical.")],
                sourceLoad.Catalog);
        }

        if (!sourceValidation.CatalogIsValid || sourceValidation.Diagnostics.Count > 0)
        {
            return BlockedResult(
                repositoryName, targetEvidence, sourceBranch, targetBranch,
                sourceLoad.Path, previousTarget.Path, sourceLoad.ContentHash, previousTarget.ContentHash,
                sourceValidation.Diagnostics.Append(Diagnostic(
                    "RCL102", ReviewedConceptDiagnosticScope.Catalog,
                    "Source reviewed concept catalog failed integrity validation.")),
                sourceLoad.Catalog);
        }

        var reboundSourceReferences = 0;
        var rebuiltDeclarations = new List<ReviewedConceptDeclaration>();
        var diagnostics = new List<ReviewedConceptDiagnostic>();
        foreach (var declaration in sourceLoad.Catalog.Declarations)
        {
            var assignments = new List<ReviewedConceptAssignment>();
            foreach (var sourceAssignment in declaration.Assignments)
            {
                if (!targetEvidence.Components.TryGetValue(sourceAssignment.EntityId, out var target))
                {
                    diagnostics.Add(Diagnostic(
                        "RCL300", ReviewedConceptDiagnosticScope.Assignment,
                        "Reviewed assignment entity is absent from target evidence.",
                        declaration.ConceptId, sourceAssignment.EntityId));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(target.RelativePath)
                    || target.StartLine <= 0
                    || string.IsNullOrWhiteSpace(target.SourceFingerprint))
                {
                    diagnostics.Add(Diagnostic(
                        "RCL301", ReviewedConceptDiagnosticScope.Assignment,
                        "Target component evidence is incomplete.",
                        declaration.ConceptId, sourceAssignment.EntityId));
                    continue;
                }

                var sourceReference = $"{target.RelativePath}:{target.StartLine}";
                if (!string.Equals(sourceReference, sourceAssignment.SourceReference, StringComparison.Ordinal))
                {
                    reboundSourceReferences++;
                }

                assignments.Add(new ReviewedConceptAssignment(
                    sourceAssignment.EntityId,
                    sourceReference,
                    target.SourceFingerprint));
            }

            var draft = declaration with
            {
                Assignments = assignments.ToArray(),
                Fingerprint = string.Empty
            };
            rebuiltDeclarations.Add(draft with
            {
                Fingerprint = ReviewedConceptSerializer.CreateDeclarationFingerprint(draft)
            });
        }

        if (diagnostics.Count > 0)
        {
            return BlockedResult(
                repositoryName, targetEvidence, sourceBranch, targetBranch,
                sourceLoad.Path, previousTarget.Path, sourceLoad.ContentHash, previousTarget.ContentHash,
                diagnostics,
                sourceLoad.Catalog);
        }

        var candidate = new ReviewedConceptCatalog(
            ReviewedConceptSerializer.CurrentSchemaVersion,
            targetEvidence.RepositoryId,
            targetBranch,
            targetEvidence.BranchKey,
            targetEvidence.SourceSnapshotSchema,
            targetEvidence.SourceAnalyzerVersion,
            sourceLoad.Catalog.VocabularyVersion,
            rebuiltDeclarations);
        var candidateFingerprint = ReviewedConceptSerializer.CreateCatalogFingerprint(candidate);
        var candidateLoad = new ReviewedConceptLoadResult(
            ReviewedConceptLoadStatus.Loaded,
            _reader.GetPath(targetBranchKnowledgeLocation),
            candidateFingerprint,
            candidate,
            []);
        var candidateValidation = _validator.Validate(candidate, targetEvidence);
        var candidateResolution = _resolver.Resolve(candidateLoad, candidateValidation, targetEvidence);
        var sourceAssignmentCount = sourceLoad.Catalog.Declarations.Sum(item => item.Assignments.Count);
        var candidateAssignmentCount = candidate.Declarations.Sum(item => item.Assignments.Count);
        if (candidateResolution.Status != ReviewedConceptResolutionStatus.Valid
            || candidateValidation.Declarations.Count != sourceLoad.Catalog.Declarations.Count
            || candidateAssignmentCount != sourceAssignmentCount)
        {
            return BlockedResult(
                repositoryName, targetEvidence, sourceBranch, targetBranch,
                sourceLoad.Path, previousTarget.Path, sourceLoad.ContentHash, previousTarget.ContentHash,
                candidateResolution.Diagnostics.Append(Diagnostic(
                    "RCL301", ReviewedConceptDiagnosticScope.Catalog,
                    "Promoted reviewed concept catalog failed target validation.")),
                sourceLoad.Catalog);
        }

        ReviewedConceptWriteResult write;
        try
        {
            write = await _writer.WriteAsync(
                targetBranchKnowledgeLocation,
                candidate,
                previousTarget.ContentHash,
                async token =>
                {
                    var currentGit = await _gitInfo.GetInfoAsync(repositoryRoot, token);
                    if (!string.Equals(currentGit.Branch, analyzedGit.Branch, StringComparison.Ordinal)
                        || !string.Equals(currentGit.HeadCommit, analyzedGit.HeadCommit, StringComparison.Ordinal)
                        || currentGit.IsWorkingTreeClean != true)
                    {
                        throw new ReviewedConceptTargetChangedException();
                    }
                },
                cancellationToken);
        }
        catch (ReviewedConceptWriteConflictException)
        {
            return BlockedResult(
                repositoryName, targetEvidence, sourceBranch, targetBranch,
                sourceLoad.Path, previousTarget.Path, sourceLoad.ContentHash, previousTarget.ContentHash,
                [Diagnostic("RCL401", ReviewedConceptDiagnosticScope.Catalog,
                    "Target reviewed concept catalog changed or is locked.")],
                sourceLoad.Catalog);
        }
        catch (ReviewedConceptTargetChangedException)
        {
            return BlockedResult(
                repositoryName, targetEvidence, sourceBranch, targetBranch,
                sourceLoad.Path, previousTarget.Path, sourceLoad.ContentHash, previousTarget.ContentHash,
                [Diagnostic("RCL201", ReviewedConceptDiagnosticScope.Catalog,
                    "Target repository state changed before promotion commit.")],
                sourceLoad.Catalog);
        }

        return new ReviewedConceptPromotionResult(
            write.Outcome == ReviewedConceptWriteOutcome.Unchanged
                ? ReviewedConceptPromotionOutcome.Unchanged
                : ReviewedConceptPromotionOutcome.Promoted,
            targetEvidence.RepositoryId,
            repositoryName,
            sourceBranch,
            sourceIdentity.BranchKey,
            targetBranch,
            targetEvidence.BranchKey,
            sourceLoad.Path,
            write.Path,
            sourceLoad.ContentHash,
            previousTarget.ContentHash,
            write.Fingerprint,
            candidate.Declarations.Count,
            candidateAssignmentCount,
            candidateResolution.Profiles.Count,
            candidateAssignmentCount,
            candidate.Declarations.Count,
            reboundSourceReferences,
            0,
            []);
    }

    private static ReviewedConceptPromotionResult BlockedResult(
        string repositoryName,
        ReviewedConceptEvidenceContext targetEvidence,
        string? sourceBranch,
        string? targetBranch,
        string sourceCatalogPath,
        string targetCatalogPath,
        string? sourceCatalogFingerprint,
        string? previousTargetCatalogFingerprint,
        IEnumerable<ReviewedConceptDiagnostic> diagnostics,
        ReviewedConceptCatalog? sourceCatalog = null)
    {
        var ordered = OrderDiagnostics(diagnostics);
        return new ReviewedConceptPromotionResult(
            ReviewedConceptPromotionOutcome.Blocked,
            targetEvidence.RepositoryId,
            repositoryName,
            sourceBranch ?? string.Empty,
            string.IsNullOrWhiteSpace(sourceBranch)
                ? string.Empty
                : KnowledgeIdentity.CreateBranchKey(sourceBranch),
            targetBranch ?? string.Empty,
            targetEvidence.BranchKey,
            sourceCatalogPath,
            targetCatalogPath,
            sourceCatalogFingerprint,
            previousTargetCatalogFingerprint,
            null,
            sourceCatalog?.Declarations.Count ?? 0,
            sourceCatalog?.Declarations.Sum(item => item.Assignments.Count) ?? 0,
            0,
            0,
            0,
            0,
            ordered.Count(item => item.Scope == ReviewedConceptDiagnosticScope.Assignment),
            ordered);
    }

    private static ReviewedConceptDiagnostic Diagnostic(
        string code,
        ReviewedConceptDiagnosticScope scope,
        string message,
        string? conceptId = null,
        string? entityId = null) => new(
        code,
        AnalysisDiagnosticSeverity.Error,
        scope,
        message,
        conceptId,
        entityId);

    private async Task<ReviewedConceptLifecycleStatusResult> InspectAsync(
        string repositoryName,
        string branchKnowledgeLocation,
        ReviewedConceptEvidenceContext evidence,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryName);
        ArgumentNullException.ThrowIfNull(evidence);

        var load = await _reader.LoadAsync(branchKnowledgeLocation, cancellationToken);
        if (load.Status == ReviewedConceptLoadStatus.Absent)
        {
            return CreateStatus(
                repositoryName,
                evidence,
                load.Path,
                ReviewedConceptResolutionResult.Absent,
                0,
                0);
        }

        if (load.Status == ReviewedConceptLoadStatus.Invalid)
        {
            var invalid = new ReviewedConceptResolutionResult(
                ReviewedConceptResolutionStatus.Invalid,
                load.ContentHash,
                [],
                OrderDiagnostics(load.Diagnostics));
            return CreateStatus(repositoryName, evidence, load.Path, invalid, null, null);
        }

        var validation = _validator.Validate(load.Catalog!, evidence);
        var resolution = _resolver.Resolve(load, validation, evidence);
        var canonicalFingerprint = ReviewedConceptSerializer.CreateCatalogFingerprint(load.Catalog!);
        if (!string.Equals(load.ContentHash, canonicalFingerprint, StringComparison.Ordinal))
        {
            var diagnostics = resolution.Diagnostics.Append(new ReviewedConceptDiagnostic(
                "RCL103",
                AnalysisDiagnosticSeverity.Warning,
                ReviewedConceptDiagnosticScope.Catalog,
                "Reviewed concept catalog serialization is not canonical."));
            resolution = resolution with
            {
                Status = resolution.Status == ReviewedConceptResolutionStatus.Valid
                    ? ReviewedConceptResolutionStatus.ValidWithDiagnostics
                    : resolution.Status,
                Diagnostics = OrderDiagnostics(diagnostics)
            };
        }

        return CreateStatus(
            repositoryName,
            evidence,
            load.Path,
            resolution,
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
        resolution.Diagnostics.Count(item => item.Scope == ReviewedConceptDiagnosticScope.Assignment),
        resolution.Diagnostics);

    private static IReadOnlyList<ReviewedConceptDiagnostic> OrderDiagnostics(
        IEnumerable<ReviewedConceptDiagnostic> diagnostics) => diagnostics
        .OrderBy(item => item.Scope)
        .ThenBy(item => item.ConceptId, StringComparer.Ordinal)
        .ThenBy(item => item.EntityId, StringComparer.Ordinal)
        .ThenBy(item => item.Code, StringComparer.Ordinal)
        .ToArray();
}

internal sealed class ReviewedConceptTargetChangedException : InvalidOperationException
{
    public ReviewedConceptTargetChangedException()
        : base("Target repository state changed before promotion commit.")
    {
    }
}
