using System.Diagnostics;

namespace BookWheel.Tests.Scripts;

public sealed class CheckVulnerablePackagesScriptTests
{
    private static readonly string ScriptPath = LocateScript();
    private static readonly string BashExecutable = ResolveBashExecutable();

    [Fact]
    public async Task ExitsZero_When_Report_Has_No_Vulnerable_Packages()
    {
        const string cleanReport = """
            Project 'BookWheel' has the following source(s) for NuGet packages:
                https://api.nuget.org/v3/index.json

            The given project `BookWheel` has no vulnerable packages given the current sources.
            """;

        var result = await RunScriptAsync(cleanReport);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("no vulnerable packages", result.StdOut);
    }

    [Fact]
    public async Task ExitsOne_When_Report_Has_Vulnerable_Packages()
    {
        const string vulnerableReport = """
            Project 'BookWheel' has the following source(s) for NuGet packages:
                https://api.nuget.org/v3/index.json

            The following sources were used:
                https://api.nuget.org/v3/index.json

            Project `BookWheel` has the following vulnerable packages
               [net8.0]:
               Top-level Package      Requested   Resolved   Severity   Advisory URL
               > Newtonsoft.Json      12.0.1      12.0.1     High       https://github.com/advisories/GHSA-example
            """;

        var result = await RunScriptAsync(vulnerableReport);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("vulnerable packages", result.StdOut);
    }

    private static async Task<(int ExitCode, string StdOut)> RunScriptAsync(string stdin)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = BashExecutable,
            ArgumentList = { ScriptPath },
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start bash process for script test.");

        await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();

        var stdOut = await process.StandardOutput.ReadToEndAsync();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdOut);
    }

    private static string LocateScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "scripts", "check-vulnerable-packages.sh");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate scripts/check-vulnerable-packages.sh relative to the test assembly.");
    }

    private static string ResolveBashExecutable()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "bash";
        }

        // Plain "bash" resolves via the native Windows PATH, where Windows' own WSL launcher stub
        // (C:\Windows\System32\bash.exe) shadows Git for Windows' real bash.exe. That stub fails
        // with a WSL error when no distro is registered, so locate Git for Windows' bash.exe
        // explicitly by finding git.exe on PATH (unambiguous - no WSL stub competes for that name)
        // and deriving bash.exe relative to the Git installation root.
        var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);
        foreach (var entry in pathEntries)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            if (!File.Exists(Path.Combine(entry, "git.exe")))
            {
                continue;
            }

            var gitRoot = new DirectoryInfo(entry).Parent;
            var candidateBash = gitRoot is null ? null : Path.Combine(gitRoot.FullName, "bin", "bash.exe");
            if (candidateBash is not null && File.Exists(candidateBash))
            {
                return candidateBash;
            }
        }

        throw new FileNotFoundException(
            "Could not locate Git for Windows' bash.exe. Ensure Git for Windows is installed and its 'cmd' directory is on PATH.");
    }
}
