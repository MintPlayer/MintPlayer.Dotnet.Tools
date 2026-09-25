using Microsoft.CodeAnalysis.CSharp;

namespace MintPlayer.SourceGenerators.Tools.Models;

internal sealed class LangVersion : IEquatable<LangVersion>
{
    public LanguageVersion LanguageVersion { get; set; }
    public int Weight { get; set; }

    public bool Equals(LangVersion? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return LanguageVersion == other.LanguageVersion
            && Weight == other.Weight;
    }

    public override bool Equals(object? obj) => Equals(obj as LangVersion);

    public override int GetHashCode()
        => ValueEquality.Combine(ValueEquality.Combine(17, (int)LanguageVersion), Weight);
}
