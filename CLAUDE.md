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
   border; sections for Error, Output, Stack Trace; colorized stack frames.
3. **Compact summary** — `PASS  39 passed, 3 skipped  3.7s`
4. **Verbose mode (`-v`)** — tree grouped by class, ✓ / ✗ / ○ icons, stdout
   under each test.
5. **Filter** — `dotest Portland.Worker` → `--filter FullyQualifiedName~Portland.Worker`
6. **Build error capture** — colorized error lines when compilation fails.

Every design decision should serve "I want to scan failures instantly."

---

## Architecture

Single-project .NET 8 tool. Four source files:

| File | Responsibility |
|------|---------------|
| `Program.cs` | Arg parsing, process start, stdout streaming (live progress), orchestration |
| `TrxParser.cs` | Parse `*.trx` XML files from the temp results directory |
| `TestResult.cs` | `record` model for a single test case |
| `Renderer.cs` | Failure boxes, verbose tree, summary line, build-error colorization |

### How tests run

`Program.cs` builds these arguments and starts `dotnet` as a child process with
redirected stdout/stderr:

```
dotnet test --nologo --results-directory <tmpdir> --logger "trx;LogFilePrefix=res" [--filter FullyQualifiedName~<filter>]
```

Stdout is read line-by-line. Lines starting with `Passed ` or `Failed ` (after
trimming) drive the live progress counter — this matches the NUnit3 adapter's
default output format. Lines containing `: error ` are saved as build errors.

Stderr is drained into a background `Task` to prevent pipe-buffer deadlock.

After the process exits, TRX files are parsed and the temp dir is deleted.

### TRX parsing

`TrxParser.cs` uses `System.Xml.Linq` (XDocument). It matches elements by
`LocalName` to avoid namespace-handling complexity.

TRX schema summary:
- `TestRun/Results/UnitTestResult[@testId, @testName, @outcome, @duration]`
- `UnitTestResult/Output/StdOut`
- `UnitTestResult/Output/ErrorInfo/Message`
- `UnitTestResult/Output/ErrorInfo/StackTrace`
- `TestRun/TestDefinitions/UnitTest[@id]/TestMethod[@className]`

Class name comes from `TestDefinitions`, not from the result — looked up via
`testId`. This is the same approach as the nushell version.

### Rendering

`Renderer.cs` does all output. Two compiled `Regex` instances (no
`[GeneratedRegex]` to keep the class non-partial):

- `StackFrameRx` — matches `at Method() in /path/File.cs:line N`
- `BuildErrorRx` — matches `/path/File.cs(line,col): error CODE: message`

**Failure boxes** are rendered manually (not Spectre.Console Panel) because we
need the test name and duration badge embedded in the top border with precise
width control. The box width adapts to the terminal width via
`Console.WindowWidth`.

**Stdout in failure boxes** is written via raw `Console.WriteLine()` after a
Spectre.Console markup border character — this preserves any ANSI escape codes
that test output may contain (e.g. Serilog-colored logs).

**Verbose tree** delegates to `Spectre.Console.Tree` for polished connectors.

**Progress line** uses raw ANSI escape codes written to `Console.Write()`:
- `\x1b[2K\r` — clear current line and return to column 0
- `\x1b[36m` / `\x1b[31m` / `\x1b[90m` / `\x1b[0m` — cyan / red / dark-grey / reset

Progress is skipped when `Console.IsOutputRedirected` is true (CI, pipes).

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
| `Spectre.Console` | 0.49.1 | Markup, Tree rendering, color support |

No other third-party dependencies. TRX parsing and process management use BCL
types only.

---

## Things to add (not yet implemented)

These were listed in the original primer as future ideas:

- `--watch` — rerun on file change (wrap `dotnet watch test`)
- `--failed` — rerun only previously failed tests (cache last run results)
- `--changed` — only tests in files touched vs git base
- JUnit XML output for CI
- Coverage integration (coverlet + pretty summary)
- Parallel project runs with unified progress bar
- `TranslationLayer` approach for real-time events instead of stdout parsing
  (would make progress correct for non-NUnit adapters)

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
