using Microsoft.CodeAnalysis;
using MintPlayer.CommandLineApp.Models;
using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.Tools.Extensions;
using System.CodeDom.Compiler;

namespace MintPlayer.CommandLineApp.Generators;

public class LaunchSettingsSummaryProducer : Producer, IDiagnosticReporter
{
    private readonly IEnumerable<ConsoleApp> consoleApps;
    private readonly IEnumerable<Location> filesWithTopLevelStatements;
    public LaunchSettingsSummaryProducer(IEnumerable<ConsoleApp> consoleApps, IEnumerable<Location> filesWithTopLevelStatements, string rootNamespace) : base(rootNamespace, "LaunchSettingsSummary.g.cs")
    {
        this.consoleApps = consoleApps;
        this.filesWithTopLevelStatements = filesWithTopLevelStatements;
    }

    public IEnumerable<Diagnostic> GetDiagnostics()
    {
        if (consoleApps.Count() > 1)
            return consoleApps.Select(ca => DiagnosticRules.OnlyOneConsoleAppAllowed.Create(ca.ClassSymbolLocation));
        else
            return Enumerable.Empty<Diagnostic>();

        //return filesWithTopLevelStatements.Select(f => DiagnosticRules.CannotHaveTopLevelStatements.Create(f));
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        foreach (var consoleApp in consoleApps)
        {
            if (!string.IsNullOrEmpty(consoleApp.Namespace))
            {
                writer.WriteLine($"namespace {consoleApp.Namespace}");
                writer.WriteLine("{");
                writer.Indent++;
            }

            writer.WriteLine($"public static class {consoleApp.ClassName}App");
            writer.WriteLine("{");
            writer.Indent++;

            writer.WriteLine("public static async global::System.Threading.Tasks.Task Run(string[] args)");
            writer.WriteLine("{");
            writer.Indent++;

            ProduceMainMethod(writer, consoleApp);

            writer.Indent--;
            writer.WriteLine("}");

            writer.Indent--;
            writer.WriteLine("}");


            if (!string.IsNullOrEmpty(consoleApp.Namespace))
            {
                writer.Indent--;
                writer.WriteLine("}");
            }
        }
    }

    private void ProduceMainMethod(IndentedTextWriter writer, ConsoleApp consoleApp)
    {
        var description = consoleApp.Description ?? string.Empty;
        writer.WriteLine($""""
            var rootCommand = new System.CommandLine.RootCommand("{description.Replace("\"", "\\\"")}");
            """");
        writer.WriteLine($""""
            var parsed = rootCommand.Parse(args);
            """");
        writer.WriteLine($""""
            await parsed.InvokeAsync();
            """");
    }
}
