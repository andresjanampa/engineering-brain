using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using EngineeringBrain.Core;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace EngineeringBrain.Infrastructure;

public sealed record OpenAIReasoningProviderOptions(
    TimeSpan Timeout,
    int MaximumRetries = 1,
    string InterpretationReasoningEffort = "low",
    string AnalysisReasoningEffort = "medium")
{
    public static OpenAIReasoningProviderOptions Default { get; } = new(TimeSpan.FromMinutes(2));

    public static OpenAIReasoningProviderOptions FromEnvironment(
        Func<string, string?>? readEnvironment = null)
    {
        var read = readEnvironment ?? Environment.GetEnvironmentVariable;
        return Default with
        {
            InterpretationReasoningEffort = read("ENGINEERING_BRAIN_INTERPRETATION_REASONING_EFFORT") ?? "low",
            AnalysisReasoningEffort = read("ENGINEERING_BRAIN_ANALYSIS_REASONING_EFFORT") ?? "medium"
        };
    }

    public string GetReasoningEffort(ReasoningStage stage) => stage switch
    {
        ReasoningStage.InitiativeUnderstanding => Normalize(InterpretationReasoningEffort),
        ReasoningStage.ArchitectureAnalysis => Normalize(AnalysisReasoningEffort),
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };

    private static string Normalize(string value) => value.Trim().ToLowerInvariant() switch
    {
        "low" => "low",
        "medium" => "medium",
        "high" => "high",
        _ => throw new ArgumentException($"Unsupported OpenAI reasoning effort '{value}'. Use low, medium, or high.")
    };
}

public sealed class OpenAIReasoningProvider : IReasoningProvider
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly ResponsesClient _client;
    private readonly OpenAIReasoningProviderOptions _options;

    public OpenAIReasoningProvider(
        string apiKey,
        OpenAIReasoningProviderOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("OpenAI API key is required.", nameof(apiKey));
        }

        _client = new ResponsesClient(apiKey);
        _options = options ?? OpenAIReasoningProviderOptions.Default;
        if (_options.Timeout <= TimeSpan.Zero || _options.MaximumRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Timeout must be positive and retries cannot be negative.");
        }
        _options.GetReasoningEffort(ReasoningStage.InitiativeUnderstanding);
        _options.GetReasoningEffort(ReasoningStage.ArchitectureAnalysis);
    }

    public string Name => "OpenAI";

    public async Task<ReasoningResult<T>> GenerateStructuredAsync<T>(
        ReasoningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stopwatch = Stopwatch.StartNew();
        var failure = "The provider did not return a result.";
        var structuredOutputFailure = false;
        var hasReportedUsage = false;
        var actualInputTokens = 0;
        var cachedInputTokens = 0;
        var actualOutputTokens = 0;
        var reasoningTokens = 0;
        var reasoningEffort = _options.GetReasoningEffort(request.Stage);
        for (var attempt = 0; attempt <= _options.MaximumRetries; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.Timeout);
            try
            {
                var options = new CreateResponseOptions
                {
                    Model = request.Model,
                    MaxOutputTokenCount = request.MaximumOutputTokens,
                    StoredOutputEnabled = false,
                    ReasoningOptions = new ResponseReasoningOptions
                    {
                        ReasoningEffortLevel = reasoningEffort switch
                        {
                            "low" => ResponseReasoningEffortLevel.Low,
                            "medium" => ResponseReasoningEffortLevel.Medium,
                            "high" => ResponseReasoningEffortLevel.High,
                            _ => throw new InvalidOperationException("Reasoning effort was not validated.")
                        }
                    },
                    TextOptions = new ResponseTextOptions
                    {
                        TextFormat = ResponseTextFormat.CreateJsonSchemaFormat(
                            JsonNamingPolicy.SnakeCaseLower.ConvertName(typeof(T).Name),
                            ReasoningJsonSchema.For<T>(),
                            jsonSchemaIsStrict: true)
                    }
                };
                options.InputItems.Add(ResponseItem.CreateSystemMessageItem(request.SystemInstructions));
                options.InputItems.Add(ResponseItem.CreateUserMessageItem(request.UserData));
                ResponseResult response = await _client.CreateResponseAsync(options, timeout.Token);
                if (response.Usage is not null)
                {
                    hasReportedUsage = true;
                    actualInputTokens += response.Usage.InputTokenCount;
                    cachedInputTokens += response.Usage.InputTokenDetails?.CachedTokenCount ?? 0;
                    actualOutputTokens += response.Usage.OutputTokenCount;
                    reasoningTokens += response.Usage.OutputTokenDetails?.ReasoningTokenCount ?? 0;
                }
                var json = response.GetOutputText();
                var value = JsonSerializer.Deserialize<T>(json, JsonOptions)
                    ?? throw new JsonException("Structured response was empty.");
                return new ReasoningResult<T>(
                    value,
                    new ReasoningCallUsage(
                        request.Stage,
                        Name,
                        request.Model,
                        request.EstimatedInputTokens,
                        hasReportedUsage ? actualInputTokens : null,
                        hasReportedUsage ? cachedInputTokens : null,
                        hasReportedUsage ? actualOutputTokens : null,
                        stopwatch.ElapsedMilliseconds,
                        attempt,
                        reasoningEffort,
                        hasReportedUsage ? reasoningTokens : null));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failure = "The reasoning provider timed out.";
                structuredOutputFailure = false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (JsonException)
            {
                failure = "The provider returned an invalid structured response.";
                structuredOutputFailure = true;
            }
            catch (Exception)
            {
                failure = "The provider request failed.";
                structuredOutputFailure = false;
            }
        }

        throw new ReasoningProviderException(
            request.Stage,
            structuredOutputFailure,
            stopwatch.ElapsedMilliseconds,
            _options.MaximumRetries,
            $"OpenAI returned no valid structured result during {request.Stage} after {_options.MaximumRetries + 1} attempts. {failure} No request content or credential was logged.",
            hasReportedUsage ? actualInputTokens : null,
            hasReportedUsage ? cachedInputTokens : null,
            hasReportedUsage ? actualOutputTokens : null,
            hasReportedUsage ? reasoningTokens : null);
    }
    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

#pragma warning restore OPENAI001
