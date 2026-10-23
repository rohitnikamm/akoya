# Manual checks (Akoya for Windows)

Nothing here could be checked by CI: it needs a real screen, microphones, a Windows keyboard layout and an installed copy. Run it on the Windows 11 PC after the build is green. CI already covers the state machine, hotkey decoding, microphone naming and a render of every screen.

## Setup
1. Run from source: `dotnet run --project windows/src/Akoya.App -c Release` (needs the .NET 10 SDK: `winget install Microsoft.DotNet.SDK.10`). A source run is not installed, so Updates and Launch at startup need the installed copy (see Install, update, uninstall).
2. To see the first run again: quit Akoya from the tray, delete `%AppData%\Akoya\settings.json`. The speech model is built in, so it is Ready at once. To see the fallback download (used only when the built-in model is missing), also rename `%LocalAppData%\Akoya\current\models` and delete `%LocalAppData%\Akoya\models\`.
3. Logs are in `%LocalAppData%\Akoya\logs\akoya.log` (they never contain what you said).

## Setup window (first launch, or when the model is missing)
- [ ] The setup window opens centred on the primary monitor with the blue rim glow, the dot logo, "Akoya", "Talk instead of type, in any app. Everything stays on your PC." and Get started. The built-in model is already loading (fallback only, with the built-in model removed: the download has started in the background).
- [ ] Microphone: the title is "Which mic do you talk into?" with "Say something. Pick the one that moves." The list shows every real input by plain name ("This PC's microphone", your headset's model name, "Webcam mic"), each with a live 10-dot meter that moves when you speak. Compare the names with Settings > System > Sound > Input.
- [ ] Loopback and virtual inputs (Stereo Mix, Voicemeeter, NVIDIA Broadcast, OBS, Discord...) are folded into a final "More (n)" row; clicking it expands in place and those rows still have live meters.
- [ ] The Windows default input is highlighted at first. Akoya never moves the highlight by itself; clicking a row does.
- [ ] Speak into the highlighted mic: a green "Hearing you" chip. Highlight a mic you are not speaking into while speaking into another: after about 3 seconds an amber "<name> isn't hearing you" chip. Before you speak, no chip.
- [ ] With only one input enabled (disable the others in Sound settings): the same screen with one row and the title "Is this the mic you want?".
- [ ] Mic privacy switch off (Settings > Privacy & security > Microphone > "Let desktop apps access your microphone"): "Turn on your mic" and "Windows is blocking it. Open Settings and switch on microphone access for desktop apps." with Open Settings, which opens the right page. The text "Waiting for the switch..." shows and the screen moves on by itself within a couple of seconds of switching it on.
- [ ] Continue saves the highlighted mic. After a relaunch the tray panel's Microphone row shows it.
- [ ] Try it: "Hold [Right Alt] and say anything" with "or tap it once to start, again to stop". Holding the key shows a live waveform and your words fill the text card while you speak. Nothing is pasted into any other window, even if one has focus. With the built-in model removed (fallback) and the model still downloading, the dots under the hint fill left to right with the download and a line under them says "Getting the speech model ready 62%"; the text card keeps "Your words will appear here." A failed download says "Model download failed · Retry" in that line.
- [ ] Done (or Skip, or the x on any step) closes setup, and setup is not shown on the next launch. Everything is still reachable from the tray panel.

## Tray icon and panel
- [ ] The dot logo is in the tray overflow (behind the ^ arrow) with the tooltip "Akoya". Dragging it onto the taskbar works and it stays there after a relaunch.
- [ ] Left-click and right-click both open the panel above the tray corner, on the taskbar's monitor. It closes on Esc and when you click elsewhere. The app you were typing in keeps its text cursor.
- [ ] Status card: "Akoya", a status dot with Ready, the dot logo, and "Hold [Right Alt v] to talk" with "or tap it once to start, again to stop". Holding the hotkey with the panel open shows Listening and then Transcribing, and it returns to Ready.
- [ ] Clicking the keycap opens the hotkey menu: Right Alt, Right Ctrl, Copilot key, with a check on the current one. Choosing one updates the keycap straight away, the new key dictates and the old one no longer does. The choice survives a relaunch and the setup Try it hint names it.
- [ ] Last dictation is hidden until the first dictation; then it shows the pasted text. Copy puts it on the clipboard and the label flips to "Copied".
- [ ] Microphone row: shows the current plain name. The menu lists "System default (<name>)", then every input by plain name, with a check on the selected one. Picking another mic dictates from it. "System default" follows Windows when you change the default input.
- [ ] Unplug the selected USB or Bluetooth mic: the row turns yellow with "Not detected". Plug it back in: the row returns to normal with no relaunch.
- [ ] Launch at startup is off by default. See Install, update, uninstall for the on and off checks.
- [ ] Quit Akoya is a quiet text row and quits the app (the tray icon disappears; no hotkey afterwards).
- [ ] Starting Akoya a second time (Start menu, or run it again) opens the panel of the running copy and the second process exits. There is never a second tray icon.

## Dictation, per app
Do these in each of: Windows Terminal, VS Code (editor and its terminal), Claude Code running in a terminal, Notepad, and a browser text box (Chrome or Edge).
- [ ] Hold Right Alt, speak for a second or two, release: the pill pops in above the taskbar (a small circle that springs out to full width) with a live waveform and timer, a blue sweep across the centre two dot rows while transcribing, then "Pasted" with a blue check for about a second, and the text (with a trailing space) appears at the cursor.
- [ ] Tap Right Alt (under 0.3 s): recording keeps going hands-free. Speak, tap again: it stops, transcribes and pastes.
- [ ] Esc while recording cancels: the pill fades, nothing is pasted, and Esc does not reach the app (it does not close a dialog or cancel a running command). Esc while NOT recording behaves normally in the app.
- [ ] A lone Right Alt tap never opens a menu bar or does anything in the app (Akoya swallows it). Left Alt is untouched (Alt+Tab, Alt+F work).
- [ ] The first word is not clipped.
- [ ] Hold the key without speaking: the pill just fades, nothing is pasted. Quiet speech still transcribes.
- [ ] Holding the key for more than a few minutes: recording stops by itself at 5 minutes and pastes what it heard.
- [ ] While the model is not ready (right after launch while it loads, or with the built-in model removed while it downloads), pressing the hotkey shows "Getting the speech model ready..." in the pill and records nothing.
- [ ] Speak quietly (as quiet as still moves the pill's dots): the words are transcribed and pasted. Hold the key without saying anything: nothing is pasted. Each take adds a line like "Take: peak -41 dBFS (level 2); transcribing" to `%LocalAppData%\Akoya\logs\akoya.log`, never the words.

## Hotkey variants
- [ ] AltGr layout (for example US International, German or Polish Programmers): with Right Alt as the hotkey, no stray Ctrl+Alt shortcuts fire in the app while holding it and typing characters that need AltGr still works when Akoya is not recording. Switch layouts with Win+Space.
- [ ] Right Ctrl as the hotkey: holding and releasing dictates; Ctrl+C, Ctrl+V and Right Ctrl combos in apps still work.
- [ ] Copilot key (only on keyboards that have one): dictates when chosen, and the Start menu does not open on release. Skip if there is no Copilot key.
- [ ] Holding the hotkey does not auto-repeat into the app or start extra recordings.

## Pill and displays
- [ ] The pill is 34 px tall, centred horizontally, with a clear gap (52 px at 100% scale) above the top of the taskbar. It never takes focus, you can click through it, and it has no taskbar button and is not in Alt+Tab.
- [ ] Turn on "Automatically hide the taskbar" (Settings, Personalization, Taskbar, Taskbar behaviors) and dictate: the pill sits at the same height as with auto-hide off (about 100 px above the screen's bottom edge at 100%), and moving the mouse to the bottom edge pops the taskbar up below the pill without covering it. Turn auto-hide off again and check the height is unchanged.
- [ ] With two monitors, dictate with a window on each: the pill shows on the monitor that holds the window you are typing into. Check it at least once on a monitor with a different display scale (for example 100% and 150%): same size relative to the screen and the same gap above the taskbar.
- [ ] Taskbar on top or on the side (with a tool such as ExplorerPatcher on Windows 11): the pill is 52 px above the bottom of the screen and stays inside the work area. With two monitors and the taskbar shown on all displays, the pill keeps the same gap above the second monitor's taskbar.
- [ ] Exclusive-fullscreen game: the pill is hidden (accepted) but dictation still pastes. A borderless-window game shows the pill.
- [ ] Tray panel, Pill row: Solid turns the dots into bars and the sweep into short bars on the centre rows; Red while recording turns the listening waveform red and the sweep amber. Both change a pill already on screen. With Windows' animation effects off, the pill fades in instead of popping.

## Pasting and the clipboard
- [ ] Dictate into one app, switch to another window and press Ctrl+V: the same text pastes again (it stays on the clipboard as the latest copy).
- [ ] Copy something, dictate, then press Win+V: the dictated text is at the top of the clipboard history and what you copied before is right under it.
- [ ] Open the tray panel while typing in an app, then press Esc (or click the tray icon again): the cursor is back in that app and you can keep typing. Open it again and click another window: focus stays in the window you clicked.
- [ ] Hold Shift or Ctrl while releasing the hotkey: the paste still works, and no modifier key is left stuck afterwards.
- [ ] Another app holding the clipboard open (some remote desktop or clipboard managers): the pill goes red with "Couldn't paste" and the text is kept as Last dictation with Copy.
- [ ] Close the panel, then dictate right away: the text lands in the previous app, not in Akoya.

## Built-in speech model
- [ ] Fresh install of Setup.exe (no `%LocalAppData%\Akoya\models\`) with the network off: Akoya becomes Ready with no download and no "Getting the speech model ready" progress, and a dictation works. `models\sherpa-onnx-nemo-parakeet-tdt-0.6b-v2-int8` (four files) and `ThirdPartyNotices.txt` are next to `Akoya.exe` in `%LocalAppData%\Akoya\current`. Setup.exe is about 524.5 MB (550,013,004 bytes).
- [ ] Update check: start from a version that downloaded its model (`%LocalAppData%\Akoya\models\` exists, with or without a `.part` file), update to this version. Once Ready, `%LocalAppData%\Akoya\models\` is gone and the model inside `current\models` is untouched.
- [ ] Damaged install: rename `current\models` and relaunch: Akoya downloads the model as before. With a corrupted built-in file of the right size: "Speech model failed"; the first Retry loads the built-in again and fails again, the second Retry uses the downloaded copy or downloads one, and the built-in is tried again after a relaunch.
- [ ] After one update, note the size of Velopack's local packages folder (`%LocalAppData%\Akoya\packages`) and of the whole `%LocalAppData%\Akoya` folder, and whether the update downloaded the full package or a delta (the model should not ship again unless it changed). Record the numbers here.

## Windows limits (expected behaviour)
- [ ] Admin window (UIPI): start Notepad as administrator, give it focus and hold the hotkey. Windows blocks a normal app from seeing keys while an elevated window has focus, so the hotkey does not fire at all there. This is expected. It works again as soon as a normal window has focus.
- [ ] Paste into an elevated window when the dictation was started from a normal window and focus moved to the elevated one before release: no paste is attempted, the pill is blue with "Copied. Press Ctrl+V", and Ctrl+V pastes the text.
- [ ] Lock the PC (Win+L) and unlock: the hotkey works again without a restart. Same after sleep and resume.
- [ ] Long uptime: the hotkey still works after several hours (Windows can silently remove a slow keyboard hook; Akoya reinstalls it).

## Errors
- [ ] Unplug the selected mic (or disable it in Sound settings) and press the hotkey: red pill with the alert icon and "Microphone not detected", gone after about 1.6 s, nothing recorded, and the tray Microphone row is yellow with "Not detected".
- [ ] Mic privacy switch off, then press the hotkey: red pill "Turn on your mic in Settings", tray status "Microphone blocked" with Open Settings.
- [ ] Unplug the headset while recording: recording stops, what was captured is transcribed and pasted, and the Microphone row turns yellow until it is back. The next dictation works.
- [ ] Fallback, with the built-in model removed (rename `current\models`, delete `%LocalAppData%\Akoya\models\`, turn the network off, relaunch): tray status "Model download failed" with Retry; Retry restarts the download. With the network back, it finishes and shows Ready.
- [ ] Fallback (built-in model removed): model download progress shows "Getting the speech model ready..." with a percentage in the tray status, and an interrupted download (kill Akoya mid-way) restarts cleanly on the next launch.
- [ ] Keyboard hook cannot install (rare): tray status "Hotkey unavailable" with Retry.

## Install, update, uninstall
Use the draft release's `Akoya-win-Setup.exe` (or the published one).
- [ ] Download Setup.exe from the release in a browser. SmartScreen warns because the file is not signed: More info, then Run anyway. It installs for the current user with no admin prompt, adds Akoya to the Start menu, and starts it. Akoya appears in Settings > Apps > Installed apps. Setup.exe, the Start menu entry, the taskbar button, Task Manager and Settings > Apps all show the Akoya icon (the dot logo on a navy rounded square), not a blank one.
- [ ] The files are under `%LocalAppData%\Akoya\` (the `current` folder and the `Akoya.exe` launcher). The speech model is built in, in `current\models`; nothing is written to `%LocalAppData%\Akoya\models\` (that folder is only used by the fallback download).
- [ ] Launch at startup on: adds `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Akoya` pointing at `%LocalAppData%\Akoya\Akoya.exe` (check with `reg query HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v Akoya`). Sign out and in: Akoya starts by itself. It also shows in Task Manager > Startup apps. Off removes the entry.
- [ ] Update: publish a newer release (for example bump the version and push the next `windows-v` tag), then in the older installed copy: within a few seconds of launch (or after the 6-hour check) the tray panel shows "Update ready" with a light-blue "Restart" link. Restart quits, updates and reopens Akoya on the new version without a new SmartScreen prompt and without downloading the model again. Your settings are kept. Launch at startup still works afterwards (the Run entry points at the launcher, which survives updates).
- [ ] After the first Windows release is published, and again after the next Mac release: the update check still finds a Windows release when a newer Mac-only release exists. Velopack reads the latest 10 published releases and skips any without `releases.win.json`, so the Mac release (tag `vX.Y.Z`, no Windows files) is ignored. To confirm, keep an older Windows build installed, publish a Mac release after the newer Windows one, and check the old build still shows "Update ready". (An automated test covers the same feed logic; this checks it against real GitHub.)
- [ ] When publishing the Windows draft, untick "Set as the latest release", so the README's release badge and `releases/latest` keep pointing at the Mac app. The updater does not care which release is marked latest.
- [ ] A draft release is not seen by the updater. After you publish the draft, an installed older copy finds it.
- [ ] Uninstall from Settings > Apps: Akoya quits, the Start menu entry and the startup entry go away, and the model folder is removed. `%AppData%\Akoya\settings.json` may remain.
- [ ] Installing a newer Setup.exe over an existing install updates it in place.
