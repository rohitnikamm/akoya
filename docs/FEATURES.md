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
| Silence gate | Loudest level step never above 2 (about -44 to -39 dBFS): not transcribed, brief red error cue | Lower: only a take that never moved the pill (loudest step 1, below about -44 dBFS) counts as silence, and it just fades, no cue. Quiet speech (step 2) is transcribed; a quiet take whose whole text is a filler word the model invents for near-silence ("Okay.", "Thank you.") is dropped. Each take's peak level is logged | Missing on Mac (the Mac drops quiet speech that its waveform shows; port open) |
| Empty transcript | Red error cue (0.6 s), nothing pasted | Pill fades, nothing pasted | Different on purpose (Windows spec) |
| Transcription error | Red error cue | Pill fades (logged) | Different on purpose (Windows spec) |
| Press while model not ready | Ignored with the red error cue | Pill shows "Getting the speech model ready…" (blue, 1.6 s), or red "Speech model failed" if it failed; nothing recorded | Different on purpose (the pill has room for words) |
| Press while transcribing | Ignored | Ignored | Same |
| Press during a result or alert | Ignored during the 0.6 s error cue | Starts a new dictation during "Pasted", "Copied" and "Couldn't paste", and a new attempt during the 1.6 s alerts (the mic may just have been plugged in) | Different on purpose (results and alerts are cosmetic on Windows) |
| Never two transcriptions at once | A live-preview pass still running is awaited before the final pass | Same | Same |
| Trailing space | Pasted text gets one trailing space | Same | Same |
| Paste method | Clipboard, then Cmd+V posted, needs Accessibility | Clipboard, then `SendInput` Ctrl+V; held modifiers released in the injected sequence | Different on purpose (platform APIs) |
| Clipboard after a dictation | Byte-for-byte snapshot of every item and type, restored after 300 ms unless the user copied something meanwhile | The dictated text stays on the clipboard as the latest copy, so Ctrl+V pastes it again anywhere (for example after switching windows mid-sentence); what was copied before is not put back but stays in Win+V history | Missing on Mac (Rohit's choice on Windows, 2026-10-20; the Mac still restores) |
| Clipboard history | Marks its entry `org.nspasteboard.TransientType` so clipboard managers skip it | An ordinary copy: it shows in Win+V history and cloud clipboard like any other | Different on purpose (the dictation is meant to stay as a copy on Windows) |
| Paste blocked fallback | Accessibility off: text left on the clipboard, red error cue, no message | Elevated (admin) foreground window: no paste, text left on the clipboard, blue pill "Copied. Press Ctrl+V" | Missing on Mac (a message saying the text is on the clipboard; unclear if wanted) |
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
| Recovery | Re-enables the event tap after a timeout; re-checks the held key against the live modifier state | Re-installs the hook after unlock, after waking from sleep, and on Retry when install failed ("Hotkey unavailable"); every 30 s it checks for a hook Windows removed silently (input per GetLastInputInfo more than 60 s after the hook's last key) and re-installs it, never during a recording and never when a key arrived meanwhile | Same |

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
| Built in | Shipped inside the app at `Contents/Resources/Models/parakeet-tdt-0.6b-v2`, loaded from there with no download; pinned in `scripts/model-pins.txt` (Hugging Face revision and checksums), added by release builds (`scripts/release.sh`, `build-app.sh --with-model`); debug builds only with `--with-model` | Shipped inside the app at `<app dir>\models\sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8`, loaded from there with no download; pinned in `ModelManifest`, added by `windows/scripts/stage-model.ps1` (run by `package.ps1`). A built-in model that fails to load is set aside for the session and Retry uses the downloaded copy or downloads one (the built-in is tried again next launch) | Same (both ship the pinned model; the download is only a fallback). The failed-load handling differs: on Windows a failed built-in load shows "Speech model failed" and Retry then uses or downloads a copy; on the Mac it falls straight through to the download. Different on purpose (Windows' existing load-failure handling and Retry were kept as they were; align the Mac later if wanted) |
| Old downloaded copy | After the built-in model loads, deletes `~/Library/Application Support/Akoya/Models/parakeet-tdt-0.6b-v2` (and `Models` if then empty) and `~/Library/Application Support/Silk/Models`; never deletes the built-in | After the built-in model loads, deletes `%LocalAppData%\Akoya\models\` (including `.part` and extract leftovers); never deletes the built-in | Same |
| Size | About 450 MB of model. App about 464 MB; release zip 462,617,365 bytes (about 441 MiB). A debug build without the model is about 31 MB | About 480 MB tarball (482,468,385 bytes). Setup.exe 550,013,004 bytes (about 524.5 MB) | Same |
| Download (fallback, only when the built-in model is missing or fails to load) | By FluidAudio at launch, in the background; whole-percent progress | From the sherpa-onnx GitHub release at launch, in the background, to a `.part` file; size and SHA-256 checked; extracted, then renamed into place | Different on purpose (different engines) |
| Interrupted download (fallback only) | Unclear (handled by FluidAudio) | Restarts cleanly on the next launch; every model file's byte size is checked, so a truncated file counts as missing | Different on purpose (unclear on Mac) |
| Retry | "Retry" in the dropdown and in setup | "Retry" in the tray and under setup's Try it waveform | Same |
| Where | Built in: inside the app bundle. Fallback download: `~/Library/Application Support/Akoya/Models` | Built in: `models\` next to `Akoya.exe` in Velopack's `current`, replaced with each update that changes it. Fallback download: `%LocalAppData%\Akoya\models\` (outside `current`) | Different on purpose (platform folders) |
| Warm-up | "Ready" once the model is loaded | "Ready" only after a warm-up transcription of 0.5 s of silence | Missing on Mac (unclear if Core ML needs it) |
| Hardware | Core ML (compute units chosen by FluidAudio, unclear) | CPU only, half the logical cores and at most 8 threads; a provider setting (Auto / Cpu) exists in settings.json but is ignored. A GPU path was measured and is not worth building yet (docs/superpowers/plans/2026-10-21-akoya-windows-gpu.md) | Different on purpose (CPU already transcribes a 7 s dictation in about 0.3 s) |
| Silk folder migration | Moves `~/Library/Application Support/Silk/Models` to the Akoya folder once (still runs first; `Silk/Models` and the migrated copy are deleted once the built-in model loads) | Not applicable | Different on purpose (Windows never had Silk) |

## Recording indicator

Mac: the menu bar item. Windows: a floating pill above the taskbar (no menu bar on Windows).

| Feature | Mac | Windows | Status |
| --- | --- | --- | --- |
| Where | Menu bar item, left of Control Center, keeps a Cmd-dragged position | Pill centred on the work area of the monitor holding the foreground window (primary if none), bottom 52 DIP above the top of that monitor's taskbar (100 DIP above the screen's bottom with the default taskbar), per-monitor DPI | Different on purpose (no menu bar on Windows) |
| At rest | Five-bar glyph (4, 8, 12, 8, 4 pt), 28 pt wide | Nothing (pill hidden) | Different on purpose (the tray icon sits in the overflow) |
| Listening | Live waveform, 22 columns, item widens to 77 pt | Live dot waveform, 23 columns by 4 rows of 3 px dots; middle rows lit from step 2, outer rows rise from step 4 and are full at step 7; plus a timer "m:ss" | Different on purpose (pill design; the menu bar has no room for a timer) |
| Waveform look | Pixel (stacked 2 pt cells) or Solid (bars), user choice | Pixel (dots) or Solid: per column one 3 DIP bar with round ends, one dot tall when quiet rising linearly to the four rows' height (Mac's Solid is linear in the level too); the sweep becomes bars the centre two rows tall | Same |
| Pixel waveform, round-dot shape (idea) | Not applicable | Proposed, not built: Pixel as round dots growing evenly from one dim centre line (1, 3 or 5 rows of 3 DIP dots, unlit dots hidden), with a small cluster gliding along the line while transcribing. Rohit is keeping today's Pixel for now and may switch later. Mockups and two rounds of designer reviews (14 reviewers): the "Pill Pixel Waveform" design canvas | Proposed |
| Red while recording | Optional: the waveform turns red | Optional (off by default): the listening dots or bars turn red `#FF6B6B`; the rim glow stays blue | Same |
| Amber sweep while processing when Red while recording is on | No (the scanner stays the menu bar colour) | Yes: the transcribing sweep's lit columns turn amber `#FBBF24` | Missing on Mac |
| Transcribing | Scanner: a bright head eases back and forth across the columns, period 1.4 s, tail opacities 0.7, 0.48, 0.32, base 0.18; two cells tall | Same scanner math on the centre two of the 4 rows across 23 columns, the outer rows staying at rest (Solid: bars two rows tall; amber lit columns with Red while recording); timer frozen | Same |
| Pasted | Item collapses back to the idle glyph | Blue check badge and "Pasted" for 0.9 s, then fades | Different on purpose (the menu bar item has no room for words) |
| Error look | Idle glyph flashes red for 0.6 s (also shown while "Hide when idle" is on) | Red rim glow, red "!" badge, white text, 1.6 s (see Messages) | Different on purpose (the pill has room for words) |
| Colours | Menu bar primary (black or white per menu bar); system red for tint and error | Dark glass (`#0E1222` to `#090D1E`), dots `#DBE6FF`, blue rim glow `#2F6BFF`, red `#EF4444`, timer white 50%, label white 85% | Different on purpose (menu bar vs floating pill) |
| Show and hide motion | Width tween 0.22 s, cubic-bezier(0.2, 0.8, 0.2, 1), content pinned right; cross-fade over the first 35% (0.12 s when the width does not change) | Pops in from hidden (below); fades and sinks out 0.28 s (10 px, scale 0.94), cubic-bezier(.2,.8,.2,1); shown again while fading out, it fades and rises back; animation effects off: plain fade in, instant hide | Different on purpose (menu bar vs floating pill) |
| Pop-in animation | Not applicable (no pill) | "Jelly" pop, 0.42 s: a 34 DIP circle grows in (scale 0.55 to 1, slight overshoot), stretches to 107% width and 92% height, springs to 98.5% and 103%, settles; contents fade in from 45%; recording and the timer start at once; plain 0.28 s fade with Windows' animation effects off | Different on purpose (the Mac has no pill) |
| Auto-hidden taskbar | Not applicable (no pill) | The pill is placed as if the taskbar were showing (52 DIP above its top), so it is at the same height shown or hidden and the popped-up taskbar never covers it | Different on purpose (the Mac has no pill) |
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
| Menu bar icon row | "Menu bar icon" with the Waveform value and ›, collapsed by default, expands inline (spring 0.32 s, chevron rotates): "Waveform" Pixel / Solid, "Red while recording", "Hide when idle", note "Hidden? Open Akoya from Spotlight to get back here." | "Pill" row between Microphone and Launch at startup, waveform glyph, the Waveform value and ›, collapsed each time the panel opens, expands inline (0.22 s, › turns, options fade down): "Waveform" Pixel / Solid, "Red while recording"; no note | Different on purpose (on Windows the waveform is the pill; Hide when idle and its note are left out) |
| Hide when idle | Switch: the menu bar item shrinks to nothing at rest | Left out: the tray icon already sits behind the ^ overflow and the pill only shows while talking | Different on purpose (nothing shows at rest on Windows anyway) |
| Launch at login / startup row | "Launch at login" switch with a power glyph | "Launch at startup" switch with a power glyph | Same |
| Update row | None | "Update ready" with a light-blue "Restart" text link (styled like Copy), shown once an update is downloaded | Missing on Mac (see Install and updates) |
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
| Model download starts | Only when the built-in model is missing; otherwise it loads at launch | Same | Same |
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
| Model status in setup | Footer on steps 2 and 3: "Getting the speech model ready…" (with %), "The speech model couldn’t load." with a small "Retry" | On Try it, under the waveform, whose dots fill left to right with the download: "Getting the speech model ready 62%" (or "…" without %); failed: "Model download failed · Retry", the dots stopped where it stopped. The text card keeps "Your words will appear here." | Different on purpose (no permissions step to carry it on Windows; the progress sits on the waveform the user is waiting to use) |
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
| "Getting the speech model ready…" | Onboarding footer (with %) | Blue pill on a press while loading (1.6 s); tray status (with %); under the Try it waveform, whose dots fill with the download (with %) | Different on purpose (see Dictation) |
| "Speech model failed" | None | Red pill on a press after the model failed, 1.6 s | Different on purpose (see Dictation) |
| Model failed (panel) | Chip "Model failed" with "Retry" | Status "Model download failed" (download) or "Speech model failed" (load; Retry loads the same files again, and when they fail a second time the model folder is deleted so Retry downloads it again) with "Retry" | Different on purpose (wording, unclear why) |
| "Setup needed" | Chip, a permission is missing, with "Finish setup" | Not applicable | Different on purpose (no permissions on Windows) |
| "Hotkey unavailable" | Not applicable (the Mac retries silently) | Tray status with "Retry" when the keyboard hook cannot install | Different on purpose (see Hotkey) |
| "Microphone blocked" | Not applicable | Tray status with "Open Settings" while the privacy switch is off | Different on purpose (see Microphone) |
| "Not detected" | "(unavailable)" after the device name | Amber line under "Microphone" in the yellow row | Different on purpose (see Microphone) |
| Mic usage string | "Akoya listens only while you hold the dictation key, and transcribes on this Mac." in the macOS prompt | Not applicable | Different on purpose (no prompt on Windows) |
| Accessibility names | "Akoya" (item), "Hotkey" with hint "Choose the key to hold or tap", "Copy last dictation", "Microphone", "Menu bar icon", "Model download" (percent), "Step N of 3", "Close" | "Microphone", "Pill" (value Pixel or Solid), "Waveform" with "Pixel" and "Solid", "Red while recording", "Copy last dictation", "Launch at startup", "Restart to update", keycap help "Choose the key to hold or tap", ✕ tooltip "Close" | Same |

## Install and updates

| Feature | Mac | Windows | Status |
| --- | --- | --- | --- |
| Requirements | macOS 14 or later, Apple Silicon only | Windows 11 target, self-contained win-x64 (no .NET install) | Different on purpose (platforms) |
| App icon | The dot logo on a navy rounded square (`Resources/AppIcon.icns`, made by `scripts/make-icon.swift`) | The same artwork as `Assets/akoya-app.ico` (16 to 256 px; 16 to 32 px drawn dot by dot so the dots stay sharp; made by `windows/scripts/make-app-icon.ps1`): Akoya.exe, the Start menu, the taskbar, Task Manager, Settings › Apps and Setup.exe | Same |
| Install | `brew install --cask rohitnikamm/tap/akoya` (the cask downloads the release zip) | `Akoya-win-Setup.exe` from the GitHub release ("Akoya for Windows"), per user, no admin prompt, Start menu entry and uninstaller | Different on purpose (platform conventions) |
| Unsigned | Not notarized; the cask clears the quarantine flag, so no Gatekeeper warning | Not code-signed; SmartScreen: "More info", then "Run anyway" once | Same |
| Updates | `brew upgrade`; no in-app check | In-app: checks GitHub releases at launch and every 6 h, downloads in the background, "Update ready" with "Restart" in the tray; Restart waits for a transcription to finish and cancels a recording; copies not installed by Setup.exe never update | Missing on Mac (in-app update check) |
| Uninstall | `brew uninstall` (the built-in model goes with the app); `--zap` also removes `~/Library/Application Support/Akoya` and the preferences | Settings > Apps: quits, removes the Start menu and startup entries and the model (built in, so it goes with the app; a fallback download folder too); `settings.json` may remain | Different on purpose (platform conventions) |
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
| Shutdown | A recording in progress is dropped, never pasted; practice mode ends | Same; also on sign-out and Windows shutdown. Quit during a paste waits for it to finish, for up to 10 s | Same |

## Open differences

- Dictation: quiet speech that moves the waveform is transcribed (Missing on Mac)
- Dictation: the dictated text stays on the clipboard (Missing on Mac)
- Dictation: paste blocked fallback (Missing on Mac)
- Dictation: screen lock mid-dictation (Missing on Mac)
- Speech model: warm-up before "Ready" (Missing on Mac)
- Tray / menu bar panel: update row (Missing on Mac)
- Messages and errors: "Copied. Press Ctrl+V" (Missing on Mac)
- Install and updates: in-app update check (Missing on Mac)
