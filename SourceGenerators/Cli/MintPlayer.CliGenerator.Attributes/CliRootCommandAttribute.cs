namespace MintPlayer.CliGenerator.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CliRootCommandAttribute : Attribute
{
    public string? Description { get; set; }
}
