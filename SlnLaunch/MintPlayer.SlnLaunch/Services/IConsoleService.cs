namespace MintPlayer.SlnLaunch.Services;

/// <summary>
/// Console output for the tool. Writes are serialized so concurrent child output stays readable.
/// </summary>
public interface IConsoleService
{
    void WriteLine(string message = "");
    void WriteInfo(string message);
    void WriteSuccess(string message);
    void WriteWarning(string message);
    void WriteError(string message);

    /// <summary>
    /// Writes one line of a child process's output, with an optional colored <paramref name="prefix"/>.
    /// </summary>
    void WriteChildLine(string prefix, string line, ConsoleColor color, bool isError);
}
