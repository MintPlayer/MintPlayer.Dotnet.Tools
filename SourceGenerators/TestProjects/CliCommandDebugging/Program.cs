using CliCommandDebugging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDemoCommandTree();
builder.Services.AddSingleton<IGreetingService, GreetingService>();

var app = builder.Build();
var exitCode = await app.Services.InvokeDemoCommandAsync(args);
return exitCode;