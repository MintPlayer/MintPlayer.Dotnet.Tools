using BenchmarkDotNet.Running;
using S4;

// dotnet run -c Release -- --verify [--quick]   correctness only (deterministic, Polly parity, fuzz)
// dotnet run -c Release -- --bench [BDN args]    timing (runs --verify --quick first as a gate)
if (args.Contains("--bench"))
{
    if (await Verify.RunAll(quick: true) != 0) return 1;
    var rest = args.Where(a => a != "--bench").ToArray();
    BenchmarkSwitcher.FromTypes([typeof(ClosedStateBenchmarks)]).Run(rest.Length == 0 ? ["--filter", "*"] : rest);
    return 0;
}

return await Verify.RunAll(quick: args.Contains("--quick"));
