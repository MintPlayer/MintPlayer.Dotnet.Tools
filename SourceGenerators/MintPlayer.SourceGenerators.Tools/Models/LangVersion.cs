using Microsoft.CodeAnalysis.CSharp;

namespace MintPlayer.SourceGenerators.Tools.Models;

//[ValueComparer(typeof(LangVersionComparer))]
internal class LangVersion
{
    public LanguageVersion LanguageVersion { get; set; }
    public int Weight { get; set; }
}
