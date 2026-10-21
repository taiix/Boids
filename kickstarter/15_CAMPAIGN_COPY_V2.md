# 15 — Campaign Page v2 (replaces 04)

Built from 14 (18 successful comparables + the Full Charge playbook). The page follows the guide's order:
1. trailer + pitch;
2. what is it (50–100 words);
3. GIFs;
4. 4–6 features;
5. story/team;
6. rewards;
7. stretch goals;
8. budget;
9. risks;
10. FAQ.

**Rules used:** at most 3 sentences per paragraph, emotion before mechanics, specific numbers, a GIF wherever words
would describe motion.

> **Before use:**
> - `[[…]]` = facts only you can supply.
> - **GIF-n** = a capture from the 4-player session (07 §4), converted with EzGif or ffmpeg (16 §5).
> - **This copy was AI-drafted.** Rewrite it in your own voice. Then the "Use of AI" answer only has to cover coding
>   help, which matters next to a "No AI" competitor (14 §4).
> - Facts updated 2026-10-20: **Unacite Studio**, **Reef Run**, **9 world events** (incl. orcas).

---

## Title, subtitle, project image

- **Title:** Reef Run — 3 fish, 1 shark, and it can hear you
- **Subtitle (≤135):** A 4-player online party game: hide in the school, finish the tasks, and keep your voice down —
  the shark hears proximity chat.
- **Project image:** `assets/visuals/ks_project_image_1920x1080.png`

## 1. Hero (top of story)

**[GIF-1: whisper → ripple on the shark's screen → lunge, 4–6 s]**

> **Three of you are fish. One of you is the shark.**
> **And the shark can hear you.**

## 2. What is Reef Run? (≈90 words)

> Reef Run is an online party game for four friends.
> One player is a great white. Three are reef fish who have to survive ten minutes, finish co-op tasks and escape.
> You talk on proximity voice, and the tasks *need* talking. But every word near the shark shows up on its screen.
> So you whisper. You hide inside a school of a thousand fish. And somebody always laughs at the wrong moment.

## 3. See it (GIF wall, 5 GIFs, ≤6 s each, captioned)

| Slot | GIF (from 07 §4) | Caption under it | Captured (2026-10-21, `assets/gifs/`) |
|---|---|---|---|
| GIF-2 | Fish swims into the school and shrinks into it | **Disappear into a school of a thousand fish.** | ✅ `03_hide_in_school` (staged) |
| GIF-3 | Two fish bite the turtle's paired ropes on a spoken count | **Talk to win: each fish sees a different clue.** | ◐ `04_turtle_rescue` (staged, no voice). Replace after the session |
| GIF-4 | Shark sonar pulse → a moving fish lights up → lunge | **Play the shark: sonar, lunge, and very good ears.** | ✅ `08_hunt_pov` (gameplay, HUD) |
| GIF-5 | Trawler net sweeps; everyone dives (or orcas chase the shark) | **The reef fights back: 9 mid-round events.** | ✅ `09_orcas_hunt_the_shark` (gameplay, HUD); `05_trawler_net` as an alternative |
| GIF-6 | Wall lifts, three fish sprint, one doesn't make it | **Then — run.** | ☐ needs the 4-player session |

The hero (GIF-1) still needs real voice. Until then, `01_shark_charge` or `02_the_catch` can stand in, but don't
caption a stand-in as the hearing mechanic. Put a small "In-engine footage, work in progress" line under the wall
(see `assets/ASSET_REGISTER.md` for which clips are staged).

## 4. Key features (6 bullets)

- 🦈 **One shark, three fish:** a 10-minute round for exactly four friends, online on Steam.
- 🎙️ **Proximity voice that can kill you:** the shark sees ripples wherever someone talks.
- 🐟 **Hide in a living school:** a real flocking simulation with hundreds to thousands of fish.
- 🐢 **Co-op tasks built around talking:** free a tangled turtle, relay a code, clean the reef.
- 🌊 **9 world events:** a trawler net, nightfall, a red tide, a jellyfish swarm and more, and orcas that hunt the shark.
- 👻 **Dead isn't quiet:** spectate, chat on the ghost channel, and flash the shark's position once.

## 5. Why we're making it (team + trust)

> [[Two or three sentences in your own words: who you are, why sharks, and the game night that started it.]]
>
> Since June 2026 we've built Reef Run in our spare time, two developers and [[N]] commits, and everything on this
> page is captured from the game. [[If true by then: "N groups played our public playtest; X% said they'd buy it."]]
>
> **[Team photo]** · **Unacite Studio**, [[city]], the Netherlands · [[name, role]] · [[name, role]]

*Trust signals that comparables used and you can add for real (14 §3):*
- a public playtest with numbers;
- a demo during Next Fest;
- an event appearance (for example a Dutch indie meetup or Indigo showcase, [[if applicable]]);
- your own time and money invested, stated plainly.

## 6. Rewards (visual cards, dates on each)

| Tier | € | What you get | Delivery |
|---|---|---|---|
| Ripple | 5 | Name in the Supporters credits · Discord backer role · 4K wallpapers | Jun 2027 |
| **Early Diver** · *limit 300* | **12** | Reef Run on Steam (backer beta + full game) · Supporters credit | Beta Jul 2027 |
| Diver | 15 | Same as Early Diver | Beta Jul 2027 |
| **Founding Diver** | **29** | Diver + **Founder cosmetics** (fish "Founder Fin" + shark "Old Scar") + Founders credit + Making-of PDF | Key Jul 2027 · cosmetics at release |
| **Pod of Four** ⭐ | **49** | **4 Steam keys for your group** · beta for all four · 4 Supporters credits | Beta Jul 2027 |
| Founding Pod | 99 | Pod of Four + 4 Founder cosmetic sets + 4 Founders credits + Making-of PDF | Keys Jul 2027 · cosmetics at release |
| Name the Trawler · *limit 25* | 199 | Founding Diver + your name on the trawler's hull (moderated, rotates per round) | At release |

*Footer: Digital only · Steam (Windows) · keys via Kickstarter's Pledge Manager · [[VAT line per your accountant;
Kickstarter recommends prices excluding VAT, added in the Pledge Manager]].*

## 7. Stretch goals (reveal one at a time; move them up once funded, per the guide)

Every goal is costed from the *Labor* and *Costs* sheets (03). The money above each step also pays founder hours
that are unpaid at the base goal.

| Pledged | % of goal | Unlock | Cost basis |
|---|---|---|---|
| €14,000 | 100% | **Funded:** the game you've seen, finished, polished and on Steam | MVD |
| €17,500 | 125% | **+1 co-op task**, and **an extra Kickstarter cosmetic for every backer** | ≈80 h |
| €21,000 | 150% | **Backers vote on a 10th world event** + **Steam Deck pass** | ≈120 h + Deck |
| €28,000 | 200% | **6 languages** (DE, FR, ES, PT-BR, PL, NL) | ≈€2,400 translation + 60 h |
| €35,000 | 250% | **Original soundtrack** by a commissioned composer | ≈€2,500 |
| €55,500 | ~400% | **A second map** (kelp forest / shipwreck) + 2 more tasks + 4 months of free updates | Realistic budget |

## 8. Where the money goes (pie by function)

`assets/visuals/ks_funding_allocation_1360x780.png` (v2 shows functions, generated from the model):

| Function | € | Share |
|---|---|---|
| Programming & netcode (online bugs, performance, controller, Steam) | 2,900 | 21% |
| Game design, balance & onboarding | 800 | 6% |
| Playtests, community & campaign | 1,400 | 10% |
| Backer rewards in game (cosmetics, credits, PDF) | 400 | 3% |
| Marketing (pre-launch + live ads) | 2,250 | 16% |
| Steam fee, KvK, accountant, test hardware & software | 1,432 | 10% |
| Contingency (15%) | 1,377 | 10% |
| Kickstarter & payment fees, failed payments, VAT, tax reserve | 3,441 | 25% |
| **Total** | **14,000** | 100% |

> Half of our remaining 1,100 hours stays unpaid. That's our share of the bet, worth about €27,500 at market rates.

## 9. Roadmap

`ks_roadmap_1360x700.png`. Text:
- Done (Jun–Oct 2026): online round, voice, schools, tasks, 9 events.
- Feb 2027: Steam Next Fest demo.
- Apr–May 2027: this campaign.
- July 2027: backer beta.
- Q4 2027: release on Steam.
- 2028: second map, if unlocked.

## 10. Risks (short, honest, 3 sentences each)

- **We're two people.** If one of us is out, things slow down. That's why the goal only covers what already works plus polish.
- **Online games break in the wild.** Backers get a beta months before release, and bug fixing is the biggest budget line.
- **It needs four players.** Public lobbies and our Discord help you find a group. Bots aren't promised.
- **Dates can move.** There's a 30% buffer in the schedule. If a date slips anyway, you hear it from us first, with the reason.

## 11. FAQ (top 10)

1. **Platforms?** PC (Windows) on Steam.
2. **Exactly 4 players?** Yes, 1 shark and 3 fish. Use friends, public lobbies or our Discord.
3. **Mic required?** Strongly recommended. Push-to-talk and open mic are both supported (planned).
4. **Shark every round?** No, the shark is picked at random.
5. **Is there a demo?** [[Yes, link / at Next Fest Feb 2027]].
6. **When do I play?** Beta in July 2027, full release in Q4 2027.
7. **Price on Steam?** Planned €12.99. Our best backer price will be matched by a launch discount.
8. **How do I get my key?** Through Kickstarter's Pledge Manager after the campaign.
9. **Late pledges?** Not until the game is on sale. Steam's rules don't allow selling keys before release.
10. **Taxes?** [[VAT line per your accountant]].

## Use of AI (Kickstarter field)

Keep it accurate. If you rewrite the page yourselves, the answer covers **coding assistance only** (draft in 04 §17).
