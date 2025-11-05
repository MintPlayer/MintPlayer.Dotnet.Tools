using System;

namespace MintPlayer.CliGenerator.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CliRootCommandAttribute : Attribute
{
    public CliRootCommandAttribute()
    {
    }

    public CliRootCommandAttribute(string? description)
    {
        Description = description;
    }

    public string? Name { get; set; }

    public string? Description { get; set; }
}
