namespace MintPlayer.SourceGenerators.Tools.Extensions;

public static class StringExtensions
{
    const string globalPrefix = "global::";

    public static string UcFirst(this string str)
    {
        if (string.IsNullOrWhiteSpace(str))
            return str;

        if (str.Length == 1)
            return str.ToUpperInvariant();

        return char.ToUpperInvariant(str[0]) + str.Substring(1).ToLowerInvariant();
    }

    public static string RemoveBegin(this string str, string value)
    {
        value ??= string.Empty;
        if (str.StartsWith(value))
            return str.Substring(value.Length);

        return str;
    }

    public static string RemoveEnd(this string str, string value)
    {
        value ??= string.Empty;
        if (str.EndsWith(value))
            return str.Substring(0, str.Length - value.Length);
        return str;
    }

    public static string WithGlobal(this string str)
    {
        if (string.IsNullOrWhiteSpace(str))
            return str;

        if (str.StartsWith(globalPrefix))
            return str;

        return globalPrefix + str;
    }

    public static string WithoutGlobal(this string str)
        => str.RemoveBegin(globalPrefix);

    public static string StringifyTypeName(this string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return typeName;

        return string.Join(string.Empty, typeName.WithoutGlobal().Split('.').Select(x => x.UcFirst()));
    }
}
