namespace Dotest;

/// <summary>A single test case result parsed from a TRX file.</summary>
public record TestResult(
    string ClassName,
    string Name,
    string FullName,
    string Outcome,      // "Passed" | "Failed" | "Skipped"
    string Duration,     // HH:MM:SS.NNNNNNN as reported by VSTest
    string ErrorMessage,
    string StackTrace,
    string StdOut
);
