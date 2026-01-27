using Microsoft.Extensions.Hosting;
using MintPlayer.CliGenerator.Attributes;
using MintPlayer.Solve;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddSolveCommand()
    .AddSolveServices();

var app = builder.Build();

try
{
    var exitCode = await app.InvokeSolveCommandAsync(args);
    return exitCode;
}
catch (ParseCommandException parseEx)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("Error parsing command:");
    foreach (var error in parseEx.Errors)
    {
        Console.Error.WriteLine($"  {error}");
    }
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
