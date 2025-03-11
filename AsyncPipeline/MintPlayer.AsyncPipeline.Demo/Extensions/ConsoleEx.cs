namespace MintPlayer.AsyncPipeline.Demo.Extensions;

public static class ConsoleEx
{
    public static void WriteLine(string message)
        => Console.WriteLine($"\x1b[92m[{DateTime.Now:HH:mm:ss.ffffff}] \x1b[39m{message}");
}
