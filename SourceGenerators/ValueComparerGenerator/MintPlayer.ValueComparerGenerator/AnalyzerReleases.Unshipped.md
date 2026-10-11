; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category               | Severity | Notes
--------|------------------------|----------|-------
MINT007 | ValueComparerGenerator | Error    | [GenerateEquality] cannot generate equality for this type (ref struct, static class, union, file-local)
MINT008 | ValueComparerGenerator | Warning  | Only one of Equals(object) and GetHashCode() is declared (was a Warning-severity MINT002)
