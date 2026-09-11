namespace EngineeringBrain.Infrastructure;

public sealed class RepositoryRootLocator
{
    public string Locate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var startingDirectory = File.Exists(fullPath)
            ? Path.GetDirectoryName(fullPath)!
            : fullPath;

        if (!Directory.Exists(startingDirectory))
        {
            throw new DirectoryNotFoundException($"Directory not found: {startingDirectory}");
        }

        var current = new DirectoryInfo(startingDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git"))
                || File.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return startingDirectory;
    }
}
