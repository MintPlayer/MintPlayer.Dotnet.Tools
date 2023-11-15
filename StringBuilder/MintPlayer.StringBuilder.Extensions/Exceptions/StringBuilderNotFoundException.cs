namespace MintPlayer.StringBuilder.Extensions.Exceptions;

public class StringBuilderNotFoundException : Exception
{
    public StringBuilderNotFoundException() : base("The StringBuilder was not used before")
    {
    }
}
