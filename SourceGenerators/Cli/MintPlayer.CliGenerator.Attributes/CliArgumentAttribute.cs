using System;

namespace MintPlayer.CliGenerator.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CliArgumentAttribute : Attribute
{
    public CliArgumentAttribute(int position)
    {
        Position = position;
    }

    public int Position { get; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public bool Required { get; set; } = true;
}
