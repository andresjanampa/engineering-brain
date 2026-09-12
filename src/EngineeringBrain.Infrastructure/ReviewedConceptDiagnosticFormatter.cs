using System.Text;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public static class ReviewedConceptDiagnosticFormatter
{
    public const int MaximumRenderedLength = 512;

    private const int MaximumCodeLength = 24;
    private const int MaximumIdentityLength = 96;
    private const int MaximumMessageLength = 240;

    public static string Format(ReviewedConceptDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        var identities = new[]
        {
            SanitizeAndBound(diagnostic.ConceptId, MaximumIdentityLength),
            SanitizeAndBound(diagnostic.EntityId, MaximumIdentityLength)
        }.Where(value => value.Length > 0);
        var identity = string.Join(' ', identities);
        var rendered = $"Reviewed concept {diagnostic.Severity} "
            + SanitizeAndBound(diagnostic.Code, MaximumCodeLength)
            + (identity.Length == 0 ? string.Empty : $" [{identity}]")
            + $": {SanitizeAndBound(diagnostic.Message, MaximumMessageLength)}";
        return Bound(rendered, MaximumRenderedLength);
    }

    private static string SanitizeAndBound(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsControl(character) ? ' ' : character);
        }

        return Bound(builder.ToString().Trim(), maximumLength);
    }

    private static string Bound(string value, int maximumLength) => value.Length <= maximumLength
        ? value
        : value[..(maximumLength - 3)] + "...";
}
