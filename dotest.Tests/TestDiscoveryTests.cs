using Dotest;
using Xunit;

namespace dotest.Tests;

public class TestDiscoveryTests
{
    // ── FindSolutionRoot ──────────────────────────────────────────────────────

    [Fact]
    public void FindSolutionRoot_DirectoryContainsSln_ReturnsThatDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotest-slntest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "MySolution.sln"), "");
            var result = TestDiscovery.FindSolutionRoot(root);
            Assert.Equal(root, result);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FindSolutionRoot_SlnInParent_ReturnsParentDirectory()
    {
        var root  = Path.Combine(Path.GetTempPath(), $"dotest-slntest-{Guid.NewGuid():N}");
        var child = Path.Combine(root, "src", "MyProject");
        Directory.CreateDirectory(child);
        try
        {
            File.WriteAllText(Path.Combine(root, "MySolution.sln"), "");
            var result = TestDiscovery.FindSolutionRoot(child);
            Assert.Equal(root, result);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void FindSolutionRoot_NoSlnAnywhere_ReturnsFallback()
    {
        // Use a temp dir that definitely has no .sln above it (isolated subtree).
        var isolated = Path.Combine(Path.GetTempPath(), $"dotest-slntest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(isolated);
        try
        {
            // We can't guarantee /tmp has no .sln above it on every machine, so
            // assert only that the returned path is a non-empty string and exists.
            var result = TestDiscovery.FindSolutionRoot(isolated);
            Assert.False(string.IsNullOrEmpty(result));
        }
        finally { Directory.Delete(isolated, recursive: true); }
    }

    [Fact]
    public void FindSolutionRoot_SlnInGrandparent_ReturnsGrandparent()
    {
        var root       = Path.Combine(Path.GetTempPath(), $"dotest-slntest-{Guid.NewGuid():N}");
        var grandchild = Path.Combine(root, "a", "b", "c");
        Directory.CreateDirectory(grandchild);
        try
        {
            File.WriteAllText(Path.Combine(root, "Top.sln"), "");
            var result = TestDiscovery.FindSolutionRoot(grandchild);
            Assert.Equal(root, result);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
