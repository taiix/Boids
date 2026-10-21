# 14 — What Successful Video-Game Campaigns Actually Do (and where ours falls short)

Research 2026-10-20, following the Full Charge Kickstarter playbook (Sam Jarvis, *"Launching a video game on
Kickstarter"*, 34 pp.) and using the research tool it recommends, **KickTraq**.

**Primary data:**
- Kickstarter's own listing of every **successful (427) and failed (934)** video-game campaign that ended
  2025-10-20 → 2026-10-20. Files: `research/ks_videogames_successful.json` and `_failed.json`, pulled by
  `tools/fetch_ks_dataset.py`.
- **18 successful comparables** read in full (story, budget, rewards, stretch goals) through Kickstarter's data
  endpoint.
- **KickTraq** daily-funding charts for 10 campaigns: `research/kicktraq/*.png`.
- Budget and stretch-goal graphics in `research/budget_images/`.

---

## 1. The market in one table (last 12 months, video games)

| Goal band (USD) | Campaigns | **Success rate** | Median raised (successful) | Median backers (successful) | Median average pledge |
|---|---|---|---|---|---|
| < $1k | 191 | 52.9% | $864 | 25 | $34 |
| $1k–5k | 332 | 35.8% | $4,134 | 78 | $51 |
| $5k–10k | 240 | 23.8% | $11,754 | 203 | $69 |
| **$10k–20k** (Reef Run's band) | 230 | **27.0%** | **$24,611** | **468** | **$59** |
| $20k–50k | 214 | 23.4% | $40,644 | 585 | $79 |
| $50k–100k | 96 | 26.0% | $156,982 | 1,569 | $79 |
| > $100k | 58 | 22.4% | $532,412 | 5,916 | $85 |

- **Overall success rate: 31.4%** (427 of 1,361). The guide quotes 36–40% for the wider *Games* category.
- **Failures aren't near misses.** The median failed campaign reached **1.9% of its goal**, 75% reached under 10%,
  and only 4% passed 50%. A campaign either arrives with an audience or it doesn't.
- **Successful campaigns overshoot.** Median **188%** of goal, and 47% finish at ≥200%. Goals are set at what the
  existing audience can cover early, not at the full budget.
- Median duration: 30 days for both outcomes, so duration isn't the lever.
- Euro campaigns: 403, with **26% success**. The median successful euro goal was **€5,000**.
- **Netherlands:** 11 campaigns, 6 funded. Two video-game hits: *All Will Rise* (€10k goal → €61.8k, 1,302 backers,
  Project We Love) and *School Night Séance* (€15k → €18.5k, 452 backers). Neither mentions VAT on its page.

## 2. Funding curves (KickTraq)

| Campaign | Goal | Day 1 (approx.) | Day 1 as % of goal | Funded on | Final |
|---|---|---|---|---|---|
| Springfield Hotel (social deduction) | $10k | ~$13k | **~130%** | day 1 | $84k (840%) |
| Lou's Lagoon (DE) | €10k | ~€9k | ~90% | day 2 | €27.3k |
| Fat Goblins (online PvP) | $8k | ~$4.7k | ~59% | day 2 | $13.6k |
| Tomo Endless Blue | $100k | ~$45k | ~45% | day 4 | $241k |
| Ahoy | £80k | ~£35k | ~44% | day 3 | £552k (+late) |
| Carnival Hunt (asymmetric) | $50k | ~$17k | ~34% | day 7 | $122k |
| Hide Or Die (asymmetric) | $54.5k | ~$11k | ~20% | day 9 | $155k |
| Mech Tech | $8k | ~$1.5k | ~19% | day 11 | $13.1k |

**Pattern:** successful campaigns take **20–130% of the goal on day 1**. Most clear 30% within 48 hours, which is
the guide's threshold for "dramatically higher" funding odds. After the first days the curve is roughly flat, with
a final-48-hour bump. Big mid-campaign jumps (Springfield day 7, Hide Or Die week 3) line up with a creator video
or a feature.

## 3. Teardown of 18 successful comparables

**What every page had:**

| Feature | Share with it | Reef Run draft (2026-10-17) |
|---|---|---|
| Project video | **18 / 18** | ❌ none |
| Stretch goals | **17 / 18** | one milestone at €53k |
| Project We Love badge | 13 / 18 | — |
| Animated GIFs | median **~7** per page (max 61) | ❌ 0 |
| Images | median **~37** | 5 |
| Story length | median ~1,500 words | ~1,700 words, mostly paragraphs |
| Playable demo mentioned | 6 / 18 (Mech Tech opens with "Play the Demo!") | ❌ none |
| Steam / wishlist link | 15 / 18 mention Steam | no Steam page yet |
| Discord mentioned | 8 / 18 | none yet |
| "No AI" statement | 1 / 18 (**Springfield Hotel**, the closest competitor) | we must disclose AI use |

**Most-backed tier (price → backers):**

| Campaign | Most-backed tier | Runner-up | Average pledge |
|---|---|---|---|
| The Boo Croo (4-player party, $10k goal) | **$12 early bird (limit 250) → 182** | $15 → 75 | $38 |
| Fat Goblins (online PvP) | $12 → 134 | $36 4-pack (limit 100) → 77 | $34 |
| Springfield Hotel (social deduction) | **$19 early (limit 385) → 380** | $29 → 187 | $110 |
| Mech Tech | $30 → 127 | $15 → 65 | $47 |
| Lou's Lagoon (€) | €20 early (limit 250) → 250 | €25 → 246 | €37 |
| Kioku (€) | €18 → 145 | — | €50 |
| Rescue Ops (€) | €50 → 292 | €30 → 72 | €82 |
| Avaria (€) | €16 → 665 | €22 → 551 | €46 |
| Carnival Hunt (asymmetric) | $35 → 271 | $25 → 248 | $83 |
| All Will Rise (NL, €) | €25 → 534 | €35 → 304 | €47 |
| School Night Séance (NL, €) | €17 → 227 | €38 → 64 | €41 |

**The single-copy price that sells is €12–20**, and a limited early bird is almost always the top tier. Deluxe tiers
at €25–40 carry the average. **Reef Run's €8/€10 game tiers are below nearly every comparable.** Only BLACKOUT ($10, limit 80)
and Drawnsters (a $10 tier from an existing YouTube audience) priced that low. Low prices leave money on the table
and need more backers for the same goal.

**Budget breakdowns shown (by function, not by person):**

| Campaign | Breakdown |
|---|---|
| **The Boo Croo** | Development & programming **30%** · Art & animation **30%** · Audio & music **10%** · Marketing & community **10%** · Rewards & shipping **10%** · Contingency & fees **10%** |
| **Springfield Hotel** | Programming **35%** · Environment art 15% · Character art 15% · Design 10% · Polish & bug fixing 10% · UI/UX 5% · Sound 5% · Taxes & fees 5% |
| Rescue Ops / Wyldheart / Tideborne / Kioku / Tomo / Combo Devils | No percentages. A "**Why Kickstarter?**" section saying what *extra* the money buys (consoles, languages, polish, more content) and that core development is already secured or self-funded |

**Stretch goals (example ladder: The Boo Croo, $10k goal):**
- $12.5k: mastered soundtrack.
- $15k: +3 minigames, voted on by backers.
- $20k: 7+ languages.
- $25k: costume pack + 1 extra Kickstarter cosmetic for all backers.
- $50k: consoles.
- $75k: a new mode.

The first steps sit at **125%, 150%, 200% and 250%** of goal, so early momentum always has a next target. The guide
adds: reveal goals one at a time and move them up once funded. *Tunnel Story* deliberately had none and explained
why.

**Trust signals used:**
- Springfield: the founder's game-industry history, "$135K of my own savings spent so far", and an Unreal service
  partner.
- Boo Croo: shown at PAX West.
- Rescue Ops: "Early Access budget already secured".
- Combo Devils: a closed beta.
- Mech Tech: a demo plus endorsements.

## 4. ⚠ Competitive warning: Springfield Hotel

*Springfield Hotel* (US, Mar–May 2026, $84.9k, Project We Love) is a 4–12-player social-deduction horror game with a
**"Terror Rank" system: "the louder you are, the faster the Ghoul finds you."** That is close to Reef Run's hook. It
also sells "**No AI in game creation**" as a feature.
- **Differentiate on the specifics:** in Reef Run, *your actual voice* on proximity chat is what gives you away, not
  in-game noise. Add the shark you play, hiding inside a living school, and the setting (sunlit reef, not haunted
  hotel).
- **AI:** keep any AI disclosure accurate. Rewrite the campaign text in your own voice so the disclosure covers coding
  help only, not the page itself. Backers comparing the two pages will notice.

## 5. What the Full Charge playbook says (summary)

- **Preparation is the outcome.** About 47% of games fail from lack of preparation. Most funded campaigns hit
  **30%+ on day 1**. 50% in 24–48 hours triggers homepage, staff-pick and category placement.
- **Audience before launch:**
  - ≥500 Kickstarter pre-launch followers (1,000+ better);
  - ≥500 email subscribers;
  - pre-launch page live ≥4 weeks;
  - start ≥8 weeks out (3–6 months with no budget).
- **Conversion rates:**
  - cold traffic 1–3%;
  - warmed email 5–10% (the guide's closing page says 1–5% typical, >5% when nurtured);
  - Kickstarter followers 15–25%+.
- **Assets:**
  - gameplay trailer of 60–90 s that opens on the strongest moment, not a logo;
  - 8–10 screenshots;
  - **3–5 GIFs**;
  - press kit with 50/100/200-word descriptions;
  - copy and reward mockups finished, not "to be completed".
- **Page order:**
  1. trailer + one-line pitch;
  2. "What is this game?" in 50–100 words;
  3. 4–6 GIFs;
  4. 4–6 feature bullets;
  5. story/team;
  6. rewards with dates;
  7. stretch goals (hidden, then revealed);
  8. risks;
  9. top-10 FAQ.
- **Copy:**
  - written for a stranger with 90 seconds;
  - emotion before features;
  - at most 3 sentences per paragraph;
  - specific numbers;
  - "GIFs beat paragraphs".
- **Paid media:** start with $5–10/day and test 3–4 creatives. Put **20–30% of the goal** into ads if starting from
  scratch. Full Charge's benchmark is **$1.70–2.50 per Kickstarter follower** with strong gameplay creative ($4–6
  with weak creative), and a 3–5× ROAS target live (2.6× on *Tails of Fate*).
- **Channels:**
  - Reddit (genuine participation, "help me make this game" posts);
  - TikTok/Shorts (30–60 s; dev-moment and reaction formats);
  - #screenshotsaturday;
  - YouTube devlogs (5–10 min);
  - micro-creators (1k–50k);
  - Discord;
  - itch.io, IndieDB and Game Jolt listings.
- **Steam page:** the guide suggests going public *during* the Kickstarter for a visibility spike. Steam's key rules
  still require the app to *exist* (confirmed distribution) before keys are promised (S3), so create the app early
  and choose when to make the store page public.
- **Tools named:**
  - **KickTraq**: research (used here).
  - **IMPRESS**: free press kit.
  - **Canva**: graphics.
  - **WIX**: site and email.
  - **Brevo / EmailOctopus**: low-cost email.
  - **Linktree**: link hub.
  - **EzGif**: video → GIF.

## 6. Gap analysis: the 2026-10-17 draft vs the evidence

| # | Gap | Evidence | Fix (v2) |
|---|---|---|---|
| 1 | **No audience** | 75% of failures reach <10%; successful campaigns take 20–130% of goal on day 1 | Unchanged blocker. v2 sets numeric pre-launch targets from the guide: 1,000 KS followers + 500 emails + Discord |
| 2 | **No video, no GIFs** | 18/18 videos; median ~7 GIFs | Capture plan → 5 GIFs (EzGif or ffmpeg) + 60–90 s trailer that opens on the bite. Page v2 is built around GIF slots |
| 3 | **Under-priced tiers** | Single-copy sweet spot €12–20; early bird is the top tier; median average pledge in our band $59 | v2 tiers: €12 early (limit 300) / €15 / €29 Founding / €49 Pod of Four / €99 Founding Pod / €199 limited |
| 4 | **Budget shown as stipends vs fees** | Comparables show *what work* the money buys, by function | v2 pie by function: netcode & programming, design & balance, art & polish, QA & playtests, marketing, business, contingency, fees & VAT |
| 5 | **Marketing under-budgeted** | Guide: 20–30% of goal for ads from scratch; Boo Croo 10% | v2: €2,250 (pre-launch lead ads + live ads) in the model, ≈17% of the goal |
| 6 | **One big stretch goal** | 17/18 have ladders starting at 125% | v2 ladder at 125/150/200/250/350/440% of goal, each costed in hours, revealed one at a time |
| 7 | **Copy too long, too dense** | Guide: ≤3-sentence paragraphs, 50–100-word intro, 4–6 bullets | v2 copy (15_CAMPAIGN_COPY_V2.md) in the guide's order |
| 8 | **Weak trust signals** | Founders' savings/hours, events, betas, demos | v2: public playtest numbers, "N groups played", the 16-week build history, a demo before launch, team photo |
| 9 | **Hook overlap with Springfield Hotel** | §4 | v2 pitch is specific about voice + shark + school |
| 10 | **AI disclosure vs "No AI" rival** | §4 | The team rewrites page text so only coding help is disclosed |
| 11 | **No press kit, email funnel or link hub** | The guide's checklist | v2: press-kit text for IMPRESS, a 4-stage email sequence for Brevo/EmailOctopus, Linktree layout |
| 12 | **Outdated facts** | The repo changed after 2026-10-17 | Company **Unacite Studio**, product **Reef Run**, **9 world events** (orcas hunt the shark), new crabs and ammonites |

## 7. Bottom line

**What's weak is the readiness, more than the page.** No comparable funded without a video, and almost none without
GIFs, an early-bird tier near €12–20, a stretch ladder and an existing audience. v2 fixes everything that can be
fixed on paper (prices, budget presentation, stretch ladder, copy, press kit, email). **The two things that decide
funding are still yours:** the gameplay video and GIFs (one 4-player capture session), and 1,000 followers before
launch.
