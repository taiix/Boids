# 06 — Visual Asset Plan

**Art direction: use the game's own identity, unchanged.**
- **Imagery:** in-engine captures only (the existing photo-mode pipeline, `Tools > Marketing`, documented in
  `Marketing/README.md`).
- **Logotype:** the chosen REEF RUN title (style M3, `Marketing/SteamCapsules/`).
- **Fonts:** the game UI's Barlow / Barlow Condensed ExtraBold Italic (SIL OFL).
- **Palette:** the game UI's colours (`ReefRunMainMenu.uss`): navy `#051729`, teal `#179499`, aqua `#2cc3c4`,
  orange `#ffb84d`, text `#eef6f7`, dim `#8fb0bb`, and shark red `#ff6b6b` for danger only. The deep-teal scrim
  `#062c40` is the one chosen for the Steam capsules, because navy turns lavender over cyan water.
- **No generative-AI imagery, no stock, no concept art presented as gameplay.**
- Every visual is rebuilt by `tools/make_visuals.py`. Funding numbers are read live from `03_funding_model.xlsx`,
  so the graphics can't drift from the budget.

## 1. Specs checked against official documentation (2026-10-17)

| Asset | Requirement | Source | What we produce |
|---|---|---|---|
| Story images | ≤50 MB; JPEG/PNG/GIF recommended; ≥700 px wide; compressed on upload | K7 | 1360 px wide (2× the ~680 px story column; the column width is an observation, not documented) |
| Animated images | Some GIFs display static → use **WEBP** under 50 MB | K7 | GIF concepts below, exported as WEBP |
| Project video | ≤5 GB; MOV/MPEG/AVI/MP4/3GP/FLV/WMV; displayed 640×360 16:9 | K7, K8 | 1920×1080 MP4 (07) |
| Discovery Mode video | 9:16 vertical, 60–90 s recommended, captions advised, ≤5,120 MB | K8 | 1080×1920 (07) |
| Captions | .VTT upload | K9 | `assets/video/reefrun_campaign_en.vtt` |
| **Project (cover) image** | **Verified in the project editor 2026-10-17: at least 1024×576, cropped to 16:9, JPG/PNG/GIF/WEBP ≤50 MB.** (Not documented in the help center.) The post-campaign Spotlight image is displayed at 16:9 and resized to ≤700×800 | help article 16236633 | **16:9 at 1920×1080 + a 1024×576 JPEG.** Check the editor's own hint at upload and re-export if it asks for something else |
| Reward images | **Verified in the editor 2026-10-17: 3:2 ratio (348×232 shown), ≤50 MB; Kickstarter asks to avoid banners, badges and overlaid text** | editor | Uploaded text-free 3:2 crops `assets/visuals/rewards_3x2/` (the 16:9 text cards are for social/story use only) |
| Alt text | Supported on story images | K7 | Alt text for every image in §3 |

## 2. Visual patterns from comparable campaigns (what we take, what we avoid)

What the research covered: reward structures and project images (01 §3.3). Story-page layout of individual
campaigns wasn't reviewed in depth, because the pages render client-side. These patterns are common practice, not
measured results.
- **Take:** a GIF of the core mechanic right under the video; section headers so mobile readers can scan; one
  infographic for "how a round works"; an honest built-vs-planned graphic; a funding pie. Reward cards share one
  template and differ in the shot behind them.
- **Avoid:** cinematic renders that never appear in play; mock HUDs presented as real; reward images of things that
  don't exist yet without an "example/concept" label (Carnival Hunt labelled theirs, and that's the right call);
  walls of text on mobile.

## 3. Produced assets (`assets/visuals`, `assets/social`)

| File | Use | Alt text |
|---|---|---|
| `ks_project_image_1920x1080.png` / `_1024x576.jpg` | Kickstarter project image; also the pre-launch page | "REEF RUN logo over a great white shark swimming toward the sunlit surface, with a school of fish and kelp" |
| `ks_header_*_1360x240.png` (10) | Section headers: the twist, how a round works, what's playable, rewards, money, roadmap, team, risks, FAQ, use of AI | the header text |
| `ks_how_a_round_works_1360x1840.png` | Main campaign visual: the 5-step loop with in-game shots | "Five steps of a round: pick your pod, hide in the school, talk carefully, survive the reef, run for it" |
| `ks_the_twist_explainer_1360x820.png` | Explains the voice ripple. **Labelled as an illustration**, because the ripple is drawn, not the in-game HUD | "Diagram: a fish talking creates a ripple the shark can see; a clearly heard fish can be locked on for 2 seconds" |
| `ks_built_vs_funded_1360x788.png` | Feature comparison: what exists vs what the pledge funds | list the two columns |
| `ks_funding_allocation_1360x780.png` | Funding donut (reads the model) | "Where €12,000 goes: €5,500 stipends, €2,182 costs, €1,152 contingency, €3,166 fees, VAT and reserves" |
| `ks_roadmap_1360x700.png` | Roadmap | dates in text |
| `ks_reward_*_1200x675.png` (7) | Reward images | tier name, price, contents |
| `ks_team_TEMPLATE_1360x640.png` | Team section. **Template with [[placeholders]]: needs real photos and names** | — |
| `social_square_announce_1080x1080.png` | Instagram / Bluesky / Discord announcement. Fill the [[link]] | — |
| `social_vertical_cover_1080x1920.png` | Shorts/TikTok/Reels cover, story. Fill the [[CTA]] | — |
| `social_card_1600x900.png` | X / Bluesky / Reddit link card | — |
| `thumbnail_TEMPLATE_1280x720.png` + `thumbnail_example_it_heard_us_1280x720.png` | YouTube thumbnail template + a worked example | — |
| `assets/landing/index.html` (+ 4 JPEGs) | Landing page draft (02 §6) | in the HTML |

**Mobile check:** body text on story graphics is ≥28 px at 1360 px wide, which renders at ≥14 px in a 680 px column
and ≥7.5 px on a 360 px phone. The headers and big numbers carry the message. The two text-heavy graphics (round
infographic, twist explainer) also have their text in the story copy (04), so nothing is image-only.

## 4. Needs capture in the game (human action, Unity Editor)

These need real gameplay, so they can't be faked from stills:

| # | Asset | How to capture | Length / format |
|---|---|---|---|
| G1 | **Hero GIF: "Talk → ripple → lunge"** | 4-player session; record the shark's view with HUD on while a fish talks nearby, then the lunge | 4–6 s loop, 680 px wide WEBP |
| G2 | "Vanish into the school" | Fish POV swimming into a school and shrinking; shark POV losing track | 4 s loop |
| G3 | Turtle task: two fish biting paired knots on a spoken count | Fish POV, voice audible in the video version | 5 s |
| G4 | Escape: the wall lifts, everyone revealed, sprint | Spectator/shark camera | 5 s |
| G5 | World event montage (trawler, nightfall, red tide) | 3 × 1.5 s cuts | 5 s |
| S1–S6 | **New Steam/KS screenshots with HUD** showing the hearing ripple, sonar and "SHARK HEARD YOU" | The existing six are HUD-free photo mode; add 3 with real UI so the mechanic is visible | 1920×1080 |

Capture settings: 1080p60, OBS with separate voice and game audio tracks; consent from everyone whose voice is
recorded (02 §4). Turn off the hard-coded "v0.4.1 · EARLY ACCESS" menu label before recording (00 §1), because
there's no Early Access release.

## 5. Asset register

See `assets/ASSET_REGISTER.md`: every file, purpose, source, licence and location.
