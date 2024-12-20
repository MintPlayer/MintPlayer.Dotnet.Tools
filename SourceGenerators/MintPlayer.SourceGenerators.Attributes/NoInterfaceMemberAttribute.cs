using System;

namespace MintPlayer.SourceGenerators.Attributes
{
    /// <summary>Do not generate a member on the implemented interface</summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
    public class NoInterfaceMemberAttribute : Attribute
    {
    }
}
