; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID      | Category                    | Severity | Notes
-------------|-----------------------------|----------|-----------------------------------------------
GENMETHOD001 | MintPlayer.SourceGenerators | Error    | [GenericMethod] requires a partial containing type
GENMETHOD002 | MintPlayer.SourceGenerators | Error    | [GenericMethod] method must be private
GENMETHOD003 | MintPlayer.SourceGenerators | Error    | [GenericMethod] method has an unsupported shape
GENMETHOD004 | MintPlayer.SourceGenerators | Error    | [GenericMethod] Count out of range
INJECT005    | MintPlayer.SourceGenerators | Error    | PostConstruct method must return void
INJECT006    | MintPlayer.SourceGenerators | Error    | Type must be partial to receive a generated constructor
INJECT007    | MintPlayer.SourceGenerators | Error    | Type with a parameter list cannot receive a generated constructor
REGISTER003  | MintPlayer.SourceGenerators | Error    | [Register] type is not accessible from the generated registration method
REGISTER004  | MintPlayer.SourceGenerators | Error    | [Register] lifetime is not a defined ServiceLifetime
REGISTER005  | MintPlayer.SourceGenerators | Error    | [Register] service type cannot be expressed in the generated registration

### Removed Rules

Rule ID    | Category                           | Severity | Notes
-----------|------------------------------------|----------|-----------------------------------------------
OPTIONS002 | MintPlayer.SourceGenerators.Config | Info     | Never reported; [Options] section is documentation only
