using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class AnalysisEvidenceValidatorTests
{
    [Fact]
    public void Validate_ValidEntityEvidenceIsAcceptedForReuse()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var recommendation = InitiativeAnalysisTestData.Recommendation(
            RecommendationDecision.Reuse,
            [InitiativeAnalysisTestData.EntityEvidence(snapshot, "entity:service")]);

        var result = new AnalysisEvidenceValidator().Validate(recommendation, snapshot);

        Assert.Equal(EvidenceValidationStatus.Validated, result.ValidationStatus);
    }

    [Fact]
    public void Validate_NonexistentEntityIsRejected()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var evidence = InitiativeAnalysisTestData.EntityEvidence(snapshot, "entity:service") with { EntityId = "fabricated" };

        var result = Validate(RecommendationDecision.Reuse, evidence, snapshot);

        Assert.Equal(EvidenceValidationStatus.Invalid, result.ValidationStatus);
    }

    [Fact]
    public void Validate_MismatchingPathIsRejectedForExtend()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var evidence = InitiativeAnalysisTestData.EntityEvidence(snapshot, "entity:service") with { RelativePath = "src/Fake.cs" };

        var result = Validate(RecommendationDecision.Extend, evidence, snapshot);

        Assert.Equal(EvidenceValidationStatus.Invalid, result.ValidationStatus);
    }

    [Fact]
    public void Validate_ValidRelationIsAccepted()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var relation = snapshot.Relations.Single(item => item.SourceEntityId == "entity:service"
            && item.RelationType == CodeRelationType.Implements);
        var evidence = RelationEvidence(snapshot, relation);
        var entity = InitiativeAnalysisTestData.EntityEvidence(snapshot, "entity:service");

        var result = new AnalysisEvidenceValidator().Validate(
            InitiativeAnalysisTestData.Recommendation(RecommendationDecision.Reuse, [entity, evidence]),
            snapshot);

        Assert.Equal(EvidenceValidationStatus.Validated, result.ValidationStatus);
        Assert.Contains(result.ValidEvidence, item => item.Kind == EvidenceKind.Relation);
    }

    [Fact]
    public void Validate_FabricatedRelationIsRejectedAndPartialIsExplicit()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var relation = snapshot.Relations.Single(item => item.SourceEntityId == "entity:service"
            && item.RelationType == CodeRelationType.Implements);
        var fabricated = RelationEvidence(snapshot, relation) with { TargetEntityId = "fabricated" };
        var entity = InitiativeAnalysisTestData.EntityEvidence(snapshot, "entity:service");

        var result = new AnalysisEvidenceValidator().Validate(
            InitiativeAnalysisTestData.Recommendation(RecommendationDecision.Reuse, [entity, fabricated]),
            snapshot);

        Assert.Equal(EvidenceValidationStatus.PartiallyValidated, result.ValidationStatus);
    }

    [Theory]
    [InlineData(RecommendationDecision.Reuse)]
    [InlineData(RecommendationDecision.Extend)]
    public void Validate_ExistingDecisionsWithoutEntityAreRejected(RecommendationDecision decision)
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var project = new EvidenceReference(
            EvidenceKind.Project,
            snapshot.Repository.Id,
            snapshot.Git.Branch!,
            null,
            "project:core",
            null,
            null,
            null,
            "src/Core/Core.csproj",
            null,
            null,
            ResolutionLevel.Exact);

        var result = Validate(decision, project, snapshot);

        Assert.Equal(EvidenceValidationStatus.PartiallyValidated, result.ValidationStatus);
    }

    [Fact]
    public void Validate_CreateWithoutNewEntityIdIsAcceptedAsProposal()
    {
        var snapshot = ProjectMemoryTestFactory.Create();

        var result = new AnalysisEvidenceValidator().Validate(
            InitiativeAnalysisTestData.Recommendation(RecommendationDecision.Create, []),
            snapshot);

        Assert.Equal(EvidenceValidationStatus.Proposal, result.ValidationStatus);
    }

    [Fact]
    public void Validate_CreateWithFabricatedEvidenceIsNotPresentedAsValidatedProposal()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var evidence = InitiativeAnalysisTestData.EntityEvidence(snapshot, "entity:service") with { EntityId = "future:not-real" };

        var result = Validate(RecommendationDecision.Create, evidence, snapshot);

        Assert.Equal(EvidenceValidationStatus.Invalid, result.ValidationStatus);
        Assert.NotEmpty(result.ValidationDiagnostics);
    }

    [Fact]
    public void Validate_AvoidModifyingFabricatedComponentIsRejected()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var evidence = InitiativeAnalysisTestData.EntityEvidence(snapshot, "entity:service") with { EntityId = "fabricated" };

        var result = Validate(RecommendationDecision.AvoidModifying, evidence, snapshot);

        Assert.Equal(EvidenceValidationStatus.Invalid, result.ValidationStatus);
    }

    [Fact]
    public void Validate_EvidenceFromDifferentBranchIsRejected()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var evidence = InitiativeAnalysisTestData.EntityEvidence(snapshot, "entity:service") with { Branch = "other" };

        var result = Validate(RecommendationDecision.Reuse, evidence, snapshot);

        Assert.Equal(EvidenceValidationStatus.Invalid, result.ValidationStatus);
    }

    private static ValidatedRecommendation Validate(
        RecommendationDecision decision,
        EvidenceReference evidence,
        RepositorySnapshot snapshot) => new AnalysisEvidenceValidator().Validate(
        InitiativeAnalysisTestData.Recommendation(decision, [evidence]),
        snapshot);

    private static EvidenceReference RelationEvidence(RepositorySnapshot snapshot, CodeRelation relation) => new(
        EvidenceKind.Relation,
        snapshot.Repository.Id,
        snapshot.Git.Branch!,
        null,
        snapshot.Entities.Single(item => item.Id == relation.SourceEntityId).ProjectId,
        relation.SourceEntityId,
        relation.TargetEntityId,
        relation.RelationType,
        relation.RelativeFilePath,
        relation.StartLine,
        relation.EndLine,
        relation.ResolutionLevel);
}
