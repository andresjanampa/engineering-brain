using System.Text.RegularExpressions;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed partial class OutboundContextGuard
{
    private static readonly HashSet<ContextSegmentKind> AllowedCall2Kinds =
    [
        ContextSegmentKind.InitiativeUnderstanding,
        ContextSegmentKind.RepositoryIdentity,
        ContextSegmentKind.RootIndex,
        ContextSegmentKind.ArchitectureOverview,
        ContextSegmentKind.ProjectNote,
        ContextSegmentKind.ComponentNote,
        ContextSegmentKind.GraphEvidence
    ];

    public OutboundValidationResult ValidateInitiative(string initiative)
    {
        ArgumentNullException.ThrowIfNull(initiative);
        var secrets = ContainsSecret(initiative) ? 1 : 0;
        var sourceBodies = LooksLikeSourceBody(initiative) ? 1 : 0;
        var absolutePaths = WindowsAbsolutePath().IsMatch(initiative) || UnixHomePath().IsMatch(initiative) ? 1 : 0;
        var rawSnapshots = LooksLikeRawSnapshot(initiative) ? 1 : 0;
        return CreateResult(sourceBodies, secrets, absolutePaths, rawSnapshots);
    }

    public OutboundValidationResult Validate(InitiativeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sourceBodies = 0;
        var secrets = 0;
        var absolutePaths = 0;
        var rawSnapshots = 0;
        foreach (var segment in context.Segments)
        {
            if (!AllowedCall2Kinds.Contains(segment.Kind) || segment.Kind == ContextSegmentKind.SourceBody)
            {
                sourceBodies++;
            }

            if (segment.Kind == ContextSegmentKind.RawSnapshot || LooksLikeRawSnapshot(segment.Content))
            {
                rawSnapshots++;
            }

            if (ContainsSecret(segment.Content))
            {
                secrets++;
            }

            if (WindowsAbsolutePath().IsMatch(segment.Content) || UnixHomePath().IsMatch(segment.Content))
            {
                absolutePaths++;
            }
        }

        if (!context.Content.Equals(ContextSegmentRenderer.Render(context.Segments), StringComparison.Ordinal))
        {
            sourceBodies++;
        }

        secrets = ContainsSecret(context.Content) ? Math.Max(1, secrets) : secrets;
        absolutePaths = WindowsAbsolutePath().IsMatch(context.Content) || UnixHomePath().IsMatch(context.Content)
            ? Math.Max(1, absolutePaths)
            : absolutePaths;
        rawSnapshots = LooksLikeRawSnapshot(context.Content) ? Math.Max(1, rawSnapshots) : rawSnapshots;

        return CreateResult(sourceBodies, secrets, absolutePaths, rawSnapshots);
    }

    public void ThrowIfInvalid(OutboundValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.IsValid)
        {
            throw new InvalidDataException(
                $"Outbound context failed security validation: {string.Join(", ", result.DiagnosticCodes)}. Values were not logged.");
        }
    }

    private static OutboundValidationResult CreateResult(
        int sourceBodies,
        int secrets,
        int absolutePaths,
        int rawSnapshots)
    {
        var diagnostics = new List<string>();
        if (sourceBodies > 0)
        {
            diagnostics.Add("OUTBOUND_SOURCE_BODY");
        }
        if (secrets > 0)
        {
            diagnostics.Add("OUTBOUND_SECRET");
        }
        if (absolutePaths > 0)
        {
            diagnostics.Add("OUTBOUND_ABSOLUTE_PATH");
        }
        if (rawSnapshots > 0)
        {
            diagnostics.Add("OUTBOUND_RAW_SNAPSHOT");
        }

        return new OutboundValidationResult(
            diagnostics.Count == 0,
            sourceBodies,
            secrets,
            absolutePaths,
            rawSnapshots,
            diagnostics);
    }

    private static bool ContainsSecret(string content) =>
        PrivateKey().IsMatch(content)
        || BearerToken().IsMatch(content)
        || SecretAssignment().IsMatch(content)
        || ConnectionStringPassword().IsMatch(content)
        || OpenAIKey().IsMatch(content);

    private static bool LooksLikeRawSnapshot(string content) =>
        content.Contains("\"schemaVersion\"", StringComparison.OrdinalIgnoreCase)
        && content.Contains("\"entities\"", StringComparison.OrdinalIgnoreCase)
        && content.Contains("\"relations\"", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSourceBody(string content) =>
        SourceDeclarationBody().IsMatch(content) || MethodBody().IsMatch(content);

    [GeneratedRegex(@"[A-Za-z]:\\", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsAbsolutePath();

    [GeneratedRegex("""(?:^|[\s`'"])/(?:home|Users)/""", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex UnixHomePath();

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", RegexOptions.CultureInvariant)]
    private static partial Regex PrivateKey();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._-]{12,}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    [GeneratedRegex(@"\b(?:password|pwd|client[_-]?secret|api[_-]?key)\s*[:=]\s*[^\s,;]{6,}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex SecretAssignment();

    [GeneratedRegex(@"\b(?:Server|Data Source)\s*=.*\b(?:Password|Pwd)\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionStringPassword();

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_-]{12,}", RegexOptions.CultureInvariant)]
    private static partial Regex OpenAIKey();

    [GeneratedRegex(@"\b(?:public|private|protected|internal)\s+(?:class|record|interface|enum)\s+\w+[^\r\n]*\{", RegexOptions.CultureInvariant)]
    private static partial Regex SourceDeclarationBody();

    [GeneratedRegex(@"\b(?:public|private|protected|internal)\s+[^\r\n;]+\([^\r\n]*\)\s*\{", RegexOptions.CultureInvariant)]
    private static partial Regex MethodBody();
}

public static class ContextSegmentRenderer
{
    public static string Render(IEnumerable<ContextSegment> segments) => string.Join(
        "\n\n",
        segments.Select(segment => $"BEGIN_{ToMarker(segment.Kind)}\n{segment.Content}\nEND_{ToMarker(segment.Kind)}"));

    private static string ToMarker(ContextSegmentKind kind) => string.Concat(
        kind.ToString().SelectMany((character, index) => char.IsUpper(character) && index > 0
            ? new[] { '_', character }
            : new[] { character })).ToUpperInvariant();
}
