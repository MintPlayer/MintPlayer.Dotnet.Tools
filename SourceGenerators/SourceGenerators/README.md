# Dependency-injection generators

Index only — the documentation lives with the package.

| Project | Package | Purpose |
|---|---|---|
| [MintPlayer.SourceGenerators](MintPlayer.SourceGenerators/README.md) | `MintPlayer.SourceGenerators` | Service registration (`[Register]`), constructor-free injection (`[Inject]`), configuration binding, generic method overloads (`[GenericMethod]`), and the interface-implementation analyzer. **Start here.** |
| MintPlayer.SourceGenerators.Attributes | `MintPlayer.SourceGenerators.Attributes` | The attributes you decorate with. Install it next to the generator package: a `dotnet add package` reference to the generator does not make the attributes compile. |
