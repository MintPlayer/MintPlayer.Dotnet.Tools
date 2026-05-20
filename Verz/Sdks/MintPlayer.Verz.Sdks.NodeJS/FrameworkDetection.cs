namespace MintPlayer.Verz.Sdks.NodeJS;

internal static class FrameworkDetection
{
    /// <summary>
    /// Inspect the package's dependencies + peerDependencies (in that order)
    /// for the first match against a known framework. Precedence:
    ///   1. @angular/core    (Angular synchronizes all @angular/* on a fixed
    ///                        major cadence — strongest coupling)
    ///   2. react            (community releases follow but not strictly)
    ///   3. vue              (looser ecosystem coupling)
    /// Returns null if no recognized framework is found — the package then
    /// falls into "patch-only" mode for version bumps.
    /// </summary>
    private static readonly string[] OrderedFrameworks = ["@angular/core", "react", "vue"];

    public static int? DetectMajor(
        IReadOnlyDictionary<string, string> dependencies,
        IReadOnlyDictionary<string, string> peerDependencies)
    {
        foreach (var fw in OrderedFrameworks)
        {
            if (TryParseMajor(dependencies, fw, out var fromDeps)) return fromDeps;
            if (TryParseMajor(peerDependencies, fw, out var fromPeer)) return fromPeer;
        }
        return null;
    }

    private static bool TryParseMajor(IReadOnlyDictionary<string, string> deps, string key, out int major)
    {
        major = 0;
        if (!deps.TryGetValue(key, out var range)) return false;
        return TryParseRangeMajor(range, out major);
    }

    /// <summary>
    /// Parse the leading integer from a semver range. Handles common prefixes
    /// (^, ~, &gt;=, =, whitespace) and lower-bounded ranges (e.g. "&gt;=17.0.0 &lt;18").
    /// </summary>
    internal static bool TryParseRangeMajor(string range, out int major)
    {
        major = 0;
        if (string.IsNullOrWhiteSpace(range)) return false;

        var trimmed = range.AsSpan().Trim();
        // Strip leading operator(s).
        while (trimmed.Length > 0 && trimmed[0] is '^' or '~' or '>' or '<' or '=' or ' ')
        {
            trimmed = trimmed[1..];
        }

        // Take leading digits up to '.', '-', whitespace, or end.
        int i = 0;
        while (i < trimmed.Length && char.IsDigit(trimmed[i])) i++;
        if (i == 0) return false;

        return int.TryParse(trimmed[..i], out major);
    }
}
