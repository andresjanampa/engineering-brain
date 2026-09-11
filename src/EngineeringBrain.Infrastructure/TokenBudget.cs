namespace EngineeringBrain.Infrastructure;

public sealed record TokenBudgetOptions(
    int MaximumInitiativeInputTokens = 10_000,
    int RootAndArchitectureTokens = 3_500,
    int ProjectNotesTokens = 10_000,
    int ComponentNotesTokens = 18_000,
    int GraphEvidenceTokens = 6_000,
    int MaximumReasoningInputTokens = 50_000,
    int InitiativeOutputTokens = 4_000,
    int ReasoningOutputTokens = 6_000);

public sealed class TokenEstimator
{
    public int Estimate(string value) => string.IsNullOrEmpty(value)
        ? 0
        : (int)Math.Ceiling(value.Length / 4d);
}
