# 16 — The Guide's Tools, Ready to Use

The Full Charge playbook names a toolset (p. 3, p. 15). Here is everything prepared for each tool. Anything that
needs an account (IMPRESS, Canva, WIX, Brevo/EmailOctopus, Linktree, itch.io, IndieDB, Game Jolt) is yours to sign
up for. Paste from here.

| Tool | Use | Status |
|---|---|---|
| **KickTraq** | Research other campaigns' daily funding | ✅ used: 10 charts in `research/kicktraq/`, findings in 14 §2 |
| **EzGif** | Video → GIF for the page | ✅ an equivalent ffmpeg recipe is below (§5); EzGif steps for manual use |
| **IMPRESS** | Free press kit | 📝 all fields below (§1). Needs your account |
| **Canva** | Social graphics | 📝 brand kit + sizes below (§4); the existing in-engine graphics import directly |
| **WIX** (or the draft `assets/landing/index.html`) | Landing page + email capture | 📝 copy ready (02 §6); connect to Brevo/EmailOctopus |
| **Brevo / EmailOctopus** | Email list and sequence | 📝 4-stage sequence below (§2) |
| **Linktree** | All links in one bio link | 📝 layout below (§3) |
| itch.io / IndieDB / Game Jolt | Discovery listings (guide p. 7) | 📝 listing text below (§6) |

## 1. Press kit (IMPRESS fields)

**Game:** Reef Run · **Developer:** Unacite Studio ([[city]], the Netherlands), 2 people · **Platform:** PC (Windows)
on Steam · **Players:** 4 online (1 shark, 3 fish) · **Release:** Q4 2027 (planned) · **Price:** €12.99 planned ·
**Engine:** Unity · **Status:** playable; public playtests from [[date]] · **Contact:** [[email]] ·
**Links:** Steam, Kickstarter, Discord, trailer.

**50 words**
> Reef Run is a 4-player online party game where one friend is a great white shark and three are reef fish. The fish
> hide inside a school of a thousand fish and must talk to finish tasks, but on proximity chat the shark sees whoever
> speaks. Keep your voice down.

**100 words**
> Reef Run is an online party game for four friends. One player is a great white shark; three are reef fish trying
> to survive ten minutes, finish co-op tasks and escape. The twist is proximity voice: the tasks need talking (one
> fish sees a clue, another must act on it), but anyone who speaks near the shark appears as a ripple on its screen.
> Fish vanish into a real flocking school of a thousand fish. The shark answers with sonar, lunges and very good
> ears. Nine world events, from a trawler net to orcas hunting the shark, keep every round different.

**200 words**: the 100-word text, plus:
> Built by a two-person team in the Netherlands since June 2026, Reef Run runs on Steam lobbies with friends-only or
> public games, a dead-player ghost channel, and an escape finale where the arena wall lifts and the shark finally
> sees everyone. The shark has a bite, a long lunge and a sonar that reveals anything moving; fish fight back by
> staying still, staying quiet and staying together. Every round is 10 minutes, and voice is the whole game: whispered
> plans, panicked screams, and the friend who just has to laugh at the wrong moment. Reef Run is coming to Kickstarter
> in [[month]] 2027 and to Steam in Q4 2027.

**Key features (bullets):** the six in 15 §4.

**Assets to upload:**
- logo `Marketing/SteamCapsules/master/title_reefrun_5002x1600.png` (copy in `kickstarter/assets/source/`);
- key art `Marketing/SteamCapsules/master/master_keyart_3840x2160.png`;
- 6 screenshots in `Marketing/Screenshots/`;
- GIFs after capture;
- the trailer.

**Credits:** [[names and roles]]. Audio credits from `Assets/Audio/CREDITS.md` (one CC-BY item must stay credited).

## 2. Email sequence (Brevo or EmailOctopus, double opt-in; the guide's funnel, p. 24)

| Stage | When | Subject | Body (short) |
|---|---|---|---|
| 1 Welcome | Immediately | You're in the first wave 🦈 | Who you are. The one-line pitch. The signup incentive (4K wallpaper pack + first playtest invite). One question: "How many people are in your usual game group?" |
| 2 Nurture | Weekly, weeks 2–6 | e.g. "How the shark hears you" / "1,000 fish, 1 player" / "Playtest #1 results" | One GIF, ≤120 words, one link (Discord or playtest). Real development news only |
| 3 Pre-launch hype | Weekly, 2 weeks before launch, then 3 days before | "Reef Run launches on Kickstarter on [[date]]" | The date and time, the Early Diver (€12, limit 300) and Pod of Four (€49), "follow the pre-launch page to get the launch email", and ask readers to whitelist your address |
| 4 Campaign | Day 1, day 3, milestones, midpoint, final week, final 48 h | "We're live!" · "What backers are saying" · "We hit 50%!" · "48 hours left" | Templates E2–E6 in 10. Honest numbers, one CTA |

Benchmarks from the guide: welcome-to-backer conversion of 1–5% on cold lists and 5–10%+ on nurtured lists.
Kickstarter followers convert at 15–25%. So **every email's primary CTA in phase E is "Follow on Kickstarter"**.

## 3. Linktree (one bio link for every social account)

1. ▶ Watch the trailer (YouTube)
2. 🦈 **Follow on Kickstarter, launching [[date]]**. Phase E only, pinned at the top
3. 🎮 Wishlist on Steam
4. 🧪 Join the playtest / Discord
5. ✉ Get the first-wave email
6. 📰 Press kit (IMPRESS)

## 4. Canva brand kit

- **Colours:** navy `#051729` · teal `#179499` · aqua `#2cc3c4` · orange `#ffb84d` (calls to action) · text
  `#eef6f7` · dim `#8fb0bb` · shark red `#ff6b6b` (danger only).
- **Fonts:** Barlow Condensed ExtraBold Italic (headlines), Barlow SemiBold (body). Both free, and both on Canva.
- **Sizes:**
  - TikTok/Reels/Shorts cover 1080×1920;
  - square post 1080×1080;
  - X/Bluesky card 1600×900;
  - YouTube thumbnail 1280×720;
  - Kickstarter story image 1360 wide;
  - Kickstarter reward image 3:2 with no overlaid text.
- **Start from:** the existing templates in `assets/social/` and `assets/visuals/`. They're in-engine art and on-brand.

## 5. GIFs (EzGif, or the same thing locally with ffmpeg)

The guide wants 3–5 gameplay GIFs. Comparables used a median of ~7. Capture first (07 §4, shots G1–G6).

**Done 2026-10-21:** 9 GIFs + WEBPs in `assets/gifs/` (listed with how each was made in `assets/ASSET_REGISTER.md`),
MP4s in `Marketing/Clips/mp4/`. They were encoded with the recipe below at 600 px / 12 fps / 112 colours, the
setting that keeps underwater shimmer under 5 MB. Still to capture: the voice hero (G1) and the escape (G4).

**EzGif, by hand:** ezgif.com → *Video to GIF* → upload the clip → set start/end (≤6 s), size 680 px wide, 15 fps →
*Convert* → then *Optimize* (lossy 30–60) → keep each under ~5 MB. Kickstarter may show large GIFs as static, so
also export WEBP (K7).

**ffmpeg (same result, no upload):**
```bash
ffmpeg -ss 00:00:03 -t 5 -i capture.mp4 -vf "fps=15,scale=680:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=128[p];[b][p]paletteuse=dither=bayer:bayer_scale=3" -loop 0 gif1_whisper_lunge.gif
```
```bash
ffmpeg -ss 00:00:03 -t 5 -i capture.mp4 -vf "fps=15,scale=680:-1" -c:v libwebp -lossless 0 -q:v 70 -loop 0 gif1_whisper_lunge.webp
```

## 6. Discovery listings (guide p. 7)

- **itch.io** (*Coming soon* page, free): title, the 100-word description, 5 screenshots, the trailer, "Follow"
  enabled, link to Steam and Discord.
- **IndieDB:** game profile with the 200-word text, a devlog every 2 weeks (reuse Steam event posts).
- **Game Jolt:** game page + devlog posts with GIFs. Its audience skews young, which fits friend-group games.

## 7. KickTraq during the campaign

Bookmark `kicktraq.com/projects/<your-slug>/`. Compare the daily curve with 14 §2:
- **day 1 ≥30% of goal** = on track;
- **under 20%** = trigger T2 (11 §4.2).
