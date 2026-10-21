using System.Runtime.InteropServices;
using Akoya.App.Platform;
using Akoya.App.Tests.Snapshots;
using Akoya.Core.Hotkey;
using Xunit;

namespace Akoya.App.Tests;

public class KeyboardHookTests
{
    [Fact]
    public void Input_HasTheSizeWindowsExpects()
    {
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<NativeMethods.Input>());
    }

    [Fact]
    public void KbdLlHookStruct_HasTheSizeWindowsExpects()
    {
        Assert.Equal(IntPtr.Size == 8 ? 24 : 20, Marshal.SizeOf<NativeMethods.KbdLlHookStruct>());
    }

    [Fact]
    public void Lifecycle_InstallConfigureReinstallDispose_NoExceptions_NoLeakedThread()
    {
        SnapshotHarness.RunSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            var hook = new KeyboardHook(new HotkeyDecoder(HotkeyKey.RightAlt), (_, _) => { });

            // Settings made before Install are queued for the hook thread.
            hook.SetKey(HotkeyKey.RightCtrl);

            hook.Install();
            Assert.True(hook.IsRunning);
            hook.Install(); // idempotent: still one thread

            hook.SetKey(HotkeyKey.Copilot);
            hook.SetRecordingActive(true);
            hook.SetRecordingActive(false);
            hook.Reset();

            hook.Reinstall();
            Assert.True(hook.IsRunning);

            hook.Dispose();
            Assert.False(hook.IsRunning);
            hook.Dispose(); // idempotent
            hook.SetKey(HotkeyKey.RightAlt); // ignored after Dispose
            Assert.Throws<ObjectDisposedException>(hook.Install);
        });
    }

    [Fact]
    public void SinceLastKey_CountsFromTheInstall_AndRestartsOnReinstall()
    {
        SnapshotHarness.RunSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            using var hook = new KeyboardHook(new HotkeyDecoder(HotkeyKey.RightAlt), (_, _) => { });
            hook.Install();
            Assert.InRange(hook.SinceLastKey, TimeSpan.Zero, TimeSpan.FromSeconds(5));

            Thread.Sleep(300);
            // The hook is system-wide: someone typing on this PC meanwhile makes the silence check meaningless.
            if (hook.KeysSeen > 0) return;
            var before = hook.SinceLastKey;
            Assert.True(before >= TimeSpan.FromMilliseconds(250), $"{before}");
            hook.Reinstall();
            Assert.True(hook.SinceLastKey < before, "a re-install starts a fresh silence count");
        });
    }

    [Fact]
    public void Reinstall_OnlyIfSilent_IsRecheckedOnTheHookThread()
    {
        SnapshotHarness.RunSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            using var hook = new KeyboardHook(new HotkeyDecoder(HotkeyKey.RightAlt), (_, _) => { });
            hook.Install();
            Thread.Sleep(300);
            // The hook is system-wide: someone typing on this PC meanwhile makes the silence checks meaningless.
            if (hook.KeysSeen > 0) return;

            // Not silent that long (as when a key arrived after the watchdog looked): left alone, held keys kept.
            var keysBefore = hook.KeysSeen;
            Assert.False(hook.Reinstall(onlyIfSilentFor: TimeSpan.FromSeconds(10)));
            Assert.True(hook.SinceLastKey >= TimeSpan.FromMilliseconds(250), "the silence count was not restarted");

            Assert.True(hook.Reinstall(onlyIfSilentFor: TimeSpan.FromMilliseconds(200)));
            Assert.True(hook.SinceLastKey < TimeSpan.FromMilliseconds(250));
            Assert.Equal(keysBefore, hook.KeysSeen);
        });
    }

    [Fact]
    public void InputIdle_ReadsGetLastInputInfo()
    {
        Assert.Equal(8, Marshal.SizeOf<NativeMethods.LastInputInfo>());
        var idle = InputIdle.Time();
        Assert.NotNull(idle);
        Assert.True(idle >= TimeSpan.Zero);
    }

    [Fact]
    public void Reinstall_AfterTheHookThreadDied_StartsAFreshThread()
    {
        SnapshotHarness.RunSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());
            using var hook = new KeyboardHook(new HotkeyDecoder(HotkeyKey.RightAlt), (_, _) => { });
            hook.Install();

            hook.SimulateThreadExit();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (hook.IsRunning && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(10);
            }

            Assert.False(hook.IsRunning);

            hook.Reinstall(); // must not block for seconds or throw
            Assert.True(hook.IsRunning);
        });
    }

    [Fact]
    public void Constructor_WithoutSynchronizationContext_Throws()
    {
        SnapshotHarness.RunSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(null);
            Assert.Throws<InvalidOperationException>(
                () => new KeyboardHook(new HotkeyDecoder(HotkeyKey.RightAlt), (_, _) => { }));
        });
    }
}
