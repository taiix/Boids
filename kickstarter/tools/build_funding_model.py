"""Builds kickstarter/03_funding_model.xlsx: the editable budget, funding goal, reward and sensitivity model.

Run:  python tools/build_funding_model.py      (from the kickstarter/ folder)
Then: python tools/check_workbook.py 03_funding_model.xlsx   (evaluates every formula, reports errors + key outputs)

Every number a human may want to change is an input cell (blue text; yellow fill = key assumption). Everything else
is a formula, so editing an input in Excel/LibreOffice/Sheets recalculates the whole model.
"""
from openpyxl import Workbook
from openpyxl.comments import Comment
from openpyxl.styles import Alignment, Border, Font, PatternFill, Side
from openpyxl.utils import get_column_letter

OUT = "03_funding_model.xlsx"
F = "Arial"
BLUE = Font(name=F, color="0000FF")
BLACK = Font(name=F)
GREEN = Font(name=F, color="008000")
BOLD = Font(name=F, bold=True)
TITLE = Font(name=F, bold=True, size=14)
HDR = Font(name=F, bold=True, color="FFFFFF")
HDR_FILL = PatternFill("solid", fgColor="0B3B52")
KEY = PatternFill("solid", fgColor="FFFF00")
OUT_FILL = PatternFill("solid", fgColor="DDEFF5")
thin = Side(style="thin", color="BBBBBB")
BOX = Border(top=thin, bottom=thin, left=thin, right=thin)
# Campaign currency: EUR (creator resident in the Netherlands, 2026-10-17). The USD/USD2 names are kept for
# the money formats but now render euros; costs quoted in USD are converted with the FX input.
USD = '€#,##0;(€#,##0);"-"'
USD2 = '€#,##0.00;(€#,##0.00);"-"'
EUR = '€#,##0;(€#,##0);"-"'
PCT = '0.0%;(0.0%);"-"'
NUM = '#,##0;(#,##0);"-"'
DEC = '0.0;(0.0);"-"'

wb = Workbook()
REF = {}  # input key -> absolute reference like Inputs!$B$5


def style_header(ws, row, ncols):
    for c in range(1, ncols + 1):
        cell = ws.cell(row=row, column=c)
        cell.font, cell.fill = HDR, HDR_FILL
        cell.alignment = Alignment(wrap_text=True, vertical="center")


def widths(ws, ws_widths):
    for i, w in enumerate(ws_widths, start=1):
        ws.column_dimensions[get_column_letter(i)].width = w


def put(ws, addr, value, font=BLACK, fmt=None, fill=None, comment=None):
    c = ws[addr]
    c.value = value
    c.font = font
    if fmt:
        c.number_format = fmt
    if fill:
        c.fill = fill
    if comment:
        c.comment = Comment(comment, "REEF RUN model")
    return c


# ---------------------------------------------------------------- README
ws = wb.active
ws.title = "README"
widths(ws, [110])
lines = [
    ("REEF RUN — Kickstarter funding model", TITLE),
    ("Built 2026-10-17 by tools/build_funding_model.py. Campaign currency: EUR (creator resident in the Netherlands). Costs quoted in USD (Steam fee, US entity) are converted with the FX input.", BLACK),
    ("", BLACK),
    ("COLOUR LEGEND", BOLD),
    ("Blue text = an input you may edit.  Yellow fill = a key assumption that is NOT verified — check it first.  Black = formula (do not overwrite).  Green = pulled from another sheet.", BLACK),
    ("", BLACK),
    ("HOW TO USE", BOLD),
    ("1. Inputs: platform fees (verified 2026-10-17, kickstarter.com/help/fees), FX, drop rate, tax reserve, entity route, conversion rates.", BLACK),
    ("2. Labor: remaining work packages in hours per scenario (MVD / Realistic / Expanded). Edit hours as the team re-estimates.", BLACK),
    ("3. Costs: every non-labor cash cost per scenario, with source or reasoning. Founder labor is NOT here — it is on Scenarios as stipend (cash) and as unpaid value (not cash).", BLACK),
    ("4. Rewards: tier prices, expected mix of backers, fulfilment cost and fees → blended average pledge and effective fees used by Scenarios.", BLACK),
    ("5. Scenarios: budget → funding goal (grossed up for fees, failed payments, tax reserve and fulfilment) → net proceeds, backers needed, timeline.", BLACK),
    ("6. Sensitivity and Backer Funnel: how goals and audience needs move with pledge size, cost overruns and conversion.", BLACK),
    ("", BLACK),
    ("GOAL FORMULA", BOLD),
    ("Goal = Budget ÷ (1 − Kickstarter fee − effective processing % − failed-payment rate − tax reserve − VAT inside pledges − (fixed fee per pledge + fulfilment per backer) ÷ average pledge), rounded up to the next €500.", BLACK),
    ("This is the amount at which, funded at exactly 100%, the team still receives the full budget after every deduction (Kickstarter guidance, help article 16236709).", BLACK),
    ("", BLACK),
    ("WHAT THIS MODEL DOES NOT DO", BOLD),
    ("It is not tax or legal advice. Dutch income tax, VAT (btw) on reward pledges and KvK registration depend on your situation — get an accountant (budgeted on Costs).", BLACK),
    ("Steam revenue after launch is not modelled; the campaign must fund delivery on its own.", BLACK),
]
for i, (t, f) in enumerate(lines, start=1):
    put(ws, f"A{i}", t, f).alignment = Alignment(wrap_text=True)

# ---------------------------------------------------------------- Inputs
ws = wb.create_sheet("Inputs")
widths(ws, [46, 14, 14, 90])
ws.append(["Input", "Value", "Unit", "Source / reasoning"])
style_header(ws, 1, 4)
inputs = [
    # key, label, value, unit, fmt, source, is_key_assumption
    ("ks_fee", "Kickstarter platform fee", 0.05, "% of funds", PCT, "Verified 2026-10-17: kickstarter.com/help/fees (5% of total funds raised).", False),
    ("proc_pct", "Payment processing — percentage (pledges ≥ €10)", 0.03, "% of pledge", PCT, "Verified 2026-10-17: kickstarter.com/help/fees (Netherlands) — 3% + €0.30 per pledge.", False),
    ("proc_fixed", "Payment processing — fixed per pledge (≥ €10)", 0.30, "EUR/pledge", USD2, "Verified 2026-10-17: kickstarter.com/help/fees (Netherlands) — 3% + €0.30 per pledge.", False),
    ("micro_pct", "Micropledge processing % (pledges < €10)", 0.05, "% of pledge", PCT, "Verified 2026-10-17 (Netherlands): 'Pledges under €10 have a discounted micropledge fee of 5% + €0.08'.", False),
    ("micro_fixed", "Micropledge fixed per pledge (< €10)", 0.08, "EUR/pledge", USD2, "Verified 2026-10-17 (same page).", False),
    ("micro_threshold", "Micropledge threshold", 10, "EUR", USD, "Verified 2026-10-17 (same page).", False),
    ("fx", "FX: USD per 1 EUR", 1.15, "USD/EUR", "0.00", "ASSUMPTION — only converts costs quoted in USD (Steam fee). Check the current rate.", True),
    ("drop_rate", "Failed / dropped payments", 0.03, "% of pledged", PCT, "ASSUMPTION — some pledges fail at card charge; Kickstarter retries for 7 days. No official rate published; 2–5% is a common planning range.", True),
    ("currency_note", "Campaign currency (text, informational)", "EUR", "", None, "Creator resident in the Netherlands (stated 2026-10-17): an individual NL project raises in EUR (help article 16236650).", False),
    ("tax_reserve", "Tax & compliance reserve", 0.05, "% of pledged", PCT, "ASSUMPTION — Dutch income tax on the part of the funds that ends up as the creator's own profit, plus filing surprises. Replace with an accountant's figure.", True),
    ("vat_share", "Share of pledges from EU consumers (VAT due inside the pledge)", 0.35, "% of pledged", PCT, "ASSUMPTION — reward pledges with a direct counter-performance are a VAT-taxable supply under the Dutch view (moore-drv.nl, taxlive.nl; verify with belastingdienst.nl). Kickstarter's automatic VAT handling targets shipped goods, so a Dutch creator of digital rewards is the responsible party. Set to 0 if the Pledge Manager collects VAT on top of pledges.", True),
    ("vat_rate", "VAT rate assumed (NL standard rate)", 0.21, "%", PCT, "21% NL standard rate. Cross-border EU consumer sales above €10k/yr use the backer's country rate (OSS) — similar range. Small-business scheme (KOR) may exempt below €20k turnover — ask the accountant.", True),
    ("fulfil", "Fulfilment cost per backer (digital)", 0.10, "EUR/backer", USD2, "ASSUMPTION — Steam keys and Kickstarter Pledge Manager digital delivery cost nothing per key; this covers email-tool overage and support time outside the labor plan.", True),
    ("entity", "Entity route: 1 = US company via Stripe Atlas, 0 = an eligible resident launches", 0, "flag", "0", "0: the creator is resident in the Netherlands, an eligible country (stated 2026-10-17). US-company lines then cost nothing.", False),
    ("atlas", "Stripe Atlas incorporation (one-time)", 500, "USD", USD, "Secondary sources (rho.co, unilink.us, 2026): $500 one-time incl. year-1 registered agent. Verify at stripe.com/atlas.", True),
    ("agent", "US registered agent renewal (year 2)", 100, "USD/yr", USD, "Secondary sources: $100/yr after year 1.", True),
    ("franchise", "US state annual tax / franchise tax", 300, "USD/yr", USD, "Secondary source (unilink.us): ~$300/yr (Delaware). Depends on entity type — verify.", True),
    ("us_filing", "US tax filings by an accountant (foreign-owned company)", 600, "USD/yr", USD, "ASSUMPTION — foreign-owned US companies have annual federal information returns; get a quote.", True),
    ("bg_acc", "Dutch accountant: income-tax return + VAT returns", 900, "EUR", EUR, "ASSUMPTION — get a quote; covers the campaign year.", True),
    ("kvk", "KvK (Chamber of Commerce) registration, if you register as a business", 85, "EUR", EUR, "ASSUMPTION — one-off KvK fee; check kvk.nl. Likely needed: receiving reward pledges is business activity under the Dutch view.", True),
    ("steam_direct", "Steam Direct fee (own App ID)", 100, "USD", USD, "Verified 2026-10-17: partner.steamgames.com/doc/gettingstarted/appfee — $100, recoupable after $1,000 revenue.", False),
    ("contingency", "Contingency on development budget", 0.15, "%", PCT, "ASSUMPTION — 15% is a conventional minimum for small software projects; Carnival Hunt (KS 2023) shipped 2 years late, so do not lower this.", True),
    ("team_hours", "Team capacity, hours per week (all members)", 60, "h/week", NUM, "ASSUMPTION — 2 developers × 30 h. Confirm with the team (NEXT_ACTIONS).", True),
    ("market_rate", "Market value of founder time (for unpaid-labor disclosure only)", 30, "EUR/h", EUR, "ASSUMPTION — freelance Unity developer rate in Eastern Europe; used only to show the value of unpaid work, never in the goal.", True),
    ("share_followers", "Share of backers coming from pre-launch followers + email", 0.60, "%", PCT, "ASSUMPTION — first-time creators get little Kickstarter discovery; see 02 §8.", True),
    ("conv_low", "Pre-launch follower → backer conversion (low)", 0.10, "%", PCT, "Range from secondary sources (02 §8, SOURCES M7): 8–11% planning rate (Botany case study).", True),
    ("conv_mid", "Pre-launch follower → backer conversion (mid)", 0.20, "%", PCT, "Midpoint assumption.", True),
    ("conv_high", "Pre-launch follower → backer conversion (high)", 0.30, "%", PCT, "~31% average quoted by a Kickstarter outreach lead for comics (SOURCES M7) — not games.", True),
    ("email_conv", "Email subscriber → backer conversion", 0.04, "%", PCT, "ASSUMPTION — Botany case study planned 1–3% for email lists; a playtest-engaged list should do better.", True),
    ("wish_conv", "Steam wishlist → Kickstarter backer", 0.03, "%", PCT, "ASSUMPTION — no public data found. Used only as a rough proxy.", True),
    ("beta_cap", "Steam beta (Release State Override) key cap for crowdfunding", 2500, "keys", NUM, "Verified 2026-10-17: partner.steamgames.com/doc/features/keys — 'generally 2,500 total'.", False),
]
for i, (key, label, val, unit, fmt, src, keyass) in enumerate(inputs, start=2):
    put(ws, f"A{i}", label)
    put(ws, f"B{i}", val, BLUE, fmt, KEY if keyass else None)
    put(ws, f"C{i}", unit)
    put(ws, f"D{i}", src).alignment = Alignment(wrap_text=True)
    REF[key] = f"Inputs!$B${i}"
for r in range(1, len(inputs) + 2):
    ws.row_dimensions[r].height = 30
ws.freeze_panes = "A2"

# ---------------------------------------------------------------- Labor
ws = wb.create_sheet("Labor")
widths(ws, [48, 12, 12, 12, 90])
ws.append(["Work package (remaining work)", "MVD (h)", "Realistic (h)", "Expanded (h)", "What it covers / why (all hours are ASSUMPTIONS — re-estimate with the team)"])
style_header(ws, 1, 5)
labor = [
    ("Fix client-side netId-0 bug + netcode hardening", 80, 80, 80, "Critical defect (00 §4): client shark cannot eat. Includes a real Steam 4-PC test matrix."),
    ("Bug backlog, stability, crash fixes", 160, 200, 240, "From playtest reports; scales with content."),
    ("Performance + Low/Medium quality presets (HDRP)", 120, 140, 160, "Editor baseline 17.5 ms main / 11–14 ms GPU at 1080p (2026-10-16). Friend groups have mixed PCs."),
    ("Onboarding: first-round hints, controls screen", 80, 100, 120, "No tutorial exists today."),
    ("Controller support complete + key rebinding", 40, 40, 60, "Movement has gamepad bindings; UI/tasks unverified."),
    ("Steamworks: own App ID, depots, achievements, rich presence", 40, 50, 60, "Today the game runs on Valve's test App 480."),
    ("Settings & accessibility (push-to-talk, colour-blind glows, captions for alerts)", 60, 70, 80, "Voice is core — push-to-talk/open-mic choice is mandatory for comfort."),
    ("Balance tuning from playtests", 80, 120, 160, "Shark vs fish win-rate targets."),
    ("Playtest operations (weekends, surveys, Discord)", 80, 100, 120, "Runs from validation through beta."),
    ("Backer rewards: credits, Founder cosmetics (2) + selection UI, trawler names", 60, 60, 60, "Rewards in 05 that need game work."),
    ("Making-of digital book (PDF)", 20, 20, 20, "Founder-tier reward; in-engine screenshots + text."),
    ("Campaign + store + community management (pre-launch through campaign end)", 200, 240, 280, "Clips, posts, updates, comments, outreach."),
    ("Release management + 1 month post-launch patches", 80, 80, 80, "Builds, hotfixes."),
    ("Second map (kelp forest / shipwreck) — Realistic scope", 0, 320, 320, "Not in MVD. Uses asset packs + in-house layout."),
    ("Two new co-op tasks — Realistic scope", 0, 120, 120, "Keeps rounds fresh."),
    ("Localization integration (Unity Localization, fonts)", 0, 60, 80, "Translation itself is a cash cost (Costs)."),
    ("Steam Deck verification pass", 0, 60, 60, "Needs perf work above."),
    ("Post-launch support months 2–4", 0, 160, 240, "Free updates promised in Realistic."),
    ("Third map — Expanded scope", 0, 0, 320, "Not promised unless Expanded is funded."),
    ("AI fish fill-ins so 2–3 players can play — Expanded scope", 0, 0, 240, "Biggest audience unlock but high netcode/AI risk."),
    ("Second shark species / role variant — Expanded scope", 0, 0, 160, "Needs a licensed model (Costs)."),
]
for r, row in enumerate(labor, start=2):
    put(ws, f"A{r}", row[0])
    for j, col in enumerate("BCD"):
        put(ws, f"{col}{r}", row[j + 1], BLUE, NUM)
    put(ws, f"E{r}", row[4]).alignment = Alignment(wrap_text=True)
LT = len(labor) + 2
put(ws, f"A{LT}", "TOTAL remaining hours", BOLD)
for col in "BCD":
    put(ws, f"{col}{LT}", f"=SUM({col}2:{col}{LT-1})", BOLD, NUM, OUT_FILL)
ws.freeze_panes = "A2"

# ---------------------------------------------------------------- Costs (cash, non-labor)
ws = wb.create_sheet("Costs")
widths(ws, [44, 9, 12, 12, 12, 13, 13, 13, 80])
ws.append(["Cash cost item (non-labor)", "Currency", "MVD", "Realistic", "Expanded", "MVD (EUR)", "Realistic (EUR)", "Expanded (EUR)", "Source / reasoning"])
style_header(ws, 1, 9)
E = REF["entity"]
costs = [
    ("Steam Direct fee", "USD", f"={REF['steam_direct']}", f"={REF['steam_direct']}", f"={REF['steam_direct']}", "Verified (Inputs)."),
    ("US company: Stripe Atlas setup", "USD", f"={REF['atlas']}*{E}", f"={REF['atlas']}*{E}", f"={REF['atlas']}*{E}", "Only if entity route = 1."),
    ("US company: year-2 agent + state tax", "USD", f"=({REF['agent']}+{REF['franchise']})*{E}", f"=({REF['agent']}+{REF['franchise']})*{E}", f"=({REF['agent']}+{REF['franchise']})*{E}", "Delivery runs into a second year."),
    ("US company: accountant filings (2 years)", "USD", f"=2*{REF['us_filing']}*{E}", f"=2*{REF['us_filing']}*{E}", f"=2*{REF['us_filing']}*{E}", "Assumption; get a quote."),
    ("Dutch accountant (income tax + VAT)", "EUR", f"={REF['bg_acc']}", f"={REF['bg_acc']}*1.5", f"={REF['bg_acc']}*2", "Assumption: longer delivery = more filing years."),
    ("KvK registration", "EUR", f"={REF['kvk']}", f"={REF['kvk']}", f"={REF['kvk']}", "Assumption."),
    ("Original music (score + jingles)", "EUR", 0, 2500, 5000, "Current music is CC0 third-party and fine to ship; an original score lets the team offer a soundtrack later. Freelance quote needed."),
    ("Sound design pack licences", "EUR", 0, 500, 1500, "Asset-store / library SFX; assumption."),
    ("Art: environment packs for second/third map", "EUR", 0, 400, 900, "Asset-store packs; check licences allow marketing renders."),
    ("Art: licensed model for second shark species", "EUR", 0, 0, 1500, "Expanded only; freelance or store model."),
    ("Freelance artist contract (Expanded)", "EUR", 0, 0, 6000, "Expanded only: ~3 months part-time 3D/tech-art help. Assumption."),
    ("Localization: translation (6 languages Realistic / 10 Expanded)", "EUR", 0, 2400, 4000, "≈4,000 words × €0.10/word × languages (assumption; get quotes). MVD ships English only."),
    ("Compatibility hardware (used low-end GPU / Steam Deck)", "EUR", 300, 900, 1200, "Assumption. Steam Deck only from Realistic."),
    ("Software, domain, email tool, misc. services", "EUR", 60, 150, 300, "Unity Personal is free below its revenue/funding threshold (verify current threshold at unity.com before launch). Steam relay + voice cost nothing."),
    ("Pre-launch ads: Kickstarter-follower & email lead ads", "EUR", 750, 1000, 1500, "Full Charge playbook: start $5–10/day, test 3–4 creatives; benchmark $1.70–2.50 per KS follower with strong gameplay creative → ~300–440 followers at MVD spend. Only with explicit approval."),
    ("Live-campaign ads (Meta/Reddit), retargeting", "EUR", 1500, 3000, 6000, "Guide: 20–30% of goal to ads when starting from scratch; Boo Croo budgeted 10% marketing. Pause if ad cost per backer > net per backer (dashboard T6)."),
    ("Campaign video: music licence / voice-over / stock SFX", "EUR", 0, 300, 600, "MVD uses CC0 music + founders' own voice-over."),
    ("Press/creator kit hosting, keys service", "EUR", 0, 0, 0, "Free tools (Steam keys, presskit pages)."),
]
for r, row in enumerate(costs, start=2):
    put(ws, f"A{r}", row[0])
    put(ws, f"B{r}", row[1], BLUE)
    for j, col in enumerate("CDE"):
        v = row[j + 2]
        put(ws, f"{col}{r}", v, BLUE if not str(v).startswith("=") else GREEN, '#,##0;(#,##0);"-"')
    for src, dst in zip("CDE", "FGH"):
        put(ws, f"{dst}{r}", f'=IF($B{r}="USD",{src}{r}/{REF["fx"]},{src}{r})', BLACK, USD)
    put(ws, f"I{r}", row[5]).alignment = Alignment(wrap_text=True)
CT = len(costs) + 2
put(ws, f"A{CT}", "TOTAL cash costs (EUR)", BOLD)
for col in "FGH":
    put(ws, f"{col}{CT}", f"=SUM({col}2:{col}{CT-1})", BOLD, USD, OUT_FILL)
ws.freeze_panes = "A2"

# ---------------------------------------------------------------- Rewards
ws = wb.create_sheet("Rewards")
widths(ws, [24, 9, 9, 10, 10, 11, 10, 10, 11, 11, 11, 52, 26, 16, 14, 40])
hdr = ["Tier", "Price (EUR)", "Limit", "Mix (share of backers)", "Steam keys incl.", "Fulfilment €/backer",
       "Proc. %", "Fixed fee €", "Total fee €/pledge", "Net €/backer", "€ share (weight)", "Includes",
       "Target segment", "Delivery", "Complexity", "Main risk"]
ws.append(hdr)
style_header(ws, 1, len(hdr))
tiers = [
    # v2 (2026-10-20): priced like the 18 successful comparables in 14_WHAT_SUCCESSFUL_CAMPAIGNS_DO.md — single copy
    # €12–20 with a limited early bird as the top seller, deluxe €25–40, group packs. Mix shares are ASSUMPTIONS.
    ("Ripple (thank you)", 5, None, 0.07, 0, 0.0, "Name in in-game Supporters credits; backer Discord role; 4K wallpaper pack (from master key art).", "Friends & well-wishers", "Jun 2027", "Very low", "None material."),
    ("Early Diver (limited)", 12, 300, 0.18, 1, 0.0, "1 Steam key for Reef Run (backer beta, then full game); name in Supporters credits.", "Early S1/S3 backers", "Beta: Jul 2027", "Low", "Below the planned €12.99 retail → plan a Steam launch discount for parity (Steam key rules)."),
    ("Diver", 15, None, 0.20, 1, 0.0, "Same as Early Diver.", "S1/S3", "Beta: Jul 2027", "Low", "Beta-key cap 2,500 (Steam)."),
    ("Founding Diver", 29, None, 0.22, 1, 0.0, "Diver + Founder cosmetics (fish 'Founder Fin' pattern + shark 'Old Scar' skin) + Founders credits section + Making-of PDF.", "S3, S2", "Key Jul 2027 · cosmetics at 1.0", "Medium", "Cosmetics need a selection UI + network sync (in Labor)."),
    ("Pod of Four", 49, None, 0.20, 4, 0.0, "4 Steam keys (for your group) + beta access for all 4 + 4 names in Supporters credits.", "S1 friend groups", "Beta: Jul 2027", "Low", "Uses 4 beta keys each → watch the 2,500 cap."),
    ("Founding Pod", 99, None, 0.10, 4, 0.0, "Pod of Four + Founder cosmetics ×4 + 4 Founders credits + Making-of PDF.", "Committed groups, S3", "Keys Jul 2027 · cosmetics at 1.0", "Medium", "Same as Founding Diver."),
    ("Name the Trawler (limited)", 199, 25, 0.03, 1, 2.0, "Founding Diver + your name (moderated) painted on the trawler that sweeps the reef — one name shown per round, rotating.", "Superfans", "At 1.0", "Medium", "Moderation of names; ~0.5 h each (in Labor)."),
]
first = 2
for r, t in enumerate(tiers, start=first):
    put(ws, f"A{r}", t[0])
    put(ws, f"B{r}", t[1], BLUE, USD)
    put(ws, f"C{r}", t[2] if t[2] else "—", BLUE)
    put(ws, f"D{r}", t[3], BLUE, PCT, KEY)
    put(ws, f"E{r}", t[4], BLUE, NUM)
    put(ws, f"F{r}", t[5], BLUE, USD2)
    put(ws, f"G{r}", f"=IF(B{r}<{REF['micro_threshold']},{REF['micro_pct']},{REF['proc_pct']})", GREEN, PCT)
    put(ws, f"H{r}", f"=IF(B{r}<{REF['micro_threshold']},{REF['micro_fixed']},{REF['proc_fixed']})", GREEN, USD2)
    put(ws, f"I{r}", f"=B{r}*({REF['ks_fee']}+G{r}+{REF['drop_rate']}+{REF['tax_reserve']}+{REF['vat_share']}*{REF['vat_rate']}/(1+{REF['vat_rate']}))+H{r}", BLACK, USD2)
    put(ws, f"J{r}", f"=B{r}-I{r}-F{r}-{REF['fulfil']}", BLACK, USD2)
    put(ws, f"K{r}", f"=B{r}*D{r}", BLACK, USD2)
    for col, v in zip("LMNOP", t[6:]):
        put(ws, f"{col}{r}", v).alignment = Alignment(wrap_text=True, vertical="top")
last = first + len(tiers) - 1
S = last + 2
put(ws, f"A{S}", "Mix check (must be 100%)", BOLD)
put(ws, f"D{S}", f"=SUM(D{first}:D{last})", BOLD, PCT, OUT_FILL)
put(ws, f"A{S+1}", "Blended average pledge (EUR)", BOLD)
put(ws, f"B{S+1}", f"=SUMPRODUCT(B{first}:B{last},D{first}:D{last})/SUM(D{first}:D{last})", BOLD, USD2, OUT_FILL)
put(ws, f"A{S+2}", "Effective processing % (dollar-weighted)", BOLD)
put(ws, f"B{S+2}", f"=SUMPRODUCT(G{first}:G{last},K{first}:K{last})/SUM(K{first}:K{last})", BOLD, PCT, OUT_FILL)
put(ws, f"A{S+3}", "Effective fixed fee per pledge (EUR)", BOLD)
put(ws, f"B{S+3}", f"=SUMPRODUCT(H{first}:H{last},D{first}:D{last})/SUM(D{first}:D{last})", BOLD, USD2, OUT_FILL)
put(ws, f"A{S+4}", "Tier fulfilment per backer, blended (EUR)", BOLD)
put(ws, f"B{S+4}", f"=SUMPRODUCT(F{first}:F{last},D{first}:D{last})/SUM(D{first}:D{last})", BOLD, USD2, OUT_FILL)
put(ws, f"A{S+5}", "Steam keys per backer, blended", BOLD)
put(ws, f"B{S+5}", f"=SUMPRODUCT(E{first}:E{last},D{first}:D{last})/SUM(D{first}:D{last})", BOLD, "0.00", OUT_FILL)
put(ws, f"A{S+7}", "Mix shares are ASSUMPTIONS informed by comparable campaigns (01 §3.3: cheapest game tier and friend packs draw most backers). Replace with real data after launch day 3.").alignment = Alignment(wrap_text=True)
REF.update({"avg": f"Rewards!$B${S+1}", "eff_proc": f"Rewards!$B${S+2}", "eff_fixed": f"Rewards!$B${S+3}",
            "tier_fulfil": f"Rewards!$B${S+4}", "keys_per_backer": f"Rewards!$B${S+5}"})
ws.freeze_panes = "B2"

# ---------------------------------------------------------------- Scenarios
ws = wb.create_sheet("Scenarios")
widths(ws, [58, 16, 16, 16, 70])
ws.append(["Line", "MVD", "Realistic", "Expanded", "Notes"])
style_header(ws, 1, 5)
rows = {}


def srow(key, label, vals, fmt, font=BLACK, fill=None, note=""):
    r = ws.max_row + 1
    rows[key] = r
    put(ws, f"A{r}", label, BOLD if fill else BLACK)
    for col, v in zip("BCD", vals):
        put(ws, f"{col}{r}", v, font, fmt, fill)
    put(ws, f"E{r}", note).alignment = Alignment(wrap_text=True)
    return r


srow("scope", "Scope (what backers are promised)", ["Current game, finished & polished", "+ 2nd map, 2 tasks, 6 languages, score, Deck", "+ 3rd map, AI fill-ins, 2nd shark"], None, BLUE,
     note="Only the funded scenario's scope may be promised (05).")
srow("hours", "Remaining labor (hours)", [f"=Labor!{c}{LT}" for c in "BCD"], NUM, GREEN)
srow("rate", "Stipend paid to team (EUR per hour, fully loaded)", [10, 12, 15], EUR, BLUE, KEY,
     "ASSUMPTION — a modest stipend so members can commit hours; includes employer/freelancer social charges. Team must confirm.")
srow("share", "Share of hours paid from the campaign", [0.5, 1.0, 1.0], PCT, BLUE, KEY,
     "MVD pays half the hours; the rest is unpaid founder time (disclosed below).")
r = rows
srow("stipend", "Team stipends (EUR)", [f"={c}{r['hours']}*{c}{r['rate']}*{c}{r['share']}" for c in "BCD"], USD)
srow("cash", "Non-labor cash costs (EUR)", [f"=Costs!{c}{CT}" for c in "FGH"], USD, GREEN)
srow("sub", "Subtotal", [f"={c}{r['stipend']}+{c}{r['cash']}" for c in "BCD"], USD)
srow("cont", "Contingency", [f"={c}{r['sub']}*{REF['contingency']}" for c in "BCD"], USD)
srow("budget", "TOTAL DEVELOPMENT BUDGET (cash the team must receive)", [f"={c}{r['sub']}+{c}{r['cont']}" for c in "BCD"], USD, BOLD, OUT_FILL)
srow("avg", "Average pledge used (EUR)", [f"={REF['avg']}"] * 3, USD2, GREEN, note="From Rewards (blended mix).")
srow("deduct", "Deductions as % of pledged (fee + proc. + drops + tax + VAT + per-pledge costs)",
     [f"={REF['ks_fee']}+{REF['eff_proc']}+{REF['drop_rate']}+{REF['tax_reserve']}+{REF['vat_share']}*{REF['vat_rate']}/(1+{REF['vat_rate']})+({REF['eff_fixed']}+{REF['fulfil']}+{REF['tier_fulfil']})/{c}{r['avg']}" for c in "BCD"], PCT)
srow("goal_raw", "Goal before rounding", [f"={c}{r['budget']}/(1-{c}{r['deduct']})" for c in "BCD"], USD)
srow("goal", "FUNDING GOAL (rounded up to €500)", [f"=ROUNDUP({c}{r['goal_raw']}/500,0)*500" for c in "BCD"], USD, BOLD, OUT_FILL,
     "Kickstarter test: at exactly 100% can we deliver? Yes by construction.")
srow("backers", "Backers needed at average pledge", [f"=ROUNDUP({c}{r['goal']}/{c}{r['avg']},0)" for c in "BCD"], NUM, BOLD)
srow("f_ks", "  Kickstarter fee at goal", [f"={c}{r['goal']}*{REF['ks_fee']}" for c in "BCD"], USD)
srow("f_proc", "  Payment processing at goal", [f"={c}{r['goal']}*{REF['eff_proc']}+{c}{r['backers']}*{REF['eff_fixed']}" for c in "BCD"], USD)
srow("f_drop", "  Failed payments at goal", [f"={c}{r['goal']}*{REF['drop_rate']}" for c in "BCD"], USD)
srow("f_tax", "  Tax & compliance reserve", [f"={c}{r['goal']}*{REF['tax_reserve']}" for c in "BCD"], USD)
srow("f_vat", "  VAT inside pledges (EU consumers)", [f"={c}{r['goal']}*{REF['vat_share']}*{REF['vat_rate']}/(1+{REF['vat_rate']})" for c in "BCD"], USD)
srow("f_ful", "  Reward fulfilment", [f"={c}{r['backers']}*({REF['fulfil']}+{REF['tier_fulfil']})" for c in "BCD"], USD)
srow("net", "NET PROCEEDS at exactly 100% funded", [f"={c}{r['goal']}-{c}{r['f_ks']}-{c}{r['f_proc']}-{c}{r['f_drop']}-{c}{r['f_tax']}-{c}{r['f_vat']}-{c}{r['f_ful']}" for c in "BCD"], USD, BOLD, OUT_FILL)
srow("check", "Net minus budget (must be ≥ 0)", [f"={c}{r['net']}-{c}{r['budget']}" for c in "BCD"], USD)
for p in (15, 22, 30, 40):
    srow(f"b{p}", f"Backers needed if average pledge = €{p}", [f"=ROUNDUP({c}{r['goal']}/{p},0)" for c in "BCD"], NUM)
srow("keys", "Steam keys owed at goal (all tiers)", [f"=ROUNDUP({c}{r['backers']}*{REF['keys_per_backer']},0)" for c in "BCD"], NUM,
     note="Beta access beyond the cap is served with Steam Playtest keys (free, never sold).")
srow("keycap", "Keys within Steam's 2,500 beta-key cap?", [f'=IF({c}{r["keys"]}<={REF["beta_cap"]},"Yes","No — use Playtest keys for the rest")' for c in "BCD"], None)
srow("months", "Development time at team capacity (months)", [f"={c}{r['hours']}/{REF['team_hours']}/4.345" for c in "BCD"], DEC,
     note="Hours ÷ weekly capacity. Add campaign + payout lag (~2 months) for calendar dates.")
srow("delivery", "Promised full-release window (calendar, with buffer)", ["Q4 2027", "Q1 2028 (2nd map Q2 2028)", "Q4 2028"], None, BLUE,
     note="Campaign assumed Apr–May 2027 (after Next Fest Feb 2027). Buffer ≥ 30% on top of the hours estimate.")
srow("unpaid", "Unpaid founder labor value (NOT cash, NOT in goal)", [f"={c}{r['hours']}*{REF['market_rate']}-{c}{r['stipend']}" for c in "BCD"], USD,
     note="Disclosed so nobody mistakes the goal for the game's real cost.")
srow("risk", "Major financial risks", ["Team availability; any cost overrun eats unpaid time", "Hours under-estimated for map 2; translation quality", "Scope creep; AI fill-ins are technically risky"], None, BLUE)
ws.freeze_panes = "B2"
for rr in range(2, ws.max_row + 1):
    ws.row_dimensions[rr].height = 28
    for col in "ABCDE":
        ws[f"{col}{rr}"].alignment = Alignment(wrap_text=True, vertical="center")
SC = rows

# ---------------------------------------------------------------- Sensitivity
ws = wb.create_sheet("Sensitivity")
widths(ws, [40, 14, 14, 14, 14, 14, 14])
put(ws, "A1", "1. Backers needed by goal and average pledge", TITLE)
put(ws, "A2", "Average pledge →", BOLD)
pledges = [15, 20, 25, 30, 35, 40]
for j, p in enumerate(pledges):
    put(ws, f"{get_column_letter(j+2)}2", p, BLUE, USD)
for i, (lab, col) in enumerate([("MVD goal", "B"), ("Realistic goal", "C"), ("Expanded goal", "D")]):
    rr = 3 + i
    put(ws, f"A{rr}", f"=\"{lab} \"&TEXT(Scenarios!{col}{SC['goal']},\"€#,##0\")", GREEN)
    for j in range(len(pledges)):
        c = get_column_letter(j + 2)
        put(ws, f"{c}{rr}", f"=ROUNDUP(Scenarios!${col}${SC['goal']}/{c}$2,0)", BLACK, NUM)

put(ws, "A8", "2. Cost overrun: Realistic budget if hours run over and stipend changes", TITLE)
put(ws, "A9", "Stipend €/h ↓ · hours multiplier →", BOLD)
mults = [0.8, 1.0, 1.25, 1.5]
rates = [8, 10, 12, 15, 20]
for j, m in enumerate(mults):
    put(ws, f"{get_column_letter(j+2)}9", m, BLUE, '0.00"x"')
for i, rt in enumerate(rates):
    rr = 10 + i
    put(ws, f"A{rr}", rt, BLUE, EUR)
    for j in range(len(mults)):
        c = get_column_letter(j + 2)
        put(ws, f"{c}{rr}", f"=(Scenarios!$C${SC['hours']}*{c}$9*$A{rr}*Scenarios!$C${SC['share']}+Scenarios!$C${SC['cash']})*(1+{REF['contingency']})", BLACK, USD)
put(ws, "A15", "Compare with the Realistic goal's net proceeds →", BOLD)
put(ws, "B15", f"=Scenarios!C{SC['net']}", GREEN, USD)

put(ws, "A17", "3. What the team receives if the MVD campaign over-funds", TITLE)
put(ws, "A18", "Funded % →", BOLD)
levels = [1.0, 1.25, 1.5, 2.0, 3.0]
for j, l in enumerate(levels):
    put(ws, f"{get_column_letter(j+2)}18", l, BLUE, "0%")
put(ws, "A19", "Pledged (EUR)")
put(ws, "A20", "Net proceeds (EUR)")
put(ws, "A21", "Surplus over MVD budget (EUR)")
put(ws, "A22", "Covers Realistic budget?")
for j in range(len(levels)):
    c = get_column_letter(j + 2)
    put(ws, f"{c}19", f"=Scenarios!$B${SC['goal']}*{c}18", BLACK, USD)
    put(ws, f"{c}20", f"={c}19*(1-Scenarios!$B${SC['deduct']})", BLACK, USD)
    put(ws, f"{c}21", f"={c}20-Scenarios!$B${SC['budget']}", BLACK, USD)
    put(ws, f"{c}22", f'=IF({c}20>=Scenarios!$C${SC["budget"]},"Yes","No")', BLACK)

put(ws, "A24", "4. Fee drag by average pledge (fixed fee matters for small pledges)", TITLE)
put(ws, "A25", "Average pledge →", BOLD)
for j, p in enumerate(pledges):
    c = get_column_letter(j + 2)
    put(ws, f"{c}25", p, BLUE, USD)
    put(ws, f"{c}26", f"={REF['ks_fee']}+IF({c}25<{REF['micro_threshold']},{REF['micro_pct']}+{REF['micro_fixed']}/{c}25,{REF['proc_pct']}+{REF['proc_fixed']}/{c}25)", BLACK, PCT)
put(ws, "A26", "Kickstarter + processing as % of pledge")

# ---------------------------------------------------------------- Backer Funnel
ws = wb.create_sheet("Backer Funnel")
widths(ws, [52, 16, 16, 16, 60])
ws.append(["Audience needed before launch", "MVD", "Realistic", "Expanded", "Notes"])
style_header(ws, 1, 5)
fr = {}


def frow(key, label, formulas, fmt, note="", bold=False):
    rr = ws.max_row + 1
    fr[key] = rr
    put(ws, f"A{rr}", label, BOLD if bold else BLACK)
    for col, f in zip("BCD", formulas):
        put(ws, f"{col}{rr}", f, BLACK, fmt)
    put(ws, f"E{rr}", note).alignment = Alignment(wrap_text=True)


frow("goal", "Funding goal", [f"=Scenarios!{c}{SC['goal']}" for c in "BCD"], USD, bold=True)
frow("backers", "Backers needed (blended average pledge)", [f"=Scenarios!{c}{SC['backers']}" for c in "BCD"], NUM)
frow("from_f", "Backers expected from followers + email", [f"=ROUNDUP({c}{fr['backers']}*{REF['share_followers']},0)" for c in "BCD"], NUM, "Inputs: share from followers.")
frow("fl", "KS pre-launch followers needed — LOW conversion", [f"=ROUNDUP({c}{fr['from_f']}/{REF['conv_low']},0)" for c in "BCD"], NUM, "If conversion is 10%.")
frow("fm", "KS pre-launch followers needed — MID conversion", [f"=ROUNDUP({c}{fr['from_f']}/{REF['conv_mid']},0)" for c in "BCD"], NUM, "If conversion is 20%.", True)
frow("fh", "KS pre-launch followers needed — HIGH conversion", [f"=ROUNDUP({c}{fr['from_f']}/{REF['conv_high']},0)" for c in "BCD"], NUM, "If conversion is 30%.")
frow("em", "…or email subscribers needed (email conversion)", [f"=ROUNDUP({c}{fr['from_f']}/{REF['email_conv']},0)" for c in "BCD"], NUM, "If the list alone had to deliver it.")
frow("wl", "Steam wishlists implied (proxy only)", [f"=ROUNDUP({c}{fr['backers']}/{REF['wish_conv']},0)" for c in "BCD"], NUM, "Wishlists needed if ALL backers came from wishlisters at the assumed rate — shows why wishlists alone are not enough.")
frow("daily", "Pledges/day needed (30-day campaign, flat)", [f"={c}{fr['backers']}/30" for c in "BCD"], DEC, "Real campaigns front-load: ~30–40% of total in the first 72 h is typical (assumption).")

wb.calculation.fullCalcOnLoad = True  # openpyxl stores no cached values; make Excel/LibreOffice compute on open
wb.save(OUT)
print("saved", OUT)
