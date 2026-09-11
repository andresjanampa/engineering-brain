using System.Text.Json;
using System.Text.Json.Serialization;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class LiveEvaluationSuiteSerializer
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public async Task<LiveEvaluationSuite> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var suite = JsonSerializer.Deserialize<LiveEvaluationSuite>(
            await File.ReadAllTextAsync(path, cancellationToken), JsonOptions)
            ?? throw new InvalidDataException("Live evaluation suite is empty.");
        if (suite.LiveEvaluationSchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Live evaluation schema {suite.LiveEvaluationSchemaVersion} is unsupported; expected {CurrentSchemaVersion}.");
        if (string.IsNullOrWhiteSpace(suite.Id) || suite.Cases.Count == 0
            || suite.Cases.Any(item => string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.InitiativePath))
            || suite.Cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != suite.Cases.Count)
            throw new InvalidDataException("Live evaluation suite has missing or duplicate case identities.");

        var root = Path.GetDirectoryName(Path.GetFullPath(path))!;
        foreach (var item in suite.Cases)
        {
            var initiativePath = ResolveWithin(root, item.InitiativePath);
            if (!File.Exists(initiativePath))
                throw new InvalidDataException($"Live evaluation initiative does not exist: {item.InitiativePath}");
        }
        return suite;
    }

    public static string ResolveWithin(string root, string relativePath)
    {
        root = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Live evaluation path escapes the suite directory.");
        return path;
    }

    internal static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public static class LiveEvaluationPlanner
{
    public const int MaximumCases = 5;
    public const int MaximumRuns = 3;
    public const int MaximumLogicalCalls = 10;

    public static LiveEvaluationPlan Create(
        LiveEvaluationSuite suite,
        int runs = 1,
        IReadOnlyList<string>? caseIds = null,
        TokenBudgetOptions? budget = null)
    {
        ArgumentNullException.ThrowIfNull(suite);
        if (runs is < 1 or > MaximumRuns)
            throw new ArgumentOutOfRangeException(nameof(runs), $"Live evaluation runs must be between 1 and {MaximumRuns}.");
        var selected = caseIds is null || caseIds.Count == 0
            ? suite.Cases.ToArray()
            : caseIds.Select(id => suite.Cases.SingleOrDefault(item => item.Id.Equals(id, StringComparison.Ordinal))
                ?? throw new InvalidDataException($"Unknown live evaluation case: {id}"))
                .DistinctBy(item => item.Id, StringComparer.Ordinal).ToArray();
        if (selected.Length > MaximumCases)
            throw new InvalidDataException($"Live evaluation is limited to {MaximumCases} cases per command.");
        var calls = checked(selected.Length * runs * 2);
        if (calls > MaximumLogicalCalls)
            throw new InvalidDataException($"Live evaluation would make {calls} logical calls; the hard limit is {MaximumLogicalCalls}. Select fewer cases or runs.");
        var limits = budget ?? new TokenBudgetOptions();
        var estimatedMaximum = checked(selected.Length * runs
            * (limits.MaximumInitiativeInputTokens + limits.MaximumReasoningInputTokens));
        return new LiveEvaluationPlan(suite, selected, runs, calls, estimatedMaximum);
    }
}

public static class LiveEvaluationAuthorization
{
    public static string? Authorize(
        bool fakeProvider,
        bool allowRemote,
        Func<string, string?>? readEnvironment = null)
    {
        if (fakeProvider && allowRemote)
            throw new InvalidOperationException("Choose either --fake-provider or --allow-remote, not both.");
        if (fakeProvider) return null;
        if (!allowRemote)
            throw new InvalidOperationException(
                "Live remote evaluation is disabled. Re-run eval-live with --allow-remote after reviewing the pre-run limits and privacy notice.");
        return RemoteReasoningAuthorization.RequireOpenAIApiKey(allowRemote, readEnvironment);
    }
}
