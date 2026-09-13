using System.Text.Json;
using System.Text.Json.Serialization;
using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class OutboundSecurityEvaluationTests
{
    [Fact]
    public async Task Suite_ProducesOneHundredPercentExpectedDecisions()
    {
        var metrics = await EvaluateSuiteAsync();

        Assert.Equal(16, metrics.Cases);
        Assert.Equal(metrics.ExpectedBlocked, metrics.ActualBlocked);
        Assert.Equal(0, metrics.FalseAllowed);
        Assert.Equal(0, metrics.FalseBlocked);
    }

    [Fact]
    public async Task Suite_HasZeroFalseAllowedFalseBlockedOrDiagnosticLeaks()
    {
        var metrics = await EvaluateSuiteAsync();

        Assert.Equal(0, metrics.FalseAllowed);
        Assert.Equal(0, metrics.FalseBlocked);
        Assert.Equal(0, metrics.DiagnosticLeaks);
    }

    [Fact]
    public async Task Suite_BlockedCasesMakeZeroProviderCalls()
    {
        var metrics = await EvaluateSuiteAsync();

        Assert.Equal(0, metrics.BlockedProviderInvocations);
    }

    private static async Task<SecurityMetrics> EvaluateSuiteAsync()
    {
        var suite = await LoadSuiteAsync();
        var expectedBlocked = 0;
        var actualBlocked = 0;
        var falseAllowed = 0;
        var falseBlocked = 0;
        var diagnosticLeaks = 0;
        var blockedProviderInvocations = 0;

        foreach (var item in suite.Cases)
        {
            var segments = item.Segments.Select((segment, index) => new ContextSegment(
                segment.Kind,
                segment.Content,
                Math.Max(1, segment.Content.Length / 4),
                segment.SourceIdentity,
                index + 1,
                true)).ToArray();
            var context = segments.Length == 0
                ? null
                : new InitiativeContext(
                    ContextSegmentRenderer.Render(segments),
                    segments.Sum(segment => segment.EstimatedTokens),
                    item.SourceReferences ?? [],
                    [],
                    segments);
            var request = new ReasoningRequest(
                item.Stage,
                "offline-security-fixture",
                item.SystemInstructions,
                context?.Content ?? item.UserData,
                500,
                100);
            var gate = new OutboundRequestGate(new OutboundContextGuard(new FixedSecretValueSource(item.KnownSecretValues)));
            var provider = new CountingProvider();
            OutboundPolicyAssessment assessment;
            var attemptsBefore = provider.Attempts;

            if (item.AssessmentKind == OutboundAssessmentKind.Projected)
            {
                assessment = gate.AssessProjected(request, context);
            }
            else
            {
                try
                {
                    var approved = gate.ApproveExact(request, context);
                    assessment = approved.Assessment;
                    await provider.GenerateStructuredAsync<object>(approved);
                }
                catch (OutboundSecurityException exception)
                {
                    assessment = exception.Assessment;
                    diagnosticLeaks += ContainsSensitiveValue(
                        exception.ToString() + JsonSerializer.Serialize(exception.Assessment),
                        item.SensitiveValues) ? 1 : 0;
                }
            }

            var expectedIsBlocked = item.ExpectedOutcome == OutboundPolicyOutcome.Blocked;
            var actualIsBlocked = assessment.OverallOutcome == OutboundPolicyOutcome.Blocked;
            expectedBlocked += expectedIsBlocked ? 1 : 0;
            actualBlocked += actualIsBlocked ? 1 : 0;
            falseAllowed += expectedIsBlocked && !actualIsBlocked ? 1 : 0;
            falseBlocked += !expectedIsBlocked && actualIsBlocked ? 1 : 0;
            blockedProviderInvocations += expectedIsBlocked ? provider.Attempts - attemptsBefore : 0;
            diagnosticLeaks += ContainsSensitiveValue(JsonSerializer.Serialize(assessment), item.SensitiveValues) ? 1 : 0;

            var blockedPolicyIds = assessment.Results
                .Where(result => result.Outcome == OutboundPolicyOutcome.Blocked)
                .Select(result => result.PolicyId)
                .ToArray();
            Assert.True(
                item.ExpectedBlockedPolicyIds.SequenceEqual(blockedPolicyIds, StringComparer.Ordinal),
                $"{item.Id}: expected [{string.Join(", ", item.ExpectedBlockedPolicyIds)}], actual [{string.Join(", ", blockedPolicyIds)}].");
        }

        return new SecurityMetrics(
            suite.Cases.Count,
            expectedBlocked,
            actualBlocked,
            falseAllowed,
            falseBlocked,
            diagnosticLeaks,
            blockedProviderInvocations);
    }

    private static async Task<SecuritySuite> LoadSuiteAsync()
    {
        var path = Path.Combine(FindRepositoryRoot(), "evaluations", "security-outbound-suite.json");
        await using var stream = File.OpenRead(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        var suite = await JsonSerializer.DeserializeAsync<SecuritySuite>(stream, options);
        Assert.NotNull(suite);
        Assert.Equal(1, suite.SchemaVersion);
        Assert.Equal(16, suite.Cases.Count);
        return suite;
    }

    private static bool ContainsSensitiveValue(string output, IReadOnlyList<string> sensitiveValues) =>
        sensitiveValues.Any(value => output.Contains(value, StringComparison.Ordinal));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "EngineeringBrain.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("EngineeringBrain.sln was not found.");
    }

    private sealed class FixedSecretValueSource(IEnumerable<string> values) : IOutboundSecretValueSource
    {
        private readonly IReadOnlySet<string> _values = new HashSet<string>(values, StringComparer.Ordinal);

        public IReadOnlySet<string> GetValues() => _values;
    }

    private sealed class CountingProvider : IReasoningProvider
    {
        public string Name => "Offline security fixture";

        public int Attempts { get; private set; }

        public Task<ReasoningResult<T>> GenerateStructuredAsync<T>(
            ApprovedReasoningRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request.EnsureIntegrity();
            Attempts++;
            return Task.FromResult(new ReasoningResult<T>(
                default!,
                new ReasoningCallUsage(request.Request.Stage, Name, request.Request.Model, 0, 0, 0, 0, 0, 0)));
        }
    }

    private sealed record SecurityMetrics(
        int Cases,
        int ExpectedBlocked,
        int ActualBlocked,
        int FalseAllowed,
        int FalseBlocked,
        int DiagnosticLeaks,
        int BlockedProviderInvocations);

    private sealed record SecuritySuite(int SchemaVersion, string Id, IReadOnlyList<SecurityCase> Cases);

    private sealed record SecurityCase(
        string Id,
        ReasoningStage Stage,
        OutboundAssessmentKind AssessmentKind,
        string SystemInstructions,
        string UserData,
        IReadOnlyList<SecuritySegment> Segments,
        IReadOnlyList<string>? SourceReferences,
        IReadOnlyList<string> KnownSecretValues,
        IReadOnlyList<string> SensitiveValues,
        OutboundPolicyOutcome ExpectedOutcome,
        IReadOnlyList<string> ExpectedBlockedPolicyIds);

    private sealed record SecuritySegment(
        ContextSegmentKind Kind,
        string Content,
        string? SourceIdentity);
}
