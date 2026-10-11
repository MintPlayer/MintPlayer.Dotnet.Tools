; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category        | Severity | Notes
--------|-----------------|----------|-------
MAPC005 | MapperGenerator | Error    | A [GenerateMapper] type that cannot be instantiated (interface, abstract, static, ref struct, open generic)
MAPC006 | MapperGenerator | Error    | A [GenerateMapper] type the generated mapper cannot access (private, protected or file-local)
MAPC007 | MapperGenerator | Error    | A [MapperConversion] method the generated mapper cannot call (generic, or in an inaccessible class)
