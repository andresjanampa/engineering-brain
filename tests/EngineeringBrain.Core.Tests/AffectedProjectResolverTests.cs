using EngineeringBrain.Core;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class AffectedProjectResolverTests
{
    [Fact]
    public void Resolve_InvalidatesDirectProjectAndTransitiveDependentsOnly()
    {
        using var repository = new ProjectFixture();
        repository.Project("A/A.csproj");
        repository.Project("B/B.csproj", "../A/A.csproj");
        repository.Project("C/C.csproj");
        var graph = repository.Graph(Project("A/A.csproj"), Project("B/B.csproj", "A/A.csproj"), Project("C/C.csproj"));
        var change = Changed("A/Service.cs");

        var plan = new AffectedProjectResolver().Resolve(
            [change],
            graph,
            [Project("A/A.csproj"), Project("B/B.csproj", "A/A.csproj"), Project("C/C.csproj")]);

        Assert.Equal(["A/A.csproj"], plan.DirectlyAffectedProjects);
        Assert.Equal(["B/B.csproj"], plan.TransitivelyAffectedProjects);
        Assert.Equal(2, plan.ProjectsToAnalyze.Count);
        Assert.Equal(["C/C.csproj"], plan.ProjectsToReuse);
    }

    [Theory]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    [InlineData("global.json")]
    [InlineData("NuGet.Config")]
    public void Resolve_GlobalConfigurationForcesFullAnalysis(string path)
    {
        using var repository = new ProjectFixture();
        repository.Project("A/A.csproj");
        var graph = repository.Graph(Project("A/A.csproj"));

        var plan = new AffectedProjectResolver().Resolve(
            [Changed(path)],
            graph,
            [Project("A/A.csproj")]);

        Assert.True(plan.RequiresFullAnalysis);
        Assert.Contains("configuration", plan.FullAnalysisReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_SolutionChangeForcesFullAnalysis()
    {
        using var repository = new ProjectFixture();
        repository.Project("A/A.csproj");

        var plan = new AffectedProjectResolver().Resolve(
            [Changed("Demo.sln")],
            repository.Graph(Project("A/A.csproj")),
            [Project("A/A.csproj")]);

        Assert.True(plan.RequiresFullAnalysis);
        Assert.Contains("Solution", plan.FullAnalysisReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ChangedProjectFileInvalidatesItsDependents()
    {
        using var repository = new ProjectFixture();
        repository.Project("A/A.csproj");
        repository.Project("B/B.csproj", "../A/A.csproj");
        var previous = new[] { Project("A/A.csproj"), Project("B/B.csproj", "A/A.csproj") };

        var plan = new AffectedProjectResolver().Resolve(
            [Changed("A/A.csproj")],
            repository.Graph(previous),
            previous);

        Assert.Equal(["A/A.csproj", "B/B.csproj"], plan.ProjectsToAnalyze);
    }

    [Fact]
    public void Resolve_NewProjectIsAnalyzedWhileExistingProjectIsReused()
    {
        using var repository = new ProjectFixture();
        repository.Project("Existing/Existing.csproj");
        repository.Project("New/New.csproj");
        var previous = new[] { Project("Existing/Existing.csproj") };

        var plan = new AffectedProjectResolver().Resolve(
            [new FileChange(FileChangeKind.Added, "New/New.csproj", null, ChangeDetectionMethod.FileSystem, null, "added")],
            repository.Graph(previous),
            previous);

        Assert.Equal(["New/New.csproj"], plan.ProjectsToAnalyze);
        Assert.Equal(["Existing/Existing.csproj"], plan.ProjectsToReuse);
    }

    [Fact]
    public void Resolve_RemovedProjectInvalidatesCurrentDependents()
    {
        using var repository = new ProjectFixture();
        repository.Project("B/B.csproj", "../A/A.csproj");
        var previous = new[] { Project("A/A.csproj"), Project("B/B.csproj", "A/A.csproj") };

        var plan = new AffectedProjectResolver().Resolve(
            [new FileChange(FileChangeKind.Deleted, null, "A/A.csproj", ChangeDetectionMethod.FileSystem, null, "deleted")],
            repository.Graph(previous),
            previous);

        Assert.Contains("B/B.csproj", plan.ProjectsToAnalyze);
        Assert.DoesNotContain("A/A.csproj", plan.ProjectsToAnalyze);
    }

    private static FileChange Changed(string path) => new(
        FileChangeKind.Modified,
        path,
        path,
        ChangeDetectionMethod.ContentHash,
        null,
        "changed");

    private static ProjectInfo Project(string path, params string[] references) => new(
        $"id:{path}",
        Path.GetFileNameWithoutExtension(path),
        path,
        "C#",
        ["net10.0"],
        references,
        [],
        ProjectAnalysisMode.Semantic,
        []);

    private sealed class ProjectFixture : IDisposable
    {
        public ProjectFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"engineering-brain-impact-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Project(string path, string? reference = null)
        {
            var referenceXml = reference is null
                ? string.Empty
                : $"<ItemGroup><ProjectReference Include=\"{reference}\" /></ItemGroup>";
            Write(path, $"<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>{referenceXml}</Project>");
        }

        public ProjectDependencyGraph Graph(params ProjectInfo[] previous)
        {
            var files = Directory.EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories)
                .Select(path => new ScannedFile(
                    Path.GetRelativePath(Root, path).Replace('\\', '/'),
                    ".csproj",
                    "MSBuild",
                    new FileInfo(path).Length,
                    "hash"))
                .ToArray();
            return ProjectDependencyGraph.Create(Root, files, previous);
        }

        private void Write(string path, string contents)
        {
            var fullPath = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, contents);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
