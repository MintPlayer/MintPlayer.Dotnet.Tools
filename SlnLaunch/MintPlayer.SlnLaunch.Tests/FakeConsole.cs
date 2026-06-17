using MintPlayer.SlnLaunch.Services;

namespace MintPlayer.SlnLaunch.Tests;

internal sealed class FakeConsole : IConsoleService
{
    private readonly object _lock = new();
    private readonly List<string> _childLines = [];
    private readonly List<string> _messages = [];

    public IReadOnlyList<string> ChildLines { get { lock (_lock) return [.. _childLines]; } }
    public IReadOnlyList<string> Messages { get { lock (_lock) return [.. _messages]; } }

    public void WriteLine(string message = "") { lock (_lock) _messages.Add(message); }
    public void WriteInfo(string message) { lock (_lock) _messages.Add(message); }
    public void WriteSuccess(string message) { lock (_lock) _messages.Add(message); }
    public void WriteWarning(string message) { lock (_lock) _messages.Add(message); }
    public void WriteError(string message) { lock (_lock) _messages.Add(message); }

    public void WriteChildLine(string prefix, string line, ConsoleColor color, bool isError)
    {
        lock (_lock) _childLines.Add(line);
    }
}
