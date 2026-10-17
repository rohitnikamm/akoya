# Akoya Redesign: Design

Status: draft · 2026-10-17
Builds on: `docs/superpowers/specs/2026-10-17-silk-design.md` (dictation engine, hotkey,
paste, menu bar waveform — all unchanged unless stated here).
Visual targets: `docs/design/` (approved mockup sources and rendered glow targets).
Approved canvas: https://claude.ai/artifact/9Ep4HKyADg3Bq9CoFRgSzU

## Intent

Silk works; its UI doesn't feel finished. Rohit wants it to look and feel premium and
distinctive, inspired by a dark "glowing card" aesthetic (deep navy-black surfaces, colour
that builds inward from a card's rim, dot-matrix details). Alongside, the app is renamed
**Akoya**. Two surfaces get redesigned: the **dropdown** (what you see when you click the
menu bar icon) and the **first-launch onboarding**. Functionality stays the same.

## Goals

- Rename Silk → Akoya everywhere the user can see it, plus bundle id, Homebrew cask and repo.
- Replace the plain `NSMenu` with a custom dark dropdown panel (layout "B").
- Replace the onboarding window with a quick 3-step flow ending in a real practice dictation.
- A signature "rim glow" in indigo that appears only while you speak and follows your voice.
- Light haptic taps during onboarding.

## Non-goals

- Changes to transcription, hotkeys, paste behaviour or the menu bar waveform item.
- A light theme for the dropdown/onboarding (they are deliberately dark-only).
- Transcript history beyond the single most recent dictation.

## Anti-goals

- A glowing *outline* or halo outside cards (Rohit rejected this explicitly).
- Glow that pulses on a timer or while idle.
- The dropdown or onboarding stealing focus from the app you dictate into.
- Losing the user's clipboard or sending anything off-device (unchanged from Silk).

## Decided by Rohit

- Name **Akoya**; logo is the existing 5-bar waveform glyph, drawn as glowing dots at large
  sizes; wordmark "akoya" in lowercase.
- Dropdown = layout B (status card, Hold/Toggle switch, last dictation + Copy, Settings,
  Quit) in the dark style. Mockup: `docs/design/dropdown-mockup.html`.
- Onboarding = 3 steps. Screens 1–2 use the "V1" look (soft indigo gradient at the top of a
  dark card). Mockup: `docs/design/onboarding-screens-1-2-mockup.html` (first two cards).
- Screen 3 "Try it" uses the tuned **Edges** rim glow driven by voice level. Mockup:
  `docs/design/onboarding-screen-3-tryit-mockup.html`; rendered target
  `docs/design/tryit-glow-target.png` (rest / mid / peak).
- Glow in the dropdown sits on the **top status card only**, not the whole panel. Target:
  `docs/design/dropdown-card-glow-target.png`.
- Haptics: a light tap when each permission turns green, one when setup finishes.
- Settings window stays a standard Mac settings window, indigo accent.

## Visual language

| Token | Value | Use |
|---|---|---|
| `surface` | `#0B0B18` | card base |
| `panel` | gradient `#11112A` → `#08080F` | dropdown panel |
| `inner` | `rgba(40,40,58,0.6–0.85)` | pills, inner cards (like the reference's "Finish Test") |
| `indigo` | `#4F46E5` → `#3730A3` | selected segment, primary button |
| `lavender` | `#A5B4FC` / `#C7D2FE` | dots, small accents, Copy link |
| `textPrimary` / `textSecondary` | white / white 0.6–0.75 | |
| Font | Outfit (OFL, bundled), 400/500/600 | all custom UI; SF for Settings |
| Corner radii | panel 30, cards 22–24, onboarding window 52 (continuous/squircle) | |
| Dot grid | 1 pt dots, 10–12 pt pitch, white 0.06–0.08 | subtle texture on cards |

**V1 top gradient** (screens 1–2, dropdown card at rest): radial ellipse from top centre,
`rgba(99,102,241,0.30–0.45)` → `rgba(91,33,182,0.14–0.22)` → transparent by ~70%.

**Rim glow** (the signature): colour builds *inward* from the card edge and fades to the
dark centre; nothing is drawn outside the card. Reference recipe (CSS, from the approved
render — reproduce the look in SwiftUI, not the CSS):

```
Onboarding card (420×520):
  inset 0 -16px 30px -10px rgba(110,110,200,0.60)   /* brighter bottom */
  inset 0  18px 34px -12px rgba(120,112,176,0.55)   /* lavender top */
  inset 0 0  8px  1px rgba(120,114,172,0.70)
  inset 0 0 26px  6px rgba(78,84,156,0.70)
  inset 0 0 52px 14px rgba(40,52,128,0.60)
  inset 0 0 80px 18px rgba(18,32,100,0.45)
Dropdown status card (~292×170): same character, roughly half the blur/spread
  (see docs/design/dropdown-mockup.html for the exact values).
```

**Voice-driven intensity**: glow opacity = `0.2 + 0.6 × level` while recording (level 0…1,
smoothed), 0 at rest. Attack ≈ 100 ms, release ≈ 450 ms, so it breathes rather than flickers.
With *Reduce Motion* on: fixed 0.5 while recording, no breathing.

## Dropdown panel

- Clicking the menu bar item toggles a borderless, **non-activating** floating panel anchored
  under the item (right-aligned to it, 8 pt gap), 320 pt wide, height to content. Clicking
  outside, pressing Esc, or clicking the item again closes it. It must not take focus from
  the frontmost app.
- Contents, top to bottom (see mockup):
  1. **Status card**: "akoya" + state chip; dot-matrix logo (5 columns of 2/4/6/4/2 dots);
     "Hold [keycap] to talk" with the current hotkey's name. States:
     - Ready (chip "Ready", lavender dot)
     - Recording: chip "Listening", rim glow follows voice level
     - Transcribing: chip "Transcribing"
     - Model downloading: chip "Downloading 42%" with a thin progress bar in the card
     - Model failed: chip "Model failed" + "Retry" button
     - Permissions missing: chip "Setup needed" + "Finish setup" button (opens onboarding at step 2)
  2. **Mode switch**: two-segment pill "Hold to talk" / "Tap to toggle" (binds to settings).
  3. **Last dictation** card: most recent inserted text (in memory only, cleared on quit),
     "Copy" copies it to the clipboard. Hidden until there is one.
  4. **Footer**: "Settings" pill (opens Settings window) and a round Quit button.
- Launch at Login moves into Settings only (not in the panel).

## Onboarding

A single borderless dark window (420×520 card, continuous corners, centred), shown on first
launch and whenever required permissions are missing at launch. Three steps with page dots;
steps cross-fade (~250 ms).

1. **Welcome**: large dot logo, "akoya", "Talk instead of type, in any app. Everything stays
   on your Mac.", **Get started**.
2. **Permissions**: one card per missing permission, in order Microphone → Accessibility →
   Input Monitoring; chips at top show done/active/pending. For the active one: icon, title,
   one-line reason, live status ("Waiting for the switch…"), one primary button:
   - Microphone: **Allow** → system prompt (no Settings pane unless previously denied).
   - Accessibility / Input Monitoring: **Open System Settings** → request once (so Akoya is
     listed) and open the pane. Never fire both a prompt and a pane at once from separate
     clicks; one button, one action sequence.
   Polls every 0.5 s; when the active permission turns green: haptic tap, auto-advance to
   the next (skipping granted ones). All granted → step 3.
   Model download progress shows as a small footer line on steps 2–3 if still running.
3. **Try it**: "Hold [keycap] and say anything". Uses the **real** pipeline: the dot waveform
   follows the mic, the rim glow follows voice level, the transcript appears in the card
   instead of being pasted into another app. **Words appear live while you speak** (Rohit's
   decision): about every 0.6 s while recording, the same Parakeet model re-transcribes the
   audio captured so far and the card shows the result; on release, one final pass replaces
   it. Rules: never run two transcriptions at once (skip a tick if one is in flight); start
   live passes only after the recording has exceeded the silence-gate level and has ≥ 0.5 s
   of audio; partial text may revise itself as context grows. No new model or download; the
   everyday paste-on-release behaviour is unchanged. **Done** becomes primary after the first
   successful transcript; a quiet "Skip" link is always available. Finish → haptic, window
   closes.

## Rename to Akoya

- App/bundle name **Akoya**, bundle id `com.rohitnikamm.akoya`, executable `Akoya`,
  SwiftPM targets `AkoyaCore` / `Akoya`, status item autosave name `AkoyaStatusItem`,
  logging subsystem `com.rohitnikamm.akoya`.
- Models folder `~/Library/Application Support/Akoya/Models`. On launch, if it is missing
  and `…/Silk/Models` exists, **move** it (avoids a 450 MB re-download).
- Settings keys renamed `akoya.*`; values are not migrated (pre-release).
- Cask `akoya` (`brew install --cask rohitnikamm/tap/akoya`), repo `rohitnikamm/akoya`.
- Signing identity "Akoya Self-Signed" via `scripts/make-signing-cert.sh`; `build-app.sh`
  falls back to an existing "Silk Self-Signed" identity, then ad-hoc. (macOS permissions must
  be granted once more anyway because the bundle id changes.)
- Local folder renamed `~/Developer/akoya`.

## Calls made after review *(Claude's calls)*

- **Dropdown status card at rest** uses the V1 top gradient with no rim glow and no
  breathing. The rim glow appears only while recording (Rohit: "whenever they are talking,
  that's whenever it starts doing the glowing"). The glow target images and the mockup's
  breathing animation depict the *recording* state, not rest.
- **External renames**: nothing was ever published as Silk, so there is no repo or cask to
  rename; `rohitnikamm/akoya` and the `akoya` cask are created at first publish (which still
  needs Rohit's OK). The local folder move to `~/Developer/akoya` is the very last step,
  after merge, so it can't break work in progress. Old `com.rohitnikamm.silk` preferences
  are left alone.
- **App icon is in scope**: a generated `.icns` (dot-matrix waveform glyph on a dark
  continuous-corner square with a faint V1 gradient), built with `iconutil` in
  `build-app.sh`, `CFBundleIconFile` in Info.plist. It makes Akoya easy to find in the
  System Settings privacy lists the onboarding sends people to.

## Haptics

`NSHapticFeedbackManager.defaultPerformer.perform(.levelChange, performanceTime: .now)` when a
permission turns green; `.generic` when onboarding finishes. Nothing during dictation.

## Constraints

Unchanged from the Silk spec (Apple Silicon, macOS 14+, CLT-only build, unsigned/self-signed
distribution). SwiftUI's inner-shadow and continuous-corner APIs must be used only where
available on macOS 14, or have an equivalent fallback.

## Left to the builder

- How the rim glow is drawn in SwiftUI (layered blurred strokes masked to the shape, Canvas,
  or a Metal shader), as long as it matches the rendered targets side by side.
- Panel positioning details across multiple displays.
- Exact Outfit font files and how they're bundled/registered.

## Testing

- Unit: live-preview scheduling (interval, no overlap, silence gate, final pass replaces
  partial), dropdown status-chip state mapping (from phase/model/permission state), onboarding
  step logic (which step/permission is active given grant states), glow intensity mapping
  (level → opacity, Reduce Motion), Silk→Akoya models-folder migration, last-dictation store.
- Visual: render each new view with `ImageRenderer` to PNG and compare side by side with
  the targets in `docs/design/` before review.
- Manual (Rohit): first-launch onboarding end to end, dropdown open/close without stealing
  focus, glow following voice, Copy, Settings, Quit.
