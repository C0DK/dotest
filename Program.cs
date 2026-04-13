using System.Diagnostics;
using Dotest;
using Spectre.Console;

return await RunAsync(args);

// ── Entry point ───────────────────────────────────────────────────────────────

static async Task<int> RunAsync(string[] args)
{
    string? filter  = null;
    bool    verbose = false;

    foreach (var arg in args)
    {
        switch (arg)
        {
            case "-v" or "--verbose":
                verbose = true;
                break;
            case "-h" or "--help":
                PrintHelp();
                return 0;
            case { } a when a.StartsWith('-'):
                Console.Error.WriteLine($"Unknown option: {arg}");
                return 1;
            default:
                filter = arg;
                break;
        }
    }

    // ── Run dotnet test ───────────────────────────────────────────────────────
    //
    // We shell out to `dotnet test` (rather than using the VSTest TranslationLayer
    // directly) because `dotnet test` handles project discovery, building, and
    // adapter loading automatically — the TranslationLayer requires locating
    // vstest.console.dll inside the SDK and managing those steps manually.
    //
    // Structured results come from the TRX file (adapter-agnostic). The stdout
    // stream is only read to update the live progress counter and to capture
    // build errors; the TRX is always the authoritative source of test outcomes.
    //
    // See TrxParser.cs for more detail and the TranslationLayer upgrade path.

    var trxDir = Path.Combine(Path.GetTempPath(), $"dotest-{Guid.NewGuid():N}");
    Directory.CreateDirectory(trxDir);

    var dotnetArgs = new List<string>
    {
        "test", "--nologo",
        "--results-directory", trxDir,
        "--logger", "trx;LogFilePrefix=res",
    };

    if (filter is not null)
    {
        dotnetArgs.Add("--filter");
        dotnetArgs.Add($"FullyQualifiedName~{filter}");
    }

    var psi = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
        UseShellExecute        = false,
    };
    foreach (var a in dotnetArgs) psi.ArgumentList.Add(a);

    // ── Run with live progress ────────────────────────────────────────────────

    var buildErrors = new List<string>();
    int totalCount = 0, failCount = 0;
    var sw = Stopwatch.StartNew();

    using var proc = Process.Start(psi)
        ?? throw new InvalidOperationException("Failed to start dotnet.");

    // Drain stderr in background to prevent pipe-buffer deadlock.
    var stderrTask = proc.StandardError.ReadToEndAsync();

    // Stream stdout line-by-line for live progress.
    // Test adapters typically emit "  Passed TestName" / "  Failed TestName" lines;
    // we count these for the in-flight counter. Results accuracy always comes from
    // the TRX file regardless of what the adapter emits here.
    string? line;
    while ((line = await proc.StandardOutput.ReadLineAsync()) is not null)
    {
        var trimmed = line.TrimStart();
        var isPass  = trimmed.StartsWith("Passed ", StringComparison.Ordinal);
        var isFail  = trimmed.StartsWith("Failed ", StringComparison.Ordinal);

        if (isPass || isFail) totalCount++;
        if (isFail) failCount++;
        if (trimmed.Contains(": error ")) buildErrors.Add(line);

        WriteProgress(totalCount, failCount, sw.Elapsed);
    }

    await proc.WaitForExitAsync();
    await stderrTask;
    sw.Stop();
    ClearProgress();

    // ── Parse TRX results ─────────────────────────────────────────────────────

    var tests = TrxParser.ParseDirectory(trxDir);
    try { Directory.Delete(trxDir, recursive: true); } catch { /* best-effort */ }

    if (tests.Count == 0)
    {
        if (buildErrors.Count > 0)
        {
            AnsiConsole.MarkupLine("\n[bold red]BUILD FAILED[/]\n");
            foreach (var err in buildErrors)
                AnsiConsole.MarkupLine(Renderer.ColorizeBuildError(err));
            Console.WriteLine();
        }
        else
        {
            AnsiConsole.MarkupLine(
                "[yellow]No test results found.[/] " +
                "Build may have failed \u2013 run [cyan]dotnet test[/] for details.");
        }
        return 1;
    }

    // ── Render ────────────────────────────────────────────────────────────────

    var failed  = tests.Where(t => t.Outcome == "Failed").ToList();
    var passed  = tests.Where(t => t.Outcome == "Passed").ToList();
    var skipped = tests.Where(t => t.Outcome != "Failed" && t.Outcome != "Passed").ToList();

    int termWidth = Console.IsOutputRedirected
        ? 100
        : Math.Clamp(Console.WindowWidth, 40, 220);

    // Failure boxes – one blank line before each box.
    foreach (var t in failed)
    {
        Console.WriteLine();
        Renderer.RenderFailure(t, termWidth);
    }

    // Verbose tree – grouped by class, with pass/skip/fail sections.
    if (verbose)
    {
        if (passed.Count  > 0) { AnsiConsole.MarkupLine("\n[bold green]Passed[/]");   Renderer.RenderTree(passed); }
        if (skipped.Count > 0) { AnsiConsole.MarkupLine("\n[bold yellow]Skipped[/]"); Renderer.RenderTree(skipped); }
        if (failed.Count  > 0) { AnsiConsole.MarkupLine("\n[bold red]Failed[/]");     Renderer.RenderTree(failed); }
        Console.WriteLine();
    }

    // Summary line.
    Renderer.RenderSummary(passed.Count, failed.Count, skipped.Count, sw.Elapsed);
    Console.WriteLine();

    // Trailing build errors (compilation errors that still let some tests run).
    if (buildErrors.Count > 0)
    {
        AnsiConsole.MarkupLine("\n[bold red]Build errors:[/]");
        foreach (var err in buildErrors)
            AnsiConsole.MarkupLine(Renderer.ColorizeBuildError(err));
        Console.WriteLine();
    }

    return failed.Count > 0 ? 1 : 0;
}

// ── Progress helpers ──────────────────────────────────────────────────────────

/// <summary>
/// Overwrite the current terminal line with a live counter:
///   ⏳ 42 tests  3s   or   ⏳ 42 tests  3s  3 failed
/// Does nothing when output is redirected (CI, pipes).
/// </summary>
static void WriteProgress(int total, int failed, TimeSpan elapsed)
{
    if (Console.IsOutputRedirected) return;

    var secs     = (int)elapsed.TotalSeconds;
    var countStr = total > 0 ? $"{total} tests" : "building\u2026";
    var failStr  = failed > 0 ? $"  \x1b[31m{failed} failed\x1b[0m" : "";

    Console.Write($"\x1b[2K\r\x1b[36m\u23f3\x1b[0m {countStr} \x1b[90m{secs}s\x1b[0m{failStr}");
}

/// <summary>Erase the progress line so result output starts on a clean line.</summary>
static void ClearProgress()
{
    if (Console.IsOutputRedirected) return;
    Console.Write("\x1b[2K\r");
}

// ── Help ─────────────────────────────────────────────────────────────────────

static void PrintHelp()
{
    Console.WriteLine("dotest \u2013 pretty wrapper around dotnet test");
    Console.WriteLine();
    Console.WriteLine("USAGE:");
    Console.WriteLine("  dotest [filter] [options]");
    Console.WriteLine();
    Console.WriteLine("ARGS:");
    Console.WriteLine("  [filter]    Substring matched via FullyQualifiedName~<filter>");
    Console.WriteLine();
    Console.WriteLine("OPTIONS:");
    Console.WriteLine("  -v, --verbose    Show all tests in a tree grouped by class");
    Console.WriteLine("  -h, --help       Show this help");
    Console.WriteLine();
    Console.WriteLine("EXAMPLES:");
    Console.WriteLine("  dotest                       Run all tests");
    Console.WriteLine("  dotest Portland.Worker       Run tests matching Portland.Worker");
    Console.WriteLine("  dotest CreateBacktest -v     Verbose tree for matching tests");
    Console.WriteLine();
    Console.WriteLine("INSTALL:");
    Console.WriteLine("  dotnet tool install -g dotest");
}
