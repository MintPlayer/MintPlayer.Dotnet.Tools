using BenchmarkDotNet.Running;
using S5;

if (args.Contains("--bench"))
{
    BenchmarkRunner.Run<RejectionBenchmarks>(args: args.Where(a => a != "--bench").ToArray());
    return;
}

Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, {Environment.ProcessorCount} cores");
Allocations.Run();
Safety.Run();
