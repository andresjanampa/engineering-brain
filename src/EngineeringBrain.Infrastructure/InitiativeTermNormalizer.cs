using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace EngineeringBrain.Infrastructure;

public sealed partial class InitiativeTermNormalizer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "add", "agregar", "al", "and", "class", "con", "data", "de", "del", "el", "en", "existing",
        "existente", "for", "la", "las", "los", "maintain", "manager", "manteniendo", "of", "para", "por",
        "project", "projects", "proyecto", "proyectos", "service", "soporte", "support", "system", "the", "to",
        "un", "una", "y"
    };

    private static readonly IReadOnlyDictionary<string, string> Synonyms =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["analisis"] = "analysis",
            ["analizador"] = "analyzer",
            ["analizar"] = "analyzer",
            ["arquitectura"] = "architecture",
            ["extensible"] = "extension",
            ["interfaz"] = "interface",
            ["lenguaje"] = "language",
            ["proyecto"] = "project",
            ["soporte"] = "support"
        };

    public IReadOnlyList<string> Tokenize(params IEnumerable<string>[] values)
    {
        var tokens = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var value in values.SelectMany(item => item))
        {
            foreach (var token in Tokenize(value))
            {
                tokens.Add(token);
            }
        }

        return tokens.ToArray();
    }

    public IReadOnlyList<string> Tokenize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var split = LowerToUpper().Replace(AcronymBoundary().Replace(value, "$1 $2"), "$1 $2");
        var normalized = RemoveDiacritics(split).ToLowerInvariant();
        return NonAlphaNumeric().Split(normalized)
            .Where(token => token.Length > 1 && !StopWords.Contains(token))
            .Select(token => Synonyms.GetValueOrDefault(token, token))
            .Where(token => !StopWords.Contains(token))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public string NormalizePhrase(string value) => string.Join(' ', Tokenize(value));

    private static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex("([A-Z]+)([A-Z][a-z])", RegexOptions.CultureInvariant)]
    private static partial Regex AcronymBoundary();

    [GeneratedRegex("([a-z0-9])([A-Z])", RegexOptions.CultureInvariant)]
    private static partial Regex LowerToUpper();

    [GeneratedRegex("[^a-zA-Z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphaNumeric();
}
