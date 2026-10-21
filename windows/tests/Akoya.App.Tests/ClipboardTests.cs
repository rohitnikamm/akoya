using System.Windows;
using Akoya.App.Platform;
using Akoya.App.Tests.Snapshots;
using Akoya.Core.Dictation;
using Xunit;

namespace Akoya.App.Tests;

public class ClipboardTests
{
    [Fact]
    public async Task Insert_PastesAndRestores()
    {
        var snapshot = ClipboardSnapshot.Of(new DataObject());
        var clipboard = new FakeClipboard(snapshot);
        var scheduler = new ManualScheduler();
        var inserter = new ClipboardPasteInserter(
            clipboard, () => false, () => clipboard.Calls.Add("ctrl+v"), scheduler, _ => Task.CompletedTask);

        var task = inserter.InsertAsync("hello world");

        // Everything up to the restore timer happens synchronously, in this order.
        Assert.Equal(new[] { "snapshot", "set:hello world:excluded", "ctrl+v" }, clipboard.Calls);
        Assert.Equal(TimeSpan.FromMilliseconds(300), Assert.Single(scheduler.Pending).Delay);
        Assert.False(task.IsCompleted);

        scheduler.FireAll();

        Assert.Equal(InsertResult.Pasted, await task);
        Assert.Equal(new[] { "snapshot", "set:hello world:excluded", "ctrl+v", "restore" }, clipboard.Calls);
        Assert.Same(snapshot, clipboard.Restored);
    }

    [Fact]
    public async Task Insert_Elevated_CopiesOnly_NoRestore()
    {
        var clipboard = new FakeClipboard(ClipboardSnapshot.Empty);
        var scheduler = new ManualScheduler();
        var inserter = new ClipboardPasteInserter(
            clipboard, () => true, () => clipboard.Calls.Add("ctrl+v"), scheduler, _ => Task.CompletedTask);

        var result = await inserter.InsertAsync("hello");

        Assert.Equal(InsertResult.CopiedOnly, result);
        Assert.Equal(new[] { "set:hello:excluded" }, clipboard.Calls);
        Assert.Empty(scheduler.Pending);
    }

    [Fact]
    public async Task Insert_ClipboardBusy_RetriesThenReportsFailure()
    {
        var clipboard = new FakeClipboard(ClipboardSnapshot.Empty) { AlwaysBusy = true };
        var scheduler = new ManualScheduler();
        var delays = new List<TimeSpan>();
        var inserter = new ClipboardPasteInserter(
            clipboard, () => false, () => clipboard.Calls.Add("ctrl+v"), scheduler,
            delay =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        var result = await inserter.InsertAsync("hello");

        Assert.Equal(InsertResult.Failed, result);
        Assert.Equal(10, clipboard.SetAttempts);
        Assert.Equal(9, delays.Count);
        Assert.All(delays, d => Assert.Equal(TimeSpan.FromMilliseconds(20), d));
        Assert.DoesNotContain("ctrl+v", clipboard.Calls);
        Assert.DoesNotContain("restore", clipboard.Calls);
        Assert.Empty(scheduler.Pending);
    }

    [Fact]
    public async Task Insert_SnapshotUnreadable_DoesNotClearClipboard()
    {
        var clipboard = new FakeClipboard(ClipboardSnapshot.Unreadable);
        var scheduler = new ManualScheduler();
        var inserter = new ClipboardPasteInserter(
            clipboard, () => false, () => clipboard.Calls.Add("ctrl+v"), scheduler, _ => Task.CompletedTask);

        var result = await inserter.InsertAsync("hello");

        Assert.Equal(InsertResult.Failed, result);
        Assert.Equal(new[] { "snapshot" }, clipboard.Calls); // no set, no paste, no restore
        Assert.Empty(scheduler.Pending);
    }

    [Fact]
    public async Task Insert_ClipboardFreesUpOnRetry_StillPastes()
    {
        var clipboard = new FakeClipboard(ClipboardSnapshot.Empty) { BusyAttempts = 2 };
        var scheduler = new ManualScheduler();
        var inserter = new ClipboardPasteInserter(
            clipboard, () => false, () => clipboard.Calls.Add("ctrl+v"), scheduler, _ => Task.CompletedTask);

        var task = inserter.InsertAsync("hello");
        scheduler.FireAll();

        Assert.Equal(InsertResult.Pasted, await task);
        Assert.Equal(3, clipboard.SetAttempts);
    }

    [Fact]
    public async Task Insert_CtrlVBlocked_KeepsTextOnClipboard()
    {
        var clipboard = new FakeClipboard(ClipboardSnapshot.Empty);
        var scheduler = new ManualScheduler();
        var inserter = new ClipboardPasteInserter(
            clipboard, () => false, () => throw new InvalidOperationException("blocked"), scheduler, _ => Task.CompletedTask);

        var result = await inserter.InsertAsync("hello");

        Assert.Equal(InsertResult.CopiedOnly, result);
        Assert.DoesNotContain("restore", clipboard.Calls);
        Assert.Empty(scheduler.Pending);
    }

    [Fact]
    public void RealClipboard_SnapshotRestore_RoundTripsTextAndHtml()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            const string originalText = "original text é世";
            var html = HtmlClipboard("<b>original</b>");

            var original = new DataObject();
            original.SetData(DataFormats.UnicodeText, originalText);
            original.SetData(DataFormats.Html, html);
            Clipboard.SetDataObject(original, true);

            var snapshot = clipboard.Snapshot();
            Assert.False(snapshot.IsEmpty);
            Assert.False(snapshot.IsUnreadable);

            Assert.True(clipboard.TrySetText("temporary dictation", excludeFromHistory: true));
            Assert.Equal("temporary dictation", Clipboard.GetText());
            AssertAvailable(SystemClipboard.ExcludeFromMonitorFormat);
            AssertAvailable(SystemClipboard.CanIncludeInHistoryFormat);
            AssertAvailable(SystemClipboard.CanUploadToCloudFormat);
            Assert.False(Clipboard.ContainsData(DataFormats.Html));

            clipboard.Restore(snapshot);

            Assert.Equal(originalText, Clipboard.GetText());
            Assert.Contains("<b>original</b>", (string)Clipboard.GetData(DataFormats.Html)!);
            Assert.False(IsAvailable(SystemClipboard.CanIncludeInHistoryFormat));
        });
    }

    [Fact]
    public void RealClipboard_Restore_SkippedWhenUserCopiedMeanwhile()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, "original"), true);
            var snapshot = clipboard.Snapshot();

            Assert.True(clipboard.TrySetText("temporary dictation", excludeFromHistory: true));
            Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, "copied by the user"), true);

            clipboard.Restore(snapshot);

            Assert.Equal("copied by the user", Clipboard.GetText());
        });
    }

    [Fact]
    public void RealClipboard_SnapshotIsDetachedFromLiveClipboard()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, "before"), true);

            var snapshot = clipboard.Snapshot();
            Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, "after"), true);

            Assert.NotNull(snapshot.Data);
            Assert.Equal("before", snapshot.Data.GetData(DataFormats.UnicodeText));
        });
    }

    [Fact]
    public void RealClipboard_RestoreOfEmptySnapshot_ClearsTemporaryText()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            Clipboard.Clear();
            var snapshot = clipboard.Snapshot();
            Assert.True(snapshot.IsEmpty);

            Assert.True(clipboard.TrySetText("temporary dictation", excludeFromHistory: true));
            clipboard.Restore(snapshot);

            Assert.False(Clipboard.ContainsText());
        });
    }

    [Fact]
    public void RealClipboard_TrySetText_Succeeds_AndFailureLeavesClipboardUntouched()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, "mine"), true);

            Assert.True(clipboard.TrySetText("one", excludeFromHistory: false));
            Assert.Equal("one", Clipboard.GetText());
            Assert.False(Clipboard.ContainsData(SystemClipboard.CanIncludeInHistoryFormat));

            Assert.True(clipboard.TrySetText("two \u00e9", excludeFromHistory: true));
            Assert.Equal("two \u00e9", Clipboard.GetText());
        });
    }

    private static string HtmlClipboard(string fragment)
    {
        // CF_HTML: the offsets are only advisory for this round trip, so a header-less payload is enough.
        return "Version:0.9\r\nStartHTML:0000000000\r\nEndHTML:0000000000\r\nStartFragment:0000000000\r\nEndFragment:0000000000\r\n"
               + "<html><body><!--StartFragment-->" + fragment + "<!--EndFragment--></body></html>";
    }

    private static void AssertAvailable(string format) =>
        Assert.True(IsAvailable(format), $"clipboard format {format} is not set");

    private static bool IsAvailable(string format) =>
        Clipboard.ContainsData(format);

    private sealed class FakeClipboard : IClipboard
    {
        private readonly ClipboardSnapshot _snapshot;

        public FakeClipboard(ClipboardSnapshot snapshot) => _snapshot = snapshot;

        public List<string> Calls { get; } = new();

        public int SetAttempts { get; private set; }

        /// <summary>The first this-many attempts fail, then attempts succeed.</summary>
        public int BusyAttempts { get; init; }

        public bool AlwaysBusy { get; init; }

        public ClipboardSnapshot? Restored { get; private set; }

        public ClipboardSnapshot Snapshot()
        {
            Calls.Add("snapshot");
            return _snapshot;
        }

        public bool TrySetText(string text, bool excludeFromHistory)
        {
            SetAttempts++;
            if (AlwaysBusy || SetAttempts <= BusyAttempts)
            {
                return false;
            }

            Calls.Add($"set:{text}:{(excludeFromHistory ? "excluded" : "plain")}");
            return true;
        }

        public void Restore(ClipboardSnapshot snapshot)
        {
            Calls.Add("restore");
            Restored = snapshot;
        }
    }

    private sealed class ManualScheduler : IScheduler
    {
        public DateTimeOffset Now => DateTimeOffset.UnixEpoch;

        public List<(TimeSpan Delay, Action Action)> Pending { get; } = new();

        public IDisposable After(TimeSpan delay, Action action)
        {
            var entry = (delay, action);
            Pending.Add(entry);
            return new Handle(() => Pending.Remove(entry));
        }

        public void FireAll()
        {
            foreach (var (_, action) in Pending.ToList())
            {
                action();
            }
        }

        private sealed class Handle(Action onDispose) : IDisposable
        {
            public void Dispose() => onDispose();
        }
    }
}
