using Dotest;
using Xunit;

namespace dotest.Tests;

public class TrxParserTests
{
    // Minimal well-formed TRX used across tests.
    private const string PassingTrx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="11111111-1111-1111-1111-111111111111"
                            testName="ShouldPass"
                            outcome="Passed"
                            duration="00:00:00.0180000" />
          </Results>
          <TestDefinitions>
            <UnitTest name="ShouldPass" id="11111111-1111-1111-1111-111111111111">
              <TestMethod className="My.Namespace.SomeTests" name="ShouldPass" />
            </UnitTest>
          </TestDefinitions>
        </TestRun>
        """;

    private const string FailingTrx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="22222222-2222-2222-2222-222222222222"
                            testName="ShouldFail"
                            outcome="Failed"
                            duration="00:00:01.2345678">
              <Output>
                <StdOut>some test output</StdOut>
                <ErrorInfo>
                  <Message>Expected: 42 But was: 41</Message>
                  <StackTrace>at SomeTests.ShouldFail() in /src/Tests.cs:line 99</StackTrace>
                </ErrorInfo>
              </Output>
            </UnitTestResult>
          </Results>
          <TestDefinitions>
            <UnitTest name="ShouldFail" id="22222222-2222-2222-2222-222222222222">
              <TestMethod className="My.Namespace.SomeTests" name="ShouldFail" />
            </UnitTest>
          </TestDefinitions>
        </TestRun>
        """;

    private const string SkippedTrx = """
        <?xml version="1.0" encoding="UTF-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results>
            <UnitTestResult testId="33333333-3333-3333-3333-333333333333"
                            testName="ShouldSkip"
                            outcome="NotExecuted"
                            duration="00:00:00.0000000" />
          </Results>
          <TestDefinitions>
            <UnitTest name="ShouldSkip" id="33333333-3333-3333-3333-333333333333">
              <TestMethod className="My.Namespace.SomeTests" name="ShouldSkip" />
            </UnitTest>
          </TestDefinitions>
        </TestRun>
        """;

    [Fact]
    public void ParseContent_PassingTest_ReturnsCorrectOutcome()
    {
        var results = TrxParser.ParseContent(PassingTrx);
        Assert.Single(results);
        Assert.Equal("Passed", results[0].Outcome);
    }

    [Fact]
    public void ParseContent_PassingTest_ReturnsCorrectName()
    {
        var results = TrxParser.ParseContent(PassingTrx);
        Assert.Equal("ShouldPass", results[0].Name);
    }

    [Fact]
    public void ParseContent_PassingTest_BuildsFullNameFromClassAndMethod()
    {
        var results = TrxParser.ParseContent(PassingTrx);
        Assert.Equal("My.Namespace.SomeTests.ShouldPass", results[0].FullName);
    }

    [Fact]
    public void ParseContent_PassingTest_ReturnsClassName()
    {
        var results = TrxParser.ParseContent(PassingTrx);
        Assert.Equal("My.Namespace.SomeTests", results[0].ClassName);
    }

    [Fact]
    public void ParseContent_PassingTest_ReturnsDuration()
    {
        var results = TrxParser.ParseContent(PassingTrx);
        Assert.Equal("00:00:00.0180000", results[0].Duration);
    }

    [Fact]
    public void ParseContent_FailingTest_ReturnsFailedOutcome()
    {
        var results = TrxParser.ParseContent(FailingTrx);
        Assert.Single(results);
        Assert.Equal("Failed", results[0].Outcome);
    }

    [Fact]
    public void ParseContent_FailingTest_ReturnsErrorMessage()
    {
        var results = TrxParser.ParseContent(FailingTrx);
        Assert.Equal("Expected: 42 But was: 41", results[0].ErrorMessage);
    }

    [Fact]
    public void ParseContent_FailingTest_ReturnsStackTrace()
    {
        var results = TrxParser.ParseContent(FailingTrx);
        Assert.Contains("ShouldFail", results[0].StackTrace);
        Assert.Contains("line 99", results[0].StackTrace);
    }

    [Fact]
    public void ParseContent_FailingTest_ReturnsStdOut()
    {
        var results = TrxParser.ParseContent(FailingTrx);
        Assert.Equal("some test output", results[0].StdOut.Trim());
    }

    [Fact]
    public void ParseContent_SkippedTest_ReturnsSkippedOutcome()
    {
        var results = TrxParser.ParseContent(SkippedTrx);
        Assert.Single(results);
        Assert.Equal("Skipped", results[0].Outcome);
    }

    [Fact]
    public void ParseContent_EmptyXml_ReturnsEmptyList()
    {
        var results = TrxParser.ParseContent("<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"></TestRun>");
        Assert.Empty(results);
    }

    [Fact]
    public void ParseContent_InvalidXml_ReturnsEmptyList()
    {
        var results = TrxParser.ParseContent("this is not xml");
        Assert.Empty(results);
    }

    [Fact]
    public void ParseContent_NoNamespace_StillParsesCorrectly()
    {
        // Namespace should be ignored by LocalName matching.
        const string noNs = """
            <TestRun>
              <Results>
                <UnitTestResult testId="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
                                testName="ShouldWork"
                                outcome="Passed"
                                duration="00:00:00.1000000" />
              </Results>
              <TestDefinitions>
                <UnitTest name="ShouldWork" id="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa">
                  <TestMethod className="Some.Class" name="ShouldWork" />
                </UnitTest>
              </TestDefinitions>
            </TestRun>
            """;
        var results = TrxParser.ParseContent(noNs);
        Assert.Single(results);
        Assert.Equal("Some.Class.ShouldWork", results[0].FullName);
    }

    [Fact]
    public void ParseContent_MissingTestDefinitions_UsesTestNameAsFullName()
    {
        const string noDefsXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testId="bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"
                                testName="OrphanedTest"
                                outcome="Passed"
                                duration="00:00:00.0500000" />
              </Results>
            </TestRun>
            """;
        var results = TrxParser.ParseContent(noDefsXml);
        Assert.Single(results);
        Assert.Equal("OrphanedTest", results[0].FullName);
        Assert.Equal("", results[0].ClassName);
    }

    [Fact]
    public void ParseDirectory_EmptyDirectory_ReturnsEmptyList()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dotest-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var results = TrxParser.ParseDirectory(dir);
            Assert.Empty(results);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ParseDirectory_NonExistentDirectory_ReturnsEmptyList()
    {
        var results = TrxParser.ParseDirectory("/nonexistent/path/that/does/not/exist");
        Assert.Empty(results);
    }

    [Fact]
    public void ParseDirectory_DirectoryWithTrxFile_ReturnsResults()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dotest-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "res.trx"), PassingTrx);
            var results = TrxParser.ParseDirectory(dir);
            Assert.Single(results);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
