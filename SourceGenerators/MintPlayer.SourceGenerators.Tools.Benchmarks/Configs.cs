using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Jobs;
using System.Runtime.InteropServices;

namespace MintPlayer.SourceGenerators.Tools.Benchmarks;

// The net11.0 jobs declare no runtime, so they run on the host (start it with -f net11.0). BenchmarkDotNet
// 0.14 has no moniker for .NET 11: an explicit CoreRuntime.CreateForNewVersion("net11.0") job fails its SDK
// validator with "SDK version check not implemented for NotRecognized".
internal static class Runtimes
{
    /// <summary>
    /// A net481 job runs only on Windows, where the csproj adds the net481 target (Visual Studio runs analyzers
    /// in-process on .NET Framework, which is why B1 measures it). Set BENCH_NET481=0 to skip it.
    /// </summary>
    public static bool Net481Enabled =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        && Environment.GetEnvironmentVariable("BENCH_NET481") != "0"
        && Directory.Exists(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8.1"));
}

/// <summary>B1: net11.0, plus net481 where available.</summary>
public sealed class EqualityConfig : ManualConfig
{
    public EqualityConfig()
    {
        var job = Job.Default.WithWarmupCount(5).WithIterationCount(15);
        AddJob(job.WithId("net11.0"));
        if (Runtimes.Net481Enabled)
        {
            AddJob(job.WithRuntime(ClrRuntime.Net481).WithId("net481"));
            // Measured on BenchmarkDotNet 0.14 with the .NET 11 SDK: without this option the net481 child
            // (built into a GUID-named folder) exits with -1 before printing anything, and every net481 row is
            // NA. With it (the build goes to a folder named after the job), the same benchmarks run fine.
            WithOptions(ConfigOptions.KeepBenchmarkFiles);
        }
    }
}

/// <summary>
/// B2: one <c>RunGenerators</c> per invocation, with the edit applied in an iteration setup so it is not
/// measured. net11.0 only (the csproj compiles Pipeline/ for net11.0 only).
/// </summary>
public sealed class PipelineConfig : ManualConfig
{
    public PipelineConfig()
    {
        AddJob(Job.Default
            .WithInvocationCount(1)
            .WithUnrollFactor(1)
            .WithWarmupCount(30)
            .WithIterationCount(40)
            .WithId("net11.0"));
    }
}
