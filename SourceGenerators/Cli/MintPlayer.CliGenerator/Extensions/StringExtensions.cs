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
}
