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
    public LaunchSettingsSummaryProducer(IEnumerable<Models.ConsoleApp> consoleApps, IEnumerable<Location> filesWithTopLevelStatements, string rootNamespace) : base(rootNamespace, "LaunchSettingsSummary.g.cs")
    {
        this.consoleApps = consoleApps;
        this.filesWithTopLevelStatements = filesWithTopLevelStatements;
    }

    public IEnumerable<Diagnostic> GetDiagnostics()
    {
        return filesWithTopLevelStatements.Select(f => DiagnosticRules.CannotHaveTopLevelStatements.Create(f));
    }

    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        if (filesWithTopLevelStatements.Any())
        {
            writer.WriteLine($"namespace {RootNamespace}");
            writer.WriteLine("{");
            writer.Indent++;

            foreach (var consoleApp in consoleApps)
            {
                writer.WriteLine($"public class {consoleApp.ClassName}App");
                writer.WriteLine("{");
                writer.Indent++;

                writer.WriteLine("public static void Entrypoint(string[] args)");
                writer.WriteLine("{");
                writer.Indent++;

                ProduceMainMethod(writer, consoleApp);

                writer.Indent--;
                writer.WriteLine("}");

                writer.Indent--;
                writer.WriteLine("}");
            }

            writer.Indent--;
            writer.WriteLine("}");
        }
        else
        {
            writer.WriteLine("""
                global::System.Console.WriteLine("Hello, World!");
                """);
        }
    }

    private void ProduceMainMethod(IndentedTextWriter writer, ConsoleApp consoleApp)
    {
        writer.WriteLine("""
            global::System.Console.WriteLine("Hello, World!");
            """);
    }
}
