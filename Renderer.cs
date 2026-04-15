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

    // ── Summary tree (default) ───────────────────────────────────────────────

    /// <summary>
    /// Render a compact tree showing namespaces and classes with pass/fail
    /// counts at each leaf — no individual test lines.
    /// </summary>
    public static void RenderTreeSummary(IReadOnlyList<TestResult> tests)
    {
        var byClass = tests
            .GroupBy(t => string.IsNullOrEmpty(t.ClassName) ? "" : t.ClassName)
            .ToDictionary(g => g.Key, g => g.ToList());

        var namedClasses = byClass.Keys.Where(k => k.Length > 0).ToList();
        var commonParts  = FindCommonPrefixParts(namedClasses);
        var commonPrefix = string.Join(".", commonParts);

        if (commonPrefix.Length == 0)
        {
            foreach (var (className, classTests) in byClass
                .Where(kv => kv.Key.Length > 0).OrderBy(kv => kv.Key))
            {
                var anyF = classTests.Any(t => t.Outcome == "Failed");
                var anyS = !anyF && classTests.Any(t => t.Outcome is not "Passed" and not "Failed");
                var col  = anyF ? "red" : anyS ? "yellow" : "green";
                var t    = new Tree($"[{col}]{Esc(className)}[/] {SummaryMarkup(classTests)}");
                AnsiConsole.Write(t);
            }
        }
        else
        {
            var root = new HierNode(commonPrefix);
            foreach (var (className, classTests) in byClass.Where(kv => kv.Key.Length > 0))
            {
                var relative = className.Length > commonPrefix.Length
                    ? className[(commonPrefix.Length + 1)..] : "";
                var segments = relative.Length == 0
                    ? [] : relative.Split(['.', '+']);

                var node = root;
                foreach (var seg in segments)
                {
                    if (!node.Children.TryGetValue(seg, out var child))
                        node.Children[seg] = child = new HierNode(seg);
                    node = child;
                }
                node.Tests.AddRange(classTests);
            }

            var spectreTree = new Tree($"[cyan]{Esc(commonPrefix)}[/]");
            foreach (var child in root.Children.Values.OrderBy(n => n.Name))
                RenderHierNodeSummary(l => spectreTree.AddNode(l), child);
            AnsiConsole.Write(spectreTree);
        }

        if (byClass.TryGetValue("", out var unknownTests))
        {
            var unknownTree = new Tree($"[grey](unknown class)[/] {SummaryMarkup(unknownTests)}");
            AnsiConsole.Write(unknownTree);
        }
    }

    private static void RenderHierNodeSummary(Func<string, TreeNode> addNode, HierNode node)
    {
        var color = node.AnyFailed ? "red" : node.AnySkipped ? "yellow" : "green";
        var all   = node.AllTests.ToList();
        var suffix = all.Count > 0 ? " " + SummaryMarkup(all) : "";
        var spectreNode = addNode($"[{color}]{Esc(node.Name)}[/]{suffix}");

        foreach (var child in node.Children.Values.OrderBy(n => n.Name))
            RenderHierNodeSummary(l => spectreNode.AddNode(l), child);
    }

    internal static string SummaryMarkup(IReadOnlyList<TestResult> tests)
    {
        if (tests.Count == 0) return "";
        var passed  = tests.Count(t => t.Outcome == "Passed");
        var failed  = tests.Count(t => t.Outcome == "Failed");
        var skipped = tests.Count - passed - failed;
        var parts   = new List<string>();
        if (passed  > 0) parts.Add($"[green]{passed} passed[/]");
        if (failed  > 0) parts.Add($"[red]{failed} failed[/]");
        if (skipped > 0) parts.Add($"[yellow]{skipped} skipped[/]");
        return string.Join(", ", parts);
    }

    // ── Verbose tree ─────────────────────────────────────────────────────────

    /// <summary>
    /// Render tests grouped by class name using Spectre.Console Tree.
    /// Stdout lines appear as child nodes of each test.
    /// </summary>
    public static void RenderTree(IReadOnlyList<TestResult> tests)
    {
        var byClass = tests
            .GroupBy(t => string.IsNullOrEmpty(t.ClassName) ? "" : t.ClassName)
            .ToDictionary(g => g.Key, g => g.ToList());

        var termWidth = Console.IsOutputRedirected ? 120 : Console.WindowWidth;
        var panels    = new List<(string displayName, string stdout)>();

        var namedClasses = byClass.Keys.Where(k => k.Length > 0).ToList();
        var commonParts  = FindCommonPrefixParts(namedClasses);
        var commonPrefix = string.Join(".", commonParts);

        if (commonPrefix.Length == 0)
        {
            // No shared namespace: one flat tree per class (fallback)
            foreach (var (className, classTests) in byClass
                .Where(kv => kv.Key.Length > 0).OrderBy(kv => kv.Key))
            {
                var anyF = classTests.Any(t => t.Outcome == "Failed");
                var anyS = !anyF && classTests.Any(t => t.Outcome is not "Passed" and not "Failed");
                var col  = anyF ? "red" : anyS ? "yellow" : "green";
                var t    = new Tree($"[{col}]{Esc(className)}[/]");
                RenderLeafTests(l => t.AddNode(l), classTests, termWidth, panels, depth: 1);
                AnsiConsole.Write(t);
            }
        }
        else
        {
            // Build a logical hierarchy rooted at the common namespace prefix.
            var root = new HierNode(commonPrefix);
            foreach (var (className, classTests) in byClass.Where(kv => kv.Key.Length > 0))
            {
                // Strip the common prefix (+ the separating dot) to get the relative path.
                var relative = className.Length > commonPrefix.Length
                    ? className[(commonPrefix.Length + 1)..] : "";
                // Split on '.' (sub-namespace) and '+' (nested class).
                var segments = relative.Length == 0
                    ? [] : relative.Split(['.', '+']);

                var node = root;
                foreach (var seg in segments)
                {
                    if (!node.Children.TryGetValue(seg, out var child))
                        node.Children[seg] = child = new HierNode(seg);
                    node = child;
                }
                node.Tests.AddRange(classTests);
            }

            var spectreTree = new Tree($"[cyan]{Esc(commonPrefix)}[/]");
            foreach (var child in root.Children.Values.OrderBy(n => n.Name))
                RenderHierNode(l => spectreTree.AddNode(l), child, termWidth, panels, depth: 1);
            RenderLeafTests(l => spectreTree.AddNode(l), root.Tests, termWidth, panels, depth: 1);
            AnsiConsole.Write(spectreTree);
        }

        // Tests with no class name appear in a separate section.
        if (byClass.TryGetValue("", out var unknownTests))
        {
            var unknownTree = new Tree("[grey](unknown class)[/]");
            RenderLeafTests(l => unknownTree.AddNode(l), unknownTests, termWidth, panels, depth: 1);
            AnsiConsole.Write(unknownTree);
        }

        // Stdout panels after all trees so raw ANSI is not disrupted by tree connectors.
        foreach (var (name, stdout) in panels)
            RenderStdOutPanel(name, stdout);
    }

    // ── Hierarchical tree helpers ─────────────────────────────────────────────

    private sealed class HierNode(string name)
    {
        internal readonly string Name = name;
        internal readonly SortedDictionary<string, HierNode> Children = new();
        internal readonly List<TestResult> Tests = [];
        internal bool AnyFailed  => Tests.Any(t => t.Outcome == "Failed")
                                 || Children.Values.Any(c => c.AnyFailed);
        internal bool AnySkipped => !AnyFailed
                                 && (Tests.Any(t => t.Outcome is not "Passed" and not "Failed")
                                   || Children.Values.Any(c => c.AnySkipped));
        internal IEnumerable<TestResult> AllTests =>
            Tests.Concat(Children.Values.SelectMany(c => c.AllTests));
    }

    /// <summary>
    /// Renders an intermediate (non-leaf) hierarchy node — coloured name, no glyph.
    /// The status colour (green/yellow/red) summarises all descendants.
    /// </summary>
    private static void RenderHierNode(
        Func<string, TreeNode> addNode, HierNode node,
        int termWidth, List<(string, string)> panels, int depth)
    {
        var color      = node.AnyFailed ? "red" : node.AnySkipped ? "yellow" : "green";
        var spectreNode = addNode($"[{color}]{Esc(node.Name)}[/]");

        foreach (var child in node.Children.Values)
            RenderHierNode(l => spectreNode.AddNode(l), child, termWidth, panels, depth + 1);

        RenderLeafTests(l => spectreNode.AddNode(l), node.Tests, termWidth, panels, depth + 1);
    }

    /// <summary>
    /// Renders the individual test results (leaf nodes) for one class into the given parent.
    /// </summary>
    private static void RenderLeafTests(
        Func<string, TreeNode> addNode, List<TestResult> tests,
        int termWidth, List<(string, string)> panels, int depth)
    {
        if (tests.Count == 0) return;

        var classPrefix  = tests[0].ClassName.Length > 0 ? tests[0].ClassName + "." : "";
        var rawNames     = tests.Select(t => classPrefix.Length > 0
            && t.Name.StartsWith(classPrefix, StringComparison.Ordinal)
            ? t.Name[classPrefix.Length..] : t.Name).ToList();
        var displayNames = AlignArguments(rawNames);

        // Each depth level adds 4 chars of tree connector ("│   " or "└── ").
        // Fixed overhead: 1 (glyph) + 1 (space) + 6 (duration) + 1 (space) + 2 (parens) + 2 (margin).
        var fixedOverhead = 4 * depth + 13;

        for (int ii = 0; ii < tests.Count; ii++)
        {
            var t           = tests[ii];
            var displayName = displayNames[ii];
            var rawName     = rawNames[ii];

            var color = t.Outcome switch { "Passed" => "green", "Failed" => "red", _ => "yellow" };
            var glyph = t.Outcome switch
            {
                "Passed" => "\u2713",   // ✓
                "Failed" => "\u2717",   // ✗
                _        => "\u25cb",   // ○
            };
            var dur = FormatElapsed(t.Duration).PadLeft(6);

            var dParen      = displayName.IndexOf('(');
            var methodPart  = dParen >= 0 ? displayName[..dParen].TrimEnd() : displayName.Trim();
            var argsAligned = dParen >= 0 ? displayName[(dParen + 1)..^1].Trim() : "";
            var rParen      = rawName.IndexOf('(');
            var argsRaw     = rParen >= 0
                            ? WhitespaceRx.Replace(rawName[(rParen + 1)..^1].Trim(), " ").Trim()
                            : "";

            var argBudget = Math.Max(10, termWidth - fixedOverhead - methodPart.Length);
            var argsInner = argsAligned.Length <= argBudget ? argsAligned
                          : argsRaw.Length    <= argBudget ? argsRaw
                          : argsRaw[..(argBudget - 1)] + "…";

            var nodeText = $"[{color}]{Esc(glyph)}[/] [grey]{Esc(dur)}[/] {Esc(methodPart)}"
                         + (argsInner.Length > 0 ? $"([silver]{Esc(argsInner)}[/])" : "");
            addNode(nodeText);

            if (!string.IsNullOrWhiteSpace(t.StdOut))
                panels.Add((displayName, t.StdOut));
        }
    }

    /// <summary>
    /// Returns the longest common dot-separated prefix across all class names.
    /// For a single class, strips the last segment so the class name itself
    /// appears as a child node rather than the tree root.
    /// </summary>
    private static string[] FindCommonPrefixParts(IReadOnlyList<string> classNames)
    {
        if (classNames.Count == 0) return [];
        var parts  = classNames.Select(n => n.Split('.')).ToArray();
        int maxLen = parts.Min(p => p.Length);
        int i      = 0;
        while (i < maxLen && parts.All(p => p[i] == parts[0][i]))
            i++;
        // For a single name the loop consumes all parts — peel the last one off
        // so the class sits as a child of its namespace, not as the root itself.
        if (classNames.Count == 1)
            i = Math.Max(0, i - 1);
        return parts[0][..i];
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

    private static readonly Regex WhitespaceRx = new(@"\s+", RegexOptions.Compiled);
    private static string Esc(string s)          => Markup.Escape(s);
    internal static string StripAnsi(string s)   => AnsiRx.Replace(s, "");

    // Single-line values are kept as-is. Multi-line values (e.g. HtmlDescription
    // containing \n) are flattened to one line. Actual display truncation is done
    // by RenderTree based on terminal width.
    internal static string Truncate(string s)
    {
        if (!s.Contains('\n') && !s.Contains('\r'))
            return s;
        return WhitespaceRx.Replace(s.Trim(), " ");
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
