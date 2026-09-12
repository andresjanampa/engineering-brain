namespace EngineeringBrain.Infrastructure;

public sealed class RepositoryScanPolicy
{
    private const long MaximumHashableFileSize = 20 * 1024 * 1024;

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        "bin",
        "obj",
        "node_modules",
        "packages",
        "dist",
        "build",
        "coverage",
        "TestResults"
    };

    private static readonly IReadOnlyDictionary<string, string> Languages =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = "C#",
            [".csproj"] = "MSBuild",
            [".sln"] = "Solution",
            [".js"] = "JavaScript",
            [".ts"] = "TypeScript",
            [".sql"] = "SQL",
            [".cshtml"] = "Razor",
            [".json"] = "JSON",
            [".xml"] = "XML",
            [".yml"] = "YAML",
            [".yaml"] = "YAML",
            [".py"] = "Python",
            [".java"] = "Java"
        };

    public bool ShouldExcludeDirectory(string directoryName) =>
        ExcludedDirectories.Contains(directoryName);

    public string DetectLanguage(string fileName)
    {
        var extension = GetExtension(fileName);
        return Languages.TryGetValue(extension, out var language) ? language : "Other";
    }

    public string GetExtension(string fileName)
    {
        if (fileName.Equals(".env", StringComparison.OrdinalIgnoreCase))
        {
            return ".env";
        }

        return Path.GetExtension(fileName).ToLowerInvariant();
    }

    public bool ShouldHash(FileInfo file)
    {
        if (file.Length > MaximumHashableFileSize
            || IsPotentiallySensitive(file.Name)
            || !Languages.ContainsKey(GetExtension(file.Name)))
        {
            return false;
        }

        return true;
    }

    private static bool IsPotentiallySensitive(string fileName)
    {
        var normalized = fileName.ToLowerInvariant();

        return normalized.StartsWith(".env", StringComparison.Ordinal)
            || normalized == "appsettings.json"
            || (normalized.StartsWith("appsettings.", StringComparison.Ordinal)
                && normalized.EndsWith(".json", StringComparison.Ordinal))
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("credential", StringComparison.Ordinal);
    }
}
