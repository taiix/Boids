# Asset register

Every file in `kickstarter/assets/` and the things it's made from. Registered 2026-10-17.
**No asset uses generative AI imagery, stock imagery or footage from other games.** The *tools* that composed them
(Python/Pillow/ffmpeg scripts) were written with an AI coding assistant. Disclose that under "Use of AI" (04 §17).

## Source materials (inputs, not created here)

| Source | Location | Origin | Licence / rights | Status |
|---|---|---|---|---|
| Key art (no text) 3840×2160 | `Marketing/SteamCapsules/master/master_keyart_3840x2160.png` | In-engine render by the team (MarketingShowcase scene) | Team-owned render. **Contains the shark model from the "Underwater life deluxe" pack: confirm the pack licence allows marketing renders** | ⚠ verify the pack licence |
| REEF RUN logotype | `Marketing/SteamCapsules/master/title_reefrun_5002x1600.png` | Made by the team (`reefrun_gametitle.py`, style M3) | Team-owned; Barlow font (SIL OFL 1.1 allows use in logos) | ✅ |
| 6 gameplay screenshots | `Marketing/Screenshots/01–06*.png` | In-engine photo mode | Same as key art (third-party models in frame) | ⚠ same check |
| Fonts | `Marketing/SteamCapsules/source/fonts/`, `Assets/UI Toolkit/Fonts/` | Barlow by Jeremy Tribby | SIL OFL 1.1 (`OFL.txt`) | ✅ |
| Music "Nautilus" (poinl) | `Assets/Audio/Music/Music_Round.mp3` | opengameart.org/content/nautilus | CC0 | ✅ (credit anyway) |
| Palette | `Assets/UI Toolkit/ReefRunMainMenu.uss` | Team | Team-owned | ✅ |

## Created assets

| File | Purpose | Made by | Licence | Location |
|---|---|---|---|---|
| `visuals/ks_project_image_1920x1080.png`, `_1024x576.jpg` | Kickstarter project image | `tools/make_visuals.py` | Team-owned composite | assets/visuals |
| `visuals/ks_header_*_1360x240.png` (10) | Story section headers | make_visuals.py | Team-owned | assets/visuals |
| `visuals/ks_how_a_round_works_1360x1840.png` | Main story visual | make_visuals.py | Team-owned | assets/visuals |
| `visuals/ks_the_twist_explainer_1360x820.png` | Mechanic illustration (**labelled as an illustration**) | make_visuals.py | Team-owned | assets/visuals |
| `visuals/ks_built_vs_funded_1360x788.png` | Feature status | make_visuals.py | Team-owned | assets/visuals |
| `visuals/ks_funding_allocation_1360x780.png` | Budget pie (reads `03_funding_model.xlsx`) | make_visuals.py | Team-owned | assets/visuals |
| `visuals/ks_roadmap_1360x700.png` | Roadmap | make_visuals.py | Team-owned | assets/visuals |
| `visuals/ks_reward_*_1200x675.png` (7) | Reward images | make_visuals.py | Team-owned | assets/visuals |
| `visuals/ks_team_TEMPLATE_1360x640.png` | Team section **template** (placeholders) | make_visuals.py | Team-owned | assets/visuals |
| `social/social_square_announce_1080x1080.png` | Square announcement (fill the link) | make_visuals.py | Team-owned | assets/social |
| `social/social_vertical_cover_1080x1920.png` | Vertical cover (fill the CTA) | make_visuals.py | Team-owned | assets/social |
| `social/social_card_1600x900.png` | Link card | make_visuals.py | Team-owned | assets/social |
| `social/thumbnail_TEMPLATE_1280x720.png`, `thumbnail_example_it_heard_us_1280x720.png` | YouTube thumbnails | make_visuals.py | Team-owned | assets/social |
| `landing/index.html` + `hero.jpg`, `shot_*.jpg` | Landing page draft | Written for this package; images resized from the screenshots | Team-owned | assets/landing |
| `video/reefrun_campaign_ANIMATIC_1920x1080.mp4` | **Animatic only: not for upload** | `tools/make_animatic.py` (stills + Nautilus CC0) | Team-owned + CC0 music | assets/video |
| `video/reefrun_discovery_ANIMATIC_1080x1920.mp4` | Vertical animatic: **not for upload** | make_animatic.py | same | assets/video |
| `video/reefrun_campaign_en.vtt`, `reefrun_discovery_en.vtt` | Captions (retime after the final edit) | make_animatic.py | Team-owned | assets/video |
| `video/animatic_frames/*.png` | Intermediate frames | make_animatic.py | Team-owned | assets/video (safe to delete) |
| `data/REEFRUN_validation_tracker.xlsx` | Demand-validation tracker | `tools/build_trackers.py` | Team-owned | assets/data |
| `../03_funding_model.xlsx` | Budget / goal / rewards model | `tools/build_funding_model.py` | Team-owned | kickstarter/ |
| `../11_campaign_dashboard.xlsx` | Live campaign dashboard | `tools/build_trackers.py` | Team-owned | kickstarter/ |

## Gameplay GIFs (captured 2026-10-21)

`gifs/NN_name.gif` (600 px, 12 fps, 3.5–4.5 s, all under 5 MB) and `gifs/NN_name.webp` (680 px, 24 fps; Kickstarter
can show large GIFs as static, so upload the WEBP where it does). A full-quality 1280/1920 px MP4 of each is in
`Marketing/Clips/mp4/` for social posts. The source frames are in `Marketing/Clips/frames/` (git-ignored, ~485 MB).

**Every frame is rendered by the game.** Nothing is drawn over it or animated by hand. There are two kinds of clip,
though, and the page should be honest about which is which:

- **Staged (S):** the MarketingShowcase director (Tools > Marketing) sets a shot up. Bodies come from the real prefabs,
  and the game's own systems react: flocking, the schools fleeing, the catch (`Edible.Devour`) with its blood, the
  trawler event. But the shark's and fish's paths are scripted, the camera is free, and there's no HUD. New:
  `MarketingShowcase.RecordClip` runs a shot on past its moment at a fixed 60 fps and saves every frame.
- **Gameplay (G):** IslandTest's offline sandbox, the player shark with its real HUD. `Marketing/GameplayClip.cs` records
  the Game view. An autopilot steers through the shark's own orbit camera and motor and fires the real sonar and
  lunge, as a player would. Staging: the shark was placed about 20 m from a school, and its hunger topped up before
  each take. Both clips show "NO MIC" in the HUD corner because no microphone was connected.

| File | Kind | What happens | Campaign slot (15 §3) |
|---|---|---|---|
| `01_shark_charge` | S | A school drifts over the reef, then the shark comes out of the blue and charges past the camera | Hero alternative / social |
| `02_the_catch` | S | The shark lunges through a fleeing school and catches a player fish: blood in the water | GIF-4 alternative / social |
| `03_hide_in_school` | S | Three player fish swim inside a school while the shark cruises past in the distance | **GIF-2** (hide in the school) |
| `04_turtle_rescue` | S | The tangled-turtle task: a loggerhead struggling in a ghost net, two fish at the ropes | **GIF-3**, interim (no spoken count) |
| `05_trawler_net` | S | The trawler's net wall sweeps in behind; the shark swims at the camera | GIF-5 alternative |
| `06_predator_prey` | S | A player fish flees toward the camera, the shark right behind it with its jaws open | Social |
| `07_flank_pass` | S | The shark's lunge passes under a scattering school while a player fish escapes | Social |
| `08_hunt_pov` | **G** | Shark POV with HUD: sonar ring, a lunge and a catch in a burst of blood, within 3 s | **GIF-4** (play the shark) |
| `09_orcas_hunt_the_shark` | **G** | Shark POV with HUD: the orcas event. An orca rams the player shark, blood, and the HUD banner warns that the orcas are hunting | **GIF-5** (the reef fights back) |

### Main-menu gameplay takes (2026-10-21, second pass). These are the ones in the Kickstarter draft

How they were recorded:
- Every take starts the game the way a player does: Main Menu → PLAY (a friends-only Steam lobby) → Ready → Start,
  into the game scene, hosted through Mirror.
- The recorder (`GameplayClip`) records in **real time**, like a screen recorder. Camera shake, hit stop, HUD
  flashes and the round clock run at the speed a player sees. (The first pass used a fixed capture rate, which
  played those about 1.6× too fast, so `08`/`09` above are superseded.)
- The Editor ran at about 18–20 fps. `frames.txt` holds each frame's real time; the encoder retimes to an even
  30 fps before making the 12 fps GIF.
- Graphics: the High Fidelity quality level, the game's own post-processing and the role's view grade. The fish view
  is softer, with colour fringing; that is the fish-vision grade.
- Staging: the autopilot steers the local player's body through its own camera and motor. Each take starts near
  its subject.
  - Events were started with `WorldEvents.DevStart`. The escape was opened with the clock-skip path (F7's
    `OpenExit`).
  - A solo lobby always deals a fish (`minPlayersForShark`), so the shark takes used the game's F8 role swap
    (`ServerSwitchRole`).
  - Hunger was topped up before shark takes.
- No mic was connected, so the HUD's voice chip reads "QUIET".

| File | View | What happens | Used in the draft |
|---|---|---|---|
| `31_shark_hunt` | Shark | Sonar, a lunge into a school and a catch with blood | Hero, under "And the shark can hear you" |
| `25_fish_join_school` | Fish | Swims into a school to blend in | Play a fish |
| `37_fish_turtle` | Fish | Swims up to the tangled turtle with the rope prompts showing | Play a fish |
| `30_shark_orcas` | Shark | Orcas close in, one rams the shark ("HIT BY AN ORCA!"), blood; it presses over the shark (after the orca body fix) | The reef fights back |
| `27_fish_storm` | Fish | Inside a school in the storm's dark, then a lightning flash lights the reef | The reef fights back |
| `26_fish_nightfall` | Fish | Nightfall: the swimming fish leaves a glowing plankton trail | The reef fights back |
| `29_fish_escape` | Fish | "THE EAST WALL HAS LIFTED", the bubble curtain, and the fish makes a run for the exit | A round, start to finish |
| `40_event_red_tide` | Fish | Approaches the rust-coloured cloud near the surface, swims in: red murk, "TOXIC WATER - you're losing health!" | The reef fights back |
| `45_event_trawler` | Fish | The trawler's net sweeps into the camera: "CAUGHT IN THE NET! You're hurt", blood | The reef fights back |
| `41_event_jellyfish` | Fish | Glowing jellyfish drift past, one close to the camera | The reef fights back |
| `44_event_current` | Fish | Schools stream sideways in the strong current | The reef fights back |
| `43_event_sandslide` | Fish | After the quake, sand clouds rise off the seabed | The reef fights back |

Events staging: each one is staged where it actually happens. The red tide is near the surface around
`ReefConditions.TideCentre()`; the jellyfish go to their densest cluster; the trawler is met 24 m ahead of its net;
the sand slide is filmed from just above its cloud, right after the quake. The cold layer was tried twice but reads
only as faint haze, so 8 of the 9 events have a GIF.

These takes were recorded but not used:
- red tide, jellyfish, sand slide, storm and trawler from the shark's view: the events happened away from the camera;
- a red tide and a jellyfish swarm from the fish's view: too little on screen.

The round-over screen shows your Steam persona name, so it is cut from the escape GIF.

The staged clips (`01`–`07`) are no longer in the draft, because they don't start from the main menu. Keep them for
trailers or social posts if you want, labelled as staged.

**Still missing (needs the 4-player session, 07 §4):**
- GIF-1, the hero: a whisper → a ripple on the shark's screen → a lunge, with real voice.
- GIF-6: the escape wall lifting.
- A turtle rescue on a spoken count.

The staged clips also contain the third-party models covered by the rights questions below.

## Open rights questions (must be closed before any public use)

1. **"Underwater life deluxe"** and **"Coral Reef Decor Pack"**: confirm the seller, the licence type, and that
   marketing renders are allowed. These appear in almost every image.
2. **`Models/RiggedFish.fbx`, `Models/Kelp/*`, `Models/Seaweeds/SM_Seabed_023_*`, `Models/Trash/*`, FPX_Seabed
   rock emitters**: record their origin and licence (00 §5). The player fish model is in many shots.
3. Voice and face releases for everyone in the final video (07 §4).
