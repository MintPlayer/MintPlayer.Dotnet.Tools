; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID      | Category                    | Severity | Notes
-------------|-----------------------------|----------|-----------------------------------------------
GENMETHOD001 | MintPlayer.SourceGenerators | Error    | [GenericMethod] requires a partial containing type
GENMETHOD002 | MintPlayer.SourceGenerators | Error    | [GenericMethod] method must be private
GENMETHOD003 | MintPlayer.SourceGenerators | Error    | [GenericMethod] method has an unsupported shape
GENMETHOD004 | MintPlayer.SourceGenerators | Error    | [GenericMethod] Count out of range
IMPLINTF001  | MintPlayer.SourceGenerators | Error    | [ImplementInterface] requires a partial type
IMPLINTF002  | MintPlayer.SourceGenerators | Error    | [ImplementInterface] argument is not an interface
IMPLINTF003  | MintPlayer.SourceGenerators | Error    | [ImplementInterface] cannot implement a member without an obvious body
IMPLINTF004  | MintPlayer.SourceGenerators | Error    | [ImplementInterface] only generates read/write properties
IMPLINTF005  | MintPlayer.SourceGenerators | Error    | [ImplementInterface] interfaces declare conflicting members
IMPLINTF006  | MintPlayer.SourceGenerators | Error    | [ImplementInterface] is not supported on this kind of type
IMPLINTF007  | MintPlayer.SourceGenerators | Warning  | [ImplementInterface] names an interface twice
IMPLINTF008  | MintPlayer.SourceGenerators | Info     | Hand-written property is redundant with [ImplementInterface]
IMPLINTF009  | MintPlayer.SourceGenerators | Error    | Hand-written member does not match the interface member
INJECT005    | MintPlayer.SourceGenerators | Error    | PostConstruct method must return void
INJECT006    | MintPlayer.SourceGenerators | Error    | Type must be partial to receive a generated constructor
INJECT007    | MintPlayer.SourceGenerators | Error    | Type with a parameter list cannot receive a generated constructor
INJECT008    | MintPlayer.SourceGenerators | Warning  | [Inject] property with a setter is not injected
OPTIONS005   | MintPlayer.SourceGenerators.Config | Error    | Conflicting with ConnectionString
REGISTER003  | MintPlayer.SourceGenerators | Error    | [Register] type is not accessible from the generated registration method
REGISTER004  | MintPlayer.SourceGenerators | Error    | [Register] lifetime is not a defined ServiceLifetime
REGISTER005  | MintPlayer.SourceGenerators | Error    | [Register] service type cannot be expressed in the generated registration

### Removed Rules

Rule ID    | Category                           | Severity | Notes
-----------|------------------------------------|----------|-----------------------------------------------
OPTIONS002 | MintPlayer.SourceGenerators.Config | Info     | Never reported; [Options] section is documentation only
