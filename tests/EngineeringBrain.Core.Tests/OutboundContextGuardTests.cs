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

    [Fact]
    public void Inspect_CompleteRepositorySegmentIsBlocked()
    {
        var context = Context(ContextSegmentKind.CompleteRepository, "repository payload");

        AssertFinding(_guard.Inspect(Request(context.Content), context), PolicyContentScope.CompleteRepository);
    }

    [Fact]
    public void Inspect_RepositoryFilesEnvelopeIsBlocked()
    {
        const string payload = """
            {"repository":{"name":"sample"},"files":[{"path":"src/A.cs","content":"content"}]}
            """;

        AssertFinding(_guard.Inspect(Request(payload)), PolicyContentScope.CompleteRepository);
    }

    [Fact]
    public void Inspect_BoundedProjectMemoryAndGraphFactsAreAllowed()
    {
        var context = Context(
            ContextSegmentKind.GraphEvidence,
            "Project: EngineeringBrain.Core\nEntity: RepositorySnapshot\nRelation: Implements");

        Assert.Empty(_guard.Inspect(Request(context.Content), context));
    }

    [Fact]
    public void Inspect_CompleteRawSnapshotObjectIsBlocked()
    {
        const string payload = """
            {"schemaVersion":3,"repository":{},"git":{},"projects":[],"entities":[],"relations":[]}
            """;

        AssertFinding(_guard.Inspect(Request(payload)), PolicyContentScope.RawSnapshot);
    }

    [Fact]
    public void Inspect_SelectedEntityRelationSummaryIsAllowed()
    {
        const string payload = "Entity: RepositoryScanner\nRelation: Implements ILanguageAnalyzer";

        Assert.Empty(_guard.Inspect(Request(payload)));
    }

    [Fact]
    public void Inspect_ExplicitSourceBodyIsBlocked()
    {
        var context = Context(ContextSegmentKind.SourceBody, "public void Run()\n{\n    return;\n}");

        AssertFinding(_guard.Inspect(Request(context.Content), context), PolicyContentScope.SourceBodies);
    }

    [Fact]
    public void Inspect_SignatureAndRelativeReferenceAreAllowed()
    {
        var context = Context(
            ContextSegmentKind.GraphEvidence,
            "Signature: ProcessAsync(string input)\nSource: src/Core/File.cs:12",
            ["src/Core/File.cs"]);

        Assert.Empty(_guard.Inspect(Request(context.Content), context));
    }

    [Fact]
    public void Inspect_KnownSecretValueInSystemInstructionsIsBlockedWithoutRetention()
    {
        const string secret = "test-secret-value-123";
        var guard = new OutboundContextGuard(new FixedSecretValueSource(secret));

        var finding = AssertFinding(
            guard.Inspect(Request("safe user", $"Never expose {secret}")),
            PolicyContentScope.Secrets);

        Assert.Equal(OutboundInspectionReasonCode.KnownSecretValue, finding.ReasonCode);
        Assert.DoesNotContain(secret, finding.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Inspect_HighConfidenceSecretSyntaxIsBlocked()
    {
        AssertFinding(
            _guard.Inspect(Request("api_key=fake-secret-value")),
            PolicyContentScope.Secrets);
    }

    [Fact]
    public void Inspect_CommonNonSecretTextIsAllowed()
    {
        Assert.Empty(_guard.Inspect(Request("Use a tokenizer in the development environment.")));
    }

    [Theory]
    [InlineData("C:\\Users\\person\\repo\\File.cs")]
    [InlineData("\\\\server\\share\\repo\\File.cs")]
    [InlineData("/home/person/repo/File.cs")]
    [InlineData("file:///C:/repo/File.cs")]
    public void Inspect_AbsoluteMachinePathIsBlocked(string value)
    {
        AssertFinding(_guard.Inspect(Request(value)), PolicyContentScope.AbsoluteLocalPaths);
    }

    [Fact]
    public void Inspect_RelativeRepositoryPathIsAllowed()
    {
        Assert.Empty(_guard.Inspect(Request("src/Core/File.cs:12")));
    }

    [Fact]
    public void Inspect_TraversalSourceReferenceIsBlocked()
    {
        var context = Context(ContextSegmentKind.GraphEvidence, "selected evidence", ["../outside/File.cs"]);

        var finding = AssertFinding(
            _guard.Inspect(Request(context.Content), context),
            PolicyContentScope.AbsoluteLocalPaths);
        Assert.Equal(OutboundInspectionReasonCode.InvalidSourceReference, finding.ReasonCode);
    }

    [Fact]
    public void Inspect_ContextContentMismatchFailsClosed()
    {
        var segment = new ContextSegment(ContextSegmentKind.GraphEvidence, "represented", 1, "test", 1, true);
        var context = new InitiativeContext("different", 1, [], [], [segment]);

        var finding = AssertFinding(
            _guard.Inspect(Request("different"), context),
            PolicyContentScope.SourceBodies);
        Assert.Equal(OutboundInspectionReasonCode.ContextRepresentationMismatch, finding.ReasonCode);
    }

    [Fact]
    public void Inspect_DuplicateMatchesAreAggregatedAndCapped()
    {
        var payload = string.Join(' ', Enumerable.Repeat("api_key=fake-secret-value", 120));

        var finding = AssertFinding(_guard.Inspect(Request(payload)), PolicyContentScope.Secrets);

        Assert.Equal(99, finding.FindingCount);
        Assert.True(finding.FindingCountCapped);
    }

    private static InitiativeContext Context(
        ContextSegmentKind kind,
        string content,
        IReadOnlyList<string>? includedNotePaths = null)
    {
        var segments = new[] { new ContextSegment(kind, content, 1, "test", 1, true) };
        return new InitiativeContext(ContextSegmentRenderer.Render(segments), 1, includedNotePaths ?? [], [], segments);
    }

    private static ReasoningRequest Request(string userData, string systemInstructions = "fixed system") => new(
        ReasoningStage.ArchitectureAnalysis,
        "test-model",
        systemInstructions,
        userData,
        500,
        100);

    private static OutboundInspectionFinding AssertFinding(
        IReadOnlyList<OutboundInspectionFinding> findings,
        PolicyContentScope category) => Assert.Single(findings, finding => finding.Category == category);

    private sealed class FixedSecretValueSource(params string[] values) : IOutboundSecretValueSource
    {
        public IReadOnlySet<string> GetValues() => new HashSet<string>(values, StringComparer.Ordinal);
    }
}
