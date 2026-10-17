# Manual checks (Akoya)

Nothing here could be checked automatically: it needs a screen, a microphone and macOS permissions.

## Setup
1. Optional, but recommended so permissions survive rebuilds: `scripts/make-signing-cert.sh`, then run the trust command it prints. Do this BEFORE granting any permissions, or skip it if the "Silk Self-Signed" identity already exists (it keeps permissions across rebuilds too). Switching identities after granting requires granting again.
2. `scripts/build-app.sh debug 0.2.0 && open build/Akoya.app`
3. To see the first-run flow again: quit Akoya, `defaults delete com.rohitnikamm.akoya`, and revoke Akoya's Microphone and Accessibility entries in System Settings > Privacy & Security.

## Upgrading from Silk (once)
- [ ] First: quit Silk, turn off its Launch at Login, and delete the old Silk.app. (Akoya also asks a running Silk to quit at launch: start Silk, then Akoya, and Silk's menu bar item disappears. Without this both apps would paste every dictation twice.)
- [ ] The bundle id changed (`com.rohitnikamm.silk` to `com.rohitnikamm.akoya`), so macOS treats Akoya as a new app: Microphone and Accessibility must be granted again once. Onboarding walks through this; the old "Silk" entries in the privacy lists can be removed.
- [ ] If `~/Library/Application Support/Silk/Models` existed, it is moved to `~/Library/Application Support/Akoya/Models` on first launch (`ls` both), with no ~450 MB re-download. The dropdown chip says "Preparing…" while the existing model loads (it may skip the download percentage entirely), then Ready. Old Silk preferences are untouched.

## Onboarding (first launch, or after the reset in Setup step 3)
- [ ] The 420x520 dark card opens centred, with the dot logo, "akoya", and "Get started". Drag it by its background; the x in the corner closes it.
- [ ] "Get started" goes to the first missing permission. Order is Microphone, then Accessibility; already-granted ones are skipped. Chips at the top show granted (green), current (blue) and pending (grey).
- [ ] Microphone: "Allow" shows the system prompt. After you click Allow in the prompt, the step advances on its own within about half a second. (If it was denied earlier, the button opens the Microphone pane instead.)
- [ ] Accessibility: "Open System Settings" opens the right pane with Akoya in the list (with the new app icon). The status reads "Waiting for the switch…"; flipping Akoya on advances to the next step on its own, with no button press. The hotkey and dictation work from this permission alone.
- [ ] A subtle haptic tap fires each time a permission turns green (trackpad Mac), and a different one when you press Done.
- [ ] While the model is still downloading, a "Getting the speech model ready…" line (with %) shows on steps 2 and 3.
- [ ] Try it: the hotkey hint names your current key ("Hold Right ⌥ and say anything", or "Tap" in toggle mode). Holding it makes the blue glow rise from the bottom edge following your voice, and the grid brightens; your words appear live while you speak, then settle as the final text. Nothing is pasted anywhere, even with another app frontmost.
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
- [ ] Status card at rest shows "akoya", the "Ready" chip, the dot logo and "Hold [Right ⌥] to talk" with no rim glow. Changing the hotkey in Settings updates the keycap.
- [ ] While you hold the hotkey with the panel open: the chip says "Listening" and the blue rim glow follows your voice; it says "Transcribing" while the model works, then returns to Ready with no glow.
- [ ] First launch (no model yet): chip "Preparing…" until the first progress report, then "Downloading 42%" with a thin progress bar; it becomes "Ready" when done. With the network off on a fresh model: chip "Model failed" and a Retry button; Retry restarts the download.
- [ ] With a permission revoked: chip "Setup needed" and a "Finish setup" button, which closes the panel and opens onboarding at the first missing permission. Open the panel after granting it in System Settings: the state is re-checked on open and becomes Ready.
- [ ] Mode switch: "Hold to talk" / "Tap to toggle" changes the behaviour immediately (and in Settings).
- [ ] "Last dictation" is hidden until you dictate; then it shows the inserted text. "Copy" copies it (the label flips to "Copied") and pasting elsewhere gives that text. It is cleared when you quit Akoya.
- [ ] Footer: "Settings" closes the panel and opens the "Akoya Settings" window; the round Quit button quits.

## Settings
- [ ] Window title is "Akoya Settings", grouped into Dictation (hotkey, mode), Appearance (waveform, red tint, hide icon, expand animation) and General (launch at login), and check the toggles show the blue accent when switched on.
- [ ] Right Command and Right Control work when chosen; toggle mode works; Launch at login adds Akoya to System Settings > General > Login Items.
- [ ] Appearance changes (look, red tint, hide idle icon, expand animation) apply live.

## App icon
- [ ] Finder shows `build/Akoya.app` with the dark rounded icon and the glowing dot logo.
- [ ] System Settings > Privacy & Security > Microphone / Accessibility list "Akoya" with that icon, so it is easy to find.

## Core flow
- [ ] Hold Right Option, speak, release: waveform while speaking, scanning bar, then text appears in TextEdit, Safari, Slack, Terminal and VS Code.
- [ ] With Accessibility granted while Akoya is in the background, the hotkey starts working within ~2 s.
- [ ] The first word isn't clipped (engine starts on press).
- [ ] Holding the key without speaking: brief red cue, nothing pasted. Quiet speech still transcribes.
- [ ] Esc while recording cancels and doesn't close a dialog (a long-held Esc may still auto-repeat into the app).
- [ ] Copy an image first; it's still on the clipboard after dictating.
- [ ] Close Settings or the dropdown, then dictate right away: text lands in the previous app, not in Akoya.
- [ ] Deny the microphone, then press: red cue, no system dialog mid-dictation.

## Edge cases
- [ ] Password field / Terminal with Secure Keyboard Entry: holding the key does nothing and nothing gets stuck.
- [ ] AirPods as input, and connecting or disconnecting them mid-recording.
- [ ] Opening the dropdown on a secondary display and with a full-screen app active: the panel appears under the item and closes on a Space change.

## Look
- [ ] Light and dark menu bars, including a wallpaper-tinted menu bar; no clipping at the tallest column.
- [ ] Item sits left of Control Center and remembers a command-dragged position after relaunch.
- [ ] With "Hide icon when idle" on, opening Akoya again from Finder/Spotlight opens Settings.
- [ ] VoiceOver reads the item as "Akoya".
- [ ] Rebuild and relaunch: permissions still granted (requires the self-signed cert).
