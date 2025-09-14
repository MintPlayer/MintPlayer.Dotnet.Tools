using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.Tools.Models;

public sealed class SettingsValueComparer : ValueComparer<Settings>
{
    protected override bool AreEqual(Settings x, Settings y)
    {
        if (!IsEquals(x.LanguageVersion, y.LanguageVersion)) return false;
        if (!IsEquals(x.RootNamespace, y.RootNamespace)) return false;
        if (!IsEquals(x.ProjectTypeGuids, y.ProjectTypeGuids)) return false;
        if (!IsEquals(x.EnforceExtendedAnalyzerRules, y.EnforceExtendedAnalyzerRules)) return false;
        if (!IsEquals(x.TargetFrameworkIdentifier, y.TargetFrameworkIdentifier)) return false;
        if (!IsEquals(x.TargetFramework, y.TargetFramework)) return false;
        if (!IsEquals(x.TargetPlatformMinVersion, y.TargetPlatformMinVersion)) return false;
        if (!IsEquals(x.TargetFrameworkVersion, y.TargetFrameworkVersion)) return false;
        if (!IsEquals(x.EnableCodeStyleSeverity, y.EnableCodeStyleSeverity)) return false;
        if (!IsEquals(x.InvariantGlobalization, y.InvariantGlobalization)) return false;
        if (!IsEquals(x.PlatformNeutralAssembly, y.PlatformNeutralAssembly)) return false;
        if (!IsEquals(x.EffectiveAnalysisLevelStyle, y.EffectiveAnalysisLevelStyle)) return false;
        if (!IsEquals(x.ProjectDir, y.ProjectDir)) return false;
        if (!IsEquals(x.EnableCOMHosting, y.EnableCOMHosting)) return false;
        if (!IsEquals(x.EnableGeneratedCOMIinterfaceCOMImportInterop, y.EnableGeneratedCOMIinterfaceCOMImportInterop)) return false;
        if (!IsEquals(x.SupportedPlatformList, y.SupportedPlatformList)) return false;
        if (!IsEquals(x.UsingMicrosoftNETSdkWeb, y.UsingMicrosoftNETSdkWeb)) return false;

        return base.AreEqual(x, y);
    }
}
