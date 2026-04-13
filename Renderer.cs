using System.Text.RegularExpressions;
using Spectre.Console;

namespace Dotest;

/// <summary>
/// All console output for dotest. Failure boxes, verbose tree, summary line,
/// and build-error colorization.
///
/// Failure box uses manual ANSI-aware rendering to embed the test name and
/// duration badge directly into the top border, matching the nushell original.
/// The verbose tree delegates to Spectre.Console Tree for polished connectors.
/// </summary>
public static class Renderer
{
    // Compiled once at startup.
    private static readonly Regex StackFrameRx =
        new(@"^at (.+?) in (.+?):line (\d+)\s*$", RegexOptions.Compiled);

    private static readonly Regex BuildErrorRx =
        new(@"^(.+?)\((\d+),\d+\): error (\w+): (.+)$", RegexOptions.Compiled);

    // ── Failure box ──────────────────────────────────────────────────────────

    /// <summary>
    /// Render a failure box styled after the nushell original:
    ///   ┏━ TestClass.Name  failed after 1.2s ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┓
    ///   ┃
    ///   ┃ Error
    ///   ┃   expected …
    ///   ┠──────────────────────────────────────────────────────────────────────┨
    ///   ┃ Output
    ///   ┃   raw stdout (may contain ANSI codes)
    ///   ┠──────────────────────────────────────────────────────────────────────┨
    ///   ┃ Stack Trace
    ///   ┃   at Method() in /path/File.cs:line 42
    ///   ┗━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┛
    /// </summary>
    public static void RenderFailure(TestResult t, int termWidth)
    {
        // inner = characters between the outer border glyphs.
        var inner = Math.Max(termWidth - 2, 20);

        var duration = FormatDuration(t.Duration);
        var badge    = $" failed after {duration} ";     // plain text for width calc
        var fullTitle = $" {t.FullName} ";

        // Truncate title so the border never overflows.
        var maxTitleW = inner - badge.Length - 3;        // 3 = initial ━ + two padding
        var title = fullTitle.Length > maxTitleW && maxTitleW > 4
            ? $" {t.FullName[..(maxTitleW - 4)]}\u2026 "
            : fullTitle;

        var rightW    = Math.Max(0, inner - title.Length - badge.Length - 1);
        var rightFill = new string('\u2501', rightW);    // ━
        var hr        = new string('\u2501', inner);      // ━━━
        var div       = new string('\u2500', inner);      // ───

        // Top border: ┏━<title><badge><fill>┓
        AnsiConsole.MarkupLine(
            $"[red]\u250f\u2501[/][bold red]{Esc(title)}{Esc(badge)}[/][red]{rightFill}\u2513[/]");

        // Error section
        if (!string.IsNullOrWhiteSpace(t.ErrorMessage))
        {
            AnsiConsole.MarkupLine("[red]\u2503[/]");
            AnsiConsole.MarkupLine("[red]\u2503[/] [bold yellow]Error[/]");
            foreach (var line in SplitLines(t.ErrorMessage))
                AnsiConsole.MarkupLine($"[red]\u2503[/]   {Esc(line)}");
        }

        // Output section (stdout may contain raw ANSI from test frameworks —
        // write the border via Spectre then the content line raw to preserve codes)
        if (!string.IsNullOrWhiteSpace(t.StdOut))
        {
            AnsiConsole.MarkupLine($"[red]\u2520{div}\u2528[/]");
            AnsiConsole.MarkupLine("[red]\u2503[/] [bold cyan]Output[/]");
            foreach (var line in SplitLines(t.StdOut))
            {
                AnsiConsole.Markup("[red]\u2503[/]   ");
                Console.WriteLine(line);    // raw — passes ANSI codes through
            }
        }

        // Stack trace section
        if (!string.IsNullOrWhiteSpace(t.StackTrace))
        {
            AnsiConsole.MarkupLine($"[red]\u2520{div}\u2528[/]");
            AnsiConsole.MarkupLine("[red]\u2503[/] [bold magenta]Stack Trace[/]");
            foreach (var line in SplitLines(t.StackTrace))
                AnsiConsole.MarkupLine($"[red]\u2503[/]{ColorizeStackFrame(line)}");
        }

        // Bottom border: ┗━━━━━━━━━━━━┛
        AnsiConsole.MarkupLine($"[red]\u2517{hr}\u251b[/]");
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
                    "Passed"  => "[green]\u2713[/]",   // ✓
                    "Failed"  => "[red]\u2717[/]",     // ✗
                    _         => "[yellow]\u25cb[/]",  // ○
                };
                var dur  = FormatDuration(t.Duration);
                var node = tree.AddNode($"{icon} {Esc(t.Name)} [grey][{Esc(dur)}][/]");

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

    /// <summary>
    /// Compact summary: PASS  39 passed, 3 skipped  3.7s
    /// Starts with a blank line so it stands out after the box/tree output.
    /// </summary>
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

    /// <summary>
    /// Colorize a single stack frame line:
    ///   at Method() in /path/File.cs:line 42
    ///   → grey method, cyan path, yellow :line N
    /// Non-matching lines render in grey.
    /// </summary>
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

    /// <summary>Format a TRX duration string (HH:MM:SS.NNNNNNN) into a human string.</summary>
    public static string FormatDuration(string? durationStr)
    {
        if (string.IsNullOrEmpty(durationStr) || !TimeSpan.TryParse(durationStr, out var ts))
            return "";
        return FormatElapsed(ts);
    }

    private static string FormatElapsed(TimeSpan ts)
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Esc(string s) => Markup.Escape(s);

    private static IEnumerable<string> SplitLines(string s) =>
        s.Trim().Split('\n').Select(l => l.TrimEnd('\r'));
}
