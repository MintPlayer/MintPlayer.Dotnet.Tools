namespace MintPlayer.SlnLaunch;

/// <summary>
/// Thrown for user-actionable problems (missing/ambiguous/malformed <c>.slnLaunch</c> files,
/// unresolvable projects). The message is meant to be shown to the user as-is, without a stack trace.
/// </summary>
public sealed class SlnLaunchException : Exception
{
    public SlnLaunchException(string message) : base(message) { }
    public SlnLaunchException(string message, Exception innerException) : base(message, innerException) { }
}
