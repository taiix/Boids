# Reef Run: trailer edit breakdown

**File:** `ReefRun_Trailer.mp4`. It runs 66.7 s at 1920×1080, 30 fps. Video is H.264 High (CRF 14). Audio is AAC 320 kb/s at 48 kHz, normalised to −14 LUFS with a −1 dBTP ceiling.
**Edit source:** `ReefRun_Trailer.blend` (Blender 5.3, Video Sequence Editor).
**Thumbnail:** `ReefRun_Trailer_Thumbnail.png` / `.jpg`. It shows the breaching shark coming at the camera, jaws open.

## Footage (re-recorded 2026-11-16, third cut)

Every shot is real gameplay, recorded in a match started from the main menu at High Fidelity, 1920×1080, with the HUD.
The clips were re-recorded with the new capture path: raw frames go to ffmpeg's hardware HEVC encoder, which keeps the CPU
free. This gives 31–51 fps real time, against 16–20 fps before. Each clip is then retimed to an even 30 fps from its
frame timestamps, so motion is smooth with no repeated frames. For the shots to run that fast, the Editor recorded with
only the Game view showing (maximized), VSync off, the Profiler off and Burst safety checks off.

## Music

**"Generic trailer music" by Emma_MA** (OpenGameArt, **CC0 public domain**: commercial use is allowed and no credit
is required). It is one continuous strip, started 46.13 s into the track:
- The track's own breakdown falls under the riser and the held breath (0:33–0:36).
- Its re-entry lands exactly on the climax cut at 0:36.0.
- It runs at 76 BPM in a 3-3-2 pulse; the climax cuts step in eighth notes (0.395 s).

See `music/README.md` for the sources. The old in-game tracks (Paranoid / Escape) are no longer used.

| Time | Section | Shot (clip @ in-point) | Text | Sound |
|---|---|---|---|---|
| 0:00–0:03.4 | **Hook** | The shark glides beside a school (`50_shark_eyes_school` @0.0) | — | Music fades up, heartbeat, whoosh |
| 0:03.4–0:04.1 | Hook | Black | — | **Boom** |
| 0:04.1–0:06.4 | Hook | Sonar ring, then a fish bleeds (`31_shark_hunt` @0.25), darkened | **THE SHARK HEARS YOU.** | Sonar, hit, bite |
| 0:06.4–0:09.4 | Intro | A fish swims up to the school (`54_fish_hides` @0.0) | **3 FISH.** | Hit |
| 0:09.4–0:11.6 | Intro | Inside the school, the camera circling (`55_fish_in_school` @0.0) | — | — |
| 0:11.6–0:14.0 | Intro | The shark under the sun (`31_shark_hunt` @3.0) | **1 SHARK.** | Hit + tension sting |
| 0:14.0–0:16.6 | Intro | The fish disappears into the school (`54_fish_hides` @2.95) | **HIDE IN THE SCHOOL.** | — |
| 0:16.6–0:19.2 | Intro | Storm-dark school (`25_fish_join_school` @1.0) | — | — |
| 0:19.2–0:22.2 | Gameplay | The tangled turtle task (`37_fish_turtle` @0.5) | **WORK TOGETHER.** | — |
| 0:22.2–0:26.4 | Gameplay | The jellyfish swarm (`41_event_jellyfish` @1.5) | — | — |
| 0:26.4–0:28.8 | Gameplay | The trawler net approaches (`45_event_trawler` @2.5) | — | Whoosh + sting |
| 0:28.8–0:31.2 | Gameplay | Nightfall plankton glow (`26_fish_nightfall` @0.5) | — | — |
| 0:31.2–0:32.4 | Gameplay | Storm (`25_fish_join_school` @3.5) | — | Thunder |
| 0:32.4–0:34.1 | Gameplay | The net closes in (`45_event_trawler` @5.0) | **SURVIVE 10 MINUTES.** | Net catch, riser |
| 0:34.1–0:35.3 | Gameplay | Red tide, "toxic water" (`40_event_red_tide` @1.8) | — | Music breaks down |
| 0:35.3–0:36.0 | — | Black | — | Held breath |
| 0:36.0–0:38.4 | **Climax** | Side-view bite and blood trail (`57_shark_eats_side` @0.2) | — | **Music re-enters**, boom, white flash, bite at 0:36.5 |
| 0:38.4–0:39.9 | Climax | Catch in a red cloud (`31_shark_hunt` @0.6) | — | Bite |
| 0:39.9–0:41.5 | Climax | Fish racing through the school (`55_fish_in_school` @2.0) | — | Whoosh |
| 0:41.5–0:43.9 | Climax | An orca rams the shark (`30_shark_orcas` @1.8) | — | Bite at the ram |
| 0:43.9–0:45.5 | Climax | Orca pod and blood (`30_shark_orcas` @4.0) | — | Bite |
| 0:45.5–0:47.1 | Climax | **Breach**: side view, out of the water (`56_shark_breach_side` @0.9) | — | Whoosh + hit |
| 0:47.1–0:48.6 | Climax | **Breach**: jaws to camera (`53_shark_breach` @1.9) | — | Bite at the jaws |
| 0:48.6–0:51.8 | Climax | Round over; the fish swims through the open wall (`29_fish_escape` @2.6) | (HUD) | — |
| 0:51.8–0:54.9 | Final | Last charge into the school, bite, blood (`51_shark_eats` @0.3) | — | Whoosh, bite, **smash cut** |
| 0:54.9–0:56.0 | End | Black | — | Boom; music cuts dead |
| 0:56.0–0:62.7 | **End** | Logo over a blurred jellyfish reef (`41_event_jellyfish` @0.2) | **WISHLIST ON STEAM** | Drone, ambience |
| 0:63.2–0:66.6 | End | Black | **UNACITE STUDIO** | Sonar ping |

## Rebuild

```bash
python Marketing/Trailer/tools/make_sfx.py
"C:/Program Files/Blender Foundation/Blender 5.3/blender.exe" -b --factory-startup --python Marketing/Trailer/tools/build_trailer.py -- --render
python Marketing/Trailer/tools/finish.py
python Marketing/Trailer/tools/make_thumbnail.py
```

The tables at the top of `tools/build_trailer.py` hold the cut list, text and mix. `_master.mkv` is the lossless intermediate.

## Known limits

- The escape shot ends before the results screen, because that screen shows the player's Steam name.
- A first-person bite take (52) was dropped: the school scattered before every lunge landed.
- The cold-layer, sandslide and storm-dive takes came out too dark or murky to use. Their old GIFs are still in
  `kickstarter/assets/gifs`.
- Nobody has listened to the mix yet. It was checked only by measurement (loudness, clipping). Give it one listen on
  headphones.
