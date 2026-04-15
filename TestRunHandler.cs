using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

using VsTestResult = Microsoft.VisualStudio.TestPlatform.ObjectModel.TestResult;

namespace Dotest;

/// <summary>
/// Receives real-time events from <c>VsTestConsoleWrapper</c> and converts
/// them to <see cref="TestResult"/> instances via the supplied callback.
/// </summary>
internal sealed class TestRunHandler : ITestRunEventsHandler
{
    private readonly Action<TestResult> _onResult;

    internal TestRunHandler(Action<TestResult> onResult) => _onResult = onResult;

    public void HandleTestRunStatsChange(TestRunChangedEventArgs? args)
    {
        if (args?.NewTestResults is null) return;
        foreach (var r in args.NewTestResults)
            _onResult(Convert(r));
    }

    public void HandleTestRunComplete(
        TestRunCompleteEventArgs    completeArgs,
        TestRunChangedEventArgs?    lastChunk,
        ICollection<AttachmentSet>? attachments,
        ICollection<string>?        executorUris)
    {
        if (lastChunk?.NewTestResults is null) return;
        foreach (var r in lastChunk.NewTestResults)
            _onResult(Convert(r));
    }

    public void HandleLogMessage(TestMessageLevel level, string? message) { }
    public void HandleRawMessage(string rawMessage) { }
    public int  LaunchProcessWithDebuggerAttached(TestProcessStartInfo info) => -1;

    // ── Conversion ────────────────────────────────────────────────────────────

    private static TestResult Convert(VsTestResult r)
    {
        var fqn          = r.TestCase.FullyQualifiedName;
        var nameForSplit = fqn.Contains('(') ? fqn[..fqn.IndexOf('(')] : fqn;
        var lastDot      = nameForSplit.LastIndexOf('.');
        var className    = lastDot > 0 ? nameForSplit[..lastDot] : "";
        var name         = r.TestCase.DisplayName ?? (lastDot > 0 ? fqn[(lastDot + 1)..] : fqn);

        var outcome = r.Outcome switch
        {
            TestOutcome.Passed  => "Passed",
            TestOutcome.Failed  => "Failed",
            TestOutcome.Skipped => "Skipped",
            _                   => "Skipped",
        };

        var stdOut = string.Join("\n",
            r.Messages
             .Where(m => m.Category == TestResultMessage.StandardOutCategory)
             .Select(m => m.Text?.TrimEnd() ?? ""));

        return new TestResult(
            ClassName:    className,
            Name:         name,
            FullName:     fqn,
            Outcome:      outcome,
            Duration:     r.Duration,
            ErrorMessage: r.ErrorMessage ?? "",
            StackTrace:   r.ErrorStackTrace ?? "",
            StdOut:       stdOut
        );
    }
}
