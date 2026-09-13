using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class LocalReviewedConceptWriterTests
{
    [Fact]
    public async Task WriteAsync_AbsentTargetCreatesCanonicalArtifactAtomically()
    {
        using var fixture = new TemporaryDirectory();
        var catalog = ReviewedConceptTestData.Catalog();
        var writer = new LocalReviewedConceptWriter();

        var result = await writer.WriteAsync(
            fixture.Path, catalog, null, CompletedGuard);

        Assert.Equal(ReviewedConceptWriteOutcome.Created, result.Outcome);
        Assert.Equal(ReviewedConceptSerializer.Serialize(catalog), await File.ReadAllTextAsync(result.Path));
        Assert.Equal(ReviewedConceptSerializer.CreateCatalogFingerprint(catalog), result.Fingerprint);
        Assert.Empty(TemporaryFiles(result.Path));
    }

    [Fact]
    public async Task WriteAsync_ExistingExpectedTargetUpdatesAtomically()
    {
        using var fixture = new TemporaryDirectory();
        var store = new LocalReviewedConceptStore();
        var original = ReviewedConceptTestData.Catalog();
        var replacement = original with { VocabularyVersion = "v2-updated" };
        await SeedAsync(store.GetPath(fixture.Path), original);

        var result = await new LocalReviewedConceptWriter().WriteAsync(
            fixture.Path,
            replacement,
            ReviewedConceptSerializer.CreateCatalogFingerprint(original),
            CompletedGuard);

        Assert.Equal(ReviewedConceptWriteOutcome.Updated, result.Outcome);
        Assert.Equal(ReviewedConceptSerializer.Serialize(replacement), await File.ReadAllTextAsync(result.Path));
    }

    [Fact]
    public async Task WriteAsync_IdenticalContentReturnsUnchangedWithoutTimestampChurn()
    {
        using var fixture = new TemporaryDirectory();
        var store = new LocalReviewedConceptStore();
        var catalog = ReviewedConceptTestData.Catalog();
        var path = store.GetPath(fixture.Path);
        await SeedAsync(path, catalog);
        var before = File.GetLastWriteTimeUtc(path);

        var result = await new LocalReviewedConceptWriter().WriteAsync(
            fixture.Path,
            catalog,
            ReviewedConceptSerializer.CreateCatalogFingerprint(catalog),
            CompletedGuard);

        Assert.Equal(ReviewedConceptWriteOutcome.Unchanged, result.Outcome);
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
        Assert.Empty(TemporaryFiles(path));
    }

    [Fact]
    public async Task WriteAsync_UnexpectedExistingTargetThrowsConflictAndPreservesBytes()
    {
        using var fixture = new TemporaryDirectory();
        var path = new LocalReviewedConceptStore().GetPath(fixture.Path);
        await SeedAsync(path, ReviewedConceptTestData.Catalog());
        var before = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<ReviewedConceptWriteConflictException>(() =>
            new LocalReviewedConceptWriter().WriteAsync(
                fixture.Path, ChangedCatalog("unexpected"), null, CompletedGuard));

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task WriteAsync_ExpectedFingerprintMismatchThrowsConflictAndPreservesBytes()
    {
        using var fixture = new TemporaryDirectory();
        var path = new LocalReviewedConceptStore().GetPath(fixture.Path);
        await SeedAsync(path, ReviewedConceptTestData.Catalog());
        var before = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<ReviewedConceptWriteConflictException>(() =>
            new LocalReviewedConceptWriter().WriteAsync(
                fixture.Path, ChangedCatalog("mismatch"), "wrong", CompletedGuard));

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task WriteAsync_HeldSiblingLockTimesOutAndPreservesTarget()
    {
        using var fixture = new TemporaryDirectory();
        var path = new LocalReviewedConceptStore().GetPath(fixture.Path);
        await SeedAsync(path, ReviewedConceptTestData.Catalog());
        var before = await File.ReadAllBytesAsync(path);
        await using var held = new FileStream(
            path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var writer = new LocalReviewedConceptWriter(
            lockTimeout: TimeSpan.FromMilliseconds(100),
            lockRetryDelay: TimeSpan.FromMilliseconds(10));

        await Assert.ThrowsAsync<ReviewedConceptWriteConflictException>(() =>
            writer.WriteAsync(
                fixture.Path,
                ChangedCatalog("locked"),
                ReviewedConceptSerializer.CreateCatalogFingerprint(ReviewedConceptTestData.Catalog()),
                CompletedGuard));

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task WriteAsync_ConcurrentCooperatingWritersAllowOneWinnerAndOneConflict()
    {
        using var fixture = new TemporaryDirectory();
        var original = ReviewedConceptTestData.Catalog();
        var path = new LocalReviewedConceptStore().GetPath(fixture.Path);
        await SeedAsync(path, original);
        var expected = ReviewedConceptSerializer.CreateCatalogFingerprint(original);
        var writer = new LocalReviewedConceptWriter();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = writer.WriteAsync(fixture.Path, ChangedCatalog("winner"), expected, async _ =>
        {
            entered.SetResult();
            await release.Task;
        });
        await entered.Task;
        var second = writer.WriteAsync(fixture.Path, ChangedCatalog("loser"), expected, CompletedGuard);
        release.SetResult();

        var outcomes = await Task.WhenAll(CaptureAsync(first), CaptureAsync(second));

        Assert.Single(outcomes, item => item.Result?.Outcome == ReviewedConceptWriteOutcome.Updated);
        Assert.Single(outcomes, item => item.Exception is ReviewedConceptWriteConflictException);
        Assert.Equal(ReviewedConceptSerializer.Serialize(ChangedCatalog("winner")), await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WriteAsync_ExceptionReleasesLockForNextWriter()
    {
        using var fixture = new TemporaryDirectory();
        var writer = new LocalReviewedConceptWriter();

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(
            fixture.Path,
            ReviewedConceptTestData.Catalog(),
            null,
            static _ => throw new InvalidOperationException("guard failed")));

        var result = await writer.WriteAsync(
            fixture.Path, ReviewedConceptTestData.Catalog(), null, CompletedGuard);
        Assert.Equal(ReviewedConceptWriteOutcome.Created, result.Outcome);
    }

    [Fact]
    public async Task WriteAsync_CancellationCleansOwnTemporaryFileAndPreservesTarget()
    {
        using var fixture = new TemporaryDirectory();
        var path = new LocalReviewedConceptStore().GetPath(fixture.Path);
        var original = ReviewedConceptTestData.Catalog();
        await SeedAsync(path, original);
        var before = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new LocalReviewedConceptWriter().WriteAsync(
                fixture.Path,
                ChangedCatalog("cancelled"),
                ReviewedConceptSerializer.CreateCatalogFingerprint(original),
                token =>
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                cancellation.Token));

        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Empty(TemporaryFiles(path));
    }

    [Fact]
    public async Task WriteAsync_ThrowingGuardRunsBeforeMoveAndPreservesTarget()
    {
        using var fixture = new TemporaryDirectory();
        var path = new LocalReviewedConceptStore().GetPath(fixture.Path);
        var original = ReviewedConceptTestData.Catalog();
        await SeedAsync(path, original);
        var before = await File.ReadAllBytesAsync(path);
        var guardCalled = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LocalReviewedConceptWriter().WriteAsync(
                fixture.Path,
                ChangedCatalog("guarded"),
                ReviewedConceptSerializer.CreateCatalogFingerprint(original),
                _ =>
                {
                    guardCalled = true;
                    Assert.True(File.Exists(path + ".lock"));
                    throw new InvalidOperationException("blocked");
                }));

        Assert.True(guardCalled);
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Empty(TemporaryFiles(path));
    }

    [Fact]
    public async Task WriteAsync_NonCooperatingMutationDuringGuardThrowsConflictAndPreservesWinner()
    {
        using var fixture = new TemporaryDirectory();
        var path = new LocalReviewedConceptStore().GetPath(fixture.Path);
        var original = ReviewedConceptTestData.Catalog();
        await SeedAsync(path, original);
        const string winner = "{\"winner\":true}";

        await Assert.ThrowsAsync<ReviewedConceptWriteConflictException>(() =>
            new LocalReviewedConceptWriter().WriteAsync(
                fixture.Path,
                ChangedCatalog("replacement"),
                ReviewedConceptSerializer.CreateCatalogFingerprint(original),
                async token => await File.WriteAllTextAsync(path, winner, token)));

        Assert.Equal(winner, await File.ReadAllTextAsync(path));
        Assert.Empty(TemporaryFiles(path));
        await using var releasedLock = new FileStream(
            path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task WriteAsync_RejectsEscapingBranchLocation()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            new LocalReviewedConceptWriter().WriteAsync(
                " ", ReviewedConceptTestData.Catalog(), null, CompletedGuard));
    }

    private static Task CompletedGuard(CancellationToken _) => Task.CompletedTask;

    private static ReviewedConceptCatalog ChangedCatalog(string value) =>
        ReviewedConceptTestData.Catalog() with { VocabularyVersion = value };

    private static async Task SeedAsync(string path, ReviewedConceptCatalog catalog)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, ReviewedConceptSerializer.Serialize(catalog));
    }

    private static string[] TemporaryFiles(string path) =>
        Directory.Exists(Path.GetDirectoryName(path))
            ? Directory.GetFiles(Path.GetDirectoryName(path)!, ".*.tmp")
            : [];

    private static async Task<(ReviewedConceptWriteResult? Result, Exception? Exception)> CaptureAsync(
        Task<ReviewedConceptWriteResult> task)
    {
        try
        {
            return (await task, null);
        }
        catch (Exception exception)
        {
            return (null, exception);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"engineering-brain-reviewed-writer-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
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
