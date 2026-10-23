# Mac pill: design

Date: 2026-10-22. Mockup: `docs/design/mac-pill-mockup.html` (approved by Rohit the same day).

## Intent

Bring the Windows dictation pill to the Mac as the default way to show dictation. Rohit wants it because a floating pill
near where you're working makes it obvious when Akoya is listening; the menu bar waveform is small and off in a corner.
A side benefit: the pill has room for words, so the Mac can finally say *why* a dictation failed instead of flashing
the menu bar icon red for every failure.

The menu bar waveform stays as an option ("Menu bar" mode), unchanged from today.

## Goals

- The pill looks and behaves like the Windows pill: same look, size, waveform, timer, transcribing sweep, "Pasted"
  result, messages, pop-in and fade-out. Rohit asked for it to be "very similar"; nothing Mac-specific was requested.
- Pill is the default for everyone, including people updating from a version without it.
- In pill mode, each failure gets its own message instead of the generic red flash.
- One setting switches between the pill and the menu bar waveform; both share the Waveform and Red while recording
  options.

## Non-goals

- No redesign of the pill. The ASCII-light restyle explored the same day is parked (`docs/design/ascii-light/`).
- No change to Menu bar mode: it keeps today's waveform, scanner and 0.6 s red flash exactly.
- No "Copied. Press ⌘V" message. On the Mac the hotkey itself needs Accessibility, so a blocked paste almost never
  happens; Rohit chose not to handle it.
- No "Couldn't paste" message (the Mac pasteboard can't be held by another app).
- No change to the Windows app.

## Anti-goals

- The pill must never take focus, steal a click, or appear in ⌘Tab / Mission Control as a window. A dictation pasting
  into the wrong place because the pill grabbed focus would make it worse than the menu bar.
- Two waveforms at once (pill plus menu bar) would look broken.

## Behaviour

### Look and motion (same as Windows)

Port these from the Windows app; the numbers are in `docs/FEATURES.md` ("Recording indicator") and the pure math is in
`windows/src/Akoya.Core/Ui/`:

- Pill 34 pt tall, radius 17, dark glass `#0E1222`→`#090D1E`, blue rim glow `#2F6BFF` (red `#EF4444` for alerts).
- Listening: 23 × 4 dot waveform (`PillDots.Listening`), Solid look as bars (`PillDots.SolidListening`), timer "m:ss".
  Red while recording: red `#FF6B6B` dots, rim stays blue.
- Transcribing: the scanner (`PillDots.Scanner`, 1.4 s back-and-forth), timer frozen. With Red while recording the lit
  columns are amber `#FBBF24`.
- Pasted: blue check badge + "Pasted", 0.9 s.
- Alerts: 1.6 s. Red ones have the red rim and "!" badge; "Getting the speech model ready…" is blue with no badge.
- Pop-in: the "jelly" pop (`PillPopIn`, 0.42 s). Hide: fade and sink 0.28 s (10 pt, scale 0.94),
  cubic-bezier(.2,.8,.2,1); shown again while fading, it rises back.
- Reduce Motion (System Settings > Accessibility > Display): plain fade in, instant hide, like Windows with animation
  effects off.

### Where it sits

- On the screen that holds the focused window of the frontmost app (read through Accessibility, which Akoya already
  has); if that can't be read, the screen with the menu bar's active space (`NSScreen.main`).
- Horizontally centred on that screen's visible frame.
- Vertically: 20 pt above the top of the Dock when the Dock is at the bottom. When the Dock is on the left or right,
  20 pt above the bottom of the visible frame. When the Dock auto-hides, the pill sits where it would be with the Dock
  showing, so the Dock never covers it when it pops up (same rule as Windows' auto-hidden taskbar). The 20 pt is
  Rohit-approved from the mockup (Windows uses 52 DIP above its taskbar; the Dock is taller and floats).
- Screens where the Dock isn't showing right now (a second display while the Dock is on another one, or a full-screen
  Space): when the Dock's position is bottom, every screen reserves the Dock's usual height, so the pill sits at the
  same height on every display and the Dock can never cover it when it moves there or pops up. When the Dock is on the
  left or right, the pill is 20 pt above the bottom edge on every screen. (Builder's call, settled when writing this
  up: the auto-hide rule extended to every screen.)
- Placed once when it appears; it doesn't follow window moves while showing.
- Shows on every Space and over full-screen apps. Click-through, never key or main, not in ⌘Tab or Mission Control.

### States and messages (pill mode)

Every failure today goes through `AppCoordinator.showError` / `flashError`. In pill mode, each gets a message:

| Case (today's red flash) | Pill |
| --- | --- |
| Hotkey pressed while the model is downloading or loading | Blue "Getting the speech model ready…" |
| Hotkey pressed after the model failed | Red "Speech model failed" (Retry stays in the dropdown) |
| Mic permission denied or not yet granted (`microphoneNotAuthorized`) | Red "Turn on your mic in System Settings" |
| No input device at all (`noInputDevice`) | Red "Microphone not detected" |
| Other audio start failures (`converterUnavailable`, `engineFailedToStart`) | Red "Microphone not detected" |
| Nothing said (silence gate) | The pill just fades, no message (as on Windows) |
| Empty transcript | The pill just fades |
| Transcription threw | The pill just fades (logged), as on Windows |
| Paste failed (Accessibility denied) | The pill just fades (rare; see non-goals) |
| Successful paste | "Pasted" |
| Screen lock / sleep mid-dictation | The pill hides; the coordinator's existing lock handling is unchanged |
| Esc cancel | The pill fades, no message |

When the chosen microphone is missing, the Mac keeps today's behaviour of falling back to the system default (Different
on purpose from Windows); "Microphone not detected" only shows when there is no input at all.

In pill mode a press during "Pasted" or an alert starts a new dictation (results and alerts are cosmetic, as on
Windows). Menu bar mode keeps today's rule (presses ignored during the 0.6 s red flash).

### Menu bar item in pill mode

Always the idle glyph: no waveform, no scanner, no red flash. Hide menu bar icon when idle keeps working. The item
still opens the dropdown.

### Onboarding

During setup practice ("Try it"), the pill stays hidden and the Try it card is the feedback, as on Windows. The menu bar
item shows the idle glyph in pill mode during practice.

### The setting

- The dropdown's "Menu bar icon" row is renamed **"Dictation indicator"**. Its value shows "Pill" or the waveform look
  in menu bar mode ("Pixel" / "Solid").
- Expanded, a new first option: **"Show while dictating"** segmented control, **Pill | Menu bar**. Then Waveform
  (Pixel | Solid), Red while recording, and "Hide menu bar icon when idle" (renamed from "Hide when idle") with the
  existing Spotlight note.
- Waveform and Red while recording apply to whichever indicator is chosen.
- Stored in UserDefaults; default Pill for new installs and upgrades alike.
- Switching while a dictation is in progress takes effect at the next dictation.

## Constraints

- macOS 14+, Apple Silicon, SwiftUI + AppKit, as today. The pill window is a borderless non-activating `NSPanel`
  hosting SwiftUI.
- Pure logic (dot opacities, pop-in frames, placement from screen/Dock geometry, message mapping) goes in `AkoyaCore`
  with Swift Testing tests, mirroring the Windows core's tests where they exist.
- `docs/FEATURES.md`: the Recording indicator and Messages sections change from "Different on purpose" / "Not applicable"
  to Same for the pill rows, with Menu bar mode noted; `docs/MANUAL_CHECKS.md` gets pill checks (Dock positions, full
  screen, Spaces, multiple displays, focus never stolen, each message).

## Left to the builder

- How the coordinator reports outcomes to the pill (e.g. an outcome/reason published alongside `phase`), as long as
  Menu bar mode's behaviour is unchanged.
- How the Dock's position and auto-hide are read (e.g. `com.apple.dock` defaults, or the difference between `frame` and
  `visibleFrame`), and the exact window level, as long as the pill shows over full-screen apps and stays click-through.
- Animation implementation (SwiftUI animations or a timeline), matching the Windows timings.
