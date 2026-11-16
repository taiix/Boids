# Gameplay takes: how the 2026-11-16 clips were recorded

Marketing only. These scripts drive the open Unity Editor through the `unity` CLI (`eval_file`). They play a real
match started from the main menu and record it with `Assets/Scripts/Marketing/GameplayClip.cs`. Nothing here is
part of the game.

## One session

```
powershell -File session.ps1 start          # Play -> HOST A GAME -> Ready -> Start, waits for the round
powershell -File session.ps1 take t25_join_school.cs
powershell -File session.ps1 event "Nightfall|e26_nightfall.cs|4|26_fish_nightfall"   # kind|take|delay|clip
powershell -File session.ps1 wait-clip
powershell -File session.ps1 stop           # always stop Play at the end
python ..\encode_clip.py 26_fish_nightfall  # MP4 + GIF + WEBP (run from kickstarter\)
```

## What each take file does

- `prelude.cs` is prepended to every take. It provides:
  - `Me()`, the local body;
  - `Put(pos, look)`, which moves the transform, the Rigidbody and the orbit camera together;
  - `Feed()`;
  - `School()` and `BigSchool()` (one BoidsManager holds all 1,000 fish, so schools are found by clustering);
  - `Calm()`, which ends the current event and holds off random ones;
  - `Extend()`, which keeps the clock from running out;
  - `Event(kind)`, which calls `WorldEvents.DevStart`.
- `t_*` are fish takes, `e*` world events, `s*` shark takes. `t_swap.cs` is the game's F8 role swap. `t29_open_exit.cs`
  is its F7 clock skip.

## Traps found on 2026-11-16

- **Button presses:** a synthetic `NavigationSubmitEvent` no longer fires UI Toolkit `clicked` handlers.
  `press.cs.tmpl` invokes the button's `Clickable.clicked` delegate instead, which is the same code a click runs.
  Wait about 8 s after Play before pressing HOST.
- **Random events:** they land in takes. Call `Calm()` first, unless you want one: the first storm take was
  accidental and kept.
- **The escape:** it ends the round as soon as the only fish leaves, and the results screen shows the player's Steam
  name. Cut the MP4 before it with `--length`: about 6.5 s for a 65 m run.
- **Red tide:** it sits at the surface. Start 6 m down and 45 m out, or the camera breaks the surface.
- **Cold layer:** it still only reads as haze.
- **Jellyfish:** the swarm is sparse (40), so one jelly carries the shot.

## New takes (2026-11-16, second batch)

- `s50_shark_eyes_school`: the shark stalks a school at 30% throttle while the camera swings to its flank.
- `s51_shark_eats` / `s57_shark_eats_side`: a lunge and a catch, filmed from behind and from the side.
- `s52_shark_eats_pov`: first person. Only the first ~2 s are usable. Run `s_tp.cs` afterwards to return to third person.
- `s53_shark_breach` / `s56_shark_breach_side`: start 14–16 m under the deepest water (`s53_probe_deep.cs` finds it;
  around (60, -260)), aim 55° up with boost, then `lungeAt` 0.8–0.9 s. The front take is made from the template with
  `sed` (`__X__ __Z__ __DEPTH__ __LUNGE__`).
- `t54_fish_hides` / `t55_fish_in_school`: the fish blends into the school (`blendAt`). Start no more than 8 m from the
  centroid, or the school has moved on before the blend. Don't record in the first ~10 s of a round: the
  "YOU ARE A FISH" banner is still up.

GameplayClip now has `lungeAt`, `pitchLimit` and a free-look sweep (`freeYawFrom`/`freeYawTo`/`freePitch`). The sweep
also runs while the flock steers a blended fish.
