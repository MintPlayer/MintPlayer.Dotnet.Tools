# MintPlayer.SlnLaunch

Run a Visual Studio **multi-project launch profile** (`.slnLaunch`) from the command line — on any OS, in any editor. Visual Studio 2022 (17.11+) can start several projects at once with F5; the `dotnet` CLI cannot. `slnlaunch` reads the same `.slnLaunch` file and launches every project in a profile concurrently, then tears the whole group down cleanly on Ctrl+C.

## Install

```bash
dotnet tool install --global MintPlayer.SlnLaunch
```

Or run it **without installing** using `dnx` (.NET 10+), which fetches and executes the tool on demand:

```bash
dnx MintPlayer.SlnLaunch --list
dnx MintPlayer.SlnLaunch -- --tenant acme        # arguments after -- are forwarded as usual
```

(You can also install it per-repo as a local tool: `dotnet tool install MintPlayer.SlnLaunch` inside a project with a tool manifest, then `dotnet slnlaunch`.)

## Usage

```bash
slnlaunch [<file>] [options]
```

With no `<file>`, the single `.slnLaunch` in the current directory is used (then `.slnLaunch.user`, then `.slnxLaunch`).

```bash
slnlaunch                       # run the only profile in the discovered file
slnlaunch --list                # show profiles and their projects
slnlaunch --profile "HR + Fleet"   # pick a profile when there are several
slnlaunch App.slnLaunch --watch    # hot-reload via `dotnet watch`
slnlaunch --dry-run                # print the dotnet commands without running them
```

Each launched project becomes `dotnet run --project <path> --launch-profile <DebugTarget>`, so the selected `launchSettings.json` profile's environment variables, `applicationUrl`, and `commandLineArgs` apply exactly as in Visual Studio.

### Options

| Option | Description |
|--------|-------------|
| `<file>` | Path to a `.slnLaunch` file (auto-discovered if omitted). |
| `--profile`, `-p` | Profile to launch (required when the file has more than one). |
| `--list`, `-l` | List the profiles and exit. |
| `--watch` | Use `dotnet watch` instead of `dotnet run`. |
| `--configuration`, `-c` | Build configuration, forwarded to every project. |
| `--framework`, `-f` | Target framework, forwarded to every project. |
| `--no-build` | Forwarded to every project. |
| `--verbosity`, `-v` | Verbosity, forwarded to every project. |
| `--no-prefix` | Don't prefix child output with the project label. |
| `--kill-on-fail` | Tear down all projects if any one exits non-zero. |
| `--dry-run` | Print the `dotnet` commands that would run, then exit. |

## Argument forwarding

Two layers, on top of what the launch profile already provides:

**Shared build options** (`-c`, `-f`, `--no-build`, `-v`) are forwarded to *every* project — e.g. run the whole composition in Release:

```bash
slnlaunch -c Release
```

**Per-project app arguments.** Add a `ForwardArguments` array to a project entry naming the arguments it wants. Everything after a standalone `--` on the command line becomes a pool; each project receives only the names it opted into, as app arguments:

```jsonc
[
  {
    "Name": "HR + Fleet",
    "Projects": [
      { "Path": "Demo\\HR\\HR\\HR.csproj",       "Action": "Start", "DebugTarget": "https", "ForwardArguments": ["tenant", "region"] },
      { "Path": "Demo\\Fleet\\Fleet\\Fleet.csproj", "Action": "Start", "DebugTarget": "https", "ForwardArguments": ["port"] }
    ]
  }
]
```

```bash
slnlaunch -c Release -- --tenant acme --region eu --port 5005
```

HR's app receives `--tenant acme --region eu`; Fleet's app receives `--port 5005`. (`ForwardArguments` is a MintPlayer extension; Visual Studio ignores unknown fields.)

> Tip: if a forwarded value itself begins with `-` (e.g. a negative number), use the `--name=value` form so it isn't mistaken for a flag.

## Behavior notes

- **`Action`**: `Start` and `StartWithoutDebugging` both launch the project (there's no debugger to attach from the CLI). `None`, or a project omitted from the array, is skipped.
- **Non-Project launch profiles**: if a `DebugTarget` names a profile the CLI can't honor (IIS Express, Docker, …), `slnlaunch` warns and runs the project without `--launch-profile`. Docker Compose (`.dcproj`) projects are skipped.
- **Clean shutdown**: Ctrl+C (or SIGTERM) stops every project — including the app each `dotnet run` spawns — by killing the whole process tree on Windows, Linux, and macOS. No orphaned processes.
- **Exit code**: `0` when every project exits successfully or you cancel; otherwise the first non-zero exit code.

## License

Apache-2.0
