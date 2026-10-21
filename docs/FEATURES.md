# Akoya feature parity

One list of every user-facing feature of the Mac app (`Sources/`) and the Windows app (`windows/`), so the two stay
equally capable. Each row was checked against the code (Mac: `Sources/AkoyaCore`, `Sources/Akoya`; Windows:
`windows/src`), not only the specs. "Unclear" means the code did not settle it.

**Rule:** When you add or change a user-facing feature on one platform, update this file in the same change, and
either build it on the other platform or mark it here as Different on purpose (with the reason) or Missing (to do).

Status values: **Same**, **Different on purpose** (reason in the cell), **Missing on Windows**, **Missing on Mac**,
**Proposed** (in a mockup awaiting Rohit's OK, not built).

## Dictation

| Feature | Mac | Windows | Status |
| --- | --- | --- | --- |
| Hold to talk | Hold the hotkey, release after 0.30 s or more: stops, transcribes, pastes | Same, 0.30 s threshold measured between the hook's own stamps of the press and the release (a slow mic start or a busy UI thread doesn't change it) | Same |
| Tap to latch (hands-free) | Release under 0.30 s: keeps recording; the next press stops | Same | Same |
| Esc cancels | Esc while recording (held or latched) cancels, nothing pasted; Esc is swallowed only while recording. A long-held Esc may still auto-repeat into the app | Same; Esc auto-repeat is swallowed too | Same |
| Max length | 300 s, then stops as if released and pastes | Same | Same |
| Silence gate | Loudest level step never above 2 (about -44 to -39 dBFS): not transcribed, brief red error cue | Same gate; the pill just fades, no cue | Different on purpose (Windows spec: a silent take fades quietly) |
| Empty transcript | Red error cue (0.6 s), nothing pasted | Pill fades, nothing pasted | Different on purpose (Windows spec) |
| Transcription error | Red error cue | Pill fades (logged) | Different on purpose (Windows spec) |
| Press while model not ready | Ignored with the red error cue | Pill shows "Getting the speech model ready…" (blue, 1.6 s), or red "Speech model failed" if it failed; nothing recorded | Different on purpose (the pill has room for words) |
| Press while transcribing | Ignored | Ignored | Same |
| Press during a result or alert | Ignored during the 0.6 s error cue | Starts a new dictation during "Pasted", "Copied" and "Couldn't paste", and a new attempt during the 1.6 s alerts (the mic may just have been plugged in) | Different on purpose (results and alerts are cosmetic on Windows) |
| Never two transcriptions at once | A live-preview pass still running is awaited before the final pass | Same | Same |
| Trailing space | Pasted text gets one trailing space | Same | Same |
| Paste method | Clipboard, then Cmd+V posted, needs Accessibility | Clipboard, then `SendInput` Ctrl+V; held modifiers released in the injected sequence | Different on purpose (platform APIs) |
| Clipboard restore | Byte-for-byte snapshot of every item and type, restored after 300 ms unless the user copied something meanwhile | Byte-for-byte Win32 snapshot of every format (private app formats, Excel cells, an Explorer cut, PNG, password managers' history markers), restored after 300 ms unless something changed it meanwhile; the restore is marked excluded from Win+V history. A format that can't be copied, or over 64 MB: the clipboard is left alone and the text is typed instead | Same |
| Clipboard history | Marks its entry `org.nspasteboard.TransientType` so clipboard managers skip it | Marks it excluded from monitoring, Win+V history and cloud clipboard | Same |
| Paste blocked fallback | Accessibility off: text left on the clipboard, red error cue, no message | Elevated (admin) foreground window: no paste, text left on the clipboard, blue pill "Copied. Press Ctrl+V", no restore | Missing on Mac (a message saying the text is on the clipboard; unclear if wanted) |
| Clipboard busy | Not applicable (the pasteboard has no lock) | 10 tries, 20 ms apart; then red "Couldn't paste", text kept as Last dictation | Different on purpose (Windows clipboard can be held open by another app) |
| Mic unplugged mid-recording | Stops and transcribes what was captured (also on a default-input switch) | Stops and transcribes what was captured | Same |
| Screen lock mid-dictation | Nothing special | Lock or sleep cancels a recording; a dictation already transcribing is not pasted, its text is kept as Last dictation | Missing on Mac |
| Hotkey changed mid-recording | Unclear (the old key's release is probably read as a release, which stops) | Recording is cancelled | Different on purpose (unclear on Mac) |
| Dictation with the panel open | Pastes into the frontmost app (the panel never takes focus) | The hotkey closes the panel and gives focus back to the window the user was in, then records; the pill and the paste go to that window | Same |
| Protected input | Secure Keyboard Entry or password fields: the hotkey does nothing | Elevated window focused: the hotkey does not fire (Windows UIPI) | Different on purpose (OS limits) |

## Hotkey

| Feature | Mac | Windows | Status |
| --- | --- | --- | --- |
| Choices | Right Option (default), Right Command, Right Control | Right Alt (default), Right Ctrl, Copilot key | Different on purpose (platform keys) |
| Keycap label | "Right ⌥", "Right ⌘", "Right ⌃" | "Right Alt", "Right Ctrl", "Copilot key" | Different on purpose (platform keys) |
| Hotkey menu | Native menu, header "Hotkey", rows "Right Option", "Right Command", "Right Control" with the symbol right-aligned, check on the current one | In-panel menu, header "HOTKEY", rows "Right Alt", "Right Ctrl", "Copilot key", check on the current one, no symbols | Different on purpose (Windows keys have no symbols) |
| Swallowing the key | The modifier is not swallowed (apps see it) | The key's down and up are swallowed so a lone Alt never opens a menu; on AltGr layouts the fake Left Ctrl is swallowed too | Different on purpose (Windows Alt opens menus) |
| Copilot key | Not applicable | LWin + LShift + F23: F23 swallowed, a mask key injected so Start does not open | Different on purpose (Windows only) |
| Key repeat | Not applicable (modifier events do not repeat) | Auto-repeat ignored and swallowed | Same |
| Change applies at once | Yes, survives relaunch, Try it hint follows | Same | Same |
| Permission | Needs Accessibility; the listener retries every 2 s (and on app activation) until granted | None needed | Different on purpose (macOS privacy) |
| Recovery | Re-enables the event tap after a timeout; re-checks the held key against the live modifier state | Re-installs the hook after unlock, and on Retry when install failed ("Hotkey unavailable"); no detection of a hook Windows removed silently | Missing on Windows (silent hook removal; MANUAL_CHECKS_WINDOWS expects a reinstall after long uptime) |

## Microphone

| Feature | Mac | Windows | Status |
| --- | --- | --- | --- |
| Default choice | "System default" (follows macOS) unless the user picks one | Setup saves the highlighted device; "System default" is only chosen from the tray | Different on purpose (a saved device is not moved by Discord or Voicemeeter changing the default) |
| Names | CoreAudio device names as they are | Plain names: "This PC's microphone", "Headset mic", "Webcam mic" (or "Webcam mic (<model>)"), Bluetooth and USB model names, "Voicemeeter", duplicates get " 2", " 3" | Different on purpose (Windows endpoint names are unreadable) |
| Menu | "System default (<name>)" (or "System default"), a separator, then every input, check on the selected one | "System default (<name>)" (or "System default"), then every input including virtual ones, check on the selected one; no separator (unclear if on purpose) | Same |
| Row value | Current device name ("System default" when there is none), truncated, with ▾ | Same, max 150 px with ellipsis | Same |
| Chosen mic missing | Falls back to the system default silently; row "<name> (unavailable)" (or "Selected microphone (unavailable)"); menu lists it checked and greyed | No fallback, nothing recorded; red pill "Microphone not detected" (1.6 s); row highlighted yellow with "Not detected" and the saved name; tray status "Microphone not detected" | Different on purpose (Windows spec: no silent fallback) |
| No mic at all | Red error cue | Same as missing: red pill, yellow row | Different on purpose (the pill has room for words) |
| Hot-plug | Menu and row update live, no relaunch | Same | Same |
| Privacy | Microphone permission (TCC). Not asked yet: a press shows the system prompt and the red cue. Denied: red cue, no dialog | Windows privacy switch. Off: red pill "Turn on your mic in Settings", tray status "Microphone blocked" with "Open Settings" (`ms-settings:privacy-microphone`); clears once a recording works | Different on purpose (platform privacy models) |

## Speech model

| Feature | Mac | Windows | Status |
| --- | --- | --- | --- |
| Model | Parakeet TDT 0.6B v2 (English) through FluidAudio (Core ML) | Parakeet TDT 0.6B v2 int8 through sherpa-onnx | Same |
| On-device | Yes, no audio leaves the Mac | Yes, no audio leaves the PC | Same |
| Size | About 450 MB | About 480 MB tarball (482,468,385 bytes) | Same |
| Download | By FluidAudio at first launch, in the background; whole-percent progress | From the sherpa-onnx GitHub release at launch, in the background, to a `.part` file; size and SHA-256 checked; extracted, then renamed into place | Different on purpose (different engines) |
| Interrupted download | Unclear (handled by FluidAudio) | Restarts cleanly on the next launch; every model file's byte size is checked, so a truncated file counts as missing | Different on purpose (unclear on Mac) |
| Retry | "Retry" in the dropdown and in setup | "Retry" in the tray and in setup's Try it card | Same |
| Where | `~/Library/Application Support/Akoya/Models` | `%LocalAppData%\Akoya\models\` (outside Velopack's `current`, so updates keep it) | Different on purpose (platform folders) |
| Warm-up | "Ready" once the model is loaded | "Ready" only after a warm-up transcription of 0.5 s of silence | Missing on Mac (unclear if Core ML needs it) |
| Hardware | Core ML (compute units chosen by FluidAudio, unclear) | CPU only; a provider setting (Auto / Cpu) exists in settings.json but is ignored | Different on purpose (Windows spec: v1 CPU-only, GPU later) |
| Silk folder migration | Moves `~/Library/Application Support/Silk/Models` to the Akoya folder once | Not applicable | Different on purpose (Windows never had Silk) |

## Recording indicator

Mac: the menu bar item. Windows: a floating pill above the taskbar (no menu bar on Windows).

| Feature | Mac | Windows | Status |
| --- | --- | --- | --- |
| Where | Menu bar item, left of Control Center, keeps a Cmd-dragged position | Pill centred on the work area of the monitor holding the foreground window (primary if none), bottom 18 DIP above the work area, per-monitor DPI | Different on purpose (no menu bar on Windows) |
| At rest | Five-bar glyph (4, 8, 12, 8, 4 pt), 28 pt wide | Nothing (pill hidden) | Different on purpose (the tray icon sits in the overflow) |
| Listening | Live waveform, 22 columns, item widens to 77 pt | Live dot waveform, 23 columns by 4 rows of 3 px dots; middle rows lit from step 2, outer rows rise from step 4 and are full at step 7; plus a timer "m:ss" | Different on purpose (pill design; the menu bar has no room for a timer) |
| Waveform look | Pixel (stacked 2 pt cells) or Solid (bars), user choice | Dots only | Proposed (see Tray panel: Pill row) |
| Red while recording | Optional: the waveform turns red | Not built | Proposed (see Tray panel: Pill row) |
| Transcribing | Scanner: a bright head eases back and forth across the columns, period 1.4 s, tail opacities 0.7, 0.48, 0.32, base 0.18; two cells tall | Same scanner math across all 4 rows of 23 columns; timer frozen | Same |
| Pasted | Item collapses back to the idle glyph | Blue check badge and "Pasted" for 0.9 s, then fades | Different on purpose (the menu bar item has no room for words) |
| Error look | Idle glyph flashes red for 0.6 s (also shown while "Hide when idle" is on) | Red rim glow, red "!" badge, white text, 1.6 s (see Messages) | Different on purpose (the pill has room for words) |
| Colours | Menu bar primary (black or white per menu bar); system red for tint and error | Dark glass (`#0E1222` to `#090D1E`), dots `#DBE6FF`, blue rim glow `#2F6BFF`, red `#EF4444`, timer white 50%, label white 85% | Different on purpose (menu bar vs floating pill) |
| Show and hide motion | Width tween 0.22 s, cubic-bezier(0.2, 0.8, 0.2, 1), content pinned right; cross-fade over the first 35% (0.12 s when the width does not change) | Fade in and rise 0.28 s (10 px, scale 0.94), cubic-bezier(.2,.8,.2,1); fades and sinks out the same way | Different on purpose (menu bar vs floating pill) |
| Pop-in animation | Not applicable | "Jelly" pop: a circle that springs out past full width and settles, about 0.42 s (mockup `windows-pill-height-and-options-mockup.html`) | Proposed |
| Auto-hidden taskbar | Not applicable | Today the pill sits 18 DIP above the screen edge and the popped-up taskbar covers it. Proposed: keep the pill above the taskbar's height when it auto-hides | Proposed |
| Reduce motion | Reduce Motion: width jumps, short cross-fade only | "Animation effects" off: fades jump to the end | Same |
| During setup practice | The menu bar item still shows listening and transcribing | Pill stays hidden; the Try it card shows the dictation | Different on purpose (Windows: the card is the feedback) |
| Focus and clicks | Item is clickable (opens the dropdown) | Click-through, never takes focus, topmost, no taskbar button, not in Alt+Tab | Different on purpose (the pill only shows while talking) |
| Fullscreen | Unclear | Exclusive-fullscreen games hide the pill; dictation still works | Different on purpose (Windows limit, accepted) |

## Tray / menu bar panel

| Feature | Mac | Windows | Status |
| --- | --- | --- | --- |
| Icon | Menu bar item (five-bar glyph), VoiceOver label "Akoya" | Dot logo (2-4-6-4-2) in the tray overflow behind ^, white dots on a dark taskbar and near-black on a light one, tooltip "Akoya"; can be dragged onto the taskbar | Different on purpose (platform conventions) |
| Opening | Left or right mouse-down toggles; reopening Akoya (Spotlight, Finder) opens it | Left or right click toggles; launching Akoya again opens it | Same |
| Panel | 320 pt wide, dark, radius 30, under the item right-aligned 8 pt below, never takes focus | 300 DIP wide, dark, radius 30, in the tray corner 12 DIP inside the work area (any taskbar edge), activates like Windows flyouts | Different on purpose (Windows flyout convention) |
| Closing | Click outside, Esc (the app sees Esc too), a second click on the item, a Space change | Losing focus, Esc (first closes an open menu), a click in its shadow, a second click on the icon, the dictation hotkey. Esc, the shadow, the icon and the hotkey give focus back to the window the user was in; a click on another window leaves it there | Different on purpose (platform conventions) |
| Status card header | "Akoya" and a status chip (blue dot) | "Akoya" and a status dot plus text (blue for Ready and busy, amber for warnings, red for errors) | Different on purpose (Windows has warning states) |
| Status texts | "Ready", "Listening", "Transcribing", "Preparing…" (no progress yet), "Downloading 42%", "Model failed", "Setup needed" | "Ready", "Listening", "Transcribing", "Getting the speech model ready…" (with " 42%" when known, shown under the hint), "Model download failed", "Speech model failed" (downloaded but would not load), "Microphone not detected", "Microphone blocked", "Hotkey unavailable" | Different on purpose (Windows spec reuses the setup wording; Mac's permission states do not exist on Windows) |
| Status buttons | "Retry" (model failed), "Finish setup" (permission missing, opens onboarding at the first missing permission), both when both apply | "Retry" (model failed or hotkey unavailable), "Open Settings" (mic blocked) | Different on purpose (different failure states) |
| Download progress bar | 180 by 3 bar under the hint | Same | Same |
| Logo and hint | Dot logo; "Hold [keycap ▾] to talk"; "or tap it once to start, again to stop" | Same | Same |
| Voice glow | Status card rim glow follows the voice while listening (0.2 floor, attack 0.10 s, release 0.45 s), none at rest | Same | Same |
| Glow with reduced motion | Reduce Motion: fixed glow while recording | "Animation effects" off: the same fixed glow (opacity 0.5) while listening, none at rest | Same |
| Last dictation | "LAST DICTATION" card with "Copy" (flips to "Copied" for 1.5 s), up to 4 lines; hidden until the first dictation; only set by a successful paste; in memory only | Same card; also set when the paste failed, only copied, or the screen locked; Copy refused while a paste is in progress, "Copied" only when it worked | Different on purpose (Windows keeps the words when the paste did not land) |
| Menu bar icon row | "Menu bar icon" with the Waveform value and ›, collapsed by default, expands inline (spring 0.32 s, chevron rotates): "Waveform" Pixel / Solid, "Red while recording", "Hide when idle", note "Hidden? Open Akoya from Spotlight to get back here." | Not built | Proposed (Windows "Pill" row with the same chevron: "Waveform" Pixel / Solid and "Red while recording" applied to the pill; only the dots turn red, the glow stays blue) |
| Hide when idle | Switch: the menu bar item shrinks to nothing at rest | Left out of the proposal: the tray icon already sits behind the ^ overflow and the pill only shows while talking | Proposed |
| Launch at login / startup row | "Launch at login" switch with a power glyph | "Launch at startup" switch with a power glyph | Same |
| Update row | None | "Update ready" with a blue "Restart" button, shown once an update is downloaded | Missing on Mac (see Install and updates) |
| Quit | Quiet "Quit Akoya" text row, no shortcut label | Same | Same |
| Neutral controls | Grey and white switches and segments, no solid-blue selected states | Same | Same |

## Setup / onboarding

| Feature | Mac | Windows | Status |
| --- | --- | --- | --- |
| Steps | Welcome, Permissions (Microphone, then Accessibility, skipping granted ones), Try it | Welcome, Microphone, Try it | Different on purpose (Mac needs permissions; Windows needs a mic pick, chosen by three Windows reviewers) |
| When it shows | First launch; a relaunch before finishing resumes at the first missing permission; after finishing, only a missing permission brings it back | When setup is not done or the speech model is missing | Different on purpose (no permissions on Windows) |
| Window | 420 by 520 card, radius 52, centred, dragged by its background, ✕ in the corner, page dots, not in the Dock | 380 by 520 card, radius 44, centred on the primary monitor, dragged by the card, ✕ in the corner, page dots, has a taskbar button titled "Akoya" | Different on purpose (Windows mockup) |
| Card look | Dark card, dot grid (Welcome, Permissions), line grid (Try it); no rim glow at rest | Fixed blue rim glow, line grid over the lower part on every step | Different on purpose (Windows mockup) |
| Step change | Cross-fade 250 ms | Same | Same |
| Welcome | Dot logo, "Akoya", "Talk instead of type, in any app." / "Everything stays on your Mac.", "Get started" (Return) | Same with "Everything stays on your PC.", Enter | Same |
| Model download starts | At launch | At launch | Same |
| Permission chips | Green (granted), blue (current), grey (pending) | Not applicable | Different on purpose (no permissions on Windows) |
| Microphone permission | "Microphone", "Lets Akoya hear you while you talk. Your voice never leaves your Mac.", "Allow" (system prompt) or "Open System Settings" if denied before, "Waiting for your answer…" | Not applicable | Different on purpose (no permissions on Windows) |
| Accessibility permission | "Accessibility", "Lets Akoya paste your words and notice your hotkey. Switch on Akoya in the list that opens.", "Open System Settings", "Waiting for the switch…" | Not applicable | Different on purpose (no permissions on Windows) |
| Auto-advance | Polls every 0.5 s; moves on once the switch is on; raises the window on Try it after the last grant | Mic privacy screen polls every 0.5 s and shows the list once the switch is on | Same |
| Haptics | Light tap per granted permission, another on Done (Force Touch trackpad) | None | Different on purpose (no haptics on Windows PCs) |
| Mic list | Not applicable | Every input by plain name with a live 10-dot meter; virtual and loopback inputs folded into "More (n)" (expands in place); the Windows default highlighted at first; Akoya never moves the highlight itself | Different on purpose (Windows only step) |
| Mic status chip | Not applicable | Green "Hearing you" (highlighted mic heard speech in the last 0.5 s); amber "<name> isn't hearing you" (another mic has heard speech for 3 s); none before speaking | Different on purpose (Windows only step) |
| Mic titles | Not applicable | "Which mic do you talk into?" / "Say something. Pick the one that moves."; one mic: "Is this the mic you want?" / "Say something. The dots should move."; none: "No mic found" / "Plug one in. It will show up here." | Different on purpose (Windows only step) |
| Mic blocked screen | Not applicable | "Turn on your mic" / "Windows is blocking it. Open Settings and switch on microphone access for desktop apps.", "Open Settings", "Waiting for the switch…" | Different on purpose (Windows only step) |
| Continue | Not applicable | "Continue" saves the highlighted device (dimmed and inert with no mic) | Different on purpose (Windows only step) |
| Model status in setup | Footer on steps 2 and 3: "Getting the speech model ready…" (with %), "The speech model couldn’t load." with a small "Retry" | In the Try it card only: "Getting the speech model ready… 62%" (or without %), "Model download failed" with "Retry" | Different on purpose (no permissions step to carry it on Windows) |
| Try it hint | "TRY IT", "Hold [keycap] and say anything", "or tap it once to start, again to stop"; dimmed to 40% while the model is not ready | Same | Same |
| Try it waveform | 22 columns of 5 pt dots, 1 to 9 per column (odd), dim at rest, bright while recording | 23 columns by 9 rows of 5 px dots, same counts and dimming | Same |
| Live words | Text card fills in while speaking (pass every 0.6 s once 0.5 s of audio is above the silence gate), then the final text; nothing is pasted | Same | Same |
| Text card | "Your words will appear here."; up to 6 lines, older words cut at the top | Same placeholder; scrolls to keep the newest words in view | Same |
| Missed take | Silence, empty or failed take: text cleared, "Didn’t catch that [em dash] try again" (the Mac string has an em dash) | Silent, empty or failed take: previous good take kept, or "Didn't catch that. Try again." if none; Esc changes nothing | Same (period instead of the em dash) |
| Voice glow on Try it | Rim glow rises from the bottom edge with the voice and the grid brightens (fixed glow with Reduce Motion) | The Mac's rim glow recipe rises over the card's fixed glow with the voice (same envelope, fades after the take) and the line grid brightens up to twice its rest strength; fixed glow with Animation effects off | Same |
| Done | Quiet and inert until the first final transcript, then light; Return presses it | Same, Enter presses it | Same |
| Skip | On Try it only | On Microphone and Try it | Different on purpose (no permissions to finish on Windows) |
| ✕ | Counts as Skip on Try it or with every permission on; otherwise setup is not marked done | Always ends setup and marks it done; other closes (Alt+F4, sign-out) do not | Different on purpose (Mac must come back for permissions) |
| After setup | Practice mode ends however the window closes; a practice recording still running is cancelled | Same | Same |
| Reopen setup later | "Finish setup" in the dropdown while a permission is missing | No way back in; everything is in the tray | Different on purpose (no permissions on Windows) |

## Messages and errors

Every user-visible string not already quoted above, with when it appears.

| Feature | Mac | Windows | Status |
| --- | --- | --- | --- |
| Generic error cue | Red idle glyph, 0.6 s: failed start, mic denied, no mic, silence, empty or failed transcript, model not ready, paste blocked | None generic; each case below has its own message or fades | Different on purpose (the pill has room for words) |
| "Pasted" | None | Blue check, after a successful paste, 0.9 s | Different on purpose (see Recording indicator) |
| "Copied. Press Ctrl+V" | None | Blue check, foreground app is elevated or input was blocked, 0.9 s | Missing on Mac (see Dictation: paste blocked fallback) |
| "Couldn't paste" | None | Red, another app holds the clipboard, 1.6 s | Different on purpose (no pasteboard lock on Mac) |
| "Microphone not detected" | None | Red pill (saved mic or every mic missing, 1.6 s); also the tray status | Different on purpose (see Microphone) |
| "Turn on your mic in Settings" | None | Red pill, privacy switch off, 1.6 s | Different on purpose (see Microphone) |
| "Getting the speech model ready…" | Onboarding footer (with %) | Blue pill on a press while loading (1.6 s); tray status (with %); Try it card (with %) | Different on purpose (see Dictation) |
| "Speech model failed" | None | Red pill on a press after the model failed, 1.6 s | Different on purpose (see Dictation) |
| Model failed (panel) | Chip "Model failed" with "Retry" | Status "Model download failed" (download) or "Speech model failed" (load; the model folder is deleted so Retry downloads it again) with "Retry" | Different on purpose (wording, unclear why) |
| "Setup needed" | Chip, a permission is missing, with "Finish setup" | Not applicable | Different on purpose (no permissions on Windows) |
| "Hotkey unavailable" | Not applicable (the Mac retries silently) | Tray status with "Retry" when the keyboard hook cannot install | Different on purpose (see Hotkey) |
| "Microphone blocked" | Not applicable | Tray status with "Open Settings" while the privacy switch is off | Different on purpose (see Microphone) |
| "Not detected" | "(unavailable)" after the device name | Amber line under "Microphone" in the yellow row | Different on purpose (see Microphone) |
| Mic usage string | "Akoya listens only while you hold the dictation key, and transcribes on this Mac." in the macOS prompt | Not applicable | Different on purpose (no prompt on Windows) |
| Accessibility names | "Akoya" (item), "Hotkey" with hint "Choose the key to hold or tap", "Copy last dictation", "Microphone", "Menu bar icon", "Model download" (percent), "Step N of 3", "Close" | "Microphone", "Copy last dictation", "Launch at startup", "Restart to update", keycap help "Choose the key to hold or tap", ✕ tooltip "Close" | Same |

## Install and updates

| Feature | Mac | Windows | Status |
| --- | --- | --- | --- |
| Requirements | macOS 14 or later, Apple Silicon only | Windows 11 target, self-contained win-x64 (no .NET install) | Different on purpose (platforms) |
| Install | `brew install --cask rohitnikamm/tap/akoya` (the cask downloads the release zip) | `Akoya-win-Setup.exe` from the GitHub release ("Akoya for Windows"), per user, no admin prompt, Start menu entry and uninstaller | Different on purpose (platform conventions) |
| Unsigned | Not notarized; the cask clears the quarantine flag, so no Gatekeeper warning | Not code-signed; SmartScreen: "More info", then "Run anyway" once | Same |
| Updates | `brew upgrade`; no in-app check | In-app: checks GitHub releases at launch and every 6 h, downloads in the background, "Update ready" with "Restart" in the tray; Restart waits for a transcription to finish and cancels a recording; copies not installed by Setup.exe never update | Missing on Mac (in-app update check) |
| Uninstall | `brew uninstall`; `--zap` also removes `~/Library/Application Support/Akoya` and the preferences | Settings > Apps: quits, removes the Start menu and startup entries and the model folder; `settings.json` may remain | Different on purpose (platform conventions) |
| Releases | `scripts/release.sh VERSION`: GitHub release `vX.Y.Z` and Homebrew tap update | Tag `windows-vX.Y.Z`: draft release with Setup.exe and Velopack packages; Rohit publishes it, unticking "Set as the latest release" | Different on purpose (separate channels) |
| Upgrading from Silk | Asks a running Silk to quit at launch; models moved; permissions asked again (new bundle id) | Not applicable | Different on purpose (Windows never had Silk) |

## Launch at login / startup

| Feature | Mac | Windows | Status |
| --- | --- | --- | --- |
| Switch | "Launch at login", off by default | "Launch at startup", off by default | Same |
| Mechanism | `SMAppService.mainApp` (System Settings > General > Login Items) | `HKCU\...\Run\Akoya` pointing at the Velopack stub `%LocalAppData%\Akoya\Akoya.exe`, so it survives updates; shows in Task Manager > Startup apps | Different on purpose (platform APIs) |
| Failure | The switch springs back | Same | Same |
| Truth on launch | Read from `SMAppService` | Read from the Run entry (its path refreshed) | Same |
| Removed on uninstall | Unclear | Yes, by the uninstall hook | Different on purpose (unclear on Mac) |

## Other

| Feature | Mac | Windows | Status |
| --- | --- | --- | --- |
| Single instance | Newest launch wins: asks other running copies (for example a dev build next to the Homebrew one) to quit | First launch wins: per-user named mutex; a second launch opens the running copy's panel and exits | Different on purpose (Mac case is two different app copies) |
| Logs | Unified log, subsystem `com.rohitnikamm.akoya` (Console.app); device names private; no transcripts | `%LocalAppData%\Akoya\logs\akoya.log`, rolled at 1 MB, three files; no transcripts | Different on purpose (platform conventions) |
| Settings storage | UserDefaults `com.rohitnikamm.akoya` | `%AppData%\Akoya\settings.json`, atomic save; corrupt file or bad values fall back to defaults; an unreadable file is never overwritten | Different on purpose (platform conventions) |
| No settings window | Everything is in the dropdown | Everything is in the tray panel | Same |
| Look | Dark only, Outfit font, electric-blue accents (`#2F6BFF` to `#1D4ED8`) | Same | Same |
| Shutdown | A recording in progress is dropped, never pasted; practice mode ends | Same; also on sign-out and Windows shutdown. Quit during a paste waits for it and its clipboard restore to finish | Same |

## Open differences

- Dictation: paste blocked fallback (Missing on Mac)
- Dictation: screen lock mid-dictation (Missing on Mac)
- Hotkey: recovery from a hook Windows removed silently (Missing on Windows)
- Speech model: warm-up before "Ready" (Missing on Mac)
- Tray / menu bar panel: update row (Missing on Mac)
- Messages and errors: "Copied. Press Ctrl+V" (Missing on Mac)
- Install and updates: in-app update check (Missing on Mac)
