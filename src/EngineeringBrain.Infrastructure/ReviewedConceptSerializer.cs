using System.Text.Json;
using System.Text.Json.Serialization;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public static class ReviewedConceptSerializer
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(ReviewedConceptCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var json = JsonSerializer.Serialize(Canonicalize(catalog), Options);
        return KnowledgeIdentity.NormalizeLineEndings(json).TrimEnd('\n') + "\n";
    }

    public static ReviewedConceptCatalog Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            var catalog = JsonSerializer.Deserialize<ReviewedConceptCatalog>(json, Options)
                ?? throw new InvalidDataException("Reviewed concept catalog is empty.");
            if (!HasCompleteStructure(catalog))
            {
                throw new InvalidDataException("Reviewed concept catalog structure is invalid.");
            }

            return catalog;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Reviewed concept JSON is invalid.", exception);
        }
        catch (NotSupportedException exception)
        {
            throw new InvalidDataException("Reviewed concept JSON is invalid.", exception);
        }
    }

    private static bool HasCompleteStructure(ReviewedConceptCatalog catalog) =>
        catalog.RepositoryId is not null
        && catalog.Branch is not null
        && catalog.BranchKey is not null
        && catalog.SourceAnalyzerVersion is not null
        && catalog.VocabularyVersion is not null
        && catalog.Declarations is not null
        && catalog.Declarations.All(declaration => declaration is not null
            && declaration.ConceptId is not null
            && declaration.Definition is not null
            && declaration.AnchorTokens is not null
            && declaration.AnchorTokens.All(group => group is not null && group.All(token => token is not null))
            && declaration.QualificationSupportTokens is not null
            && declaration.QualificationSupportTokens.All(token => token is not null)
            && declaration.ContextSupportTokens is not null
            && declaration.ContextSupportTokens.All(token => token is not null)
            && declaration.Assignments is not null
            && declaration.Assignments.All(assignment => assignment is not null
                && assignment.EntityId is not null
                && assignment.SourceReference is not null
                && assignment.SourceFingerprint is not null)
            && declaration.Provenance is not null
            && declaration.Provenance.SourceReference is not null
            && declaration.Provenance.SourceHash is not null
            && declaration.Review is not null
            && declaration.Review.Reviewer is not null
            && declaration.Fingerprint is not null);

    public static ReviewedConceptCatalog Canonicalize(ReviewedConceptCatalog catalog) => catalog with
    {
        Declarations = catalog.Declarations
            .Select(declaration => declaration with
            {
                Definition = KnowledgeIdentity.NormalizeLineEndings(declaration.Definition),
                AnchorTokens = declaration.AnchorTokens
                    .Select(group => (IReadOnlyList<string>)group
                        .Distinct(StringComparer.Ordinal)
                        .Order(StringComparer.Ordinal)
                        .ToArray())
                    .OrderBy(group => string.Join('\n', group), StringComparer.Ordinal)
                    .ToArray(),
                QualificationSupportTokens = declaration.QualificationSupportTokens
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                ContextSupportTokens = declaration.ContextSupportTokens
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                Assignments = declaration.Assignments
                    .OrderBy(item => item.EntityId, StringComparer.Ordinal)
                    .ThenBy(item => item.SourceReference, StringComparer.Ordinal)
                    .ToArray(),
                Provenance = declaration.Provenance with
                {
                    SourceReference = KnowledgeIdentity.NormalizeLineEndings(
                        declaration.Provenance.SourceReference)
                }
            })
            .OrderBy(item => item.ConceptId, StringComparer.Ordinal)
            .ToArray()
    };

    public static string CreateDeclarationFingerprint(ReviewedConceptDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var canonical = Canonicalize(new ReviewedConceptCatalog(
            CurrentSchemaVersion,
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            string.Empty,
            string.Empty,
            [declaration])).Declarations[0];

        var payload = new DeclarationFingerprintPayload(
            canonical.ConceptId,
            canonical.Definition,
            canonical.AnchorPolicy,
            canonical.AnchorTokens,
            canonical.QualificationSupportTokens,
            canonical.ContextSupportTokens,
            canonical.Assignments,
            canonical.Provenance,
            canonical.Review with { ReviewedAtUtc = canonical.Review.ReviewedAtUtc.ToUniversalTime() });
        return KnowledgeIdentity.ContentHash(JsonSerializer.Serialize(payload, Options));
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private sealed record DeclarationFingerprintPayload(
        string ConceptId,
        string Definition,
        ReviewedConceptAnchorPolicy AnchorPolicy,
        IReadOnlyList<IReadOnlyList<string>> AnchorTokens,
        IReadOnlyList<string> QualificationSupportTokens,
        IReadOnlyList<string> ContextSupportTokens,
        IReadOnlyList<ReviewedConceptAssignment> Assignments,
        ReviewedConceptProvenance Provenance,
        ReviewedConceptReview Review);
}
