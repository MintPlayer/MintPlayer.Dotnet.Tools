# Source generators

Index only — each generator documents itself in its own package README, which is also what ships
to NuGet.

| Package | What it does |
|---|---|
| [MintPlayer.SourceGenerators](SourceGenerators/MintPlayer.SourceGenerators/README.md) | Service registration via `[Register]`, constructor-free injection via `[Inject]`, configuration binding, and an interface-implementation analyzer with a code fix. |
| [MintPlayer.Mapper](Mapper/MintPlayer.Mapper/README.md) | Generates mapper extension methods between your types, including property-name and property-type remapping. |
| [MintPlayer.ValueComparerGenerator](ValueComparerGenerator/MintPlayer.ValueComparerGenerator/README.md) | Generates the value-comparers an incremental generator needs in order to cache correctly. |
| [MintPlayer.CliGenerator](Cli/MintPlayer.CliGenerator/README.md) | Builds a `System.CommandLine` command tree from your classes, with dependency-injection wiring. |
| [MintPlayer.ValueComparers.NewtonsoftJson](ValueComparers/MintPlayer.ValueComparers.NewtonsoftJson/README.md) | A ready-made comparer for `JObject`, which otherwise compares by reference and defeats caching. |
| [MintPlayer.SourceGenerators.Tools](MintPlayer.SourceGenerators.Tools/README.md) | The toolkit the generators above are built on: an incremental-generator base class, emission helpers and the comparer registry. Use it to write your own. |

Each generator also publishes a `*.Attributes` companion holding just the attributes you decorate
with. Referencing the generator brings it along, so you rarely reference one directly.

## Layout

`eng/` holds the shared MSBuild for every generator project (`sourcegenerator.targets` defines the
netstandard2.0 + Roslyn multiplexing shape, `filenesting.targets` the `X.cs` / `X.Producer.cs`
nesting). `TestProjects/` holds non-packable consumers used to debug generator output.
