using MintPlayer.SourceGenerators.Tools;
using MintPlayer.SourceGenerators.ValueComparers;

namespace MintPlayer.SourceGenerators.Models;

[ValueComparer(typeof(XmlMarkupComparer))]
public class XmlMarkup
{
    public string? Text { get; set; }
    public string MethodName { get; internal set; }
    public string ClassName { get; internal set; }
    public string Namespace { get; internal set; }
    public string ReturnType { get; internal set; }
    public string[] MethodGenericParameters { get; internal set; }
    public string[] ClassGenericParameters { get; internal set; }
    public XmlMarkupParameter[] MethodParameters { get; internal set; }
}

[ValueComparer(typeof(XmlMarkupParameterComparer))]
public class XmlMarkupParameter
{
    public string Name { get; set; }
    public string Type { get; internal set; }
}