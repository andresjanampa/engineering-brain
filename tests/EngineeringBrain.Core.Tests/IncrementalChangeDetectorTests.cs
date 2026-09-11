using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class IncrementalChangeDetectorTests
{
    private readonly IncrementalChangeDetector _detector = new();

    [Fact]
    public void Detect_UnchangedHashProducesNoChanges()
    {
        var previous = File("src/A.cs", "same", 1);
        var current = File("src/A.cs", "same", 2);

        Assert.Empty(_detector.Detect([previous], [current]));
    }

    [Fact]
    public void Detect_ChangedHashProducesModified()
    {
        var changes = _detector.Detect(
            [File("src/A.cs", "before", 1)],
            [File("src/A.cs", "after", 1)]);

        var change = Assert.Single(changes);
        Assert.Equal(FileChangeKind.Modified, change.Kind);
        Assert.Equal(ChangeDetectionMethod.ContentHash, change.DetectionMethod);
    }

    [Fact]
    public void Detect_FindsAddedAndDeletedWithoutGit()
    {
        var changes = _detector.Detect(
            [File("src/Deleted.cs", "old", 1)],
            [File("src/Added.cs", "new", 1)]);

        Assert.Contains(changes, change => change.Kind == FileChangeKind.Added);
        Assert.Contains(changes, change => change.Kind == FileChangeKind.Deleted);
    }

    [Fact]
    public void Detect_UsesMetadataWithoutHashingSensitiveContent()
    {
        var changes = _detector.Detect(
            [File(".env", null, 1, 10)],
            [File(".env", null, 2, 10)]);

        var change = Assert.Single(changes);
        Assert.Equal(ChangeDetectionMethod.FileMetadata, change.DetectionMethod);
    }

    [Fact]
    public void Detect_RepresentsRenameOnlyWhenGitProvesIt()
    {
        var changes = _detector.Detect(
            [File("src/Old.cs", "same", 1)],
            [File("src/New.cs", "same", 2)],
            [new GitRename("src/Old.cs", "src/New.cs")]);

        var change = Assert.Single(changes);
        Assert.Equal(FileChangeKind.Renamed, change.Kind);
        Assert.Equal(ChangeDetectionMethod.Git, change.DetectionMethod);
        Assert.Equal("src/Old.cs", change.PreviousPath);
        Assert.Equal("src/New.cs", change.CurrentPath);
    }

    private static ScannedFile File(
        string path,
        string? hash,
        int timestampSeconds,
        long size = 1) => new(
        path,
        Path.GetExtension(path),
        "C#",
        size,
        hash,
        DateTimeOffset.UnixEpoch.AddSeconds(timestampSeconds));
}
