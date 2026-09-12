using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class ReviewedConceptModelTests
{
    [Fact]
    public void ResolutionResult_AbsentContainsNoProfilesOrDiagnostics()
    {
        var result = ReviewedConceptResolutionResult.Absent;

        Assert.Equal(ReviewedConceptResolutionStatus.Absent, result.Status);
        Assert.Null(result.CatalogFingerprint);
        Assert.Empty(result.Profiles);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void EvidenceContext_FromMemoryUsesOnlyManagedComponentFingerprints()
    {
        var snapshot = ProjectMemoryTestFactory.Create();
        var build = new ProjectMemoryBuilder().Build(snapshot);
        var memory = ReviewedConceptTestData.Memory(snapshot, build.Manifest);

        var evidence = ReviewedConceptEvidenceContext.FromMemory(memory);

        Assert.Equal(snapshot.Repository.Id, evidence.RepositoryId);
        Assert.Equal(snapshot.Git.Branch, evidence.Branch);
        Assert.Equal(build.Manifest.BranchKey, evidence.BranchKey);
        Assert.Equal(
            build.Manifest.Notes.Count(note => note.Kind == KnowledgeNoteKind.Component),
            evidence.Components.Count);
        Assert.All(evidence.Components.Values, item =>
            Assert.False(string.IsNullOrWhiteSpace(item.SourceFingerprint)));
    }
}
