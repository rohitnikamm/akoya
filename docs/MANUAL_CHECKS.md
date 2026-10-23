# Manual checks (Akoya)

Nothing here could be checked automatically: it needs a screen, a microphone and macOS permissions.

## Setup
1. Optional, but recommended so permissions survive rebuilds: `scripts/make-signing-cert.sh`, then run the trust command it prints. Do this BEFORE granting any permissions, or skip it if the "Silk Self-Signed" identity already exists (it keeps permissions across rebuilds too). Switching identities after granting requires granting again.
2. `scripts/build-app.sh debug 0.2.0 && open build/products.noindex/Akoya.app`
3. To see the first-run flow again: quit Akoya, `defaults delete com.rohitnikamm.akoya`, and revoke Akoya's Microphone and Accessibility entries in System Settings > Privacy & Security.

## Upgrading from Silk (once)
- [ ] First: quit Silk, turn off its Launch at Login, and delete the old Silk.app. (Akoya also asks a running Silk to quit at launch: start Silk, then Akoya, and Silk's menu bar item disappears. Without this both apps would paste every dictation twice.)
- [ ] The bundle id changed (`com.rohitnikamm.silk` to `com.rohitnikamm.akoya`), so macOS treats Akoya as a new app: Microphone and Accessibility must be granted again once. Onboarding walks through this; the old "Silk" entries in the privacy lists can be removed.
- [ ] If `~/Library/Application Support/Silk/Models` existed, it is moved to `~/Library/Application Support/Akoya/Models` on first launch (`ls` both), with no ~450 MB re-download. The dropdown chip says "Preparing…" while the existing model loads (it may skip the download percentage entirely), then Ready. Old Silk preferences are untouched.

## Onboarding (first launch, or after the reset in Setup step 3)
- [ ] The 420x520 dark card opens centred, with the dot logo, "Akoya", and "Get started". Drag it by its background; the x in the corner closes it.
- [ ] "Get started" goes to the first missing permission. Order is Microphone, then Accessibility; already-granted ones are skipped. Chips at the top show granted (green), current (blue) and pending (grey).
- [ ] Microphone: "Allow" shows the system prompt. After you click Allow in the prompt, the step advances on its own within about half a second. (If it was denied earlier, the button opens the Microphone pane instead.)
- [ ] Accessibility: "Open System Settings" opens the right pane with Akoya in the list (with the new app icon). The status reads "Waiting for the switch…"; flipping Akoya on advances to the next step on its own, with no button press. The hotkey and dictation work from this permission alone.
- [ ] A subtle haptic tap fires each time a permission turns green (trackpad Mac), and a different one when you press Done.
- [ ] While the model is still downloading, a "Getting the speech model ready…" line (with %) shows on steps 2 and 3.
- [ ] Try it: the hotkey hint names your current key ("Hold Right ⌥ and say anything") with a quieter second line "or tap it once to start, again to stop". Holding it makes the blue glow rise from the bottom edge following your voice, and the grid brightens; your words appear live while you speak, then settle as the final text. Nothing is pasted anywhere, even with another app frontmost.
- [ ] Say nothing or something unintelligible: "Didn’t catch that — try again" appears.
- [ ] "Done" is quiet and does nothing until the first transcript arrives; after that it turns light and closes setup. "Skip" closes setup without needing a dictation. Either way setup is not shown on the next launch (if all permissions are still granted).
- [ ] Closing with the x mid-flow: dictation works normally again (it pastes, no longer goes to the practice card). Setup is not marked complete: the next launch restarts at the welcome step if no permission is on yet, or at the first missing permission if at least one already is. The exception is the x on the Try it step, or with both permissions on, which counts as Skip (not shown again). After Done or Skip, a later launch with a permission missing resumes at the first missing permission.
- [ ] When the last permission is switched on in System Settings, the setup window comes to the front on the Try it step by itself (Done and Return work without an extra click).
- [ ] Try it: Return presses Done once it is light (after the first transcript). A "Didn’t catch that" hint from a failed paste-mode dictation made earlier is not shown when the step opens.
- [ ] Model failure during setup (network off on a fresh model): steps 2 and 3 show "The speech model couldn’t load." with a small Retry that restarts the download. The dropdown with a permission still missing shows "Setup needed" with both "Finish setup" and "Retry".
- [ ] Reduce Motion on (System Settings > Accessibility > Display): the glow still follows the voice but without the breathing animation.

## Dropdown
- [ ] Click the Akoya menu bar item: a dark 320 pt panel opens under it, right-aligned. The app you were typing in stays frontmost and keeps its text cursor (the panel never steals focus).
- [ ] Click outside, press Esc, or click the item again: the panel closes.
- [ ] Status card at rest shows only the dot logo and "Hold [Right ⌥ ▾] to talk", centred, with no title, no status chip, no hint and no rim glow; the dropdown is the same size as before. While the model downloads, after it fails, or with a permission missing, one line appears under the Hold line ("Downloading speech model · 42%", red "Model failed" with Retry, "Setup needed" with Finish setup). The hotkey menu ends with a greyed "Hold to talk, or tap once to start and again to stop."
- [ ] While you hold the hotkey with the panel open: the chip says "Listening" and the blue rim glow follows your voice; it says "Transcribing" while the model works, then returns to Ready with no glow.
- [ ] First launch (no model yet): chip "Preparing…" until the first progress report, then "Downloading 42%" with a thin progress bar; it becomes "Ready" when done. With the network off on a fresh model: chip "Model failed" and a Retry button; Retry restarts the download.
- [ ] With a permission revoked: chip "Setup needed" and a "Finish setup" button, which closes the panel and opens onboarding at the first missing permission. Open the panel after granting it in System Settings: the state is re-checked on open and becomes Ready.
- [ ] "Last dictation" is hidden until you dictate; then it shows the inserted text. "Copy" copies it (the label flips to "Copied") and pasting elsewhere gives that text. It is cleared when you quit Akoya.
- [ ] There is no Settings window or Settings button anywhere: the dropdown holds everything. Below the status and last-dictation cards is one group with Microphone, Dictation indicator and Launch at login, then a quiet "Quit Akoya" text row (no round button, no ⌘Q label).

### Hotkey
- [ ] Click the keycap in "Hold [Right ⌥ ▾] to talk": a small "Hotkey" menu opens with Right Option, Right Command and Right Control and a check on the current one. The app you were typing in stays frontmost and keeps its text cursor while the menu is open.
- [ ] Choosing another key updates the keycap straight away and the new key works to dictate. Right Command and Right Control both work; the old key no longer does. The choice survives a relaunch, and the onboarding Try it hint names it too.
- [ ] Hold-or-tap, no mode switch (there is no Hold/Tap control anywhere): hold the key, speak, release after about a second: it transcribes and pastes. Tap it (under about 0.3 s): recording keeps going; speak, then press it once more: it stops, transcribes and pastes. Esc cancels in both cases (held and tapped), pasting nothing.
- [ ] Tap the key and say nothing, then tap again: the brief red error cue, nothing pasted.

### Microphone
- [ ] The Microphone row shows the current input's name (truncated if long) with a ▾. Clicking it opens a menu: "System default (<current default name>)", then every input device, with a check on the selected one. The frontmost app keeps focus while it is open.
- [ ] Pick a specific microphone and dictate: audio comes from that device (check with its level in System Settings > Sound > Input, or by covering it). Pick "System default" and change the default input in System Settings: dictation follows the new default. The choice survives a relaunch.
- [ ] Select a USB or Bluetooth microphone, then unplug it: the row shows "<name> (unavailable)" (checked but greyed out in the menu) and dictation still works using the system default. Plug it back in: the row returns to the normal name and dictation uses it again, with no relaunch.
- [ ] AirPods: connect them while the dropdown is open: they appear in the Microphone menu without reopening it. Select them and dictate. Disconnect them: the row shows "(unavailable)" and dictation falls back to the system default without an error. Reconnect: they are used again. Do the same while recording (see Edge cases).

### Dictation indicator
- [ ] The "Dictation indicator" row shows "Pill" (or "Pixel" / "Solid" in Menu bar mode) with a › and is collapsed by default. Clicking it expands inline with a short animation and the chevron rotates; clicking again collapses it. The panel resizes to fit and stays under the menu bar item.
- [ ] Expanded: a "Show while dictating" control (Pill | Menu bar), a Waveform control (Pixel | Solid), a "Red while recording" switch, a "Hide menu bar icon when idle" switch, and the note "Hidden? Open Akoya from Spotlight to get back here." The controls are neutral grey/white (no solid-blue selected states).
- [ ] With "Red while recording" on, the waveform is red while listening and the transcribing sweep is amber, on a light and on a dark menu bar; with it off, both are the menu bar colour.
- [ ] Waveform and Red while recording apply live to the pill and the menu bar item, Hide menu bar icon when idle to the item, and the collapsed row's value is "Pill" in pill mode, else follows the Waveform choice. They survive a relaunch.

### Launch at login
- [ ] The "Launch at login" switch adds Akoya to System Settings > General > Login Items when on and removes it when off.

## App icon
- [ ] Finder shows `build/products.noindex/Akoya.app` with the dark rounded icon and the glowing dot logo.
- [ ] System Settings > Privacy & Security > Microphone / Accessibility list "Akoya" with that icon, so it is easy to find.

## Core flow
The default indicator is the pill; lines below that mention the menu bar item or its red cue describe Menu bar mode (set it in the dropdown first). The pill itself is under "Pill".
- [ ] Hold Right Option, speak, release: waveform while speaking, scanning bar, then text appears in TextEdit, Safari, Slack, Terminal and VS Code.
- [ ] With Accessibility granted while Akoya is in the background, the hotkey starts working within ~2 s.
- [ ] The first word isn't clipped (engine starts on press).
- [ ] Holding the key without speaking: brief red cue, nothing pasted. Quiet speech still transcribes.
- [ ] Esc while recording cancels and doesn't close a dialog (a long-held Esc may still auto-repeat into the app).
- [ ] Copy an image first; it's still on the clipboard after dictating.
- [ ] Close the dropdown, then dictate right away: text lands in the previous app, not in Akoya.
- [ ] Open the panel, open the Hotkey or Microphone menu, then dictate (also with the menu still open): the paste lands in the frontmost app.
- [ ] Reopen Akoya from Spotlight, then dictate with the panel open: the paste lands in the previously frontmost app.
- [ ] Deny the microphone, then press: red cue, no system dialog mid-dictation.

## Pill
Default indicator ("Show while dictating": Pill). Use TextEdit as the frontmost app unless stated.
- [ ] Dock at the bottom: hold the hotkey; the pill appears centred, 20 pt above the Dock, with the live waveform and timer.
- [ ] Dock on the left, then on the right: the pill is 20 pt above the bottom of the visible frame, centred on the space beside the Dock.
- [ ] Dock set to auto-hide: the pill is at the same height as with the Dock showing, and the Dock popping up never covers it.
- [ ] Second display: focus a window on it, dictate: the pill is on that display at the same height above its bottom. Move the window to the other display and repeat.
- [ ] A full-screen app, and another Space: the pill shows over them.
- [ ] TextEdit keeps focus and its cursor while the pill is up (clicks go through it), and the paste lands.
- [ ] Resize: while the pill is up, a change of content (listening to "Pasted") resizes it with a short animation, no jump.
- [ ] "Pasted": blue check and "Pasted" for about a second after a paste, then it fades.
- [ ] "Turn on your mic in System Settings" (red): deny Akoya's microphone in System Settings > Privacy & Security, press the hotkey.
- [ ] "Microphone not detected" (red): unplug every microphone (and disconnect AirPods; on a Mac with a built-in mic, use an external-only setup such as a Mac mini), press the hotkey. With only the chosen mic missing it falls back to the system default instead, on purpose.
- [ ] "Getting the speech model ready…" (blue): quit Akoya, remove `~/Library/Application Support/Akoya/Models`, relaunch, then press the hotkey while it downloads.
- [ ] "Speech model failed" (red): corrupt the model folder (replace a file in `~/Library/Application Support/Akoya/Models` with junk), relaunch, press the hotkey. "Retry" in the dropdown recovers.
- [ ] Silence: hold the hotkey saying nothing, release: the pill just fades, no message and no red.
- [ ] Press the hotkey during "Pasted" or during a red or blue message: a new dictation starts at once.
- [ ] Esc while recording, and locking the screen (Ctrl+Cmd+Q) while recording: the pill just fades, nothing pasted.
- [ ] Switch "Show while dictating" to Menu bar and back in the dropdown: the pill stops showing and the item shows the waveform and red flash again; back to Pill, the item stays on the idle glyph while dictating. The choice survives a relaunch.
- [ ] Pill mode with "Hide menu bar icon when idle" on: the item is hidden at rest and the pill shows while dictating.
- [ ] Onboarding Try it (reset setup as in Setup step 3): dictating shows no pill; the card shows the dictation.
- [ ] Reduce Motion on: the pill gets a plain fade in and an instant hide, no pop.

## Edge cases
- [ ] Password field / Terminal with Secure Keyboard Entry: holding the key does nothing and nothing gets stuck.
- [ ] Lock the screen (Ctrl+Cmd+Q) while recording: nothing is pasted, and after unlocking the item is idle with no red flash. Lock it right after releasing a long dictation: nothing is pasted, and after unlocking the words are in the dropdown's Last dictation. The next dictation pastes as usual.
- [ ] AirPods as input, and connecting or disconnecting them mid-recording: no hang or stuck recording; the take ends or continues on the default input, and the next dictation works.
- [ ] Opening the dropdown on a secondary display and with a full-screen app active: the panel appears under the item and closes on a Space change.

## Look
- [ ] Light and dark menu bars, including a wallpaper-tinted menu bar; no clipping at the tallest column.
- [ ] Expand/collapse (always on): pressing the hotkey widens the item leftwards in about a fifth of a second. The waveform stays put and is uncovered from the left; it never slides sideways or shows only its middle columns, and the bars do not shimmer. The idle glyph fades out over the start of the expand. On release the scanner cross-fades in with no width change. When the item collapses back to idle, the glyph stays at the right while the scanner fades and is covered from the left. Check on a 120 Hz (ProMotion) display too.
- [ ] With "Hide when idle" on, the item grows from nothing to the waveform the same way, and shrinks back to nothing.
- [ ] Reduce Motion on (System Settings > Accessibility > Display): the width jumps with no tween and the content only cross-fades briefly.
- [ ] Open the dropdown, then dictate: the menu bar item shows no background or capsule behind the icon/waveform at any point (clicking, holding, recording, collapsing).
- [ ] Item sits left of Control Center and remembers a command-dragged position after relaunch.
- [ ] With "Hide when idle" on, opening Akoya again from Finder/Spotlight opens the dropdown under the (hidden) menu bar item. With the icon visible, reopening Akoya from Spotlight does the same.
- [ ] VoiceOver reads the item as "Akoya".
- [ ] Rebuild and relaunch: permissions still granted (requires the self-signed cert).
