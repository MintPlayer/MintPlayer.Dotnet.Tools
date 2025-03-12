using MintPlayer.SourceGenerators.Models;
using MintPlayer.SourceGenerators.Tools.ValueComparers;

namespace MintPlayer.SourceGenerators.ValueComparers;

public class XmlMarkupComparer : ValueComparer<XmlMarkup>
{
    protected override bool AreEqual(XmlMarkup x, XmlMarkup y)
    {
        if (!IsEquals(x.Text, y.Text)) return false;
        if (!IsEquals(x.MethodName, y.MethodName)) return false;
        if (!IsEquals(x.ClassName, y.ClassName)) return false;
        if (!IsEquals(x.Namespace, y.Namespace)) return false;
        if (!IsEquals(x.ReturnType, y.ReturnType)) return false;
        if (!IsEquals(x.MethodGenericParameters, y.MethodGenericParameters)) return false;
        if (!IsEquals(x.ClassGenericParameters, y.ClassGenericParameters)) return false;
        if (!IsEquals(x.MethodAccessModifiers, y.MethodAccessModifiers)) return false;

        return true;
    }
}


public class XmlMarkupParameterComparer : ValueComparer<XmlMarkupParameter>
{
    protected override bool AreEqual(XmlMarkupParameter x, XmlMarkupParameter y)
    {
        if (!IsEquals(x.Name, y.Name)) return false;
        if (!IsEquals(x.Type, y.Type)) return false;

        return true;
    }
}
