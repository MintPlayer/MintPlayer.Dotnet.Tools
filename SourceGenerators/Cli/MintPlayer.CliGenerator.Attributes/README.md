# MintPlayer.CliGenerator.Attributes

Attribute definitions for [MintPlayer.CliGenerator](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/blob/master/SourceGenerators/Cli/MintPlayer.CliGenerator/README.md), the generator that builds a System.CommandLine command tree from your classes.

Install this package next to `MintPlayer.CliGenerator`. The generator package depends on it, but it is a development
dependency: `dotnet add package` and the Visual Studio package manager reference it with an `IncludeAssets` list
without `compile`, which also applies to this dependency. The attributes are then restored but not compiled against
(CS0246), so reference this package yourself.

It can also be referenced on its own, for example by a shared contracts assembly that declares the attributes
without running the generator.

## See also

- [MintPlayer.CliGenerator](https://github.com/MintPlayer/MintPlayer.Dotnet.Tools/blob/master/SourceGenerators/Cli/MintPlayer.CliGenerator/README.md) - the generator that reads these attributes
