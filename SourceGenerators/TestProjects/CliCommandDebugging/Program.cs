using CliCommandDebugging;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddDemoCommandTree();
builder.Services.AddServices();

var app = builder.Build();
var exitCode = await app.Services.InvokeDemoCommandAsync(args);
return exitCode;