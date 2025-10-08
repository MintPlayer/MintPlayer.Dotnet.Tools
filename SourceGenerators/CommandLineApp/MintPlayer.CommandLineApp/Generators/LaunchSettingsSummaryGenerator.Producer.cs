using MintPlayer.CommandLineApp.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.CommandLineApp.Generators;

public class LaunchSettingsSummaryProducer : Producer
{
    private readonly IEnumerable<ConsoleApp> consoleApps;
    private readonly bool alreadyHasTopLevelStatements;
    public LaunchSettingsSummaryProducer(IEnumerable<Models.ConsoleApp> consoleApps, bool alreadyHasTopLevelStatements, string rootNamespace) : base(rootNamespace, "LaunchSettingsSummary.g.cs")
    {
        this.consoleApps = consoleApps;
        this.alreadyHasTopLevelStatements = alreadyHasTopLevelStatements;
    }
    
    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine("""
            global::System.Console.WriteLine("Hello, World!");
            """);
    }
}
