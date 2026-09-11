using System.Diagnostics;
using EngineeringBrain.Infrastructure;

namespace EngineeringBrain.Core.Tests;

public sealed class GitInfoProviderTests
{
    [Fact]
    public async Task GetRenamesAsync_ReportsCommittedRenameProvenByGit()
    {
        using var repository = new GitRepositoryFixture();
        var previous = repository.CommitFile("Old.cs", "public class Value { }", "initial");
        repository.Run("mv", "Old.cs", "New.cs");
        repository.Run("commit", "-m", "rename");
        var current = repository.Run("rev-parse", "HEAD");

        var renames = await new GitInfoProvider().GetRenamesAsync(
            repository.Root,
            previous,
            current);

        Assert.Contains(renames, rename => rename.PreviousPath == "Old.cs" && rename.CurrentPath == "New.cs");
    }

    [Fact]
    public async Task GetRenamesAsync_ReportsStagedWorkingTreeRenameAtSameHead()
    {
        using var repository = new GitRepositoryFixture();
        var head = repository.CommitFile("Old.cs", "public class Value { }", "initial");
        repository.Run("mv", "Old.cs", "New.cs");

        var renames = await new GitInfoProvider().GetRenamesAsync(
            repository.Root,
            head,
            head);

        Assert.Contains(renames, rename => rename.PreviousPath == "Old.cs" && rename.CurrentPath == "New.cs");
    }

    private sealed class GitRepositoryFixture : IDisposable
    {
        public GitRepositoryFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), $"engineering-brain-git-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Root);
            Run("init", "-b", "main");
            Run("config", "user.email", "engineering-brain@example.invalid");
            Run("config", "user.name", "Engineering Brain Tests");
        }

        public string Root { get; }

        public string CommitFile(string path, string contents, string message)
        {
            File.WriteAllText(Path.Combine(Root, path), contents);
            Run("add", path);
            Run("commit", "-m", message);
            return Run("rev-parse", "HEAD");
        }

        public string Run(params string[] arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    WorkingDirectory = Root,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add("-C");
            process.StartInfo.ArgumentList.Add(Root);
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, error);
            return output.Trim();
        }

        public void Dispose()
        {
            foreach (var path in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(Root, recursive: true);
                    return;
                }
                catch (UnauthorizedAccessException) when (attempt < 4)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }
}
