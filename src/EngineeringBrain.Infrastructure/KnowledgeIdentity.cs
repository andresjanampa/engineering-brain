using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EngineeringBrain.Infrastructure;

public static class KnowledgeIdentity
{
    private const int MaximumReadableNameLength = 60;

    public static string CreateBranchKey(string branch)
    {
        var readable = Sanitize(branch, 48).ToLowerInvariant();
        return $"{readable}--{ShortHash(branch)}";
    }

    public static string CreateRepositoryKey(string repositoryId)
    {
        var readable = Sanitize(repositoryId, MaximumReadableNameLength);
        return string.Equals(readable, repositoryId, StringComparison.Ordinal)
            ? readable
            : $"{readable}--{ShortHash(repositoryId)}";
    }

    public static string CreateNoteFileName(string name, string sourceId) =>
        $"{Sanitize(name, MaximumReadableNameLength)}--{ShortHash(sourceId)}.md";

    public static string Fingerprint(params string?[] values) => Hash(
        string.Join("\n", values.Select(value => value ?? "<null>")));

    public static string ContentHash(string content) => Hash(NormalizeLineEndings(content));

    public static string NormalizeLineEndings(string content) =>
        content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

    public static string Quote(string value) => JsonSerializer.Serialize(value);

    public static string WikiTarget(string relativeMarkdownPath) =>
        relativeMarkdownPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? relativeMarkdownPath[..^3].Replace('\\', '/')
            : relativeMarkdownPath.Replace('\\', '/');

    private static string Sanitize(string value, int maximumLength)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSeparator = false;
        foreach (var character in value)
        {
            var safe = char.IsLetterOrDigit(character) || character is '.' or '-' or '_';
            var output = safe ? character : '-';
            if (output == '-' && previousWasSeparator)
            {
                continue;
            }

            builder.Append(output);
            previousWasSeparator = output == '-';
        }

        var result = builder.ToString().Trim(' ', '.', '-');
        if (string.IsNullOrWhiteSpace(result))
        {
            result = "unnamed";
        }

        if (result.Length > maximumLength)
        {
            result = result[..maximumLength].TrimEnd(' ', '.', '-');
        }

        return IsWindowsReservedName(result) ? $"_{result}" : result;
    }

    private static bool IsWindowsReservedName(string value)
    {
        var baseName = value.Split('.')[0];
        return baseName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (baseName.Length == 4
                && (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && baseName[3] is >= '1' and <= '9');
    }

    private static string ShortHash(string value) => Hash(value)[..12];

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
