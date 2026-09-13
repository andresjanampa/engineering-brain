using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed record PersistedInitiativeAnalysis(
    int SchemaVersion,
    string AnalysisId,
    DateTimeOffset GeneratedAtUtc,
    string InitiativeFileName,
    string InitiativeContentHash,
    RepositoryInfo Repository,
    GitInfo Git,
    InitiativeUnderstanding Understanding,
    CandidateRetrievalResult Retrieval,
    int ContextEstimatedTokens,
    IReadOnlyList<string> IncludedNotePaths,
    InitiativeAnalysis Analysis,
    IReadOnlyList<GovernedRecommendation> Recommendations,
    PolicyOutcome PolicyOutcome,
    InitiativeAnalysisUsage Usage,
    IReadOnlyList<OutboundPolicyAssessment>? OutboundPolicyAssessments = null);

public sealed class LocalInitiativeAnalysisStore
{
    public const int CurrentSchemaVersion = 3;

    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();
    private readonly string _dataRoot;

    public LocalInitiativeAnalysisStore(string? dataRoot = null)
    {
        _dataRoot = dataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".engineering-brain");
    }

    public static string CreateContentHash(string initiative) => Hash(initiative);

    public static string CreateAnalysisId(string repositoryId, string branch, string contentHash) =>
        Hash(string.Join('\n', repositoryId, branch, contentHash));

    public async Task<string> SaveAsync(
        InitiativeAnalysisResult result,
        CancellationToken cancellationToken = default)
    {
        var branch = result.Git.Branch ?? "(no branch)";
        var directory = Path.Combine(
            _dataRoot,
            "repositories",
            KnowledgeIdentity.CreateRepositoryKey(result.Repository.Id),
            "analyses",
            "branches",
            KnowledgeIdentity.CreateBranchKey(branch));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{result.AnalysisId}.json");
        var stored = new PersistedInitiativeAnalysis(
            CurrentSchemaVersion,
            result.AnalysisId,
            DateTimeOffset.UtcNow,
            result.InitiativeFileName,
            result.InitiativeContentHash,
            result.Repository with { Root = "." },
            result.Git,
            result.Understanding,
            result.Retrieval,
            result.Context.EstimatedTokens,
            result.Context.IncludedNotePaths,
            result.Analysis,
            result.Recommendations,
            result.PolicyOutcome,
            result.Usage,
            result.OutboundPolicyAssessments);
        var json = JsonSerializer.Serialize(stored, JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        var temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, json, new UTF8Encoding(false), cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return path;
    }

    public async Task<PersistedInitiativeAnalysis> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("schemaVersion", out var schemaVersion)
            || schemaVersion.ValueKind != JsonValueKind.Number
            || !schemaVersion.TryGetInt32(out var version))
        {
            throw new InvalidDataException("Initiative analysis does not declare a valid schemaVersion.");
        }

        if (version is not 2 and not CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Initiative analysis schema {version} is unsupported; expected 2 or {CurrentSchemaVersion}. Historical analyses are not rewritten automatically.");
        }

        var persisted = JsonSerializer.Deserialize<PersistedInitiativeAnalysis>(json, JsonOptions)
            ?? throw new InvalidDataException("Initiative analysis JSON could not be deserialized.");
        return persisted with
        {
            OutboundPolicyAssessments = persisted.OutboundPolicyAssessments ?? [OutboundPolicyAssessment.NotRecorded]
        };
    }

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
