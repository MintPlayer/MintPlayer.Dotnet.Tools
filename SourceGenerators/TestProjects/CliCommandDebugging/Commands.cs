using System;
using System.Threading;
using System.Threading.Tasks;
using MintPlayer.SourceGenerators.Attributes;
using MintPlayer.CliGenerator.Attributes;

namespace CliCommandDebugging;

[CliRootCommand(Name = "demo", Description = "Demonstrates the CLI command generator")]
public partial class DemoCommand
{
    [CliOption("--verbose", "-v", Description = "Enable verbose output")]
    public bool Verbose { get; set; }

    public void Execute()
    {
        if (Verbose)
        {
            Console.WriteLine("Running demo command in verbose mode");
        }
    }

    [CliCommand("greet", Description = "Greets a person")]
    public partial class Greet
    {
        private readonly IGreetingService greetingService;

        public Greet(IGreetingService greetingService)
        {
            this.greetingService = greetingService;
        }

        [CliArgument(0, Name = "name", Description = "Person to greet")]
        public string Name { get; set; } = "world";

        [CliOption("--times", "-t", Description = "Number of times to greet", DefaultValue = 1)]
        public int Times { get; set; }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            for (var i = 0; i < Times; i++)
            {
                await greetingService.GreetAsync(Name, cancellationToken);
            }
        }

        [CliCommand("shout", Description = "Greets a person loudly")]
        public partial class Shout
        {
            private readonly IGreetingService greetingService;

            public Shout(IGreetingService greetingService)
            {
                this.greetingService = greetingService;
            }

            [CliArgument(0, Name = "name", Description = "Person to greet loudly")]
            public string Target { get; set; } = "team";

            public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
            {
                await greetingService.GreetAsync(Target.ToUpperInvariant(), cancellationToken);
                return 0;
            }
        }
    }
}

public interface IGreetingService
{
    Task GreetAsync(string name, CancellationToken cancellationToken);
}

public sealed class GreetingService : IGreetingService
{
    public Task GreetAsync(string name, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Hello, {name}!");
        return Task.CompletedTask;
    }
}
