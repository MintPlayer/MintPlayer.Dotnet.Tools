using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using MintPlayer.SourceGenerators.Tools.Models;

namespace MintPlayer.SourceGenerators.Tools.Tests;

/// <summary>
/// The hand-written <see cref="IEquatable{T}"/> on Tools' own pipeline types. Tools cannot use the generator (the
/// generator references Tools), so these are written by hand; the tests pin the equal-hash contract that the old
/// hand-written comparers broke (P3: several had no hash at all and fell back to the reference hash).
/// </summary>
public class HandWrittenEquatableTests
{
    private static void ShouldBeEqual<T>(T a, T b) where T : IEquatable<T>
    {
        a.Equals(b).Should().BeTrue();
        b.Equals(a).Should().BeTrue("equality is symmetric");
        a.Equals((object)b).Should().BeTrue();
        EqualityComparer<T>.Default.Equals(a, b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode(), "equal instances must hash equally");
    }

    private static void ShouldBeUnequal<T>(T a, T b) where T : IEquatable<T>
    {
        a.Equals(b).Should().BeFalse();
        b.Equals(a).Should().BeFalse("equality is symmetric");
        EqualityComparer<T>.Default.Equals(a, b).Should().BeFalse();
    }

    #region LocationKey (from ComparerRegistryResolutionTests.LocationKey_ComparesByValue)

    [Fact]
    public void LocationKey_ComparesByValue()
    {
        var a = new LocationKey("/src/A.cs", 1, 2, 3, 4);

        ShouldBeEqual(a, new LocationKey("/src/A.cs", 1, 2, 3, 4));
        ShouldBeUnequal(a, new LocationKey("/src/B.cs", 1, 2, 3, 4));
        ShouldBeUnequal(a, new LocationKey("/src/A.cs", 2, 2, 3, 4));
        ShouldBeUnequal(a, new LocationKey("/src/A.cs", 1, 3, 3, 4));
        ShouldBeUnequal(a, new LocationKey("/src/A.cs", 1, 2, 4, 4));
        ShouldBeUnequal(a, new LocationKey("/src/A.cs", 1, 2, 3, 5));
        ShouldBeUnequal(a, new LocationKey("/src/a.cs", 1, 2, 3, 4));
        a.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void LocationKey_Null_EqualsAnotherKeyWithoutAPath()
        => ShouldBeEqual(LocationKey.Null, new LocationKey(null));

    #endregion

    #region PathSpec / PathSpecElement

    private static PathSpecElement Element(string name, EPathSpecType type = EPathSpecType.Class, bool isPartial = true, string? generics = null)
        => new() { Name = name, Type = type, IsPartial = isPartial, GenericTypeParameters = generics };

    [Fact]
    public void PathSpecElement_ComparesEveryProperty()
    {
        ShouldBeEqual(Element("Outer"), Element("Outer"));
        ShouldBeUnequal(Element("Outer"), Element("Other"));
        ShouldBeUnequal(Element("Outer"), Element("Outer", EPathSpecType.Struct));
        ShouldBeUnequal(Element("Outer"), Element("Outer", isPartial: false));
        ShouldBeUnequal(Element("Outer", generics: "<T>"), Element("Outer", generics: "<U>"));
    }

    [Fact]
    public void PathSpec_ComparesTheNamespaceAndEveryParent()
    {
        PathSpec Spec(string? ns, params PathSpecElement[] parents) => new() { ContainingNamespace = ns, Parents = parents };

        ShouldBeEqual(Spec("Ns", Element("A"), Element("B")), Spec("Ns", Element("A"), Element("B")));
        ShouldBeEqual(Spec(null), Spec(null));
        ShouldBeUnequal(Spec("Ns", Element("A")), Spec("Other", Element("A")));
        ShouldBeUnequal(Spec("Ns", Element("A"), Element("B")), Spec("Ns", Element("B"), Element("A")));
        ShouldBeUnequal(Spec("Ns", Element("A")), Spec("Ns", Element("A"), Element("B")));
        ShouldBeUnequal(Spec("Ns", Element("A")), Spec("Ns", Element("A", isPartial: false)));
    }

    #endregion

    #region AnalyzerInfo / LangVersion / Settings (replaces ValueComparerTests.TheModuleInitializerRegistersTheBuiltInComparers)

    private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
        {
            if (values.TryGetValue(key, out var found)) { value = found; return true; }
            value = null;
            return false;
        }
    }

    private static AnalyzerInfo Info(string rootNamespace, string? targetFramework = "net10.0")
    {
        var values = new Dictionary<string, string> { ["build_property.rootnamespace"] = rootNamespace };
        if (targetFramework is not null) values["build_property.targetframework"] = targetFramework;
        return AnalyzerInfo.FromGlobalOptions(new Options(values));
    }

    [Fact]
    public void TheBuiltInModels_AreEquatableUnderTheDefaultComparer()
    {
        typeof(IEquatable<AnalyzerInfo>).IsAssignableFrom(typeof(AnalyzerInfo)).Should().BeTrue();
        typeof(IEquatable<LangVersion>).IsAssignableFrom(typeof(LangVersion)).Should().BeTrue();
        typeof(IEquatable<Settings>).IsAssignableFrom(typeof(Settings)).Should().BeTrue();
    }

    [Fact]
    public void AnalyzerInfo_ComparesEveryOption()
    {
        ShouldBeEqual(Info("App"), Info("App"));
        ShouldBeUnequal(Info("App"), Info("Other"));
        ShouldBeUnequal(Info("App"), Info("App", "net9.0"));
        ShouldBeUnequal(Info("App"), Info("App", null));
    }

    [Fact]
    public void LangVersion_ComparesVersionAndWeight()
    {
        LangVersion Version(LanguageVersion v, int weight) => new() { LanguageVersion = v, Weight = weight };

        ShouldBeEqual(Version(LanguageVersion.CSharp12, 12), Version(LanguageVersion.CSharp12, 12));
        ShouldBeUnequal(Version(LanguageVersion.CSharp12, 12), Version(LanguageVersion.CSharp11, 12));
        ShouldBeUnequal(Version(LanguageVersion.CSharp12, 12), Version(LanguageVersion.CSharp12, 11));
    }

    [Fact]
    public void Settings_ComparesTheLanguageVersionAndEveryOption()
    {
        var csharp12 = new LangVersion { LanguageVersion = LanguageVersion.CSharp12, Weight = 12 };
        var csharp11 = new LangVersion { LanguageVersion = LanguageVersion.CSharp11, Weight = 11 };

        ShouldBeEqual(Settings.FromAnalyzerAndLangVersion(Info("App"), csharp12), Settings.FromAnalyzerAndLangVersion(Info("App"), csharp12));
        ShouldBeEqual(Settings.FromAnalyzerAndLangVersion(Info("App"), null), Settings.FromAnalyzerAndLangVersion(Info("App"), null));
        ShouldBeUnequal(Settings.FromAnalyzerAndLangVersion(Info("App"), csharp12), Settings.FromAnalyzerAndLangVersion(Info("App"), csharp11));
        ShouldBeUnequal(Settings.FromAnalyzerAndLangVersion(Info("App"), csharp12), Settings.FromAnalyzerAndLangVersion(Info("Other"), csharp12));
        ShouldBeUnequal(Settings.FromAnalyzerAndLangVersion(Info("App"), csharp12), Settings.FromAnalyzerAndLangVersion(Info("App", "net9.0"), csharp12));
    }

    #endregion
}
