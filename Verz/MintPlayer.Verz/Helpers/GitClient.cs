using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace MintPlayer.Verz.Helpers;

/// <summary>
/// Thin wrapper around the <c>git</c> CLI. Shells out per call; the cost is
/// negligible at typical monorepo scale and avoids native-dependency packaging
/// concerns of LibGit2Sharp.
/// </summary>
public sealed class GitClient(ILogger<GitClient> logger)
{
    public string RevParse(string @ref, string workingDirectory)
    {
        var result = Run(workingDirectory, "rev-parse", @ref);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git rev-parse {@ref} failed (exit {result.ExitCode}): {result.Stderr.Trim()}");
        }
        return result.Stdout.Trim();
    }

    public IReadOnlyList<string> TagsPointingAt(string @ref, string workingDirectory)
    {
        var result = Run(workingDirectory, "tag", "--points-at", @ref);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git tag --points-at {@ref} failed (exit {result.ExitCode}): {result.Stderr.Trim()}");
        }
        return result.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    /// <summary>
    /// True if <c>git diff --quiet {sinceRef}..HEAD -- {relativePath}</c>
    /// reports any change. Exit code 0 = no diff, 1 = diff. Higher exit codes
    /// throw (likely a malformed ref or an unreadable repository).
    /// </summary>
    public bool HasChanges(string sinceRef, string relativePath, string workingDirectory)
    {
        var result = Run(workingDirectory, "diff", "--quiet", $"{sinceRef}..HEAD", "--", relativePath);
        return result.ExitCode switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidOperationException(
                $"git diff --quiet {sinceRef}..HEAD -- {relativePath} failed (exit {result.ExitCode}): {result.Stderr.Trim()}"),
        };
    }

    public void CreateTag(string tagName, string workingDirectory, bool annotated = false, string? message = null)
    {
        var args = annotated
            ? new[] { "tag", "-a", tagName, "-m", message ?? tagName }
            : new[] { "tag", tagName };
        var result = Run(workingDirectory, args);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git tag {tagName} failed (exit {result.ExitCode}): {result.Stderr.Trim()}");
        }
    }

    public void PushTags(string workingDirectory, string remote = "origin")
    {
        var result = Run(workingDirectory, "push", remote, "--tags", "--follow-tags");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git push {remote} --tags failed (exit {result.ExitCode}): {result.Stderr.Trim()}");
        }
    }

    public IReadOnlyList<string> TagList(string pattern, bool mergedHead, string workingDirectory)
    {
        var args = mergedHead
            ? new[] { "tag", "--list", pattern, "--merged", "HEAD" }
            : new[] { "tag", "--list", pattern };
        var result = Run(workingDirectory, args);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git tag --list {pattern} failed (exit {result.ExitCode}): {result.Stderr.Trim()}");
        }
        return result.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private GitResult Run(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        logger.LogDebug("git {Args} (in {Cwd})", string.Join(' ', args), workingDirectory);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("could not start git; is it on PATH?");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit();

        return new GitResult(proc.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}
