namespace MintPlayer.StringExtensions;

public static class RepeatExtensions
{
    public static string Repeat(this string text, int count)
        => string.Join(string.Empty, Enumerable.Range(0, count).Select(_ => text));

    public static string Repeat(this char c, int count)
        => new string(Enumerable.Range(0, count).Select(_ => c).ToArray());
}
