using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed partial class LocalLiveEvaluationStore
{
    public const int CurrentResultSchemaVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();
    private readonly string _dataRoot;

    public LocalLiveEvaluationStore(string? dataRoot = null)
    {
        _dataRoot = dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".engineering-brain");
    }

    public string GetRunDirectory(string repositoryId, string runId) => Path.Combine(
        _dataRoot,
        "repositories",
        KnowledgeIdentity.CreateRepositoryKey(repositoryId),
        "evaluations",
        "live",
        runId);

    public async Task<LiveEvaluationRun> SaveAsync(
        LiveEvaluationRun run,
        CancellationToken cancellationToken = default)
    {
        var directory = GetRunDirectory(run.RepositoryId, run.RunId);
        var casesDirectory = Path.Combine(directory, "cases");
        Directory.CreateDirectory(casesDirectory);
        foreach (var item in run.Cases)
        {
            var safe = item with { ErrorMessage = Redact(item.ErrorMessage) };
            await WriteAtomicAsync(Path.Combine(casesDirectory, $"{SafeName(item.CaseId)}--run-{item.RunNumber}.json"),
                JsonSerializer.Serialize(safe, JsonOptions) + "\n", cancellationToken);
        }

        var summaryPath = Path.Combine(directory, "summary.json");
        var reviewPath = Path.Combine(directory, "review.md");
        var persisted = run with
        {
            Cases = run.Cases.Select(item => item with { ErrorMessage = Redact(item.ErrorMessage) }).ToArray(),
            ResultDirectory = ".",
            SummaryPath = "summary.json",
            ReviewPath = "review.md"
        };
        await WriteAtomicAsync(summaryPath, JsonSerializer.Serialize(persisted, JsonOptions) + "\n", cancellationToken);
        await WriteAtomicAsync(reviewPath, RenderReview(persisted), cancellationToken);
        return run with { ResultDirectory = directory, SummaryPath = summaryPath, ReviewPath = reviewPath };
    }

    public async Task<LiveEvaluationRun> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("liveResultSchemaVersion", out var schemaVersion)
            || schemaVersion.ValueKind != JsonValueKind.Number
            || !schemaVersion.TryGetInt32(out var version))
        {
            throw new InvalidDataException("Live evaluation result does not declare a valid liveResultSchemaVersion.");
        }

        if (version != CurrentResultSchemaVersion)
        {
            throw new InvalidDataException(
                $"Live evaluation result schema {version} is unsupported; expected {CurrentResultSchemaVersion}. Historical runs are not rewritten automatically.");
        }

        return JsonSerializer.Deserialize<LiveEvaluationRun>(json, JsonOptions)
            ?? throw new InvalidDataException("Live evaluation result JSON could not be deserialized.");
    }

    public static string RenderReview(LiveEvaluationRun run)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Live Evaluation Review").AppendLine()
            .AppendLine($"Run: `{run.RunId}`")
            .AppendLine($"Repository: `{run.RepositoryName}`")
            .AppendLine($"Branch: `{run.Branch}`")
            .AppendLine($"Provider: `{run.Provider}`")
            .AppendLine();
        foreach (var item in run.Cases.OrderBy(value => value.CaseId, StringComparer.Ordinal).ThenBy(value => value.RunNumber))
        {
            builder.AppendLine($"## Case: {item.CaseId} / run {item.RunNumber}").AppendLine()
                .AppendLine("### Automatic").AppendLine()
                .AppendLine($"- Status: `{item.Status}`")
                .AppendLine($"- Initiative: `{item.InitiativeFileName}` / `{item.InitiativeHash}`")
                .AppendLine($"- Capability hit rate: `{item.UnderstandingMetrics?.RequiredCapabilityHitRate:F3}`")
                .AppendLine($"- Golden retrieval Recall@5/10/MRR: `{FormatRetrieval(item.RetrievalComparison?.Golden)}`")
                .AppendLine($"- Live retrieval Recall@5/10/MRR: `{FormatRetrieval(item.RetrievalComparison?.Actual)}`")
                .AppendLine($"- Golden missing required retrieval entities: `{FormatMissing(item.RetrievalComparison?.Golden.MissingRequiredEntities)}`")
                .AppendLine($"- Live missing required retrieval entities: `{FormatMissing(item.RetrievalComparison?.Actual.MissingRequiredEntities)}`")
                .AppendLine($"- Evidence validation: `{item.Call2Metrics?.EvidenceValidationRate:F3}`")
                .AppendLine($"- Invalid evidence: `{item.Call2Metrics?.InvalidEvidence ?? 0}`")
                .AppendLine($"- Policy outcome: `{item.PolicyOutcome?.ToString() ?? "n/a"}`")
                .AppendLine($"- Policy activation accuracy: `{item.PolicyMetrics?.PolicyActivationAccuracy:F3}`")
                .AppendLine($"- Blocked recommendation escapes: `{item.PolicyMetrics?.BlockedRecommendationEscapeCount ?? 0}`")
                .AppendLine($"- Error: `{Redact(item.ErrorMessage) ?? "none"}`").AppendLine();
            AppendJson(builder, "CALL #1 structured output", item.Understanding);
            AppendJson(builder, "Understanding expectation matches", item.UnderstandingMetrics is null ? null : new
            {
                item.UnderstandingMetrics.RequiredCapabilityMatches,
                item.UnderstandingMetrics.AcceptableCapabilityMatches,
                item.UnderstandingMetrics.UnknownTopicMatches
            });
            AppendJson(builder, "Retrieved candidates", item.Retrieval is null ? null : new
            {
                Projects = item.Retrieval.Projects.Select(value => new { value.ProjectId, value.Name, value.Score }),
                Components = item.Retrieval.Components.Select(value => new { value.EntityId, value.FullName, value.Score }),
                Relations = item.Retrieval.Relations.Count
            });
            AppendJson(builder, "CALL #2 structured output", item.Analysis);
            AppendJson(builder, "Governed recommendations", item.Recommendations);
            AppendJson(builder, "Policy metrics", item.PolicyMetrics);
            AppendJson(builder, "Usage", item.Usage);
            builder.AppendLine("### Human Review").AppendLine()
                .AppendLine("Initiative understanding [1-5]:")
                .AppendLine("Architectural relevance [1-5]:")
                .AppendLine("Recommendation usefulness [1-5]:")
                .AppendLine("Evidence discipline [1-5]:")
                .AppendLine("Uncertainty handling [1-5]:")
                .AppendLine("Actionability [1-5]:")
                .AppendLine("Notes:").AppendLine();
        }
        return builder.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string FormatRetrieval(LiveRetrievalMetrics? metrics) => metrics is null
        ? "n/a/n/a/n/a"
        : $"{metrics.RecallAt5:F3}/{metrics.RecallAt10:F3}/{metrics.MeanReciprocalRank:F3}";

    private static string FormatMissing(IReadOnlyList<string>? values) => values is null
        ? "n/a"
        : values.Count == 0 ? "none" : string.Join(", ", values);

    public static string? Redact(string? value)
    {
        if (value is null) return null;
        return Authorization().Replace(OpenAIKey().Replace(SecretAssignment().Replace(value, "$1=[REDACTED]"), "[REDACTED]"), "Authorization: [REDACTED]");
    }

    private static void AppendJson(StringBuilder builder, string title, object? value)
    {
        builder.AppendLine($"#### {title}").AppendLine().AppendLine("```json")
            .AppendLine(value is null ? "null" : JsonSerializer.Serialize(value, JsonOptions))
            .AppendLine("```").AppendLine();
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(path)!, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string SafeName(string value) => string.Concat(value.Select(character =>
        char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    [GeneratedRegex(@"(?i)\b(api[_-]?key|password|pwd|client[_-]?secret)\s*[:=]\s*[^\s,;]+")]
    private static partial Regex SecretAssignment();

    [GeneratedRegex(@"\bsk-[A-Za-z0-9_-]{12,}")]
    private static partial Regex OpenAIKey();

    [GeneratedRegex(@"(?i)Authorization\s*:\s*[^\r\n]+")]
    private static partial Regex Authorization();
}
