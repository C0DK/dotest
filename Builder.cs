using System.Diagnostics;

namespace Dotest;

internal static class Builder
{
    /// <summary>
    /// Runs <c>dotnet build --nologo</c> and collects lines that contain
    /// <c>: error </c> into <paramref name="buildErrors"/>.
    /// </summary>
    /// <returns><c>true</c> if the build succeeded.</returns>
    internal static bool Build(List<string> buildErrors)
    {
        var psi = new ProcessStartInfo("dotnet", "build --nologo")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
        };

        using var proc       = Process.Start(psi)!;
        var       stderrTask = Task.Run(() => proc.StandardError.ReadToEnd());

        string? line;
        while ((line = proc.StandardOutput.ReadLine()) is not null)
        {
            if (line.Contains(": error "))
                buildErrors.Add(line);
        }

        proc.WaitForExit();
        stderrTask.Wait();

        return proc.ExitCode == 0;
    }
}
