# CLI command generators

Create structured CLI applications with nested commands and dependency injection support using the `CliCommand` source generator.

Install both [`MintPlayer.CliGenerator`](https://nuget.org/packages/MintPlayer.CliGenerator) and [`MintPlayer.CliGenerator.Attributes`](https://nuget.org/packages/MintPlayer.CliGenerator.Attributes). The generator is a development dependency: a `dotnet add package` reference to it does not make the attributes compile (CS0246), so the attributes package must be referenced explicitly. The generated code uses `Microsoft.Extensions.Hosting` (`IHost`), so the project needs that package as well.

1. Annotate your root command with `[CliRootCommand]` and mark each command (and subcommand) with `[CliCommand]`.
2. Define command options with `[CliOption]` and positional arguments with `[CliArgument]` on mutable properties.
3. Implement `MintPlayer.CliGenerator.Attributes.ICliCommand` so each command exposes an `Execute(CancellationToken)` handler returning a task-based exit code.
4. Register the tree with the generated `Add{RootCommand}()` extension on `IServiceCollection`, and run it with `Invoke{RootCommand}Async(args)` on the built `IHost` (`Build{RootCommand}()` returns the `System.CommandLine` root command).
5. Use `[CliParentCommand(typeof(ParentCommand))]` on a non-nested partial command class to attach it under an existing command without nesting the type in code. This enables splitting large command trees across multiple files.

```csharp
[CliRootCommand(Name = "demo", Description = "Sample CLI")]
public partial class DemoCommand : ICliCommand
{
    [CliOption("--verbose", "-v")] public bool Verbose { get; set; }

    public Task<int> Execute(CancellationToken cancellationToken)
    {
        if (Verbose)
        {
            Console.WriteLine("Verbose mode active");
        }
        return Task.FromResult(0);
    }
}

// In another file:
[CliCommand("greet", Description = "Greets a person")]
[CliParentCommand(typeof(DemoCommand))]
public partial class Greet : ICliCommand
{
    private readonly IGreetingService greetingService;
    public Greet(IGreetingService greetingService) => this.greetingService = greetingService;

    [CliArgument(0)] public string Name { get; set; } = "world";
    [CliOption("--times", DefaultValue = 1)] public int Times { get; set; }

    public async Task<int> Execute(CancellationToken cancellationToken)
    {
        for (var i = 0; i < Times; i++)
            await greetingService.GreetAsync(Name, cancellationToken);
        return 0;
    }
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDemoCommand();
builder.Services.AddSingleton<IGreetingService, GreetingService>();
var app = builder.Build();
return await app.InvokeDemoCommandAsync(args);
```

The generator creates:

- Scoped service registrations for all commands in the tree.
- Methods that build `System.CommandLine` command hierarchies and wire options and arguments automatically.
- A `{RootCommand}CliExtensions` class with `Add{RootCommand}` on `IServiceCollection`, `Build{RootCommand}` on `IServiceProvider` and `Invoke{RootCommand}Async` on `IHost`, to register, build and execute the command tree.
- Recognition of non-nested partial command classes linked via `[CliParentCommand]`.

A command can be a `class` or a `record` (`record class`), at top level or nested in a partial class, record,
struct, record struct or interface. `Execute` receives the invocation's cancellation token, which is cancelled
on Ctrl+C.

Commands the generator cannot generate for are reported instead of producing code that does not compile. Every
rule is an error:

| ID | Reason |
|---|---|
| MINTCLI001 | The command does not implement `ICliCommand` |
| MINTCLI002 | The command is not `partial` |
| MINTCLI003 | The command is generic, or nested in a generic type |
| MINTCLI004 | The command is `static` |
| MINTCLI005 | A containing type is not `partial` |
| MINTCLI006 | The command, or a containing type, is `file`-local |
| MINTCLI007 | A root command is less accessible than `internal` |
| MINTCLI008 | A subcommand is not accessible from its parent command |
| MINTCLI009 | Two root commands share a type name, so their `{Name}CliExtensions` classes would collide |
