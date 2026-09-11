namespace EngineeringBrain.Infrastructure;

public static class RemoteReasoningAuthorization
{
    public static string RequireOpenAIApiKey(bool allowRemote, Func<string, string?>? readEnvironment = null)
    {
        if (!allowRemote)
        {
            throw new InvalidOperationException(
                "Remote reasoning is disabled. Re-run analyze with --allow-remote after reviewing the privacy notice.");
        }

        var apiKey = (readEnvironment ?? Environment.GetEnvironmentVariable)("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "OPENAI_API_KEY is not configured. Set it in the process environment; never store it in the repository.");
        }

        return apiKey;
    }
}
