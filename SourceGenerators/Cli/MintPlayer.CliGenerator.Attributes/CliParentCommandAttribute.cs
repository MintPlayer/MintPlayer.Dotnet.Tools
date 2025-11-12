using System;

namespace MintPlayer.CliGenerator.Attributes;

/// <summary>
/// Specifies the parent CLI command for a non-nested command class so the generator can build a tree.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CliParentCommandAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of <see cref="CliParentCommandAttribute"/>.
    /// </summary>
    /// <param name="parentCommandType">The parent command type which must itself be annotated with <see cref="CliRootCommandAttribute"/> or <see cref="CliCommandAttribute"/>.</param>
    public CliParentCommandAttribute(Type parentCommandType)
    {
        ParentCommandType = parentCommandType;
    }

    public Type ParentCommandType { get; }
}
