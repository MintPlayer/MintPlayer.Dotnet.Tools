using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.SlnLaunch.Services;

[Register(typeof(IConsoleService), ServiceLifetime.Singleton, "SlnLaunchServices")]
internal sealed class ConsoleService : IConsoleService
{
    private readonly object _lock = new();
    private readonly bool _useColor =
        Environment.GetEnvironmentVariable("NO_COLOR") is null && !Console.IsOutputRedirected;

    public void WriteLine(string message = "") => Write(message, null, isError: false);
    public void WriteInfo(string message) => Write(message, ConsoleColor.Cyan, isError: false);
    public void WriteSuccess(string message) => Write(message, ConsoleColor.Green, isError: false);
    public void WriteWarning(string message) => Write(message, ConsoleColor.Yellow, isError: false);
    public void WriteError(string message) => Write(message, ConsoleColor.Red, isError: true);

    public void WriteChildLine(string prefix, string line, ConsoleColor color, bool isError)
    {
        lock (_lock)
        {
            var writer = isError ? Console.Error : Console.Out;
            if (!string.IsNullOrEmpty(prefix))
            {
                if (_useColor)
                {
                    Console.ForegroundColor = color;
                    writer.Write(prefix);
                    Console.ResetColor();
                }
                else
                {
                    writer.Write(prefix);
                }
            }
            writer.WriteLine(line);
        }
    }

    private void Write(string message, ConsoleColor? color, bool isError)
    {
        lock (_lock)
        {
            var writer = isError ? Console.Error : Console.Out;
            if (_useColor && color is not null)
            {
                Console.ForegroundColor = color.Value;
                writer.WriteLine(message);
                Console.ResetColor();
            }
            else
            {
                writer.WriteLine(message);
            }
        }
    }
}
