namespace MintPlayer.SourceGenerators.Tools.Extensions;

public static class StringExtensions
{
    public static string RemoveBegin(this string value, string trim)
    {
        if (value.StartsWith(trim)) return value.Substring(trim.Length);
        else return value;
    }
}
