namespace MintPlayer.CommandLineApp.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public class ConsoleAppAttribute : Attribute
{
    public ConsoleAppAttribute(string description)
    {
    }
}
