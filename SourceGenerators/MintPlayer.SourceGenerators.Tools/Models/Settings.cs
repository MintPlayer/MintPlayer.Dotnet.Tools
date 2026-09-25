using Microsoft.CodeAnalysis.CSharp;

namespace MintPlayer.SourceGenerators.Tools;

/// <summary>
/// The project settings every <see cref="IncrementalGenerator"/> receives. Compares by value, so the
/// settings provider stays cached until a setting actually changes.
/// </summary>
public sealed class Settings : IEquatable<Settings>
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

    internal static Settings FromAnalyzerAndLangVersion(Models.AnalyzerInfo left, Models.LangVersion? right)
    {
        return new Settings
        {
            LanguageVersion = right?.LanguageVersion ?? LanguageVersion.Default,
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

    public bool Equals(Settings? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return LanguageVersion == other.LanguageVersion
            && S(RootNamespace, other.RootNamespace)
            && S(ProjectTypeGuids, other.ProjectTypeGuids)
            && S(EnforceExtendedAnalyzerRules, other.EnforceExtendedAnalyzerRules)
            && S(TargetFrameworkIdentifier, other.TargetFrameworkIdentifier)
            && S(TargetFramework, other.TargetFramework)
            && S(TargetPlatformMinVersion, other.TargetPlatformMinVersion)
            && S(TargetFrameworkVersion, other.TargetFrameworkVersion)
            && S(EnableCodeStyleSeverity, other.EnableCodeStyleSeverity)
            && S(InvariantGlobalization, other.InvariantGlobalization)
            && S(PlatformNeutralAssembly, other.PlatformNeutralAssembly)
            && S(EffectiveAnalysisLevelStyle, other.EffectiveAnalysisLevelStyle)
            && S(ProjectDir, other.ProjectDir)
            && S(EnableCOMHosting, other.EnableCOMHosting)
            && S(EnableGeneratedCOMIinterfaceCOMImportInterop, other.EnableGeneratedCOMIinterfaceCOMImportInterop)
            && S(SupportedPlatformList, other.SupportedPlatformList)
            && S(UsingMicrosoftNETSdkWeb, other.UsingMicrosoftNETSdkWeb);
    }

    public override bool Equals(object? obj) => Equals(obj as Settings);

    public override int GetHashCode()
    {
        var h = 17;
        h = ValueEquality.Combine(h, (int)LanguageVersion);
        h = ValueEquality.Combine(h, H(RootNamespace));
        h = ValueEquality.Combine(h, H(ProjectTypeGuids));
        h = ValueEquality.Combine(h, H(EnforceExtendedAnalyzerRules));
        h = ValueEquality.Combine(h, H(TargetFrameworkIdentifier));
        h = ValueEquality.Combine(h, H(TargetFramework));
        h = ValueEquality.Combine(h, H(TargetPlatformMinVersion));
        h = ValueEquality.Combine(h, H(TargetFrameworkVersion));
        h = ValueEquality.Combine(h, H(EnableCodeStyleSeverity));
        h = ValueEquality.Combine(h, H(InvariantGlobalization));
        h = ValueEquality.Combine(h, H(PlatformNeutralAssembly));
        h = ValueEquality.Combine(h, H(EffectiveAnalysisLevelStyle));
        h = ValueEquality.Combine(h, H(ProjectDir));
        h = ValueEquality.Combine(h, H(EnableCOMHosting));
        h = ValueEquality.Combine(h, H(EnableGeneratedCOMIinterfaceCOMImportInterop));
        h = ValueEquality.Combine(h, H(SupportedPlatformList));
        h = ValueEquality.Combine(h, H(UsingMicrosoftNETSdkWeb));
        return h;
    }

    private static bool S(string? x, string? y) => string.Equals(x, y, StringComparison.Ordinal);
    private static int H(string? x) => x is null ? 0 : StringComparer.Ordinal.GetHashCode(x);
}