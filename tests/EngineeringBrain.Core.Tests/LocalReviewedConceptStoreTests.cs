using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class LocalReviewedConceptStoreTests
{
    [Fact]
    public async Task LoadAsync_AbsentArtifactReturnsAbsentWithoutCreatingDirectories()
    {
        using var fixture = new TemporaryDirectory(create: false);
        var branchRoot = Path.Combine(fixture.Path, "branch");

        var result = await new LocalReviewedConceptStore().LoadAsync(branchRoot);

        Assert.Equal(ReviewedConceptLoadStatus.Absent, result.Status);
        Assert.False(Directory.Exists(branchRoot));
    }

    [Fact]
    public async Task LoadAsync_MalformedArtifactReturnsInvalidSafeDiagnostic()
    {
        using var fixture = new TemporaryDirectory();
        var store = new LocalReviewedConceptStore();
        var path = store.GetPath(fixture.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{not-json");

        var result = await store.LoadAsync(fixture.Path);

        Assert.Equal(ReviewedConceptLoadStatus.Invalid, result.Status);
        Assert.Null(result.Catalog);
        Assert.Equal(KnowledgeIdentity.ContentHash("{not-json"), result.ContentHash);
        Assert.Single(result.Diagnostics, item => item.Code == "RC001");
        Assert.DoesNotContain("not-json", result.Diagnostics[0].Message, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Path, result.Diagnostics[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"repositoryId\":null,\"branch\":\"main\",\"branchKey\":\"main--key\",\"sourceSnapshotSchema\":3,\"sourceAnalyzerVersion\":\"analyzer\",\"vocabularyVersion\":\"v2\",\"declarations\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"repositoryId\":\"repository\",\"branch\":null,\"branchKey\":\"main--key\",\"sourceSnapshotSchema\":3,\"sourceAnalyzerVersion\":\"analyzer\",\"vocabularyVersion\":\"v2\",\"declarations\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"repositoryId\":\"repository\",\"branch\":\"main\",\"branchKey\":\"main--key\",\"sourceSnapshotSchema\":3,\"sourceAnalyzerVersion\":\"analyzer\",\"vocabularyVersion\":\"v2\",\"declarations\":null}")]
    [InlineData("{\"schemaVersion\":1,\"repositoryId\":\"repository\",\"branch\":\"main\",\"branchKey\":\"main--key\",\"sourceSnapshotSchema\":3,\"sourceAnalyzerVersion\":\"analyzer\",\"vocabularyVersion\":\"v2\",\"declarations\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"repositoryId\":\"repository\",\"branch\":\"main\",\"branchKey\":\"main--key\",\"sourceSnapshotSchema\":3,\"sourceAnalyzerVersion\":\"analyzer\",\"vocabularyVersion\":\"v2\",\"declarations\":[{\"conceptId\":\"incomplete\"}]}")]
    public async Task LoadAsync_StructurallyIncompleteArtifactReturnsInvalid(string json)
    {
        using var fixture = new TemporaryDirectory();
        var store = new LocalReviewedConceptStore();
        var path = store.GetPath(fixture.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, json);

        var result = await store.LoadAsync(fixture.Path);

        Assert.Equal(ReviewedConceptLoadStatus.Invalid, result.Status);
        Assert.Null(result.Catalog);
        Assert.Equal(KnowledgeIdentity.ContentHash(json), result.ContentHash);
        Assert.Single(result.Diagnostics, item => item.Code == "RC001");
    }

    [Fact]
    public async Task LoadAsync_ValidArtifactReturnsCatalogAndContentHash()
    {
        using var fixture = new TemporaryDirectory();
        var store = new LocalReviewedConceptStore();
        var path = store.GetPath(fixture.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = ReviewedConceptSerializer.Serialize(ReviewedConceptTestData.Catalog());
        await File.WriteAllTextAsync(path, json);

        var result = await store.LoadAsync(fixture.Path);

        Assert.Equal(ReviewedConceptLoadStatus.Loaded, result.Status);
        Assert.NotNull(result.Catalog);
        Assert.Equal(KnowledgeIdentity.ContentHash(json), result.ContentHash);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task LoadAsync_CanceledReadPropagatesCancellation()
    {
        using var fixture = new TemporaryDirectory();
        var store = new LocalReviewedConceptStore();
        var path = store.GetPath(fixture.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, ReviewedConceptSerializer.Serialize(ReviewedConceptTestData.Catalog()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.LoadAsync(fixture.Path, cancellation.Token));
    }

    [Fact]
    public void GetPath_RejectsEmptyBranchLocation()
    {
        Assert.Throws<ArgumentException>(() => new LocalReviewedConceptStore().GetPath(" "));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory(bool create = true)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"engineering-brain-reviewed-concepts-{Guid.NewGuid():N}");
            if (create)
            {
                Directory.CreateDirectory(Path);
            }
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
