using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace MintPlayer.AdminHelper;

public static class AdminHelper
{
    [DllImport("libc")]
    static extern uint geteuid();

    static bool IsRunningAsAdmin()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        else
        {
            return geteuid() == 0;
        }
    }

    static string QuoteArgument(string arg)
    {
        // If arg contains spaces or quotes, escape it properly
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows rules: wrap in quotes and escape internal quotes
            return arg.Contains(' ') || arg.Contains('"')
                ? "\"" + arg.Replace("\"", "\\\"") + "\""
                : arg;
        }
        else
        {
            // Unix rules: single-quote everything, escape existing single quotes
            return "'" + arg.Replace("'", "'\\''") + "'";
        }
    }

    static void RestartAsAdmin()
    {
        var exeName = Process.GetCurrentProcess().MainModule!.FileName!;
        var args = Environment.GetCommandLineArgs().Skip(1).Select(QuoteArgument);
        var joinedArgs = string.Join(" ", args);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var psi = new ProcessStartInfo(exeName, joinedArgs)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(psi);
        }
        else
        {
            var psi = new ProcessStartInfo("sudo", $"{QuoteArgument(exeName)} {joinedArgs}")
            {
                UseShellExecute = false
            };
            Process.Start(psi);
        }

        Environment.Exit(0);
    }

    /// <summary>
    /// Ensures that the current process is running with administrative privileges, restarting the application with
    /// elevated rights if necessary.
    /// </summary>
    /// <remarks>
    /// If the process is not running as an administrator, this method attempts to restart the
    /// application with elevated permissions. The current process will terminate if elevation is required. Use this
    /// method at the start of your application to enforce administrative access for operations that require it.
    /// </remarks>
    public static void EnsureRunningAsAdmin()
    {
        if (!IsRunningAsAdmin())
            RestartAsAdmin();
    }
}
