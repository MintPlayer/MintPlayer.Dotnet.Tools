using System.Runtime.CompilerServices;

namespace MintPlayer.SourceGenerators.Tests._Infrastructure;

/// <summary>
/// A minimal approval-test mechanism for the four large producers, where hand-writing
/// assertions over 15–25KB of generated output is unmaintainable but a formatting or shape
/// regression is worth catching.
/// </summary>
/// <remarks>
/// Hand-rolled rather than Verify.SourceGenerators. Verify is a fine library, but the whole
/// mechanism here is ~40 lines, and it avoids a dependency with a fast-moving major-version
/// cadence plus the DiffEngine auto-launch that has to be disabled in CI anyway.
///
/// A snapshot proves the output did not CHANGE — never that it is correct. So every snapshot
/// test also asserts the generated code compiles; otherwise an accepted-but-wrong snapshot
/// silently locks the bug in.
///
/// To accept new output, set MINTPLAYER_ACCEPT_SNAPSHOTS=1 and re-run, then review the diff.
/// </remarks>
internal static class Snapshot
{
    private const string AcceptEnvironmentVariable = "MINTPLAYER_ACCEPT_SNAPSHOTS";

    public static void Match(string actual, [CallerFilePath] string callerFile = "", [CallerMemberName] string caller = "")
    {
        var directory = Path.Combine(Path.GetDirectoryName(callerFile)!, "Snapshots");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(callerFile)}.{caller}.verified.txt");
        var normalized = Normalize(actual);

        if (Environment.GetEnvironmentVariable(AcceptEnvironmentVariable) == "1")
        {
            File.WriteAllText(path, normalized);
            return;
        }

        if (!File.Exists(path))
        {
            File.WriteAllText(Path.ChangeExtension(path, ".received.txt"), normalized);
            throw new InvalidOperationException(
                $"No snapshot at '{path}'. The output was written alongside it as .received.txt. " +
                $"Review it, then re-run with {AcceptEnvironmentVariable}=1 to accept.");
        }

        var expected = Normalize(File.ReadAllText(path));

        if (expected == normalized) return;

        File.WriteAllText(Path.ChangeExtension(path, ".received.txt"), normalized);
        throw new InvalidOperationException(
            $"Generated output no longer matches '{Path.GetFileName(path)}'. The new output was written " +
            $"alongside it as .received.txt. If the change is intended, re-run with " +
            $"{AcceptEnvironmentVariable}=1 to accept it." + Environment.NewLine + Environment.NewLine +
            FirstDifference(expected, normalized));
    }

    /// <summary>Line endings only — the content itself must match exactly.</summary>
    private static string Normalize(string value)
        => value.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd() + "\n";

    private static string FirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');

        for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            var e = i < expectedLines.Length ? expectedLines[i] : "<end of file>";
            var a = i < actualLines.Length ? actualLines[i] : "<end of file>";

            if (e != a)
                return $"First difference at line {i + 1}:{Environment.NewLine}  expected: {e}{Environment.NewLine}  actual:   {a}";
        }

        return "Files differ only in trailing whitespace.";
    }
}
