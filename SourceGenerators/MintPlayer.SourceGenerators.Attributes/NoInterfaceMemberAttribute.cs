using System;

namespace MintPlayer.SourceGenerators.Attributes
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
    public class NoInterfaceMemberAttribute : Attribute
    {
    }
}
