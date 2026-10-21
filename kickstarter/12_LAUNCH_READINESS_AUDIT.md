# 12 — Launch Readiness Audit

Audited 2026-10-17 and re-scored the same day after the creator confirmed residence in the Netherlands, as an independent producer deciding whether to put their own money behind this campaign. A
complete set of documents earns no points by itself. Each score is backed by the evidence cited.

## Verdict: **BLOCKED** (weighted score 52 / 100 after v2; 51 after the Netherlands update; 48 originally)

**v2 re-score (2026-10-20):**
- Rewards repriced to market (criterion 7: 8 → 9).
- A concrete, tool-backed pre-launch funnel (15: 6 → 7). See 14, 15, 16.
- **Nothing that decides funding has changed:** still no audience, no video or GIFs, no Steam app, and the bug is
  still open.
- The research makes the stakes concrete. **75% of failed video-game campaigns reached <10% of their goal**, and every
  funded comparable had a video (14 §1, §3).

Status definitions:
- **Ready:** ≥75 points, no blocker, and demand evidence ≥6/10.
- **Needs work:** 50–74 points, or specific fixes outstanding, with no blocker.
- **Blocked:** any material issue that prevents launch, whatever the score.

## 1. Scorecard

| # | Criterion | Weight | Score /10 | Points | Evidence |
|---|---|---|---|---|---|
| 1 | Is the game appealing enough? | 9 | 7 | 6.3 | A clear, novel hook (voice = detection, 00 §2). High visual quality (screenshots). Unproven with strangers: no playtest outside the team. |
| 2 | Target audience defined | 5 | 8 | 4.0 | 3 segments with channels (01 §4). Kickstarter propensity per segment assessed. |
| 3 | Pitch communicates quickly | 5 | 8 | 4.0 | "Three fish. One shark. It can hear you." One line plus one GIF. Not tested on real viewers yet. |
| 4 | **Evidence of actual demand** | 15 | **1** | 1.5 | **None.** No Steam page, no wishlists, no list, no followers (00 §7). Category data shows original multiplayer KS campaigns without an audience mostly fail (01 §3.2). |
| 5 | Budget covers realistic delivery | 7 | 6 | 4.2 | Line-item model, NL fees verified, contingency 15% (03). But: hours are unvalidated estimates, half the MVD hours are unpaid, and Dutch VAT/income-tax treatment is an assumption until an accountant confirms it. |
| 6 | Goal financially defensible | 6 | 6 | 3.6 | Delivers the full budget at exactly 100% by construction. Not artificially low, because unpaid time is disclosed. Depends on #5's assumptions. |
| 7 | Rewards profitable and fulfillable | 5 | 9 | 4.5 | Digital-only. Net per backer is positive on every tier (05). Keys within the Steam cap at goal. Cosmetics need 60 h of work (budgeted). |
| 8 | Release dates credible | 7 | 4 | 2.8 | 30% buffer and MVD scope. But team availability is unknown, a critical bug is open, and the comparables shipped late (Carnival Hunt: 2 years). |
| 9 | Visuals consistent with the real game | 5 | 8 | 4.0 | Everything is in-engine (06, register). The illustration is labelled. But the stills are HUD-free photo mode; the mechanic needs real HUD captures. |
| 10 | Video demonstrates compelling gameplay | 6 | 2 | 1.2 | Only a watermarked **animatic** exists (07). The real video depends on a 4-player capture and the bug fix. |
| 11 | Claims supported by evidence | 5 | 7 | 3.5 | Every feature is tagged built or planned against the code (04). Placeholders remain for team facts. |
| 12 | Licences and asset rights documented | 5 | 4 | 2.0 | Audio and fonts are documented. **Several models have no recorded origin.** Marketing-render rights for the asset packs are unconfirmed (register). |
| 13 | Complies with current Kickstarter requirements | 10 | 4 | 4.0 | Creator is resident in the Netherlands, which is eligible (K3); identity and bank verification are still to do. **Steam keys can't be promised yet** (no own app, S3). AI disclosure is drafted but unconfirmed (K10). |
| 14 | Risks and limitations disclosed | 3 | 8 | 2.4 | Honest risk section, transparency box, "not promised" list (04 §8, §14). |
| 15 | Realistic plan to attract backers | 5 | 7 | 3.5 | A concrete, low-cost plan with gates (02, 08, 09). Not executed. Creator and press reach unknown. |
| 16 | Ready for external scrutiny | 2 | 3 | 0.6 | A backer asking "can I play it?" or "where's the Steam page?" gets no answer today. |
| | **Total** | **100** | | **52.1** | |

## 2. Blockers (each one alone prevents launch)

| # | Blocker | Why it blocks | What must change | Owner |
|---|---|---|---|---|
| ~~B1~~ | ~~Kickstarter eligibility~~ **Resolved 2026-10-17** | The creator is resident in the Netherlands, one of the 25 eligible countries (K3). | Still to do (not a blocker): pass Kickstarter/Stripe identity verification with a Dutch or other EU ID and a Dutch euro current account in your own name. | You |
| **B2** | **No Steam app / confirmed Steam distribution** | Steam keys can only be promised for products with confirmed Steam distribution (S3). Every game tier is a Steam key. | Pay Steam Direct ($100, S1). Create the REEF RUN app and a Coming Soon page. Request key types. | Team |
| **B3** | **No demand evidence** | Launching with zero audience against an all-or-nothing goal: comparable campaigns failed (01 §3.2). | Run 02 until gates G2–G4 pass: ≥7,500 wishlists, ≥1,500 KS followers or equivalent, ≥40% "yes at $9.99", ≥30% group return. | Team |
| **B4** | **Critical client-side bug** | A client shark can't eat (00 §4). Every public playtest, demo and video depends on the core loop working for all role draws. | Reproduce in a real Steam session. Fix. Re-test with the client as the shark. | Developers |
| **B5** | **Asset provenance** | The rules require rights to all materials (K2). The player fish, kelp and seaweed models have no recorded source. | Record the source and licence of every third-party model. Confirm marketing use for the asset packs. Replace anything unlicensed. | Team |

## 3. Needs work (not blockers, but required before launch)

1. **Real campaign video** from live gameplay (07 §8). Kickstarter doesn't require a video, but it says projects
   with one succeed "at a much higher rate" (K8).
2. **AI disclosure:** confirm the scope of AI-assisted coding, and either rewrite the AI-drafted copy or disclose it
   (04 §17).
3. **Team facts:** names, photos, roles, location; the "why we're making it" paragraph in the team's own words
   (04 §9, §12).
4. **Team capacity commitment** through Q4 2027, in writing, between the members (gate G5).
5. **Dutch accountant:** VAT (btw) treatment of reward pledges, the KOR small-business scheme, KvK registration and income tax. Replace the 6.1% VAT and 5% reserve assumptions (03 §6), and decide the "prices include VAT" wording (04 FAQ, 05 footer).
6. **HUD screenshots and GIFs** of the hearing mechanic (06 §4).
7. **Kickstarter project-image size:** confirm the editor's current requirement at upload (06 §1).
8. **Unity licence threshold:** confirm Unity Personal's current revenue/funding limit (03 §3).
9. Remove the hard-coded "v0.4.1 · EARLY ACCESS" menu label before any public build or capture.

## 4. Strongest evidence for launching (eventually)

- The friend-group, proximity-voice genre is the largest indie multiplayer trend of 2023–25 (Lethal Company 511k
  reviews, R.E.P.O. 429k, PEAK 380k), with prices of $5–10 and natural 4-copy purchases (01 §2).
- REEF RUN's hook (*the shark can hear you*) turns that genre's core feature into its core mechanic. No comparable
  game does this in PvP (01 §5).
- The closest original asymmetric MP Kickstarters raised $85k–$155k (Springfield Hotel, Carnival Hunt, Hide Or Die),
  well above the €12,000 MVD goal (01 §3.2), *once they had audiences*.
- The creator is in an eligible country (the Netherlands), so no foreign company is needed.
- The game is real and substantial after 16 weeks. Infrastructure costs are near zero (Steam relay and voice).
- The marketing assets are original, in-engine, and already Steam-compliant.

## 5. Biggest risks

1. **Zero audience** (B3). The single biggest determinant of crowdfunding outcomes (M1).
2. **Dutch VAT on reward pledges:** if most EU pledges carry 21% VAT inside the price, up to about 11% more of
   the money is lost than modelled. The accountant answer changes the goal.
3. **Exactly-4-player requirement:** limits the audience, and makes every playtest and demo harder to fill.
4. **Delivery capacity:** two people, unknown hours. A late, rough release would damage the Steam reviews that sell
   the game.
5. **Genre crowding:** many friend-group co-op titles in 2025–26; discovery is the bottleneck, not quality.

## 6. Recommendation

**Don't launch a Kickstarter now.** Building the project *draft* on kickstarter.com is harmless and useful (drafts are
private until you submit them for review); launching is what must wait. Do the following, in this order:
1. Fix B4.
2. Create the Steam app (B2).
3. Run the 12-week validation (02).
4. Do Steam Next Fest in Feb 2027.
5. Resolve asset provenance (B5) and get the Dutch accountant's VAT answer in parallel.

Re-score this audit at the gate review (March 2027). If every gate passes, the package is ready to adapt and launch
with the **€14,000 MVD goal (or the lean €6,000; 03 §1) for 30 days**. If the gates fail, the same work carries a Steam-first release, and that's
a better outcome than a public failed campaign.
