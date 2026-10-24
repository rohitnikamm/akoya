# CI summary

## Model
tarball cache: hit

## Build
Build succeeded.
0 Warning(s)

## Tests
- akoya.core.tests: total 417, passed 417, failed 0, not executed 0
- akoya.app.tests: total 405, passed 404, failed 1, not executed 0

### FAILED: Akoya.App.Tests.ClipboardTests.RealClipboard_TrySetText_ReplacesEverything_EmojiAndAccentsIntact
```
Assert.True() Failure
Expected: True
Actual:   False

   at Akoya.App.Tests.ClipboardTests.<>c.<RealClipboard_TrySetText_ReplacesEverything_EmojiAndAccentsIntact>b__8_0() in D:\a\akoya\akoya\windows\tests\Akoya.App.Tests\ClipboardTests.cs:line 100
   at Akoya.App.Tests.Snapshots.SnapshotHarness.<>c__DisplayClass5_0.<RunSta>b__0() in D:\a\akoya\akoya\windows\tests\Akoya.App.Tests\Snapshots\SnapshotHarness.cs:line 98
--- End of stack trace from previous location ---
   at Akoya.App.Tests.Snapshots.SnapshotHarness.RunSta(Action action) in D:\a\akoya\akoya\windows\tests\Akoya.App.Tests\Snapshots\SnapshotHarness.cs:line 111
   at Akoya.App.Tests.ClipboardTests.RealClipboard_TrySetText_ReplacesEverything_EmojiAndAccentsIntact() in D:\a\akoya\akoya\windows\tests\Akoya.App.Tests\ClipboardTests.cs:line 94
   at System.RuntimeMethodHandle.InvokeMethod(ObjectHandleOnStack target, Void** arguments, ObjectHandleOnStack sig, BOOL isConstructor, ObjectHandleOnStack result)
   at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
   at System.Reflection.RuntimeMethodInfo.Invoke(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
```

## Smoke
```
exit code: 0
--- akoya.log ---
2026-10-24 18:55:52.119 +00:00 INFO Smoke run: models at C:\Users\runneradmin\AppData\Local\Temp\akoya-smoke-models-9080
2026-10-24 18:55:52.339 +00:00 INFO Akoya 1.0.0.0 starting
2026-10-24 18:55:52.768 +00:00 INFO Downloading the speech model
2026-10-24 18:55:52.783 +00:00 INFO Not installed by Setup.exe; updates off
2026-10-24 18:55:57.793 +00:00 INFO Smoke run finished
2026-10-24 18:55:57.794 +00:00 INFO Akoya stopping

PASSED
```
