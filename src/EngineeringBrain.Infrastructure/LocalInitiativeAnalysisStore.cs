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
    IReadOnlyList<ValidatedRecommendation> Recommendations,
    InitiativeAnalysisUsage Usage);

public sealed class LocalInitiativeAnalysisStore
{
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
            1,
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
            result.Usage);
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
