namespace Dotest;

/// <summary>A single test case result from the VSTest TranslationLayer.</summary>
public record TestResult(
    string ClassName,
    string Name,
    string FullName,
    string Outcome,      // "Passed" | "Failed" | "Skipped"
    TimeSpan Duration,
    string ErrorMessage,
    string StackTrace,
    string StdOut
);
