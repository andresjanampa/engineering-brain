using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class RepositoryScannerTests
{
    [Fact]
    public async Task ScanAsync_DetectsFilesHonorsExclusionsAndCountsLanguages()
    {
        using var fixture = new TemporaryDirectory();
        fixture.Write("src/Feature.cs", "public class Feature { }");
        fixture.Write("web/app.js", "export const value = 1;");
        fixture.Write("database/schema.sql", "select 1;");
        fixture.Write(".env", "TOKEN=not-a-real-token");
        fixture.Write("appsettings.json", "{ \"ConnectionString\": \"not-real\" }");
        fixture.Write("bin/Ignored.cs", "public class Ignored { }");
        fixture.Write("node_modules/ignored.js", "throw new Error();");

        var result = await new RepositoryScanner().ScanAsync(fixture.Path);

        Assert.Contains(result.Files, file => file.RelativePath == "src/Feature.cs");
        Assert.Contains(result.Files, file => file.RelativePath == "src/Feature.cs" && file.ContentHash is not null);
        Assert.Contains(result.Files, file => file.RelativePath == "web/app.js");
        Assert.Contains(result.Files, file => file.RelativePath == ".env" && file.ContentHash is null);
        Assert.Contains(result.Files, file => file.RelativePath == "appsettings.json" && file.ContentHash is null);
        Assert.DoesNotContain(result.Files, file => file.RelativePath.Contains("Ignored", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Files, file => file.RelativePath.Contains("node_modules", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, result.Languages.Single(item => item.Language == "C#").FileCount);
        Assert.Equal(1, result.Languages.Single(item => item.Language == "JavaScript").FileCount);
        Assert.Equal(1, result.Languages.Single(item => item.Language == "SQL").FileCount);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"engineering-brain-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Write(string relativePath, string contents)
        {
            var fullPath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
