using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MintPlayer.CliGenerator.Attributes;
using MintPlayer.SlnLaunch;
using MintPlayer.SlnLaunch.Models;

// Everything after the first standalone "--" is the forwardable-argument pool: each project picks the
// names it opted into via its ForwardArguments. Split it off here — the CLI source generator can't capture
// trailing tokens, and they'd otherwise be parse errors.
var separator = Array.IndexOf(args, "--");
var cliArgs = separator >= 0 ? args[..separator] : args;
var passthrough = separator >= 0 ? args[(separator + 1)..] : [];

var builder = Host.CreateApplicationBuilder(cliArgs);
builder.Services
    .AddSlnLaunchCommand()
    .AddSlnLaunchServices();
builder.Services.AddSingleton(ForwardableArguments.Parse(passthrough));

var app = builder.Build();

try
{
    return await app.InvokeSlnLaunchCommandAsync(cliArgs);
}
catch (ParseCommandException parseEx)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("Error parsing command:");
    foreach (var error in parseEx.Errors)
        Console.Error.WriteLine($"  {error}");
    Console.ResetColor();
    return 1;
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.ResetColor();
    return 1;
}
