# 11 — Campaign Dashboard, Forecasts and Contingency Plans

**Spreadsheets:**
- `11_campaign_dashboard.xlsx`: inputs, pre-launch, daily campaign log, tiers, forecast, triggers. 539 formulas,
  0 errors (`tools/check_workbook.py`, 2026-10-17).
- `assets/data/REEFRUN_validation_tracker.xlsx`: weekly KPIs, clips, concept A/B/C comparison, playtest survey,
  go/no-go gates. 908 formulas, 0 errors.

Both are rebuilt by `tools/build_trackers.py`. Blue cells are typed in; the first grey italic row of every log is an
example to overwrite.

## 1. What it tracks

| Requirement | Where |
|---|---|
| Pre-launch email subscribers, KS followers, wishlists | *Pre-launch* (daily, 42 days) + validation *Weekly KPIs* |
| Qualified prospective backers | *Pre-launch* col F = followers × 20% + email × 4% (assumptions from 03) |
| Traffic sources, campaign visits | *Daily* cols K–Q (Kickstarter, email, social, creators/press, ads) from the KS creator dashboard |
| Pledges and cancellations | *Daily* C–F → net cumulative G, backers H |
| Conversion | *Daily* L = new backers ÷ visits |
| Average pledge, funding % | *Daily* J, I |
| Tier performance | *Tiers*: actual mix vs plan, limited tiers remaining |
| Marketing spend | *Daily* R–T: ad spend, attributed backers, cost per backer |
| Funding velocity, remaining time | *Daily* U (days left), V (EUR needed per remaining day) |
| Forecast | *Forecast*: conservative, baseline and optimistic final totals |
| Triggers | *Triggers* T1–T8, red when fired |

## 2. Forecasting method (assumption-heavy, be honest about it)

Final total ≈ pledged so far ÷ the share of the final total a campaign usually has by that day. Three
cumulative-share curves, with **day-3 anchors at 40% (conservative), 30% (baseline) and 22% (optimistic)**, a flat
middle and a last-3-days push. These describe the common "U-shaped" crowdfunding pattern. **They're assumptions, not
measured data for this genre**, so treat the forecast as unreliable before day 4. Replace the curves with your own
data afterwards.

**Pre-launch outcome forecast** (MVD goal €12,000, 416 backers needed). It's shown here and computed on the
*Pre-launch* sheet from your real numbers:

| Scenario | Followers at launch | Email list | Expected backers from them | + other sources (40%) | Projected outcome |
|---|---|---|---|---|---|
| Conservative | 500 | 800 | 132 | ≈88 | ≈220 backers ≈ €6.4k (53%), **fails** |
| Baseline | 1,250 | 1,500 | 310 | ≈207 | ≈517 backers ≈ €14.9k (124%) |
| Optimistic | 2,500 | 3,000 | 620 | ≈413 | ≈1,033 backers ≈ €29.9k (249%) |

These are illustrations of the gates. With today's real numbers (0 followers, 0 email) the forecast is **0%**.

## 3. Triggers (from the *Triggers* sheet)

| # | Fires when | Response |
|---|---|---|
| T1 | One week before launch, expected launch backers cover <30% of the goal | §4.1 Weak pre-launch |
| T2 | Funded <20% at the end of day 2 | §4.2 Slow first 48 h |
| T3 | Baseline forecast <100% (from day 4) | §4.5 Below expectations |
| T4 | Daily pledges <50% of the needed pace for 3 days running | §4.3 Plateau |
| T5 | Cancellations >5% of gross | Check for a bad update, a price complaint, card failures. Answer in comments |
| T6 | Ad cost per backer > net revenue per backer (~€22.10) | Pause the ads |
| T7 | Optimistic forecast >300% (from day 4) | §4.4 Sudden attention |
| T8 | A limited tier sells out | Mention it once. **Don't add another limited tier** (no manufactured scarcity) |

## 4. Contingency plans

**4.1 Weak pre-launch interest (T1, or gates G2/G3 missed)**
- **Postpone the launch.** A pre-launch page can stay up. A failed campaign is public and permanent.
- Re-run the validation loop on whichever concept performed best. Push the Steam playtest and the next Next Fest
  (Jun 14–21 2027).
- If two cycles miss: drop Kickstarter. Ship on Steam (Early Access or 1.0) at MVD scope, funded by the team or a
  small publisher deal.

**4.2 Slow first 48 hours (T2)**
- Personal (not mass) messages to everyone who said they'd back.
- Ask playtesters to bring their group (the Pod of Four is the ask).
- A creator session in the next 72 hours, from creators who already played (C3).
- A livestream with backers in a public lobby.
- Don't change the goal: it can't be changed (K12). Don't add stretch goals to fake momentum.
- If day 7 is still under 30%, decide openly whether to **cancel and relaunch** after more audience-building. Say
  why in an update.

**4.3 Mid-campaign plateau (T4)**
- New content every 2–3 days (08 §6): mechanic explainers, a balance devlog, a backers-vs-devs night.
- Shift ad spend to the best-converting channel only if T6 isn't firing.
- Reach new communities through creator sessions, not repeat posts in the same subreddit.

**4.4 Sudden increase in attention (T7, or a viral clip)**
- Have answers ready for the FAQ topics newcomers raise (4 players, mic, PC specs, release date). Pin a comment.
- **Keep scope fixed.** Only the €53,000 milestone is pre-costed. Don't improvise new stretch goals.
- Watch the beta-key cap: past about 1,390 backers at the current mix (1.8 keys per backer), beta keys exceed 2,500 and Steam Playtest keys
  cover the rest (S3). Say so in an update before it happens.
- Scale Discord moderation (recruit trusted playtesters).

**4.5 Funding below expectations (T3 by day 10–20)**
- If the realistic best case is under 100%: be candid in an update. Two honest options: push hard for the last
  week, or cancel early and relaunch with a lower-risk plan.
- **Never** back your own project or ask friends to make fake pledges to reach the goal (K2: inflating pledges is
  forbidden).

**4.6 Funding above expectations**
- Up to €53,000: the surplus pays the founder hours planned as unpaid, plus contingency. Show this in the
  post-campaign update.
- At €53,000 or more: the Realistic scope is promised. Publish its schedule (Q1–Q2 2028) within 2 weeks.
- Over about €111k: still **no new promises** beyond Realistic until a new costed plan is published. Extra money is
  runway and quality.

**4.7 Unexpected development costs**
- Order of fallback: contingency (15%) → pay cuts on founder stipends → scope cut of *unpromised* items only →
  schedule slip announced in advance.
- Never cut a promised reward. Never charge backers extra for a promised reward.

**4.8 Delays after funding**
- Post the delay **before** the date passes (template U2). Give a reason, a new date, and what's done and not done.
- Run a monthly update even when there's little news.
- If delivery becomes impossible: per Kickstarter's terms, explain, show how the funds were used, and offer
  refunds where possible (K: help article 16236551).
