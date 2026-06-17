namespace MintPlayer.SlnLaunch.Models;

/// <summary>
/// The pool of extra arguments passed to <c>slnlaunch</c> after a <c>--</c> separator, indexed by option
/// name. Each project forwards only the names it opted into via <see cref="LaunchProjectEntry.ForwardArguments"/>.
/// </summary>
/// <remarks>
/// Tokens are kept in their original form. <c>--tenant acme</c> is stored under <c>tenant</c> as the pair
/// <c>["--tenant", "acme"]</c>; <c>--verbose</c> as <c>["--verbose"]</c>; <c>--tenant=acme</c> as
/// <c>["--tenant=acme"]</c>. A name may occur more than once. Names are matched ignoring leading dashes.
/// </remarks>
public sealed class ForwardableArguments
{
    public static readonly ForwardableArguments Empty = new([]);

    private readonly Dictionary<string, List<string[]>> _byName;

    private ForwardableArguments(Dictionary<string, List<string[]>> byName) => _byName = byName;

    public static ForwardableArguments Parse(IReadOnlyList<string> tokens)
    {
        var byName = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!token.StartsWith('-'))
                continue; // a stray positional has no name to forward by; ignore it.

            var trimmed = token.TrimStart('-');
            var equals = trimmed.IndexOf('=');

            string name;
            string[] occurrence;
            if (equals >= 0)
            {
                name = trimmed[..equals];
                occurrence = [token];
            }
            else if (i + 1 < tokens.Count && !tokens[i + 1].StartsWith('-'))
            {
                name = trimmed;
                occurrence = [token, tokens[i + 1]];
                i++; // consume the value
            }
            else
            {
                name = trimmed; // a flag with no value
                occurrence = [token];
            }

            if (name.Length == 0)
                continue;

            if (!byName.TryGetValue(name, out var list))
                byName[name] = list = [];
            list.Add(occurrence);
        }

        return new ForwardableArguments(byName);
    }

    /// <summary>
    /// Returns the tokens to forward for the given option names, in the order the names are listed,
    /// preserving the original token form. Unknown names contribute nothing.
    /// </summary>
    public IReadOnlyList<string> Select(IEnumerable<string> names)
    {
        var result = new List<string>();
        foreach (var name in names)
        {
            if (_byName.TryGetValue(name.TrimStart('-'), out var occurrences))
                foreach (var occurrence in occurrences)
                    result.AddRange(occurrence);
        }
        return result;
    }
}
