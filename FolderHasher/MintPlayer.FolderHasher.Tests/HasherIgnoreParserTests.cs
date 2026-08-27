using MintPlayer.Assertions;
using MintPlayer.Assertions.Execution;

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
        parser.IsIgnored(@"C:\TestFolder\anyfile.txt").Should().BeFalse();
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
        parser.IsIgnored(@"C:\TestFolder\anyfile.txt").Should().BeFalse();
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
        using (new AssertionScope("*.log pattern"))
        {
            parser.IsIgnored(@"C:\TestFolder\app.log").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\subdir\error.log").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\deep\nested\folder\debug.log").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\app.txt").Should().BeFalse();
        }
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
        using (new AssertionScope("node_modules/ pattern"))
        {
            parser.IsIgnored(@"C:\TestFolder\node_modules\package.json").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\node_modules\deep\nested\index.js").Should().BeTrue();
            // Does not match in subdirectories (use **/node_modules/ for that)
            parser.IsIgnored(@"C:\TestFolder\subdir\node_modules\index.js").Should().BeFalse();
            parser.IsIgnored(@"C:\TestFolder\node_modules_backup\file.txt").Should().BeFalse();
        }
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
        using (new AssertionScope("**/node_modules/ pattern"))
        {
            parser.IsIgnored(@"C:\TestFolder\node_modules\package.json").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\subdir\node_modules\index.js").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\deep\nested\node_modules\lib\file.js").Should().BeTrue();
        }
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
        using (new AssertionScope("negated *.log pattern"))
        {
            parser.IsIgnored(@"C:\TestFolder\app.log").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\error.log").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\important.log").Should().BeFalse(because: "it is negated");
            parser.IsIgnored(@"C:\TestFolder\subdir\important.log").Should().BeFalse(because: "it is negated");
        }
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
        parser.IsIgnored(@"C:\TestFolder\build\output.dll").Should().BeTrue();
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
        using (new AssertionScope("**/temp/** pattern"))
        {
            parser.IsIgnored(@"C:\TestFolder\temp\file.txt").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\src\temp\cache.dat").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\deep\nested\temp\data\file.bin").Should().BeTrue();
        }
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
        using (new AssertionScope("combined patterns"))
        {
            parser.IsIgnored(@"C:\TestFolder\app.log").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\cache.tmp").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\node_modules\pkg\index.js").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\app.js").Should().BeFalse();
        }
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
        using (new AssertionScope("case-insensitive matching"))
        {
            parser.IsIgnored(@"C:\TestFolder\app.log").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\APP.LOG").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\App.Log").Should().BeTrue();
        }
    }

    [Fact]
    public void IsIgnored_DifferentBasePath_OnlyMatchesWithinBasePath()
    {
        // Arrange
        var parser = new HasherIgnoreParser();

        // Act
        parser.AddPattern("*.log", @"C:\TestFolder\src");

        // Assert
        using (new AssertionScope("base-path scoped pattern"))
        {
            parser.IsIgnored(@"C:\TestFolder\src\app.log").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\src\subdir\error.log").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\other\app.log").Should().BeFalse();
            parser.IsIgnored(@"C:\TestFolder\app.log").Should().BeFalse();
        }
    }

    [Fact]
    public void AddPatternsFromFile_NonExistentFile_DoesNotThrow()
    {
        // Arrange
        var parser = new HasherIgnoreParser();

        // Act
        var act = () => parser.AddPatternsFromFile(@"C:\NonExistent\Path\.hasherignore");

        // Assert
        act.Should().NotThrow();
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
            using (new AssertionScope("patterns read from file"))
            {
                parser.IsIgnored(Path.Combine(tempDir, "app.log")).Should().BeTrue();
                parser.IsIgnored(Path.Combine(tempDir, "cache.tmp")).Should().BeTrue();
                parser.IsIgnored(Path.Combine(tempDir, "node_modules", "pkg.json")).Should().BeTrue();
                parser.IsIgnored(Path.Combine(tempDir, "important.log")).Should().BeFalse();
                parser.IsIgnored(Path.Combine(tempDir, "app.js")).Should().BeFalse();
            }
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
        using (new AssertionScope("dist/*.js pattern"))
        {
            parser.IsIgnored(@"C:\TestFolder\dist\bundle.js").Should().BeTrue();
            parser.IsIgnored(@"C:\TestFolder\dist\styles.css").Should().BeFalse();
            parser.IsIgnored(@"C:\TestFolder\src\app.js").Should().BeFalse();
        }
    }
}
