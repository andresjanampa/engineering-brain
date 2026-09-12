using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class ReviewedConceptResolver
{
    public ReviewedConceptResolutionResult Resolve(
        ReviewedConceptLoadResult load,
        ReviewedConceptValidationResult validation,
        ReviewedConceptEvidenceContext evidence)
    {
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(validation);
        ArgumentNullException.ThrowIfNull(evidence);

        if (load.Status == ReviewedConceptLoadStatus.Absent)
        {
            return ReviewedConceptResolutionResult.Absent;
        }

        var diagnostics = load.Diagnostics.Concat(validation.Diagnostics).ToList();
        if (load.Status == ReviewedConceptLoadStatus.Invalid
            || load.Catalog is null
            || !validation.CatalogIsValid)
        {
            return new ReviewedConceptResolutionResult(
                ReviewedConceptResolutionStatus.Invalid,
                load.ContentHash,
                [],
                OrderDiagnostics(diagnostics));
        }

        var resolved = new List<(ComponentFingerprintEvidence Evidence, ResolvedReviewedConcept Concept)>();
        foreach (var declaration in validation.Declarations.OrderBy(item => item.ConceptId, StringComparer.Ordinal))
        {
            foreach (var assignment in declaration.Assignments.OrderBy(item => item.EntityId, StringComparer.Ordinal))
            {
                if (!evidence.Components.TryGetValue(assignment.EntityId, out var component))
                {
                    diagnostics.Add(AssignmentDiagnostic(
                        "RC400",
                        "Reviewed concept assignment entity is absent from current evidence.",
                        declaration.ConceptId,
                        assignment.EntityId));
                    continue;
                }

                var pathReference = component.RelativePath;
                var lineReference = $"{component.RelativePath}:{component.StartLine}";
                if (!assignment.SourceReference.Equals(pathReference, StringComparison.Ordinal)
                    && !assignment.SourceReference.Equals(lineReference, StringComparison.Ordinal))
                {
                    diagnostics.Add(AssignmentDiagnostic(
                        "RC401",
                        "Reviewed concept assignment source reference does not match current evidence.",
                        declaration.ConceptId,
                        assignment.EntityId));
                    continue;
                }

                if (!assignment.SourceFingerprint.Equals(component.SourceFingerprint, StringComparison.Ordinal))
                {
                    diagnostics.Add(AssignmentDiagnostic(
                        "RC402",
                        "Reviewed concept assignment source fingerprint is stale.",
                        declaration.ConceptId,
                        assignment.EntityId));
                    continue;
                }

                resolved.Add((component, new ResolvedReviewedConcept(
                    declaration.ConceptId,
                    declaration.AnchorPolicy,
                    declaration.AnchorTokens,
                    declaration.QualificationSupportTokens,
                    declaration.ContextSupportTokens,
                    declaration.Fingerprint)));
            }
        }

        var profiles = resolved
            .GroupBy(item => item.Evidence.EntityId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ComponentConceptProfile(
                group.Key,
                group.First().Evidence.SourceFingerprint,
                group.Select(item => item.Concept)
                    .OrderBy(item => item.ConceptId, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();

        return new ReviewedConceptResolutionResult(
            diagnostics.Count == 0
                ? ReviewedConceptResolutionStatus.Valid
                : ReviewedConceptResolutionStatus.ValidWithDiagnostics,
            load.ContentHash,
            profiles,
            OrderDiagnostics(diagnostics));
    }

    private static ReviewedConceptDiagnostic AssignmentDiagnostic(
        string code,
        string message,
        string conceptId,
        string entityId) => new(
        code,
        AnalysisDiagnosticSeverity.Warning,
        ReviewedConceptDiagnosticScope.Assignment,
        message,
        conceptId,
        entityId);

    private static IReadOnlyList<ReviewedConceptDiagnostic> OrderDiagnostics(
        IEnumerable<ReviewedConceptDiagnostic> diagnostics) => diagnostics
        .OrderBy(item => item.Scope)
        .ThenBy(item => item.ConceptId, StringComparer.Ordinal)
        .ThenBy(item => item.EntityId, StringComparer.Ordinal)
        .ThenBy(item => item.Code, StringComparer.Ordinal)
        .ToArray();
}
