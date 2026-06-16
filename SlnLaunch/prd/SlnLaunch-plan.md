# SlnLaunch Implementation Plan

Companion to [SlnLaunch-prd.md](SlnLaunch-prd.md). Phases are ordered so each ends in a buildable, testable state; CI (`dotnet build` → `dotnet test` → `dotnet pack`, see `.github/workflows/dotnet-build-master.yml`) stays green after every phase.

Structure: **one CLI project** (`MintPlayer.SlnLaunch`) + **one test project** (`MintPlayer.SlnLaunch.Tests`) — same shape as `Solve`, using the repo's CLI source generators.

## Phase 1 — Project scaffold + parsing (no launching) ⏳ PENDING

1. Create `SlnLaunch/MintPlayer.SlnLaunch/MintPlayer.SlnLaunch.csproj` (`net10.0`, `Exe`, `ImplicitUsings`/`Nullable` enable, `LangVersion 14`), copying the package/tool metadata block from `Solve/Solve.csproj`:
   - `PackAsTool=true`, `ToolCommandName=slnlaunch`, `PackageId=MintPlayer.SlnLaunch`, `Version=10.0.0`, `IsPackable=true`, Apache-2.0, Authors/Company/Repository, `IncludeSymbols`/`snupkg`.
   - `PackageReference`: `System.CommandLine` (match `Verz`'s 2.0.5), `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`.
   - `ProjectReference` (analyzers): `MintPlayer.SourceGenerators` + `.Attributes`, `MintPlayer.CliGenerator` + `.Attributes` — copy the four `<ProjectReference>` lines verbatim from `Solve.csproj`.
2. Models (`Models/`): `SlnLaunchFile` (list of `LaunchProfile`), `LaunchProfile` (`Name`, `Projects`), `LaunchProjectEntry` (`Path`, `Action`, `DebugTarget?`), `LaunchAction` enum (`Start`, `StartWithoutDebugging`, `None`). Use `System.Text.Json` with case-insensitive options + `JsonStringEnumConverter`.
3. `ISlnLaunchFileService` / `SlnLaunchFileService` (`[Register(..., "SlnLaunchServices")]`):
   - `Find(directory)`: precedence `*.slnLaunch` → `*.slnLaunch.user` → `*.slnxLaunch`; return single match, or signal "none"/"multiple".
   - `Load(path)`: deserialize, validate (non-empty profiles, each project has `Path`+`Action`), throw a typed `SlnLaunchException` with a clear message on malformed input.
4. `MintPlayer.SlnLaunch.Tests` (xUnit, mirror `FolderHasher.Tests` package set: `Microsoft.NET.Test.Sdk` 18.3.0, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.5, `coverlet.collector`; `<Using Include="Xunit" />`). Tests: parse the `MintPlayer.Spark` sample, `.user`/`.slnx` variants, enum mapping, malformed-JSON error, discovery precedence (none/one/many).
5. Add both projects to `MintPlayer.Dotnet.Tools.sln` under a `SlnLaunch` solution folder.

**Done when:** `dotnet test` green; parsing fully covered; nothing launches yet.

## Phase 2 — Launch plan builder (path + profile resolution) ⏳ PENDING

6. `Models/LaunchCommand` (resolved: working dir, exe `dotnet`, arg list, project label, warnings) and `LaunchPlan` (commands + plan-level warnings).
7. `ILaunchPlanBuilder` / `LaunchPlanBuilder`:
   - Resolve each `Path` against the `.slnLaunch` directory; normalize `\`/`/` to `Path.DirectorySeparatorChar`; verify the file exists (error if not).
   - Filter out `Action: None`/absent; map `Start`/`StartWithoutDebugging` → run.
   - Build args: `run --project <abs> --launch-profile "<DebugTarget>"` (omit `--launch-profile` if absent); `--watch` mode → `watch --project <abs> --launch-profile ...`.
   - **Non-Project fallback:** read `<projectDir>/Properties/launchSettings.json`; if `DebugTarget` resolves to a profile whose `commandName != "Project"` (or `.dcproj`), drop `--launch-profile` and record a warning (`.dcproj` → skip + warning).
   - `--label` derives from the project file name (e.g. `Fleet`).
8. Tests: command construction for every branch (with/without DebugTarget, watch, spaces in profile name quoted, non-Project fallback via a fixture `launchSettings.json`, `.dcproj` skip), path normalization on a Windows-authored sample, missing-project error.

**Done when:** given a parsed file the builder emits a correct, fully-tested `LaunchPlan`; still nothing spawns.

## Phase 3 — Process orchestration + cross-platform teardown ⏳ PENDING

9. `IProcessOrchestrator` / `ProcessOrchestrator` — `Task<int> RunAsync(LaunchPlan, CancellationToken)`:
   - Spawn each `LaunchCommand` via `Process` (`RedirectStandardOutput/Error`, `UseShellExecute=false`); attach `OutputDataReceived`/`ErrorDataReceived` → `IConsoleService` with per-project prefix/color (`--no-prefix` raw mode; respect `NO_COLOR`).
   - Track all `Process` handles. Await all `WaitForExitAsync` linked to the token.
   - **Teardown (the critical path):** on cancellation, for each tracked process call `Kill(entireProcessTree: true)` (kills the `dotnet run` runner **and** the spawned app — Windows/Linux/macOS), then bounded `WaitForExitAsync(graceTimeout)`. Assert no tracked PID remains.
   - `--kill-on-fail`: first non-zero exit triggers the same teardown for the rest.
   - Aggregate exit code: non-zero if any failed-to-start/exited non-zero; `0` if all `0` or user-cancelled.
10. Signal wiring (in the command, around the orchestrator call): one shared `CancellationTokenSource`; `Console.CancelKeyPress` (`e.Cancel = true` → cancel) and `PosixSignalRegistration` for `SIGINT`/`SIGTERM`. Second signal → escalate to force-kill (skip grace wait).
11. Tests with a **fixture stub process** (a tiny console app or `dotnet exec` of a sleep loop that itself spawns a grandchild) — **not** real ASP.NET:
    - cancel → entire tree dead (assert grandchild PID gone) on the CI OS;
    - second-signal force-kill path;
    - exit-code aggregation (all-zero, one-failure, `--kill-on-fail`);
    - output is prefixed per project.

**Done when:** `slnlaunch` launches a multi-process plan, multiplexes output, and leaves **zero** orphans after Ctrl+C — verified by test on Linux (CI) and manually on Windows.

## Phase 4 — CLI command wiring ⏳ PENDING

12. `Commands/SlnLaunchCommand.cs` — `[CliRootCommand(Name = "slnlaunch", Description = "Run a Visual Studio .slnLaunch multi-project launch profile from the CLI")]`, `[Inject]` the services, options/argument per the PRD CLI surface: `[CliArgument(0, "file", Required=false)]`, `--profile`/`-p`, `--list`/`-l`, `--watch`, `--no-prefix`, `--kill-on-fail`, `--dry-run`, `--verbosity`. `Execute(CancellationToken)`:
    - discover/load file → select profile (single auto; multiple require `--profile`, else error with names) → `--list` prints and returns 0 → build plan (print warnings) → `--dry-run` prints resolved `dotnet` commands and returns 0 → else orchestrate.
13. `Program.cs` — copy `Solve/Program.cs`: `Host.CreateApplicationBuilder` → `AddSlnLaunchCommand().AddSlnLaunchServices()` → `InvokeSlnLaunchCommandAsync(args)` with `ParseCommandException`/`Exception` handling.
14. `IConsoleService`/`ConsoleService` (adapt `Solve`'s) for info/warn/error/success + prefixed child-line writing.
15. Tests: end-to-end argument parsing (each option), `--list` output, `--dry-run` output for the sample file, multiple-profiles-without-`--profile` error.

**Done when:** the tool runs end-to-end against the `MintPlayer.Spark.slnLaunch` example (`--dry-run` exercised in CI; live launch verified manually).

## Phase 5 — Docs, packaging & release ⏳ PENDING

16. `MintPlayer.SlnLaunch/README.md` — install, all options, the Spark example, non-Project fallback note, teardown guarantee, cross-platform note. Wire `PackageReadmeFile`.
17. `dotnet pack -c Release` locally; inspect the nupkg (tool layout, no NU* warnings). Confirm `dotnet tool install --global --add-source ./nupkg MintPlayer.SlnLaunch` then `slnlaunch --help` works.
18. Flip PRD status columns ❌ → ✅; add a Version History table.
19. Merge to `master` → CI publishes `MintPlayer.SlnLaunch` to nuget.org + GitHub Packages automatically.

## Risks / Watch-outs

| Risk | Mitigation |
|------|------------|
| **Orphaned child processes after cancel** (the headline risk: `dotnet run` spawns the app as a child, so killing only the runner leaks the app) | Always `Kill(entireProcessTree: true)`; Phase 3 test asserts a *grandchild* PID is gone, not just the runner. Bounded grace wait + force-kill on second signal. |
| Process-tree kill behaves differently across OSes | `Process.Kill(entireProcessTree:true)` is the .NET cross-platform primitive (Win/Linux/macOS); CI proves Linux, manual check on Windows; note macOS as a verification gap if no runner available. |
| `--launch-profile` only honors `commandName: Project` profiles | Inspect `launchSettings.json`; warn + fall back to no-profile for IIS Express/Docker/Executable; skip `.dcproj`. |
| `-p` ambiguity (`--project` vs `--property`) | Always emit the long `--project`; reserve `-p` as the **tool's own** `--profile` short alias (the tool never forwards `-p` to `dotnet`). |
| Windows-authored `\\` paths fail on Linux/macOS CI | Normalize separators in the builder; path tests run on `ubuntu-latest`. |
| Interleaved child output unreadable | Per-project prefix + color by default; `--no-prefix` escape hatch; respect `NO_COLOR`. |
| `dotnet watch` opens browsers / extra noise | `--watch` is opt-in; document `DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=1`. |
| CI runs `dotnet test` before `pack` | Tests use a self-contained stub process; no dependency on a packed artifact. |
| Source-generator wiring differs subtly from `Solve` | Copy the four analyzer `ProjectReference` lines and `Program.cs` verbatim; build early in Phase 1 to confirm `AddSlnLaunchCommand`/`AddSlnLaunchServices` generate. |

## Notes / Decisions (from PRD)

- Command name **`slnlaunch`**; `--watch` opt-in (default `dotnet run`); non-Project `DebugTarget` → **warn + run without profile**.
- No debugger attach, no launchSettings re-implementation, no Docker Compose, no `.slnLaunch` authoring — see PRD Non-Goals.
- Multiple profiles require explicit `--profile`; single profile runs flagless.
