namespace MintPlayer.SourceGenerators.Tools.Extensions
{
    internal static class StringExtensions
    {
        internal static string NullIfEmpty(this string value)
            => string.IsNullOrEmpty(value) ? null : value;
    }
}
