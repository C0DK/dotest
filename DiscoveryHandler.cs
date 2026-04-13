using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;
#pragma warning disable CS0618 // ITestDiscoveryEventsHandler is the overload DiscoverTests accepts

namespace Dotest;

/// <summary>
/// Collects <see cref="TestCase"/> objects from a
/// <c>VsTestConsoleWrapper.DiscoverTests</c> call.
/// </summary>
internal sealed class DiscoveryHandler : ITestDiscoveryEventsHandler
{
    private readonly List<TestCase> _cases = [];

    internal IReadOnlyList<TestCase> Cases => _cases;

    public void HandleDiscoveredTests(IEnumerable<TestCase>? discovered)
    {
        if (discovered is not null)
            _cases.AddRange(discovered);
    }

    public void HandleDiscoveryComplete(long totalTests, IEnumerable<TestCase>? lastChunk, bool isAborted)
    {
        if (lastChunk is not null)
            _cases.AddRange(lastChunk);
    }

    public void HandleLogMessage(TestMessageLevel level, string? message) { }
    public void HandleRawMessage(string rawMessage) { }
}
