using Microsoft.Extensions.DependencyInjection;
using MintPlayer.Assertions;
using MintPlayer.FolderHasher.Abstractions;
using System.Security.Cryptography;

namespace MintPlayer.FolderHasher.Tests;

public class FolderHasherTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IFolderHasher _hasher;

    public FolderHasherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FolderHasherTests_" + Guid.NewGuid());
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
    public async Task GetFolderHashAsync_EmptyFolder_ReturnsConsistentHash()
    {
        // Arrange - folder is already empty

        // Act
        var hash1 = await _hasher.GetFolderHashAsync(_tempDir);
        var hash2 = await _hasher.GetFolderHashAsync(_tempDir);

        // Assert
        hash1.Should().NotBeNullOrEmpty();
        hash2.Should().Be(hash1);
    }

    [Fact]
    public async Task GetFolderHashAsync_SameContent_ReturnsSameHash()
    {
        // Arrange
        var file1 = Path.Combine(_tempDir, "file1.txt");
        var file2 = Path.Combine(_tempDir, "file2.txt");
        await File.WriteAllTextAsync(file1, "Hello World");
        await File.WriteAllTextAsync(file2, "Test Content");

        // Act
        var hash1 = await _hasher.GetFolderHashAsync(_tempDir);
        var hash2 = await _hasher.GetFolderHashAsync(_tempDir);

        // Assert
        hash2.Should().Be(hash1);
    }

    [Fact]
    public async Task GetFolderHashAsync_DifferentContent_ReturnsDifferentHash()
    {
        // Arrange
        var file = Path.Combine(_tempDir, "file.txt");
        await File.WriteAllTextAsync(file, "Content A");
        var hash1 = await _hasher.GetFolderHashAsync(_tempDir);

        await File.WriteAllTextAsync(file, "Content B");

        // Act
        var hash2 = await _hasher.GetFolderHashAsync(_tempDir);

        // Assert
        hash2.Should().NotBe(hash1);
    }

    [Fact]
    public async Task GetFolderHashAsync_NewFile_ReturnsDifferentHash()
    {
        // Arrange
        var file1 = Path.Combine(_tempDir, "file1.txt");
        await File.WriteAllTextAsync(file1, "Content");
        var hash1 = await _hasher.GetFolderHashAsync(_tempDir);

        var file2 = Path.Combine(_tempDir, "file2.txt");
        await File.WriteAllTextAsync(file2, "New Content");

        // Act
        var hash2 = await _hasher.GetFolderHashAsync(_tempDir);

        // Assert
        hash2.Should().NotBe(hash1);
    }

    [Fact]
    public async Task GetFolderHashAsync_DeletedFile_ReturnsDifferentHash()
    {
        // Arrange
        var file1 = Path.Combine(_tempDir, "file1.txt");
        var file2 = Path.Combine(_tempDir, "file2.txt");
        await File.WriteAllTextAsync(file1, "Content 1");
        await File.WriteAllTextAsync(file2, "Content 2");
        var hash1 = await _hasher.GetFolderHashAsync(_tempDir);

        File.Delete(file2);

        // Act
        var hash2 = await _hasher.GetFolderHashAsync(_tempDir);

        // Assert
        hash2.Should().NotBe(hash1);
    }

    [Fact]
    public async Task GetFolderHashAsync_WithHasherIgnore_ExcludesIgnoredFiles()
    {
        // Arrange
        var file1 = Path.Combine(_tempDir, "app.js");
        var file2 = Path.Combine(_tempDir, "debug.log");
        var ignoreFile = Path.Combine(_tempDir, ".hasherignore");

        await File.WriteAllTextAsync(file1, "console.log('hello');");
        await File.WriteAllTextAsync(file2, "Debug output");
        await File.WriteAllTextAsync(ignoreFile, "*.log");

        var hash1 = await _hasher.GetFolderHashAsync(_tempDir);

        // Modify the ignored file
        await File.WriteAllTextAsync(file2, "Modified debug output");

        // Act
        var hash2 = await _hasher.GetFolderHashAsync(_tempDir);

        // Assert - hash should be the same since .log files are ignored
        hash2.Should().Be(hash1);
    }

    [Fact]
    public async Task GetFolderHashAsync_NestedDirectories_IncludesAllFiles()
    {
        // Arrange
        var subDir = Path.Combine(_tempDir, "subdir");
        var deepDir = Path.Combine(subDir, "deep");
        Directory.CreateDirectory(deepDir);

        await File.WriteAllTextAsync(Path.Combine(_tempDir, "root.txt"), "Root");
        await File.WriteAllTextAsync(Path.Combine(subDir, "sub.txt"), "Sub");
        await File.WriteAllTextAsync(Path.Combine(deepDir, "deep.txt"), "Deep");

        var hash1 = await _hasher.GetFolderHashAsync(_tempDir);

        // Modify a deeply nested file
        await File.WriteAllTextAsync(Path.Combine(deepDir, "deep.txt"), "Modified Deep");

        // Act
        var hash2 = await _hasher.GetFolderHashAsync(_tempDir);

        // Assert
        hash2.Should().NotBe(hash1);
    }

    [Fact]
    public async Task GetFolderHashAsync_WithIgnoreFolders_ExcludesMatchingFolders()
    {
        // Arrange
        var nodeModules = Path.Combine(_tempDir, "node_modules");
        Directory.CreateDirectory(nodeModules);

        await File.WriteAllTextAsync(Path.Combine(_tempDir, "app.js"), "App code");
        await File.WriteAllTextAsync(Path.Combine(nodeModules, "package.json"), "Dependencies");

        var hash1 = await _hasher.GetFolderHashAsync(_tempDir, ["node_modules"]);

        // Modify a file in ignored folder
        await File.WriteAllTextAsync(Path.Combine(nodeModules, "package.json"), "Modified Dependencies");

        // Act
        var hash2 = await _hasher.GetFolderHashAsync(_tempDir, ["node_modules"]);

        // Assert - hash should be the same since node_modules is ignored
        hash2.Should().Be(hash1);
    }

    [Fact]
    public async Task GetFolderHashAsync_CustomAlgorithm_UsesSpecifiedAlgorithm()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "file.txt"), "Content");

        // Act
        using var sha256 = SHA256.Create();
        using var sha512 = SHA512.Create();

        var hashSha256 = await _hasher.GetFolderHashAsync(_tempDir, [], sha256);

        // Create new hasher for fresh algorithm state
        var services = new ServiceCollection();
        services.AddFolderHasher();
        var provider = services.BuildServiceProvider();
        var hasher2 = provider.GetRequiredService<IFolderHasher>();

        var hashSha512 = await hasher2.GetFolderHashAsync(_tempDir, [], sha512);

        // Assert - different algorithms produce different length hashes
        using (new AssertionScope("hash lengths"))
        {
            hashSha256.Should().HaveLength(64);  // SHA256 = 32 bytes = 64 hex chars
            hashSha512.Should().HaveLength(128); // SHA512 = 64 bytes = 128 hex chars
        }
    }

    [Fact]
    public async Task GetFolderHashAsync_SpecialCharactersInFileName_HandlesCorrectly()
    {
        // Arrange
        var file = Path.Combine(_tempDir, "file with spaces.txt");
        await File.WriteAllTextAsync(file, "Content");

        // Act
        var hash = await _hasher.GetFolderHashAsync(_tempDir);

        // Assert
        hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetFolderHashAsync_NestedHasherIgnore_AppliesCorrectly()
    {
        // Arrange
        var subDir = Path.Combine(_tempDir, "subdir");
        Directory.CreateDirectory(subDir);

        // Root ignore file ignores all .log files
        await File.WriteAllTextAsync(Path.Combine(_tempDir, ".hasherignore"), "*.log");

        // Subdirectory ignore file ignores .tmp files
        await File.WriteAllTextAsync(Path.Combine(subDir, ".hasherignore"), "*.tmp");

        await File.WriteAllTextAsync(Path.Combine(_tempDir, "root.txt"), "Root content");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "root.log"), "Root log");
        await File.WriteAllTextAsync(Path.Combine(subDir, "sub.txt"), "Sub content");
        await File.WriteAllTextAsync(Path.Combine(subDir, "sub.log"), "Sub log");
        await File.WriteAllTextAsync(Path.Combine(subDir, "sub.tmp"), "Sub temp");

        var hash1 = await _hasher.GetFolderHashAsync(_tempDir);

        // Modify ignored files
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "root.log"), "Modified root log");
        await File.WriteAllTextAsync(Path.Combine(subDir, "sub.log"), "Modified sub log");
        await File.WriteAllTextAsync(Path.Combine(subDir, "sub.tmp"), "Modified sub temp");

        // Act
        var hash2 = await _hasher.GetFolderHashAsync(_tempDir);

        // Assert - hash should be the same since all modified files are ignored
        hash2.Should().Be(hash1);
    }

    [Fact]
    public async Task GetFolderHashAsync_HashIsLowercaseHex()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "file.txt"), "Content");

        // Act
        var hash = await _hasher.GetFolderHashAsync(_tempDir);

        // Assert - hash should be lowercase hexadecimal
        hash.Should().MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public async Task GetFolderHashAsync_DeterministicAcrossRuns()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.txt"), "AAA");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "b.txt"), "BBB");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "c.txt"), "CCC");

        // Act - compute hash multiple times with new hasher instances
        var hashes = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var services = new ServiceCollection();
            services.AddFolderHasher();
            var provider = services.BuildServiceProvider();
            var hasher = provider.GetRequiredService<IFolderHasher>();
            hashes.Add(await hasher.GetFolderHashAsync(_tempDir));
        }

        // Assert - all hashes should be identical
        hashes.Should().AllSatisfy(h => h.Should().Be(hashes[0]));
    }
}

/// <summary>
/// The known-answer test for the folder hashing scheme.
/// </summary>
/// <remarks>
/// Every other test in this file is RELATIVE — same content hashes the same, different content
/// hashes differently, the value is lowercase hex. All of them keep passing if the scheme changes
/// wholesale, because they only ever compare the implementation against itself. That is a real
/// gap: the hash is a cache key, so changing it silently invalidates every downstream cache and
/// nothing in the suite notices.
///
/// This test pins one fixed input tree to one fixed output. It is deliberately brittle. If it
/// fails, either the change was unintended, or it was intended and the constant below needs
/// updating in the same commit — with the cache-invalidation consequences stated in the message.
///
/// The expected value is path-independent (verified by running the same tree from two different
/// temp directories), so it is safe to pin.
/// </remarks>
public sealed class FolderHasherGoldenTests : IDisposable
{
    private const string ExpectedSha256 = "c117ec420eb08e1d5d874b2da488ea8e34d554be7d9564bdb1e95a21c097fb31";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "FolderHasherGolden_" + Guid.NewGuid());
    private readonly IFolderHasher _hasher;

    public FolderHasherGoldenTests()
    {
        Directory.CreateDirectory(_dir);
        var services = new ServiceCollection();
        services.AddFolderHasher();
        _hasher = services.BuildServiceProvider().GetRequiredService<IFolderHasher>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    /// <summary>Two files, one nested, with fixed contents.</summary>
    private void WriteFixtureTree()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "sub"));
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(_dir, "sub", "b.txt"), "beta");
    }

    [Fact]
    public async Task TheHashingSchemeHasNotChanged()
    {
        WriteFixtureTree();

        var hash = await _hasher.GetFolderHashAsync(_dir);

        hash.Should().Be(
            ExpectedSha256,
            "the folder hash is a cache key — if this changed deliberately, update the constant " +
            "and expect every downstream cache to invalidate");
    }

    /// <summary>
    /// The golden value must not depend on where the tree happens to live, or it would be a test
    /// of the temp path rather than of the hashing scheme.
    /// </summary>
    [Fact]
    public async Task TheHashDoesNotDependOnTheFolderPath()
    {
        WriteFixtureTree();
        var first = await _hasher.GetFolderHashAsync(_dir);

        var elsewhere = Path.Combine(Path.GetTempPath(), "FolderHasherGolden_" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(elsewhere, "sub"));
        File.WriteAllText(Path.Combine(elsewhere, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(elsewhere, "sub", "b.txt"), "beta");

        try
        {
            var second = await _hasher.GetFolderHashAsync(elsewhere);
            second.Should().Be(first);
        }
        finally
        {
            Directory.Delete(elsewhere, true);
        }
    }
}
