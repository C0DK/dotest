using System.Diagnostics;
using System.Text.RegularExpressions;
using Dotest;
using Microsoft.TestPlatform.VsTestConsole.TranslationLayer;
using Microsoft.TestPlatform.VsTestConsole.TranslationLayer.Interfaces;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
using Spectre.Console;

using VsTestResult = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult;

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
    var results      = new List<Dotest.TestResult>();
    var sw           = Stopwatch.StartNew();
    bool buildFailed = false;

    AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .Start("Building...", ctx =>
        {
            // ── Step 1: build ────────────────────────────────────────────────
            if (!Build(buildErrors))
            {
                buildFailed = true;
                return;
            }

            // ── Step 2: discover test assemblies ─────────────────────────────
            ctx.Status("Discovering tests...");
            var assemblies = FindTestAssemblies();
            if (assemblies.Count == 0) return;

            // ── Step 3: run via TranslationLayer ─────────────────────────────
            int total = 0, fails = 0;
            void OnResult(Dotest.TestResult tr)
            {
                lock (results) results.Add(tr);
                var n   = Interlocked.Increment(ref total);
                var f   = tr.Outcome == "Failed" ? Interlocked.Increment(ref fails) : fails;
                var dur = Renderer.FormatElapsed(sw.Elapsed);
                ctx.Status(f > 0
                    ? $"{n} tests  [grey]{dur}[/]  [red]{f} failed[/]"
                    : $"{n} tests  [grey]{dur}[/]");
            }

            try
            {
                var vstestPath = FindVsTestConsolePath();
                var wrapper = new VsTestConsoleWrapper(vstestPath, new ConsoleParameters
                {
                    LogFilePath = null,
                    TraceLevel  = System.Diagnostics.TraceLevel.Off,
                });
                wrapper.StartSession();
                wrapper.InitializeExtensions([]);

                var runSettings = filter is null
                    ? "<RunSettings/>"
                    : $"<RunSettings><RunConfiguration>" +
                      $"<TestCaseFilter>FullyQualifiedName~{filter}</TestCaseFilter>" +
                      $"</RunConfiguration></RunSettings>";

                wrapper.RunTests(assemblies, runSettings, new TestRunHandler(OnResult));
                wrapper.EndSession();
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error running tests: {Markup.Escape(ex.Message)}[/]");
            }
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

// ── Build ─────────────────────────────────────────────────────────────────────

static bool Build(List<string> buildErrors)
{
    var psi = new ProcessStartInfo("dotnet", "build --nologo")
    {
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
        UseShellExecute        = false,
    };

    using var proc       = Process.Start(psi)!;
    var       stderrTask = Task.Run(() => proc.StandardError.ReadToEnd());

    string? line;
    while ((line = proc.StandardOutput.ReadLine()) is not null)
    {
        if (line.Contains(": error "))
            buildErrors.Add(line);
    }

    proc.WaitForExit();
    stderrTask.Wait();

    return proc.ExitCode == 0;
}

// ── Test assembly discovery ───────────────────────────────────────────────────

static List<string> FindTestAssemblies()
{
    var assemblies = new List<string>();

    string[] projFiles;
    try { projFiles = Directory.GetFiles(".", "*.csproj", SearchOption.AllDirectories); }
    catch { return assemblies; }

    foreach (var proj in projFiles)
    {
        try
        {
            var content = File.ReadAllText(proj);
            if (!content.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase))
                continue;

            var psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
            };
            psi.ArgumentList.Add("msbuild");
            psi.ArgumentList.Add(proj);
            psi.ArgumentList.Add("-getProperty:TargetPath");
            psi.ArgumentList.Add("-nologo");
            psi.ArgumentList.Add("-verbosity:quiet");

            using var proc = Process.Start(psi)!;
            var path = proc.StandardOutput.ReadToEnd().Trim();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            if (File.Exists(path))
                assemblies.Add(path);
        }
        catch { /* skip invalid projects */ }
    }

    return assemblies;
}

// ── vstest.console.dll path ───────────────────────────────────────────────────

static string FindVsTestConsolePath()
{
    var psi = new ProcessStartInfo("dotnet", "--info")
    {
        RedirectStandardOutput = true,
        UseShellExecute        = false,
    };
    using var proc   = Process.Start(psi)!;
    var       output = proc.StandardOutput.ReadToEnd();
    proc.WaitForExit();

    var match = Regex.Match(output, @"Base Path:\s+(.+)");
    if (!match.Success)
        throw new InvalidOperationException(
            "Cannot determine dotnet SDK base path from 'dotnet --info'.");

    var sdkPath = match.Groups[1].Value.Trim().TrimEnd('/', '\\');
    return Path.Combine(sdkPath, "vstest.console.dll");
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

// ── VSTest event handler ──────────────────────────────────────────────────────

internal sealed class TestRunHandler : ITestRunEventsHandler
{
    private readonly Action<Dotest.TestResult> _onResult;

    public TestRunHandler(Action<Dotest.TestResult> onResult) => _onResult = onResult;

    public void HandleTestRunStatsChange(TestRunChangedEventArgs? args)
    {
        if (args?.NewTestResults is null) return;
        foreach (var r in args.NewTestResults)
            _onResult(Convert(r));
    }

    public void HandleTestRunComplete(
        TestRunCompleteEventArgs     completeArgs,
        TestRunChangedEventArgs?     lastChunk,
        ICollection<AttachmentSet>?  attachments,
        ICollection<string>?         executorUris)
    {
        if (lastChunk?.NewTestResults is null) return;
        foreach (var r in lastChunk.NewTestResults)
            _onResult(Convert(r));
    }

    public void HandleLogMessage(TestMessageLevel level, string? message) { }
    public void HandleRawMessage(string rawMessage) { }
    public int  LaunchProcessWithDebuggerAttached(TestProcessStartInfo info) => -1;

    private static Dotest.TestResult Convert(VsTestResult r)
    {
        var fqn          = r.TestCase.FullyQualifiedName;
        var nameForSplit = fqn.Contains('(') ? fqn[..fqn.IndexOf('(')] : fqn;
        var lastDot      = nameForSplit.LastIndexOf('.');
        var className    = lastDot > 0 ? nameForSplit[..lastDot] : "";
        var name         = r.TestCase.DisplayName ?? (lastDot > 0 ? fqn[(lastDot + 1)..] : fqn);

        var outcome = r.Outcome switch
        {
            TestOutcome.Passed  => "Passed",
            TestOutcome.Failed  => "Failed",
            TestOutcome.Skipped => "Skipped",
            _                   => "Skipped",
        };

        var stdOut = string.Join("\n",
            r.Messages
             .Where(m => m.Category == TestResultMessage.StandardOutCategory)
             .Select(m => m.Text?.TrimEnd() ?? ""));

        return new Dotest.TestResult(
            ClassName:    className,
            Name:         name,
            FullName:     fqn,
            Outcome:      outcome,
            Duration:     r.Duration,
            ErrorMessage: r.ErrorMessage ?? "",
            StackTrace:   r.ErrorStackTrace ?? "",
            StdOut:       stdOut
        );
    }
}
