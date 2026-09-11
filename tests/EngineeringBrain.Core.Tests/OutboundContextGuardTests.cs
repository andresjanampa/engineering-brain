using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class OutboundContextGuardTests
{
    private readonly OutboundContextGuard _guard = new();

    [Fact]
    public void ObviousSecretIsDetected() => Assert.Equal(1, _guard.Validate(Context(ContextSegmentKind.ProjectNote, "api_key=fake-secret-value")).SecretFindings);

    [Fact]
    public void SecretValueIsNotEchoed()
    {
        var value = "api_key=fake-secret-value";
        var exception = Assert.Throws<InvalidDataException>(() => _guard.ThrowIfInvalid(_guard.Validate(Context(ContextSegmentKind.ProjectNote, value))));
        Assert.DoesNotContain(value, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowsAbsolutePathIsDetected() => Assert.Equal(1, _guard.Validate(Context(ContextSegmentKind.ProjectNote, @"C:\Users\name\repo\file.cs")).AbsolutePathFindings);

    [Fact]
    public void UnixHomePathIsDetected() => Assert.Equal(1, _guard.Validate(Context(ContextSegmentKind.ProjectNote, "/home/name/repo/file.cs")).AbsolutePathFindings);

    [Fact]
    public void RelativeSourcePathIsAccepted() => Assert.True(_guard.Validate(Context(ContextSegmentKind.GraphEvidence, "src/Core/File.cs:1")).IsValid);

    [Fact]
    public void AllowedProjectMemoryIsAccepted() => Assert.True(_guard.Validate(Context(ContextSegmentKind.ComponentNote, "# Component\nSource: `src/Core/File.cs`" )).IsValid);

    [Fact]
    public void SourceBodySegmentIsRejected() => Assert.Equal(1, _guard.Validate(Context(ContextSegmentKind.SourceBody, "signature only")).SourceBodyFindings);

    [Fact]
    public void RawSnapshotIsRejected() => Assert.Equal(1, _guard.Validate(Context(ContextSegmentKind.RawSnapshot, "{}" )).RawSnapshotFindings);

    [Fact]
    public void SnapshotShapedJsonIsRejected() => Assert.Equal(1, _guard.Validate(Context(ContextSegmentKind.ProjectNote, "{\"schemaVersion\":3,\"entities\":[],\"relations\":[]}" )).RawSnapshotFindings);

    [Fact]
    public void NormalTypedContextPasses() => Assert.True(_guard.Validate(Context(ContextSegmentKind.RepositoryIdentity, "Repository: safe\nBranch: feature" )).IsValid);

    [Fact]
    public void InitiativeAbsolutePathAndSourceBodyAreRejected()
    {
        var result = _guard.ValidateInitiative("Inspect C:\\Users\\name\\repo and public class Leaked {");
        Assert.Equal(1, result.AbsolutePathFindings);
        Assert.Equal(1, result.SourceBodyFindings);
    }

    [Fact]
    public void UntypedPayloadContentIsRejected()
    {
        var segment = new ContextSegment(ContextSegmentKind.RepositoryIdentity, "safe", 1, "test", 1, true);
        var context = new InitiativeContext("unrepresented payload", 1, [], [], [segment]);
        Assert.Equal(1, _guard.Validate(context).SourceBodyFindings);
    }

    private static InitiativeContext Context(ContextSegmentKind kind, string content)
    {
        var segments = new[] { new ContextSegment(kind, content, 1, "test", 1, true) };
        return new InitiativeContext(ContextSegmentRenderer.Render(segments), 1, [], [], segments);
    }
}
