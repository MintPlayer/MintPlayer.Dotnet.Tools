using Microsoft.CodeAnalysis.Text;

namespace MintPlayer.SourceGenerators.Tools.ValueComparers;

/// <summary>
/// Value Comparer for SourceText
/// </summary>
public sealed class SourceTextValueComparer : ValueComparer<SourceText>
{
    protected override bool AreEqual(SourceText x, SourceText y)
    {
        return x.ContentEquals(y);
    }
}