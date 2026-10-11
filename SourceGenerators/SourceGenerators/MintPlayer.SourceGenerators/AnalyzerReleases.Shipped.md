; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

; Releases before 12.1.1 were not tracked. This section is the baseline: every rule in the
; published MintPlayer.SourceGenerators 12.1.1 package, with its severity and category at the time.

## Release 12.1.1

### New Rules

Rule ID     | Category                           | Severity | Notes
------------|------------------------------------|----------|------------------------------------------------
CONFIG001   | MintPlayer.SourceGenerators.Config | Error    | [Config] on a non-partial class
CONFIG002   | MintPlayer.SourceGenerators.Config | Error    | Empty configuration key
CONFIG003   | MintPlayer.SourceGenerators.Config | Error    | Unsupported type
CONFIG005   | MintPlayer.SourceGenerators.Config | Error    | Default value type mismatch
CONFIG006   | MintPlayer.SourceGenerators.Config | Error    | Conflicting attributes
CONFIG007   | MintPlayer.SourceGenerators.Config | Warning  | Duplicate configuration key
CONFIG008   | MintPlayer.SourceGenerators.Config | Error    | Conflicting with Inject
CONNSTR001  | MintPlayer.SourceGenerators.Config | Error    | Empty connection string name
CONNSTR002  | MintPlayer.SourceGenerators.Config | Error    | Invalid field type
CONNSTR003  | MintPlayer.SourceGenerators.Config | Error    | Conflicting with Inject
INJECT001   | MintPlayer.SourceGenerators        | Error    | PostConstruct method must be parameterless
INJECT002   | MintPlayer.SourceGenerators        | Error    | Only one PostConstruct method allowed per class
INJECT003   | MintPlayer.SourceGenerators        | Error    | PostConstruct method cannot be static
INJECT004   | MintPlayer.SourceGenerators        | Warning  | PostConstruct in class without [Inject] members
INTF001     | Empty                              | Warning  | Interface implementation mismatch
MP001       | CodeQuality                        | Warning  | Unused usings
OPTIONS001  | MintPlayer.SourceGenerators.Config | Error    | Invalid options type
OPTIONS002  | MintPlayer.SourceGenerators.Config | Info     | Options section binding not detected (never reported)
OPTIONS003  | MintPlayer.SourceGenerators.Config | Error    | Conflicting with Inject
OPTIONS004  | MintPlayer.SourceGenerators.Config | Error    | Conflicting with Config
REGISTER001 | MintPlayer.SourceGenerators        | Error    | Assembly-level [Register] requires at least the implementation type
REGISTER002 | MintPlayer.SourceGenerators        | Error    | Class-level [Register] should not specify implementation type
