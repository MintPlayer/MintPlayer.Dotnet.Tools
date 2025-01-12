// dotnet tool install --global MintPlayer.Verz

using System.CommandLine;
using System.Diagnostics;
using System.Reflection;

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

        var packagesCommand = new Command("packages", "Manage globally installed nuget packages");
        rootCommand.AddCommand(packagesCommand);

        var listPackagesCommand = new Command("list", "Lists all globally installed nuget packages");
        packagesCommand.AddCommand(listPackagesCommand);
        listPackagesCommand.SetHandler(async () =>
        {
            // TODO: read verz.json file from cwd, read valid verz subpackages property
            // and only then load the assembly + search for a class that implements a specific interface

            var stp = new Stopwatch();
            stp.Start();
            var globalPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
            var modules = Directory.GetDirectories(globalPath);
            var moduleAssemblies = await Task.WhenAll(modules.Select(m => AnalyzeModule(m)));
            var validModules = moduleAssemblies.Where(asm => asm is not null && asm.IsFullyTrusted).ToList();
            stp.Stop();
            var x = stp.ElapsedMilliseconds;
        });

        var statusCode = await rootCommand.InvokeAsync(args);
        return statusCode;
    }

    private static async Task<Assembly?> AnalyzeModule(string modulePath)
    {
        await Task.CompletedTask;

        var firstVersion = Directory.EnumerateDirectories(modulePath).FirstOrDefault();
        if (firstVersion == null) return null;

        var libFolder = Path.Combine(firstVersion, "lib");
        if (!Directory.Exists(libFolder)) return null;

        var moduleName = Path.GetFileName(modulePath);
        var entryPoint = Directory.EnumerateFiles(libFolder, $"{moduleName}.dll", SearchOption.AllDirectories).LastOrDefault();
        if (entryPoint == null) return null;

        try
        {
            var asm = Assembly.LoadFrom(entryPoint);
            return asm;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static async Task ReadFile(FileInfo file)
    {
        var lines = await File.ReadAllLinesAsync(file.FullName);
        lines.ToList().ForEach(line => Console.WriteLine(line));
    }
}