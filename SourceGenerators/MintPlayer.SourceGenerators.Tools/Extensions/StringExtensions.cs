using Microsoft.CodeAnalysis.CSharp;

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
    /// Turns a project's <c>RootNamespace</c> into a namespace name C# accepts, in the spirit of the "safe project
    /// name" project templates derive from a project name. Returns null for a null or blank value.
    /// </summary>
    /// <remarks>
    /// The SDK defaults <c>RootNamespace</c> to the project name with only spaces replaced, so a project named
    /// <c>My-App</c> passes <c>My-App</c> through <c>build_property.rootnamespace</c>. Written into
    /// <c>namespace {RootNamespace}</c> that is CS0246/CS0102/CS0548 in every generated file. Per dot-separated
    /// segment: a character that cannot be part of an identifier becomes <c>_</c>, a segment that cannot start an
    /// identifier (a digit) gets a <c>_</c> prefix, an empty segment becomes <c>_</c>, and a reserved keyword is
    /// <c>@</c>-escaped. So <c>My-App.1st.class</c> becomes <c>My_App._1st.@class</c>. A valid name is returned
    /// unchanged, so applying it twice is harmless.
    /// </remarks>
    public static string? ToSafeNamespace(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var segments = value!.Trim().Split('.').Select(static segment =>
        {
            var name = segment.Trim();
            var escaped = name.StartsWith("@") && name.Length > 1;
            if (escaped)
                name = name.Substring(1);

            var chars = name.Select(static c => SyntaxFacts.IsIdentifierPartCharacter(c) ? c : '_').ToArray();
            var safe = new string(chars);
            if (safe.Length == 0)
                return "_";
            if (!SyntaxFacts.IsIdentifierStartCharacter(safe[0]))
                return "_" + safe;
            return SyntaxFacts.GetKeywordKind(safe) != SyntaxKind.None ? "@" + safe : safe;
        });

        return string.Join(".", segments);
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
