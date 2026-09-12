using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ConceptCandidateRerankerTests
{
    [Fact]
    public void Rerank_ClearAnchorRequiresQualificationSupport()
    {
        var candidate = ReviewedConceptTestData.Candidate("entity:store", score: 26);
        var profile = ReviewedConceptTestData.PersistenceProfile(candidate.EntityId);

        var result = new ConceptCandidateReranker().Rerank(
            [candidate], ["analysis", "initiative"], [profile]);

        Assert.Equal(26, result[0].Score);
        Assert.DoesNotContain(result[0].MatchReasons, reason => reason.Signal == "reviewed concept");
    }

    [Fact]
    public void Rerank_ContextSupportContributesOnlyAfterValidQualification()
    {
        var candidate = ReviewedConceptTestData.Candidate("entity:store", score: 26);
        var profile = ReviewedConceptTestData.PersistenceProfile(candidate.EntityId);

        var result = new ConceptCandidateReranker().Rerank(
            [candidate], ["analysis", "initiative", "persistence"], [profile]);

        Assert.Equal(32, result[0].Score);
        Assert.Contains(result[0].MatchReasons,
            reason => reason.Signal == "reviewed concept" && reason.Points == 6);
    }

    [Theory]
    [InlineData("retry", "retry", true)]
    [InlineData("offline-evaluation", "offline", false)]
    [InlineData("offline-evaluation", "offline evaluation", true)]
    [InlineData("incremental-change-detection", "incremental change", true)]
    [InlineData("incremental-change-detection", "incremental", false)]
    [InlineData("outbound-context-safety-guard", "outbound context safety", true)]
    [InlineData("outbound-context-safety-guard", "outbound context", false)]
    public void Rerank_P1CoverageUsesFrozenThresholds(string conceptId, string query, bool qualifies)
    {
        var candidate = ReviewedConceptTestData.Candidate("entity:component", 10);
        var profile = ReviewedConceptTestData.Profile(candidate.EntityId,
            ReviewedConceptTestData.Concept(conceptId));

        var result = new ConceptCandidateReranker().Rerank(
            [candidate], new InitiativeTermNormalizer().Tokenize(query), [profile]);

        Assert.Equal(qualifies, result[0].Score > candidate.Score);
    }

    [Fact]
    public void Rerank_ClearPolicyAcceptsAnyCompleteAnchorGroup()
    {
        var candidate = ReviewedConceptTestData.Candidate("entity:component", 10);
        var concept = ReviewedConceptTestData.Concept(
            "remote-provider-boundary",
            ReviewedConceptAnchorPolicy.Clear,
            [["remote", "provider"], ["boundary"]],
            ["provider"]);

        var result = new ConceptCandidateReranker().Rerank(
            [candidate], ["boundary", "provider", "remote"],
            [ReviewedConceptTestData.Profile(candidate.EntityId, concept)]);

        Assert.Equal(16, result[0].Score);
    }

    [Theory]
    [InlineData(ReviewedConceptAnchorPolicy.Ambiguous)]
    [InlineData(ReviewedConceptAnchorPolicy.NotRequired)]
    public void Rerank_NonClearPoliciesUseP1Only(ReviewedConceptAnchorPolicy policy)
    {
        var candidate = ReviewedConceptTestData.Candidate("entity:component", 10);
        var concept = ReviewedConceptTestData.Concept("bounded-retry", policy);

        var result = new ConceptCandidateReranker().Rerank(
            [candidate], ["bounded", "retry"],
            [ReviewedConceptTestData.Profile(candidate.EntityId, concept)]);

        Assert.Equal(14, result[0].Score);
    }

    [Fact]
    public void Rerank_AggregateConceptContributionIsCappedAndDuplicatesDoNotCountTwice()
    {
        var candidate = ReviewedConceptTestData.Candidate("entity:component", 10);
        var first = ReviewedConceptTestData.Concept("outbound-context-safety");
        var second = ReviewedConceptTestData.Concept("remote-provider-boundary");
        var profile = ReviewedConceptTestData.Profile(candidate.EntityId, first, first, second);

        var result = new ConceptCandidateReranker().Rerank(
            [candidate], ["boundary", "context", "outbound", "provider", "remote", "safety"],
            [profile, profile]);

        Assert.Equal(16, result[0].Score);
        Assert.Equal(6, result[0].MatchReasons
            .Where(reason => reason.Signal == "reviewed concept").Sum(reason => reason.Points));
    }

    [Fact]
    public void Rerank_E2PreventsConceptFromReversingStrongerExactIdentity()
    {
        var exact = ReviewedConceptTestData.Candidate(
            "entity:exact",
            48,
            "ExactComponent",
            [new MatchReason("exact full name", "Demo.ExactComponent", 48)]);
        var semantic = ReviewedConceptTestData.Candidate("entity:semantic", 46);
        var profile = ReviewedConceptTestData.Profile(
            semantic.EntityId,
            ReviewedConceptTestData.Concept("semantic-boundary"));

        var result = new ConceptCandidateReranker().Rerank(
            [exact, semantic], ["boundary", "semantic"], [profile]);

        Assert.Equal([exact.EntityId, semantic.EntityId], result.Select(item => item.EntityId));
        Assert.Equal(50, result[1].Score);
    }

    [Fact]
    public void Rerank_E2PreservesLexicalOrderBetweenEqualExactIdentityCandidates()
    {
        var service = ReviewedConceptTestData.Candidate(
            "entity:service",
            36,
            "ProjectMemoryService",
            [new MatchReason("exact component name", "ProjectMemoryService", 32)]);
        var validator = ReviewedConceptTestData.Candidate(
            "entity:validator",
            36,
            "ProjectMemoryValidator",
            [new MatchReason("exact component name", "ProjectMemoryValidator", 32)]);
        var profile = ReviewedConceptTestData.Profile(
            validator.EntityId,
            ReviewedConceptTestData.Concept("project-memory-integrity-validation"));

        var result = new ConceptCandidateReranker().Rerank(
            [service, validator],
            ["integrity", "memory", "project", "validation"],
            [profile]);

        Assert.Equal([service.EntityId, validator.EntityId], result.Select(item => item.EntityId));
        Assert.True(result[1].Score > result[0].Score);
    }

    [Fact]
    public void Rerank_StableIdentityBreaksEqualFinalScoreTies()
    {
        var beta = ReviewedConceptTestData.Candidate("entity:beta", 10, "Beta");
        var alpha = ReviewedConceptTestData.Candidate("entity:alpha", 10, "Alpha");
        var concept = ReviewedConceptTestData.Concept("stable-signal");

        var result = new ConceptCandidateReranker().Rerank(
            [beta, alpha],
            ["signal", "stable"],
            [
                ReviewedConceptTestData.Profile(beta.EntityId, concept),
                ReviewedConceptTestData.Profile(alpha.EntityId, concept)
            ]);

        Assert.Equal(["Demo.Alpha", "Demo.Beta"], result.Select(item => item.FullName));
    }

    [Fact]
    public void Rerank_EmptyProfilesPreservesCandidateValues()
    {
        var candidates = new[]
        {
            ReviewedConceptTestData.Candidate("entity:first", 12),
            ReviewedConceptTestData.Candidate("entity:second", 10)
        };

        var result = new ConceptCandidateReranker().Rerank(candidates, ["analysis"], []);

        Assert.Equal(candidates, result);
    }
}
