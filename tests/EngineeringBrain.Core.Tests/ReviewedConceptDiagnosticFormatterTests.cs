using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ReviewedConceptDiagnosticFormatterTests
{
    [Theory]
    [InlineData("concept\nsecond-line")]
    [InlineData("concept\rsecond-line")]
    [InlineData("concept\tindented")]
    [InlineData("concept\u0001control")]
    public void Format_ControlCharactersCannotCreateAdditionalOutputLines(string conceptId)
    {
        var rendered = ReviewedConceptDiagnosticFormatter.Format(Diagnostic(conceptId));

        Assert.DoesNotContain(rendered, character => char.IsControl(character));
        Assert.Single(rendered.Split('\n'));
    }

    [Fact]
    public void Format_VeryLongIdentifiersAndMessageAreBounded()
    {
        var diagnostic = Diagnostic(new string('c', 10_000)) with
        {
            EntityId = new string('e', 10_000),
            Message = new string('m', 10_000)
        };

        var rendered = ReviewedConceptDiagnosticFormatter.Format(diagnostic);

        Assert.True(rendered.Length <= ReviewedConceptDiagnosticFormatter.MaximumRenderedLength);
        Assert.EndsWith("...", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_NormalIdentifiersRemainReadable()
    {
        var diagnostic = Diagnostic("provider-boundary") with
        {
            EntityId = "entity:abc123"
        };

        var rendered = ReviewedConceptDiagnosticFormatter.Format(diagnostic);

        Assert.Equal(
            "Reviewed concept Warning RC200 [provider-boundary entity:abc123]: Reviewed concept identifier is invalid.",
            rendered);
    }

    private static ReviewedConceptDiagnostic Diagnostic(string conceptId) => new(
        "RC200",
        AnalysisDiagnosticSeverity.Warning,
        ReviewedConceptDiagnosticScope.Declaration,
        "Reviewed concept identifier is invalid.",
        conceptId);
}
