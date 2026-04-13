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
            int total = 0, fails = 0;
            void OnResult(TestResult tr)
            {
                lock (results) results.Add(tr);
                var n   = Interlocked.Increment(ref total);
                var f   = tr.Outcome == "Failed" ? Interlocked.Increment(ref fails) : fails;
                var dur = Renderer.FormatElapsed(sw.Elapsed);
                ctx.Status(f > 0
                    ? $"{n} tests  [grey]{dur}[/]  [red]{f} failed[/]"
                    : $"{n} tests  [grey]{dur}[/]");
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

    foreach (var t in failed)
        Renderer.RenderFailure(t);

    if (verbose)
    {
        if (passed.Count  > 0) { AnsiConsole.MarkupLine("\n[bold green]Passed[/]");   Renderer.RenderTree(passed); }
        if (skipped.Count > 0) { AnsiConsole.MarkupLine("\n[bold yellow]Skipped[/]"); Renderer.RenderTree(skipped); }
        if (failed.Count  > 0) { AnsiConsole.MarkupLine("\n[bold red]Failed[/]");     Renderer.RenderTree(failed); }
        Console.WriteLine();
    }

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

        var runSettings = filter is null
            ? "<RunSettings/>"
            : $"<RunSettings><RunConfiguration>" +
              $"<TestCaseFilter>FullyQualifiedName~{filter}</TestCaseFilter>" +
              $"</RunConfiguration></RunSettings>";

        wrapper.RunTests(assemblies, runSettings, new TestRunHandler(onResult));
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
