"Verz" was supposed to be a dotnet tool which someone can run from a directory. 
  This directory can for example contain a .NET Solution, or NX workspace, or Maven workspace, ...

  Tool configuration
  ==================

  The tool should search for a verz.json file in the CWD, for example like this

  ```json
  {
      "Registries": [{
          "id": "nuget.org",
          "url": "https://api.nuget.org/v3/index.json",
      }, {
          "id": "MintPlayer github",
          "url": "https://nuget.pkg.github.com/mintplayer"
      }],
      "Plugins": [
          "MintPlayer.Verz.Dotnet",
          "MintPlayer.Verz.NodeJS",

          "MintPlayer.Verz.NugetOrg",
          "MintPlayer.Verz.GithubPackageRegistry",
          "MintPlayer.Verz.NpmJS"
      ]
  }
  ```

  Then the tool should use `Nuget.Protocol` to download these packages,
  dynamically load the assemblies and scan them for classes which implement `IPackageRegistry` or `IDevelopmentSdk`.
  Then in order:

  IDevelopmentSdk
  ---------------
  Next each `IDevelopmentSdk` implementation needs to scan the CWD for candidate folders/projects.
  Next it needs to read the git-tags for the current commit and extract the tag that matches the specific project.
  Next the tool should explicitly apply this tag as version to the project
  - For .net => edit csproj file
  - For nodjes => `npm version set ...` (or whatever the command is)
  - ...

  IPackageRegistry
  ----------------
  When all package versions in the repository have been explicitly set (eg during CI/CD)
  we should be able to publish the package to all known registries.

  Key principles
  ==============
  - I was first considering:
      - fetching the latest version for all libraries inside the cwd, from all registries
      - determining if the public-api changed (.d.ts composite hash or nuget public-api hash)
      - If changed => bump minor /// else => bump patch
      - This is probably too complex, and you cannot know what commit serves what package versions
  - Better approach I think:
      - Apply a git-tag
      - Let github actions react to the git-tag-push event
          - Github actions clones the code
          - let this tool run to extract the git-tags from the current commit, and apply them to the libraries in this repository
          - publish all packages to all package-registries from the verz.json
      - How to know the next git-tag?
          - Also use github actions to apply a git-tag after a pull-request was merged
          - Download the packages from the previous commit (using the IPackageRegistry implementation)
          - Determine the major version for the previous commit (eg .NET 9 - angular 20 - ...)
          - Determine the major version for the tag's commit (eg .NET 10 - angular 21 - ...)
          - If different => Bump major => change the rest to 0
          - Else
              - Read the nuspec, or package's produced package.json file (can we add custom fields here, like publicApiHash?) and extract the previous commit's publicApiHash
              - Compute the publicApiHash of the current library code
              - If different => Bump minor
              - Else => Bump patch

  For .NET libraries we can look for the TargetFramework(s) => Take the highest framework number (NET9.0 - NET10.0 => 10 => Major = 10)
  For NodeJS libraries we should be able to find what framework it targets
  - Angular => run along with the angular major version
  - React => run along with the react major version
  - Vue => run along with the vue major version
  - ...
  - None => Just increment the patch by 1 /// Or update the minor version when the PublicApiHash has changed

  Goals
  =====
  - Users should be able to use the tool with any sdk (hence all IDevelopmentSdk implementations)
  - Since we don't need to read the existing versions from the registries, the IPackageRegistry interface could actually be removed
  - Git-tags tag the version, there will probably be multiple tags per-commit. One for each package
  - Tool should be compatible with .NET Solutions, nx workspaces, npm workspaces, yarn workspaces, ...

  Can you launch a team to analyze what the end-goal is, and then create a PRD + plan?
  The code that currently lives in the Verz folder can be completely removed/ignored.

  Considerations
  ==============
  What if for some libraries, no code has changed at all? We don't need to update the version then? For .NET (not for nodejs) what if library A was changed, while a dependent library B (that has library A as packageReference) has no changes at all, should that package version also be updated? Should we build a dependency graph, similar to what NX does?
  Would it be better not to create our own "Verz" tool to determine + apply package versions, but instead rely on NX + nx/dotnet ?
  Note: I won't be combining NX + Verz together. It's either the one or the other. But I feel like a custom tool would allow more flexibility in auto versioning

  Additional notes
  ================
  - Verz\MintPlayer.Verz should also get a readme.md with instructions on how to set this up on their repository
  - You can also mention "dnx" in the readme
  - Let's call the plugins "MintPlayer.Verz.Sdks.Dotnet" and "MintPlayer.Verz.Registries.NugetOrg"
  - The verz tool should also work immediately (on first merge of this pull-request) on MintPlayer.Verz itself, and all other library projects in this git-repository
