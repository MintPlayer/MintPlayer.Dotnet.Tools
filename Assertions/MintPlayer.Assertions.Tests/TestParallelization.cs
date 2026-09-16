// Phase 2 added process-wide configuration — Formatter.Options, the global formatter registry and
// AssertionConfiguration.ExceptionFactory — and a handful of tests necessarily mutate it and put it
// back. Under xUnit's default per-collection parallelism those tests run beside every other test in
// the assembly, so an unrelated assertion elsewhere fails with whatever exception type or renderer
// the mutating test had installed at that instant.
//
// That is not a flake to retry: it is two tests using one process-wide setting at once. The
// alternatives were making the configuration async-local (which would make it useless for its actual
// purpose, since a failing assertion usually has no scope) or hoping the interleaving stays lucky.
// The suite runs in a few seconds, so serialising it costs almost nothing.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
