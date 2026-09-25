# Value-equality generator

Index only — the documentation lives with the package.

| Project | Package | Purpose |
|---|---|---|
| [MintPlayer.ValueComparerGenerator](MintPlayer.ValueComparerGenerator/README.md) | `MintPlayer.ValueComparerGenerator` | Generates `IEquatable<T>` on your model types, so incremental source generators cache correctly. **Start here.** |
| MintPlayer.ValueComparerGenerator.Attributes | `MintPlayer.ValueComparerGenerator.Attributes` | The attributes you decorate with: `[GenerateEquality]`, `[EqualityIgnore]`, `[UseEqualityComparer]`. Referenced automatically by the generator package. |

Plug-in comparers for third-party types live alongside, in
[ValueComparers](../ValueComparers/README.md).
