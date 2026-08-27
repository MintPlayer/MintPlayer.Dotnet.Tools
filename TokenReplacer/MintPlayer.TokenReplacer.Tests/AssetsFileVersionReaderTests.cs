using MintPlayer.Assertions;
using MintPlayer.TokenReplacer.Targets;

namespace MintPlayer.TokenReplacer.Tests;

public class AssetsFileVersionReaderTests
{
    private const string SampleAssetsJson = """
        {
          "version": 3,
          "targets": {
            "net10.0": {
              "Newtonsoft.Json/13.0.3": {
                "type": "package",
                "compile": { "lib/net6.0/Newtonsoft.Json.dll": {} }
              }
            }
          },
          "libraries": {
            "Newtonsoft.Json/13.0.3": {
              "sha512": "HrC5BXdl00IP9zeV+0Z848QWPAoCr9P3bDEZguI+gkLcBKAOxix/tLEAAHC+UvDNPv4a2d18lOReHMOagPa+zQ==",
              "type": "package",
              "path": "newtonsoft.json/13.0.3",
              "files": [ ".nupkg.metadata", "lib/net6.0/Newtonsoft.Json.dll" ]
            },
            "Microsoft.Extensions.Logging.Abstractions/9.0.0": {
              "sha512": "xyz==",
              "type": "package",
              "path": "microsoft.extensions.logging.abstractions/9.0.0",
              "files": []
            },
            "My.Project/1.0.0": {
              "type": "project",
              "path": "../My.Project/My.Project.csproj",
              "msbuildProject": "../My.Project/My.Project.csproj"
            }
          },
          "projectFileDependencyGroups": {
            "net10.0": [ "Newtonsoft.Json >= 13.0.3" ]
          },
          "project": {
            "restore": { "projectName": "Fixture" },
            "frameworks": { "net10.0": { "dependencies": { "Newtonsoft.Json": { "target": "Package", "version": "[13.0.3, )" } } } }
          }
        }
        """;

    [Fact]
    public void Reads_Direct_Package_Version()
    {
        var versions = AssetsFileVersionReader.ReadLibraryVersions(SampleAssetsJson);

        versions.Should().Contain("Newtonsoft.Json", "13.0.3");
    }

    [Fact]
    public void Reads_Transitive_Package_Version()
    {
        var versions = AssetsFileVersionReader.ReadLibraryVersions(SampleAssetsJson);

        versions.Should().Contain("Microsoft.Extensions.Logging.Abstractions", "9.0.0");
    }

    [Fact]
    public void Package_Id_Lookup_Is_Case_Insensitive()
    {
        var versions = AssetsFileVersionReader.ReadLibraryVersions(SampleAssetsJson);

        // Deliberately the indexer: this test is about the map's own case-insensitive comparer,
        // which the dictionary assertions do not honour (they use EqualityComparer<TKey>.Default).
        versions["newtonsoft.json"].Should().Be("13.0.3");
    }

    [Fact]
    public void Missing_Package_Is_Not_Present()
    {
        var versions = AssetsFileVersionReader.ReadLibraryVersions(SampleAssetsJson);

        versions.Should().NotContainKey("Absent.Package");
    }

    [Fact]
    public void Does_Not_Pick_Up_Nested_Libraries_Objects()
    {
        const string json = """
            {
              "project": { "libraries": { "Fake.Package/9.9.9": {} } },
              "libraries": { "Real.Package/1.0.0": { "type": "package" } }
            }
            """;
        var versions = AssetsFileVersionReader.ReadLibraryVersions(json);

        versions.Should().Contain("Real.Package", "1.0.0").And.NotContainKey("Fake.Package");
    }

    [Fact]
    public void Handles_Escaped_Strings_In_Values()
    {
        const string json = """
            {
              "comment": "quoted \" brace } bracket ] and A",
              "libraries": { "Pkg/2.0.0": { "path": "with \"escape\"" } }
            }
            """;
        var versions = AssetsFileVersionReader.ReadLibraryVersions(json);

        versions.Should().Contain("Pkg", "2.0.0");
    }

    [Fact]
    public void Malformed_Json_Throws_FormatException()
    {
        var truncated = () => AssetsFileVersionReader.ReadLibraryVersions("{ \"libraries\": ");
        var garbage = () => AssetsFileVersionReader.ReadLibraryVersions("not json");

        truncated.Should().Throw<FormatException>();
        garbage.Should().Throw<FormatException>();
    }

    [Fact]
    public void Empty_Object_Returns_Empty_Map()
    {
        var versions = AssetsFileVersionReader.ReadLibraryVersions("{}");

        versions.Should().BeEmpty();
    }
}
