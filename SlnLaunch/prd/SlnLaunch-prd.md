# SlnLaunch PRD

## Overview

`MintPlayer.SlnLaunch` is a .NET global/local tool (`dotnet tool install MintPlayer.SlnLaunch`, command `slnlaunch`) that runs a Visual Studio **multi-project launch profile** (`.slnLaunch` file) from the command line — something Visual Studio 2022 17.11+ supports but the `dotnet` CLI does not.

It reads the `.slnLaunch` JSON, resolves the named profile, and launches every project in that profile **concurrently** as a managed process group — each as `dotnet run --project <Path> --launch-profile <DebugTarget>` — multiplexing their output and tearing the whole group down cleanly on Ctrl+C across Windows, Linux, and macOS.

### Origin / Inspiration

Visual Studio's "Configure Startup Projects → Multiple startup projects" feature (VS 2022 17.11+, preview) persists multi-project launch profiles to a `<Solution>.slnLaunch` file next to the `.sln`. Pressing F5 starts all listed projects at once, each with its chosen launch profile. This works **only inside Visual Studio** — `dotnet run` operates on one project at a time and has no concept of a solution-level launch profile (the SDK team declined to own these files: dotnet/sdk#48014, closed *not planned*). Developers on the CLI, on macOS/Linux, or in lightweight editors have no equivalent. This tool fills that gap.

Reference example (`MintPlayer.Spark.slnLaunch`):

```json
[
  {
    "Name": "HR + Fleet",
    "Projects": [
      { "Path": "Demo\\Fleet\\Fleet\\Fleet.csproj", "Action": "Start", "DebugTarget": "https" },
      { "Path": "Demo\\HR\\HR\\HR.csproj", "Action": "Start", "DebugTarget": "https" }
    ]
  }
]
```

Running `slnlaunch` in that directory starts both `Fleet` and `HR` under their `https` launch profiles simultaneously, exactly as VS would.

### Primary Use Cases

1. **CLI / cross-platform multi-project launch:** A developer on macOS/Linux, in VS Code/Rider, or in a terminal-only workflow runs the same multi-project profile their team configured in Visual Studio — no manual juggling of N terminals each running `dotnet run --launch-profile`.
2. **Reproducible local "run the whole app" command:** A single committed `.slnLaunch` becomes a one-command "spin up the full system" (e.g. an API + a worker + a front-end host), runnable in scripts, demos, and onboarding.
3. **CI / smoke-launch:** Bring the composed set of projects up together (e.g. for an integration smoke test) and tear them all down deterministically.

---

## Behavior Summary

| Step | Behavior |
|------|----------|
| **Discover** | If no path argument is given, find a single `.slnLaunch` (then `.slnLaunch.user`, `.slnxLaunch`) in the current directory. Zero → error with guidance; multiple → error listing them, asking for an explicit path. |
| **Select profile** | A `.slnLaunch` is an array of named profiles. One profile → use it. Multiple → require `--profile <Name>` (otherwise error listing available names). `--list` prints profiles + their projects and exits. |
| **Resolve projects** | For each project entry: resolve `Path` (solution-relative, `\\`-escaped, slash-normalized) against the `.slnLaunch` file's own directory. Skip entries with `Action: None` or absent action. |
| **Map to command** | `dotnet run --project <resolvedPath> --launch-profile "<DebugTarget>"` (omit `--launch-profile` when `DebugTarget` is absent). `--watch` swaps `run` → `watch`. Shared build options (`-c`/`-f`/`--no-build`/`-v`) and per-project forwarded app args (see **Argument Forwarding**) are appended. |
| **Launch** | Start all resolved projects concurrently as child processes; prefix each project's stdout/stderr with a short label so interleaved output stays readable. |
| **Teardown** | On Ctrl+C / SIGINT / SIGTERM, or when the tool is otherwise cancelled, kill **every** child process tree (cross-platform) and wait for exit before returning. |
| **Exit code** | `0` only if every launched project exited `0` (or was cleanly cancelled by the user). Non-zero if any project failed to start or exited non-zero. |

---

## Requirements

### `.slnLaunch` Parsing

| Requirement | Description | Status |
|-------------|-------------|--------|
| **Schema** | Parse the JSON array of `{ Name, Projects[] }`; each project `{ Path (required), Action (required), DebugTarget? (optional), ForwardArguments? (optional string[]) }`. Case-insensitive property matching; tolerate trailing commas / comments leniently. `ForwardArguments` is a MintPlayer extension to the VS schema (VS ignores unknown fields). | ✅ |
| **`.user` + `.slnx` variants** | Also recognize `<Solution>.slnLaunch.user` and `.slnxLaunch` (identical schema). | ✅ |
| **Path resolution** | `Path` is relative to the `.slnLaunch` file's directory; backslashes in JSON are literal separators — normalize to the host separator so Windows-authored files run on Linux/macOS. | ✅ |
| **Action mapping** | `Start` and `StartWithoutDebugging` → launch (the debugger distinction is moot for a CLI runner). `None` or absent → skip the project. Projects can also simply be omitted from the array (already skipped). | ✅ |
| **Validation** | Missing file, malformed JSON, empty `Projects`, a `Path` that doesn't exist on disk → clear actionable errors, not stack traces. | ✅ |

### Profile / Launch-Target Resolution

| Requirement | Description | Status |
|-------------|-------------|--------|
| **DebugTarget pass-through** | Pass `DebugTarget` verbatim as `--launch-profile "<value>"`; quote values containing spaces (e.g. `"IIS Express"`). Let the dotnet CLI apply the profile's `applicationUrl` / `environmentVariables` / `commandLineArgs` — do **not** re-implement launchSettings translation. | ✅ |
| **Absent DebugTarget** | Omit `--launch-profile`; the CLI selects the first `commandName: Project` profile. | ✅ |
| **Non-Project profiles** | When `DebugTarget` names a profile the CLI can't honor (`IIS Express`, `Docker`, `Executable`, etc.) — detected by reading the project's `Properties/launchSettings.json` — emit a **warning** and fall back to running the project **without** `--launch-profile` (CLI picks the first `Project` profile). `.dcproj` entries are skipped with a warning (Docker Compose is out of scope). | ✅ |
| **`--project` not `-p`** | Always use the long `--project` form (`-p` now maps to `--property` when the arg contains `=`). | ✅ |

### Argument Forwarding

`.slnLaunch` itself carries no build configuration or app arguments — the dotnet CLI already applies the selected launch profile's `environmentVariables`, `applicationUrl`, and `commandLineArgs`. On top of that, the tool forwards arguments from its own command line in two ways:

| Requirement | Description | Status |
|-------------|-------------|--------|
| **Shared build options** | `--configuration`/`-c`, `--framework`/`-f`, `--no-build`, `--verbosity`/`-v` are appended (as run options, before any `--`) to **every** project's `dotnet run`/`watch`. Lets one command run the whole composition in e.g. Release. | ✅ |
| **Per-project arg selection** | Everything after a standalone `--` on the `slnlaunch` command line is a *pool* of named arguments. Each project receives (as **app** args, after its own `--`) only the names it opted into via its `ForwardArguments` field — so one invocation can feed different args to different apps. | ✅ |
| **Token-form preservation** | The pool preserves original token form: flags (`--verbose`), `--name value`, `--name=value`, and repeated occurrences. Names are matched ignoring leading dashes. | ✅ |
| **argv split** | The first standalone `--` is split off in `Program.cs`; the left side is parsed by the CLI, the right side becomes the forwardable pool (works around the source generator's lack of trailing-token capture). | ❌ (Phase 4) |

Example: with `ForwardArguments` on each project —

```json
[
  {
    "Name": "HR + Fleet",
    "Projects": [
      { "Path": "Demo\\HR\\HR\\HR.csproj", "Action": "Start", "DebugTarget": "https", "ForwardArguments": ["tenant", "region"] },
      { "Path": "Demo\\Fleet\\Fleet\\Fleet.csproj", "Action": "Start", "DebugTarget": "https", "ForwardArguments": ["port"] }
    ]
  }
]
```

`slnlaunch -c Release -- --tenant acme --region eu --port 5005` launches both projects in Release; HR's app gets `--tenant acme --region eu`, Fleet's app gets `--port 5005`.

### Process Group Management

| Requirement | Description | Status |
|-------------|-------------|--------|
| **Concurrent launch** | Start all resolved projects at once (not sequentially). Honor `.slnLaunch` ordering only for log readability, not as a startup barrier. | ✅ |
| **Output multiplexing** | Prefix each child's stdout/stderr lines with a per-project label (e.g. `[Fleet] ...`), optionally colorized, so concurrent output is attributable. A `--no-prefix`/raw mode is available. | ✅ |
| **Cross-platform process-tree kill on cancel** | **Critical.** `dotnet run` is a *runner* that spawns the app (`<App>.exe`/`<App>` dll) as a child; killing only the runner leaves the app alive. On cancellation, kill the entire tree of every child via `Process.Kill(entireProcessTree: true)` on Windows, Linux, **and** macOS. Wait for exit (bounded) before returning. | ✅ |
| **Signal handling** | Handle Ctrl+C (`Console.CancelKeyPress`) and POSIX `SIGINT`/`SIGTERM` (`PosixSignalRegistration`); trigger a single shared `CancellationTokenSource` that teardown observes. First signal → graceful teardown; second signal → force-kill immediately. | ❌ (Phase 4) |
| **Fail-fast option** | `--kill-on-fail` (opt-in): if any project exits non-zero, tear down the rest. Default: let the others keep running (VS-like). | ✅ |
| **Exit-code aggregation** | Return non-zero if any project failed to start or exited non-zero; `0` if all succeeded or the user cleanly cancelled. | ✅ |

### CLI Surface

| Requirement | Description | Status |
|-------------|-------------|--------|
| **Root command** | `slnlaunch [<file>] [options]` — `<file>` optional (auto-discovered). | ❌ |
| **`--profile <Name>` / `-p`** | Select a named profile when the file has more than one. | ❌ |
| **`--list` / `-l`** | List profiles and their projects, then exit `0`. | ❌ |
| **`--watch`** | Use `dotnet watch` instead of `dotnet run` per project (hot reload; also honors `launchBrowser`). Default: `dotnet run`. | ❌ |
| **`--configuration`/`-c`** | Forwarded to every project's `dotnet run`/`watch`. | ❌ |
| **`--framework`/`-f`** | Forwarded to every project. | ❌ |
| **`--no-build`** | Forwarded to every project. | ❌ |
| **`--verbosity`/`-v`** | Forwarded to every project's `dotnet run`/`watch` build. | ❌ |
| **`-- <args>`** | Everything after a standalone `--` is the forwardable pool; projects opt in by name via `ForwardArguments` (see **Argument Forwarding**). | ❌ |
| **`--no-prefix`** | Disable per-project log prefixing (raw passthrough). | ❌ |
| **`--kill-on-fail`** | Tear down the group if any project exits non-zero. | ❌ |
| **`--dry-run`** | Print the exact `dotnet` commands that would run (resolved paths, profiles, fallbacks/warnings) without launching. | ❌ |
| **Help/usage** | `--help` documents all of the above with examples. | ❌ |

### Packaging & Distribution

| Requirement | Description | Status |
|-------------|-------------|--------|
| **`dotnet tool`** | `PackAsTool=true`, `ToolCommandName=slnlaunch`, `PackageId=MintPlayer.SlnLaunch`, `OutputType=Exe`, `net10.0`. | ❌ |
| **Repo metadata** | Authors/Company/RepositoryUrl/`PackageLicenseExpression=Apache-2.0`/`IncludeSymbols`+`snupkg`, matching `Solve`/`Verz`. | ❌ |
| **CI publish** | Picked up automatically by `.github/workflows/dotnet-build-master.yml` (build → test → pack → push to nuget.org + GitHub Packages) on merge to `master`. | ❌ |

### Testing

| Requirement | Description | Status |
|-------------|-------------|--------|
| **Unit tests (xUnit)** | `.slnLaunch` parsing (schema, `.user`/`.slnx` variants, lenient JSON), path resolution (slash normalization, relative→absolute), Action mapping, profile selection (single/multiple/`--list`), command construction (`--project`/`--launch-profile`/`--watch`/absent DebugTarget/non-Project fallback), exit-code aggregation. | ❌ |
| **Process-lifecycle tests** | Launch a trivial long-running fixture process (not real ASP.NET — a sleep/echo stub) and assert: cancellation kills the whole tree; second signal force-kills; no orphaned children remain (assert by PID on each OS). | ❌ |
| **Cross-platform** | CI runs on `ubuntu-latest`; the teardown/path tests must pass on Linux. Note Windows-only verification gaps where relevant. | ❌ |

### Documentation

| Requirement | Description | Status |
|-------------|-------------|--------|
| **README** | Install, usage for all options, the `MintPlayer.Spark` example, the non-Project fallback behavior, and the teardown guarantee. Pack via `PackageReadmeFile`. | ❌ |
| **XML docs** | On public command options. | ❌ |

---

## Architecture

### Projects

Mirrors the `Solve` tool's shape (single CLI project + one test project), using the repo's CLI source-generator pattern.

| Project | TFM | Description |
|---------|-----|-------------|
| `MintPlayer.SlnLaunch` | net10.0 | The CLI tool. `Program.cs` (Host + generated `Add*Command`/`Invoke*CommandAsync`), `Commands/` (the `[CliRootCommand]`), `Services/` (parser, project resolver, process orchestrator, console). `PackAsTool`. |
| `MintPlayer.SlnLaunch.Tests` | net10.0 | xUnit unit + process-lifecycle tests. References the tool project. |

### Conventions (match `Solve`/`Verz`)

- **CLI:** `System.CommandLine` + the repo's source generators — `MintPlayer.CliGenerator` (`[CliRootCommand]`, `[CliOption]`, `[CliArgument]`, generated `AddSlnLaunchCommand()` / `InvokeSlnLaunchCommandAsync()`) and `MintPlayer.SourceGenerators` (`[Inject]` constructor injection, `[Register(typeof(IFoo), ServiceLifetime.*, "SlnLaunchServices")]` → generated `AddSlnLaunchServices()`).
- **Host:** `Host.CreateApplicationBuilder(args)` → `AddSlnLaunchCommand().AddSlnLaunchServices()` → `InvokeSlnLaunchCommandAsync(args)`, with the same top-level `ParseCommandException`/`Exception` handling as `Solve/Program.cs`.

### Service decomposition (deep modules)

| Service | Responsibility (interface) | Hidden complexity |
|---------|---------------------------|-------------------|
| `ISlnLaunchFileService` | `Find(dir) → path?`, `Load(path) → SlnLaunchFile` | File discovery precedence, lenient JSON, `.user`/`.slnx` variants, validation errors |
| `ILaunchPlanBuilder` | `Build(profile, dir, options) → LaunchPlan` (list of resolved commands + warnings) | Path resolution, Action filtering, DebugTarget→`--launch-profile`, launchSettings inspection for the non-Project fallback, `dotnet run` vs `watch` |
| `IProcessOrchestrator` | `RunAsync(LaunchPlan, CancellationToken) → int` | Concurrent spawn, output multiplexing, **cross-platform tree kill**, signal registration, exit-code aggregation, fail-fast |
| `IConsoleService` | Structured colored output (cf. `Solve`'s `IConsoleService`) | ANSI/color, prefixing |

The orchestrator is the one genuinely hard module; everything else is straightforward parsing/mapping that feeds it a plan.

### Cross-platform teardown mechanism (the core risk)

`dotnet run`/`dotnet watch` spawn the application as a **child** process. The orchestrator therefore tracks each top-level `dotnet` `Process` and, on cancel, calls `process.Kill(entireProcessTree: true)` — which .NET implements on Windows (Job/`taskkill`-equivalent), Linux, and macOS (process-group walk). Signals are captured with:

- `Console.CancelKeyPress` (Ctrl+C, all platforms) — set `e.Cancel = true`, trigger the shared CTS, let teardown run.
- `PosixSignalRegistration.Create(PosixSignal.SIGINT/SIGTERM, …)` (Linux/macOS) for non-TTY termination (e.g. `kill`, container stop).

Teardown: trigger CTS → for each child, `Kill(entireProcessTree: true)` → `await WaitForExitAsync` with a bounded grace timeout → on second signal, skip waiting and force-kill. After the loop, assert no tracked PID is still alive.

---

## Non-Goals

- **Attaching a debugger.** `Start` vs `StartWithoutDebugging` both become a plain process launch; this is a runner, not a debugger host. (Could be revisited via `vsdbg`/`netcoredbg`, but out of scope.)
- **Re-implementing `launchSettings.json` semantics.** The tool defers `applicationUrl`/`environmentVariables`/`commandLineArgs`/`launchBrowser` handling to the `dotnet` CLI by passing `--launch-profile`.
- **Docker Compose / `.dcproj` orchestration.** Skipped with a warning; `docker compose` is its own tool.
- **IIS / IIS Express hosting.** Not a CLI-launchable host; falls back to a default `Project` profile (with warning).
- **Authoring/editing `.slnLaunch` files.** Read-only consumer; VS (or a future `--init`) writes them.
- **Replacing .NET Aspire / Tye.** Those are orchestration frameworks with their own config; this tool's value is faithfully running the *existing VS* `.slnLaunch` artifact.

## Open Questions

| Question | Recommendation |
|----------|----------------|
| Tool command name: `slnlaunch` vs `slnrun` vs `launch`? | **`slnlaunch`** — matches the file extension, unambiguous (decided). |
| `dotnet watch` in v1? | **Yes, as opt-in `--watch`;** default `dotnet run` (decided). |
| Non-`Project` `DebugTarget` (IIS Express/Docker)? | **Warn + run without `--launch-profile`;** skip `.dcproj` with a warning (decided). |
| Auto-pick profile when multiple exist, or always require `--profile`? | Require `--profile` when >1 (explicit > magic); single profile runs with no flag. |
| Color/prefix output by default? | Yes (prefixed + colored), with `--no-prefix` and respect for `NO_COLOR`. |
| Default on a project's non-zero exit: keep others running or fail-fast? | Keep others running (VS-like); fail-fast behind `--kill-on-fail`. |
| Health/readiness ordering (start B only after A is listening)? | Out of scope for v1 — `.slnLaunch` has no ordering/health concept; revisit only if requested. |
