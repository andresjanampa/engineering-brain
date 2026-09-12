using EngineeringBrain.Analyzers.CSharp;

namespace EngineeringBrain.Analyzers.CSharp.Tests;

public sealed class SafeDiagnosticTests
{
    [Fact]
    public void Message_RedactsPathsCredentialsAndSensitiveAssignments()
    {
        const string repositoryRoot = "C:\\work\\private-repository";
        var message = SafeDiagnostic.Message(
            "Failed in C:\\work\\private-repository. token=abc123 password: 'hidden value' https://user:pass@example.test/path",
            repositoryRoot);

        Assert.DoesNotContain(repositoryRoot, message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("abc123", message, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden value", message, StringComparison.Ordinal);
        Assert.DoesNotContain("user:pass", message, StringComparison.Ordinal);
        Assert.Contains("<redacted>", message, StringComparison.Ordinal);
    }
}
