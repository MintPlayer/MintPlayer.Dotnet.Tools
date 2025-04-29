var rootPath = Directory.GetCurrentDirectory();
var foldersToDelete = new[]
{
    "bin", "obj", "bin-windows", "bin-linux", "obj-windows", "obj-linux", "tmp-build"
};

var allDirectories = Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
    .Where(dir => foldersToDelete.Contains(Path.GetFileName(dir), StringComparer.OrdinalIgnoreCase));

foreach (var binDir in allDirectories)
{
    try
    {
        Console.WriteLine($"Deleting: {binDir}");
        Directory.Delete(binDir, recursive: true);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to delete {binDir}: {ex.Message}");
    }
}