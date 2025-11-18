using Microsoft.Extensions.DependencyInjection;
using MintPlayer.CliGenerator.Attributes;
using MintPlayer.SourceGenerators.Attributes;

namespace CliCommandDebugging;

[CliRootCommand(Name = "demo", Description = "Demonstrates the CLI command generator")]
public partial class DemoCommand : ICliCommand
{
    [CliOption("--verbose", "-v", Description = "Enable verbose output"), NoInterfaceMember]
    public bool Verbose { get; set; }

    public Task<int> Execute(CancellationToken cancellationToken)
    {
        if (Verbose)
        {
            Console.WriteLine("Running demo command in verbose mode");
        }

        return Task.FromResult(0);
    }

    [CliCommand("greet", Description = "Greets a person")]
    public partial class Greet : ICliCommand
    {
        private readonly IGreetingService greetingService;

        public Greet(IGreetingService greetingService)
        {
            this.greetingService = greetingService;
        }

        [CliArgument(0, Name = "name", Description = "Person to greet"), NoInterfaceMember]
        public string Name { get; set; } = "world";

        [CliOption("--times", "-t", Description = "Number of times to greet", DefaultValue = 1), NoInterfaceMember]
        public int Times { get; set; }

        public async Task<int> Execute(CancellationToken cancellationToken)
        {
            for (var i = 0; i < Times; i++)
            {
                await greetingService.GreetAsync(Name, cancellationToken);
            }

            return 0;
        }


        [CliCommand("farewell", Description = "Says farewell to a person")]
        public partial class Farewell : ICliCommand
        {
            public Task<int> Execute(CancellationToken cancellationToken)
            {
                var farewellMessage = $"Goodbye, {Name}!";
                if (!string.IsNullOrEmpty(MeetAgain))
                {
                    farewellMessage += $" See you again {MeetAgain}.";
                }
                Console.WriteLine(farewellMessage);
                return Task.FromResult(0);
            }

            [CliArgument(0, Name = "name", Description = "Name of the person to bid farewell"), NoInterfaceMember]
            public string Name { get; set; }

            [CliOption("--meet-again", "-m", Description = "When will we meet again"), NoInterfaceMember]
            public string MeetAgain { get; set; }
        }

    }
}

// Demonstrates that you can move subcommands to the root level too.
[CliCommand("shout", Description = "Greets a person loudly"), CliParentCommand(typeof(DemoCommand.Greet))]
public partial class Shout : ICliCommand
{
    private readonly IGreetingService greetingService;

    public Shout(IGreetingService greetingService)
    {
        this.greetingService = greetingService;
    }

    [CliArgument(0, Name = "name", Description = "Person to greet loudly"), NoInterfaceMember]
    public string Target { get; set; } = "team";

    public async Task<int> Execute(CancellationToken cancellationToken)
    {
        await greetingService.GreetAsync(Target.ToUpperInvariant(), cancellationToken);
        return 0;
    }
}

public interface IGreetingService
{
    Task GreetAsync(string name, CancellationToken cancellationToken);
}

[Register(typeof(IGreetingService), ServiceLifetime.Scoped, "GreetingServices")]
public sealed class GreetingService : IGreetingService
{
    public Task GreetAsync(string name, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Hello, {name}!");
        return Task.CompletedTask;
    }
}