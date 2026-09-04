; Unshipped analyzer releases
; Before merging a release, the contents of this file should be moved to AnalyzerReleases.Shipped.md under a release header.
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID  | Category              | Severity | Notes
---------|-----------------------|----------|--------------------------------------------------------
MPA0001  | MintPlayer.Assertions | Error    | Assertion returning a Task is not awaited
MPA0002  | MintPlayer.Assertions | Warning  | Should() without an assertion does nothing
MPA0003  | MintPlayer.Assertions | Warning  | AssertionScope is never disposed
MPA0004  | MintPlayer.Assertions | Info     | Equivalency expectation erased to object loses generated accessors and options
MPA0100  | MintPlayer.Assertions | Info     | FluentAssertions usage detected; migration fix available
MPAG001  | MintPlayer.Assertions | Warning  | [GenerateAssertion] method has an unsupported shape
