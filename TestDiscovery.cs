using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Dotest;

internal static class TestDiscovery
{
    /// <summary>
    /// Returns the paths of compiled test assemblies found under the solution
    /// root (or CWD if no <c>.sln</c> is found).  A project is considered a
    /// test project when its <c>.csproj</c> references
    /// <c>Microsoft.NET.Test.Sdk</c>.
    /// </summary>
    internal static List<string> FindTestAssemblies()
    {
        var assemblies = new List<string>();
        var root       = FindSolutionRoot();

        string[] projFiles;
        try { projFiles = Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories); }
        catch { return assemblies; }

        foreach (var proj in projFiles)
        {
            try
            {
                var content = File.ReadAllText(proj);
                if (!content.Contains("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase))
                    continue;

                var psi = new ProcessStartInfo("dotnet")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    UseShellExecute        = false,
                };
                psi.ArgumentList.Add("msbuild");
                psi.ArgumentList.Add(proj);
                psi.ArgumentList.Add("-getProperty:TargetPath");
                psi.ArgumentList.Add("-nologo");
                psi.ArgumentList.Add("-verbosity:quiet");

                using var proc = Process.Start(psi)!;
                var path = proc.StandardOutput.ReadToEnd().Trim();
                proc.StandardError.ReadToEnd();
                proc.WaitForExit();

                if (File.Exists(path))
                    assemblies.Add(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"warning: skipping {proj}: {ex.Message}");
            }
        }

        return assemblies;
    }

    /// <summary>
    /// Locates <c>vstest.console.dll</c> inside the active .NET SDK by
    /// parsing the "Base Path" line from <c>dotnet --info</c>.
    /// </summary>
    internal static string FindVsTestConsolePath()
    {
        var psi = new ProcessStartInfo("dotnet", "--info")
        {
            RedirectStandardOutput = true,
            UseShellExecute        = false,
        };
        using var proc   = Process.Start(psi)!;
        var       output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();

        var match = Regex.Match(output, @"Base Path:\s+(.+)");
        if (!match.Success)
            throw new InvalidOperationException(
                "Cannot determine dotnet SDK base path from 'dotnet --info'.");

        var sdkPath = match.Groups[1].Value.Trim().TrimEnd('/', '\\');
        return Path.Combine(sdkPath, "vstest.console.dll");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks up from the current directory until a directory containing a
    /// <c>.sln</c> file is found.  Falls back to the current directory.
    /// </summary>
    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
