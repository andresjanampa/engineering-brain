using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public enum ReasoningProviderFailureCode
{
    Timeout,
    InvalidStructuredOutput,
    TransportFailure,
    NoValidResult
}

public sealed class ReasoningProviderException : InvalidOperationException
{
    public ReasoningProviderException(
        ReasoningProviderFailureCode failureCode,
        ReasoningStage stage,
        long durationMilliseconds,
        int retries,
        int? actualInputTokens = null,
        int? cachedInputTokens = null,
        int? actualOutputTokens = null,
        int? reasoningTokens = null)
        : base(MessageFor(failureCode))
    {
        FailureCode = failureCode;
        Stage = stage;
        DurationMilliseconds = durationMilliseconds;
        Retries = retries;
        ActualInputTokens = actualInputTokens;
        CachedInputTokens = cachedInputTokens;
        ActualOutputTokens = actualOutputTokens;
        ReasoningTokens = reasoningTokens;
    }

    public ReasoningProviderFailureCode FailureCode { get; }
    public ReasoningStage Stage { get; }
    public bool StructuredOutputFailure => FailureCode == ReasoningProviderFailureCode.InvalidStructuredOutput;
    public long DurationMilliseconds { get; }
    public int Retries { get; }
    public int? ActualInputTokens { get; }
    public int? CachedInputTokens { get; }
    public int? ActualOutputTokens { get; }
    public int? ReasoningTokens { get; }

    private static string MessageFor(ReasoningProviderFailureCode failureCode) => failureCode switch
    {
        ReasoningProviderFailureCode.Timeout => "The reasoning provider timed out.",
        ReasoningProviderFailureCode.InvalidStructuredOutput => "The reasoning provider returned invalid structured output.",
        ReasoningProviderFailureCode.TransportFailure => "The reasoning provider request failed.",
        ReasoningProviderFailureCode.NoValidResult => "The reasoning provider returned no valid result.",
        _ => "The reasoning provider failed."
    };
}
