using System;

namespace MintPlayer.SourceGenerators.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CliCommandAttribute : Attribute
{
    public CliCommandAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public string? Description { get; set; }
}
