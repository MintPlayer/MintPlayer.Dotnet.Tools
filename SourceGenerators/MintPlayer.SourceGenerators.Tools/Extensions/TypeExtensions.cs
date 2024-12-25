using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MintPlayer.SourceGenerators.Tools.Extensions;

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

    public static MethodInfo GetOverload(this Type type, string methodName, params Type[] basicParameterTypes)
    {
        var qualifyingMethods = type.GetMethods();
        return GetOverloadInternal(qualifyingMethods, methodName, types => types.Select(t => t.BasicType).SequenceEqual(basicParameterTypes));
    }

    public static MethodInfo GetOverload(this Type type, string methodName, BindingFlags bindingAttr, params Type[] basicParameterTypes)
    {
        var qualifyingMethods = type.GetMethods(bindingAttr);
        return GetOverloadInternal(qualifyingMethods, methodName, types => types.Select(t => t.BasicType).SequenceEqual(basicParameterTypes));
    }

    public static MethodInfo GetOverload(this Type type, string methodName, Func<IList<TypeFilter>, bool> filter)
    {
        var qualifyingMethods = type.GetMethods();
        return GetOverloadInternal(qualifyingMethods, methodName, types => filter(types));
    }

    public static MethodInfo GetOverload(this Type type, string methodName, BindingFlags bindingAttr, Func<IList<TypeFilter>, bool> filter)
    {
        var qualifyingMethods = type.GetMethods(bindingAttr);
        return GetOverloadInternal(qualifyingMethods, methodName, types => filter(types));
    }

    private static MethodInfo GetOverloadInternal(MethodInfo[] qualifyingMethods, string methodName, Func<IList<TypeFilter>, bool> filter)
    {
        var res = qualifyingMethods
            .Select(m => new
            {
                Method = m,
                Parameters = m.GetParameters()
                    .Select(p => new
                    {
                        p.ParameterType,
                        GenericArguments = p.ParameterType.IsGenericType
                            ? p.ParameterType.GetGenericArguments()
                            : [],
                        GenericTypeDefinition = p.ParameterType.IsGenericType
                            ? p.ParameterType.GetGenericTypeDefinition()
                            : null,
                    })
                    .ToList(),
            })
            .Where(m => m.Method.Name == methodName)
            .ToList();

        var qual = res
            .Where(m =>
            {
                var list = m.Parameters.Select(p => new TypeFilter
                {
                    BasicType = p.GenericTypeDefinition ?? p.ParameterType,
                    GenericArguments = p.GenericArguments
                }).ToList();
                var isok = filter(list);
                return isok;
            })
            .ToList();

        return qual.SingleOrDefault()?.Method;
    }

    public class TypeFilter
    {
        public Type? BasicType { get; set; }
        public Type[] GenericArguments { get; set; } = [];
    }
}
