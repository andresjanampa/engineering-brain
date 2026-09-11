using System.Diagnostics;
using EngineeringBrain.Core;

namespace EngineeringBrain.Infrastructure;

public sealed class GitInfoProvider : IGitInfoProvider
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

    public async Task<GitInfo> GetInfoAsync(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var isRepository = await RunGitAsync(repositoryRoot, cancellationToken, "rev-parse", "--is-inside-work-tree");
        if (!string.Equals(isRepository, "true", StringComparison.OrdinalIgnoreCase))
        {
            return new GitInfo(false, null, null, null, null);
        }

        var branch = await RunGitAsync(repositoryRoot, cancellationToken, "branch", "--show-current");
        var head = await RunGitAsync(repositoryRoot, cancellationToken, "rev-parse", "HEAD");
        var remote = SanitizeRemote(
            await RunGitAsync(repositoryRoot, cancellationToken, "remote", "get-url", "origin"));
        var status = await RunGitAsync(repositoryRoot, cancellationToken, "status", "--porcelain");

        var reportedBranch = branch switch
        {
            null => null,
            "" => "(detached HEAD)",
            _ => branch
        };
        bool? isWorkingTreeClean = status is null ? null : status.Length == 0;

        return new GitInfo(
            true,
            reportedBranch,
            head,
            remote,
            isWorkingTreeClean);
    }

    private static async Task<string?> RunGitAsync(
        string repositoryRoot,
        CancellationToken cancellationToken,
        params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.StartInfo.ArgumentList.Add("-C");
        process.StartInfo.ArgumentList.Add(repositoryRoot);
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            if (!process.Start())
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = await outputTask;
            await errorTask;
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static string? SanitizeRemote(string? remote)
    {
        if (string.IsNullOrWhiteSpace(remote)
            || !Uri.TryCreate(remote, UriKind.Absolute, out var uri)
            || uri.IsFile)
        {
            return remote;
        }

        var sanitized = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        };

        return sanitized.Uri.AbsoluteUri;
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
