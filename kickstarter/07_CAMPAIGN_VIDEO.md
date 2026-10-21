# 07 — Campaign Video

**What exists:** a full-length **animatic**, not the final video:
- `assets/video/reefrun_campaign_ANIMATIC_1920x1080.mp4` (2:05)
- `assets/video/reefrun_discovery_ANIMATIC_1080x1920.mp4` (1:02, Kickstarter "Discovery Mode" vertical)
- captions in `.vtt`

Both are built by `tools/make_animatic.py` from HUD-free photo-mode stills, on-screen text and CC0 music
("Nautilus" by poinl), and watermarked **ANIMATIC**. They lock the timing, text and voice-over length.
**The final video needs live 4-player gameplay with real voice, which only the team can record (§4).** Never upload
the animatic as the campaign video: it would misrepresent stills as gameplay (K2).

## 1. Concept

**"Keep your voice down."** Open on the mechanic itself: a whisper, a ripple, a lunge. Then explain the game in the
order a viewer's questions come up. Real gameplay, real voices, the founders on camera once. No cinematic trailer
that hides the game.

| Viewer question | Where it's answered |
|---|---|
| 1. What is the game? | 0:06–0:20 (title, "one of you is the shark") |
| 2. What makes it interesting? | 0:20–0:48 (hide in the school, the tasks need talking, the shark sees who's talking) |
| 3. What does playing feel like? | 0:00–0:06 cold open; 0:48–1:14 shark kit, events, escape |
| 4. Why back it? | 1:30–1:55 (what the pledge does, beta date) |
| 5. What will funding achieve? | 1:30–1:45 (bug fixing, performance, controller, Steam release) |
| 6. Why trust the developers? | 1:14–1:30 (founders on camera, "everything you've seen is the game running", git history) |
| 7. What next? | 1:55–2:05 (Pod of Four, back now) |

## 2. Script and storyboard (source: `CAMPAIGN` list in `tools/make_animatic.py`)

| # | Time | Picture (final cut) | On-screen text | Voice-over |
|---|---|---|---|---|
| 1 | 0:00–0:06 | **G1:** shark POV, HUD on. A fish whispers, a ripple appears, the shark turns and lunges. Real voice. | — | *(whisper)* "…it's right behind you." |
| 2 | 0:06–0:11 | Logo over key art | 3 FISH · 1 SHARK · IT CAN HEAR YOU | "Three fish. One shark. And it can hear you." |
| 3 | 0:11–0:20 | Lobby with 4 avatars → role reveal → shark spawns | ONE OF YOU IS THE SHARK · 4 friends · online · Steam | "REEF RUN is an online game for four friends. Every round, one of you becomes a great white. The other three are reef fish." |
| 4 | 0:20–0:29 | **G2:** fish POV merging into the school; shark POV scanning | HIDE IN THE SCHOOL | "The fish can disappear into a school of a thousand fish. Swim in and you move with them. The shark has to spot the one acting strange." |
| 5 | 0:29–0:39 | **G3:** two fish on the turtle ropes, "three, two, one, bite!" | THE TASKS NEED TALKING | "To win, the fish have to work together: free a tangled turtle, repeat a code, clean the reef. And that means talking." |
| 6 | 0:39–0:48 | Split screen: fish talking, and on the shark's HUD the ripple; then "SHARK HEARD YOU" | THE SHARK SEES WHO'S TALKING | "But voices carry. Anyone who talks near the shark shows up on its screen. So every round, you have to talk… and you have to shut up." |
| 7 | 0:48–0:58 | Shark POV: sonar ping, lunge, catch; fish mashing to break free | BITE · LUNGE · SONAR | "As the shark you've got a bite, a long lunge, and a sonar that catches anything that moves." |
| 8 | 0:58–1:06 | **G5:** trawler net, nightfall plankton trails, red tide (≈1.5 s each, matched cuts) | THE REEF FIGHTS BACK · 8 world events | "And the reef changes mid-round: a trawler net, nightfall, a red tide." |
| 9 | 1:06–1:14 | **G4:** the wall lifts, three fish sprint, one is caught at the gap | THEN — RUN. | "When the clock runs out, a wall lifts, the shark sees everyone, and the fish make a run for it." |
| 10 | 1:14–1:30 | Founders to camera (a phone is fine; natural light), then 3 s of the Unity editor and git history | WHO WE ARE · [[names]] | "We're [[names]], two developers from [[city]]. We built REEF RUN in sixteen weeks, and everything you've just seen is the game running." |
| 11 | 1:30–1:45 | Built-vs-funded graphic, then a performance before/after (frame counter) | WHAT YOUR PLEDGE DOES | "Now we need to make it ready for everyone: fix the online bugs, make it run on older PCs, add controller support, and release it on Steam." |
| 12 | 1:45–1:55 | Roadmap graphic | BETA JULY 2027 · RELEASE Q4 2027 · €12,000 goal | "Backers play the beta from July 2027, and the full release is planned for the end of 2027." |
| 13 | 1:55–2:05 | End card: logo, "Back REEF RUN", Pod of Four | BACK REEF RUN · Pod of Four: 4 keys for your group | "Grab a Pod of Four for your group, and we'll see you on the reef. Keep your voice down." |

Voice-over: about 270 words, about 2:05 at a relaxed pace. Record it in the founders' own voices (credibility, and €0).
**If any date, amount or tier changes, update `make_animatic.py`, re-run it, and re-record the affected lines.**

## 3. Discovery Mode vertical (≈60 s, 9:16)

Kickstarter recommends 60–90 s, real people over polish, captions (K8). Storyboard in the `DISCOVERY` list:
whisper cold open → "3 fish · 1 shark" (founder selfie-cam) → hide in the school → "but you have to talk" → be the
shark → "2 devs · playable now" (founders) → "Back REEF RUN". Keep text away from the bottom 20% and the right edge,
where the app UI sits.

## 4. Footage to capture (human action)

| ID | Shot | POV / camera | Setup | Notes |
|---|---|---|---|---|
| G1 | Whisper → ripple → lunge | Shark, HUD on | 4 players, real voice; one fish talks near the shark | **The single most important shot.** Do 10+ takes |
| G2 | Vanish into the school | Fish, then shark | Near a big school | Also capture slowed to 50% |
| G3 | Turtle knots on a spoken count | Fish | Two fish at paired ropes | Voice clearly audible, no swearing (all-ages cut) |
| G4 | Escape sprint | Spectator / shark | Clock skipped with F7 (dev builds only) | Don't show the debug key on screen |
| G5 | Trawler / nightfall / red tide | Any | `WorldEvents.DevStart(kind)` in a dev build | 3 × 1.5 s |
| G6 | Sonar, lunge, catch, break-free | Shark + fish | — | |
| G7 | Lobby + role reveal | Menu UI | 4 Steam accounts | Hide real Steam names unless the people agree |
| G8 | Founders to camera | Phone at eye level | Quiet room | 15 s max each |
| G9 | Performance before/after | Any | Same spot, Low vs current preset | Only once the presets exist |

**Capture settings:** OBS, 1920×1080 at 60 fps, ~40 Mbps CQP, separate tracks for game audio and each player's voice
(record each player locally if possible). Disable the hard-coded "v0.4.1 · EARLY ACCESS" label (00 §1). Use a
development build with the HUD on. The existing photo-mode stills have no HUD, which is right for Steam screenshots
but wrong for showing the ripple mechanic.

**Consent:** everyone whose voice or face appears signs a one-line release ("I agree that my voice/image in REEF RUN
recordings may be used in its marketing"). No staged reactions. No paid actors posing as players (K2: honest
presentation).

## 5. Music and sound

- **Music:** "Nautilus" by poinl (CC0, already the in-game round music); "Sirens in Darkness" (CC0) under the escape.
  Credit them in the video description even though CC0 doesn't require it. **Don't use the CC-BY "Acid burn" sound
  without the credit line.**
- **Sound design:** keep the game's own SFX (sonar ping, bite crunch, bubbles) at full level. Duck the music under
  every voice line by about 8 dB. For the cold open: silence → whisper → sonar-like sting on the ripple → hard cut on
  the bite.
- **Mix target:** −14 LUFS integrated (common streaming target), voices intelligible on phone speakers.

## 6. Editing instructions

1. Cut to the VO: use the animatic as the timeline template, replacing each still with the matching G-shot.
2. The first 6 seconds must work **muted**: burn the "IT CAN HEAR YOU" caption into the cold open.
3. Cuts every 2–4 s in gameplay sections; hold 5 s on the founders.
4. Text: Barlow Condensed ExtraBold Italic, white with a teal accent (the brand kit in 06), centred in the lower
   third, inside the title-safe area.
5. No fake UI, no slow-motion presented as real time without a "0.5×" tag, no footage from other games.
6. Export: H.264 MP4, 1080p, ~20 Mbps (well under the 5 GB limit, K8). Upload the `.vtt` captions in the Basics tab
   (K9). Kickstarter shows the video at 640×360 (K7), so check that text is readable at that size.
7. Make a 15 s and a 30 s cut for social media from shots 1, 4, 6 and 13.

## 7. Subtitles

`assets/video/reefrun_campaign_en.vtt` and `reefrun_discovery_en.vtt` are timed to the animatic. After the final
edit, retime them; most editors import and export VTT. Add more languages only once the game has them.

## 8. Required human actions (in order)

1. Fix the netId-0 bug, or every client-side shark shot will fail.
2. Run a 4-person capture session (2–3 hours) for G1–G7.
3. Record G8 and the voice-over.
4. Edit using the animatic and this storyboard. Free tools are enough (DaVinci Resolve).
5. Replace every `[[…]]` placeholder.
