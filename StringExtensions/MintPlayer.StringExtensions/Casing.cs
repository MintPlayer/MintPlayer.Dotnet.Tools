using System.Text;

namespace MintPlayer.StringExtensions;

public static class Casing
{
    /// <summary>Makes the first character of a string upper-case.</summary>
    /// <param name="str">Input string</param>
    /// <returns>String with an upper-case first character.</returns>
    public static string UcFirst(this string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return string.Empty;
        }

        char[] a = str.ToCharArray();
        a[0] = char.ToUpperInvariant(a[0]);
        return new string(a);
    }

    /// <summary>Makes the first character of a string lower-case.</summary>
    /// <param name="str">Input string</param>
    /// <returns>String with an lower-case first character.</returns>
    public static string LcFirst(this string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return string.Empty;
        }

        char[] a = str.ToCharArray();
        a[0] = char.ToLowerInvariant(a[0]);
        return new string(a);
    }

    #region Helper methods to extract the words from strings
    private static IEnumerable<string> WordsFromSnake(string str) => WordsFromSeparator(str, '_');
    private static IEnumerable<string> WordsFromKebab(string str) => WordsFromSeparator(str, '-');
    private static IEnumerable<string> WordsFromSeparator(string str, char separator)
    {
        if (string.IsNullOrEmpty(str))
        {
            return new string[0];
        }
        else
        {
            return str.Split(new[] { separator }, StringSplitOptions.RemoveEmptyEntries)
                .Select((s, i) => s.ToLowerInvariant());
        }
    }

    private static string FromCamelCase(string str, string glue)
    {
        if (string.IsNullOrEmpty(str))
        {
            return string.Empty;
        }
        else if (str.Length < 2)
        {
            return str;
        }
        else
        {
            var sb = new StringBuilder();
            sb.Append(char.ToLowerInvariant(str[0]));
            for (int i = 1; i < str.Length; ++i)
            {
                var c = str[i];
                if (char.IsUpper(c))
                {
                    sb.Append(glue);
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
    #endregion

    /// <summary>Converts a camel-case string to snake-case string.</summary>
    /// <param name="str">Input string</param>
    /// <returns>Snake-case string.</returns>
    public static string Camel2Snake(this string str) => FromCamelCase(str, "_");

    /// <summary>Converts a camel-case string to kebab-case string.</summary>
    /// <param name="str">Input string</param>
    /// <returns>Kebab-case string.</returns>
    public static string Camel2Kebab(this string str) => FromCamelCase(str, "-");

    /// <summary>Converts a snake-case string to camel-case string.</summary>
    /// <param name="str">Input string</param>
    /// <returns>Camel-case string.</returns>
    public static string Snake2Camel(this string str)
    {
        var words = WordsFromSnake(str)
            .Select((w, i) => i == 0 ? w.ToLowerInvariant() : w.ToLowerInvariant().UcFirst());

        return string.Join(string.Empty, words);
    }

    /// <summary>Converts a snake-case string to kebab-case string.</summary>
    /// <param name="str">Input string</param>
    /// <returns>Kebab-case string.</returns>
    public static string Snake2Kebab(this string str) => str.Replace('_', '-');

    /// <summary>Converts a kebab-case string to camel-case string.</summary>
    /// <param name="str">Input string</param>
    /// <returns>Kebab-case string.</returns>
    public static string Kebab2Camel(this string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return string.Empty;
        }
        else
        {
            return string.Join(string.Empty, str.Split('-').Select(s => s.UcFirst()));
        }
    }

    /// <summary>Converts a kebab-case string to snake-case string.</summary>
    /// <param name="str">Input string</param>
    /// <returns>Kebab-case string.</returns>
    public static string Kebab2Snake(this string str) => str.Replace('-', '_');

    /// <summary>Scrambles the characters in a string in a random order.</summary>
    /// <param name="str">Input string</param>
    /// <returns>A scrambled string</returns>
    public static string Scramble(this string str)
    {
        var rnd = new Random();
        var sb = new StringBuilder();
        var chars = new List<char>(str);

        int i;
        while (chars.Any())
        {
            i = rnd.Next(0, chars.Count);
            sb.Append(chars[i]);
            chars.RemoveAt(i);
        }

        return sb.ToString();
    }

    public static int NthIndexOf(this string str, char c, int occurance)
    {
        var index = -1;
        var counter = 0;
        while (true)
        {
            var nextIndex = str.IndexOf(c, index);
            if (nextIndex == -1)
            {
                return -1;
            }
            else if (++counter == occurance)
            {
                return nextIndex;
            }
            else
            {
                index = nextIndex;
            }
        }
    }
}
