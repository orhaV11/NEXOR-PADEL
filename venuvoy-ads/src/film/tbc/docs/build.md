# Build notes for "To Be Confirmed" (read before touching anything)

Root: `/home/user/NEXOR-PADEL/venuvoy-ads` (all paths below are relative to it). The series plan is
`src/film/tbc/docs/final.md` (the cut sheet: every beat, every word on screen, the house style and all decisions). English only.

## Hard rules
- Spend nothing: no generation tools, no MuAPI, no Higgsfield. No network. No `git commit`, no `git push`.
- Edit ONLY the files your task lists. Other agents are editing the other files at the same moment.
- Scratch files (test scripts, stills, drafts) go in the scratch folder your task names, never in the repository.
- You cannot hear. For sound, check what can be measured (length, peak, where the energy is).
- Look at what you make: render stills and open them with the Read tool. A graphic nobody looked at is not done.
- No real club, league, competition, player, app or brand anywhere. The messenger is a generic one in the brand's
  colours, not a copy of a real app. The only brand is the wordmark VENUVOY.

## The frame
- 1080 x 1920, 24 fps. A 4:5 version is cut from rows 250-1600, so everything that matters sits in rows 262-1588
  and x 40-1040. Rows 0-250 and 1600-1920 may hold only background.
- Speakers' captions are drawn by the edit engine at y = 1260 (large white capitals with a black outline).
  The narrator's caption is a dark box with cream italic serif text whose top is at y = 1300 and which can be two
  lines (to about y = 1480). So rows 1250-1480 are the CAPTION LANE: full-frame graphics and chat screens keep
  it free of text (background only).
- Top-left corner (x 60, y 258): the small wordmark VENUVOY, drawn by the edit engine over footage only.
- Top-right block, x 620-1040: rows 262-338 belong to the day-count tag ("96 DAYS BEFORE*" with a small
  "*to be confirmed" under it), rows 344-404 to the chip "OWE MAYA: 6". Nothing else goes there.
- Phone apps cover the right edge (x > 960) in rows 1050-1600 with their buttons: no small text there.

## Look
- Colours: deep green #16392b and #0f2e21, cream #f4eee1, paper #e9e2d4, gold #e6c47a (text on green) and
  #b0832a (on paper), ink #14201a, grey text #55615a, stamp red #b3261e (on paper) / #ea3d2c (on footage),
  stamp green #1f7a4d.
- Fonts (in `fonts/`, already loaded by the HTML files): Montserrat as "Mont" (labels, capitals, numbers),
  Playfair Display (the wordmark and the narrator), Heebo. For typed paperwork use
  `"JetBrains Mono", "DejaVu Sans Mono", monospace` (installed on this machine).
- Tone of every graphic: a grave documentary's evidence, plain and exact. No emoji, no cartoon icons, no
  confetti, no gradients that shout. Movement is short (0.1-0.3 s) and then things hold still so they can be read.
  Text must be readable on a phone: nothing a viewer has to read is smaller than 30 px; main lines 40-60 px.
- THE STAMP is the series' signature and is already written: `src/film/stamp.js` (read its header). Use it for
  every stamp so it is the same object everywhere. Do not edit that file; if it lacks something, say so in your
  report. A stamp lands in three frames and whatever is under it jolts once (`Stamp.shake`).

## How pictures are made
- `src/film/chat.html` and (new) `src/film/board.html` are pages of 1080 x 1920 with a global `seek(t)` that
  draws the frame for second t, deterministically (no CSS animations, no timers, no Math.random without a seed)
  and a promise `window.__ready`. `node src/film/render-chat.mjs <script.json> en <out.mp4>` films a page at
  24 fps; `--still <t> <out.png>` writes one frame. The script JSON arrives in the query string.
- `src/film/overlay.html` is the transparent layer the edit engine (`src/film/cut.py`) composites over the cut;
  `src/film/danny.json` is a complete, shipped edit list and the best documentation of the engine.
  `python3 src/film/cut.py <edit.json> en --draft` renders a half-size draft into `work/<slug>/`.
- `python3 src/film/review.py <clip.mp4> --out <dir> --fps 3` makes contact sheets of a clip.
- Sound effects are synthesised in `src/audio/kit.py` (each function returns a stereo float array at 48 kHz)
  and placed by name from an edit list: `{ "synth": "stamp", "at": "mark+0.2", "gain_db": -12 }`.
