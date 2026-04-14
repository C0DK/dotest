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
}
