using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed partial class OutboundContextGuard
{
    private const int MaximumFindingCount = 99;
    private const int MaximumCFamilyHeaderCharacters = 16_384;
    private const int MaximumRawStringDelimiterQuotes = 32;
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
                // Continue inside the failed range so valid nested objects remain candidates.
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
        var state = CFamilyScanState.Searching;
        var header = new StringBuilder();
        var scanner = new CFamilyLexicalScanner();

        foreach (var rawLine in lines)
        {
            if (state == CFamilyScanState.Searching)
            {
                scanner.PrepareForSearchLine();
            }

            var scan = scanner.ScanLine(rawLine.TrimEnd('\r'));
            var line = scan.Text;

            if (state == CFamilyScanState.Searching)
            {
                var declarationStart = FindCFamilyDeclarationStart(line);
                if (declarationStart < 0)
                {
                    continue;
                }

                header.Clear();
                state = CFamilyScanState.CollectingHeader;
                line = line[declarationStart..];
                scan = new CFamilyLexicalScan(line, scan.Structure.Slice(declarationStart));
            }

            if (state == CFamilyScanState.CollectingHeader)
            {
                var structure = scan.Structure;
                var openingBrace = structure.OpeningBrace;
                var terminator = structure.Terminator;
                var headerEnd = openingBrace >= 0 ? openingBrace : line.Length;
                if (terminator >= 0 && terminator < headerEnd)
                {
                    ResetCFamilyScan();
                    continue;
                }

                AppendCFamilyHeader(header, line[..headerEnd]);
                if (openingBrace < 0)
                {
                    continue;
                }

                if (!IsCFamilyDeclarationHeader(header.ToString()))
                {
                    ResetCFamilyScan();
                    continue;
                }

                state = CFamilyScanState.WaitingForBodyEvidence;
                line = line[(openingBrace + 1)..];
            }

            if (HasCFamilyBodyContent(line))
            {
                count++;
                ResetCFamilyScan();
                continue;
            }

            if (scan.Structure.ClosingBrace >= 0)
            {
                ResetCFamilyScan();
            }
        }

        if (state != CFamilyScanState.Searching && !scanner.IsComplete)
        {
            throw new InvalidDataException("Outbound C-family lexical scan was incomplete.");
        }

        return count;

        void ResetCFamilyScan()
        {
            state = CFamilyScanState.Searching;
            header.Clear();
            scanner.Reset();
        }
    }

    private static bool IsCFamilyDeclarationHeader(string header)
    {
        var text = header.Trim();
        var structure = ScanCFamilyStructure(text);
        if (text.Length == 0
            || structure.Terminator >= 0
            || structure.ExpressionArrow >= 0)
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

        var structure = ScanCFamilyStructure(text);
        var openParenthesis = structure.OpeningParenthesis;
        var closeParenthesis = structure.ClosingParenthesis;
        if (openParenthesis <= 0
            || closeParenthesis < openParenthesis
            || !IsCFamilyDeclarationSuffix(text[(closeParenthesis + 1)..]))
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

    private static int FindCFamilyDeclarationStart(string line)
    {
        if (IsPlausibleCFamilyDeclarationStart(line))
        {
            return 0;
        }

        foreach (var modifier in new[] { "public", "private", "protected", "internal" })
        {
            var searchStart = 0;
            while (searchStart < line.Length)
            {
                var index = line.IndexOf(modifier, searchStart, StringComparison.Ordinal);
                if (index < 0)
                {
                    break;
                }

                var beforeIsBoundary = index == 0 || !IsCFamilyIdentifierCharacter(line[index - 1]);
                var after = index + modifier.Length;
                var afterIsBoundary = after == line.Length || !IsCFamilyIdentifierCharacter(line[after]);
                if (beforeIsBoundary && afterIsBoundary
                    && IsPlausibleCFamilyDeclarationStart(line[index..]))
                {
                    return index;
                }

                searchStart = after;
            }
        }

        return -1;
    }

    private static bool IsPlausibleCFamilyDeclarationStart(string value)
    {
        var text = value.TrimStart();
        if (text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        var structure = ScanCFamilyStructure(text);
        var openingBrace = structure.OpeningBrace;
        var semicolon = structure.Terminator;
        var end = openingBrace >= 0 ? openingBrace : semicolon >= 0 ? semicolon : text.Length;
        var candidate = text[..end].Trim();
        var tokens = candidate.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
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
            return true;
        }

        var openParenthesis = ScanCFamilyStructure(candidate).OpeningParenthesis;
        if (openParenthesis <= 0)
        {
            return false;
        }

        var prefixTokens = candidate[..openParenthesis]
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

    private static void AppendCFamilyHeader(StringBuilder header, string line)
    {
        var value = line.Trim();
        if (value.Length == 0)
        {
            return;
        }

        var separatorLength = header.Length == 0 ? 0 : 1;
        if (header.Length + separatorLength + value.Length > MaximumCFamilyHeaderCharacters)
        {
            throw new InvalidDataException("Outbound C-family header limit exceeded.");
        }

        if (separatorLength != 0)
        {
            header.Append(' ');
        }

        header.Append(value);
    }

    private static CFamilyStructure ScanCFamilyStructure(string value) =>
        new CFamilyLexicalScanner().ScanLine(value).Structure;

    private static bool IsCFamilyDeclarationSuffix(string value)
    {
        var suffix = value.Trim();
        return suffix.Length == 0
            || suffix.StartsWith("where ", StringComparison.Ordinal)
            || suffix.StartsWith(": base(", StringComparison.Ordinal)
            || suffix.StartsWith(": this(", StringComparison.Ordinal);
    }

    private static bool IsCFamilyIdentifierCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static bool HasCFamilyBodyContent(string value) => value.Any(character =>
        !char.IsWhiteSpace(character) && character is not '{' and not '}' and not ';');

    private enum CFamilyScanState
    {
        Searching,
        CollectingHeader,
        WaitingForBodyEvidence
    }

    private enum CFamilyLexicalState
    {
        Normal,
        LineComment,
        BlockComment,
        RegularString,
        VerbatimString,
        RawString,
        CharacterLiteral
    }

    private sealed class CFamilyLexicalScanner
    {
        private CFamilyLexicalState _state;
        private bool _escaped;
        private int _rawDelimiterLength;

        public bool IsComplete => _state == CFamilyLexicalState.Normal;

        public CFamilyLexicalScan ScanLine(string line)
        {
            var text = new StringBuilder(line.Length);
            var openingBrace = -1;
            var closingBrace = -1;
            var terminator = -1;
            var openingParenthesis = -1;
            var closingParenthesis = -1;
            var expressionArrow = -1;
            var parenthesisDepth = 0;

            for (var index = 0; index < line.Length; index++)
            {
                var character = line[index];
                var next = index + 1 < line.Length ? line[index + 1] : '\0';

                switch (_state)
                {
                    case CFamilyLexicalState.LineComment:
                        index = line.Length;
                        continue;
                    case CFamilyLexicalState.BlockComment:
                        if (character == '*' && next == '/')
                        {
                            _state = CFamilyLexicalState.Normal;
                            index++;
                        }

                        continue;
                    case CFamilyLexicalState.RegularString:
                    case CFamilyLexicalState.CharacterLiteral:
                        if (_escaped)
                        {
                            text.Append(' ');
                            _escaped = false;
                        }
                        else if (character == '\\')
                        {
                            text.Append(' ');
                            _escaped = true;
                        }
                        else if ((_state == CFamilyLexicalState.RegularString && character == '"')
                            || (_state == CFamilyLexicalState.CharacterLiteral && character == '\''))
                        {
                            text.Append(character);
                            _state = CFamilyLexicalState.Normal;
                        }
                        else
                        {
                            text.Append(' ');
                        }

                        continue;
                    case CFamilyLexicalState.VerbatimString:
                        if (character != '"')
                        {
                            text.Append(' ');
                            continue;
                        }

                        if (next == '"')
                        {
                            text.Append("  ");
                            index++;
                        }
                        else
                        {
                            text.Append(character);
                            _state = CFamilyLexicalState.Normal;
                        }

                        continue;
                    case CFamilyLexicalState.RawString:
                        if (character != '"')
                        {
                            text.Append(' ');
                            continue;
                        }

                        var closingQuoteCount = CountConsecutiveQuotes(line, index);
                        index += closingQuoteCount - 1;
                        if (closingQuoteCount > _rawDelimiterLength)
                        {
                            throw new InvalidDataException("Outbound C-family raw-string delimiter was invalid.");
                        }

                        if (closingQuoteCount == _rawDelimiterLength)
                        {
                            text.Append(line.AsSpan(index - closingQuoteCount + 1, closingQuoteCount));
                            _state = CFamilyLexicalState.Normal;
                            _rawDelimiterLength = 0;
                        }
                        else
                        {
                            text.Append(' ', closingQuoteCount);
                        }

                        continue;
                    case CFamilyLexicalState.Normal:
                        break;
                    default:
                        throw new InvalidOperationException("Unknown C-family lexical state.");
                }

                if (character == '/' && next == '/')
                {
                    _state = CFamilyLexicalState.LineComment;
                    index = line.Length;
                    continue;
                }

                if (character == '/' && next == '*')
                {
                    _state = CFamilyLexicalState.BlockComment;
                    index++;
                    continue;
                }

                if (character == '@'
                    && next == '$'
                    && index + 2 < line.Length
                    && line[index + 2] == '"')
                {
                    text.Append("@$\"");
                    _state = CFamilyLexicalState.VerbatimString;
                    index += 2;
                    continue;
                }

                if (character == '@' && next == '"')
                {
                    text.Append(character);
                    text.Append(next);
                    _state = CFamilyLexicalState.VerbatimString;
                    index++;
                    continue;
                }

                if (character == '"')
                {
                    var openingQuoteCount = CountConsecutiveQuotes(line, index);
                    if (openingQuoteCount >= 3)
                    {
                        if (openingQuoteCount > MaximumRawStringDelimiterQuotes)
                        {
                            throw new InvalidDataException("Outbound C-family raw-string delimiter limit exceeded.");
                        }

                        text.Append(line.AsSpan(index, openingQuoteCount));
                        _state = CFamilyLexicalState.RawString;
                        _rawDelimiterLength = openingQuoteCount;
                        index += openingQuoteCount - 1;
                        continue;
                    }

                    text.Append(character);
                    _state = CFamilyLexicalState.RegularString;
                    _escaped = false;
                    continue;
                }

                if (character == '\'')
                {
                    text.Append(character);
                    _state = CFamilyLexicalState.CharacterLiteral;
                    _escaped = false;
                    continue;
                }

                var position = text.Length;
                switch (character)
                {
                    case '{' when openingBrace < 0:
                        openingBrace = position;
                        break;
                    case '}' when closingBrace < 0:
                        closingBrace = position;
                        break;
                    case ';' when terminator < 0:
                        terminator = position;
                        break;
                    case '=' when expressionArrow < 0 && next == '>':
                        expressionArrow = position;
                        break;
                    case '(' when closingParenthesis < 0:
                        if (parenthesisDepth == 0 && openingParenthesis < 0)
                        {
                            openingParenthesis = position;
                        }

                        parenthesisDepth++;
                        break;
                    case ')' when parenthesisDepth > 0 && closingParenthesis < 0:
                        parenthesisDepth--;
                        if (parenthesisDepth == 0)
                        {
                            closingParenthesis = position;
                        }

                        break;
                }

                text.Append(character);
            }

            if (_state == CFamilyLexicalState.LineComment)
            {
                _state = CFamilyLexicalState.Normal;
            }

            return new CFamilyLexicalScan(
                text.ToString(),
                new CFamilyStructure(
                    openingBrace,
                    closingBrace,
                    terminator,
                    openingParenthesis,
                    closingParenthesis,
                    expressionArrow));
        }

        public void Reset()
        {
            _state = CFamilyLexicalState.Normal;
            _escaped = false;
            _rawDelimiterLength = 0;
        }

        public void PrepareForSearchLine()
        {
            if (_state != CFamilyLexicalState.BlockComment)
            {
                Reset();
            }
        }

        private static int CountConsecutiveQuotes(string value, int start)
        {
            var count = 0;
            while (start + count < value.Length && value[start + count] == '"')
            {
                count++;
            }

            return count;
        }
    }

    private readonly record struct CFamilyLexicalScan(string Text, CFamilyStructure Structure);

    private readonly record struct CFamilyStructure(
        int OpeningBrace,
        int ClosingBrace,
        int Terminator,
        int OpeningParenthesis,
        int ClosingParenthesis,
        int ExpressionArrow)
    {
        public CFamilyStructure Slice(int start) => new(
            Shift(OpeningBrace, start),
            Shift(ClosingBrace, start),
            Shift(Terminator, start),
            Shift(OpeningParenthesis, start),
            Shift(ClosingParenthesis, start),
            Shift(ExpressionArrow, start));

        private static int Shift(int position, int start) => position < start ? -1 : position - start;
    }

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
