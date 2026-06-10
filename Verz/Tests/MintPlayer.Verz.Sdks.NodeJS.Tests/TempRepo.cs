namespace MintPlayer.Verz.Sdks.NodeJS.Tests;

/// <summary>
/// Helper that creates a temp dir, lets the test populate file structure,
/// and best-effort cleans up on Dispose.
/// </summary>
internal sealed class TempRepo : IDisposable
{
    public TempRepo()
    {
        Root = Path.Combine(Path.GetTempPath(), "verz-nodejs-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public void Write(string relativePath, string content)
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public string PathOf(string relativePath) => Path.Combine(Root, relativePath);

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}
