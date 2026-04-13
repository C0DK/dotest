using Dotest;
using Xunit;

namespace dotest.Tests;

public class RendererTests
{
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
        Assert.Contains("[cyan]", result);
        Assert.Contains("/src/File.cs", result);
    }
}
