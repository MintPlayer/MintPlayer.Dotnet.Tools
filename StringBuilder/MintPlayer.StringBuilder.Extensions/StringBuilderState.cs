namespace MintPlayer.StringBuilder.Extensions;

internal class StringBuilderState
{
    public Stack<Indentation> Indentations { get; set; } = new Stack<Indentation>();
}

internal class Indentation
{
    public EIndentationType IndentationType { get; set; }
    public int Size { get; set; }
}

public enum EIndentationType
{
    Tab,
    Space,
}