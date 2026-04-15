# dotest

A pretty wrapper around `dotnet test` that replaces the noisy MSBuild output
with a compact, human-friendly format.

```
⏳ 42 tests  3s
```

becomes

```
┏━ Portland.Worker.Test.CreateBacktestTests.ShouldReturnPositions  failed after 1.2s ━━━━━━━━━━━┓
┃                                                                                               ┃
┃ Error                                                                                         ┃
┃   Expected: 3 items                                                                           ┃
┃   But was:  2 items                                                                           ┃
┠───────────────────────────────────────────────────────────────────────────────────────────────┨
┃ Stack Trace                                                                                   ┃
┃   at CreateBacktestTests.ShouldReturnPositions() in /src/Tests/CreateBacktestTests.cs:line 84 ┃
┃                                                                                               ┃
┗━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┛

PASS  39 passed, 1 failed, 3 skipped  4.1s
```

## Features

- **Live single-line progress** while tests run — `⏳ 42 tests  3s  2 failed` — overwritten in place, gone when done
- **Unicode failure boxes** with the test name and duration in the border, plus Error / Output / Stack Trace sections
- **Colorized stack frames** — grey method, cyan file path, yellow `:line N`
- **Compact summary** — `PASS  39 passed, 3 skipped  3.7s` / `FAIL  2 failed  3.7s`
- **Summary tree (default)** — hierarchy of namespaces/classes with pass/fail counts per node, no individual test lines
- **Verbose mode (`-v`)** — full tree with every test, ✓ / ✗ / ○ icons and captured stdout
- **Filter by substring** — `dotest Portland.Worker` → `--filter FullyQualifiedName~Portland.Worker`
- **Build error capture** — when compilation fails, shows colorized error lines instead of just "build may have failed"
- **Proper exit codes** — exits 1 on failures or build errors, 0 on pass

## Requirements

- .NET 8 SDK or later

## Install

### As a global tool (recommended)

```sh
dotnet tool install -g dotest
```

Then run from any directory containing a solution or project:

```sh
dotest
```

### From source

```sh
git clone https://github.com/c0dk/dotest
cd dotest
dotnet pack -c Release -o nupkg
dotnet tool install -g --add-source ./nupkg dotest
```

## Usage

```
USAGE:
  dotest [filter] [options]

ARGS:
  [filter]    Substring matched via FullyQualifiedName~<filter>

OPTIONS:
  -v, --verbose    Show full tree with every individual test
  -c, --compact    Compact output: failures and summary only, no tree
  -h, --help       Show this help

EXAMPLES:
  dotest                       Run all tests (summary tree + failures)
  dotest Portland.Worker       Run tests matching Portland.Worker
  dotest -c                    Compact: failures and summary only
  dotest CreateBacktest -v     Full tree for matching tests
```

### Default tree (no flags)

Shows a hierarchy of namespaces and classes, with pass/fail counts at each node
but no individual test lines. Failure boxes still appear before the summary.

```
Portland
└── Core.Test
    ├── PositionTests  2 passed
    └── OrderTests     5 passed
```

### Verbose tree (`-v`)

Shows every test in the full hierarchy, with icons and per-test stdout:

```
Portland
├── Core.Test
│   └── PositionTests
│       ├── ✓   12ms ShouldCalculateNetValue
│       └── ✓    5ms ShouldHandleEmptyPortfolio
└── Worker.Test
    └── CreateBacktestTests
        └── ✗  1.2s ShouldReturnPositions
```

### Filter

Pass any substring and dotest forwards it as a `FullyQualifiedName~` filter:

```sh
dotest Portland.Worker          # matches any test whose full name contains Portland.Worker
dotest CreateBacktest           # matches specific test classes or methods
```

## How it works

`dotest` first runs `dotnet build`, then uses the VSTest **TranslationLayer**
(`VsTestConsoleWrapper`) to run tests in-process:

1. Discovers test assemblies by scanning `.csproj` files for `Microsoft.NET.Test.Sdk`
2. Discovers all test cases, then applies the filter client-side
3. Receives real-time `TestResult` events as each test completes
4. Renders failures, tree, and summary once all tests finish

This approach works with all adapters (NUnit, xUnit, MSTest) without scraping stdout.

## License

MIT
