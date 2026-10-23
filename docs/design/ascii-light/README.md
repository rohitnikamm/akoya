# ASCII light for the status card (parked)

Exploration from 2026-10-22, parked by Rohit to come back to later. Nothing here is built.

The idea: a subtle ASCII-art light at the top of the dropdown's status card, like ascii-magic.com's effects, where
characters appear only in the highlights, tinted by the light, with bloom, and the dark parts stay smooth.

## Where it landed

- `direction-2.html` ("B", #6 refined) is Rohit's pick. Direction 1 (`direction-1.html`) is out; direction 3
  (`direction-3.html`, "Words first") is the runner-up. Open the HTML pages for the live motion; the JPEGs are stills.
- `pill-in-b-style.html`: the Windows pill restyled to match B (light from the top instead of the bottom rim), with and
  without a row of characters. Not decided.

## What the review panels agreed on (about 40 design reviews over several rounds)

- Card: no "Akoya" title, no "● Ready"/"● Listening" tags, no dot grid; one line under the logo that changes with the
  state; the "or tap it once…" hint moves into the hotkey menu; same height in every state.
- Light: classic characters `· : + *` coming from above the card. It breathes at rest, brightens and reaches the logo
  while talking, turns amber with a sweep while transcribing, dims while downloading, and goes grey and still on errors.
- Characters only work on top of a real soft light (near-white core cooling to blue) plus a blurred bloom pass; faint
  characters on near-black read as a printed pattern. One grid only: characters and dots must line up.

## Open points if this is picked up again

- What amber means: always "transcribing" everywhere, or only with "Red while recording" on (today's menu bar icon and
  Windows pill follow the toggle; the card mockups turn amber every time).
- Fixes the reviewers asked for on B: neutral (not amber) setup wording; keep the hotkey reachable on errors (direction
  3's corner chip); Retry / Finish setup in the dropdown's existing Copy-link or quiet-button style, not tinted pills;
  keep the hotkey menu a native menu; semibold not bold; the download bar's existing 180×3 size.
- Motion: make the transcribing sweep go back and forth like the menu bar scanner (1.4 s scanner easing, a 2.8 s cycle on
  the wider card); voice response 0.10 s up / 0.45 s down like `GlowEnvelope`; 0.22 s state changes; stop drawing while
  the dropdown is closed; Reduce Motion as a still frame.
- The pill: reviewers advised against ASCII characters on the pill itself; share colours, wording and motion instead.
