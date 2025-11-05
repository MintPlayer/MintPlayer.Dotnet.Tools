using System;
using System.Collections.Generic;

namespace MintPlayer.CliGenerator.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CliOptionAttribute : Attribute
{
    public CliOptionAttribute(params string[] aliases)
    {
        Aliases = aliases ?? Array.Empty<string>();
    }

    public IReadOnlyList<string> Aliases { get; }

    public string? Description { get; set; }

    public bool Required { get; set; }

    public bool Hidden { get; set; }

    public object? DefaultValue { get; set; }
}
