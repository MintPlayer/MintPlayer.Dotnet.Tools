namespace MintPlayer.CliParser.Extensions;

public static class StringExtensions
{
    public static string[] SplitEqualsSign(this string input, char separator)
    {
        var result = new List<string>();
        var inputSpan = input.AsSpan();
        var lastSeparatorIndex = -1;
        var currentIndex = 0;
        var isQuoted = false;
        do
        {
            if (currentIndex >= inputSpan.Length)
            {
                result.Add(input.Substring(lastSeparatorIndex + 1, currentIndex - lastSeparatorIndex - 1));
                lastSeparatorIndex = currentIndex;
                break;
            }

            switch (inputSpan[currentIndex])
            {
                case '\\':
                    currentIndex++;
                    break;
                case '"':
                    isQuoted = !isQuoted;
                    break;
                default:
                    if ((inputSpan[currentIndex] == separator) && !isQuoted)
                    {
                        result.Add(input.Substring(lastSeparatorIndex + 1, currentIndex - lastSeparatorIndex - 1));
                        lastSeparatorIndex = currentIndex;
                    }
                    break;
            }
            currentIndex++;
        }
        while (lastSeparatorIndex < inputSpan.Length);

        return result.ToArray();
    }
}