"""Generates KICKSTARTER_COPY_SHEET.html (v2): every Kickstarter editor field with a Copy button.
Content mirrors 15_CAMPAIGN_COPY_V2.md and 05_REWARD_TIERS.md (v2). Run from kickstarter/: python tools/make_copy_sheet.py
Text inside [[double brackets]] is rendered highlighted: those are facts only the team can supply.
"""
import html
import re

GOAL = "14000"
F = []  # (section_id, section_title, [ (label, meta, value, note_or_None, maxlen_or_None) ])


def sec(sid, title, fields, intro=None):
    F.append((sid, title, intro, fields))


VAT = "[[VAT line, per your accountant: Kickstarter recommends \"Prices exclude VAT; it's added in the Pledge Manager\"]]"
KEYS = "Steam keys are delivered through Kickstarter's Pledge Manager. Reef Run is PC (Windows) on Steam. " + VAT

sec("basics", "1 · Basics", [
    ("Project title", None, "Reef Run — 3 fish, 1 shark, and it can hear you", None, 60),
    ("Subtitle", None, "A 4-player online party game: hide in the school, finish the tasks, and keep your voice down — the shark hears proximity chat.", None, 135),
    ("Category", None, "Games › Video Games", None, None),
    ("Location", None, "[[your city]], Netherlands", None, None),
    ("Funding goal (EUR)", None, GOAL, "Default v2 goal. Lean alternative €6,000 (all founder hours unpaid) — team decision, 03 §1.", None),
    ("Campaign duration (days)", None, "30", "Fixed 30 days — Steam's key rules allow 30 at most.", None),
], "Project image: kickstarter/assets/visuals/ks_project_image_1920x1080.png · Project video: the real gameplay trailer only (never the animatic).")

T = [
    ("Ripple", "€5 · no limit · Jun 2027", "Thank you! Your name goes into Reef Run's in-game Supporters credits, you get the Backer role in our Discord, and a 4K wallpaper pack of the reef. No copy of the game is included."),
    ("Early Diver", "€12 · limit 300 · Jul 2027", "1 Steam key for Reef Run: backer beta (planned July 2027), then the full game at release. Your name in the Supporters credits. Limited to the first 300 backers.\n\n" + KEYS),
    ("Diver", "€15 · no limit · Jul 2027", "1 Steam key for Reef Run: backer beta (planned July 2027), then the full game at release. Your name in the Supporters credits.\n\n" + KEYS),
    ("Founding Diver", "€29 · no limit · key Jul 2027, rest Dec 2027", "1 Steam key (beta + full game), the Founder cosmetic set (a \"Founder Fin\" fish pattern and an \"Old Scar\" shark skin, exclusive to Kickstarter backers), your name in the Founders credits, and the digital \"Making of Reef Run\" PDF.\n\n" + KEYS),
    ("Pod of Four", "€49 · no limit · Jul 2027", "Reef Run is a 4-player game. This is the one for your group: 4 Steam keys (beta + full game) to share, and 4 names in the Supporters credits.\n\n" + KEYS),
    ("Founding Pod", "€99 · no limit · keys Jul 2027, rest Dec 2027", "Everything in Founding Diver, x4: 4 Steam keys, 4 Founder cosmetic sets, 4 Founders credits, and the Making-of PDF.\n\n" + KEYS),
    ("Name the Trawler", "€199 · limit 25 · Dec 2027", "Everything in Founding Diver, plus your name (or your group's) painted on the hull of the trawler that drags its net across the reef. One backer name shows per round, rotating. Names are checked by us and must suit a PG-13 game. Limited to 25.\n\n" + KEYS),
]
rew = []
for name, meta, desc in T:
    rew.append((f"{name} · title", meta, name, None, None))
    rew.append((f"{name} · description", None, desc, None, None))
sec("rewards", "2 · Rewards (v2 prices)", rew, "Reward images (3:2, no overlaid text): kickstarter/assets/visuals/rewards_3x2/reward_*.jpg · Shipping: Digital reward (no shipping).")

STORY = [
    ("3.1 · Hero (under GIF-1: whisper → ripple → lunge)", "Three of you are fish. One of you is the shark.\nAnd the shark can hear you."),
    ("3.2 · What is Reef Run?", "Reef Run is an online party game for four friends.\nOne player is a great white. Three are reef fish who have to survive ten minutes, finish co-op tasks and escape.\nYou talk on proximity voice, and the tasks need talking. But every word near the shark shows up on its screen.\nSo you whisper. You hide inside a school of a thousand fish. And somebody always laughs at the wrong moment."),
    ("3.3 · GIF captions (one under each GIF)", "Disappear into a school of a thousand fish.\nTalk to win: each fish sees a different clue.\nPlay the shark: sonar, lunge, and very good ears.\nThe reef fights back: 9 mid-round events.\nThen — run."),
    ("3.4 · Key features", "🦈 One shark, three fish: a 10-minute round for exactly four friends, online on Steam.\n🎙️ Proximity voice that can kill you: the shark sees ripples wherever someone talks.\n🐟 Hide in a living school: a real flocking simulation with hundreds to thousands of fish.\n🐢 Co-op tasks built around talking: free a tangled turtle, relay a code, clean the reef.\n🌊 9 world events: a trawler net, nightfall, a red tide, a jellyfish swarm and more, plus orcas that hunt the shark.\n👻 Dead isn't quiet: spectate, chat on the ghost channel, and flash the shark's position once."),
    ("3.5 · Why we're making it", "[[Two or three sentences in your own words: who you are, why sharks, and the game night that started it.]]\n\nSince June 2026 we've built Reef Run in our spare time, two developers and [[N]] commits, and everything on this page is captured from the game. [[If true: \"N groups played our public playtest; X% said they'd buy it.\"]]\n\nUnacite Studio · [[city]], the Netherlands · [[name, role]] · [[name, role]]"),
    ("3.6 · Stretch goals (reveal one at a time)", "€17,500: +1 co-op task, and an extra Kickstarter cosmetic for every backer\n€21,000: backers vote on a 10th world event, plus a Steam Deck pass\n€28,000: 6 languages (DE, FR, ES, PT-BR, PL, NL)\n€35,000: an original soundtrack by a commissioned composer\n€55,500: a second map (kelp forest / shipwreck), 2 more tasks and 4 months of free updates"),
    ("3.7 · Where the money goes", "Programming & netcode: €2,900 (21%)\nGame design, balance & onboarding: €800 (6%)\nPlaytests, community & campaign: €1,400 (10%)\nBacker rewards in game: €400 (3%)\nMarketing (pre-launch + live ads): €2,250 (16%)\nSteam fee, KvK, accountant, test hardware & software: €1,432 (10%)\nContingency: €1,377 (10%)\nKickstarter & payment fees, failed payments, VAT, tax reserve: €3,441 (25%)\n\nHalf of our remaining 1,100 hours stays unpaid. That's our share of the bet, worth about €27,500 at market rates."),
    ("3.8 · Roadmap", "Done (Jun–Oct 2026): online round, voice, schools, tasks, 9 events\nFeb 2027: Steam Next Fest demo\nApr–May 2027: this campaign\nJuly 2027: backer beta\nQ4 2027: release on Steam\n2028: second map, if unlocked\n\nThere's a 30% buffer in the schedule. If a date slips anyway, you hear it from us first."),
]
sec("story", "3 · Story (v2 order: hero → what → GIFs → features → why → rewards → stretch → money → roadmap)",
    [(l, None, v, None, None) for l, v in STORY],
    "Images: ks_funding_allocation_1360x780.png under 3.7, ks_roadmap_1360x700.png under 3.8, reward cards under the rewards. GIF-1…GIF-6 come from the capture session (16 §5).")

sec("risks", "4 · Risks and challenges", [("Risks", None,
    "We're two people. If one of us is out, things slow down. That's why the goal only covers what already works plus polish.\n\n"
    "Online games break in the wild. Backers get a beta months before release, and bug fixing is the biggest budget line.\n\n"
    "It needs four players. Public lobbies and our Discord help you find a group. Bots aren't promised.\n\n"
    "Dates can move. There's a 30% buffer in the schedule. If a date slips anyway, you hear it from us first, with the reason.", None, None)])

sec("ai", "5 · Use of AI (must be accurate)", [
    ("Other AI use (coding)", None, "We used an AI coding assistant to help write and debug parts of the game's code and tooling. Every system was designed, reviewed, tested and integrated by us. [[Confirm and adjust the scope honestly.]] No AI-generated art, models, music or sound is in the game, and the fish schools use a classic flocking algorithm (boids), not machine learning.", None, None),
    ("AI-generated content (only if you keep AI-drafted page text)", None, "First drafts of this page's text were written with an AI assistant and edited by us. [[Delete if you rewrite the page yourselves — recommended, see 14 §4.]]", None, None),
])

FAQ = [("Platforms?", "PC (Windows) on Steam."), ("Exactly 4 players?", "Yes, 1 shark and 3 fish. Use friends, public lobbies or our Discord."),
       ("Mic required?", "Strongly recommended. Push-to-talk and open mic are both supported (planned)."), ("Shark every round?", "No, the shark is picked at random."),
       ("Is there a demo?", "[[Yes, link / at Steam Next Fest Feb 2027]]"), ("When do I play?", "Beta in July 2027, full release in Q4 2027."),
       ("Price on Steam?", "Planned €12.99. Our best backer price will be matched by a launch discount."), ("How do I get my key?", "Through Kickstarter's Pledge Manager after the campaign."),
       ("Late pledges?", "Not until the game is on sale. Steam's rules don't allow selling keys before release."), ("Taxes?", VAT)]
sec("faq", "6 · FAQ (top 10)", [(f"Q: {q}", None, a, None, None) for q, a in FAQ])


def esc(t):
    return re.sub(r"\[\[(.*?)\]\]", lambda m: "<mark>[[" + m.group(1) + "]]</mark>", html.escape(t, quote=False))


src = open("KICKSTARTER_COPY_SHEET.html", encoding="utf-8").read()
head = src[:src.index("<body>")].replace("REEF RUN Copy Sheet", "Reef Run Copy Sheet")
script = src[src.index("<script>"):]
out = [head, "<body>\n<main>",
       "<h1>Reef Run: Kickstarter copy sheet (v2)</h1>",
       f"<p class=\"meta\">Individual Dutch project · EUR · goal €{int(GOAL):,} · v2 built 2026-10-20 from docs 05, 14, 15 and 16. Re-check numbers in <code>03_funding_model.xlsx</code> on the day you submit.</p>",
       "<div class=\"warn\"><strong>Draft only.</strong> Save the draft. <strong>Don't click \"Submit for review\" or \"Launch\".</strong> Highlighted <mark>[[…]]</mark> parts are facts only you can supply. Steam-key tiers need Reef Run's own Steam app first (12 B2).</div>",
       "<nav>" + "".join(f"<a href=\"#{sid}\">{html.escape(t.split('·')[-1].split('(')[0].strip())}</a>" for sid, t, _, _ in F) + "</nav>"]
for sid, title, intro, fields in F:
    out.append(f"<h2 id=\"{sid}\">{html.escape(title)}</h2>")
    if intro:
        out.append(f"<p class=\"img\">{esc(intro)}</p>")
    for label, meta, value, note, mx in fields:
        dm = f" data-max=\"{mx}\"" if mx else ""
        m = f"<span class=\"meta\">{html.escape(meta)}</span>" if meta else ("<span class=\"meta\"></span>" if mx else "")
        out.append(f"<div class=\"field\"{dm}><div class=\"head\"><span class=\"label\">{html.escape(label)}</span>{m}</div><pre class=\"val\">{esc(value)}</pre></div>")
        if note:
            out.append(f"<p class=\"note\">{esc(note)}</p>")
out.append("<h2 id=\"settings\">7 · Settings checklist</h2><ul class=\"check\"><li>Netherlands (individual) · EUR · goal €" + f"{int(GOAL):,}" + " · fixed 30 days</li><li>Late Pledges off · Pledge Over Time off · Pledge Manager on</li><li>Add-ons none · Shipping: digital</li><li>Video: real gameplay trailer only</li><li><strong>Save the draft. Don't submit, don't launch.</strong></li></ul>")
out.append("</main>\n" + script)
open("KICKSTARTER_COPY_SHEET.html", "w", encoding="utf-8").write("\n".join(out))
print("fields:", sum(len(f[3]) for f in F))
