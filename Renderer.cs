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

    // Matches all ANSI/VT escape sequences so they can be stripped before
    // passing test stdout to Spectre.Console, which cannot render raw ANSI.
    private static readonly Regex AnsiRx =
        new(@"\x1b\[[0-9;]*[a-zA-Z]", RegexOptions.Compiled);

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
            foreach (var line in SplitLines(StripAnsi(t.StdOut)))
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
            var tree    = new Tree($"  [cyan]{Esc(g.Key)}[/]");
            var classPrefix = g.Key.Length > 0 ? g.Key + "." : "";
            var testList    = g.ToList();

            // Strip class prefix, then right-align theory argument values.
            var rawNames     = testList.Select(t => t.Name.StartsWith(classPrefix, StringComparison.Ordinal)
                                   ? t.Name[classPrefix.Length..] : t.Name).ToList();
            var displayNames = AlignArguments(rawNames);

            // 4 = tree connector "├── ", 1 = glyph, 1 = space, 6 = duration, 1 = space,
            // methodPart (varies), 2 = "(" and ")", 2 = right margin
            var termWidth = Console.IsOutputRedirected ? 120 : Console.WindowWidth;

            for (int ii = 0; ii < testList.Count; ii++)
            {
                var t           = testList[ii];
                var displayName = displayNames[ii];
                var rawName     = rawNames[ii];

                var color = t.Outcome switch
                {
                    "Passed" => "green",
                    "Failed" => "red",
                    _        => "yellow",
                };
                var glyph = t.Outcome switch
                {
                    "Passed" => "\u2713",   // ✓
                    "Failed" => "\u2717",   // ✗
                    _        => "\u25cb",   // ○
                };
                var dur = FormatElapsed(t.Duration).PadLeft(6);

                // displayName from AlignArguments is already single-line (Truncate flattened
                // any multi-line values). Do NOT apply WhitespaceRx here — that would collapse
                // the intentional padding spaces added by PadLeft for column alignment.
                var dParen     = displayName.IndexOf('(');
                var methodPart = dParen >= 0 ? displayName[..dParen].TrimEnd() : displayName.Trim();

                // argsAligned: from the aligned display name, leading/trailing spaces trimmed.
                // For labeled args (x:   1) internal spaces are preserved → alignment intact.
                // For unlabeled args padded with leading spaces, Trim() removes them cleanly.
                var argsAligned = dParen >= 0 ? displayName[(dParen + 1)..^1].Trim() : "";

                // argsRaw: from the pre-alignment name, whitespace-collapsed for multi-line safety.
                // Used as truncation source so leading padding spaces never eat into the budget.
                var rParen  = rawName.IndexOf('(');
                var argsRaw = rParen >= 0
                            ? WhitespaceRx.Replace(rawName[(rParen + 1)..^1].Trim(), " ").Trim()
                            : "";

                var argBudget = Math.Max(10, termWidth - 17 - methodPart.Length);

                // Prefer aligned (preserves column alignment for short numeric args).
                // Fall back to unpadded raw when args exceed the budget so the truncation
                // point is driven by actual content, not padding spaces.
                string argsInner;
                if (argsAligned.Length <= argBudget)
                    argsInner = argsAligned;
                else if (argsRaw.Length <= argBudget)
                    argsInner = argsRaw;
                else
                    argsInner = argsRaw[..(argBudget - 1)] + "…";

                var nodeText = $"[{color}]{Esc(glyph)}[/] [grey]{Esc(dur)}[/] {Esc(methodPart)}"
                             + (argsInner.Length > 0 ? $"([silver]{Esc(argsInner)}[/])" : "");
                tree.AddNode(nodeText);
            }
            AnsiConsole.Write(tree);

            // Stdout panels are printed after the tree so raw ANSI codes can be
            // stripped cleanly without disrupting the tree connectors.
            foreach (var (t, displayName) in testList.Zip(displayNames))
            {
                if (!string.IsNullOrWhiteSpace(t.StdOut))
                    RenderStdOutPanel(displayName, t.StdOut);
            }
        }
    }

    private static void RenderStdOutPanel(string testName, string stdOut)
    {
        // Spectre strips raw ESC bytes from anything it renders, so we bypass
        // it entirely and draw the panel frame with Console.Write. ANSI codes
        // in the content are preserved; AnsiRx is used only for visible-length
        // calculation so the right border column stays aligned.
        var width = Console.IsOutputRedirected ? 120 : Math.Max(Console.WindowWidth, 20);
        var inner = width - 2;  // space between the two │ borders
        var label = $" {testName} ";
        var topRight = Math.Max(0, inner - 1 - label.Length);

        const string Grey  = "\x1b[90m";
        const string Reset = "\x1b[0m";

        Console.WriteLine($"{Grey}╭─{label}{new string('─', topRight)}╮{Reset}");

        foreach (var line in SplitLines(stdOut))
        {
            var visibleLen = AnsiRx.Replace(line, "").Length;
            var pad        = Math.Max(0, inner - 1 - visibleLen);
            Console.Write($"{Grey}│{Reset} {line}{new string(' ', pad)}{Grey}│{Reset}");
            Console.WriteLine();
        }

        Console.WriteLine($"{Grey}╰{new string('─', inner)}╯{Reset}");
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
            return $"   [silver]{Esc(trimmed)}[/]";

        var m = StackFrameRx.Match(trimmed);
        if (!m.Success)
            return $"   [silver]{Esc(trimmed)}[/]";

        return $"   [silver]at {Esc(m.Groups[1].Value)}[/]" +
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

    private const  int    MaxArgValueLen  = 40;
    private static readonly Regex WhitespaceRx = new(@"\s+", RegexOptions.Compiled);
    private static string Esc(string s)          => Markup.Escape(s);
    internal static string StripAnsi(string s)   => AnsiRx.Replace(s, "");

    // Single-line values are kept as-is. Multi-line values (e.g. HtmlDescription
    // containing \n) are flattened to one line and then truncated because the
    // collapsed result can be arbitrarily long.
    internal static string Truncate(string s)
    {
        if (!s.Contains('\n') && !s.Contains('\r'))
            return s;
        var flat = WhitespaceRx.Replace(s.Trim(), " ");
        return flat.Length <= MaxArgValueLen ? flat : flat[..(MaxArgValueLen - 1)] + "…";
    }
    internal static List<string> AlignArguments(List<string> names)
    {
        // Group list indices by base name (e.g. "MyMethod" from "MyMethod(x: 1)").
        var buckets = new Dictionary<string, List<int>>();
        for (int i = 0; i < names.Count; i++)
        {
            var p   = names[i].IndexOf('(');
            var key = p >= 0 ? names[i][..p].TrimEnd() : names[i];
            if (!buckets.ContainsKey(key)) buckets[key] = [];
            buckets[key].Add(i);
        }

        var result = names.ToList();
        foreach (var (baseName, indices) in buckets)
        {
            if (indices.Count < 2) continue;  // nothing to align

            // Parse and truncate each value so columns stay a reasonable width.
            var parsed = indices
                .Select(i => ParseArgList(names[i][(baseName.Length + 1)..^1])
                             .Select(a => (a.label, value: Truncate(a.value)))
                             .ToList())
                .ToList();

            // Find the widest value at each argument position.
            int maxArgs   = parsed.Max(a => a.Count);
            var maxWidths = new int[maxArgs];
            foreach (var args in parsed)
                for (int j = 0; j < args.Count; j++)
                    maxWidths[j] = Math.Max(maxWidths[j], args[j].value.Length);

            // Rebuild each name with values padded to the column width.
            // Skip the "label: " prefix when the label is empty (unlabelled params).
            for (int k = 0; k < indices.Count; k++)
            {
                var args    = parsed[k];
                var aligned = args.Select((a, j) => string.IsNullOrEmpty(a.label)
                    ? a.value.PadLeft(maxWidths[j])
                    : $"{a.label}: {a.value.PadLeft(maxWidths[j])}");
                result[indices[k]] = baseName + "(" + string.Join(", ", aligned) + ")";
            }
        }
        return result;
    }

    private static IEnumerable<string> SplitLines(string s) =>
        s.Trim().Split('\n').Select(l => l.TrimEnd('\r'));

    /// <summary>
    /// Splits an xUnit-style argument list into (label, value) pairs,
    /// respecting nesting so that <c>", "</c> inside strings, records, or
    /// collections is not treated as an argument separator.
    /// </summary>
    internal static List<(string label, string value)> ParseArgList(string argsStr)
    {
        var result  = new List<(string, string)>();
        var current = new StringBuilder();
        int depth   = 0;
        bool inStr  = false;

        void Flush()
        {
            var s = current.ToString().Trim();
            if (s.Length == 0) return;
            var colon = s.IndexOf(": ");
            // Only treat as "label: value" when the label is a plain identifier
            // (letters, digits, underscores). Values like records or HTML strings
            // often contain ": " inside them and must not be split here.
            if (colon > 0 && s[..colon].All(c => char.IsLetterOrDigit(c) || c == '_'))
                result.Add((s[..colon], s[(colon + 2)..]));
            else
                result.Add(("", s));
            current.Clear();
        }

        for (int i = 0; i < argsStr.Length; i++)
        {
            var c = argsStr[i];

            if (c == '"' && (i == 0 || argsStr[i - 1] != '\\'))
                inStr = !inStr;

            if (!inStr)
            {
                if (c is '(' or '[' or '{') depth++;
                else if (c is ')' or ']' or '}') depth--;
                else if (c == ',' && depth == 0 &&
                         i + 1 < argsStr.Length && argsStr[i + 1] == ' ')
                {
                    Flush();
                    i++;   // skip the space after the comma
                    continue;
                }
            }

            current.Append(c);
        }

        Flush();
        return result;
    }
}
