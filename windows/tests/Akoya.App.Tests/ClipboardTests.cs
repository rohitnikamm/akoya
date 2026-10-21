using Akoya.App.Platform;
using Akoya.App.Tests.Snapshots;
using Akoya.Core.Dictation;
using Xunit;

namespace Akoya.App.Tests;

public class ClipboardTests
{
    private static readonly ClipboardSnapshot TextSnapshot =
        ClipboardSnapshot.Of(new[] { new ClipboardItem(RawClipboard.CfUnicodeText, RawClipboard.Utf16("before")) });

    [Fact]
    public async Task Insert_PastesAndRestores()
    {
        var clipboard = new FakeClipboard(TextSnapshot);
        var scheduler = new ManualScheduler();
        var inserter = NewInserter(clipboard, scheduler);

        var task = inserter.InsertAsync("hello world");

        // Everything up to the restore timer happens synchronously, in this order.
        Assert.Equal(new[] { "snapshot", "set:hello world:excluded", "ctrl+v" }, clipboard.Calls);
        Assert.Equal(TimeSpan.FromMilliseconds(300), Assert.Single(scheduler.Pending).Delay);
        Assert.False(task.IsCompleted);

        scheduler.FireAll();

        Assert.Equal(InsertResult.Pasted, await task);
        Assert.Equal(new[] { "snapshot", "set:hello world:excluded", "ctrl+v", "restore" }, clipboard.Calls);
        Assert.Same(TextSnapshot, clipboard.Restored);
    }

    [Fact]
    public async Task Insert_ClipboardBusyAtRestore_RetriesForAboutASecond()
    {
        var clipboard = new FakeClipboard(TextSnapshot) { RestoreBusyAttempts = 30 };
        var scheduler = new ManualScheduler();
        var delays = new List<TimeSpan>();
        var inserter = new ClipboardPasteInserter(
            clipboard, () => false, () => clipboard.Calls.Add("ctrl+v"), t => clipboard.Calls.Add("type:" + t), scheduler,
            d =>
            {
                delays.Add(d);
                return Task.CompletedTask;
            });

        var task = inserter.InsertAsync("hello");
        scheduler.FireAll();

        Assert.Equal(InsertResult.Pasted, await task);
        Assert.Equal(31, clipboard.RestoreAttempts);
        Assert.Same(TextSnapshot, clipboard.Restored);
        Assert.Equal(30, delays.Count);
    }

    [Fact]
    public async Task Insert_ClipboardBusyAtRestore_GivesUpAfterAboutASecond()
    {
        var clipboard = new FakeClipboard(TextSnapshot) { RestoreBusyAttempts = int.MaxValue };
        var scheduler = new ManualScheduler();
        var waited = TimeSpan.Zero;
        var inserter = new ClipboardPasteInserter(
            clipboard, () => false, () => clipboard.Calls.Add("ctrl+v"), t => clipboard.Calls.Add("type:" + t), scheduler,
            d =>
            {
                waited += d;
                return Task.CompletedTask;
            });

        var task = inserter.InsertAsync("hello");
        scheduler.FireAll();

        Assert.Equal(InsertResult.Pasted, await task); // the paste landed; only the old contents stay away
        Assert.Equal(ClipboardPasteInserter.MaxRestoreAttempts, clipboard.RestoreAttempts);
        Assert.InRange(waited, TimeSpan.FromSeconds(0.9), TimeSpan.FromSeconds(1.1));
        Assert.Null(clipboard.Restored);
    }

    [Fact]
    public async Task Insert_HandsTheSnapshotToTheSet_SoAFailedWriteCanPutItBack()
    {
        var clipboard = new FakeClipboard(TextSnapshot);
        var scheduler = new ManualScheduler();
        var task = NewInserter(clipboard, scheduler).InsertAsync("hello");
        scheduler.FireAll();
        await task;

        Assert.Same(TextSnapshot, clipboard.RestoreOnFailure);
    }

    [Fact]
    public async Task Insert_IncompleteSnapshot_TypesTheTextInstead_AndLeavesTheClipboardAlone()
    {
        var clipboard = new FakeClipboard(ClipboardSnapshot.Incomplete);
        var scheduler = new ManualScheduler();
        var inserter = NewInserter(clipboard, scheduler);

        var result = await inserter.InsertAsync("hello world ");

        Assert.Equal(InsertResult.Pasted, result);
        Assert.Equal(new[] { "snapshot", "type:hello world " }, clipboard.Calls); // no set, no Ctrl+V, no restore
        Assert.Empty(scheduler.Pending);
    }

    [Fact]
    public async Task Insert_IncompleteSnapshot_LogsWhichFormat()
    {
        var clipboard = new FakeClipboard(ClipboardSnapshot.IncompleteBecause("format 49999 \"Odd\" could not be read"));
        var logged = new List<string>();
        var inserter = new ClipboardPasteInserter(
            clipboard, () => false, () => clipboard.Calls.Add("ctrl+v"), t => clipboard.Calls.Add("type:" + t),
            new ManualScheduler(), _ => Task.CompletedTask, logged.Add);

        await inserter.InsertAsync("secret words ");

        var line = Assert.Single(logged);
        Assert.Contains("format 49999 \"Odd\" could not be read", line);
        Assert.DoesNotContain("secret", line); // never the transcript
    }

    [Fact]
    public async Task Insert_IncompleteSnapshot_TypingBlocked_Fails()
    {
        var clipboard = new FakeClipboard(ClipboardSnapshot.Incomplete);
        var scheduler = new ManualScheduler();
        var inserter = new ClipboardPasteInserter(
            clipboard, () => false, () => clipboard.Calls.Add("ctrl+v"),
            _ => throw new System.ComponentModel.Win32Exception(5), scheduler, _ => Task.CompletedTask);

        Assert.Equal(InsertResult.Failed, await inserter.InsertAsync("hello"));
        Assert.Equal(new[] { "snapshot" }, clipboard.Calls);
    }

    [Fact]
    public async Task Insert_Elevated_CopiesOnly_NoRestore()
    {
        var clipboard = new FakeClipboard(ClipboardSnapshot.Empty);
        var scheduler = new ManualScheduler();
        var inserter = new ClipboardPasteInserter(
            clipboard, () => true, () => clipboard.Calls.Add("ctrl+v"), t => clipboard.Calls.Add("type:" + t), scheduler,
            _ => Task.CompletedTask);

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
            clipboard, () => false, () => clipboard.Calls.Add("ctrl+v"), t => clipboard.Calls.Add("type:" + t), scheduler,
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

        var result = await NewInserter(clipboard, scheduler).InsertAsync("hello");

        Assert.Equal(InsertResult.Failed, result);
        Assert.Equal(new[] { "snapshot" }, clipboard.Calls); // no set, no paste, no restore
        Assert.Empty(scheduler.Pending);
    }

    [Fact]
    public async Task Insert_ClipboardFreesUpOnRetry_StillPastes()
    {
        var clipboard = new FakeClipboard(ClipboardSnapshot.Empty) { BusyAttempts = 2 };
        var scheduler = new ManualScheduler();

        var task = NewInserter(clipboard, scheduler).InsertAsync("hello");
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
            clipboard, () => false, () => throw new InvalidOperationException("blocked"), t => clipboard.Calls.Add("type:" + t),
            scheduler, _ => Task.CompletedTask);

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
            var html = RawClipboard.Ascii(ClipboardSamples.Html("<b>original</b>"));
            var htmlFormat = RawClipboard.Format("HTML Format");
            RawClipboard.Set(new[] { (RawClipboard.CfUnicodeText, RawClipboard.Utf16(originalText)), (htmlFormat, html) });

            var snapshot = clipboard.Snapshot();
            Assert.False(snapshot.IsEmpty);
            Assert.False(snapshot.IsUnreadable);
            Assert.False(snapshot.IsIncomplete);

            Assert.True(clipboard.TrySetText("temporary dictation", excludeFromHistory: true));
            Assert.Equal("temporary dictation", System.Windows.Clipboard.GetText());
            Assert.True(RawClipboard.Has(RawClipboard.Format(SystemClipboard.ExcludeFromMonitorFormat)));
            Assert.True(RawClipboard.Has(RawClipboard.Format(SystemClipboard.CanIncludeInHistoryFormat)));
            Assert.True(RawClipboard.Has(RawClipboard.Format(SystemClipboard.CanUploadToCloudFormat)));
            Assert.False(RawClipboard.Has(htmlFormat));

            clipboard.TryRestore(snapshot);

            Assert.True(RawClipboard.StartsWith(RawClipboard.Get(RawClipboard.CfUnicodeText), RawClipboard.Utf16(originalText)));
            Assert.True(RawClipboard.StartsWith(RawClipboard.Get(htmlFormat), html));
            Assert.False(RawClipboard.Has(RawClipboard.Format(SystemClipboard.CanIncludeInHistoryFormat)));
        });
    }

    [Fact]
    public void RealClipboard_PrivateRegisteredFormatOnly_SurvivesByteForByte()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            var privateFormat = RawClipboard.Format("Akoya Test Private Layers");
            var payload = Enumerable.Range(0, 5000).Select(i => (byte)(i * 31)).ToArray();
            RawClipboard.Set(new[] { (privateFormat, payload) });

            var snapshot = clipboard.Snapshot();
            Assert.False(snapshot.IsEmpty);
            Assert.False(snapshot.IsIncomplete);

            Assert.True(clipboard.TrySetText("temporary dictation", excludeFromHistory: true));
            Assert.False(RawClipboard.Has(privateFormat));
            clipboard.TryRestore(snapshot);

            Assert.True(RawClipboard.StartsWith(RawClipboard.Get(privateFormat), payload));
            Assert.False(RawClipboard.Has(RawClipboard.CfUnicodeText)); // not left holding our text
        });
    }

    [Fact]
    public void RealClipboard_PasswordManagerMarkersSurvive_AndTheRestoreIsExcludedFromMonitoring()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            var canInclude = RawClipboard.Format(SystemClipboard.CanIncludeInHistoryFormat);
            var canUpload = RawClipboard.Format(SystemClipboard.CanUploadToCloudFormat);
            var exclude = RawClipboard.Format(SystemClipboard.ExcludeFromMonitorFormat);
            RawClipboard.Set(new[]
            {
                (RawClipboard.CfUnicodeText, RawClipboard.Utf16("hunter2")),
                (canInclude, new byte[4]),
                (canUpload, new byte[4]),
            });
            Assert.False(RawClipboard.Has(exclude));

            var snapshot = clipboard.Snapshot();
            Assert.True(clipboard.TrySetText("temporary dictation", excludeFromHistory: true));
            clipboard.TryRestore(snapshot);

            Assert.True(RawClipboard.StartsWith(RawClipboard.Get(RawClipboard.CfUnicodeText), RawClipboard.Utf16("hunter2")));
            Assert.True(RawClipboard.StartsWith(RawClipboard.Get(canInclude), new byte[4]));
            Assert.True(RawClipboard.StartsWith(RawClipboard.Get(canUpload), new byte[4]));
            Assert.True(RawClipboard.Has(exclude), "the restore must not be recorded as a new copy");
        });
    }

    [Fact]
    public void RealClipboard_MultiFormatSet_Survives()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            var rtf = RawClipboard.Format("Rich Text Format");
            var html = RawClipboard.Format("HTML Format");
            var dropEffect = RawClipboard.Format("Preferred DropEffect");
            var items = new (uint, byte[])[]
            {
                (RawClipboard.CfUnicodeText, RawClipboard.Utf16("cells")),
                (rtf, RawClipboard.Ascii(@"{\rtf1 cells}")),
                (html, RawClipboard.Ascii(ClipboardSamples.Html("<td>cells</td>"))),
                (RawClipboard.CfDib, ClipboardSamples.Dib()),
                (RawClipboard.CfHdrop, ClipboardSamples.FileDrop(@"C:\one.txt", @"C:\two.txt")),
                (dropEffect, BitConverter.GetBytes(2)), // DROPEFFECT_MOVE: an Explorer cut
            };
            RawClipboard.Set(items, ClipboardSamples.EnhMetaFile());
            var emf = RawClipboard.GetEnhMetaFileBits();
            Assert.NotNull(emf);

            var snapshot = clipboard.Snapshot();
            Assert.False(snapshot.IsIncomplete);
            Assert.True(clipboard.TrySetText("temporary dictation", excludeFromHistory: true));
            clipboard.TryRestore(snapshot);

            foreach (var (format, data) in items)
            {
                Assert.True(RawClipboard.StartsWith(RawClipboard.Get(format), data), $"format {format} did not survive");
            }

            Assert.Equal(emf, RawClipboard.GetEnhMetaFileBits());
        });
    }

    [Fact]
    public void RealClipboard_OddBitmap_DoesNotFailThePaste()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            var odd = ClipboardSamples.OddDib();
            RawClipboard.Set(new[] { (RawClipboard.CfDib, odd), (RawClipboard.CfUnicodeText, RawClipboard.Utf16("caption")) });

            var scheduler = new ManualScheduler();
            var pasted = new List<string>();
            var inserter = new ClipboardPasteInserter(
                clipboard, () => false, () => pasted.Add(System.Windows.Clipboard.GetText()),
                t => throw new InvalidOperationException("should paste, not type"), scheduler, _ => Task.CompletedTask);

            // As in the app, the restore continues on this thread's dispatcher (it owns the clipboard window).
            var result = RunOnDispatcher(() =>
            {
                var task = inserter.InsertAsync("dictated ");
                scheduler.FireAll();
                return task;
            });

            Assert.Equal(InsertResult.Pasted, result);
            Assert.Equal(new[] { "dictated " }, pasted);
            Assert.True(RawClipboard.StartsWith(RawClipboard.Get(RawClipboard.CfDib), odd));
            Assert.True(RawClipboard.StartsWith(RawClipboard.Get(RawClipboard.CfUnicodeText), RawClipboard.Utf16("caption")));
        });
    }

    [Fact]
    public void RealClipboard_OverTheSizeCap_IsIncomplete()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard(maxSnapshotBytes: 1024);
            RawClipboard.Set(new[] { (RawClipboard.Format("Akoya Test Big"), new byte[4096]) });

            var snapshot = clipboard.Snapshot();

            Assert.True(snapshot.IsIncomplete);
            Assert.False(snapshot.IsEmpty);
            Assert.Contains("\"Akoya Test Big\"", snapshot.IncompleteReason); // the log says which format
        });
    }

    [Fact]
    public void RealClipboard_4kScreenshot_FitsUnderTheCap_SynthesisedSiblingsAreNotCopied()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            var dib = ClipboardSamples.Dib(3840, 2160); // 33 MB; Windows offers a synthesised CF_DIBV5 of the same size
            var png = RawClipboard.Format("PNG");
            var pngBytes = Enumerable.Range(0, 2_000_000).Select(i => (byte)i).ToArray();
            RawClipboard.Set(new[] { (RawClipboard.CfDib, dib), (png, pngBytes) });
            Assert.True(RawClipboard.Has(RawClipboard.CfDibV5));

            var snapshot = clipboard.Snapshot();

            Assert.False(snapshot.IsIncomplete);
            Assert.Contains(snapshot.Items, i => i.Format == RawClipboard.CfDib);
            Assert.DoesNotContain(snapshot.Items, i => i.Format == RawClipboard.CfDibV5);

            Assert.True(clipboard.TrySetText("temporary dictation", excludeFromHistory: true));
            clipboard.TryRestore(snapshot);

            Assert.True(RawClipboard.StartsWith(RawClipboard.Get(RawClipboard.CfDib), dib));
            Assert.True(RawClipboard.StartsWith(RawClipboard.Get(png), pngBytes));
            Assert.True(RawClipboard.Has(RawClipboard.CfDibV5)); // Windows synthesises it again
        });
    }

    [Fact]
    public void RealClipboard_IncompleteSnapshot_IsNeverRestored()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            RawClipboard.Set(new[] { (RawClipboard.CfUnicodeText, RawClipboard.Utf16("before")) });
            Assert.True(clipboard.TrySetText("temporary dictation", excludeFromHistory: true));

            clipboard.TryRestore(ClipboardSnapshot.Incomplete);

            Assert.Equal("temporary dictation", System.Windows.Clipboard.GetText());
        });
    }

    [Fact]
    public void RealClipboard_SetFailsAfterEmpty_PutsTheSnapshotBackAtOnce()
    {
        SnapshotHarness.RunSta(() =>
        {
            var failOurText = true;
            using var clipboard = new SystemClipboard(
                SystemClipboard.DefaultMaxSnapshotBytes,
                (format, handle) => failOurText && format == RawClipboard.CfUnicodeText
                    ? IntPtr.Zero
                    : NativeMethods.SetClipboardData(format, handle));
            var privateFormat = RawClipboard.Format("Akoya Test Private Layers");
            RawClipboard.Set(new[] { (privateFormat, new byte[] { 1, 2, 3, 4 }) });
            var snapshot = clipboard.Snapshot();

            Assert.False(clipboard.TrySetText("temporary dictation", excludeFromHistory: true, restoreOnFailure: snapshot));

            Assert.True(RawClipboard.StartsWith(RawClipboard.Get(privateFormat), new byte[] { 1, 2, 3, 4 }));
            Assert.True(RawClipboard.Has(RawClipboard.Format(SystemClipboard.ExcludeFromMonitorFormat)));
            failOurText = false;
        });
    }

    [Fact]
    public void RealClipboard_Restore_SkippedWhenUserCopiedMeanwhile()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            RawClipboard.Set(new[] { (RawClipboard.CfUnicodeText, RawClipboard.Utf16("original")) });
            var snapshot = clipboard.Snapshot();

            Assert.True(clipboard.TrySetText("temporary dictation", excludeFromHistory: true));
            RawClipboard.Set(new[] { (RawClipboard.CfUnicodeText, RawClipboard.Utf16("copied by the user")) });

            clipboard.TryRestore(snapshot);

            Assert.Equal("copied by the user", System.Windows.Clipboard.GetText());
        });
    }

    [Fact]
    public void RealClipboard_RestoreWhileAnotherAppHoldsIt_SaysBusy_ThenWorks()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            RawClipboard.Set(new[] { (RawClipboard.CfUnicodeText, RawClipboard.Utf16("original")) });
            var snapshot = clipboard.Snapshot();
            Assert.True(clipboard.TrySetText("temporary dictation", excludeFromHistory: true));

            using (RawClipboard.HoldOpenOnAnotherThread())
            {
                Assert.False(clipboard.TryRestore(snapshot));
            }

            Assert.True(clipboard.TryRestore(snapshot));
            Assert.Equal("original", System.Windows.Clipboard.GetText());
        });
    }

    [Fact]
    public void RealClipboard_CfTextReadBetweenSetAndRestore_StillRestores()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            RawClipboard.Set(new[] { (RawClipboard.CfUnicodeText, RawClipboard.Utf16("original")) });
            var snapshot = clipboard.Snapshot();
            Assert.True(clipboard.TrySetText("temporary dictation", excludeFromHistory: true));

            // The target app reads the ANSI text, which Windows synthesises on the spot.
            Assert.True(RawClipboard.StartsWith(RawClipboard.Get(RawClipboard.CfText), RawClipboard.Ascii("temporary dictation")));

            Assert.True(clipboard.TryRestore(snapshot));
            Assert.Equal("original", System.Windows.Clipboard.GetText());
        });
    }

    [Fact]
    public void RealClipboard_DelayedRenderedFormatThatNeverRenders_TypesInstead()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            var promised = RawClipboard.Format("Akoya Test Promised");
            // An owner that promises a format (delayed rendering) and then never renders it.
            using var owner = RawClipboard.SetDelayed(promised);

            var typed = new List<string>();
            var inserter = new ClipboardPasteInserter(
                clipboard, () => false, () => throw new InvalidOperationException("should type, not paste"), typed.Add,
                new ManualScheduler(), _ => Task.CompletedTask);
            var result = inserter.InsertAsync("dictated ").GetAwaiter().GetResult();

            Assert.Equal(InsertResult.Pasted, result);
            Assert.Equal(new[] { "dictated " }, typed);
            Assert.True(RawClipboard.Has(promised)); // the clipboard was left alone
            Assert.False(RawClipboard.Has(RawClipboard.CfUnicodeText));
            Assert.Contains("\"Akoya Test Promised\"", clipboard.Snapshot().IncompleteReason);
        });
    }

    [Fact]
    public void RealClipboard_SnapshotIsDetachedFromLiveClipboard()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            RawClipboard.Set(new[] { (RawClipboard.CfUnicodeText, RawClipboard.Utf16("before")) });

            var snapshot = clipboard.Snapshot();
            RawClipboard.Set(new[] { (RawClipboard.CfUnicodeText, RawClipboard.Utf16("after")) });

            var text = Assert.Single(snapshot.Items, i => i.Format == RawClipboard.CfUnicodeText);
            Assert.True(RawClipboard.StartsWith(text.Data, RawClipboard.Utf16("before")));
        });
    }

    [Fact]
    public void RealClipboard_RestoreOfEmptySnapshot_ClearsTemporaryText()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            RawClipboard.Set(Array.Empty<(uint, byte[])>());
            var snapshot = clipboard.Snapshot();
            Assert.True(snapshot.IsEmpty);

            Assert.True(clipboard.TrySetText("temporary dictation", excludeFromHistory: true));
            clipboard.TryRestore(snapshot);

            Assert.False(System.Windows.Clipboard.ContainsText());
        });
    }

    [Fact]
    public void RealClipboard_TrySetText_Succeeds_AndMarksOnlyWhenAsked()
    {
        SnapshotHarness.RunSta(() =>
        {
            using var clipboard = new SystemClipboard();
            RawClipboard.Set(new[] { (RawClipboard.CfUnicodeText, RawClipboard.Utf16("mine")) });

            Assert.True(clipboard.TrySetText("one", excludeFromHistory: false));
            Assert.Equal("one", System.Windows.Clipboard.GetText());
            Assert.False(RawClipboard.Has(RawClipboard.Format(SystemClipboard.CanIncludeInHistoryFormat)));
            Assert.False(RawClipboard.Has(RawClipboard.Format(SystemClipboard.ExcludeFromMonitorFormat)));

            Assert.True(clipboard.TrySetText("two \u00e9", excludeFromHistory: true));
            Assert.Equal("two \u00e9", System.Windows.Clipboard.GetText());
        });
    }

    /// <summary>Runs an async flow to completion with this STA thread's dispatcher as its synchronization context.</summary>
    private static T RunOnDispatcher<T>(Func<Task<T>> flow)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(
            new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        try
        {
            var task = flow();
            var frame = new System.Windows.Threading.DispatcherFrame();
            task.ContinueWith(_ => frame.Continue = false, TaskScheduler.FromCurrentSynchronizationContext());
            System.Windows.Threading.Dispatcher.PushFrame(frame);
            return task.GetAwaiter().GetResult();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static ClipboardPasteInserter NewInserter(FakeClipboard clipboard, ManualScheduler scheduler) =>
        new(clipboard, () => false, () => clipboard.Calls.Add("ctrl+v"), t => clipboard.Calls.Add("type:" + t), scheduler,
            _ => Task.CompletedTask);

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

        public ClipboardSnapshot? RestoreOnFailure { get; private set; }

        public ClipboardSnapshot Snapshot()
        {
            Calls.Add("snapshot");
            return _snapshot;
        }

        public bool TrySetText(string text, bool excludeFromHistory, ClipboardSnapshot? restoreOnFailure = null)
        {
            SetAttempts++;
            RestoreOnFailure = restoreOnFailure;
            if (AlwaysBusy || SetAttempts <= BusyAttempts)
            {
                return false;
            }

            Calls.Add($"set:{text}:{(excludeFromHistory ? "excluded" : "plain")}");
            return true;
        }

        /// <summary>The first this-many restore attempts find the clipboard busy.</summary>
        public int RestoreBusyAttempts { get; init; }

        public int RestoreAttempts { get; private set; }

        public bool TryRestore(ClipboardSnapshot snapshot)
        {
            RestoreAttempts++;
            if (RestoreAttempts <= RestoreBusyAttempts)
            {
                return false;
            }

            Calls.Add("restore");
            Restored = snapshot;
            return true;
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
