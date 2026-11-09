using System.Diagnostics;
using Aholo.Core;

namespace Aholo.Tests;

[Collection("USD runtime process isolation")]
public sealed class UsdRuntimeTests
{
    [Fact]
    public async Task MissingRuntimeGivesActionableStatusWithoutLaunchingAnything()
    {
        string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "python");
        UsdRuntimeStatus status = await UsdRuntime.CheckAsync(missing);
        Assert.False(status.Ready); Assert.Equal(missing, status.PythonPath); Assert.Contains("not installed", status.Detail);
    }
    [Fact]
    public async Task AlreadyCancelledProbeDoesNotInspectOrLaunchRuntime()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UsdRuntime.CheckAsync("unused", cancellation.Token));
    }
    [Theory]
    [InlineData(false, "/tmp/team's plugin/setup-usd.py", "python3.12 '/tmp/team'\"'\"'s plugin/setup-usd.py'")]
    [InlineData(true, "C:\\Users\\Team Name\\setup-usd.py", "py -3.12 'C:\\Users\\Team Name\\setup-usd.py'")]
    public void SetupCommandQuotesSpacesAndShellCharacters(bool windows, string path, string expected)
    {
        Assert.Equal(expected, UsdRuntime.SetupCommand(path, windows));
        Assert.Equal(expected + " --check", UsdRuntime.SetupCommand(path, windows, true));
    }
    [Theory]
    [InlineData("unsafe\ncommand")]
    [InlineData("unsafe\rcommand")]
    public void SetupCommandRejectsMultilinePaths(string path) => Assert.Throws<ArgumentException>(() => UsdRuntime.SetupCommand(path, false));
    [Fact]
    public void SetupScriptCanBeFoundFromBuiltSourceTree() => Assert.True(File.Exists(UsdRuntime.SetupScript));

    [UnixRuntimeFact]
    public async Task CancellationStopsRunningReadinessProbe()
    {
        using var launcher = new FakeInterpreter(waitForChild: true);
        using var cancellation = new CancellationTokenSource();
        Task<UsdRuntimeStatus> probe = UsdRuntime.CheckAsync(launcher.Path, cancellation.Token);
        await launcher.WaitUntilStarted();
        cancellation.Cancel();
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => probe.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        await launcher.WaitUntilLauncherExits();
    }

    [UnixRuntimeFact]
    public async Task CancellationIncludesPipesHeldByChildAfterLauncherExits()
    {
        using var launcher = new FakeInterpreter();
        using var cancellation = new CancellationTokenSource();
        Task<UsdRuntimeStatus> probe = UsdRuntime.CheckAsync(launcher.Path, cancellation.Token);
        await launcher.WaitUntilLauncherExits();
        Assert.True(launcher.ChildIsAlive);
        Assert.False(probe.IsCompleted);
        cancellation.Cancel();
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => probe.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
    }

    [UnixRuntimeFact]
    public async Task ReadinessTimeoutIncludesPipesHeldByChildAfterLauncherExits()
    {
        using var launcher = new FakeInterpreter();
        Task<UsdRuntimeStatus> probe = UsdRuntime.CheckAsync(launcher.Path);
        await launcher.WaitUntilLauncherExits();
        Assert.True(launcher.ChildIsAlive);
        UsdRuntimeStatus status = await probe.WaitAsync(TimeSpan.FromSeconds(22));
        Assert.False(status.Ready);
        Assert.Contains("timed out", status.Detail);
    }

    [UnixRuntimeFact]
    public async Task PublicImportCancellationIncludesInheritedPipesAndRemovesWorkspace()
    {
        using var launcher = new FakeInterpreter();
        using var cancellation = new CancellationTokenSource();
        string? previous = Environment.GetEnvironmentVariable(UsdGaussianReader.PythonEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(UsdGaussianReader.PythonEnvironmentVariable, launcher.Path);
            string input = System.IO.Path.Combine(launcher.DirectoryPath, "input.usda");
            File.WriteAllText(input, "#usda 1.0\n");
            Task<GaussianScene> read = Task.Run(() => UsdGaussianReader.Read(input, cancellation.Token));
            await launcher.WaitUntilLauncherExits();
            Assert.True(launcher.ChildIsAlive);
            string workspace = File.ReadAllText(System.IO.Path.Combine(launcher.DirectoryPath, "output.path"));
            Assert.True(Directory.Exists(workspace));
            cancellation.Cancel();
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => read.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(cancellation.Token, error.CancellationToken);
            Assert.False(Directory.Exists(workspace));
        }
        finally { Environment.SetEnvironmentVariable(UsdGaussianReader.PythonEnvironmentVariable, previous); }
    }

    // The Unix launcher deliberately leaves an orphan with inherited stdout and stderr.
    // A production probe cannot enumerate descendants after the launcher exits; this test
    // owns the child and always terminates it, even if the cancellation assertion fails.
    private sealed class FakeInterpreter : IDisposable
    {
        public string DirectoryPath { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aholo-probe-tests-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(DirectoryPath, "python");
        private string ChildPidPath => System.IO.Path.Combine(DirectoryPath, "child.pid");
        private string LauncherPidPath => System.IO.Path.Combine(DirectoryPath, "launcher.pid");
        public bool ChildIsAlive => TryReadPid(ChildPidPath, out int pid) && IsAlive(pid);

        public FakeInterpreter(bool waitForChild = false)
        {
            if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This process fixture requires a Unix shell.");
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(Path, $$"""
                #!/bin/sh
                sleep 45 &
                child=$!
                printf '%s' "$child" > {{Quote(ChildPidPath)}}
                printf '%s' "$$" > {{Quote(LauncherPidPath)}}
                while [ "$#" -gt 0 ]; do
                    if [ "$1" = "--output" ]; then
                        shift
                        printf '%s' "$1" > {{Quote(System.IO.Path.Combine(DirectoryPath, "output.path"))}}
                        break
                    fi
                    shift
                done
                printf '%s\n' '{"version":[0,26,11],"gaussianSchema":true}'
                {{(waitForChild ? "wait \"$child\"" : "exit 0")}}
                """ + "\n");
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        public Task WaitUntilStarted() => WaitUntil(() => TryReadPid(LauncherPidPath, out _) && TryReadPid(ChildPidPath, out _));
        public async Task WaitUntilLauncherExits()
        {
            await WaitUntilStarted();
            await WaitUntil(() => TryReadPid(LauncherPidPath, out int pid) && !IsAlive(pid));
        }
        private static async Task WaitUntil(Func<bool> condition)
        {
            var timer = Stopwatch.StartNew();
            while (!condition())
            {
                if (timer.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Test launcher did not reach its expected state.");
                await Task.Delay(10);
            }
        }
        private static string Quote(string path) => "'" + path.Replace("'", "'\"'\"'") + "'";
        private static bool TryReadPid(string path, out int pid)
        {
            pid = 0;
            return File.Exists(path) && int.TryParse(File.ReadAllText(path), out pid);
        }
        private static bool IsAlive(int pid)
        {
            try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }
        private static void Stop(string pidPath)
        {
            if (!TryReadPid(pidPath, out int pid)) return;
            try
            {
                using var process = Process.GetProcessById(pid);
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
        public void Dispose()
        {
            Stop(LauncherPidPath); Stop(ChildPidPath);
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }

}


[CollectionDefinition("USD runtime process isolation", DisableParallelization = true)]
public sealed class UsdRuntimeProcessCollection { }

// No Python installation or network is required. Windows still runs the platform-neutral
// readiness/setup tests; the inherited Unix pipe regression is explicitly reported skipped.
public sealed class UnixRuntimeFactAttribute : FactAttribute
{
    public UnixRuntimeFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "Requires the Unix shell process fixture.";
    }
}
