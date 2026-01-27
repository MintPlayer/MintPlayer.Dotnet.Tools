using Microsoft.Extensions.DependencyInjection;
using MintPlayer.SourceGenerators.Attributes;

namespace MintPlayer.Solve.Services;

[Register(typeof(IConsoleService), ServiceLifetime.Singleton, "SolveServices")]
public class ConsoleService : IConsoleService
{
    public void WriteLine(string message = "")
    {
        Console.WriteLine(message);
    }

    public void WriteInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public void WriteWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public void WriteHeader(string message)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    public bool Confirm(string message)
    {
        Console.Write($"{message} (y/n): ");
        var response = Console.ReadLine()?.Trim().ToLowerInvariant();
        return response == "y" || response == "yes";
    }

    public string? Prompt(string message)
    {
        Console.Write($"{message}: ");
        return Console.ReadLine()?.Trim();
    }

    public void WriteGhInstallInstructions()
    {
        WriteError("Error: GitHub CLI (gh) is not installed.");
        WriteLine();
        WriteLine("The solve tool requires the GitHub CLI to interact with GitHub.");
        WriteLine();
        WriteHeader("Installation instructions:");
        WriteLine();
        WriteLine("  Windows (winget):  winget install GitHub.cli");
        WriteLine("  Windows (scoop):   scoop install gh");
        WriteLine("  Windows (choco):   choco install gh");
        WriteLine("  macOS (Homebrew):  brew install gh");
        WriteLine("  Linux (apt):       sudo apt install gh");
        WriteLine("  Linux (dnf):       sudo dnf install gh");
        WriteLine();
        WriteLine("For other installation methods, visit:");
        WriteInfo("  https://cli.github.com/manual/installation");
        WriteLine();
        WriteLine("After installation, authenticate with:");
        WriteInfo("  gh auth login");
    }

    public void WriteGhAuthInstructions()
    {
        WriteError("Error: GitHub CLI (gh) is not authenticated.");
        WriteLine();
        WriteLine("Please authenticate with GitHub by running:");
        WriteInfo("  gh auth login");
        WriteLine();
        WriteLine("Follow the prompts to complete authentication.");
        WriteLine("You can choose to authenticate via:");
        WriteLine("  - Browser (recommended)");
        WriteLine("  - Personal access token");
        WriteLine();
        WriteLine("For more information, visit:");
        WriteInfo("  https://cli.github.com/manual/gh_auth_login");
    }
}
