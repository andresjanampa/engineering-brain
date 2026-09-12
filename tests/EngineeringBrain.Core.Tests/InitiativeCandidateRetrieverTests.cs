using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class InitiativeCandidateRetrieverTests
{
    [Fact]
    public void Retrieve_ExactComponentNameRanksCandidateAndExplainsScore()
    {
        var result = Retrieve("BusinessService");

        var candidate = Assert.Single(result.Components, item => item.EntityId == "entity:business-service");
        Assert.True(candidate.Score >= 12);
        Assert.Contains(candidate.MatchReasons, reason => reason.Signal == "exact component name");
    }

    [Fact]
    public void Retrieve_ProjectNameSelectsProject()
    {
        var result = Retrieve("Business");

        Assert.Equal("project:business", result.Projects[0].ProjectId);
        Assert.Contains(result.Projects[0].MatchReasons, reason => reason.Signal.Contains("project alias", StringComparison.Ordinal));
    }

    [Fact]
    public void Retrieve_CsprojFilenameActsAsObjectiveAlias()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var project = snapshot.Projects.Single(item => item.Id == "project:business");
        snapshot = snapshot with
        {
            Projects = snapshot.Projects.Select(item => item.Id == project.Id
                ? item with { Name = "Human Display" }
                : item).ToArray()
        };

        var result = Retrieve("Business", snapshot);

        Assert.Contains(result.Projects, item => item.ProjectId == project.Id);
    }

    [Fact]
    public void Retrieve_ProjectEntityDisplayNameActsAsObjectiveAlias()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var projectEntity = snapshot.Entities.Single(item => item.Id == "project:business");
        snapshot = snapshot with
        {
            Entities = snapshot.Entities.Select(item => item.Id == projectEntity.Id
                ? item with { Name = "brain", FullName = "brain" }
                : item).ToArray()
        };

        var result = Retrieve("brain", snapshot);

        Assert.Contains(result.Projects, item => item.ProjectId == projectEntity.Id);
    }

    [Fact]
    public void Retrieve_NamespaceAndMemberNamesAreSignals()
    {
        var namespaceResult = Retrieve("Demo Business");
        var memberResult = Retrieve("Execute");

        Assert.Contains(namespaceResult.Components, item => item.EntityId == "entity:business-service");
        Assert.Contains(memberResult.Components.Single(item => item.EntityId == "entity:business-service").MatchReasons,
            reason => reason.Signal == "member name");
    }

    [Theory]
    [InlineData("business_service")]
    [InlineData("business-service")]
    [InlineData("business service")]
    public void Retrieve_NormalizesPascalSnakeKebabAndSpaces(string term)
    {
        var result = Retrieve(term);

        Assert.Equal("entity:business-service", result.Components[0].EntityId);
    }

    [Fact]
    public void Retrieve_GenericStopWordsDoNotCreateCandidates()
    {
        var result = Retrieve("service manager class data system project");

        Assert.Empty(result.Components);
        Assert.Empty(result.Projects);
    }

    [Fact]
    public void Retrieve_OrderingIsDeterministic()
    {
        var first = Retrieve("core business model execute");
        var second = Retrieve("core business model execute");

        Assert.Equal(first.Components.Select(item => item.EntityId), second.Components.Select(item => item.EntityId));
        Assert.Equal(first.Projects.Select(item => item.ProjectId), second.Projects.Select(item => item.ProjectId));
    }

    [Fact]
    public void Retrieve_GraphExpansionAddsDirectInterfaceAndIsBounded()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var manifest = new ProjectMemoryBuilder().Build(snapshot).Manifest;
        var result = new InitiativeCandidateRetriever(options: new CandidateRetrievalOptions(4, 1, 1, 1))
            .Retrieve(InitiativeAnalysisTestData.Understanding("business"), manifest, snapshot);

        Assert.Contains(result.Components, item => item.EntityId == "entity:contract" && item.GraphExpanded);
        Assert.True(result.Components.Count <= 2);
        Assert.Contains(result.Relations, item => item.RelationType == CodeRelationType.Implements);
    }

    [Fact]
    public void Retrieve_RespectsCentralCandidateLimits()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var manifest = new ProjectMemoryBuilder().Build(snapshot).Manifest;
        var result = new InitiativeCandidateRetriever(options: new CandidateRetrievalOptions(1, 1, 0, 0))
            .Retrieve(InitiativeAnalysisTestData.Understanding("core business model execute"), manifest, snapshot);

        Assert.Single(result.Projects);
        Assert.Single(result.Components);
    }

    [Fact]
    public void Retrieve_UsesExhaustiveSnapshotWhenProjectMarkdownTruncatesComponents()
    {
        var snapshot = AddComponents(ProjectMemoryTestFactory.Create(), 101);
        var build = new ProjectMemoryBuilder().Build(snapshot);
        var projectNote = build.Notes.Single(note => note.ManifestEntry.SourceId == "project:core");
        var hidden = snapshot.Entities
            .Where(item => item.Id.StartsWith("entity:generated-", StringComparison.Ordinal))
            .First(item => !projectNote.Content.Contains(item.FullName, StringComparison.Ordinal));

        var result = new InitiativeCandidateRetriever().Retrieve(
            InitiativeAnalysisTestData.Understanding(hidden.Name),
            build.Manifest,
            snapshot);

        Assert.Contains(result.Components, item => item.EntityId == hidden.Id);
    }

    private static CandidateRetrievalResult Retrieve(string term, RepositorySnapshot? snapshot = null)
    {
        snapshot ??= ProjectMemoryTestFactory.Create();
        return new InitiativeCandidateRetriever().Retrieve(
            InitiativeAnalysisTestData.Understanding(term),
            new ProjectMemoryBuilder().Build(snapshot).Manifest,
            snapshot);
    }

    private static RepositorySnapshot AddComponents(RepositorySnapshot snapshot, int count)
    {
        var project = snapshot.Entities.Single(item => item.Id == "project:core");
        var namespaceEntity = snapshot.Entities.Single(item => item.Id == "entity:ns-core");
        var added = Enumerable.Range(1, count).Select(index => ProjectMemoryTestFactory.Entity(
            $"entity:generated-{index}",
            $"UniqueGateway{index}",
            $"Demo.Core.UniqueGateway{index}",
            CodeEntityType.Class,
            $"src/Core/UniqueGateway{index}.cs",
            project.Id)).ToArray();
        return snapshot with
        {
            Entities = snapshot.Entities.Concat(added).ToArray(),
            Relations = snapshot.Relations.Concat(added.Select(item => ProjectMemoryTestFactory.Contains(namespaceEntity, item))).ToArray()
        };
    }
}
