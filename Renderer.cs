using System.Text.RegularExpressions;
using Spectre.Console;

namespace Dotest;

/// <summary>
/// All console output for dotest. Failure boxes, verbose tree, summary line,
/// and build-error colorization.
///
/// Failure boxes are rendered manually (not Spectre.Console Panel) to embed the
/// test name and duration badge directly into the top border and to add precise
/// right-border alignment. The verbose tree delegates to Spectre.Console Tree.
/// </summary>
public static class Renderer
{
    // Compiled once at startup.
    private static readonly Regex StackFrameRx =
        new(@"^at (.+?) in (.+?):line (\d+)\s*$", RegexOptions.Compiled);

    private static readonly Regex BuildErrorRx =
        new(@"^(.+?)\((\d+),\d+\): error (\w+): (.+)$", RegexOptions.Compiled);

    // Strips ANSI escape codes for visible-length calculation.
    private static readonly Regex AnsiCodeRx =
        new(@"\x1b\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled);

    // ── Failure box ──────────────────────────────────────────────────────────

    /// <summary>
    /// Render a failure box:
    ///   ┏━ TestClass.Name  failed after 1.2s ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┓
    ///   ┃                                                                                                ┃
    ///   ┃ Error                                                                                          ┃
    ///   ┃   Expected 42 but was 41                                                                       ┃
    ///   ┠────────────────────────────────────────────────────────────────────────────────────────────────┨
    ///   ┃ Stack Trace                                                                                    ┃
    ///   ┃   at Method() in /path/File.cs:line 42                                                        ┃
    ///   ┃                                                                                                ┃
    ///   ┗━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━┛
    /// </summary>
    public static void RenderFailure(TestResult t, int termWidth)
    {
        // inner = characters between the outer border glyphs ┏ and ┓.
        var inner = Math.Max(termWidth - 2, 20);

        var duration  = FormatDuration(t.Duration);
        var badge     = $" failed after {duration} ";     // plain text for width calc
        var fullTitle = $" {t.FullName} ";

        // Truncate title so border never overflows.
        var maxTitleW = inner - badge.Length - 3;
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

        // Empty line after header border.
        BoxLine("", inner);

        // Error section.
        if (!string.IsNullOrWhiteSpace(t.ErrorMessage))
        {
            BoxLine(" [bold yellow]Error[/]", inner);
            foreach (var l in SplitLines(t.ErrorMessage))
                BoxLine("   " + Esc(l), inner);
        }

        // Output section (stdout may contain raw ANSI codes — write border via
        // Spectre then content raw so escape sequences pass through).
        if (!string.IsNullOrWhiteSpace(t.StdOut))
        {
            AnsiConsole.MarkupLine($"[red]\u2520{div}\u2528[/]");
            BoxLine(" [bold cyan]Output[/]", inner);
            foreach (var l in SplitLines(t.StdOut))
                BoxRawLine(l, inner);
        }

        // Stack trace section.
        if (!string.IsNullOrWhiteSpace(t.StackTrace))
        {
            AnsiConsole.MarkupLine($"[red]\u2520{div}\u2528[/]");
            BoxLine(" [bold magenta]Stack Trace[/]", inner);
            foreach (var l in SplitLines(t.StackTrace))
                BoxLine(ColorizeStackFrame(l), inner);
        }

        // Blank line before bottom border (symmetric with the top).
        BoxLine("", inner);

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

    // ── Box-drawing helpers ───────────────────────────────────────────────────

    /// <summary>
    /// Write one content line inside the failure box with aligned right border:
    ///   ┃{markup}{padding}┃
    /// <paramref name="markup"/> may contain Spectre.Console markup; visible length
    /// is calculated by stripping tags with Markup.Remove().
    /// </summary>
    private static void BoxLine(string markup, int inner)
    {
        var visible = Markup.Remove(markup);
        // Truncate if the visible content would overflow.
        if (visible.Length > inner)
        {
            markup  = Esc(visible[..(inner - 1)] + "\u2026");
            visible = Markup.Remove(markup);
        }
        var pad = new string(' ', Math.Max(0, inner - visible.Length));
        AnsiConsole.MarkupLine($"[red]\u2503[/]{markup}{pad}[red]\u2503[/]");
    }

    /// <summary>
    /// Write a raw (ANSI-passthrough) line inside the failure box.
    /// The border is written via Spectre and the content via Console.Write so
    /// any ANSI escape codes in test stdout are preserved.
    /// </summary>
    private static void BoxRawLine(string raw, int inner, string indent = "   ")
    {
        var visLen = AnsiCodeRx.Replace(raw, "").Length;
        var available = inner - indent.Length - 1; // -1 for right ┃

        string display = raw;
        if (visLen > available)
        {
            // Can't truncate ANSI-coded strings cleanly: strip codes first.
            display = AnsiCodeRx.Replace(raw, "");
            if (display.Length > available - 1)
                display = display[..(available - 1)] + "\u2026";
            visLen = display.Length;
        }

        AnsiConsole.Markup("[red]\u2503[/]");
        Console.Write(indent);
        Console.Write(display);
        Console.Write(new string(' ', Math.Max(0, inner - indent.Length - visLen)));
        AnsiConsole.MarkupLine("[red]\u2503[/]");
    }

    // ── Misc helpers ──────────────────────────────────────────────────────────

    private static string Esc(string s) => Markup.Escape(s);

    private static IEnumerable<string> SplitLines(string s) =>
        s.Trim().Split('\n').Select(l => l.TrimEnd('\r'));
}
