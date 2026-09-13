using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public interface IReasoningProvider
{
    string Name { get; }

    Task<ReasoningResult<T>> GenerateStructuredAsync<T>(
        ApprovedReasoningRequest request,
        CancellationToken cancellationToken = default);
}
