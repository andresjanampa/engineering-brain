using System.Globalization;
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
            return JsonSerializer.Deserialize<ReviewedConceptCatalog>(json, Options)
                ?? throw new InvalidDataException("Reviewed concept catalog is empty.");
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

        var values = new List<string?>
        {
            canonical.ConceptId,
            canonical.Definition,
            canonical.AnchorPolicy.ToString()
        };
        values.AddRange(canonical.AnchorTokens.Select(group => string.Join('\u001f', group)));
        values.Add("<qualification-support>");
        values.AddRange(canonical.QualificationSupportTokens);
        values.Add("<context-support>");
        values.AddRange(canonical.ContextSupportTokens);
        values.Add("<assignments>");
        foreach (var assignment in canonical.Assignments)
        {
            values.Add(assignment.EntityId);
            values.Add(assignment.SourceReference);
            values.Add(assignment.SourceFingerprint);
        }

        values.Add(canonical.Provenance.SourceReference);
        values.Add(canonical.Provenance.SourceHash);
        values.Add(canonical.Review.Reviewer);
        values.Add(canonical.Review.Version.ToString(CultureInfo.InvariantCulture));
        values.Add(canonical.Review.ReviewedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        return KnowledgeIdentity.Fingerprint(values.ToArray());
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
}
