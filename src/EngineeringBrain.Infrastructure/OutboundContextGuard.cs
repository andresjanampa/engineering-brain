using System.Text.Json;
using System.Text.RegularExpressions;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed partial class OutboundContextGuard
{
    private const int MaximumFindingCount = 99;
    private const int MaximumCFamilyBodyLookaheadLines = 64;
    private const int MaximumInspectionCharacters = 1_000_000;
    private const int MaximumJsonCandidates = 32;
    private const int MaximumJsonNestingDepth = 64;
    private const int MaximumJsonNodesAndProperties = 10_000;

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
        var budget = new InspectionBudget();

        try
        {
            if (context is not null)
            {
                InspectContext(request, context, findings, segmentFindings, budget);
            }

            InspectText(request.SystemInstructions, findings, budget);
            InspectText(request.UserData, findings, budget);
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
        Dictionary<PolicyContentScope, FindingAccumulator> segmentFindings,
        InspectionBudget budget)
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

            InspectText(segment.Content, segmentFindings, budget);
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
        Dictionary<PolicyContentScope, FindingAccumulator> findings,
        InspectionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(content);
        budget.ExamineCharacters(content.Length);

        InspectJsonObjects(content, findings, budget);

        if (RecursiveFilePayloadCount(content) >= 3)
        {
            Add(findings, PolicyContentScope.CompleteRepository,
                OutboundInspectionReasonCode.CompleteRepositoryPayload, OutboundTriggerKind.ContentPattern, 1);
        }

        var sourceBodies = CountCFamilyBodies(content)
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

    private static void InspectJsonObjects(
        string content,
        Dictionary<PolicyContentScope, FindingAccumulator> findings,
        InspectionBudget budget)
    {
        for (var start = 0; start < content.Length; start++)
        {
            if (content[start] != '{')
            {
                continue;
            }

            budget.AddJsonCandidate();
            var end = FindJsonObjectEnd(content, start, budget);
            if (end < 0)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(content.AsMemory(start, end - start + 1));
                InspectJsonTree(document.RootElement, findings, budget);
                start = end;
            }
            catch (JsonException)
            {
                start = end;
            }
        }
    }

    private static int FindJsonObjectEnd(string content, int start, InspectionBudget budget)
    {
        Span<char> expectedClosures = stackalloc char[MaximumJsonNestingDepth];
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = start; index < content.Length; index++)
        {
            var character = content[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
                continue;
            }

            if (character is '{' or '[')
            {
                depth++;
                budget.EnsureJsonNestingDepth(depth);
                expectedClosures[depth - 1] = character == '{' ? '}' : ']';
                continue;
            }

            if (character is not ('}' or ']'))
            {
                continue;
            }

            if (depth == 0 || expectedClosures[depth - 1] != character)
            {
                return -1;
            }

            depth--;
            if (depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static void InspectJsonTree(
        JsonElement root,
        Dictionary<PolicyContentScope, FindingAccumulator> findings,
        InspectionBudget budget)
    {
        var pending = new Stack<(JsonElement Element, int Depth)>();
        budget.VisitJsonNode(1);
        pending.Push((root, 1));

        while (pending.TryPop(out var current))
        {
            if (current.Element.ValueKind == JsonValueKind.Object)
            {
                if (LooksLikeRepositoryEnvelope(current.Element))
                {
                    Add(findings, PolicyContentScope.CompleteRepository,
                        OutboundInspectionReasonCode.CompleteRepositoryPayload, OutboundTriggerKind.ContentPattern, 1);
                }

                if (LooksLikeRawSnapshot(current.Element))
                {
                    Add(findings, PolicyContentScope.RawSnapshot,
                        OutboundInspectionReasonCode.RawSnapshotPayload, OutboundTriggerKind.ContentPattern, 1);
                }

                foreach (var property in current.Element.EnumerateObject())
                {
                    budget.VisitJsonProperty();
                    budget.VisitJsonNode(current.Depth + 1);
                    pending.Push((property.Value, current.Depth + 1));
                }
            }
            else if (current.Element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in current.Element.EnumerateArray())
                {
                    budget.VisitJsonNode(current.Depth + 1);
                    pending.Push((item, current.Depth + 1));
                }
            }
        }
    }

    private static bool LooksLikeRepositoryEnvelope(JsonElement root)
    {
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

    private static bool LooksLikeRawSnapshot(JsonElement root) =>
        new[] { "schemaVersion", "repository", "git", "projects", "entities", "relations" }
            .All(property => TryGetProperty(root, property, out _));

    private static bool LooksLikeLegacyRawSnapshot(string content) =>
        content.Contains("\"schemaVersion\"", StringComparison.OrdinalIgnoreCase)
        && content.Contains("\"entities\"", StringComparison.OrdinalIgnoreCase)
        && content.Contains("\"relations\"", StringComparison.OrdinalIgnoreCase);

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

    private static int CountCFamilyBodies(string content)
    {
        var lines = content.Split('\n');
        var count = 0;

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex].TrimEnd('\r');
            var openingBrace = line.IndexOf('{');
            var header = openingBrace >= 0 ? line[..openingBrace] : line;
            if (!IsCFamilyDeclarationHeader(header))
            {
                continue;
            }

            var braceLineIndex = lineIndex;
            string contentAfterBrace;
            if (openingBrace >= 0)
            {
                contentAfterBrace = line[(openingBrace + 1)..];
            }
            else
            {
                braceLineIndex = NextNonEmptyLine(lines, lineIndex + 1);
                if (braceLineIndex < 0)
                {
                    continue;
                }

                var braceLine = lines[braceLineIndex].TrimEnd('\r');
                openingBrace = braceLine.IndexOf('{');
                if (openingBrace < 0 || !string.IsNullOrWhiteSpace(braceLine[..openingBrace]))
                {
                    continue;
                }

                contentAfterBrace = braceLine[(openingBrace + 1)..];
            }

            if (HasCFamilyBodyContent(contentAfterBrace)
                || HasCFamilyBodyLine(lines, braceLineIndex + 1))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsCFamilyDeclarationHeader(string header)
    {
        var text = header.Trim();
        if (text.Length == 0 || text.EndsWith(';') || text.Contains("=>", StringComparison.Ordinal))
        {
            return false;
        }

        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (IsCFamilyDeclarationHeader(tokens, text))
        {
            return true;
        }

        for (var index = 1; index < tokens.Length; index++)
        {
            if (IsCFamilyAccessModifier(tokens[index])
                && IsCFamilyDeclarationHeader(tokens[index..], string.Join(' ', tokens[index..])))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCFamilyDeclarationHeader(string[] tokens, string text)
    {
        var firstSignificant = 0;
        while (firstSignificant < tokens.Length && IsCFamilyModifier(tokens[firstSignificant]))
        {
            firstSignificant++;
        }

        if (firstSignificant >= tokens.Length)
        {
            return false;
        }

        if (IsCFamilyTypeKeyword(tokens[firstSignificant]))
        {
            var nameIndex = tokens[firstSignificant].Equals("record", StringComparison.Ordinal)
                && firstSignificant + 1 < tokens.Length
                && (tokens[firstSignificant + 1].Equals("class", StringComparison.Ordinal)
                    || tokens[firstSignificant + 1].Equals("struct", StringComparison.Ordinal))
                    ? firstSignificant + 2
                    : firstSignificant + 1;
            return nameIndex < tokens.Length && CFamilyIdentifier().IsMatch(tokens[nameIndex]);
        }

        var openParenthesis = text.IndexOf('(');
        var closeParenthesis = text.LastIndexOf(')');
        if (openParenthesis <= 0
            || closeParenthesis < openParenthesis
            || !string.IsNullOrWhiteSpace(text[(closeParenthesis + 1)..]))
        {
            return false;
        }

        var prefixTokens = text[..openParenthesis]
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var methodStart = 0;
        while (methodStart < prefixTokens.Length && IsCFamilyModifier(prefixTokens[methodStart]))
        {
            methodStart++;
        }

        return methodStart < prefixTokens.Length
            && !IsCFamilyControlKeyword(prefixTokens[methodStart])
            && CFamilyMemberName().IsMatch(prefixTokens[^1]);
    }

    private static int NextNonEmptyLine(string[] lines, int startIndex)
    {
        for (var index = startIndex;
             index < lines.Length && index - startIndex < MaximumCFamilyBodyLookaheadLines;
             index++)
        {
            if (!string.IsNullOrWhiteSpace(lines[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool HasCFamilyBodyLine(string[] lines, int startIndex)
    {
        for (var index = startIndex;
             index < lines.Length && index - startIndex < MaximumCFamilyBodyLookaheadLines;
             index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (HasCFamilyBodyContent(line))
            {
                return true;
            }

            if (line.Contains('}'))
            {
                return false;
            }
        }

        return false;
    }

    private static bool HasCFamilyBodyContent(string value) => value.Any(character =>
        !char.IsWhiteSpace(character) && character is not '{' and not '}' and not ';');

    private static bool IsCFamilyModifier(string value) => value is
        "public" or "private" or "protected" or "internal" or "static" or "abstract" or "sealed"
        or "partial" or "readonly" or "ref" or "unsafe" or "new" or "file" or "async" or "virtual"
        or "override" or "extern" or "final" or "synchronized" or "native" or "inline" or "constexpr";

    private static bool IsCFamilyAccessModifier(string value) => value is
        "public" or "private" or "protected" or "internal";

    private static bool IsCFamilyTypeKeyword(string value) => value is "class" or "record" or "struct" or "enum";

    private static bool IsCFamilyControlKeyword(string value) => value is
        "if" or "for" or "foreach" or "while" or "switch" or "catch" or "using" or "lock"
        or "return" or "throw" or "new";

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

    private sealed class InspectionBudget
    {
        private int _charactersExamined;
        private int _jsonCandidates;
        private int _jsonNodesAndProperties;

        public void ExamineCharacters(int count)
        {
            _charactersExamined = checked(_charactersExamined + count);
            if (_charactersExamined > MaximumInspectionCharacters)
            {
                throw new InvalidDataException("Outbound inspection character limit exceeded.");
            }
        }

        public void AddJsonCandidate()
        {
            _jsonCandidates++;
            if (_jsonCandidates > MaximumJsonCandidates)
            {
                throw new InvalidDataException("Outbound JSON candidate limit exceeded.");
            }
        }

        public void EnsureJsonNestingDepth(int depth)
        {
            if (depth > MaximumJsonNestingDepth)
            {
                throw new InvalidDataException("Outbound JSON nesting limit exceeded.");
            }
        }

        public void VisitJsonNode(int depth)
        {
            EnsureJsonNestingDepth(depth);
            AddJsonNodeOrProperty();
        }

        public void VisitJsonProperty() => AddJsonNodeOrProperty();

        private void AddJsonNodeOrProperty()
        {
            _jsonNodesAndProperties++;
            if (_jsonNodesAndProperties > MaximumJsonNodesAndProperties)
            {
                throw new InvalidDataException("Outbound JSON traversal limit exceeded.");
            }
        }
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

    [GeneratedRegex(@"^[A-Za-z_]\w*(?:<[^<>\r\n]+>)?$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex CFamilyIdentifier();

    [GeneratedRegex(@"^(?:[A-Za-z_]\w*\.)?[A-Za-z_]\w*(?:<[^<>\r\n]+>)?$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex CFamilyMemberName();

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
