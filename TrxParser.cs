using System.Xml.Linq;

namespace Dotest;

/// <summary>
/// Parses Visual Studio Test Results (TRX) XML files produced by dotnet test
/// with --logger "trx;LogFilePrefix=res".
///
/// TRX is the VSTest standard result format — it is written by dotnet test
/// regardless of which test adapter (NUnit, xUnit, MSTest, …) is in use, so
/// this parser gives correct, adapter-agnostic results.
///
/// The ideal future alternative is the VSTest TranslationLayer
/// (VsTestConsoleWrapper), which delivers real-time TestResult events
/// in-process without any file parsing. That approach requires locating
/// vstest.console.dll inside the installed SDK and wiring up event handlers,
/// so TRX parsing is used here as the simpler initial implementation.
///
/// TRX schema overview:
///   TestRun/Results/UnitTestResult  – outcome, duration, per-test output
///   TestRun/TestDefinitions/UnitTest/TestMethod[@className]  – class lookup via testId
/// </summary>
public static class TrxParser
{
    public static List<TestResult> ParseDirectory(string dir)
    {
        string[] files;
        try { files = Directory.GetFiles(dir, "*.trx"); }
        catch { return []; }

        if (files.Length == 0) return [];

        var results = new List<TestResult>();
        foreach (var file in files)
            results.AddRange(ParseFile(file));
        return results;
    }

    /// <summary>Parse TRX XML from a string. Primarily for unit testing.</summary>
    internal static List<TestResult> ParseContent(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return []; }
        return ParseDocument(doc);
    }

    private static List<TestResult> ParseFile(string path)
    {
        XDocument doc;
        try { doc = XDocument.Load(path); }
        catch { return []; }
        return ParseDocument(doc);
    }

    private static List<TestResult> ParseDocument(XDocument doc)
    {
        var root = doc.Root;
        if (root is null) return [];

        // Ignore namespace by matching LocalName – handles any xmlns variant.
        var resultsEl = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Results");
        if (resultsEl is null) return [];

        var defsEl = root.Elements().FirstOrDefault(e => e.Name.LocalName == "TestDefinitions");

        // Build testId → className map from TestDefinitions.
        var classMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (defsEl is not null)
        {
            foreach (var unitTest in defsEl.Elements().Where(e => e.Name.LocalName == "UnitTest"))
            {
                var id = unitTest.Attribute("id")?.Value;
                var method = unitTest.Elements().FirstOrDefault(e => e.Name.LocalName == "TestMethod");
                var className = method?.Attribute("className")?.Value ?? "";
                if (!string.IsNullOrEmpty(id))
                    classMap[id] = className;
            }
        }

        var results = new List<TestResult>();
        foreach (var r in resultsEl.Elements().Where(e => e.Name.LocalName == "UnitTestResult"))
        {
            var testId   = r.Attribute("testId")?.Value ?? "";
            var testName = r.Attribute("testName")?.Value ?? "";
            var outcome  = r.Attribute("outcome")?.Value ?? "";
            var duration = r.Attribute("duration")?.Value ?? "";

            var className = classMap.TryGetValue(testId, out var cn) ? cn : "";
            var fullName  = !string.IsNullOrEmpty(className)
                ? $"{className}.{testName}"
                : testName;

            var normalizedOutcome = outcome switch
            {
                "Passed" => "Passed",
                "Failed" => "Failed",
                _        => "Skipped",
            };

            string errorMessage = "", stackTrace = "", stdOut = "";

            var outputEl = r.Elements().FirstOrDefault(e => e.Name.LocalName == "Output");
            if (outputEl is not null)
            {
                var stdOutEl = outputEl.Elements().FirstOrDefault(e => e.Name.LocalName == "StdOut");
                stdOut = stdOutEl?.Value ?? "";

                var errorInfoEl = outputEl.Elements().FirstOrDefault(e => e.Name.LocalName == "ErrorInfo");
                if (errorInfoEl is not null)
                {
                    var msgEl = errorInfoEl.Elements().FirstOrDefault(e => e.Name.LocalName == "Message");
                    errorMessage = msgEl?.Value ?? "";

                    var stEl = errorInfoEl.Elements().FirstOrDefault(e => e.Name.LocalName == "StackTrace");
                    stackTrace = stEl?.Value ?? "";
                }
            }

            results.Add(new TestResult(
                ClassName:    className,
                Name:         testName,
                FullName:     fullName,
                Outcome:      normalizedOutcome,
                Duration:     duration,
                ErrorMessage: errorMessage,
                StackTrace:   stackTrace,
                StdOut:       stdOut
            ));
        }

        return results;
    }
}
