# CLAUDE.md — dotest project context

Drop this file (or paste it) into a new Claude session to pick up where the
last one left off.

---

## What this is

`dotest` is a .NET global tool that wraps `dotnet test` with a compact,
human-friendly output format. Install with `dotnet tool install -g dotest`.

The original implementation was a ~340-line nushell script (`dotest.nu` in
`LiHRaM/NuDetNu`). This repo is the C# port.

### Core UX goals (from the nushell version)

1. **Live single-line progress** — count + failures + elapsed, overwrites in
   place, disappears when done.
2. **Unicode failure boxes** — test name and duration badge embedded in the top
   border via Spectre.Console Panel; sections for Error, Output, Stack Trace.
3. **Compact summary** — `PASS  39 passed, 3 skipped  3.7s`
4. **Default tree mode** — hierarchy of namespaces/classes with pass/fail counts
   at each leaf node; no individual test lines.
5. **Verbose mode (`-v`)** — full tree with every test, ✓ / ✗ / ○ icons, stdout
   under each test.
6. **Compact mode (`-c`)** — failures and summary only, no tree.
7. **Fail-fast mode (`-f`)** — stop after the first test failure; shows failure
   box and summary, skips the tree (run is incomplete). Implemented via
   `wrapper.CancelTestRun()` called from `TestRunHandler` on first failure.
8. **Filter** — `dotest Portland.Worker` → substring match on `FullyQualifiedName`
9. **Build error capture** — colorized error lines when compilation fails.

Every design decision should serve "I want to scan failures instantly."

---

## Architecture

Single-project .NET 10 tool. Source files:

| File | Responsibility |
|------|---------------|
| `Program.cs` | Arg parsing, orchestration, build → discover → run → render flow |
| `Builder.cs` | Runs `dotnet build`, captures build errors |
| `TestDiscovery.cs` | Finds `.sln` root, test assemblies, and `vstest.console.dll` |
| `DiscoveryHandler.cs` | `ITestDiscoveryEventsHandler` — collects `TestCase` list |
| `TestRunHandler.cs` | `ITestRunEventsHandler` — converts `VsTestResult` → `TestResult` |
| `TestResult.cs` | `record` model for a single test case |
| `Renderer.cs` | Failure boxes, summary tree, verbose tree, summary line, build-error colorization |

### How tests run

`Program.cs` orchestrates three steps inside `AnsiConsole.Status()`:

1. **Build** — `Builder.Build()` runs `dotnet build --nologo` and captures any
   lines containing `: error ` as build errors.

2. **Discover assemblies** — `TestDiscovery.FindTestAssemblies()` scans `.csproj`
   files under the solution root for those referencing `Microsoft.NET.Test.Sdk`,
   then resolves each to its output `.dll` via `dotnet msbuild -getProperty:TargetPath`.

3. **Run via TranslationLayer** — `VsTestConsoleWrapper` is constructed with the
   path to `vstest.console.dll` (found via `dotnet --info` Base Path). Tests are
   first discovered with `DiscoveryHandler`, then filtered client-side on
   `FullyQualifiedName`, then run via `TestRunHandler`.

`TestRunHandler` converts `VsTestResult` → `TestResult` and calls a callback on
each result; `Program.cs` updates the live Spectre status line from that callback.

### Rendering

`Renderer.cs` does all output. Three compiled `Regex` instances:

- `StackFrameRx` — matches `at Method() in /path/File.cs:line N`
- `BuildErrorRx` — matches `/path/File.cs(line,col): error CODE: message`
- `AnsiRx` — strips ANSI/VT escape sequences from test stdout before Spectre rendering

**Failure boxes** use `Spectre.Console Panel` with `Expand()` — no manual width
calculations. ANSI codes in test stdout are stripped via `AnsiRx` before passing
to Spectre Markup.

**Summary tree (default)** — `RenderTreeSummary()` builds the same `HierNode`
hierarchy as the verbose tree but renders pass/fail counts at leaf nodes instead
of individual test lines. No per-test stdout panels.

**Verbose tree** — `RenderTree()` uses `Spectre.Console.Tree` for polished
connectors. Each test is a leaf node with glyph, duration, and argument list.
Theory variants are column-aligned via `AlignArguments()`. Arguments that exceed
the terminal width are right-truncated with `…`. Stdout is rendered in a raw
`Console.Write()` panel after all trees so ANSI codes are preserved.

**Hierarchical grouping** — `FindCommonPrefixParts()` finds the longest shared
dot-separated prefix across all class names and uses it as the tree root. Nested
xUnit classes (which use `+` as separator in `FullyQualifiedName`) are split on
both `.` and `+`. Color of intermediate nodes reflects worst child outcome:
red > yellow > green.

**Terminal width** — uses `Console.IsOutputRedirected ? 120 : Console.WindowWidth`
(not `AnsiConsole.Profile.Width` which defaults to 80 in some environments).

---

## Building

```sh
dotnet build         # development build
dotnet run           # run from source (in a solution directory)
dotnet pack          # create NuGet package in nupkg/
```

Install locally from source:

```sh
dotnet tool install -g --add-source ./nupkg dotest
dotnet tool uninstall -g dotest   # to remove
```

## Key dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Spectre.Console` | 0.49.1 | Panel, Tree, Markup, Status spinner |
| `Microsoft.TestPlatform.ObjectModel` | 18.0.1 | `TestCase`, `TestResult` types |
| `Microsoft.TestPlatform.TranslationLayer` | 18.0.1 | `VsTestConsoleWrapper`, event handler interfaces |
| `Newtonsoft.Json` | 13.0.3 | Required by TranslationLayer internals |

---

## Things to add (not yet implemented)

- `--watch` — rerun on file change (wrap `dotnet watch test`)
- `--failed` — rerun only previously failed tests (cache last run results)
- `--changed` — only tests in files touched vs git base
- JUnit XML output for CI
- Coverage integration (coverlet + pretty summary)
- Parallel project runs with unified progress bar

## Nushell → C# translation notes

Things that caused pain in nushell that are non-issues in C#:

- **Float division** — nushell `/` always returns float, `3732 / 1000 = 3.732…`.
  C# integer division truncates by default.
- **Closure mutation** — nushell closures can't mutate outer `mut` variables,
  forcing temp-file workarounds. C# captured variables work normally.
- **ANSI in interpolated strings** — nushell mangles escape codes inside `$"…"`.
  C# doesn't interpolate strings at the terminal level; raw `Console.Write()`
  passes them through.
- **`let` scoping in if-blocks** — not a C# concern.
- **`parse --regex` returns table** — in C# use `Regex.Match().Groups[N].Value`.
