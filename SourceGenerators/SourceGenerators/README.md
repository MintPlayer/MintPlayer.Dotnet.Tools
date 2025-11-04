# Dependency-Injection generators
This library contains source-generators to simplify dependency-injection in your application

## Getting started
You need to install both [`MintPlayer.SourceGenerators`](https://nuget.org/packages/MintPlayer.SourceGenerators) and [`MintPlayer.SourceGenerators.Attributes`](https://nuget.org/packages/MintPlayer.SourceGenerators.Attributes) packages in your project.

## Service-registration

Place this class in your abstractions library:

```csharp
public interface ICustomerService { }
```

Place this class in your implementation library:

```csharp
[Register(typeof(ICustomerService), "DemoServices")]
internal class CustomerService : ICustomerService { }
```

Now you get an extension method generated for you, which will register all services for you:

```csharp
var services = new ServiceCollection()
    .AddDemoServices()
    .BuildServiceProvider();
```

## Dependency Injection

Inject a registered service anywhere:

```csharp
public partial class CustomerController {
    [Inject] private readonly ICustomerService customerService;
}
```

The source-generator will generate the constructor for you. It supports DI when using inheritance too.

## Interface Implementation

There's also an analyzer that will check if all `public` class members are known on the implemented interface. The analyzer provides a code-fix to add the missing members.

```csharp
public interface ICustomerService { }

[Register(typeof(ICustomerService), "DemoServices")]
internal class CustomerService : ICustomerService {
    public Task<Customer> GetCustomer(int id) => throw new NotImplementedException();
}
```

This also works when the interface resides in an abstractions-library and the class resides in an implementation-library. Which is why this analyzer is so powerfull.

## Command-line applications

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