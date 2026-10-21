# 13 — Kickstarter project editor: what goes in each field (v2 prices, 2026-10-20)

Use this when you create the project draft on kickstarter.com. **A draft is private until you submit it for review,
so building it now is safe.** Submitting and launching wait for the gates (02 §9, 12 §6).

**You do these yourself** (I can't enter passwords, IDs or bank details):
- create the account and verify your email;
- the **Payment** tab: Stripe identity verification with your Dutch or other EU ID, your Dutch euro current account
  (IBAN), and a card in your name;
- clicking *Submit for review* and, later, *Launch*.

Field names follow Kickstarter's editor tabs (Basics, Rewards, Story, People, Payment, Promotion). Re-check every
number in `03_funding_model.xlsx` on the day you submit.

## Basics

| Field | Value | Source |
|---|---|---|
| Project country | **Netherlands** (individual) | You, 2026-10-17 |
| Category | Games → **Video Games** | — |
| Title | **REEF RUN — 3 fish, 1 shark, and it can hear you** | 04 §1 option A |
| Subtitle | An online voice-chat party game for 4 friends: hide in the school, finish the tasks, and keep your voice down. | 04 §1 |
| Location | [[your city]], Netherlands | — |
| Project image | `assets/visuals/ks_project_image_1920x1080.png` (16:9). If the editor asks for another size, re-export from `tools/make_visuals.py` | 06 §1 |
| Project video | **Leave empty until the real video exists.** Never upload the animatic | 07 |
| Discovery Mode video (9:16) | Later, the real vertical cut | 07 §3 |
| Target launch date | Tue 13 Apr 2027 (provisional) | 03 §5 |
| Funding goal | **€14,000** (v2; lean alternative €6,000, a team decision) | 03 §1 |
| Campaign duration | **Fixed: 30 days** (ends Thu 13 May 2027). No longer: Steam's key rules | S3 |
| Pledge Over Time | Off | 05 §2 |
| Late Pledges | **Off** until REEF RUN is on sale on Steam | S3 |

## Rewards (all digital; paste the text from 05 §3)

| Tier | Price | Limit | Items to itemise | Estimated delivery |
|---|---|---|---|---|
| Ripple | €5 | — | Supporters credit; Discord role; wallpaper pack | Jun 2027 |
| Early Diver | €12 | 300 | Steam key (beta + game); Supporters credit | Jul 2027 |
| Diver | €15 | — | Steam key (beta + game); Supporters credit | Jul 2027 |
| Founding Diver | €29 | — | Steam key; Founder cosmetics; Founders credit; Making-of PDF | Dec 2027 |
| Pod of Four | €49 | — | 4 × Steam key; 4 × Supporters credit | Jul 2027 |
| Founding Pod | €99 | — | 4 × Steam key; 4 × Founder cosmetics; 4 × Founders credit; Making-of PDF | Dec 2027 |
| Name the Trawler | €199 | 25 | Founding Diver items + trawler name | Dec 2027 |

Reward images: `assets/visuals/rewards_3x2/reward_*.jpg` (3:2, no overlaid text, as the editor asks). Shipping: **no shipping**. Add-ons: none.
**Blocker:** the Steam-key tiers can't go live until REEF RUN has its own Steam app (12 B2).

## Story

Order the story like this (copy from 04; images from `assets/visuals/`; add the alt text from 06 §3):
1. Hook (04 §3) + the hero GIF (G1, once captured)
2. `ks_header_the_twist` → 04 §5 + `ks_the_twist_explainer`
3. `ks_header_how_a_round_works` → 04 §4 and §6 + `ks_how_a_round_works`
4. `ks_header_whats_playable` → 04 §8 + `ks_built_vs_funded`
5. `ks_header_rewards` → the tier list
6. `ks_header_where_the_money_goes` → 04 §10–11 + `ks_funding_allocation`
7. `ks_header_roadmap` → 04 §13 + `ks_roadmap`
8. `ks_header_team` → 04 §9 + §12 (real photos, names)
9. `ks_header_faq` → 04 §15
10. Risks and challenges → Kickstarter's own *Risks* field: 04 §14
11. Use of AI → Kickstarter's AI questions: 04 §17 (it must be accurate)

Fill every `[[…]]` first. The FAQ "Taxes?" answer and the reward footer depend on your accountant's VAT advice (03 §6).

## People

Your profile (name, photo, short bio). Add teammates as collaborators with their permission. Each collaborator
needs a Kickstarter account.

## Payment (you only)

Dutch or other EU government ID · Dutch euro current account in your name · a card in your name (K3). If you
register at the KvK first, use the details your accountant recommends.

## Promotion

After the project passes review: activate the **pre-launch page** ≥4 weeks before launch (K11), and submit the
project to the Games team for *Projects We Love* consideration (K16). From then on, every channel's call to action
becomes "Notify me on launch" (08 §4).
