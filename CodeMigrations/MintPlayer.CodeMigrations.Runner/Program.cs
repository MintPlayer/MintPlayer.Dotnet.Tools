// dotnet tool install --global MintPlayer.CodeMigrations
// or
// dnx MintPlayer.CodeMigrations

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MintPlayer.CodeMigrations.Tools;

var builder = Host.CreateApplicationBuilder(args);

var currentDirectory = Directory.GetCurrentDirectory();
var migrationsPath = Path.Combine(currentDirectory, "migrations.json");
builder.Configuration.AddJsonFile(migrationsPath, optional: true);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddSingleton(provider =>
{
    var migrationConfig = new MigrationConfig { PackageName =  };
    provider.GetRequiredService<IConfiguration>().Bind(migrationConfig);
    return migrationConfig;
});
