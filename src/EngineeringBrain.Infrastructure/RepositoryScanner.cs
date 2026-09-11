using System.Security.Cryptography;
using System.Text;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class RepositoryScanner : IRepositoryScanner
{
    private readonly RepositoryRootLocator _rootLocator;
    private readonly RepositoryScanPolicy _policy;

    public RepositoryScanner(
        RepositoryRootLocator? rootLocator = null,
        RepositoryScanPolicy? policy = null)
    {
        _rootLocator = rootLocator ?? new RepositoryRootLocator();
        _policy = policy ?? new RepositoryScanPolicy();
    }

    public async Task<RepositoryScanResult> ScanAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var root = _rootLocator.Locate(path);
        var files = new List<ScannedFile>();
        var pendingDirectories = new Queue<DirectoryInfo>();
        pendingDirectories.Enqueue(new DirectoryInfo(root));

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pendingDirectories.Dequeue();

            foreach (var child in EnumerateDirectoriesSafely(directory))
            {
                if (!_policy.ShouldExcludeDirectory(child.Name) && !IsReparsePoint(child))
                {
                    pendingDirectories.Enqueue(child);
                }
            }

            foreach (var file in EnumerateFilesSafely(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsReparsePoint(file))
                {
                    continue;
                }

                var relativePath = Path.GetRelativePath(root, file.FullName).Replace('\\', '/');
                var hash = _policy.ShouldHash(file)
                    ? await TryComputeHashAsync(file.FullName, cancellationToken)
                    : null;

                files.Add(new ScannedFile(
                    relativePath,
                    _policy.GetExtension(file.Name),
                    _policy.DetectLanguage(file.Name),
                    file.Length,
                    hash));
            }
        }

        files.Sort((left, right) =>
            StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath));

        var languages = files
            .GroupBy(file => file.Language, StringComparer.Ordinal)
            .Select(group => new LanguageStatistics(
                group.Key,
                group.Count(),
                group.Sum(file => file.SizeBytes)))
            .OrderByDescending(statistics => statistics.FileCount)
            .ThenBy(statistics => statistics.Language, StringComparer.Ordinal)
            .ToArray();

        var repository = new RepositoryInfo(
            CreateRepositoryId(root),
            new DirectoryInfo(root).Name,
            root);

        return new RepositoryScanResult(repository, files, languages);
    }

    private static IEnumerable<DirectoryInfo> EnumerateDirectoriesSafely(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateDirectories().ToArray();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static IEnumerable<FileInfo> EnumerateFilesSafely(DirectoryInfo directory)
    {
        try
        {
            return directory.EnumerateFiles().ToArray();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }

    private static bool IsReparsePoint(FileSystemInfo entry) =>
        (entry.Attributes & FileAttributes.ReparsePoint) != 0;

    private static async Task<string?> TryComputeHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            var hash = await SHA256.HashDataAsync(stream, cancellationToken);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string CreateRepositoryId(string root)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (OperatingSystem.IsWindows())
        {
            normalized = normalized.ToUpperInvariant();
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash).ToLowerInvariant()[..24];
    }
}
