using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace Dotest;

/// <summary>
/// All console output for dotest. Failure boxes (via Spectre.Console Panel),
/// verbose tree, summary line, and build-error colorization.
/// </summary>
public static class Renderer
{
    private static readonly Regex StackFrameRx =
        new(@"^at (.+?) in (.+?):line (\d+)\s*$", RegexOptions.Compiled);

    private static readonly Regex BuildErrorRx =
        new(@"^(.+?)\((\d+),\d+\): error (\w+): (.+)$", RegexOptions.Compiled);

    // ── Failure box ──────────────────────────────────────────────────────────

    /// <summary>
    /// Render a failure box using Spectre.Console Panel with the test name in
    /// the border and sections for Error, Output, and Stack Trace.
    /// </summary>
    public static void RenderFailure(TestResult t)
    {
        var dur    = FormatElapsed(t.Duration);
        var header = $" [bold]{Markup.Escape(t.FullName)}[/] – [dim]failed after {dur}[/] ";

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(t.ErrorMessage))
        {
            sb.AppendLine("[bold yellow]Error[/]");
            foreach (var line in SplitLines(t.ErrorMessage))
                sb.AppendLine($"  {Markup.Escape(line)}");
        }

        if (!string.IsNullOrWhiteSpace(t.StdOut))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("[bold cyan]Output[/]");
            foreach (var line in SplitLines(t.StdOut))
                sb.AppendLine($"  {Markup.Escape(line)}");
        }

        if (!string.IsNullOrWhiteSpace(t.StackTrace))
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("[bold magenta]Stack Trace[/]");
            foreach (var line in SplitLines(t.StackTrace))
                sb.AppendLine(ColorizeStackFrame(line));
        }

        var panel = new Panel(new Markup(sb.ToString().TrimEnd()))
            .Header(header)
            .BorderColor(Color.Red)
            .Expand();

        AnsiConsole.WriteLine();
        AnsiConsole.Write(panel);
    }

    // ── Verbose tree ─────────────────────────────────────────────────────────

    /// <summary>
    /// Render tests grouped by class name using Spectre.Console Tree.
    /// Stdout lines appear as child nodes of each test.
    /// </summary>
    public static void RenderTree(IReadOnlyList<TestResult> tests)
    {
        var groups = tests.GroupBy(t =>
            string.IsNullOrEmpty(t.ClassName) ? "(unknown)" : t.ClassName);

        foreach (var g in groups)
        {
            var tree = new Tree($"  [cyan]{Esc(g.Key)}[/]");
            foreach (var t in g)
            {
                var icon = t.Outcome switch
                {
                    "Passed" => "[green]\u2713[/]",   // ✓
                    "Failed" => "[red]\u2717[/]",     // ✗
                    _        => "[yellow]\u25cb[/]",  // ○
                };
                var dur  = FormatElapsed(t.Duration);
                var node = tree.AddNode($"{icon} {Esc(t.Name)} [grey]{Esc(dur)}[/]");

                if (!string.IsNullOrWhiteSpace(t.StdOut))
                {
                    foreach (var line in SplitLines(t.StdOut))
                        node.AddNode($"[grey]{Esc(line)}[/]");
                }
            }
            AnsiConsole.Write(tree);
        }
    }

    // ── Summary line ─────────────────────────────────────────────────────────

    /// <summary>Compact summary: PASS  39 passed, 3 skipped  3.7s</summary>
    public static void RenderSummary(int passed, int failed, int skipped, TimeSpan elapsed)
    {
        var parts = new List<string>();
        if (passed  > 0) parts.Add($"[green]{passed} passed[/]");
        if (failed  > 0) parts.Add($"[red]{failed} failed[/]");
        if (skipped > 0) parts.Add($"[yellow]{skipped} skipped[/]");

        var icon = failed > 0 ? "[bold red]FAIL[/]" : "[bold green]PASS[/]";
        var dur  = FormatElapsed(elapsed);

        AnsiConsole.MarkupLine($"\n{icon}  {string.Join(", ", parts)}  [grey]{Esc(dur)}[/]");
    }

    // ── Build error colorization ──────────────────────────────────────────────

    /// <summary>
    /// Colorize a build error line:
    ///   /path/File.cs(42,5): error CS1061: ...
    ///   → cyan path, yellow :42, red error code, normal message
    /// </summary>
    public static string ColorizeBuildError(string line)
    {
        var trimmed = line.TrimStart();
        var m = BuildErrorRx.Match(trimmed);
        if (!m.Success)
            return $"  [red]{Esc(trimmed)}[/]";

        return $"  [cyan]{Esc(m.Groups[1].Value)}[/]" +
               $"[yellow]:{m.Groups[2].Value}[/] " +
               $"[red]error {Esc(m.Groups[3].Value)}[/]: " +
               Esc(m.Groups[4].Value);
    }

    // ── Stack frame colorization ──────────────────────────────────────────────

    private static string ColorizeStackFrame(string line)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("at ", StringComparison.Ordinal))
            return $"   [grey]{Esc(trimmed)}[/]";

        var m = StackFrameRx.Match(trimmed);
        if (!m.Success)
            return $"   [grey]{Esc(trimmed)}[/]";

        return $"   [grey]at {Esc(m.Groups[1].Value)}[/]" +
               $" in [cyan]{Esc(m.Groups[2].Value)}[/]" +
               $"[yellow]:line {m.Groups[3].Value}[/]";
    }

    // ── Duration formatting ───────────────────────────────────────────────────

    internal static string FormatElapsed(TimeSpan ts)
    {
        var ms = (int)ts.TotalMilliseconds;
        if (ms == 0) return "< 1ms";
        if (ms < 1_000) return $"{ms}ms";
        if (ms < 60_000)
        {
            var sec  = ms / 1_000;
            var frac = (ms % 1_000) / 100;
            return frac == 0 ? $"{sec}s" : $"{sec}.{frac}s";
        }
        return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
    }

    // ── Misc helpers ──────────────────────────────────────────────────────────

    private static string Esc(string s) => Markup.Escape(s);

    private static IEnumerable<string> SplitLines(string s) =>
        s.Trim().Split('\n').Select(l => l.TrimEnd('\r'));
}
