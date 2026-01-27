namespace Solve.Services;

/// <summary>
/// Service for console output with color support.
/// </summary>
public interface IConsoleService
{
    void WriteLine(string message = "");
    void WriteInfo(string message);
    void WriteSuccess(string message);
    void WriteWarning(string message);
    void WriteError(string message);
    void WriteHeader(string message);
    bool Confirm(string message);
    string? Prompt(string message);

    /// <summary>
    /// Displays instructions for installing the GitHub CLI.
    /// </summary>
    void WriteGhInstallInstructions();

    /// <summary>
    /// Displays instructions for authenticating with the GitHub CLI.
    /// </summary>
    void WriteGhAuthInstructions();
}
