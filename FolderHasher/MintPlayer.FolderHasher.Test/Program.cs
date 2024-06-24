using Microsoft.Extensions.DependencyInjection;
using MintPlayer.FolderHasher.Abstractions;
using System.Diagnostics;
using System.Security.Cryptography;

namespace MintPlayer.FolderHasher.Test;

internal class Program
{
    static async Task Main(string[] args)
    {
        var services = new ServiceCollection()
            .AddFolderHasher()
            .BuildServiceProvider();

        var hasher = services.GetRequiredService<IFolderHasher>();
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        var hash = await hasher.GetFolderHashAsync(@"C:\repos\MintPlayer.AspNetCore.Templates", ["node_modules"], SHA256.Create());
        stopwatch.Stop();

        Console.WriteLine($"Hash: {hash}");
        Console.WriteLine($"That took {stopwatch.ElapsedMilliseconds} ms");
    }
}
