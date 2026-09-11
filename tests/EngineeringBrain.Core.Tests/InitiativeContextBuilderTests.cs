using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class InitiativeContextBuilderTests
{
    [Fact]
    public async Task BuildAsync_SmallContextIsAcceptedAndPreservesMandatoryUnderstanding()
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync();
        var understanding = InitiativeAnalysisTestData.Understanding("business");
        var retrieval = new InitiativeCandidateRetriever().Retrieve(understanding, memory.Manifest, memory.SourceSnapshot);

        var context = await new InitiativeContextBuilder().BuildAsync(understanding, retrieval, memory);

        Assert.InRange(context.EstimatedTokens, 1, 50_000);
        Assert.Contains("INITIATIVE_UNDERSTANDING", context.Content, StringComparison.Ordinal);
        Assert.Contains(understanding.Summary, context.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_PrunesLowerRankedWholeNotesFirst()
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync();
        var understanding = InitiativeAnalysisTestData.Understanding("core business model execute");
        var retrieval = new InitiativeCandidateRetriever().Retrieve(understanding, memory.Manifest, memory.SourceSnapshot);
        var options = new TokenBudgetOptions(
            RootAndArchitectureTokens: 3_500,
            ProjectNotesTokens: 1,
            ComponentNotesTokens: 1,
            GraphEvidenceTokens: 6_000,
            MaximumReasoningInputTokens: 50_000);

        var context = await new InitiativeContextBuilder(budget: options).BuildAsync(understanding, retrieval, memory);

        Assert.NotEmpty(context.PrunedNotePaths);
        Assert.DoesNotContain(context.PrunedNotePaths, path => context.Content.Contains($"NOTE: {path}\n", StringComparison.Ordinal));
        Assert.Contains(understanding.Summary, context.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_PreservesHigherRankedComponentWhenBudgetFitsOnlyIt()
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync();
        var understanding = InitiativeAnalysisTestData.Understanding("business core execute model");
        var retrieval = new InitiativeCandidateRetriever().Retrieve(understanding, memory.Manifest, memory.SourceSnapshot);
        Assert.True(retrieval.Components.Count > 1);
        var top = retrieval.Components[0];
        var topNote = memory.Manifest.Notes.Single(note => note.SourceId == top.EntityId);
        var topContent = await new ProjectMemoryReader().ReadManagedNoteAsync(memory, topNote);
        var budget = new TokenEstimator().Estimate($"NOTE: {topNote.RelativePath}\n{topContent}");
        var options = new TokenBudgetOptions(ComponentNotesTokens: budget);

        var context = await new InitiativeContextBuilder(budget: options).BuildAsync(understanding, retrieval, memory);

        Assert.Contains(topNote.RelativePath, context.IncludedNotePaths);
        Assert.Contains(context.PrunedNotePaths, path => path.StartsWith("components/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BuildAsync_IncludesManagedNotesWithoutMidStringTruncation()
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync();
        var understanding = InitiativeAnalysisTestData.Understanding("business");
        var retrieval = new InitiativeCandidateRetriever().Retrieve(understanding, memory.Manifest, memory.SourceSnapshot);
        var context = await new InitiativeContextBuilder().BuildAsync(understanding, retrieval, memory);
        var note = memory.Manifest.Notes.First(item =>
            item.Kind == KnowledgeNoteKind.Component && context.IncludedNotePaths.Contains(item.RelativePath));
        var noteContent = await new ProjectMemoryReader().ReadManagedNoteAsync(memory, note);

        Assert.Contains(noteContent, context.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_HardLimitCannotBeSilentlyExceeded()
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync();
        var understanding = InitiativeAnalysisTestData.Understanding("business");
        var retrieval = new InitiativeCandidateRetriever().Retrieve(understanding, memory.Manifest, memory.SourceSnapshot);
        var options = new TokenBudgetOptions(MaximumReasoningInputTokens: 1);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new InitiativeContextBuilder(budget: options).BuildAsync(understanding, retrieval, memory));

        Assert.Contains("hard limit", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TokenEstimator_IsDeterministicAndConservativeByFourCharacters()
    {
        var estimator = new TokenEstimator();

        Assert.Equal(3, estimator.Estimate("123456789"));
        Assert.Equal(estimator.Estimate("deterministic"), estimator.Estimate("deterministic"));
    }

    [Fact]
    public async Task ReadManagedNoteAsync_RejectsTamperedMemory()
    {
        using var fixture = new InitiativeMemoryFixture();
        var memory = await fixture.CreateMemoryAsync();
        var note = memory.Manifest.Notes.First(item => item.Kind == KnowledgeNoteKind.Component);
        File.AppendAllText(Path.Combine(memory.Location, note.RelativePath.Replace('/', Path.DirectorySeparatorChar)), "tampered");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new ProjectMemoryReader().ReadManagedNoteAsync(memory, note));
    }
}
