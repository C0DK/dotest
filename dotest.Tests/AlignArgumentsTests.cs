using Dotest;
using Xunit;

namespace dotest.Tests;

public class AlignArgumentsTests
{
    // ── single entry – nothing to align ──────────────────────────────────────

    [Fact]
    public void AlignArguments_SingleEntry_Unchanged()
    {
        var result = Renderer.AlignArguments(["Method(x: 1)"]);
        Assert.Equal("Method(x: 1)", result[0]);
    }

    [Fact]
    public void AlignArguments_NoParens_Unchanged()
    {
        var names  = new List<string> { "TestA", "TestB" };
        var result = Renderer.AlignArguments(names);
        Assert.Equal(names, result);
    }

    // ── numeric value alignment ───────────────────────────────────────────────

    [Fact]
    public void AlignArguments_TwoVariants_RightAlignsNumericValue()
    {
        var result = Renderer.AlignArguments(["M(x: 1)", "M(x: 100)"]);
        Assert.Equal("M(x:   1)", result[0]);
        Assert.Equal("M(x: 100)", result[1]);
    }

    [Fact]
    public void AlignArguments_MultipleParams_AlignsEachColumnIndependently()
    {
        var result = Renderer.AlignArguments(
        [
            "M(a: 1, b: \"hi\")",
            "M(a: 100, b: \"hello\")",
        ]);
        Assert.Equal("M(a:   1, b:    \"hi\")", result[0]);
        Assert.Equal("M(a: 100, b: \"hello\")", result[1]);
    }

    // ── method grouping ───────────────────────────────────────────────────────

    [Fact]
    public void AlignArguments_DifferentMethods_AlignedWithinEachGroup()
    {
        var result = Renderer.AlignArguments(
        [
            "MethodA(x: 1)",
            "MethodB(x: 100)",  // only one variant – no padding
            "MethodA(x: 10)",
        ]);
        Assert.Equal("MethodA(x:  1)", result[0]);
        Assert.Equal("MethodB(x: 100)", result[1]);
        Assert.Equal("MethodA(x: 10)", result[2]);
    }

    // ── mixed: theories and plain tests side-by-side ─────────────────────────

    [Fact]
    public void AlignArguments_MixedParensAndNoParens_HandledSeparately()
    {
        var result = Renderer.AlignArguments(
        [
            "Theory(n: 1)",
            "PlainTest",
            "Theory(n: 42)",
        ]);
        Assert.Equal("Theory(n:  1)", result[0]);
        Assert.Equal("PlainTest",     result[1]);
        Assert.Equal("Theory(n: 42)", result[2]);
    }

    // ── complex values are truncated, then aligned ───────────────────────────

    [Fact]
    public void AlignArguments_LongMultilineValue_TruncatedThenAligned()
    {
        // Multi-line values are flattened and truncated to MaxArgValueLen (40) before aligning.
        var longVal = "line one\n" + new string('x', 42);
        var names   = new List<string> { "M(v: 1)", $"M(v: {longVal})" };
        var result  = Renderer.AlignArguments(names);
        // Short value padded to match truncated long value width (40 chars)
        Assert.EndsWith("M(v: " + new string(' ', 39) + "1)", result[0]);
        Assert.Contains("…",     result[1]);
        Assert.DoesNotContain(longVal, result[1]);
    }

    [Fact]
    public void AlignArguments_RecordValue_SingleLineNotTruncated()
    {
        // Record values with internal ", " must be parsed as a single argument (not split).
        // Single-line values are never truncated, even if they exceed MaxArgValueLen.
        var names = new List<string>
        {
            "M(order: Order { Id = 1, Total = 10 })",
            "M(order: Order { Id = 2, Total = 9999 })",
        };
        var result = Renderer.AlignArguments(names);
        // Both start with "M(order: " and must NOT be truncated (single-line values are kept as-is).
        Assert.All(result, r => Assert.StartsWith("M(order: ", r));
        Assert.DoesNotContain("…", result[0]);
        Assert.DoesNotContain("…", result[1]);
    }

    // ── Truncate ──────────────────────────────────────────────────────────────

    [Fact]
    public void Truncate_ShortString_Unchanged()
    {
        Assert.Equal("hello", Renderer.Truncate("hello"));
    }

    [Fact]
    public void Truncate_ExactlyMaxLen_Unchanged()
    {
        var s = new string('a', 40);
        Assert.Equal(s, Renderer.Truncate(s));
    }

    [Fact]
    public void Truncate_MultilineLongerThanMax_EndsWithEllipsis()
    {
        // Multi-line values that flatten to more than 40 chars get truncated with '…'.
        var s      = "aaaaaaaaaa\naaaaaaaaaa\naaaaaaaaaa\naaaaaaaaaa\naaa";  // flattens to 43 chars
        var result = Renderer.Truncate(s);
        Assert.Equal(40, result.Length);
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void Truncate_SingleLineLongerThanMax_Unchanged()
    {
        // Single-line values are never truncated, regardless of length.
        var s      = new string('a', 41);
        var result = Renderer.Truncate(s);
        Assert.Equal(s, result);
    }

    [Fact]
    public void Truncate_MultilineValue_CollapsedToSingleLine()
    {
        // HtmlDescription values may contain \n — they must become a single line.
        var result = Renderer.Truncate("line one\nline two\nline three");
        Assert.DoesNotContain("\n", result);
        Assert.Equal("line one line two line three", result);
    }

    [Fact]
    public void Truncate_ExcessiveInternalWhitespace_SingleLine_Unchanged()
    {
        // Single-line values are returned as-is; whitespace is only collapsed for multi-line values.
        var s = "Reason:                   reason";
        Assert.Equal(s, Renderer.Truncate(s));
    }

    [Fact]
    public void Truncate_MultilineExcessiveWhitespace_Collapsed()
    {
        // Multi-line values have whitespace collapsed when flattened.
        Assert.Equal("Reason: reason", Renderer.Truncate("Reason:\n                  reason"));
    }

    // ── ParseArgList: label detection ─────────────────────────────────────────

    [Fact]
    public void ParseArgList_LabelWithColonInsideRecord_NotSplitAsLabel()
    {
        // "HtmlDescription = Reason: reason" inside a record — the "Reason:" part
        // must NOT be treated as a label because what precedes ": " is not an identifier.
        var args = Renderer.ParseArgList(
            "ManuallyMarkedAsCancelled { Reason = reason, HtmlDescription = Note: hi }");
        Assert.Single(args);
        Assert.Equal("", args[0].label);  // no identifier label found
    }

    [Fact]
    public void ParseArgList_ValidIdentifierLabel_RecognisedCorrectly()
    {
        var args = Renderer.ParseArgList("input: 42");
        Assert.Single(args);
        Assert.Equal("input", args[0].label);
        Assert.Equal("42",    args[0].value);
    }

    // ── ParseArgList: nested structures ──────────────────────────────────────

    [Fact]
    public void ParseArgList_NestedBraces_NotSplitAtInnerComma()
    {
        var args = Renderer.ParseArgList("order: Order { Id = 1, Total = 10 }");
        Assert.Single(args);
        Assert.Equal("order",                       args[0].label);
        Assert.Equal("Order { Id = 1, Total = 10 }", args[0].value);
    }

    [Fact]
    public void ParseArgList_QuotedStringWithComma_NotSplit()
    {
        var args = Renderer.ParseArgList("s: \"hello, world\", n: 42");
        Assert.Equal(2, args.Count);
        Assert.Equal("\"hello, world\"", args[0].value);
        Assert.Equal("42",               args[1].value);
    }

    [Fact]
    public void ParseArgList_SimpleArgs_SplitCorrectly()
    {
        var args = Renderer.ParseArgList("ms: 1000, expected: \"1s\"");
        Assert.Equal(2,       args.Count);
        Assert.Equal("1000",  args[0].value);
        Assert.Equal("\"1s\"", args[1].value);
    }
}
