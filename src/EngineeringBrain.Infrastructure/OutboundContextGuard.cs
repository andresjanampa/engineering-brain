using System.Text.Json;
using System.Text.RegularExpressions;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed partial class OutboundContextGuard
{
    private const int MaximumFindingCount = 99;

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

    private readonly HashSet<string> _secretValues;

    public OutboundContextGuard(IOutboundSecretValueSource? secretValues = null)
    {
        _secretValues = secretValues is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(secretValues.GetValues(), StringComparer.Ordinal);
    }

    public IReadOnlyList<OutboundInspectionFinding> Inspect(
        ReasoningRequest request,
        InitiativeContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var findings = new Dictionary<PolicyContentScope, FindingAccumulator>();
        var segmentFindings = new Dictionary<PolicyContentScope, FindingAccumulator>();

        try
        {
            if (context is not null)
            {
                InspectContext(request, context, findings, segmentFindings);
            }

            InspectText(request.SystemInstructions, findings);
            InspectText(request.UserData, findings);
            MergeMissing(findings, segmentFindings);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            findings.Clear();
            Add(
                findings,
                PolicyContentScope.CompleteRepository,
                OutboundInspectionReasonCode.InspectionFailure,
                OutboundTriggerKind.OperationalFailure,
                1);
        }

        return SystemSecurityPolicyCatalog.Definitions
            .Where(definition => findings.ContainsKey(definition.Category))
            .Select(definition => findings[definition.Category].ToFinding(definition.Category))
            .ToArray();
    }

    public OutboundValidationResult ValidateInitiative(string initiative)
    {
        ArgumentNullException.ThrowIfNull(initiative);
        return ToLegacyResult(Inspect(new ReasoningRequest(
            ReasoningStage.InitiativeUnderstanding,
            string.Empty,
            string.Empty,
            initiative,
            0,
            0)), LooksLikeLegacyRawSnapshot(initiative));
    }

    public OutboundValidationResult Validate(InitiativeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ToLegacyResult(Inspect(new ReasoningRequest(
            ReasoningStage.ArchitectureAnalysis,
            string.Empty,
            string.Empty,
            context.Content,
            0,
            context.EstimatedTokens), context),
            LooksLikeLegacyRawSnapshot(context.Content)
                || context.Segments.Any(segment => LooksLikeLegacyRawSnapshot(segment.Content)));
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

    private void InspectContext(
        ReasoningRequest request,
        InitiativeContext context,
        Dictionary<PolicyContentScope, FindingAccumulator> findings,
        Dictionary<PolicyContentScope, FindingAccumulator> segmentFindings)
    {
        foreach (var segment in context.Segments)
        {
            switch (segment.Kind)
            {
                case ContextSegmentKind.CompleteRepository:
                    Add(findings, PolicyContentScope.CompleteRepository,
                        OutboundInspectionReasonCode.CompleteRepositoryPayload, OutboundTriggerKind.PayloadKind, 1);
                    break;
                case ContextSegmentKind.RawSnapshot:
                    Add(findings, PolicyContentScope.RawSnapshot,
                        OutboundInspectionReasonCode.RawSnapshotPayload, OutboundTriggerKind.PayloadKind, 1);
                    break;
                case ContextSegmentKind.SourceBody:
                    Add(findings, PolicyContentScope.SourceBodies,
                        OutboundInspectionReasonCode.SourceBodyPayload, OutboundTriggerKind.PayloadKind, 1);
                    break;
                default:
                    if (!AllowedCall2Kinds.Contains(segment.Kind))
                    {
                        Add(findings, PolicyContentScope.SourceBodies,
                            OutboundInspectionReasonCode.InspectionFailure, OutboundTriggerKind.PayloadKind, 1);
                    }
                    break;
            }

            InspectText(segment.Content, segmentFindings);
        }

        var rendered = ContextSegmentRenderer.Render(context.Segments);
        if (!string.Equals(request.UserData, context.Content, StringComparison.Ordinal)
            || !string.Equals(context.Content, rendered, StringComparison.Ordinal))
        {
            Add(
                findings,
                PolicyContentScope.SourceBodies,
                OutboundInspectionReasonCode.ContextRepresentationMismatch,
                OutboundTriggerKind.ContextIntegrity,
                1);
        }

        foreach (var sourceReference in context.IncludedNotePaths)
        {
            if (!IsRepositoryRelativeReference(sourceReference))
            {
                Add(findings, PolicyContentScope.AbsoluteLocalPaths,
                    OutboundInspectionReasonCode.InvalidSourceReference, OutboundTriggerKind.SourceReference, 1);
            }
        }
    }

    private void InspectText(
        string content,
        Dictionary<PolicyContentScope, FindingAccumulator> findings)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (LooksLikeRepositoryEnvelope(content) || RecursiveFilePayloadCount(content) >= 3)
        {
            Add(findings, PolicyContentScope.CompleteRepository,
                OutboundInspectionReasonCode.CompleteRepositoryPayload, OutboundTriggerKind.ContentPattern, 1);
        }

        if (LooksLikeRawSnapshot(content))
        {
            Add(findings, PolicyContentScope.RawSnapshot,
                OutboundInspectionReasonCode.RawSnapshotPayload, OutboundTriggerKind.ContentPattern, 1);
        }

        var sourceBodies = SourceDeclarationBody().Matches(content).Count
            + MethodBody().Matches(content).Count
            + PythonBody().Matches(content).Count;
        Add(findings, PolicyContentScope.SourceBodies,
            OutboundInspectionReasonCode.SourceBodyPayload, OutboundTriggerKind.ContentPattern, sourceBodies);

        var knownSecrets = _secretValues.Sum(secret => CountOrdinal(content, secret));
        Add(findings, PolicyContentScope.Secrets,
            OutboundInspectionReasonCode.KnownSecretValue, OutboundTriggerKind.KnownSecretValue, knownSecrets);

        var secretSyntax = PrivateKey().Matches(content).Count
            + BearerToken().Matches(content).Count
            + SecretAssignment().Matches(content).Count
            + ConnectionStringPassword().Matches(content).Count
            + OpenAIKey().Matches(content).Count;
        Add(findings, PolicyContentScope.Secrets,
            OutboundInspectionReasonCode.SecretSyntax, OutboundTriggerKind.ContentPattern, secretSyntax);

        var absolutePaths = WindowsAbsolutePath().Matches(content).Count
            + UncPath().Matches(content).Count
            + FileUri().Matches(content).Count
            + UnixAbsolutePath().Matches(content).Count;
        Add(findings, PolicyContentScope.AbsoluteLocalPaths,
            OutboundInspectionReasonCode.AbsoluteLocalPath, OutboundTriggerKind.ContentPattern, absolutePaths);
    }

    private static bool LooksLikeRepositoryEnvelope(string content)
    {
        if (!TryParseObject(content, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            var hasRepository = TryGetProperty(root, "repository", out _)
                || TryGetProperty(root, "repositoryId", out _);
            if (!hasRepository
                || !TryGetProperty(root, "files", out var files)
                || files.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            return files.EnumerateArray().Any(file => file.ValueKind == JsonValueKind.Object
                && TryGetProperty(file, "path", out _)
                && TryGetProperty(file, "content", out _));
        }
    }

    private static bool LooksLikeRawSnapshot(string content)
    {
        if (!TryParseObject(content, out var document))
        {
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            return new[] { "schemaVersion", "repository", "git", "projects", "entities", "relations" }
                .All(property => TryGetProperty(root, property, out _));
        }
    }

    private static bool LooksLikeLegacyRawSnapshot(string content) =>
        content.Contains("\"schemaVersion\"", StringComparison.OrdinalIgnoreCase)
        && content.Contains("\"entities\"", StringComparison.OrdinalIgnoreCase)
        && content.Contains("\"relations\"", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseObject(string content, out JsonDocument document)
    {
        try
        {
            document = JsonDocument.Parse(content);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return true;
            }

            document.Dispose();
        }
        catch (JsonException)
        {
        }

        document = null!;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static int RecursiveFilePayloadCount(string content) => FilePayloadBoundary()
        .Matches(content)
        .Select(match => match.Groups[1].Value)
        .Distinct(StringComparer.Ordinal)
        .Count();

    private static int CountOrdinal(string content, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static bool IsRepositoryRelativeReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)
            || Path.IsPathRooted(reference)
            || Uri.TryCreate(reference, UriKind.Absolute, out _))
        {
            return false;
        }

        return !reference.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment.Equals("..", StringComparison.Ordinal));
    }

    private static void Add(
        Dictionary<PolicyContentScope, FindingAccumulator> findings,
        PolicyContentScope category,
        OutboundInspectionReasonCode reasonCode,
        OutboundTriggerKind triggerKind,
        int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (!findings.TryGetValue(category, out var finding))
        {
            finding = new FindingAccumulator(reasonCode, triggerKind);
            findings.Add(category, finding);
        }

        finding.Add(count);
    }

    private static void MergeMissing(
        Dictionary<PolicyContentScope, FindingAccumulator> findings,
        IReadOnlyDictionary<PolicyContentScope, FindingAccumulator> additionalFindings)
    {
        foreach (var (category, finding) in additionalFindings)
        {
            findings.TryAdd(category, finding);
        }
    }

    private static OutboundValidationResult ToLegacyResult(
        IReadOnlyList<OutboundInspectionFinding> findings,
        bool legacyRawSnapshot = false)
    {
        var sourceBodies = Count(PolicyContentScope.SourceBodies);
        var secrets = Count(PolicyContentScope.Secrets);
        var absolutePaths = Count(PolicyContentScope.AbsoluteLocalPaths);
        var rawSnapshots = Math.Min(MaximumFindingCount,
            Count(PolicyContentScope.RawSnapshot) + Count(PolicyContentScope.CompleteRepository));
        if (legacyRawSnapshot)
        {
            rawSnapshots = Math.Max(1, rawSnapshots);
        }
        var diagnostics = new List<string>();
        if (sourceBodies > 0) diagnostics.Add("OUTBOUND_SOURCE_BODY");
        if (secrets > 0) diagnostics.Add("OUTBOUND_SECRET");
        if (absolutePaths > 0) diagnostics.Add("OUTBOUND_ABSOLUTE_PATH");
        if (rawSnapshots > 0) diagnostics.Add("OUTBOUND_RAW_SNAPSHOT");
        return new OutboundValidationResult(
            diagnostics.Count == 0,
            sourceBodies,
            secrets,
            absolutePaths,
            rawSnapshots,
            diagnostics);

        int Count(PolicyContentScope category) => findings
            .Where(finding => finding.Category == category)
            .Sum(finding => finding.FindingCount);
    }

    private sealed class FindingAccumulator(
        OutboundInspectionReasonCode reasonCode,
        OutboundTriggerKind triggerKind)
    {
        private int _count;
        private bool _capped;

        public void Add(int count)
        {
            if (_count + count > MaximumFindingCount)
            {
                _count = MaximumFindingCount;
                _capped = true;
                return;
            }

            _count += count;
        }

        public OutboundInspectionFinding ToFinding(PolicyContentScope category) => new(
            category,
            reasonCode,
            triggerKind,
            _count,
            _capped);
    }

    [GeneratedRegex(@"[A-Za-z]:[\\/]", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsAbsolutePath();

    [GeneratedRegex("""(?:^|[\s`'"])(?:\\\\|//)[^\\/\s]+[\\/][^\s]+""", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex UncPath();

    [GeneratedRegex(@"\bfile://[^\s]+", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex FileUri();

    [GeneratedRegex("""(?:^|[\s`'"])/(?:home|Users|tmp|var|etc|opt|srv|workspace|mnt|Volumes)(?:/|\b)""", RegexOptions.CultureInvariant | RegexOptions.Multiline)]
    private static partial Regex UnixAbsolutePath();

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

    [GeneratedRegex(@"(?m)^\s*(?:class|def)\s+\w+[^\r\n]*:\s*\r?\n[ \t]+(?!#)\S", RegexOptions.CultureInvariant)]
    private static partial Regex PythonBody();

    [GeneratedRegex(@"(?ms)^BEGIN_FILE\s+([^\r\n]+)\r?\n.*?^END_FILE\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex FilePayloadBoundary();
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
