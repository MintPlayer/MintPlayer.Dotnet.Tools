# FolderHasher PRD
## Description
The FolderHasher is a .NET library that computes the hash of a folder and its contents.

## Ignoring files/folders
The tool should have the ability to process `.hasherignore` files, similarily to how `.gitignore` works.
The `.hasherignore` files should not be included when computing the folder-hash.

## Usage
### c#
We need to provide a project (class library name: MintPlayer.FolderHasher) that contains the core code
for computing the hash of a folder, taking `.hasherignore` files into account.

### MSBuild
We need to provide a project (class library name: MintPlayer.FolderHasher.MSBuild) which we can use in other `.csproj` files anywhere, as an MSBuild target.
This would allow us to run a specific command only if the contents of a folder in the project have changed.
The Microsoft.MSBuild.

With the following code in this repository:

```xml
<PackageReference Include="Microsoft.Build.Utilities.Core" />
```

Then we can write a Task implementation that fulfills our requirements:

```cs
using Microsoft.Build.Utilities;
using Microsoft.Build.Framework;

public class ComputeFolderHashTask : Task
{
    [Required]
    public string FolderPath { get; set; } = "";

    public override bool Execute()
    {
        Log.LogMessage($"Computing the hash for {FolderPath}");
        // Add your code here
        return true;
    }
}
```

After publishing this library we can use the `ComputeFolderHashTask` in other `.csproj` files:

```xml
<PackageReference Include="MintPlayer.FolderHasher.MSBuild" />
```

Then we can use the `ComputeFolderHashTask` in a Target:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MintPlayer.FolderHasher.MSBuild" Version="1.0.0" GeneratePathProperty="true" />
  </ItemGroup>

  <!-- Import the task from the package -->
  <UsingTask TaskName="ComputeFolderHashTask"
             AssemblyFile="$(PkgMintPlayer_FolderHasher_MSBuild)\lib\net10.0\MintPlayer.FolderHasher.MSBuild.dll" />

  <!-- Define a target that computes the folder hash -->
  <Target Name="ComputeAssetsFolderHash" BeforeTargets="Build">
    <ComputeFolderHashTask FolderPath="$(MSBuildProjectDirectory)\Assets">
      <Output TaskParameter="Hash" PropertyName="AssetsFolderHash" />
    </ComputeFolderHashTask>
    <Message Text="Assets folder hash: $(AssetsFolderHash)" Importance="high" />
  </Target>

  <!-- Example: Only run a command if the folder contents changed -->
  <Target Name="ProcessAssetsIfChanged"
          AfterTargets="ComputeAssetsFolderHash"
          Inputs="$(AssetsFolderHash)"
          Outputs="$(IntermediateOutputPath)assets.hash">

    <!-- Your custom processing here -->
    <Exec Command="echo Processing assets..." />

    <!-- Store the hash to track changes -->
    <WriteLinesToFile File="$(IntermediateOutputPath)assets.hash"
                      Lines="$(AssetsFolderHash)"
                      Overwrite="true" />
  </Target>

</Project>
```

The `ComputeFolderHashTask` exposes the following properties:

| Property | Direction | Description |
|----------|-----------|-------------|
| `FolderPath` | Input (Required) | The path to the folder to hash |
| `Hash` | Output | The computed hash of the folder contents |

This allows you to conditionally run expensive build steps only when the folder contents have actually changed.