namespace MintPlayer.Verz.Sdks.Dotnet;

internal static class TfmHelper
{
    public static bool IsNetTfm(string tfm) =>
        tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase) &&
        tfm.Length >= 4 &&
        char.IsDigit(tfm[3]);

    public static int ParseNetMajor(string tfm)
    {
        // net8.0 -> 8; net10.0 -> 10
        var digits = new string(tfm.Skip(3).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var major) ? major : 0;
    }

    public static int ParseNetMinor(string tfm)
    {
        var idx = tfm.IndexOf('.') + 1;
        if (idx <= 0) return 0;
        var minorDigits = new string(tfm.Skip(idx).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(minorDigits, out var minor) ? minor : 0;
    }

    public static int? DetectMajorFromTargets(IEnumerable<string> targetFrameworks)
    {
        var max = targetFrameworks
            .Where(IsNetTfm)
            .Select(ParseNetMajor)
            .DefaultIfEmpty(0)
            .Max();
        return max > 0 ? max : null;
    }
}
