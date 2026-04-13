# dotest

A pretty wrapper around `dotnet test` that replaces the noisy MSBuild output
with a compact, human-friendly format.

```
⏳ 42 tests  3s
```

becomes

```
┏━ Portland.Worker.Test.CreateBacktestTests.ShouldReturnPositions  failed after 1.2s ━━━━━━━━━━┓
┃
┃ Error
┃   Expected: 3 items
┃   But was:  2 items
┠──────────────────────────────────────────────────────────────────────────────────────────────┨
┃ Stack Trace
┃   at CreateBacktestTests.ShouldReturnPositions() in /src/Tests/CreateBacktestTests.cs:line 84
┗━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┛

PASS  39 passed, 1 failed, 3 skipped  4.1s
```

## Features

- **Live single-line progress** while tests run — `⏳ 42 tests  3s  2 failed` — overwritten in place, gone when done
- **Unicode failure boxes** with the test name and duration in the border, plus Error / Output / Stack Trace sections
- **Colorized stack frames** — grey method, cyan file path, yellow `:line N`
- **Compact summary** — `PASS  39 passed, 3 skipped  3.7s` / `FAIL  2 failed  3.7s`
- **Verbose mode (`-v`)** — tree grouped by class name with ✓ / ✗ / ○ icons and captured stdout
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
dotnet pack -c Release
dotnet tool install -g --add-source ./nupkg dotest
```

## Usage

```
USAGE:
  dotest [filter] [options]

ARGS:
  [filter]    Substring matched via FullyQualifiedName~<filter>

OPTIONS:
  -v, --verbose    Show all tests in a tree grouped by class
  -h, --help       Show this help

EXAMPLES:
  dotest                       Run all tests
  dotest Portland.Worker       Run tests matching Portland.Worker
  dotest CreateBacktest -v     Verbose tree for matching tests
```

### Verbose tree (`-v`)

Shows every test grouped by class, with icons and per-test stdout:

```
Passed
  Portland.Core.Test.PositionTests
  ├── ✓ ShouldCalculateNetValue [12ms]
  └── ✓ ShouldHandleEmptyPortfolio [5ms]

Failed
  Portland.Worker.Test.CreateBacktestTests
  └── ✗ ShouldReturnPositions [1.2s]
      └── Expected: 3 items …
```

### Filter

Pass any substring and dotest forwards it as a `FullyQualifiedName~` filter:

```sh
dotest Portland.Worker          # matches any test whose full name contains Portland.Worker
dotest CreateBacktest           # matches specific test classes or methods
```

## How it works

`dotest` runs:

```
dotnet test --nologo --results-directory <tmpdir> --logger "trx;LogFilePrefix=res" [--filter FullyQualifiedName~<filter>]
```

It streams stdout line-by-line to drive the live progress counter (the NUnit3
adapter emits `Passed TestName` / `Failed TestName` lines at default verbosity).
After the process exits, it parses the TRX XML file for structured results
(error messages, stack traces, stdout, class names) and renders the output.

## Notes

- Progress detection relies on lines starting with `Passed ` / `Failed ` as
  emitted by the NUnit3 test adapter. Other adapters (xUnit, MSTest) may not
  emit these lines, so the counter will show `building…` the whole time — but
  results are always correct since they come from the TRX file.
- The TRX logger is always used; `dotnet test`'s own console output is
  suppressed and replaced with dotest's rendering.

## License

MIT
