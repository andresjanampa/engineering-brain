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
