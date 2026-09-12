using System.Text.RegularExpressions;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed partial class ReviewedConceptValidator
{
    private readonly InitiativeTermNormalizer _normalizer;

    public ReviewedConceptValidator(InitiativeTermNormalizer? normalizer = null)
    {
        _normalizer = normalizer ?? new InitiativeTermNormalizer();
    }

    public ReviewedConceptValidationResult Validate(
        ReviewedConceptCatalog catalog,
        ReviewedConceptEvidenceContext evidence)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(evidence);

        var diagnostics = ValidateEnvelope(catalog, evidence);
        if (diagnostics.Count > 0)
        {
            return new ReviewedConceptValidationResult(false, [], diagnostics);
        }

        var declarations = new List<ReviewedConceptDeclaration>();
        foreach (var declaration in catalog.Declarations.OrderBy(item => item.ConceptId, StringComparer.Ordinal))
        {
            var declarationDiagnostics = ValidateDeclaration(declaration);
            if (declarationDiagnostics.Count > 0)
            {
                diagnostics.AddRange(declarationDiagnostics);
                continue;
            }

            var assignments = new List<ReviewedConceptAssignment>();
            var duplicateAssignments = declaration.Assignments
                .GroupBy(item => item.EntityId, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var assignment in declaration.Assignments
                         .OrderBy(item => item.EntityId, StringComparer.Ordinal)
                         .ThenBy(item => item.SourceReference, StringComparer.Ordinal))
            {
                var diagnostic = ValidateAssignment(declaration.ConceptId, assignment, duplicateAssignments);
                if (diagnostic is not null)
                {
                    diagnostics.Add(diagnostic);
                    continue;
                }

                assignments.Add(assignment);
            }

            declarations.Add(declaration with { Assignments = assignments });
        }

        return new ReviewedConceptValidationResult(true, declarations, diagnostics);
    }

    private static List<ReviewedConceptDiagnostic> ValidateEnvelope(
        ReviewedConceptCatalog catalog,
        ReviewedConceptEvidenceContext evidence)
    {
        var diagnostics = new List<ReviewedConceptDiagnostic>();
        AddCatalogError(catalog.SchemaVersion != ReviewedConceptSerializer.CurrentSchemaVersion, "RC100",
            "Reviewed concept schema is incompatible.", diagnostics);
        AddCatalogError(!catalog.RepositoryId.Equals(evidence.RepositoryId, StringComparison.Ordinal), "RC101",
            "Reviewed concept repository identity does not match current evidence.", diagnostics);
        AddCatalogError(!catalog.Branch.Equals(evidence.Branch, StringComparison.Ordinal), "RC102",
            "Reviewed concept branch does not match current evidence.", diagnostics);
        AddCatalogError(!catalog.BranchKey.Equals(evidence.BranchKey, StringComparison.Ordinal), "RC103",
            "Reviewed concept branch key does not match current evidence.", diagnostics);
        AddCatalogError(catalog.SourceSnapshotSchema != evidence.SourceSnapshotSchema, "RC104",
            "Reviewed concept snapshot schema does not match current evidence.", diagnostics);
        AddCatalogError(!catalog.SourceAnalyzerVersion.Equals(evidence.SourceAnalyzerVersion, StringComparison.Ordinal),
            "RC105", "Reviewed concept analyzer version does not match current evidence.", diagnostics);
        AddCatalogError(string.IsNullOrWhiteSpace(catalog.VocabularyVersion), "RC106",
            "Reviewed concept vocabulary version is missing.", diagnostics);
        AddCatalogError(catalog.Declarations
                .GroupBy(item => item.ConceptId, StringComparer.Ordinal)
                .Any(group => group.Count() > 1),
            "RC107", "Reviewed concept identifiers must be unique.", diagnostics);
        return diagnostics;
    }

    private List<ReviewedConceptDiagnostic> ValidateDeclaration(ReviewedConceptDeclaration declaration)
    {
        var diagnostics = new List<ReviewedConceptDiagnostic>();
        AddDeclarationError(!ConceptIdPattern().IsMatch(declaration.ConceptId), "RC200",
            "Reviewed concept identifier is invalid.", declaration.ConceptId, diagnostics);
        AddDeclarationError(string.IsNullOrWhiteSpace(declaration.Definition), "RC201",
            "Reviewed concept definition is missing.", declaration.ConceptId, diagnostics);
        AddDeclarationError(string.IsNullOrWhiteSpace(declaration.Provenance.SourceReference)
                || string.IsNullOrWhiteSpace(declaration.Provenance.SourceHash),
            "RC202", "Reviewed concept provenance is incomplete.", declaration.ConceptId, diagnostics);
        AddDeclarationError(string.IsNullOrWhiteSpace(declaration.Review.Reviewer)
                || declaration.Review.Version <= 0
                || declaration.Review.ReviewedAtUtc.Offset != TimeSpan.Zero,
            "RC203", "Reviewed concept review metadata is invalid.", declaration.ConceptId, diagnostics);
        AddDeclarationError(!declaration.Fingerprint.Equals(
                ReviewedConceptSerializer.CreateDeclarationFingerprint(declaration),
                StringComparison.Ordinal),
            "RC204", "Reviewed concept declaration fingerprint is invalid.", declaration.ConceptId, diagnostics);

        var conceptTokens = _normalizer.Tokenize(declaration.ConceptId).ToHashSet(StringComparer.Ordinal);
        var allMetadataTokens = declaration.AnchorTokens.SelectMany(group => group)
            .Concat(declaration.QualificationSupportTokens)
            .Concat(declaration.ContextSupportTokens)
            .ToArray();
        var invalidToken = declaration.AnchorTokens.Any(group => group.Count == 0)
            || allMetadataTokens.Any(token =>
            {
                var normalized = _normalizer.Tokenize(token);
                return normalized.Count != 1
                    || !normalized[0].Equals(token, StringComparison.Ordinal)
                    || !conceptTokens.Contains(token);
            });
        AddDeclarationError(invalidToken, "RC205",
            "Reviewed concept qualification metadata contains an invalid token.", declaration.ConceptId, diagnostics);

        var overlap = declaration.QualificationSupportTokens
            .Intersect(declaration.ContextSupportTokens, StringComparer.Ordinal)
            .Any();
        AddDeclarationError(overlap, "RC206",
            "Reviewed concept qualification and context support tokens must be disjoint.",
            declaration.ConceptId, diagnostics);

        var invalidPolicy = declaration.AnchorPolicy switch
        {
            ReviewedConceptAnchorPolicy.Clear => declaration.AnchorTokens.Count == 0
                || declaration.QualificationSupportTokens.Count == 0,
            ReviewedConceptAnchorPolicy.NotRequired => declaration.AnchorTokens.Count != 0,
            ReviewedConceptAnchorPolicy.Ambiguous => false,
            _ => true
        };
        AddDeclarationError(invalidPolicy, "RC207",
            "Reviewed concept anchor policy configuration is invalid.", declaration.ConceptId, diagnostics);
        return diagnostics;
    }

    private static ReviewedConceptDiagnostic? ValidateAssignment(
        string conceptId,
        ReviewedConceptAssignment assignment,
        IReadOnlySet<string> duplicateAssignments)
    {
        if (string.IsNullOrWhiteSpace(assignment.EntityId))
        {
            return AssignmentError("RC300", "Reviewed concept assignment entity identity is missing.", conceptId,
                assignment.EntityId);
        }

        if (!IsNormalizedRelativeSourceReference(assignment.SourceReference))
        {
            return AssignmentError("RC301", "Reviewed concept assignment source reference is invalid.", conceptId,
                assignment.EntityId);
        }

        if (string.IsNullOrWhiteSpace(assignment.SourceFingerprint))
        {
            return AssignmentError("RC302", "Reviewed concept assignment source fingerprint is missing.", conceptId,
                assignment.EntityId);
        }

        return duplicateAssignments.Contains(assignment.EntityId)
            ? AssignmentError("RC303", "Reviewed concept assignment identity is duplicated.", conceptId,
                assignment.EntityId)
            : null;
    }

    private static bool IsNormalizedRelativeSourceReference(string sourceReference)
    {
        if (string.IsNullOrWhiteSpace(sourceReference) || sourceReference.Contains('\\'))
        {
            return false;
        }

        var path = sourceReference;
        var separator = sourceReference.LastIndexOf(':');
        if (separator > 0 && int.TryParse(sourceReference[(separator + 1)..], out var line) && line > 0)
        {
            path = sourceReference[..separator];
        }

        return !Path.IsPathFullyQualified(path)
            && !path.StartsWith("/", StringComparison.Ordinal)
            && path.Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static void AddCatalogError(
        bool condition,
        string code,
        string message,
        ICollection<ReviewedConceptDiagnostic> diagnostics)
    {
        if (condition)
        {
            diagnostics.Add(new ReviewedConceptDiagnostic(
                code,
                AnalysisDiagnosticSeverity.Error,
                ReviewedConceptDiagnosticScope.Catalog,
                message));
        }
    }

    private static void AddDeclarationError(
        bool condition,
        string code,
        string message,
        string conceptId,
        ICollection<ReviewedConceptDiagnostic> diagnostics)
    {
        if (condition)
        {
            diagnostics.Add(new ReviewedConceptDiagnostic(
                code,
                AnalysisDiagnosticSeverity.Warning,
                ReviewedConceptDiagnosticScope.Declaration,
                message,
                conceptId));
        }
    }

    private static ReviewedConceptDiagnostic AssignmentError(
        string code,
        string message,
        string conceptId,
        string? entityId) => new(
        code,
        AnalysisDiagnosticSeverity.Warning,
        ReviewedConceptDiagnosticScope.Assignment,
        message,
        conceptId,
        entityId);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex ConceptIdPattern();
}
