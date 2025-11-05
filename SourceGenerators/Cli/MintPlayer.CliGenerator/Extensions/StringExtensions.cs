using System.Text;

namespace MintPlayer.CliGenerator.Extensions;

internal static class StringExtensions
{
    internal static string ToKebabCase(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        var builder = new StringBuilder(str.Length * 2);
        for (var i = 0; i < str.Length; i++)
        {
            var character = str[i];
            if (char.IsUpper(character))
            {
                if (i > 0)
                    builder.Append('-');

                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    // Helper to convert PascalCase to camelCase
    internal static string ToCamelCase(this string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        if (name.Length == 1) return name.ToLowerInvariant();
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
