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
        Assert.Contains(result.Projects[0].MatchReasons, reason => reason.Signal == "exact project alias");
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

    [Fact]
    public void Retrieve_ExactComponentNameOutranksMemberOnlyMatch()
    {
        var snapshot = AddComponent(ProjectMemoryTestFactory.Create(), "entity:member-only", "Worker", "Demo.Core.Worker",
            "project:core", ["BusinessService"]);

        var result = Retrieve("BusinessService", snapshot);

        Assert.Equal("entity:business-service", result.Components[0].EntityId);
        Assert.Contains(result.Components[0].MatchReasons, reason => reason.Signal == "exact component name");
    }

    [Fact]
    public void Retrieve_ExactFullNameOutranksTokenMatches()
    {
        var result = Retrieve("Demo.Business.BusinessService");

        Assert.Equal("entity:business-service", result.Components[0].EntityId);
        Assert.Contains(result.Components[0].MatchReasons, reason => reason.Signal == "exact full name");
    }

    [Fact]
    public void Retrieve_ExactIdentityDoesNotDoubleCountItsNameTokens()
    {
        var candidate = Retrieve("BusinessService").Components.Single(item => item.EntityId == "entity:business-service");

        Assert.DoesNotContain(candidate.MatchReasons, reason => reason.Signal == "component name");
        Assert.DoesNotContain(candidate.MatchReasons, reason => reason.Signal == "component name rarity");
    }

    [Fact]
    public void Retrieve_ComponentContributionToProjectIsBounded()
    {
        var result = Retrieve("BusinessService");
        var project = result.Projects.Single(item => item.ProjectId == "project:business");

        Assert.Contains(project.MatchReasons,
            reason => reason.Signal == "contains selected component" && reason.Points == 12);
    }

    [Fact]
    public void Retrieve_OneMeaningfulMemberMatchContributes()
    {
        var result = Retrieve("Execute");
        var candidate = result.Components.Single(item => item.EntityId == "entity:business-service");

        Assert.Contains(candidate.MatchReasons, reason => reason.Signal == "member name" && reason.Points > 0);
    }

    [Fact]
    public void Retrieve_RepeatedMemberTokenIsDeduplicatedAndBounded()
    {
        var snapshot = AddComponent(ProjectMemoryTestFactory.Create(), "entity:worker", "Worker", "Demo.Core.Worker",
            "project:core", ["ExecuteFirst", "ExecuteSecond", "ExecuteThird", "ExecuteFourth"]);

        var candidate = Retrieve("execute", snapshot).Components.Single(item => item.EntityId == "entity:worker");

        Assert.Single(candidate.MatchReasons, reason => reason.Signal == "member name" && reason.MatchedValue == "execute");
        Assert.True(candidate.MatchReasons.Where(reason => reason.Signal == "member name").Sum(reason => reason.Points) <= 6);
    }

    [Fact]
    public void Retrieve_DifferentMemberTermsRemainBounded()
    {
        var snapshot = AddComponent(ProjectMemoryTestFactory.Create(), "entity:worker", "Worker", "Demo.Core.Worker",
            "project:core", ["Alpha", "Beta", "Gamma", "Delta"]);

        var candidate = Retrieve("alpha beta gamma delta", snapshot).Components.Single(item => item.EntityId == "entity:worker");

        Assert.Equal(6, candidate.MatchReasons.Where(reason => reason.Signal == "member name").Sum(reason => reason.Points));
    }

    [Fact]
    public void Retrieve_ProductionCandidateOutranksEquivalentTestCandidate()
    {
        var snapshot = AddProductionTwin(ProjectMemoryTestFactory.Create());

        var result = Retrieve("BusinessService", snapshot);

        Assert.Equal("entity:production-twin", result.Components[0].EntityId);
        var test = result.Components.Single(item => item.EntityId == "entity:business-service");
        Assert.Contains(test.MatchReasons, reason => reason.Signal == "non-test initiative penalty" && reason.Points < 0);
    }

    [Fact]
    public void Retrieve_TestIntentDisablesPenalty()
    {
        var snapshot = MarkBusinessProjectAsTests(ProjectMemoryTestFactory.Create());

        var candidate = Retrieve("integration tests BusinessService", snapshot).Components
            .Single(item => item.EntityId == "entity:business-service");

        Assert.DoesNotContain(candidate.MatchReasons, reason => reason.Signal == "non-test initiative penalty");
    }

    [Fact]
    public void Retrieve_ExplicitTestComponentRemainsSearchableDespitePenalty()
    {
        var snapshot = MarkBusinessProjectAsTests(ProjectMemoryTestFactory.Create());

        var result = Retrieve("BusinessService", snapshot);

        Assert.Contains(result.Components, item => item.EntityId == "entity:business-service");
        Assert.Contains(result.Components.Single(item => item.EntityId == "entity:business-service").MatchReasons,
            reason => reason.Signal == "exact component name");
    }

    [Fact]
    public void Retrieve_WeakTestMatchRemainsEligibleWhenPenaltyMakesScoreNegative()
    {
        var snapshot = MarkBusinessProjectAsTests(ProjectMemoryTestFactory.Create());

        var candidate = Retrieve("Execute", snapshot).Components
            .Single(item => item.EntityId == "entity:business-service");

        Assert.True(candidate.Score < 0);
        Assert.Contains(candidate.MatchReasons, reason => reason.Signal == "member name" && reason.Points > 0);
        Assert.Contains(candidate.MatchReasons, reason => reason.Signal == "non-test initiative penalty");
    }

    [Fact]
    public void Retrieve_StableIdentityBreaksEqualScoreTies()
    {
        var snapshot = AddComponent(ProjectMemoryTestFactory.Create(), "entity:z", "SignalAlpha", "Demo.Core.SignalAlpha", "project:core", []);
        snapshot = AddComponent(snapshot, "entity:a", "SignalBeta", "Demo.Core.SignalBeta", "project:core", []);

        var first = Retrieve("signal", snapshot).Components.Where(item => item.EntityId is "entity:z" or "entity:a").Select(item => item.FullName).ToArray();
        var second = Retrieve("signal", snapshot).Components.Where(item => item.EntityId is "entity:z" or "entity:a").Select(item => item.FullName).ToArray();

        Assert.Equal(["Demo.Core.SignalAlpha", "Demo.Core.SignalBeta"], first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void TermRarity_RareTermsReceiveLargerBoundedDeterministicContribution()
    {
        var first = TermRarityIndex.Create([["common", "rare"], ["common"], ["common"]]);
        var reordered = TermRarityIndex.Create([["common"], ["common", "rare"], ["common"]]);

        Assert.True(first.Contribution("rare") > first.Contribution("common"));
        Assert.InRange(first.Contribution("rare"), 0, 4);
        Assert.Equal(first.Contribution("rare"), reordered.Contribution("rare"));
        Assert.Equal(0, TermRarityIndex.Create([]).Contribution("missing"));
    }

    [Fact]
    public void TestCandidateClassifier_UsesOnlyConventionalProjectEvidence()
    {
        var suffix = ProjectMemoryTestFactory.Create().Projects[0] with { Name = "Demo.Tests" };
        var path = suffix with { Name = "Demo", RelativePath = "tests/Demo/Demo.csproj" };
        var incidental = suffix with { Name = "ContestTools", RelativePath = "src/TestHelpers/TestHelpers.csproj" };

        Assert.True(TestCandidateClassifier.IsTestProject(suffix));
        Assert.True(TestCandidateClassifier.IsTestProject(path));
        Assert.False(TestCandidateClassifier.IsTestProject(incidental));
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

    private static RepositorySnapshot AddComponent(
        RepositorySnapshot snapshot,
        string id,
        string name,
        string fullName,
        string projectId,
        IReadOnlyList<string> members)
    {
        var component = ProjectMemoryTestFactory.Entity(id, name, fullName, CodeEntityType.Class, $"src/Core/{name}.cs", projectId);
        var namespaceEntity = snapshot.Entities.First(item => item.Id == "entity:ns-core");
        var memberEntities = members.Select((member, index) => ProjectMemoryTestFactory.Entity(
            $"{id}:member:{index}", member, $"{fullName}.{member}()", CodeEntityType.Method,
            component.RelativeFilePath, projectId)).ToArray();
        return snapshot with
        {
            Entities = snapshot.Entities.Concat([component]).Concat(memberEntities).ToArray(),
            Relations = snapshot.Relations.Concat([ProjectMemoryTestFactory.Contains(namespaceEntity, component)])
                .Concat(memberEntities.Select(member => ProjectMemoryTestFactory.Contains(component, member))).ToArray()
        };
    }

    private static RepositorySnapshot AddProductionTwin(RepositorySnapshot snapshot)
    {
        snapshot = MarkBusinessProjectAsTests(snapshot);
        return AddComponent(snapshot, "entity:production-twin", "BusinessService", "Demo.Core.BusinessService", "project:core", []);
    }

    private static RepositorySnapshot MarkBusinessProjectAsTests(RepositorySnapshot snapshot)
    {
        var project = snapshot.Projects.Single(item => item.Id == "project:business");
        return snapshot with
        {
            Projects = snapshot.Projects.Select(item => item.Id == project.Id
                ? item with { Name = "Business.Tests", RelativePath = "tests/Business.Tests/Business.Tests.csproj" }
                : item).ToArray()
        };
    }
}
