namespace MintPlayer.Verz.Abstractions;

public abstract class VerzException : Exception
{
    protected VerzException(int exitCode, string message) : base(message)
    {
        ExitCode = exitCode;
    }

    protected VerzException(int exitCode, string message, Exception inner) : base(message, inner)
    {
        ExitCode = exitCode;
    }

    public int ExitCode { get; }
}

public sealed class InitConflictException(string path)
    : VerzException(2, $"verz.json already exists at {path}; refusing to overwrite");

public sealed class NoTagsAtRefException(string @ref)
    : VerzException(3, $"no parseable {{PackageId}}/v{{semver}} tags at {@ref}");

public sealed class UnmatchedTagException(string packageId, string @ref)
    : VerzException(4, $"tag {packageId}/v* at {@ref} does not match any project discovered by any loaded SDK");

public sealed class ColdStartException(string packageId, string version)
    : VerzException(5, $"prior tag {packageId}/v{version} exists but no configured registry hosts that version");

public sealed class FrameworkDowngradeException(string packageId, int from, int to)
    : VerzException(6, $"{packageId}: framework major decreased ({from} -> {to}); semver cannot represent this");

public sealed class PublishFailureException(string detail)
    : VerzException(7, detail);

public sealed class NoArtifactsException()
    : VerzException(8, "publish produced zero artifacts; nothing to push");

public sealed class CycleException(IReadOnlyList<string> cyclePath)
    : VerzException(9, $"cycle in project graph: {string.Join(" -> ", cyclePath)}")
{
    public IReadOnlyList<string> CyclePath { get; } = cyclePath;
}
