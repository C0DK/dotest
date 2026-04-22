using System.Diagnostics;
using Dotest;
using Microsoft.TestPlatform.VsTestConsole.TranslationLayer;
using Spectre.Console;

return Run(args);

// ── Entry point ───────────────────────────────────────────────────────────────

static int Run(string[] args)
{
    string? filter  = null;
    bool    verbose = false;
    bool    compact = false;

    foreach (var arg in args)
    {
        switch (arg)
        {
            case "-v" or "--verbose":
                verbose = true;
                break;
            case "-c" or "--compact":
                compact = true;
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

    var buildErrors  = new List<string>();
    var results      = new List<TestResult>();
    var sw           = Stopwatch.StartNew();
    bool buildFailed = false;

    AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start("Building...", ctx =>
        {
            // ── Step 1: build ────────────────────────────────────────────────
            if (!Builder.Build(buildErrors))
            {
                buildFailed = true;
                return;
            }

            // ── Step 2: discover test assemblies ─────────────────────────────
            ctx.Status("Discovering tests...");
            var assemblies = TestDiscovery.FindTestAssemblies();
            if (assemblies.Count == 0) return;

            // ── Step 3: run via TranslationLayer ─────────────────────────────
            int total = 0, fails = 0, skips = 0;
            void OnResult(TestResult tr)
            {
                lock (results) results.Add(tr);
                var n = Interlocked.Increment(ref total);
                var f = tr.Outcome == "Failed"  ? Interlocked.Increment(ref fails) : fails;
                var s = tr.Outcome == "Skipped" ? Interlocked.Increment(ref skips) : skips;
                var dur    = Renderer.FormatElapsed(sw.Elapsed);
                var status = $"{n} tests  [grey]{dur}[/]";
                if (f > 0) status += $"  [red]{f} failed[/]";
                if (s > 0) status += $"  [yellow]{s} skipped[/]";
                ctx.Status(status);
            }

            RunTests(assemblies, filter, OnResult);
        });

    sw.Stop();

    // ── Render results ────────────────────────────────────────────────────────

    if (buildFailed)
    {
        AnsiConsole.MarkupLine("\n[bold red]BUILD FAILED[/]\n");
        foreach (var err in buildErrors)
            AnsiConsole.MarkupLine(Renderer.ColorizeBuildError(err));
        Console.WriteLine();
        return 1;
    }

    if (results.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]No tests found.[/] " +
            "Ensure at least one project references [cyan]Microsoft.NET.Test.Sdk[/].");
        return 0;
    }

    var failed  = results.Where(t => t.Outcome == "Failed").ToList();
    var passed  = results.Where(t => t.Outcome == "Passed").ToList();
    var skipped = results.Where(t => t.Outcome != "Failed" && t.Outcome != "Passed").ToList();

    if (verbose)
    {
        Console.WriteLine();
        Renderer.RenderTree(results);
        Console.WriteLine();
    }
    else if (!compact)
    {
        Console.WriteLine();
        Renderer.RenderTreeSummary(results);
        Console.WriteLine();
    }

    foreach (var t in failed)
        Renderer.RenderFailure(t);

    Renderer.RenderSummary(passed.Count, failed.Count, skipped.Count, sw.Elapsed);
    Console.WriteLine();

    return failed.Count > 0 ? 1 : 0;
}

// ── Test run ──────────────────────────────────────────────────────────────────

static void RunTests(List<string> assemblies, string? filter, Action<TestResult> onResult)
{
    var logFile = Path.Combine(Path.GetTempPath(), $"dotest-{Guid.NewGuid():N}.log");
    try
    {
        var vstestPath = TestDiscovery.FindVsTestConsolePath();
        var wrapper    = new VsTestConsoleWrapper(vstestPath, new ConsoleParameters
        {
            LogFilePath = logFile,
            TraceLevel  = System.Diagnostics.TraceLevel.Off,
        });
        wrapper.StartSession();
        wrapper.InitializeExtensions([]);

        // Discover all test cases, then filter client-side. This is more
        // reliable than relying on TestCaseFilter in RunSettings, which not
        // all adapters honour when running from source paths.
        var discovery = new DiscoveryHandler();
        wrapper.DiscoverTests(assemblies, "<RunSettings/>", discovery);

        var testCases = filter is null
            ? discovery.Cases
            : discovery.Cases
                .Where(tc => tc.FullyQualifiedName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (testCases.Count > 0)
            wrapper.RunTests(testCases, "<RunSettings/>", new TestRunHandler(onResult));

        wrapper.EndSession();
    }
    catch (Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            AnsiConsole.MarkupLine($"[red]Error running tests: {Markup.Escape(e.Message)}[/]");
    }
    finally
    {
        try { File.Delete(logFile); } catch { /* best-effort cleanup */ }
    }
}

// ── Help ──────────────────────────────────────────────────────────────────────

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
    Console.WriteLine("  -v, --verbose    Show full tree with every individual test");
    Console.WriteLine("  -c, --compact    Compact output: failures and summary only, no tree");
    Console.WriteLine("  -h, --help       Show this help");
    Console.WriteLine();
    Console.WriteLine("EXAMPLES:");
    Console.WriteLine("  dotest                       Run all tests (summary tree + failures)");
    Console.WriteLine("  dotest Portland.Worker       Run tests matching Portland.Worker");
    Console.WriteLine("  dotest -c                    Compact: failures and summary only");
    Console.WriteLine("  dotest CreateBacktest -v     Full tree for matching tests");
    Console.WriteLine();
    Console.WriteLine("INSTALL:");
    Console.WriteLine("  dotnet tool install -g dotest");
}
