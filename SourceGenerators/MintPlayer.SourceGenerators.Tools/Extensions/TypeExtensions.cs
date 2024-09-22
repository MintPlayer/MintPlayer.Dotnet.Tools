namespace MintPlayer.SourceGenerators.Tools.Extensions;

internal static class TypeExtensions
{
    public static bool IsDerivedFrom(this Type type, Type baseType)
    {
        var objectType = typeof(object);
        while (!type.OneOf([objectType, null]))
        {
            var cur = type!.IsGenericType ? type.GetGenericTypeDefinition() : type;

            if (baseType == cur)
                return true;

            type = type.BaseType!;
        }

        return false;
    }
}
