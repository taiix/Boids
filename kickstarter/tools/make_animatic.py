"""Builds the campaign-video ANIMATIC (timing + script check), its subtitle files, and the Discovery Mode animatic.

Run from kickstarter/:  python tools/make_animatic.py
Needs ffmpeg on PATH. Output: assets/video/
  reefrun_campaign_ANIMATIC_1920x1080.mp4   ~2:05, stills + on-screen text + CC0 music, watermarked ANIMATIC
  reefrun_discovery_ANIMATIC_1080x1920.mp4  ~60 s vertical version
  reefrun_campaign_en.vtt / reefrun_discovery_en.vtt   captions (Kickstarter accepts .VTT, help article 16236802)
The animatic is NOT the campaign video: it uses HUD-free photo-mode stills where the final cut needs live gameplay
(07 §4). Its job is to lock timing, text and voice-over length before anyone records footage.
The scene list below is the single source for the storyboard table in 07_CAMPAIGN_VIDEO.md and the .vtt files.
"""
import os
import subprocess
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import make_visuals as mv  # noqa: E402  (shared fonts, palette and helpers; run make_visuals.py first)

KS = mv.KS
OUT = os.path.join(KS, "assets", "video")
FR = os.path.join(OUT, "animatic_frames")
os.makedirs(FR, exist_ok=True)
MUSIC = os.path.join(mv.ROOT, "Assets", "Audio", "Music", "Music_Round.mp3")  # "Nautilus" by poinl, CC0
FPS = 30

# (seconds, still, focus, big on-screen text, small text, voice-over line, capture note for the final cut)
CAMPAIGN = [
    (6, "hide", (0.5, 0.5), "", "", "(whisper) …it's right behind you.",
     "G1: shark POV, HUD on. A fish whispers; ripple appears; lunge. Real voice audio."),
    (5, "KEYART", (0.5, 0.42), "LOGO", "3 FISH · 1 SHARK · IT CAN HEAR YOU", "Three fish. One shark. And it can hear you.",
     "Logo over key art (existing master art)."),
    (9, "hero", (0.5, 0.5), "ONE OF YOU IS THE SHARK", "4 friends · online · Steam",
     "REEF RUN is an online game for four friends. Every round, one of you becomes a great white. The other three are reef fish.",
     "Lobby with 4 avatars → role reveal screen → shark spawning (real UI)."),
    (9, "hide", (0.5, 0.45), "HIDE IN THE SCHOOL", "you shrink and swim with the flock",
     "The fish can disappear into a school of a thousand fish. Swim in and you move with them. The shark has to spot the one acting strange.",
     "G2: fish POV merging into school; shark POV scanning the school."),
    (10, "turtle", (0.5, 0.55), "THE TASKS NEED TALKING", "each fish sees a different clue",
     "To win, the fish have to work together: free a tangled turtle, repeat a code, clean the reef. And that means talking.",
     "G3: two fish on the turtle ropes, voice audible: 'three, two, one, bite!'"),
    (9, "TWIST", (0.5, 0.5), "THE SHARK SEES WHO'S TALKING", "proximity voice · ripples · 2 s lock-on",
     "But voices carry. Anyone who talks near the shark shows up on its screen. So every round, you have to talk… and you have to shut up.",
     "Shark HUD: ripple appears where a fish speaks; 'SHARK HEARD YOU' on the fish's HUD (split screen)."),
    (10, "catch", (0.55, 0.5), "BITE · LUNGE · SONAR", "", "As the shark you've got a bite, a long lunge, and a sonar that catches anything that moves.",
     "Shark POV: sonar ping, lunge, catch; fish mashing to break free."),
    (8, "trawler", (0.5, 0.5), "THE REEF FIGHTS BACK", "8 world events", "And the reef changes mid-round: a trawler net, nightfall, a red tide.",
     "G5: trawler sweep, nightfall plankton trails, red tide haze (1.5 s each)."),
    (8, "shadow", (0.5, 0.5), "THEN — RUN.", "the wall lifts · everyone is revealed",
     "When the clock runs out, a wall lifts, the shark sees everyone, and the fish make a run for it.",
     "G4: wall lifting; three fish sprinting; one caught at the gap."),
    (16, "TEAM", (0.5, 0.5), "WHO WE ARE", "[[names]] · 2 developers · since June 2026",
     "We're [[names]], two developers from [[city]]. We built REEF RUN in sixteen weeks, and everything you've just seen is the game running.",
     "Founders on camera (phone is fine), then 3 s of the editor / git history."),
    (15, "BUILT", (0.5, 0.5), "WHAT YOUR PLEDGE DOES", "polish · performance · controller · Steam release",
     "Now we need to make it ready for everyone: fix the online bugs, make it run on older PCs, add controller support, and release it on Steam.",
     "Built-vs-funded graphic (assets/visuals), then a short performance before/after."),
    (10, "ROAD", (0.5, 0.5), "BETA JULY 2027 · RELEASE Q4 2027", f"€{mv.N['goal']:,.0f} goal",
     "Backers play the beta from July 2027, and the full release is planned for the end of 2027.",
     "Roadmap graphic."),
    (10, "KEYART", (0.5, 0.42), "BACK REEF RUN", "Pod of Four: 4 keys for your group",
     "Grab a Pod of Four for your group, and we'll see you on the reef. Keep your voice down.",
     "End card: logo, 'Back REEF RUN', Pod of Four."),
]

DISCOVERY = [
    (4, "hide", (0.5, 0.5), "IT CAN HEAR YOU", "", "(whisper) …it's right behind you.", "G1 vertical crop"),
    (8, "hero", (0.45, 0.5), "3 FISH · 1 SHARK", "online · 4 friends", "Three of us are fish. One of us is the shark.", "Founder selfie-cam or G1"),
    (10, "hide", (0.5, 0.5), "HIDE IN THE SCHOOL", "", "We hide in a school of a thousand fish…", "G2 vertical"),
    (12, "turtle", (0.45, 0.5), "BUT YOU HAVE TO TALK", "and the shark sees who's talking",
     "…but the tasks only work if we talk, and the shark can see whoever's talking.", "G3 + ripple"),
    (10, "catch", (0.6, 0.5), "BE THE SHARK", "bite · lunge · sonar", "If you're the shark, you hear everything.", "Shark POV"),
    (10, "TEAM", (0.5, 0.5), "2 DEVS · PLAYABLE NOW", "", "We're two developers and it's playable right now. Help us finish it.", "Founders to camera"),
    (8, "KEYART", (0.5, 0.42), "BACK REEF RUN", "link in bio", "Back REEF RUN on Kickstarter. Keep your voice down.", "End card"),
]


def card(kind, w, h):
    """Stand-ins for graphic scenes, built from the visual set."""
    if kind == "KEYART":
        return mv.cover(mv.KEYART, w, h, (0.5, 0.42))
    if kind == "TWIST":
        return mv.cover(os.path.join(mv.OUT_V, "ks_the_twist_explainer_1360x820.png"), w, h)
    if kind == "BUILT":
        return mv.cover(os.path.join(mv.OUT_V, "ks_built_vs_funded_1360x788.png"), w, h)
    if kind == "ROAD":
        return mv.cover(os.path.join(mv.OUT_V, "ks_roadmap_1360x700.png"), w, h)
    if kind == "TEAM":
        return mv.cover(os.path.join(mv.OUT_V, "ks_team_TEMPLATE_1360x640.png"), w, h)
    return None


def scene_png(i, sc, w, h, prefix):
    secs, still, focus, big, small, vo, note = sc
    base = card(still, w, h)
    if base is None:
        base = mv.cover(mv.SHOTS[still], w, h, focus)
    im = base.convert("RGBA")
    if big not in ("", "LOGO") and still not in ("TWIST", "BUILT", "ROAD", "TEAM"):
        im.alpha_composite(mv.vgrad(w, h, mv.DEEP, None, 0, 150))
    d = ImageDraw.Draw(im)
    vertical = h > w
    if big == "LOGO":
        logo = mv.title_logo(int(w * (0.86 if vertical else 0.6)))
        im.alpha_composite(logo, ((w - logo.width) // 2, (h - logo.height) // 2))
    elif big and still not in ("BUILT", "ROAD"):
        f = mv.H(int(w * (0.085 if vertical else 0.05)))
        lines = mv.wrap(d, big, f, w * 0.86)
        y = int(h * (0.70 if vertical else 0.74))
        for k, line in enumerate(lines):
            mv.text_shadow(im, (w // 2, y + k * int(f.size * 1.05)), line, f, anchor="mm")
        if small:
            mv.text_shadow(im, (w // 2, y + len(lines) * int(f.size * 1.05) + 10), small, mv.HB(int(f.size * 0.45)),
                           anchor="mm", fill=mv.AQUA)
    elif small and still not in ("BUILT", "ROAD"):  # graphic scenes already carry their own text
        mv.text_shadow(im, (w // 2, int(h * 0.68)), small, mv.HB(int(w * (0.05 if vertical else 0.03))), anchor="mm")
    # watermark: this is an animatic, not footage
    wm = "ANIMATIC — placeholder still · final uses live gameplay"
    # placed inside the 6% push-in crop so it survives the Ken Burns zoom
    ImageDraw.Draw(im).text((int(w * 0.06), int(h * 0.06)), wm, font=mv.B(int(w * 0.016) if not vertical else 26),
                            fill=(255, 184, 77))
    p = os.path.join(FR, f"{prefix}_{i:02d}.png")
    im.convert("RGB").save(p)
    return p


def vtt_time(t):
    m, s = divmod(t, 60)
    return f"00:{int(m):02d}:{s:06.3f}"


def write_vtt(scenes, path):
    t = 0.0
    out = ["WEBVTT", ""]
    for i, sc in enumerate(scenes, 1):
        secs, vo = sc[0], sc[5]
        if vo:
            out += [str(i), f"{vtt_time(t + 0.3)} --> {vtt_time(t + secs - 0.3)}", vo, ""]
        t += secs
    with open(path, "w", encoding="utf-8") as f:
        f.write("\n".join(out))
    return t


def render(scenes, w, h, prefix, outname):
    pngs = [scene_png(i, sc, w, h, prefix) for i, sc in enumerate(scenes)]
    total = sum(sc[0] for sc in scenes)
    args = ["ffmpeg", "-y", "-loglevel", "error"]
    for p in pngs:
        args += ["-i", p]  # one still per scene: zoompan expands that single frame into d frames
    args += ["-i", MUSIC]
    parts = []
    for k, sc in enumerate(scenes):
        frames = sc[0] * FPS
        # slow push-in (Ken Burns) rendered at 2x then scaled down to avoid zoompan jitter
        parts.append(f"[{k}:v]scale={w*2}:{h*2},zoompan=z='1+0.06*on/{frames}':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)'"
                     f":d={frames}:s={w}x{h}:fps={FPS},setsar=1,format=yuv420p[v{k}]")
    parts.append("".join(f"[v{k}]" for k in range(len(scenes))) + f"concat=n={len(scenes)}:v=1:a=0[v]")
    parts.append(f"[{len(scenes)}:a]atrim=0:{total},afade=t=in:d=1.5,afade=t=out:st={total-3}:d=3,volume=0.8[a]")
    args += ["-filter_complex", ";".join(parts), "-map", "[v]", "-map", "[a]", "-c:v", "libx264", "-preset", "medium",
             "-crf", "22", "-c:a", "aac", "-b:a", "160k", "-shortest", "-movflags", "+faststart", os.path.join(OUT, outname)]
    subprocess.run(args, check=True)
    return total


t1 = render(CAMPAIGN, 1920, 1080, "c", "reefrun_campaign_ANIMATIC_1920x1080.mp4")
write_vtt(CAMPAIGN, os.path.join(OUT, "reefrun_campaign_en.vtt"))
t2 = render(DISCOVERY, 1080, 1920, "d", "reefrun_discovery_ANIMATIC_1080x1920.mp4")
write_vtt(DISCOVERY, os.path.join(OUT, "reefrun_discovery_en.vtt"))
print(f"campaign animatic {t1}s, discovery animatic {t2}s")
