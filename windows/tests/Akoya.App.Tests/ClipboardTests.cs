using Akoya.App.Platform;
using Akoya.App.Tests.Snapshots;
using Akoya.Core.Dictation;
using Xunit;

namespace Akoya.App.Tests;

public class ClipboardTests
{
    private const string ExcludeFromMonitor = "ExcludeClipboardContentFromMonitorProcessing";
    private const string CanIncludeInHistory = "CanIncludeInClipboardHistory";

    [Fact]
    public async Task Insert_CopiesThenPastes_AndTheTextStaysOnTheClipboard()
    {
        var clipboard = new FakeClipboard();

        var result = await NewInserter(clipboard).InsertAsync("hello world ");

        Assert.Equal(InsertResult.Pasted, result);
        Assert.Equal(new[] { "set:hello world ", "ctrl+v" }, clipboard.Calls); // nothing put back afterwards
    }

    [Fact]
    public async Task Insert_Elevated_CopiesOnly()
    {
        var clipboard = new FakeClipboard();
        var inserter = new ClipboardPasteInserter(clipboard, () => true, () => clipboard.Calls.Add("ctrl+v"), _ => Task.CompletedTask);

        Assert.Equal(InsertResult.CopiedOnly, await inserter.InsertAsync("hello"));
        Assert.Equal(new[] { "set:hello" }, clipboard.Calls);
    }

    [Fact]
    public async Task Insert_ClipboardBusy_RetriesThenReportsFailure()
    {
        var clipboard = new FakeClipboard { AlwaysBusy = true };
        var delays = new List<TimeSpan>();
        var inserter = new ClipboardPasteInserter(clipboard, () => false, () => clipboard.Calls.Add("ctrl+v"), d =>
        {
            delays.Add(d);
            return Task.CompletedTask;
        });

        Assert.Equal(InsertResult.Failed, await inserter.InsertAsync("hello"));
        Assert.Equal(10, clipboard.SetAttempts);
        Assert.Equal(9, delays.Count);
        Assert.All(delays, d => Assert.Equal(TimeSpan.FromMilliseconds(20), d));
        Assert.DoesNotContain("ctrl+v", clipboard.Calls);
    }

    [Fact]
    public async Task Insert_ClipboardFreesUpOnRetry_StillPastes()
    {
        var clipboard = new FakeClipboard { BusyAttempts = 2 };

        Assert.Equal(InsertResult.Pasted, await NewInserter(clipboard).InsertAsync("hello"));
        Assert.Equal(3, clipboard.SetAttempts);
    }

    [Fact]
    public async Task Insert_CtrlVBlocked_KeepsTextOnClipboard()
    {
        var clipboard = new FakeClipboard();
        var inserter = new ClipboardPasteInserter(clipboard, () => false, () => throw new InvalidOperationException("blocked"),
            _ => Task.CompletedTask);

        Assert.Equal(InsertResult.CopiedOnly, await inserter.InsertAsync("hello"));
        Assert.Equal(new[] { "set:hello" }, clipboard.Calls);
    }

    [Fact]
    public void RealClipboard_Dictation_StaysAsAnOrdinaryCopy()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            RawClipboard.Set(new[] { (RawClipboard.CfUnicodeText, RawClipboard.Utf16("copied earlier")) });
            var inserter = new ClipboardPasteInserter(clipboard, () => false, () => { }, _ => Task.CompletedTask);

            var result = inserter.InsertAsync("dictated words ").GetAwaiter().GetResult();

            Assert.Equal(InsertResult.Pasted, result);
            Assert.Equal("dictated words ", System.Windows.Clipboard.GetText());
            // A normal copy: Win+V history keeps it (and the earlier copy).
            Assert.False(RawClipboard.Has(RawClipboard.Format(ExcludeFromMonitor)));
            Assert.False(RawClipboard.Has(RawClipboard.Format(CanIncludeInHistory)));
        });
    }

    [Fact]
    public void RealClipboard_TrySetText_ReplacesEverything_EmojiAndAccentsIntact()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            var privateFormat = RawClipboard.Format("Akoya Test Private Layers");
            RawClipboard.Set(new[] { (privateFormat, new byte[] { 1, 2, 3 }), (RawClipboard.CfUnicodeText, RawClipboard.Utf16("old")) });

            Assert.True(clipboard.TrySetText("naïve café 😀"));

            Assert.Equal("naïve café 😀", System.Windows.Clipboard.GetText());
            Assert.False(RawClipboard.Has(privateFormat));
        });
    }

    [Fact]
    public void RealClipboard_WhileAnotherAppHoldsIt_SaysBusy_ThenWorks()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            RawClipboard.Set(new[] { (RawClipboard.CfUnicodeText, RawClipboard.Utf16("original")) });

            using (RawClipboard.HoldOpenOnAnotherThread())
            {
                Assert.False(clipboard.TrySetText("dictated"));
            }

            Assert.Equal("original", System.Windows.Clipboard.GetText()); // untouched while busy
            Assert.True(clipboard.TrySetText("dictated"));
            Assert.Equal("dictated", System.Windows.Clipboard.GetText());
        });
    }

    [Fact]
    public void RealClipboard_WriteFails_ReportsIt()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard((_, _) => IntPtr.Zero);

            Assert.False(clipboard.TrySetText("dictated"));
        });
    }

    private static ClipboardPasteInserter NewInserter(FakeClipboard clipboard) =>
        new(clipboard, () => false, () => clipboard.Calls.Add("ctrl+v"), _ => Task.CompletedTask);

    private sealed class FakeClipboard : IClipboard
    {
        public List<string> Calls { get; } = new();

        public int SetAttempts { get; private set; }

        /// <summary>The first this-many attempts fail, then attempts succeed.</summary>
        public int BusyAttempts { get; init; }

        public bool AlwaysBusy { get; init; }

        public bool TrySetText(string text)
        {
            SetAttempts++;
            if (AlwaysBusy || SetAttempts <= BusyAttempts)
            {
                return false;
            }

            Calls.Add("set:" + text);
            return true;
        }
    }
}
