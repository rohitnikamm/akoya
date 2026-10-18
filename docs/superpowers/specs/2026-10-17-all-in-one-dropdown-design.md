# All-in-one Dropdown: Design

Status: approved by Rohit (mockup) · 2026-10-17
Builds on: `2026-10-17-akoya-redesign-design.md` (visual language, electric-blue palette).
Visual target: `docs/design/all-in-one-dropdown-target.png` (exact CSS in the `.html` beside it):
left = normal, middle = hotkey menu open, right = Menu bar icon options expanded.

## Intent

Simplify Akoya's UI. Three reviewers found the dropdown's solid-blue Hold/Tap switch loud and
"cheap", the white Quit disc too prominent, and a separate Settings window unnecessary for a
handful of options. Rohit wants everything in the dropdown, all existing options kept, plus a
microphone picker.

## Decided by Rohit

- **No Settings window.** Everything lives in the dropdown.
- **Hold-or-tap hotkey; no mode setting.** Hold the key = push-to-talk; a quick tap starts
  recording and the next press stops it.
- **Clickable keycap** in "Hold [key] to talk" opens a small menu to pick Right Option / Right
  Command / Right Control.
- **Microphone picker** row.
- **Keep all three icon options** (waveform Pixel/Solid, red while recording, hide when idle) in
  a collapsible **Menu bar icon** row.
- **Neutral controls**: no solid-blue selected states. Blue stays for live states (listening
  glow, progress, status dot), the Copy link and real call-to-action buttons (Retry, Finish setup).

## Dropdown layout (top → bottom)

1. **Status card** — unchanged behaviour (V1 card at rest, compact rim glow while listening,
   states Ready/Listening/Transcribing/Preparing/Downloading/Model failed/Setup needed with their
   Retry / Finish setup buttons). Hint line: "Hold [Right ⌥ ▾] to talk" and a quieter second line
   "or tap it once to start, again to stop". The keycap is a button: clicking it opens the hotkey
   menu (section title "Hotkey", rows with key symbol and ✓ on the current one).
2. **Last dictation** card — unchanged (hidden until the first dictation), label contrast raised
   (≥ 4.5:1: white 62% at 11 pt uppercase).
3. **Options group** (one rounded inner card, hairline separators):
   - **Microphone** — value shows the current input's name (truncated) with ▾; clicking opens a
     menu: "System default (<current default name>)", then every input device, ✓ on the selected.
   - **Menu bar icon** — value shows "Pixel"/"Solid" with ›; clicking expands inline (animated,
     chevron rotates) to: Waveform segmented Pixel|Solid; "Red while recording" switch; "Hide when
     idle" switch; note "Hidden? Open Akoya from Spotlight to get back here."
   - **Launch at login** — switch.
4. **Quit Akoya** — a quiet text row (no white disc). No ⌘Q label: the panel never becomes key,
   so the shortcut can't work there, and showing it would mislead.

Removed: the Settings pill and the Hold/Tap segmented switch.

## Neutral control styles

- Segmented: track white 6% with 1 px white-8% inner hairline, radius 8; selected segment white
  14% with a 1 px white-18% top highlight, white semibold text; unselected text white 55%.
- Switch: off track white 10% (+ hairline), thumb white 55%; on track white 28%, thumb white
  with a small shadow. Thumb slides with a spring.
- Rows: 13 pt text white 88%, values white 62%, icons white 60%.

## Behaviour

- **Hold or tap.** Press starts recording. Release after ≥ 0.30 s stops it (push-to-talk).
  Release before 0.30 s keeps recording; the next press stops it. Esc cancels in both cases.
  `DictationMode` and the `akoya.mode` setting are removed. The silence gate still turns an
  accidental tap with no speech into the error cue.
- **Microphone.** Setting `akoya.inputDeviceUID` (nil = System default). Recording uses the
  chosen device if it is connected, otherwise the system default (and the row shows the device
  as unavailable). The device list refreshes when devices are added or removed.
- **Reopening the app** (Spotlight/Finder) opens the dropdown under the menu bar item (works even
  when the icon is hidden while idle). The old Settings window code is deleted.
- **The dropdown must still never take focus** from the frontmost app, including while its menus
  (hotkey, microphone) are open.

## Left to the builder

- How the hotkey and microphone menus are presented (NSMenu popped from the panel, or a SwiftUI
  `Menu`), as long as focus is never taken and they look native.
- Disclosure animation details.

## Testing

Unit: hold-vs-tap state machine (hold → stop on release; tap → keeps recording, next press
stops; Esc cancels both); microphone resolution (selected present → it; selected missing →
default + unavailable flag; nil → default); settings persistence for `inputDeviceUID`; removal
of `mode`. Visual: ImageRenderer renders of the dropdown (normal, options expanded) compared to
the target PNG. Manual: menus open without stealing focus; switching microphones works with
AirPods connecting/disconnecting.
