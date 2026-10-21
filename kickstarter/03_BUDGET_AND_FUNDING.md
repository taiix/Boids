# 03 — Budget and Funding Model

**Campaign setup (updated 2026-10-17):**
- **The creator is resident in the Netherlands**, an eligible Kickstarter country (help article 16236650).
- It's an individual project raising in **EUR**, with no US company needed.

**Spreadsheet:** `03_funding_model.xlsx` (built by `tools/build_funding_model.py`, verified by
`tools/check_workbook.py`: 295 formulas, 0 errors). Blue cells are inputs and yellow cells are unverified
assumptions. **Every figure below is a formula output at the default inputs.** Change an input and the workbook
recalculates, so this document goes stale. Rebuild the tables from the workbook before publishing.

Fees were verified 2026-10-17 at kickstarter.com/help/fees (Netherlands): **5% platform + 3% + €0.30 per pledge**
(5% + €0.08 for pledges under €10).

> v2 note: sections 2–8 below were written for v1 (goal €12,000, average €28.90). The method is unchanged; take every
> number from the workbook or §1.

---

## 1. Headline (v2, 2026-10-20)

v2 changes:
- tiers repriced to match successful comparables (14 §3, 05);
- the ad budget raised to €2,250 (pre-launch €750 + live €1,500) per the Full Charge playbook;
- facts updated (Unacite Studio, 9 events).

| | **MVD** (recommended scope) | Realistic | Expanded |
|---|---|---|---|
| What backers get | The current game finished, polished and on Steam, with backer rewards | + second map, tasks, 6 languages, score, Steam Deck, 4 months of updates | + third map, AI fill-ins, second shark |
| Remaining work | 1,100 h | 2,020 h | 3,060 h |
| Team stipends | €5,500 (half the hours at €10/h) | €24,240 | €45,900 |
| Other cash costs (incl. ads) | €3,682 | €12,672 | €30,472 |
| Contingency (15%) | €1,377 | €5,537 | €11,456 |
| **Development budget** | **€10,559** | **€42,449** | **€87,828** |
| **Funding goal** | **€14,000** | **€55,500** | **€114,500** |
| Deductions at goal | 23.3% | 23.3% | 23.3% |
| Net at exactly 100% | €10,741 (+€182) | €42,581 | €87,848 |
| Backers needed (blended **€37.56**) | **373** | 1,478 | 3,049 |
| …at €15 / €22 / €30 / €40 average | 934 / 637 / 467 / 350 | 3,700 / 2,523 / 1,850 / 1,388 | 7,634 / 5,205 / 3,817 / 2,863 |
| Release window | Q4 2027 | Q1–Q2 2028 | Q4 2028 |
| Unpaid founder time (not cash) | €27,500 | €36,360 | €45,900 |

**Goal decision for the team: €14,000 or a lean €6,000.**
- **€14,000 (default):** pays half the remaining hours. It sits in the $10–20k band, where 27% of campaigns succeed
  and the median funded campaign raised $24.6k from 468 backers (14 §1).
- **€6,000 (lean):** stipends set to 0, so the goal covers only cash costs + contingency + deductions. That's 160
  backers. All 1,100 hours stay unpaid (≈€33,000 of time), and every euro above €6,000 pays hours first, then
  stretch goals.
  - This is how many of the successful comparables were set up: Springfield Hotel ($10k goal after $135k of
    founder money), All Will Rise (€10k), and a €5k median goal for funded euro campaigns.
  - It makes day-1 funding far more likely, which is what drives Kickstarter's algorithm.
  - **Only choose it if the team can finish the MVD unpaid**, because the goal must be deliverable at exactly 100%
    (K12).
  - To model it: *Scenarios* → MVD "Share of hours paid" = 0.

## 2. Deductions (why the goal is 36% above the budget)

At the default inputs, per €1 pledged:

| Deduction | Rate | Source |
|---|---|---|
| Kickstarter fee | 5.00% | K1, verified |
| Payment processing (dollar-weighted; tiers under €10 use 5% + €0.08) | 3.12% | K1, verified |
| Fixed processing fee (€0.245 average per pledge ÷ €28.90) | 0.85% | K1, verified |
| Failed / dropped payments | 3.00% | **Assumption** |
| Income-tax & compliance reserve | 5.00% | **Assumption: replace with an accountant's figure** |
| **VAT inside pledges** (35% of pledges from EU consumers × 21/121) | **6.07%** | **Assumption** (see §6) |
| Fulfilment (€0.10 + €0.04 tier-specific per backer ÷ €28.90) | 0.48% | **Assumption** |
| **Total** | **23.5%** | |

Goal = Budget ÷ (1 − 0.235) → rounded up to €500.

## 3. Cost lines (non-labor cash)

All of these are in the *Costs* sheet, with a note on each line.

| Item | MVD | Realistic | Expanded | Basis |
|---|---|---|---|---|
| Steam Direct fee ($100) | €87 | €87 | €87 | S1, verified; FX 1.15 assumed |
| Dutch accountant (income tax + VAT returns) | €900 | €1,350 | €1,800 | Assumption. Get a quote |
| KvK registration | €85 | €85 | €85 | Assumption. Check kvk.nl |
| Original music | — | €2,500 | €5,000 | Assumption. The current CC0 music ships fine |
| SFX licences | — | €500 | €1,500 | Assumption |
| Environment art packs | — | €400 | €900 | Assumption |
| Second-shark model + freelance artist | — | — | €7,500 | Assumption |
| Translation (≈4k words × €0.10 × languages) | — | €2,400 | €4,000 | Assumption. Get quotes |
| Compatibility hardware | €300 | €900 | €1,200 | Assumption |
| Software/domain/email tool | €60 | €150 | €300 | Unity Personal is free below Unity's threshold (**verify**) |
| Validation paid test (only with approval) | €250 | €250 | €250 | 02 §7 |
| Campaign paid promotion | €500 | €2,000 | €5,000 | Ads only complement community (M1) |
| Campaign video extras | — | €300 | €600 | MVD uses CC0 music and the founders' own voice-over |
| US-company lines (Stripe Atlas etc.) | €0 | €0 | €0 | Not needed: entity route = 0 |

**Infrastructure is nearly free by design:** Steam relay networking and Steam voice need no servers.

**Not double-counted:** labor appears only as stipends (cash) or unpaid value (not cash). Reward work is in the
*Labor* hours. Steam keys and Kickstarter's digital delivery cost nothing per unit.

## 4. Labor plan (hours, *Labor* sheet)

Unchanged by the move to the Netherlands: **1,100 / 2,020 / 3,060 hours**. Breakdown:

| Work package | MVD | Real. | Exp. |
|---|---|---|---|
| Fix client netId-0 bug + netcode hardening | 80 | 80 | 80 |
| Bug backlog / stability | 160 | 200 | 240 |
| Performance + Low/Medium presets | 120 | 140 | 160 |
| Onboarding | 80 | 100 | 120 |
| Controller + rebinding | 40 | 40 | 60 |
| Steamworks (own App ID, achievements, presence) | 40 | 50 | 60 |
| Settings and accessibility | 60 | 70 | 80 |
| Balance tuning | 80 | 120 | 160 |
| Playtest operations | 80 | 100 | 120 |
| Backer rewards in game | 60 | 60 | 60 |
| Making-of PDF | 20 | 20 | 20 |
| Campaign, store and community management | 200 | 240 | 280 |
| Release + 1 month of patches | 80 | 80 | 80 |
| Second map / tasks / localization / Deck / post-launch months 2–4 | — | 720 | 800 |
| Third map / AI fill-ins / second shark | — | — | 720 |

All hours are **assumptions**. The cost-overrun grid (Sensitivity §2) shows the Realistic budget at 1.25× hours is
€47,405 at €12/h. That's above its own net proceeds (€40,532), so **a 25% overrun on Realistic comes out of unpaid
time**.

## 5. Delivery timeline

| Date | Milestone |
|---|---|
| Oct–Dec 2026 | Validation: bug fix, Steam page, playtest #1 (02) |
| **Feb 22 – Mar 1 2027** | Steam Next Fest demo (S2) |
| Mar 2027 | Gate review; submit the Kickstarter project for review; pre-launch page |
| **Apr 13 – May 13 2027** (provisional) | 30-day campaign (≤30 days: Steam key rule, S3) |
| ~May 27 – Jun 10 2027 | Payout (≈14 days + bank days, K19) |
| **Jul 2027** | Backer beta (≤2,500 beta keys; Playtest keys beyond that) |
| **Q4 2027** | Full release on Steam + backer rewards |
| Q1–Q2 2028 | Second map, only if €53,000 is reached |

## 6. Dutch tax and VAT (high uncertainty: get an accountant before launch)

- **Eligibility:** an individual must be resident in the Netherlands, have a government ID from the Netherlands or
  another EU country, and a current account in their name that receives euros (K3). **You handle verification
  yourself on Kickstarter/Stripe.**
- **VAT (btw):** Dutch advisers treat a reward pledge with a direct counter-performance (here, a Steam key) as a
  taxable supply, with VAT due when the money is received (moore-drv.nl, taxlive.nl; **verify on
  belastingdienst.nl**).
  - Kickstarter's Pledge Manager collects and remits VAT itself only where it's the "deemed supplier", which is
    written around *shipped goods*. For other cases it offers an optional service that collects creator-responsible
    tax and pays it to *you* to remit (help article 16236608).
  - So the model treats VAT for EU consumers as **inside the pledge** (6.1% of pledged).
  - If your accountant confirms you can add VAT on top in the Pledge Manager, or that the small-business scheme
    (KOR) applies, set *Inputs → vat_share* to 0 and the goal falls.
- **Income tax:** campaign funds are taxable income; costs, including payments to teammates, are deductible.
  Registering at the KvK is likely, since this is business activity. The 5% reserve is a placeholder.
- **Paying teammates:** if team members live outside the Netherlands, pay them as contractors against invoices. The
  stipend rate is "fully loaded", so it includes their own taxes and social charges.
- **Steam key parity:** two tiers are under the planned €9.99 retail (the €8 early bird and €9 per key in the 4-pack).
  Plan a **≥20% Steam launch discount** (S3).

## 7. Major financial risks

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Campaign fails (under goal) | **High today** | No money, public failure | Gates first (02 §9) |
| VAT treatment worse than assumed (e.g. all EU pledges at 21% plus non-EU complications) | Medium | Up to −11% of pledged in the worst case | Accountant before launch. Input is editable |
| Hours overrun | Medium | Unpaid time grows | Contingency. Frozen scope. Monthly updates |
| Payment drops above 3% | Low-medium | −1–2% | Kickstarter retries failed cards |
| USD-pricing backers see converted prices | Low | Small conversion friction | Round euro tiers |
| Beta-key cap | Low (MVD ≈749 keys) | Some wait for release keys | Playtest keys beyond 2,500 |
| Over-funding pressure on scope | Low-medium | Delays | Only the €53,000 milestone |

## 8. How to update

1. Re-estimate *Labor* hours with the team.
2. Set *Inputs*: VAT share and rate after the accountant's advice, team hours per week, FX.
3. Replace *Costs* assumptions with quotes.
4. After day 3 of the campaign, replace the *Rewards* mix with the real shares.
5. Re-run `python tools/make_visuals.py` so the graphics pick up the new numbers.
