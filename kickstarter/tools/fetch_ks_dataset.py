"""Pulls every successful AND failed Kickstarter video-game campaign that ended in the last N days, from Kickstarter's
own Discover listing (category 35 = Video Games), and saves it to research/ks_videogames_<state>.json.

Run from kickstarter/:  python tools/fetch_ks_dataset.py [days=365]
Polite: 1.5 s between pages, backs off on HTTP 429.
"""
import json
import os
import sys
import time
import urllib.request
from datetime import datetime, timedelta, timezone

DAYS = int(sys.argv[1]) if len(sys.argv) > 1 else 365
CUTOFF = (datetime.now(timezone.utc) - timedelta(days=DAYS)).date().isoformat()
UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0 Safari/537.36"
OUT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "research")


def get(url):
    for attempt in range(8):
        req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=30) as r:
                return json.load(r)
        except urllib.error.HTTPError as e:
            if e.code == 429:
                time.sleep(30 + attempt * 15)
                continue
            raise
    raise RuntimeError("too many retries: " + url)


def iso(ts):
    return datetime.fromtimestamp(ts, timezone.utc).date().isoformat()


for state in ("successful", "failed"):
    rows, seen = [], set()
    for page in range(1, 400):
        j = get(f"https://www.kickstarter.com/discover/advanced?format=json&category_id=35&state={state}&sort=end_date&page={page}")
        ps = j.get("projects") or []
        if not ps:
            break
        for p in ps:
            if p["id"] in seen:
                continue
            seen.add(p["id"])
            rows.append({
                "id": p["id"], "name": p["name"], "blurb": p.get("blurb"), "country": p.get("country"),
                "currency": p.get("currency"), "goal": p.get("goal"), "pledged": p.get("pledged"),
                "usd_pledged": round(float(p.get("usd_pledged") or 0)), "usd_rate": p.get("static_usd_rate") or p.get("fx_rate"),
                "backers": p.get("backers_count"), "launched": iso(p["launched_at"]), "deadline": iso(p["deadline"]),
                "days": round((p["deadline"] - p["launched_at"]) / 86400), "staff_pick": p.get("staff_pick"),
                "creator": (p.get("creator") or {}).get("name"), "url": p["urls"]["web"]["project"],
            })
        last = rows[-1]["deadline"]
        print(state, "page", page, "rows", len(rows), "last deadline", last, flush=True)
        if last < CUTOFF:
            break
        time.sleep(1.5)
    rows = [r for r in rows if r["deadline"] >= CUTOFF]
    with open(os.path.join(OUT, f"ks_videogames_{state}.json"), "w", encoding="utf-8") as f:
        json.dump(rows, f, ensure_ascii=False, indent=0)
    print("saved", state, len(rows), flush=True)
