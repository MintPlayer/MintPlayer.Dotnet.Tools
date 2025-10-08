using MintPlayer.CommandLineApp.Models;
using MintPlayer.SourceGenerators.Tools;
using System.CodeDom.Compiler;

namespace MintPlayer.CommandLineApp.Generators;

public class LaunchSettingsSummaryProducer : Producer
{
    private readonly IEnumerable<ConsoleApp> consoleApps;
    public LaunchSettingsSummaryProducer(IEnumerable<Models.ConsoleApp> consoleApps, string rootNamespace) : base(rootNamespace, "LaunchSettingsSummary.g.cs")
    {
        this.consoleApps = consoleApps;
    }
    
    protected override void ProduceSource(IndentedTextWriter writer, CancellationToken cancellationToken)
    {
        writer.WriteLine("""
            global::System.Console.WriteLine("Hello, World!");
            """);
    }
}
