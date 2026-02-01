namespace MintPlayer.FolderHasher.Tests;

public class HasherIgnoreParserTests
{
    [Fact]
    public void AddPattern_CommentLine_IsIgnored()
    {
        // Arrange
        var parser = new HasherIgnoreParser();
        var basePath = @"C:\TestFolder";

        // Act
        parser.AddPattern("# This is a comment", basePath);
        parser.AddPattern("  # Another comment with leading spaces", basePath);

        // Assert - no patterns should be added, so nothing should be ignored
        Assert.False(parser.IsIgnored(@"C:\TestFolder\anyfile.txt"));
    }

    [Fact]
    public void AddPattern_EmptyLine_IsIgnored()
    {
        // Arrange
        var parser = new HasherIgnoreParser();
        var basePath = @"C:\TestFolder";

        // Act
        parser.AddPattern("", basePath);
        parser.AddPattern("   ", basePath);

        // Assert - no patterns should be added
        Assert.False(parser.IsIgnored(@"C:\TestFolder\anyfile.txt"));
    }

    [Fact]
    public void IsIgnored_SimpleFilePattern_MatchesInAnyDirectory()
    {
        // Arrange
        var parser = new HasherIgnoreParser();
        var basePath = @"C:\TestFolder";

        // Act
        parser.AddPattern("*.log", basePath);

        // Assert
        Assert.True(parser.IsIgnored(@"C:\TestFolder\app.log"));
        Assert.True(parser.IsIgnored(@"C:\TestFolder\subdir\error.log"));
        Assert.True(parser.IsIgnored(@"C:\TestFolder\deep\nested\folder\debug.log"));
        Assert.False(parser.IsIgnored(@"C:\TestFolder\app.txt"));
    }

    [Fact]
    public void IsIgnored_DirectoryPattern_MatchesAtRoot()
    {
        // Arrange
        var parser = new HasherIgnoreParser();
        var basePath = @"C:\TestFolder";

        // Act
        // node_modules/ gets normalized to node_modules/** (root-level only, because it contains /)
        parser.AddPattern("node_modules/", basePath);

        // Assert
        Assert.True(parser.IsIgnored(@"C:\TestFolder\node_modules\package.json"));
        Assert.True(parser.IsIgnored(@"C:\TestFolder\node_modules\deep\nested\index.js"));
        // Does not match in subdirectories (use **/node_modules/ for that)
        Assert.False(parser.IsIgnored(@"C:\TestFolder\subdir\node_modules\index.js"));
        Assert.False(parser.IsIgnored(@"C:\TestFolder\node_modules_backup\file.txt"));
    }

    [Fact]
    public void IsIgnored_DirectoryPatternWithDoubleStar_MatchesAnywhere()
    {
        // Arrange
        var parser = new HasherIgnoreParser();
        var basePath = @"C:\TestFolder";

        // Act
        // **/node_modules/ matches node_modules anywhere in the tree
        parser.AddPattern("**/node_modules/", basePath);

        // Assert
        Assert.True(parser.IsIgnored(@"C:\TestFolder\node_modules\package.json"));
        Assert.True(parser.IsIgnored(@"C:\TestFolder\subdir\node_modules\index.js"));
        Assert.True(parser.IsIgnored(@"C:\TestFolder\deep\nested\node_modules\lib\file.js"));
    }

    [Fact]
    public void IsIgnored_NegationPattern_ExcludesFromIgnore()
    {
        // Arrange
        var parser = new HasherIgnoreParser();
        var basePath = @"C:\TestFolder";

        // Act
        parser.AddPattern("*.log", basePath);
        parser.AddPattern("!important.log", basePath);

        // Assert
        Assert.True(parser.IsIgnored(@"C:\TestFolder\app.log"));
        Assert.True(parser.IsIgnored(@"C:\TestFolder\error.log"));
        Assert.False(parser.IsIgnored(@"C:\TestFolder\important.log"));
        Assert.False(parser.IsIgnored(@"C:\TestFolder\subdir\important.log"));
    }

    [Fact]
    public void IsIgnored_LeadingSlash_MatchesFromRoot()
    {
        // Arrange
        var parser = new HasherIgnoreParser();
        var basePath = @"C:\TestFolder";

        // Act
        parser.AddPattern("/build", basePath);

        // Assert
        Assert.True(parser.IsIgnored(@"C:\TestFolder\build\output.dll"));
        // Without leading slash, would match in subdirs too, but with leading slash it's relative to base
    }

    [Fact]
    public void IsIgnored_DoubleStarPattern_MatchesRecursively()
    {
        // Arrange
        var parser = new HasherIgnoreParser();
        var basePath = @"C:\TestFolder";

        // Act
        parser.AddPattern("**/temp/**", basePath);

        // Assert
        Assert.True(parser.IsIgnored(@"C:\TestFolder\temp\file.txt"));
        Assert.True(parser.IsIgnored(@"C:\TestFolder\src\temp\cache.dat"));
        Assert.True(parser.IsIgnored(@"C:\TestFolder\deep\nested\temp\data\file.bin"));
    }

    [Fact]
    public void IsIgnored_MultiplePatterns_AllApply()
    {
        // Arrange
        var parser = new HasherIgnoreParser();
        var basePath = @"C:\TestFolder";

        // Act
        parser.AddPattern("*.log", basePath);
        parser.AddPattern("*.tmp", basePath);
        parser.AddPattern("node_modules/", basePath);

        // Assert
        Assert.True(parser.IsIgnored(@"C:\TestFolder\app.log"));
        Assert.True(parser.IsIgnored(@"C:\TestFolder\cache.tmp"));
        Assert.True(parser.IsIgnored(@"C:\TestFolder\node_modules\pkg\index.js"));
        Assert.False(parser.IsIgnored(@"C:\TestFolder\app.js"));
    }

    [Fact]
    public void IsIgnored_CaseInsensitive_OnWindows()
    {
        // Arrange
        var parser = new HasherIgnoreParser();
        var basePath = @"C:\TestFolder";

        // Act
        parser.AddPattern("*.LOG", basePath);

        // Assert - should match regardless of case on Windows
        Assert.True(parser.IsIgnored(@"C:\TestFolder\app.log"));
        Assert.True(parser.IsIgnored(@"C:\TestFolder\APP.LOG"));
        Assert.True(parser.IsIgnored(@"C:\TestFolder\App.Log"));
    }

    [Fact]
    public void IsIgnored_DifferentBasePath_OnlyMatchesWithinBasePath()
    {
        // Arrange
        var parser = new HasherIgnoreParser();

        // Act
        parser.AddPattern("*.log", @"C:\TestFolder\src");

        // Assert
        Assert.True(parser.IsIgnored(@"C:\TestFolder\src\app.log"));
        Assert.True(parser.IsIgnored(@"C:\TestFolder\src\subdir\error.log"));
        Assert.False(parser.IsIgnored(@"C:\TestFolder\other\app.log"));
        Assert.False(parser.IsIgnored(@"C:\TestFolder\app.log"));
    }

    [Fact]
    public void AddPatternsFromFile_NonExistentFile_DoesNotThrow()
    {
        // Arrange
        var parser = new HasherIgnoreParser();

        // Act & Assert - should not throw
        parser.AddPatternsFromFile(@"C:\NonExistent\Path\.hasherignore");
    }

    [Fact]
    public void AddPatternsFromFile_ValidFile_ParsesPatterns()
    {
        // Arrange
        var parser = new HasherIgnoreParser();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var ignoreFile = Path.Combine(tempDir, ".hasherignore");
            File.WriteAllText(ignoreFile, """
                # Comment line
                *.log
                node_modules/
                !important.log

                # Another comment
                *.tmp
                """);

            // Act
            parser.AddPatternsFromFile(ignoreFile);

            // Assert
            Assert.True(parser.IsIgnored(Path.Combine(tempDir, "app.log")));
            Assert.True(parser.IsIgnored(Path.Combine(tempDir, "cache.tmp")));
            Assert.True(parser.IsIgnored(Path.Combine(tempDir, "node_modules", "pkg.json")));
            Assert.False(parser.IsIgnored(Path.Combine(tempDir, "important.log")));
            Assert.False(parser.IsIgnored(Path.Combine(tempDir, "app.js")));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void IsIgnored_SpecificFileInDirectory_MatchesCorrectly()
    {
        // Arrange
        var parser = new HasherIgnoreParser();
        var basePath = @"C:\TestFolder";

        // Act
        parser.AddPattern("dist/*.js", basePath);

        // Assert
        Assert.True(parser.IsIgnored(@"C:\TestFolder\dist\bundle.js"));
        Assert.False(parser.IsIgnored(@"C:\TestFolder\dist\styles.css"));
        Assert.False(parser.IsIgnored(@"C:\TestFolder\src\app.js"));
    }
}
