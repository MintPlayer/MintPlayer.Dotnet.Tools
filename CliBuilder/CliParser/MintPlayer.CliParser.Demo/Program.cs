// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MintPlayer.CliParser.Abstractions;
using MintPlayer.CliParser.Extensions;
using System.Diagnostics;

Console.WriteLine("Hello, World!");

var host = new HostBuilder()
    .ConfigureServices((context, services) =>
    {
        // Register your services here
        services.AddCliParser();
    })
    .Build();

var cliParser = host.Services.GetRequiredService<ICliParser>();
var result = cliParser.ParseArguments(args);
Debugger.Break();