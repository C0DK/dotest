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

    // ── complex values skip alignment ────────────────────────────────────────

    [Fact]
    public void AlignArguments_LongValue_SkipsAlignment()
    {
        // Values over 50 chars should be left as-is to avoid absurdly wide output.
        var longVal = new string('x', 51);
        var names   = new List<string> { $"M(v: 1)", $"M(v: {longVal})" };
        var result  = Renderer.AlignArguments(names);
        Assert.Equal($"M(v: 1)",       result[0]);
        Assert.Equal($"M(v: {longVal})", result[1]);
    }

    [Fact]
    public void AlignArguments_RecordValue_SkipsAlignment()
    {
        // Record values > 50 chars should skip alignment entirely.
        var names = new List<string>
        {
            "M(order: Order { Id = 1, Total = 10, Description = \"long desc\" })",
            "M(order: Order { Id = 2, Total = 9999, Description = \"other\" })",
        };
        var result = Renderer.AlignArguments(names);
        Assert.Equal(names, result);  // unchanged
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
