using Dotest;
using Xunit;

namespace dotest.Tests;

public class RendererTests
{
    // ── FormatDuration ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("00:00:00.0000000", "< 1ms")]
    [InlineData("00:00:00.0010000", "1ms")]
    [InlineData("00:00:00.0180000", "18ms")]
    [InlineData("00:00:00.9990000", "999ms")]
    [InlineData("00:00:01.0000000", "1s")]
    [InlineData("00:00:01.2000000", "1.2s")]
    [InlineData("00:00:05.3500000", "5.3s")]
    [InlineData("00:00:59.9000000", "59.9s")]
    [InlineData("00:01:00.0000000", "1m 0s")]
    [InlineData("00:01:05.0000000", "1m 5s")]
    [InlineData("00:03:45.0000000", "3m 45s")]
    public void FormatDuration_KnownInputs_ReturnsExpectedString(string input, string expected)
    {
        Assert.Equal(expected, Renderer.FormatDuration(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-duration")]
    public void FormatDuration_InvalidInput_ReturnsEmpty(string? input)
    {
        Assert.Equal("", Renderer.FormatDuration(input));
    }

    // ── FormatElapsed ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0,      "< 1ms")]
    [InlineData(500,    "500ms")]
    [InlineData(1000,   "1s")]
    [InlineData(1200,   "1.2s")]
    [InlineData(5000,   "5s")]
    [InlineData(61000,  "1m 1s")]
    [InlineData(125000, "2m 5s")]
    public void FormatElapsed_VariousMilliseconds_ReturnsExpectedString(int ms, string expected)
    {
        Assert.Equal(expected, Renderer.FormatElapsed(TimeSpan.FromMilliseconds(ms)));
    }

    // ── ColorizeBuildError ────────────────────────────────────────────────────

    [Fact]
    public void ColorizeBuildError_ValidErrorLine_ContainsCyanPath()
    {
        const string line = "/src/File.cs(42,5): error CS1061: message here";
        var result = Renderer.ColorizeBuildError(line);
        Assert.Contains("[cyan]", result);
        Assert.Contains("/src/File.cs", result);
    }

    [Fact]
    public void ColorizeBuildError_ValidErrorLine_ContainsYellowLineNumber()
    {
        const string line = "/src/File.cs(42,5): error CS1061: message here";
        var result = Renderer.ColorizeBuildError(line);
        Assert.Contains("[yellow]", result);
        Assert.Contains(":42", result);
    }

    [Fact]
    public void ColorizeBuildError_ValidErrorLine_ContainsRedErrorCode()
    {
        const string line = "/src/File.cs(42,5): error CS1061: message here";
        var result = Renderer.ColorizeBuildError(line);
        Assert.Contains("[red]", result);
        Assert.Contains("CS1061", result);
    }

    [Fact]
    public void ColorizeBuildError_ValidErrorLine_ContainsMessage()
    {
        const string line = "/src/File.cs(42,5): error CS1061: message here";
        var result = Renderer.ColorizeBuildError(line);
        Assert.Contains("message here", result);
    }

    [Fact]
    public void ColorizeBuildError_UnrecognizedLine_WrapsInRed()
    {
        const string line = "some unrecognized error output";
        var result = Renderer.ColorizeBuildError(line);
        Assert.Contains("[red]", result);
        Assert.Contains("some unrecognized error output", result);
    }

    [Fact]
    public void ColorizeBuildError_LeadingWhitespace_IsStripped()
    {
        const string line = "   /src/File.cs(10,1): error CS0001: oops";
        var result = Renderer.ColorizeBuildError(line);
        // Should parse correctly even with leading whitespace.
        Assert.Contains("[cyan]", result);
        Assert.Contains("/src/File.cs", result);
    }
}
