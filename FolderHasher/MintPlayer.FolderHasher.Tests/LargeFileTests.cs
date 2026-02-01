using Microsoft.Extensions.DependencyInjection;
using MintPlayer.FolderHasher.Abstractions;

namespace MintPlayer.FolderHasher.Tests;

public class LargeFileTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IFolderHasher _hasher;

    public LargeFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LargeFileTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        services.AddFolderHasher();
        var provider = services.BuildServiceProvider();
        _hasher = provider.GetRequiredService<IFolderHasher>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task GetFolderHashAsync_LargeFile_ProducesDeterministicHash()
    {
        // Arrange - create a file just over the 10MB threshold
        var largeFile = Path.Combine(_tempDir, "large.bin");
        var fileSize = 11 * 1024 * 1024; // 11MB
        var content = new byte[fileSize];
        new Random(42).NextBytes(content); // Seed for reproducibility
        await File.WriteAllBytesAsync(largeFile, content);

        // Act
        var hash1 = await _hasher.GetFolderHashAsync(_tempDir);
        var hash2 = await _hasher.GetFolderHashAsync(_tempDir);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public async Task GetFolderHashAsync_LargeFileModified_ProducesDifferentHash()
    {
        // Arrange
        var largeFile = Path.Combine(_tempDir, "large.bin");
        var fileSize = 11 * 1024 * 1024; // 11MB
        var content = new byte[fileSize];
        new Random(42).NextBytes(content);
        await File.WriteAllBytesAsync(largeFile, content);

        var hash1 = await _hasher.GetFolderHashAsync(_tempDir);

        // Modify just one byte
        content[fileSize / 2] = (byte)(content[fileSize / 2] + 1);
        await File.WriteAllBytesAsync(largeFile, content);

        // Act
        var hash2 = await _hasher.GetFolderHashAsync(_tempDir);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task GetFolderHashAsync_MixedSmallAndLargeFiles_HandlesCorrectly()
    {
        // Arrange
        var smallFile = Path.Combine(_tempDir, "small.txt");
        var largeFile = Path.Combine(_tempDir, "large.bin");

        await File.WriteAllTextAsync(smallFile, "Small content");

        var largeContent = new byte[11 * 1024 * 1024];
        new Random(42).NextBytes(largeContent);
        await File.WriteAllBytesAsync(largeFile, largeContent);

        // Act
        var hash1 = await _hasher.GetFolderHashAsync(_tempDir);

        // Modify small file
        await File.WriteAllTextAsync(smallFile, "Modified small content");
        var hash2 = await _hasher.GetFolderHashAsync(_tempDir);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public async Task GetFolderHashAsync_FileBelowThreshold_HandlesCorrectly()
    {
        // Arrange - create a file just below the 10MB threshold
        var file = Path.Combine(_tempDir, "medium.bin");
        var fileSize = 9 * 1024 * 1024; // 9MB
        var content = new byte[fileSize];
        new Random(42).NextBytes(content);
        await File.WriteAllBytesAsync(file, content);

        // Act
        var hash1 = await _hasher.GetFolderHashAsync(_tempDir);
        var hash2 = await _hasher.GetFolderHashAsync(_tempDir);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public async Task GetFolderHashAsync_ExactlyAtThreshold_HandlesCorrectly()
    {
        // Arrange - create a file exactly at the 10MB threshold
        var file = Path.Combine(_tempDir, "threshold.bin");
        var fileSize = 10 * 1024 * 1024; // Exactly 10MB
        var content = new byte[fileSize];
        new Random(42).NextBytes(content);
        await File.WriteAllBytesAsync(file, content);

        // Act
        var hash1 = await _hasher.GetFolderHashAsync(_tempDir);
        var hash2 = await _hasher.GetFolderHashAsync(_tempDir);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public async Task GetFolderHashAsync_EmptyLargeFile_HandlesCorrectly()
    {
        // Arrange - create an empty file (edge case)
        var emptyFile = Path.Combine(_tempDir, "empty.bin");
        await File.WriteAllBytesAsync(emptyFile, []);

        // Act
        var hash1 = await _hasher.GetFolderHashAsync(_tempDir);
        var hash2 = await _hasher.GetFolderHashAsync(_tempDir);

        // Assert
        Assert.Equal(hash1, hash2);
    }
}
