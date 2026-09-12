using System.Text.RegularExpressions;

namespace EngineeringBrain.Analyzers.CSharp;

internal static partial class SafeDiagnostic
{
    private const int MaximumLength = 300;

    public static string Message(Exception exception, string repositoryRoot) =>
        Message(exception.Message, repositoryRoot);

    public static string Message(string message, string repositoryRoot)
    {
        var sanitized = Whitespace().Replace(message, " ").Trim();
        sanitized = sanitized.Replace(repositoryRoot, "<repository>", StringComparison.OrdinalIgnoreCase);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            sanitized = sanitized.Replace(userProfile, "~", StringComparison.OrdinalIgnoreCase);
        }

        sanitized = SensitiveAssignment().Replace(sanitized, "$1=<redacted>");
        sanitized = UriUserInfo().Replace(sanitized, "${scheme}<redacted>@");

        return sanitized.Length <= MaximumLength
            ? sanitized
            : $"{sanitized[..MaximumLength]}...";
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(
        "(?i)\\b(password|pwd|token|secret|authorization|api[_-]?key|connectionstrings?)\\b\\s*[:=]\\s*(?:\"[^\"]*\"|'[^']*'|[^\\s;,]+)")]
    private static partial Regex SensitiveAssignment();

    [GeneratedRegex(@"(?i)(?<scheme>https?://)[^/@\s]+@")]
    private static partial Regex UriUserInfo();
}
