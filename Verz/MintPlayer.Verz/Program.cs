// dotnet tool install --global MintPlayer.Verz

using System.CommandLine;

namespace MintPlayer.Verz;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var fileOption = new Option<FileInfo?>(
            name: "--file",
            description: "The file to read and display on the console.");

        var rootCommand = new RootCommand("Sample app for System.CommandLine");
        rootCommand.AddOption(fileOption);
        rootCommand.SetHandler(file => ReadFile(file!), fileOption);

        var statusCode = await rootCommand.InvokeAsync(args);
        return statusCode;
    }

    static async Task ReadFile(FileInfo file)
    {
        var lines = await File.ReadAllLinesAsync(file.FullName);
        lines.ToList().ForEach(line => Console.WriteLine(line));
    }
}