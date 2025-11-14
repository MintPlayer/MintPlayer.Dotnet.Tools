using CliCommandDebugging;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddDemoCommand()
    .AddGreetingServices();

var app = builder.Build();
var exitCode = await app.InvokeDemoCommandAsync(args);
return exitCode;