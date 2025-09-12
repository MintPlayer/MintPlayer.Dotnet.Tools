namespace MintPlayer.SourceGenerators.Tools.Extensions;

public static class StringExtensions
{
    public static string RemoveBegin(this string str, string value)
    {
        value ??= string.Empty;
        if (str.StartsWith(value))
            return str.Substring(value.Length);

        return str;
    }
}
