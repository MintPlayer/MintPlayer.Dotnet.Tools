using System.Text;

namespace MintPlayer.Assertions.SourceGenerator.Helpers;

/// <summary>
/// Turns a predicate name into the fluent assertion vocabulary: <c>IsEven</c> becomes
/// <c>BeEven</c> (and reads as "to be even" in a failure message).
/// </summary>
internal static class AssertionNaming
{
    public static string DeriveName(string methodName)
    {
        if (StartsWithWord(methodName, "Is")) return "Be" + methodName.Substring(2);
        if (StartsWithWord(methodName, "Has")) return "Have" + methodName.Substring(3);
        if (StartsWithWord(methodName, "Can")) return "BeAbleTo" + methodName.Substring(3);
        return "Be" + methodName;
    }

    /// <summary>"BeEven" → "be even"; used to build the failure message phrase.</summary>
    public static string Humanize(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                builder.Append(' ');
            builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    /// <summary>True when <paramref name="value"/> starts with <paramref name="prefix"/> followed by another word.</summary>
    private static bool StartsWithWord(string value, string prefix)
        => value.Length > prefix.Length
        && value.StartsWith(prefix, StringComparison.Ordinal)
        && char.IsUpper(value[prefix.Length]);
}
