using CliCommandDebugging;
using Microsoft.Extensions.Hosting;
using MintPlayer.CliGenerator.Attributes;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddDemoCommand()
    .AddGreetingServices();

var app = builder.Build();
try
{
    var exitCode = await app.InvokeDemoCommandAsync(args);
    return exitCode;
}
catch (ParseCommandException parseEx)
{
	throw;
}