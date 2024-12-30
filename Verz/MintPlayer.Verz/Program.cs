// dotnet tool install --global MintPlayer.Verz

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using MintPlayer.Verz;

var app = Host.CreateDefaultBuilder()
    .ConfigureAppConfiguration((context, config) => { })
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<IApp, App>();
        services.AddSingleton<IHelper, App>();

        services
            //.AddNugetOrgRegistry()
            .AddNpmjsComRegistry()
            .AddGithubPackageRegistry("MintPlayer", context.Configuration.GetValue<string>("GithubPAT")!)
            ;
        services
            .AddDotnetSDK()
            .AddNodejsSDK();
    })
    .Build();

var runner = app.Services.GetRequiredService<IApp>();
var helper = app.Services.GetRequiredService<IHelper>();
if (args.Length is 1 && args[0] is "--help")
{
    await helper.ShowUsage();
    return;
}

#if DEBUG
await runner.Run(args);
#else
try
{
    await runner.Run(args);
}
catch (Exception ex)
{
    await helper.ShowUsage();
}
#endif

internal interface IApp
{
    Task Run(string[] args);
}

internal interface IHelper
{
    Task ShowUsage();
}

//public interface IAllDotnetPackageSources
//{

//}

//internal class AllDotnetPackageSources : IAllDotnetPackageSources
//{

//}