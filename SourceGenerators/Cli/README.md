# CLI command generators

Create structured CLI applications with nested commands and dependency injection support using the `CliCommand` source generator.

1. Annotate your root command with `[CliRootCommand]` and mark each command (and subcommand) with `[CliCommand]`.
2. Define command options with `[CliOption]` and positional arguments with `[CliArgument]` on mutable properties.
3. Implement `Execute`, `ExecuteAsync`, or `ExecuteAsync(CancellationToken)` handlers to run the command.
4. Register your commands with the generated `Add<MyCommand>CommandTree` extension and invoke them through the generated helpers.

```csharp
[CliRootCommand(Name = "demo", Description = "Sample CLI")]
public partial class DemoCommand
{
    [CliOption("--verbose", "-v")] public bool Verbose { get; set; }

    public void Execute()
    {
        if (Verbose)
        {
            Console.WriteLine("Verbose mode active");
        }
    }

    [CliCommand("greet", Description = "Greets a person")]
    public partial class Greet
    {
        private readonly IGreetingService greetingService;

        public Greet(IGreetingService greetingService) => this.greetingService = greetingService;

        [CliArgument(0)] public string Name { get; set; } = "world";
        [CliOption("--times", DefaultValue = 1)] public int Times { get; set; }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            for (var i = 0; i < Times; i++)
            {
                await greetingService.GreetAsync(Name, cancellationToken);
            }
        }
    }
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDemoCommandTree();
builder.Services.AddSingleton<IGreetingService, GreetingService>();
var app = builder.Build();
return await app.Services.InvokeDemoCommandAsync(args);
```

The generator creates:

- Scoped service registrations for all commands in the tree.
- Methods that build `System.CommandLine` command hierarchies and wire options and arguments automatically.
- Extensions on `IServiceCollection` and `IServiceProvider` to register and execute the command tree.
