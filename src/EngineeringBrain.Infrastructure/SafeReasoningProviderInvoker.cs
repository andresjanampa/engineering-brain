using System.Text.Json;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class SafeReasoningProviderInvoker
{
    public async Task<ReasoningResult<T>> InvokeAsync<T>(
        IReasoningProvider provider,
        ApprovedReasoningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await provider.GenerateStructuredAsync<T>(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ReasoningProviderException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw new ReasoningProviderException(
                ReasoningProviderFailureCode.InvalidStructuredOutput,
                request.Request.Stage,
                0,
                0);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new ReasoningProviderException(
                ReasoningProviderFailureCode.TransportFailure,
                request.Request.Stage,
                0,
                0);
        }
    }
}
