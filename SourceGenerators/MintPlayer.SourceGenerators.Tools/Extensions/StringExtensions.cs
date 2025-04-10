using System.Text.RegularExpressions;

namespace MintPlayer.SourceGenerators.Tools.Extensions;

public static class StringExtensions
{
    public static string SwitchCasing(this string str, ECasingStyle input, ECasingStyle output)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        // Normalize the input to a list of words
        var words = SplitWords(str, input);

        // Convert to the desired output style
        switch (output)
        {
            case ECasingStyle.Pascal:
                return string.Concat(words.Select(Capitalize));
            case ECasingStyle.Camel:
                return string.Concat(words.Take(1).Select(w => w.ToLower()))
                       + string.Concat(words.Skip(1).Select(Capitalize));
            case ECasingStyle.Snake:
                return string.Join("_", words).ToLower();
            case ECasingStyle.Kebab:
                return string.Join("-", words).ToLower();
            case ECasingStyle.UpperSnake:
                return string.Join("_", words).ToUpper();
            case ECasingStyle.UpperKebab:
                return string.Join("-", words).ToUpper();
            default:
                return str;
        }
    }

    private static List<string> SplitWords(string str, ECasingStyle casing)
    {
        switch (casing)
        {
            case ECasingStyle.Pascal:
            case ECasingStyle.Camel:
                return Regex.Matches(str, @"[A-Z]?[a-z]+|[A-Z]+(?![a-z])")
                    .Cast<Match>()
                    .Select(m => m.Value.ToLower())
                    .ToList();
            case ECasingStyle.Snake:
            case ECasingStyle.UpperSnake:
                return str.Split('_')
                    .Select(w => w.ToLower())
                    .ToList();
            case ECasingStyle.Kebab:
            case ECasingStyle.UpperKebab:
                return str.Split('-')
                    .Select(w => w.ToLower())
                    .ToList();
            default:
                return [str];
        }
    }

    private static string Capitalize(string word)
    {
        return string.IsNullOrEmpty(word)
            ? word
            : char.ToUpper(word[0]) + word.Substring(1).ToLower();
    }
}

public enum ECasingStyle
{
    /// <summary>redRidingHoodWalksThroughTheWoods</summary>
    Pascal,
    /// <summary>RedRidingHoodWalksThroughTheWoods</summary>
    Camel,
    /// <summary>red_riding_hood_walks_through_the_woods</summary>
    Snake,
    /// <summary>red-riding-hood-walks-through-the-woods</summary>
    Kebab,
    /// <summary>RED_RIDING_HOOD_WALKS_THROUGH_THE_WOODS</summary>
    UpperSnake,
    /// <summary>RED-RIDING-HOOD-WALKS-THROUGH-THE-WOODS</summary>
    UpperKebab
}