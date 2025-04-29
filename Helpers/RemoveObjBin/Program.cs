var rootPath = Directory.GetCurrentDirectory();
Console.WriteLine("Current Directory: " + rootPath);

foreach (var binDir in Directory.EnumerateDirectories(rootPath, "bin|obj|bin-windows|bin-linux|obj-windows|obj-linux|tmp-build", SearchOption.AllDirectories))
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