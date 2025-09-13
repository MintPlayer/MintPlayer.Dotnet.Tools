using Microsoft.CodeAnalysis.CSharp;

namespace MintPlayer.SourceGenerators.Tools.Models;

[ValueComparer(typeof(SettingsValueComparer))]
public sealed class Settings
{
    private Settings() { }

    public LanguageVersion LanguageVersion { get; private set; }
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

    internal static Settings FromAnalyzerAndLangVersion(AnalyzerInfo left, LanguageVersion right)
    {
        return new Settings
        {
            LanguageVersion = right,
            RootNamespace = left.RootNamespace,
            ProjectTypeGuids = left.ProjectTypeGuids,
            EnforceExtendedAnalyzerRules = left.EnforceExtendedAnalyzerRules,
            TargetFrameworkIdentifier = left.TargetFrameworkIdentifier,
            TargetFramework = left.TargetFramework,
            TargetPlatformMinVersion = left.TargetPlatformMinVersion,
            TargetFrameworkVersion = left.TargetFrameworkVersion,
            EnableCodeStyleSeverity = left.EnableCodeStyleSeverity,
            InvariantGlobalization = left.InvariantGlobalization,
            PlatformNeutralAssembly = left.PlatformNeutralAssembly,
            EffectiveAnalysisLevelStyle = left.EffectiveAnalysisLevelStyle,
            ProjectDir = left.ProjectDir,
            EnableCOMHosting = left.EnableCOMHosting,
            EnableGeneratedCOMIinterfaceCOMImportInterop = left.EnableGeneratedCOMIinterfaceCOMImportInterop,
            SupportedPlatformList = left.SupportedPlatformList,
            UsingMicrosoftNETSdkWeb = left.UsingMicrosoftNETSdkWeb,
        };
    }
}