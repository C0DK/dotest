using Dotest;
using Xunit;

namespace dotest.Tests;

public class StripAnsiTests
{
    [Fact]
    public void StripAnsi_EmptyString_ReturnsEmpty()
    {
        Assert.Equal("", Renderer.StripAnsi(""));
    }

    [Fact]
    public void StripAnsi_NoEscapeCodes_Unchanged()
    {
        Assert.Equal("hello world", Renderer.StripAnsi("hello world"));
    }

    [Fact]
    public void StripAnsi_ResetCode_Removed()
    {
        Assert.Equal("hello", Renderer.StripAnsi("\x1b[0mhello"));
    }

    [Fact]
    public void StripAnsi_BasicColor_Removed()
    {
        Assert.Equal("green", Renderer.StripAnsi("\x1b[32mgreen\x1b[0m"));
    }

    [Fact]
    public void StripAnsi_256Color_Removed()
    {
        // 256-colour codes like those produced by Serilog: \x1b[38;5;15m
        Assert.Equal("white text", Renderer.StripAnsi("\x1b[38;5;15mwhite\x1b[0m text"));
    }

    [Fact]
    public void StripAnsi_MultipleSequencesOnOneLine_AllRemoved()
    {
        const string input    = "\x1b[1m\x1b[36mINFO\x1b[0m message \x1b[90m(detail)\x1b[0m";
        const string expected = "INFO message (detail)";
        Assert.Equal(expected, Renderer.StripAnsi(input));
    }

    [Fact]
    public void StripAnsi_PreservesNonAnsiContent()
    {
        // Brackets that are NOT escape sequences must be left intact.
        const string input = "[04/13/2026 14:18:22] Written 10000/28000 metrics";
        Assert.Equal(input, Renderer.StripAnsi(input));
    }
}
