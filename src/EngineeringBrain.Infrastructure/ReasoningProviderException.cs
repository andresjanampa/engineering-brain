using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class ReasoningProviderException : InvalidOperationException
{
    public ReasoningProviderException(
        ReasoningStage stage,
        bool structuredOutputFailure,
        long durationMilliseconds,
        int retries,
        string message,
        int? actualInputTokens = null,
        int? cachedInputTokens = null,
        int? actualOutputTokens = null,
        int? reasoningTokens = null)
        : base(message)
    {
        Stage = stage;
        StructuredOutputFailure = structuredOutputFailure;
        DurationMilliseconds = durationMilliseconds;
        Retries = retries;
        ActualInputTokens = actualInputTokens;
        CachedInputTokens = cachedInputTokens;
        ActualOutputTokens = actualOutputTokens;
        ReasoningTokens = reasoningTokens;
    }

    public ReasoningStage Stage { get; }
    public bool StructuredOutputFailure { get; }
    public long DurationMilliseconds { get; }
    public int Retries { get; }
    public int? ActualInputTokens { get; }
    public int? CachedInputTokens { get; }
    public int? ActualOutputTokens { get; }
    public int? ReasoningTokens { get; }
}
