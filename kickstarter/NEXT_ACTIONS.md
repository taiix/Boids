# Next actions: the smallest set needed from you

Ordered by what unblocks the most. ☐ = open.

## v2 (2026-10-20): what changed and what it needs from you

- **The campaign is rebuilt** from the Full Charge playbook and 18 successful comparables (14, 15, 16).
  - New tiers: €12 early bird (limit 300) / €15 / €29 / **€49 Pod of Four** / €99 / €199.
  - New goal: €14,000, or a lean €6,000.
  - A stretch ladder, a budget shown by function, a press kit, an email sequence and a GIF recipe.
- ☐ **Decide the goal:** €14,000 (half the hours paid) or the lean €6,000 (all hours unpaid, day-1 funding far
  likelier). 03 §1.
- ☐ **Confirm the planned Steam price (€12.99 in v2, was €9.99)**. Ask it in playtest surveys.
- ☐ **Rewrite the page text in your own voice** (15). Then the AI disclosure covers coding only. Your closest
  competitor, Springfield Hotel, advertises "No AI" (14 §4).
- ☐ **The decisive two:** the 4-player capture session (video + the voice hero GIF and the escape GIF), and 1,000+
  Kickstarter followers before launch.
- ☑ **9 gameplay GIFs captured (2026-10-21)** in `assets/gifs/`. Which clips are staged and which are gameplay:
  `assets/ASSET_REGISTER.md`.
- ☑ **Story rebuilt (2026-10-21, second pass):** about 1,190 words in 11 sections, with 7 real gameplay GIFs, all
  started from the main menu and recorded in real time (see `assets/ASSET_REGISTER.md`). The new sections: how the
  shark hears you, play the shark, play a fish, the reef fights back, a round start to finish, and what exists
  today. The draft is still private and unsubmitted. **Rewrite the new text in your own voice; it was AI-drafted.**
- ☑ **Events (2026-10-21):** "The reef fights back" now has GIFs for 8 of the 9 events (all except the cold
  layer): orcas, storm, nightfall, red tide, trawler, jellyfish, current and sand slide. The story has 17 images.
- ☐ The orca fix (orcas shove the shark instead of swimming through it) is in `OrcaHunter.cs` and `FishPlayer.cs`
  (`ServerOrcaShove`). It was tested offline and as host; the client-shark path is untested. Not committed.
- ~~First pass:~~ GIFs added to the Kickstarter draft story (2026-10-21). Details:
  - `02_the_catch` under "And the shark can hear you" (a stand-in until the voice hero exists);
  - a captioned wall of 4 in "What is Reef Run?": hide in the school, turtle, shark sonar/lunge, orcas;
  - the line "In-engine footage from the current build. Work in progress."
  - Swap in GIF-1 (voice) and the escape after the 4-player session.
- ☐ Sign up for IMPRESS, Brevo or EmailOctopus, Linktree and Canva (free tiers). All text is in 16.
- ☑ **Kickstarter draft updated to v2 (2026-10-20)**, still private and unsubmitted.
  - Title "Reef Run", new subtitle, goal €14,000.
  - 7 tiers at v2 prices and names; item names say "Reef Run".
  - v2 story with 4 images; v2 risks; 10 FAQs.
  - **Still yours:** the [[…]] placeholders (team, "why", demo, VAT line, GIF wall), your city, the People and
    Payment tabs, and the Use of AI answers marked [[Confirm]].
- ⚠ **Git:** your Git tool auto-stashed the `kickstarter/` folder on 2026-10-20 at 12:14 (`stash@{1}`, together with
  your Marketing changes). I restored only `kickstarter/` into the working tree. **If you later `git stash pop`
  stash@{1}, it will refuse because those files now exist.** Move `kickstarter/` aside first, or commit it.

## Decisions and facts only you can provide (≈1 hour)

1. ☑ **Residence:** you're in the Netherlands (2026-10-17). The package is now an individual Dutch project in **EUR**
   (goal €12,000). Still yours to do on kickstarter.com: create your account, then pass Kickstarter/Stripe identity
   verification (Dutch or other EU ID) and add a Dutch euro current account in your own name. I can't enter IDs,
   passwords or bank details for you.
1a. ☑ **Kickstarter draft created 2026-10-17** (private, NOT submitted): https://www.kickstarter.com/projects/1793754777/1228105899/build
   Done: Basics (title, subtitle, Games › Video Games, image, €12,000, 30 days, target 13 Apr 2027), Preferences (Pledge
   Over Time off, Late Pledges off, Pledge Manager planned), 8 items + 7 reward tiers, story with 5 images, risks,
   Use of AI (draft answers marked [[Confirm]]), 9 FAQs. **Still yours:** project location (your city), every
   `[[…]]` placeholder in the story, tiers and AI answers, the People tab (bio/photo), the Payment tab, the rules
   "Got it". Don't click Submit for review until 12_LAUNCH_READINESS_AUDIT says Ready.
   Note from the editor: Kickstarter recommends reward prices *excluding* VAT, with VAT added in its Pledge Manager.
   Ask your accountant whether that works for you; if so, set `vat_share` to 0 in the model (the goal drops).
1b. ☐ **Dutch accountant (one meeting):** VAT on reward pledges (incl. the KOR scheme), KvK registration, income
   tax, and paying teammates who live elsewhere. Replace the VAT and tax inputs in `03_funding_model.xlsx`.
2. ☐ **Hours per week** each member can commit through Q4 2027, and whether anyone needs paying. *Assumed:* 60 h/week
   in total; MVD pays half the hours at €10/h.
3. ☐ **Licences:** where did `RiggedFish.fbx`, the kelp (`KelpForest 1.fbx`), the seaweed (`SM_Seabed_023_*`), the
   trash models and the FPX_Seabed rocks come from? Do the "Underwater life deluxe" and "Coral Reef Decor Pack"
   licences allow marketing renders? (12 B5)
4. ☐ **AI disclosure scope:** how much code was AI-assisted? Will you rewrite the campaign copy yourselves or
   disclose the AI drafting? (04 §17)
5. ☐ Do any social accounts, a Discord server or a mailing list already exist? (00 §7 assumes none)

## Do next (in order)

6. ☐ **Reproduce and fix the client-side netId-0 bug in a real 2-PC Steam session** (12 B4). Test with the
   *client* as the shark.
7. ☐ **Create the REEF RUN Steam app** (Steam Direct, $100) → Coming Soon page using `Marketing/Steam/` → set up a
   Steam Playtest. *Spends money and publishes: your call.*
8. ☐ Claim the handles. Set up the email tool (double opt-in). Publish `assets/landing/index.html` after filling in
   its placeholders.
9. ☐ **4-player capture session** (2–3 h) for G1–G7 (07 §4), including HUD shots of the hearing ripple. Get voice
   consent from everyone recorded.
10. ☐ Start the 12-week validation (02) and log it in `assets/data/REEFRUN_validation_tracker.xlsx`.
11. ☐ Register for **Steam Next Fest, Feb 22 – Mar 1 2027** (partner.steamgames.com/optin/sale/nextfest_feb_2027).

## Later (only if the gates pass, March 2027)

12. ☐ Accountant quote for the entity and tax route; replace the assumptions in `03_funding_model.xlsx`.
13. ☐ Fill every `[[…]]` in 04, 05, 07 and 10, and in the team graphic. Record the voice-over. Edit the real video.
14. ☐ Build the Kickstarter page, submit for review ≥4 weeks before launch, turn Late Pledges **off**, set the
    duration to **30 days** (Steam key rules).
15. ☐ Re-score `12_LAUNCH_READINESS_AUDIT.md`. Launch only if it says Ready.

*Nothing in this package has been published, sent, purchased or committed on your behalf.*
