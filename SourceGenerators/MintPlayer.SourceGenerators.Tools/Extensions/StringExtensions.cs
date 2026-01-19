namespace MintPlayer.SourceGenerators.Tools;

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

    public static string EnsureStartsWith(this string str, string value)
    {
        if (string.IsNullOrWhiteSpace(str))
            return str;

        if (str.StartsWith(value))
            return str;

        return value + str;
    }

    public static string WithGlobal(this string str)
        => str.EnsureStartsWith(globalPrefix);

    public static string WithoutGlobal(this string str)
        => str.RemoveBegin(globalPrefix);

    public static string StringifyTypeName(this string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return typeName;

        return string.Join(string.Empty, typeName.WithoutGlobal().Split('.').Select(x => x.UcFirst()));
    }

    /// <summary>
    /// Escapes special characters for use in a C# string literal.
    /// Handles backslashes, double-quotes, carriage returns, and newlines.
    /// </summary>
    /// <param name="value">The string to escape.</param>
    /// <returns>The escaped string (without surrounding quotes).</returns>
    public static string EscapeForStringLiteral(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value
            .Replace("\\", "\\\\")  // Escape backslashes FIRST
            .Replace("\"", "\\\"")  // Then escape quotes
            .Replace("\r", "\\r")   // Escape carriage returns
            .Replace("\n", "\\n");  // Escape newlines
    }

    /// <summary>
    /// Converts a string to a C# string literal, including surrounding quotes.
    /// Returns "null" for null values.
    /// </summary>
    /// <param name="value">The string to convert.</param>
    /// <returns>A valid C# string literal with surrounding quotes, or "null".</returns>
    public static string ToStringLiteral(this string? value)
    {
        if (value is null)
            return "null";

        return $"\"{value.EscapeForStringLiteral()}\"";
    }
}
