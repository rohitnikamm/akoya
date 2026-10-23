# Built-in Speech Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the speech model inside the Mac and Windows apps so Akoya works offline on first launch, and delete any previously downloaded copy.

**Architecture:** Each app first looks for the model inside its own install; if it is complete it loads from there and, after a successful load, deletes the old downloaded copy; otherwise today's download path runs unchanged. Release builds fetch a pinned, checksum-verified copy of the model at build time (never committed) and put it into the app.

**Tech Stack:** Swift 6 + FluidAudio (Mac), C# / .NET 10 + sherpa-onnx + Velopack (Windows), bash and PowerShell build scripts, GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-10-23-built-in-model-design.md`

## Global Constraints

- Mac built-in location: `Akoya.app/Contents/Resources/Models/parakeet-tdt-0.6b-v2/` (FluidAudio's v2 layout: `Preprocessor.mlmodelc`, `Encoder.mlmodelc`, `Decoder.mlmodelc`, `JointDecision.mlmodelc`, `parakeet_vocab.json`, `config.json`).
- Windows built-in location: `<app dir>\models\sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8\` (`AppContext.BaseDirectory`), the four files and byte sizes already pinned in `ModelManifest`.
- Old copies to delete, only after the built-in model loaded successfully: Mac `~/Library/Application Support/Akoya/Models/parakeet-tdt-0.6b-v2` (and `Models` if then empty) and `~/Library/Application Support/Silk/Models`; Windows `%LocalAppData%\Akoya\models\` (including `.part` and extract leftovers).
- The built-in model is never deleted or modified by the app (not by Retry, not by "discard after a second failure").
- Model files are never committed to git. Mac pins live in one file (`scripts/model-pins.txt`); Windows pins stay in `ModelManifest`.
- Mac build cache: `~/Library/Caches/Akoya-build/models/`. `scripts/build-app.sh` includes the model only with `--with-model`; `scripts/release.sh` always passes it. Sign after copying.
- Credit: `ThirdPartyNotices.txt` (Mac `Contents/Resources/`, Windows next to `Akoya.exe`) and a README line: "Speech model: NVIDIA Parakeet TDT 0.6B v2, licensed CC-BY-4.0 (https://huggingface.co/nvidia/parakeet-tdt-0.6b-v2); Core ML conversion by FluidInference, ONNX int8 conversion by sherpa-onnx."
- Never push a `windows-v*` tag (CLAUDE.md). Windows App tests run only on Windows: use `windows/scripts/ci.sh`.
- Commits: no Claude co-author trailer, no "Generated with Claude Code".

## Review Focus

1. **Retry / discard never touches the built-in model** (Windows deletes the model folder after a second load failure): the Windows store tests pin it (Task 3).
2. **A built-in model that fails to load** falls back to the download path and leaves the downloaded copy alone: tests in Tasks 1 and 3.
3. **Developer builds without the model behave exactly as today** (download, progress, Retry): existing tests unchanged, plus a no-bundle test in Tasks 1 and 3.
4. **The signed Mac app stays valid after the model is added:** `codesign --verify --deep --strict` in Task 2.
5. **Users upgrading from Silk** (legacy folder) end with no leftover copy: test in Task 1.

---

### Task 1: Mac loads the built-in model and removes the downloaded copy

**Files:**
- Create: `Sources/AkoyaCore/Transcription/BuiltInModel.swift`
- Modify: `Sources/AkoyaCore/Transcription/ParakeetTranscriber.swift`
- Test: `Tests/AkoyaCoreTests/BuiltInModelTests.swift`, `Tests/AkoyaCoreTests/ParakeetTranscriberTests.swift` (opt-in real-model test)

**Interfaces:**
- Produces: `public enum BuiltInModel { static let folderName = "parakeet-tdt-0.6b-v2"; static func directory(in resources: URL?) -> URL? /* resources/Models/parakeet-tdt-0.6b-v2 if FluidAudio's AsrModels.modelsExist(at:version: .v2) says it's complete, else nil */; static func removeDownloadedCopies(modelsDirectory: URL, legacyDirectory: URL, fileManager: FileManager = .default) }`.
- `ParakeetTranscriber.init(modelsDirectory: URL = defaultDirectory, builtInDirectory: URL? = BuiltInModel.directory(in: Bundle.main.resourceURL))`.
- `prepare`: if `builtInDirectory` is non-nil → `AsrModels.loadLocal(from: builtInDirectory, version: .v2)`, load into `AsrManager`, report `.ready` (no `.downloading` progress), then `BuiltInModel.removeDownloadedCopies(...)`. If that load throws → log it and run today's download path unchanged (do not delete anything). If nil → today's path unchanged.

- [ ] **Step 1: Write the failing tests** (temp directories; no real model needed):

```swift
@Test func incompleteBundleIsNotUsed() { /* resources/Models/parakeet-tdt-0.6b-v2 with only config.json → directory(in:) == nil */ }
@Test func missingResourcesIsNotUsed() { #expect(BuiltInModel.directory(in: nil) == nil) }
@Test func removesTheDownloadedRepoFolderAndTheEmptyModelsFolder() { /* Models/parakeet-tdt-0.6b-v2/x → both gone */ }
@Test func keepsOtherFilesInTheModelsFolder() { /* Models/parakeet-tdt-0.6b-v2 + Models/other → repo gone, Models and other kept */ }
@Test func removesTheLegacySilkModels() { /* Silk/Models/... → gone; Silk folder itself kept */ }
@Test func removingWhenNothingIsThereIsANoOp() { /* no throw, nothing created */ }
```
Plus `@Test(.enabled(if: ProcessInfo.processInfo.environment["AKOYA_BUILT_IN_MODEL"] != nil)) func builtInModelLoadsAndTranscribes()` in ParakeetTranscriberTests: point `builtInDirectory` at that path, `prepare` reports only `.ready` (never `.downloading`), and `transcribe` of 1 s of silence returns without throwing.

- [ ] **Step 2: Run** `swift test --filter BuiltInModel` — expect compile failure.
- [ ] **Step 3: Implement** per Interfaces. `removeDownloadedCopies` ignores missing paths and logs (does not throw) on errors.
- [ ] **Step 4: Run** `swift test` — all pass (existing ParakeetTranscriber tests unchanged).
- [ ] **Step 5: Commit** `feat(mac): load the built-in speech model and remove the downloaded copy`

---

### Task 2: Mac build puts the pinned model into the app

**Files:**
- Create: `scripts/fetch-model.sh`, `scripts/model-pins.txt`, `Resources/ThirdPartyNotices.txt`
- Modify: `scripts/build-app.sh`, `scripts/release.sh`, `README.md`

**Interfaces:**
- `scripts/model-pins.txt`: first line `repo FluidInference/parakeet-tdt-0.6b-v2-coreml`, second `revision <full commit sha>`, then one line per file: `<sha256>  <path inside the repo>` for every file under the four `.mlmodelc` folders plus `parakeet_vocab.json` and `config.json` (get the commit and file list from `https://huggingface.co/api/models/FluidInference/parakeet-tdt-0.6b-v2-coreml/revision/main`; confirm the files match the copy in `~/Library/Application Support/Akoya/Models/parakeet-tdt-0.6b-v2` — that is what FluidAudio downloads today).
- `scripts/fetch-model.sh` → prints the path of a verified copy in `~/Library/Caches/Akoya-build/models/parakeet-tdt-0.6b-v2/`; downloads each pinned file from `https://huggingface.co/<repo>/resolve/<revision>/<path>` only if missing or its sha256 differs; exits non-zero on any mismatch.
- `scripts/build-app.sh [debug|release] [--with-model]`: with `--with-model`, run `fetch-model.sh` and `ditto` the folder into `Contents/Resources/Models/parakeet-tdt-0.6b-v2/` before `codesign`. Always copy `Resources/ThirdPartyNotices.txt` into `Contents/Resources/`. `release.sh` passes `--with-model`.

- [ ] **Step 1:** Write `model-pins.txt` and `fetch-model.sh`; run `scripts/fetch-model.sh` twice — the second run downloads nothing and prints the same path. Corrupt one cached file and re-run: it is re-downloaded.
- [ ] **Step 2:** Add `--with-model` and the notices to `build-app.sh`; run `scripts/build-app.sh debug --with-model`. Expect `du -sh build/products.noindex/Akoya.app` ≈ 450 MB, the model folder present, and `codesign --verify --deep --strict build/products.noindex/Akoya.app` passes.
- [ ] **Step 3:** Run `AKOYA_BUILT_IN_MODEL=build/products.noindex/Akoya.app/Contents/Resources/Models/parakeet-tdt-0.6b-v2 swift test --filter builtInModelLoadsAndTranscribes` — passes.
- [ ] **Step 4:** `scripts/build-app.sh debug` (no flag) still builds a small app without the model. `scripts/release.sh --dry-run <next version>` builds and zips with the model (do NOT run it without `--dry-run`). Note the zip size in the report.
- [ ] **Step 5:** `ThirdPartyNotices.txt` and the README credit line exactly as in Global Constraints. Commit `build(mac): ship the pinned speech model inside the app`.

---

### Task 3: Windows loads the built-in model and removes the downloaded copy

**Files:**
- Modify: `windows/src/Akoya.App/Speech/ModelStore.cs`, `windows/src/Akoya.App/AppAdapters.cs` (`SpeechModel`), `windows/src/Akoya.App/AppComposition.cs`
- Test: `windows/tests/Akoya.App.Tests/ModelStoreTests.cs`, `SpeechModelTests.cs`

**Interfaces:**
- `ModelStore(string modelsRoot, HttpClient http, ModelSpec spec, TimeSpan? inactivityTimeout = null, string? builtInRoot = null)` (keep the existing overloads; the 2-arg one passes `Path.Combine(AppContext.BaseDirectory, "models")`).
- `public bool UsesBuiltIn` — true when `builtInRoot/<FolderName>` passes the existing `IsComplete` check.
- `ModelDirectory` → the built-in folder when `UsesBuiltIn`, else today's download folder. `IsInstalled` true when either is complete. `EnsureAsync` returns at once when `UsesBuiltIn`.
- `Discard()` never deletes the built-in folder; when `UsesBuiltIn` it only deletes the downloaded copy.
- `public void RemoveDownloadedCopy()` — deletes `modelsRoot` entirely (model folder, `.part`, extract leftovers); never touches `builtInRoot`.
- `SpeechModel.Load`: after the transcriber is created successfully and `UsesBuiltIn`, call `RemoveDownloadedCopy()`. If loading the built-in model throws, keep today's behaviour (the existing failure/Retry path), but Retry must not delete the built-in folder.

- [ ] **Step 1: Write the failing tests** (temp dirs, fake files at the pinned names/sizes using the existing test helpers):

```csharp
[Fact] public void BuiltInCompleteIsUsed()            // UsesBuiltIn, ModelDirectory == built-in folder, IsInstalled
[Fact] public void BuiltInIncompleteFallsBackToDownloadFolder()
[Fact] public async Task EnsureDoesNothingWithBuiltIn() // no HTTP request made
[Fact] public void DiscardNeverDeletesBuiltIn()       // built-in files still there after Discard()
[Fact] public void RemoveDownloadedCopyDeletesOnlyTheDownloadRoot() // includes .part and extract leftovers
[Fact] public void LoadWithBuiltInRemovesTheDownloadedCopy()  // SpeechModelTests, with a fake transcriber factory if needed
```
- [ ] **Step 2: Run** locally `dotnet build windows/Akoya.Windows.slnx -c Release -p:EnableWindowsTargeting=true` (must compile); the App tests run on Windows only.
- [ ] **Step 3: Implement** per Interfaces.
- [ ] **Step 4: Run** `windows/scripts/ci.sh` (pushes the branch, waits for the `windows` workflow) — all tests pass.
- [ ] **Step 5: Commit** `feat(windows): load the built-in speech model and remove the downloaded copy`

---

### Task 4: Windows package puts the pinned model into the app

**Files:**
- Create: `windows/scripts/stage-model.ps1`, `windows/ThirdPartyNotices.txt`
- Modify: `windows/scripts/package.ps1`, `.github/workflows/windows.yml`, `.github/workflows/release-windows.yml` (cache only), `windows/tests/Akoya.App.Tests/SpeechModelTests.cs` (opt-in real-model test)

**Interfaces:**
- `stage-model.ps1 -Destination <dir> [-CacheDir <dir>]`: downloads `ModelManifest.Url` (read the URL, size and SHA-256 from `windows/src/Akoya.Core/Model/ModelManifest.cs`, single source), verifies size and SHA-256, extracts the four files into `<dir>\models\<FolderName>\`, verifies each file's pinned byte size; reuses a cached tarball whose hash matches.
- `package.ps1` calls it on the publish folder before `vpk pack`, and copies `ThirdPartyNotices.txt` next to `Akoya.exe`.
- `windows.yml`: cache the tarball (`actions/cache`, key = the pinned SHA-256), run `stage-model.ps1` into a temp folder, and set `AKOYA_BUILT_IN_MODELS` for the test step so an opt-in test (`[Fact] BuiltInModelLoadsAndTranscribes`, skipped when the variable is unset) creates `SpeechModel` with that built-in root, loads, and transcribes 1 s of silence without throwing. `release-windows.yml`: same cache before packaging.

- [ ] **Step 1:** Write `stage-model.ps1` and the opt-in test; wire `windows.yml`.
- [ ] **Step 2:** `windows/scripts/ci.sh` — the workflow stages the model (cache miss first run, hit second) and the opt-in test passes.
- [ ] **Step 3:** Wire `package.ps1` and the notices. Do not run the release workflow or push a tag; confirm by reading `release-windows.yml` that `package.ps1` runs after the cache step.
- [ ] **Step 4: Commit** `build(windows): ship the pinned speech model inside the app`

---

### Task 5: Docs

**Files:** `docs/FEATURES.md`, `docs/MANUAL_CHECKS.md`, `docs/MANUAL_CHECKS_WINDOWS.md`

- [ ] **Step 1:** FEATURES.md "Speech model" section: add a "Built in" row (Same: both apps ship the pinned model inside; download only as a fallback); update Download, Interrupted download and Where to say they apply only when the built-in model is missing; note the old downloaded copy is deleted after the built-in model loads; Uninstall rows (the model goes with the app). Sizes: Mac zip and Windows Setup.exe (use the numbers measured in Tasks 2 and 4).
- [ ] **Step 2:** Manual checks, both platforms: fresh install with networking off becomes Ready with no download; an update from a version with a downloaded model deletes the old copy (check the folder paths in Global Constraints); Windows: after one update, note the size of Velopack's local packages folder.
- [ ] **Step 3: Commit** `docs: built-in speech model in the feature list and manual checks`
