﻿namespace MintPlayer.SourceGenerators.Tools;

internal static class TypeExtensions
{
    public static bool IsDerivedFrom(this Type type, Type baseType)
    {
        var objectType = typeof(object);
        //while (!type.OneOf([objectType, null]))
        while (type != objectType && type != null)
        {
            var cur = type!.IsGenericType ? type.GetGenericTypeDefinition() : type;

            if (baseType == cur)
                return true;

            type = type.BaseType!;
        }

        return false;
    }
}
