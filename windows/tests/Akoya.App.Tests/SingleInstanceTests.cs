using System.IO;
using System.Windows.Threading;
using Akoya.App.Platform;
using Microsoft.Win32;
using Xunit;

namespace Akoya.App.Tests;

public class SingleInstanceTests
{
    private static string UniqueName() => @"Local\Akoya-test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void SecondAcquire_Fails_AndItsSignalReachesTheFirst()
    {
        var name = UniqueName();
        Assert.True(SingleInstance.TryAcquire(name, out var first));
        using (first)
        {
            using var shown = new ManualResetEventSlim();
            first!.ShowRequested += shown.Set;

            Assert.False(SingleInstance.TryAcquire(name, out var second));
            Assert.Null(second);
            Assert.True(SingleInstance.SignalFirst(name));

            Assert.True(shown.Wait(TimeSpan.FromSeconds(5)), "the first instance was not signalled");
        }

        // Once the first exits, a new launch is the first again.
        Assert.True(SingleInstance.TryAcquire(name, out var again));
        again!.Dispose();
    }

    [Fact]
    public void SignalFirst_WithNoRunningInstance_ReturnsFalse() =>
        Assert.False(SingleInstance.SignalFirst(UniqueName()));

    [Fact]
    public void DefaultName_IsPerUser() =>
        Assert.StartsWith(@"Local\Akoya-S-1-", SingleInstance.DefaultName);
}

public class StartupRegistrationTests
{
    [Fact]
    public void SetAndIsEnabled_RoundTripUnderATestKey()
    {
        var key = @"Software\Akoya.Tests\" + Guid.NewGuid().ToString("N") + @"\Run";
        try
        {
            Assert.False(StartupRegistration.IsEnabled(key));

            const string exe = @"C:\Users\rohit\AppData\Local\Akoya\Akoya.exe";
            StartupRegistration.Set(true, exe, key);
            Assert.True(StartupRegistration.IsEnabled(key));
            Assert.Equal("\"" + exe + "\"", StartupRegistration.RegisteredCommand(key));

            StartupRegistration.Set(true, "\"" + exe + "\"", key); // already quoted: not quoted twice
            Assert.Equal("\"" + exe + "\"", StartupRegistration.RegisteredCommand(key));

            StartupRegistration.Set(false, exe, key);
            Assert.False(StartupRegistration.IsEnabled(key));
            StartupRegistration.Set(false, exe, key); // removing twice is fine
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Akoya.Tests", throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void LaunchPath_WhenNotInstalled_IsTheRunningExe()
    {
        Assert.False(StartupRegistration.IsInstalledCopy);
        Assert.Equal(Environment.ProcessPath, StartupRegistration.LaunchPath);
    }
}

public class DispatcherSchedulerTests
{
    [Fact]
    public void After_RunsOnTheDispatcherThread_AndDisposeCancels()
    {
        UiThread.Run(() =>
        {
            var scheduler = new DispatcherScheduler(Dispatcher.CurrentDispatcher);
            var uiThread = Environment.CurrentManagedThreadId;
            int? ranOn = null;
            var cancelledRan = false;

            scheduler.After(TimeSpan.FromMilliseconds(30), () => ranOn = Environment.CurrentManagedThreadId);
            var cancelled = scheduler.After(TimeSpan.FromMilliseconds(30), () => cancelledRan = true);
            cancelled.Dispose();

            UiThread.PumpUntil(() => ranOn is not null);
            UiThread.Pump(TimeSpan.FromMilliseconds(100));
            Assert.Equal(uiThread, ranOn);
            Assert.False(cancelledRan);
        });
    }
}

public class FileLogTests
{
    [Fact]
    public void RollsAtTheLimit_KeepingThreeFiles()
    {
        var dir = TempDir.Create();
        try
        {
            var log = new FileLog(dir, maxBytes: 200);
            for (var i = 0; i < 40; i++) log.Info($"line {i:00} " + new string('x', 30));
            log.Error("failed", new InvalidOperationException("outer", new IOException("inner")));

            var files = Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "akoya.1.log", "akoya.2.log", "akoya.log" }, files);
            Assert.All(Directory.GetFiles(dir), f => Assert.True(new FileInfo(f).Length <= 200));
            Assert.Contains("ERROR failed: InvalidOperationException: outer <- IOException: inner", File.ReadAllText(log.CurrentPath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
