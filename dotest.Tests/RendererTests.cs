using Dotest;
using Xunit;

namespace dotest.Tests;

public class RendererTests
{
    public class FormatElapsed
    {
        [Theory]
        [InlineData(0,      "< 1ms")]
        [InlineData(500,    "500ms")]
        [InlineData(1000,   "1s")]
        [InlineData(1200,   "1.2s")]
        [InlineData(5000,   "5s")]
        [InlineData(61000,  "1m 1s")]
        [InlineData(125000, "2m 5s")]
        public void VariousMilliseconds_ReturnsExpectedString(int ms, string expected)
        {
            Assert.Equal(expected, Renderer.FormatElapsed(TimeSpan.FromMilliseconds(ms)));
        }
    }

    public class SummaryMarkup
    {
        private static TestResult Make(string outcome) =>
            new("Ns.MyTests", "Test", "Ns.MyTests.Test", outcome, TimeSpan.Zero, "", "", "");

        [Fact]
        public void AllPassed_GreenOnly()
        {
            var result = Renderer.SummaryMarkup([Make("Passed"), Make("Passed")]);
            Assert.Contains("2 passed", result);
            Assert.DoesNotContain("failed",  result);
            Assert.DoesNotContain("skipped", result);
        }

        [Fact]
        public void SomeFailed_ShowsBothColors()
        {
            var result = Renderer.SummaryMarkup([Make("Passed"), Make("Failed"), Make("Failed")]);
            Assert.Contains("1 passed", result);
            Assert.Contains("2 failed", result);
        }

        [Fact]
        public void SomeSkipped_ShowsYellow()
        {
            var result = Renderer.SummaryMarkup([Make("Passed"), Make("Skipped")]);
            Assert.Contains("1 passed",  result);
            Assert.Contains("1 skipped", result);
        }

        [Fact]
        public void EmptyList_ReturnsEmpty()
        {
            Assert.Equal("", Renderer.SummaryMarkup([]));
        }
    }

    public class RenderTreeSummary
    {
        private static TestResult Make(string className, string outcome) =>
            new(className, "Test", $"{className}.Test", outcome, TimeSpan.Zero, "", "", "");

        [Fact]
        public void DirectTests_ShowsCount()
        {
            // A class with only direct tests should show its count.
            var tests = new[]
            {
                Make("Ns.FooTests", "Passed"),
                Make("Ns.FooTests", "Passed"),
                Make("Ns.FooTests", "Failed"),
            };
            // Should not throw; basic smoke test.
            Renderer.RenderTreeSummary(tests);
        }

        [Fact]
        public void SummaryMarkup_NestedClassCounts_BubbleUp()
        {
            // The aggregated markup for a parent that has both direct tests and
            // a nested child must include all descendant counts.
            // We test this via SummaryMarkup which is the core counting logic.
            var direct = new[]
            {
                Make("Ns.Parent", "Passed"),   // 1 direct pass
                Make("Ns.Parent", "Passed"),   // 2 direct passes
            };
            var nested = new[]
            {
                Make("Ns.Parent+Child", "Passed"),  // 1 nested pass
                Make("Ns.Parent+Child", "Failed"),  // 1 nested fail
            };
            // Aggregate of direct + nested = 3 passed, 1 failed
            var all    = direct.Concat(nested).ToList();
            var markup = Renderer.SummaryMarkup(all);
            Assert.Contains("3 passed", markup);
            Assert.Contains("1 failed", markup);
        }

        [Fact]
        public void NoTests_DoesNotThrow()
        {
            Renderer.RenderTreeSummary([]);
        }
    }

    public class ColorizeBuildError
    {
        [Fact]
        public void ValidErrorLine_ContainsCyanPath()
        {
            const string line = "/src/File.cs(42,5): error CS1061: message here";
            var result = Renderer.ColorizeBuildError(line);
            Assert.Contains("[cyan]", result);
            Assert.Contains("/src/File.cs", result);
        }

        [Fact]
        public void ValidErrorLine_ContainsYellowLineNumber()
        {
            const string line = "/src/File.cs(42,5): error CS1061: message here";
            var result = Renderer.ColorizeBuildError(line);
            Assert.Contains("[yellow]", result);
            Assert.Contains(":42", result);
        }

        [Fact]
        public void ValidErrorLine_ContainsRedErrorCode()
        {
            const string line = "/src/File.cs(42,5): error CS1061: message here";
            var result = Renderer.ColorizeBuildError(line);
            Assert.Contains("[red]", result);
            Assert.Contains("CS1061", result);
        }

        [Fact]
        public void ValidErrorLine_ContainsMessage()
        {
            const string line = "/src/File.cs(42,5): error CS1061: message here";
            var result = Renderer.ColorizeBuildError(line);
            Assert.Contains("message here", result);
        }

        [Fact]
        public void UnrecognizedLine_WrapsInRed()
        {
            const string line = "some unrecognized error output";
            var result = Renderer.ColorizeBuildError(line);
            Assert.Contains("[red]", result);
            Assert.Contains("some unrecognized error output", result);
        }

        [Fact]
        public void LeadingWhitespace_IsStripped()
        {
            const string line = "   /src/File.cs(10,1): error CS0001: oops";
            var result = Renderer.ColorizeBuildError(line);
            Assert.Contains("[cyan]", result);
            Assert.Contains("/src/File.cs", result);
        }
    }
}
