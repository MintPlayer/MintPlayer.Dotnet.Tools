// The package's result types under the names the existing tests already use. GeneratorRun and
// CodeFixResult were local records before the harness moved into
// MintPlayer.SourceGenerators.TestHarness; aliasing rather than renaming keeps 52 call sites untouched
// and keeps the diff about the harness rather than about test bodies.
global using GeneratorRun = MintPlayer.SourceGenerators.TestHarness.GeneratorResult;
global using IncrementalRun = MintPlayer.SourceGenerators.TestHarness.IncrementalGeneratorResult;
global using CodeFixResult = MintPlayer.SourceGenerators.TestHarness.CodeFixResult;
global using FixtureProject = MintPlayer.SourceGenerators.TestHarness.FixtureProject;
