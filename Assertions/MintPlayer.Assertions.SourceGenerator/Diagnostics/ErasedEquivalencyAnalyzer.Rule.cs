using Microsoft.CodeAnalysis;

namespace MintPlayer.Assertions.Analyzers.Diagnostics;

public static partial class DiagnosticRules
{
    public static readonly DiagnosticDescriptor ErasedEquivalencyRule = new DiagnosticDescriptor(
        id: "MPA0004",
        title: "Equivalency expectation is erased to object",
        messageFormat: "An expectation cast to object falls back to reflection instead of the generated accessors, and cannot be configured with Excluding/Including. Pass the expectation as its concrete type.",
        category: "MintPlayer.Assertions",
        // ⚠️ UNADDRESSED: Info is arguably too quiet for what this catches. An expectation erased to
        // object silently drops to the reflection fallback — correct results, roughly 15× slower,
        // no runtime signal of any kind — which is the single easiest way to lose the entire
        // benefit of the source generator without noticing. Info does not appear in a normal build
        // log and never fails CI, so in practice nobody sees it.
        //
        // Not raised to Warning here only because that is a behaviour change for every consumer and
        // belongs in its own change with a release note, not folded into unrelated work. When it is
        // raised, check the repo's own build first: MPA0004 was unenforceable in this library until
        // OutputItemType="Analyzer" was added, so its true hit count here has never been seen.
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);
}
