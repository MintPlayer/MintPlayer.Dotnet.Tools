using Microsoft.CodeAnalysis.Diagnostics;

namespace MintPlayer.SourceGenerators.Tools.Models;

[ValueComparer(typeof(AnalyzerInfoComparer))]
internal class AnalyzerInfo
{
    private AnalyzerInfo() { }

    public string? RootNamespace { get; private set; }
    public string? ProjectTypeGuids { get; private set; }
    public string? EnforceExtendedAnalyzerRules { get; private set; }
    public string? TargetFrameworkIdentifier { get; private set; }
    public string? TargetFramework { get; private set; }
    public string? TargetPlatformMinVersion { get; private set; }
    public string? TargetFrameworkVersion { get; private set; }
    public string? EnableCodeStyleSeverity { get; private set; }
    public string? InvariantGlobalization { get; private set; }
    public string? PlatformNeutralAssembly { get; private set; }
    public string? EffectiveAnalysisLevelStyle { get; private set; }
    public string? ProjectDir { get; private set; }
    public string? EnableCOMHosting { get; private set; }
    public string? EnableGeneratedCOMIinterfaceCOMImportInterop { get; private set; }
    public string? SupportedPlatformList { get; private set; }
    public string? UsingMicrosoftNETSdkWeb { get; private set; }

    public static AnalyzerInfo FromGlobalOptions(AnalyzerConfigOptions options)
    {
        options.TryGetValue("build_property.rootnamespace", out var rootNamespace);
        options.TryGetValue("build_property.projecttypeguids", out var projectTypeGuids);
        options.TryGetValue("build_property.enforceextendedanalyzerrules", out var enforceExtendedAnalyzerRules);
        options.TryGetValue("build_property.targetframeworkidentifier", out var targetFrameworkIdentifier);
        options.TryGetValue("build_property.targetframework", out var targetFramework);
        options.TryGetValue("build_property.targetplatformminversion", out var targetPlatformMinVersion);
        options.TryGetValue("build_property.targetframeworkversion", out var targetFrameworkVersion);
        options.TryGetValue("build_property.enablecodestyleseverity", out var enableCodeStyleSeverity);
        options.TryGetValue("build_property.invariantglobalization", out var invariantGlobalization);
        options.TryGetValue("build_property.platformneutralassembly", out var platformNeutralAssembly);
        options.TryGetValue("build_property.effectiveanalysislevelstyle", out var effectiveAnalysisLevelStyle);
        options.TryGetValue("build_property.projectdir", out var projectDir);
        options.TryGetValue("build_property.enablecomhosting", out var enableCOMHosting);
        options.TryGetValue("build_property.enablegeneratedcominterfacecomimportinterop", out var enableGeneratedCOMIinterfaceCOMImportInterop);
        options.TryGetValue("build_property._supportedplatformlist", out var supportedPlatformList);
        options.TryGetValue("build_property.usingmicrosoftnetsdkweb", out var usingMicrosoftNETSdkWeb);
        return new AnalyzerInfo
        {
            RootNamespace = rootNamespace,
            ProjectTypeGuids = projectTypeGuids,
            EnforceExtendedAnalyzerRules = enforceExtendedAnalyzerRules,
            TargetFrameworkIdentifier = targetFrameworkIdentifier,
            TargetFramework = targetFramework,
            TargetPlatformMinVersion = targetPlatformMinVersion,
            TargetFrameworkVersion = targetFrameworkVersion,
            EnableCodeStyleSeverity = enableCodeStyleSeverity,
            InvariantGlobalization = invariantGlobalization,
            PlatformNeutralAssembly = platformNeutralAssembly,
            EffectiveAnalysisLevelStyle = effectiveAnalysisLevelStyle,
            ProjectDir = projectDir,
            EnableCOMHosting = enableCOMHosting,
            EnableGeneratedCOMIinterfaceCOMImportInterop = enableGeneratedCOMIinterfaceCOMImportInterop,
            SupportedPlatformList = supportedPlatformList,
            UsingMicrosoftNETSdkWeb = usingMicrosoftNETSdkWeb,
        };
    }
}