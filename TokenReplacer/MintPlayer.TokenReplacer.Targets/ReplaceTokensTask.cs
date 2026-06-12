using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using System.Text;

namespace MintPlayer.TokenReplacer.Targets;

/// <summary>
/// MSBuild task that replaces <c>$token$</c>-style placeholders in source files and writes
/// the result to an output file. The output is only rewritten when its content changed,
/// so up-to-date checks and file watchers are not disturbed.
/// </summary>
public class ReplaceTokensTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// The files to process. Each item must carry an <c>OutputFile</c> metadata value with the
    /// path (absolute, or relative to <see cref="BaseDirectory"/>) to write the result to.
    /// </summary>
    [Required]
    public ITaskItem[] SourceFiles { get; set; } = [];

    /// <summary>
    /// The tokens to replace: the item spec is the token name, the <c>Value</c> metadata the replacement.
    /// Token names are matched case-insensitively.
    /// </summary>
    public ITaskItem[] Tokens { get; set; } = [];

    /// <summary>Opening token delimiter. Default <c>$</c>.</summary>
    public string TokenStart { get; set; } = "$";

    /// <summary>Closing token delimiter. Default <c>$</c>.</summary>
    public string TokenEnd { get; set; } = "$";

    /// <summary>
    /// What to do when the source contains a token without a configured value:
    /// <c>Warn</c> (default), <c>Error</c> or <c>Ignore</c>.
    /// </summary>
    public string MissingTokenPolicy { get; set; } = "Warn";

    /// <summary>Directory that relative <c>Output</c> metadata paths are resolved against (typically <c>$(MSBuildProjectDirectory)</c>).</summary>
    public string? BaseDirectory { get; set; }

    /// <summary>
    /// The generated files. Item spec is the output path; all metadata of the corresponding
    /// source item is preserved, plus <c>SourceFile</c> pointing back to the template.
    /// </summary>
    [Output]
    public ITaskItem[] ReplacedFiles { get; private set; } = [];

    /// <inheritdoc/>
    public override bool Execute()
    {
        if (!TryParsePolicy(out var policyIsError, out var policyIsWarn))
        {
            Log.LogError(null, "MPTR005", null, null, 0, 0, 0, 0,
                $"Invalid MissingTokenPolicy '{MissingTokenPolicy}'. Valid values: Warn, Error, Ignore.");
            return false;
        }

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in Tokens)
            tokens[token.ItemSpec] = token.GetMetadata("Value");

        var replacedFiles = new List<ITaskItem>();

        foreach (var source in SourceFiles)
        {
            var sourcePath = source.GetMetadata("FullPath");
            if (!File.Exists(sourcePath))
            {
                Log.LogError(null, "MPTR003", null, sourcePath, 0, 0, 0, 0,
                    $"Token replacement source file not found: {sourcePath}");
                continue;
            }

            var output = source.GetMetadata("OutputFile");
            if (string.IsNullOrEmpty(output))
            {
                Log.LogError(null, "MPTR006", null, sourcePath, 0, 0, 0, 0,
                    $"TokenReplaceFile item '{source.ItemSpec}' has no 'OutputFile' metadata.");
                continue;
            }

            var outputPath = Path.IsPathRooted(output)
                ? Path.GetFullPath(output)
                : Path.GetFullPath(Path.Combine(
                    string.IsNullOrEmpty(BaseDirectory) ? Directory.GetCurrentDirectory() : BaseDirectory, output));

            try
            {
                ReplaceFile(sourcePath, outputPath, tokens, policyIsError, policyIsWarn);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Log.LogError(null, "MPTR007", null, sourcePath, 0, 0, 0, 0,
                    $"Token replacement of '{sourcePath}' into '{outputPath}' failed: {ex.Message}");
                continue;
            }

            var item = new TaskItem(outputPath);
            source.CopyMetadataTo(item);
            item.SetMetadata("SourceFile", sourcePath);
            replacedFiles.Add(item);
        }

        ReplacedFiles = replacedFiles.ToArray();
        return !Log.HasLoggedErrors;
    }

    private void ReplaceFile(string sourcePath, string outputPath, IReadOnlyDictionary<string, string> tokens, bool policyIsError, bool policyIsWarn)
    {
        var sourceBytes = File.ReadAllBytes(sourcePath);
        var hasBom = sourceBytes.Length >= 3 && sourceBytes[0] == 0xEF && sourceBytes[1] == 0xBB && sourceBytes[2] == 0xBF;
        var text = Encoding.UTF8.GetString(sourceBytes, hasBom ? 3 : 0, sourceBytes.Length - (hasBom ? 3 : 0));

        var result = TokenReplacementEngine.Replace(text, tokens, TokenStart, TokenEnd);

        foreach (var token in result.UnmatchedTokens)
        {
            var message = $"No value configured for token '{TokenStart}{token}{TokenEnd}' in '{sourcePath}'. Declare a TokenReplaceValue item named '{token}'.";
            if (policyIsError)
                Log.LogError(null, "MPTR002", null, sourcePath, 0, 0, 0, 0, message);
            else if (policyIsWarn)
                Log.LogWarning(null, "MPTR002", null, sourcePath, 0, 0, 0, 0, message);
        }

        var payload = Encoding.UTF8.GetBytes(result.Content);
        byte[] outputBytes;
        if (hasBom)
        {
            outputBytes = new byte[payload.Length + 3];
            outputBytes[0] = 0xEF; outputBytes[1] = 0xBB; outputBytes[2] = 0xBF;
            payload.CopyTo(outputBytes, 3);
        }
        else
        {
            outputBytes = payload;
        }

        if (File.Exists(outputPath) && outputBytes.SequenceEqual(File.ReadAllBytes(outputPath)))
        {
            Log.LogMessage(MessageImportance.Low, $"Token replacement output '{outputPath}' is unchanged, skipping write.");
            return;
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(outputPath, outputBytes);
        Log.LogMessage(MessageImportance.Normal, $"Replaced {result.ReplacedCount} token(s): '{sourcePath}' -> '{outputPath}'.");
    }

    private bool TryParsePolicy(out bool isError, out bool isWarn)
    {
        isError = string.Equals(MissingTokenPolicy, "Error", StringComparison.OrdinalIgnoreCase);
        isWarn = string.Equals(MissingTokenPolicy, "Warn", StringComparison.OrdinalIgnoreCase);
        return isError || isWarn || string.Equals(MissingTokenPolicy, "Ignore", StringComparison.OrdinalIgnoreCase);
    }
}
