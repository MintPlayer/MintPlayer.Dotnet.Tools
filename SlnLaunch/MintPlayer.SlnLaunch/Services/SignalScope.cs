using System.Runtime.InteropServices;

namespace MintPlayer.SlnLaunch.Services;

/// <summary>
/// Bridges OS termination signals to a <see cref="CancellationTokenSource"/> for the duration of a run:
/// Ctrl+C (all platforms) via <see cref="Console.CancelKeyPress"/>, and SIGTERM (POSIX, e.g. container stop)
/// via <see cref="PosixSignalRegistration"/>. The first signal requests graceful teardown; the process is
/// kept alive (the default terminate is suppressed) so the orchestrator can kill the child trees itself.
/// </summary>
internal sealed class SignalScope : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly IConsoleService _console;
    private readonly PosixSignalRegistration? _sigterm;
    private int _count;

    public SignalScope(CancellationTokenSource cts, IConsoleService console)
    {
        _cts = cts;
        _console = console;
        Console.CancelKeyPress += OnCancelKeyPress;
        // SIGTERM isn't surfaced through CancelKeyPress; register it explicitly on POSIX.
        if (!OperatingSystem.IsWindows())
            _sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSigTerm);
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true; // keep the process alive so teardown can run.
        Trigger();
    }

    private void OnSigTerm(PosixSignalContext context)
    {
        context.Cancel = true;
        Trigger();
    }

    private void Trigger()
    {
        if (Interlocked.Increment(ref _count) == 1)
            _console.WriteWarning("Shutdown requested — stopping projects… (press Ctrl+C again to force)");

        try { _cts.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= OnCancelKeyPress;
        _sigterm?.Dispose();
    }
}
