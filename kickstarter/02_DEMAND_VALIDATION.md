# 02 — Demand Validation (before any campaign)

**Status: preparation, not a recommendation to launch.** As of 2026-10-17 REEF RUN has **no measurable audience**:
no Steam page, no list, no social accounts found (00 §7). Launching a Kickstarter now would be **high risk** (01 §7).
This plan finds out cheaply, in about 10–14 weeks, whether people *outside the team* want the game.

**Principle: measure behaviour that costs the person something** (time, an email, a wishlist, a 30-minute play
session, money). Views, likes and follower counts are logged, but they never decide anything.

| Vanity (log only) | Real signal (decides) |
|---|---|
| Views, likes, impressions | **Steam wishlists** (net of deletions) |
| Follower count | **Email signups** (double opt-in confirmed) |
| "Looks cool!" comments | **Playtest sessions started, and rounds per player** |
| Upvotes | **Groups that come back a second day** (D1 group return) |
| Discord member count | **"Would you buy at €9.99?" = yes**, asked after 3+ rounds |
| | **Kickstarter pre-launch "Notify me" follows** (once that page exists) |
| | Click-through rate from clip → Steam page (UTM-tagged) |

---

## 1. Prerequisites (week 0–2)

1. **Fix the client-side shark bug** (00 §4). A public test with a broken core loop invalidates every measurement.
2. **Get REEF RUN its own Steam App ID**: Steam Direct, $100 ≈ €87 (S1). Publish a **"Coming Soon" store page** using the
   existing capsules and screenshots (`Marketing/Steam/`). Wishlists only exist once this page does.
3. **Create a Steam Playtest app** (free; keys and open sign-ups). It is the free testing route. Never sell access to
   it (S3).
4. Claim handles: **@reefrungame** (or similar) on TikTok, YouTube, X/Bluesky, Instagram; a Discord server; an email
   tool (Buttondown, MailerLite or Kit free tiers. **Check current free-tier limits before choosing.**)
5. Put **UTM-tagged links** everywhere: `?utm_source=tiktok&utm_campaign=clipA` etc. Steam's UTM analytics reports
   visits and wishlists per tagged link.

## 2. Three audience segments (from 01 §4)

| | S1 Friend-group co-op players | S2 Shark & ocean-predator fans | S3 Asymmetric / social-deduction fans |
|---|---|---|---|
| Test question | "Would my group play this tonight?" | "Is being the shark the draw?" | "Is voice-as-detection a fresh twist?" |
| Where to reach (no spam; read each community's rules first) | TikTok/Shorts; co-op streamers; friend-game subreddits' promo threads | Shark-game YouTube; play-as-creature communities (Feed and Grow, The Isle, Depth) | Asymmetric- and social-deduction-focused subreddits and Discords; indie-MP Discords that allow showcases |
| Hook that should work | Group screaming in voice as the shark appears | Shark POV: hearing ripples, sonar, lunge, catch | "The hunter sees who's talking" explainer |
| Key metric | Clip → wishlist CTR; playtest *group* sign-ups | Wishlist rate on shark-POV clips | Kickstarter-propensity question in survey; Discord join → playtest |

## 3. Three positioning concepts to A/B (from 01 §6)

Each concept gets its own clip set, Steam page short-description variant (rotate weekly) and landing-page headline.

| Concept | Headline | Sub | Lead visual |
|---|---|---|---|
| **A — "It can hear you"** (P1, recommended) | **Three fish. One shark. It can hear you.** | Proximity-voice survival for 4 friends. Hide in the school, finish the tasks, and keep your voice down. | Fish whispering → ripple on shark HUD → lunge |
| **B — "Be the great white"** (P2/S2) | **Be the shark. Or be lunch.** | 1-vs-3 asymmetric hunt on a living reef. Sonar, lunges, and three friends who think they're hiding. | Screenshot 01 (shark charge) |
| **C — "Hide in plain sight"** (school blending) | **Blend in. Shut up. Swim.** | Vanish into a school of a thousand fish while your friend, the shark, hunts for the one that's acting suspicious. | Player fish merging into the boid school |

**Decision rule:** after 4 weeks, the concept with the highest **wishlists per 1,000 tagged visits** (minimum 300
visits per concept) becomes the campaign concept. If none reaches 300 visits, the reach problem has to be solved
before positioning can be judged.

## 4. Five short-form validation videos (15–30 s, 9:16)

All from real gameplay with real voice. Players agree to their voices being used. No staged or paid reactions.

| # | Hook (first 1.5 s, on-screen text) | Content | Concept tested | CTA |
|---|---|---|---|---|
| V1 | "We had ONE rule: don't talk." | Group whispers at the turtle task. One laughs, ripple appears, shark lunges. Hard cut on the scream. | A | "Wishlist REEF RUN" (Steam link) |
| V2 | "POV: you're the shark and they think you can't hear them" | Shark POV with the hearing ripples on screen, sonar ping, catch. | B | "Would you hunt your friends?" + wishlist |
| V3 | "Which fish is my friend? (1,000 options)" | Slow zoom into the school. Freeze, circle, reveal. The shark misses. | C | "Spot them? Comment the timestamp." |
| V4 | "The exit is open. The shark sees EVERYONE." | Escape phase. Three fish race to the lifted wall, one doesn't make it. | A/S1 | "Join the playtest" (Steam Playtest) |
| V5 | "When the trawler net comes through mid-round" | World event chaos: everyone dives under the net. | Variety | "Playtest sign-ups open" |

Post each on TikTok, YouTube Shorts and Instagram Reels on the same day. Log 72-hour metrics in the tracker.

## 5. Three community-feedback posts

Use them only where the community's rules allow self-promotion or feedback requests. Many subreddits limit this to
weekly threads or require a ratio of non-promotional participation. Read the sidebar and pinned rules every time.
Be a participant first. One post per community per month at most. Disclose that you're the developer.

**Post 1 — Design feedback (asymmetric / game-design communities)**
> **Title:** I'm making a 3-v-1 shark game where the shark can *see* the fish who talk on voice. Is that fair?
>
> Hi, I'm one of two devs on REEF RUN. Three players are reef fish and one is a great white. Fish need voice to do
> co-op tasks (one sees a word, the other has to act on it), but anyone talking near the shark shows up as a ripple
> on its screen. Clear up close, fuzzy at range. [15 s clip]
>
> Honest question for asymmetric players: does that sound like tension or like punishment? What would you want as
> a fish to fight back? (We're deliberately not giving fish abilities, which might be wrong.)

**Post 2 — Visual showcase (indie showcase threads, screenshot days)**
> **Title:** The fish you're hiding among are real boids: 1,000 of them, and the shark is a player
>
> [GIF of player blending into school] Our hide mechanic: you shrink to school size and swim *with* the flock. The
> shark has to read behaviour, not shapes. Built in Unity HDRP by a 2-person team. Feedback on readability welcome.
> Can you spot the player?

**Post 3 — Playtest call (playtest-friendly communities and our own Discord)**
> **Title:** Free playtest: need groups of 4 for a shark-vs-fish voice game (PC, Steam)
>
> We're running a free Steam Playtest of REEF RUN this weekend. It's built for groups of 4 with voice (proximity
> chat). Solo? Join the Discord and we'll match you into a group. After 3 rounds there's a 2-minute survey. That's
> what we really need. No purchase, no strings. [Steam Playtest link]

## 6. Landing page and signup incentive

- **Draft page:** `assets/landing/index.html`. One screen: headline (concept A), autoplay muted gameplay loop, three
  bullets, email field, Steam wishlist button, footer with the honest status ("in development, playtests
  2026–2027"). It has no backend. Wire the form to the chosen email tool's embed and add the privacy notice that
  tool requires.
- **Signup incentive (no money, no sold access):** **"Join the first playtest wave."** Subscribers get
  playtest invitations before public sign-ups and a 4K wallpaper pack (from `Marketing/SteamCapsules/master/`). The
  playtest itself stays free (S3).
- **Welcome email:** Subject "You're in the first wave 🦈". Body: what the game is (2 lines), when the next test is,
  the reply question "How many people are in your usual game group?" (segmentation data), and the Discord link.

## 7. The demand-validation experiment (12 weeks, ≈€0–€250)

**Hypothesis H1:** strangers who see REEF RUN gameplay wishlist it at ≥5% of tagged visits (benchmark: an
assumption, to compare against Steam's own UTM report).
**Hypothesis H2:** groups who play 3+ rounds want to come back, with ≥30% of groups returning on another day.
**Hypothesis H3:** ≥40% of players with 3+ rounds answer "yes, at €9.99" to the purchase question.

| Week | Action | Output |
|---|---|---|
| 0–2 | Bug fix, Steam page live, Playtest app, handles, landing page, tracker | Baseline = 0 |
| 3–6 | 3 clips/week (V1–V5 + variants). Concepts A/B/C rotate. Posts 1–2. | Wishlists/1k visits per concept |
| 6–7 | **Playtest weekend #1** (Discord matchmaking for solo sign-ups). Survey after round 3. | Sessions, rounds/player, H2, H3 |
| 8–10 | Fix the top 5 survey complaints. Creator outreach (10 small co-op streamers, playtest keys). | Creator coverage, wishlist spikes |
| 11 | **Playtest weekend #2**, same survey | Trend versus #1 |
| 12 | Decision review against the gates below | Go / adjust / stop |

Optional paid test (only with explicit approval; **≤€250**): Reddit or TikTok promoted post of the best organic clip,
UTM-tagged, to measure cost per wishlist. Report it, don't scale it.

**Feeds into:** Steam Next Fest **Feb 22 – Mar 1 2027** (S2). Registration is on Steamworks. A public demo there is
the biggest free visibility event before any campaign.

## 8. How many qualified people does each goal need?

All rates are **assumptions** from wide-ranging, non-game-specific sources (M7). Treat the output as an order of
magnitude, not a forecast. Formulas live in `03_funding_model.xlsx` → *Backer Funnel*.

Backers needed = Goal ÷ Average pledge. Pre-launch followers needed = Backers × share from followers ÷ follower
conversion.

| Scenario (goal from 03) | Avg pledge | Backers needed | From followers/email (60%) | Followers needed: **low** conv. 10% | **mid** 20% | **high** 30% | …or email subs at 4% |
|---|---|---|---|---|---|---|---|
| **MVD — €12,000** | €28.90 (blended tiers) | **416** | 250 | 2,500 | **1,250** | 834 | 6,250 |
| MVD — €12,000 | €22 (if small tiers dominate) | 546 | 328 | 3,280 | 1,640 | 1,094 | 8,200 |
| Realistic — €53,000 | €28.90 | 1,834 | 1,101 | 11,010 | 5,505 | 3,670 | 27,525 |
| Expanded — €111,500 | €28.90 | 3,859 | 2,316 | 23,160 | 11,580 | 7,720 | 57,900 |

Assumed: **60% of backers come from pre-launch followers and email**, and 40% from Kickstarter discovery, creators and
launch promotion (an assumption; first-time creators often see a lower discovery share). Backers needed for the MVD
goal at a flat 30-day pace: **≈14 pledges a day**; Realistic ≈61/day.

**Steam wishlists as a proxy:** no reliable public conversion from wishlists to Kickstarter backers exists. The
working assumption is 3% of wishlisters back (labelled as an assumption in the model). At 3%, **7,500 wishlists
≈ 225 backers**: alone that's not enough even for the MVD goal (416 backers would need ~13,900 wishlists). That's why the gates require both wishlists *and*
Kickstarter followers.

## 9. Go / no-go gates (review at week 12, or after Next Fest Feb 2027)

| Gate | Threshold | Why |
|---|---|---|
| G0 Eligibility | Creator resident in the Netherlands ✔ (2026-10-17); Kickstarter identity + NL bank verification passed | Legal precondition |
| G1 Steam | Store page live, key distribution confirmed (S3) | Needed before keys are promised |
| G2 Wishlists | **≥7,500** | Shows reach beyond friends; the floor for a viable Steam launch is commonly cited higher. Assumption to revisit. |
| G3 Direct list | **≥1,250 Kickstarter pre-launch followers**, or a mix worth ≥250 expected backers (followers × 0.20 + email subscribers × 0.04) | Covers the MVD goal at mid conversion (§8) |
| G4 Product | Playtest: ≥40% "yes at €9.99" (3+ rounds), ≥30% group D1 return, critical bugs = 0 | Proves the fun reaches strangers |
| G5 Team | Committed hours confirmed through the delivery date (03) | Delivery credibility |

- **All gates met:** launch the MVD-goal campaign (€12,000; 03), 30 days.
- **G2–G4 met, G0 fails** (e.g. verification problems): fix them, or fund through Steam Early Access or a publisher.
  The validation work is still fully reusable.
- **G2/G3 missed:** keep building on Steam. Don't launch a campaign that the data says will fail. Re-test after the
  next Next Fest.

## 10. Tracking

`assets/data/REEFRUN_validation_tracker.xlsx` (sheets: *Weekly KPIs*, *Clips*, *Playtest Survey*, *Gates*). One row
per week or clip. Formulas compute wishlists per 1k visits, signup rate, group return and gate status. The survey
question bank is in the *Playtest Survey* sheet.
