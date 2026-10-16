"""REEF RUN title, round 3: structurally different lettering (not recolours). All drawn from the game's own
fonts (Barlow / Barlow Condensed, SIL OFL) and colours; transparent RGBA output.

    title(height, style) -> RGBA

    "G"  Cinematic   - upright Barlow SemiBold, wide tracking, hairline rules and a small fin: a film-poster title
    "H"  Surface     - the extruded title cut by the waterline: crisp above, refracted and rippling below
    "I"  Bitten      - the extruded title with a shark bite (serrated arc of teeth) torn out of RUN
    "J"  Coral       - letter faces carved from the game's own coral texture, stone-dark extrusion
    "K"  Plankton    - hollow neon outlines glowing like the game's bioluminescent plankton
    "L"  Sticker     - upright, stacked, chunky white die-cut border and a dropped 3D edge, slightly rotated
    "HI" Chosen      - I + H: the bite torn out of RUN, and the waterline through both words (the final title)
"""
import math
import os
import random

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

import reefrun_logo as L
import reefrun_logo3d as T

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SHOTS = os.path.join(os.path.dirname(ROOT), "Screenshots")
GAME_FONTS = os.path.join(os.path.dirname(os.path.dirname(ROOT)), "Assets", "UI Toolkit", "Fonts")
CAP = T.CAP


def _font(name, size):
    p = os.path.join(HERE, "fonts", name)
    if not os.path.exists(p):
        p = os.path.join(GAME_FONTS, name)
    return ImageFont.truetype(p, size)


def _finish(lock, height, shadow=(2, 10, 20), alpha=0.6, blur=0.14, drop=0.10):
    pad = int(CAP * 0.5)
    canvas = Image.new("RGBA", (lock.width + 2 * pad, lock.height + 2 * pad), (0, 0, 0, 0))
    a = Image.new("L", canvas.size, 0)
    a.paste(lock.getchannel("A").point(lambda v: int(v * alpha)), (pad, pad + int(CAP * drop)))
    a = a.filter(ImageFilter.GaussianBlur(CAP * blur)).point(lambda v: 0 if v < 3 else v)
    sh = Image.new("RGBA", canvas.size, shadow + (0,))
    sh.putalpha(a)
    canvas.alpha_composite(sh)
    canvas.alpha_composite(lock, (pad, pad))
    canvas = canvas.crop(canvas.getchannel("A").getbbox())
    s = height / canvas.height
    return canvas.resize((max(1, round(canvas.width * s)), round(height)), Image.LANCZOS)


def _text_mask(text, font, tracking=0):
    """Mask of a line of text with extra letter spacing (px)."""
    widths = [font.getbbox(c)[2] - font.getbbox(c)[0] for c in text]
    asc, desc = font.getmetrics()
    w = sum(font.getlength(c) for c in text) + tracking * (len(text) - 1) + CAP
    m = Image.new("L", (int(w), asc + desc + CAP // 4), 0)
    d = ImageDraw.Draw(m)
    x = CAP // 2
    for c in text:
        d.text((x, CAP // 8), c, font=font, fill=255)
        x += font.getlength(c) + tracking
    return m.crop(m.getbbox())


# ------------------------------------------------------------------ G cinematic

def _cinematic():
    f = _font("Barlow-SemiBold.ttf", int(CAP * 1.05))
    word = _text_mask("REEF RUN", f, tracking=int(CAP * 0.22))
    W, H = word.width + int(CAP * 0.4), word.height + int(CAP * 0.9)
    lock = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ox, oy = (W - word.width) // 2, int(CAP * 0.45)
    # Faces: white with a faint cool gradient over a shallow navy-teal edge, for a touch of depth.
    m = Image.new("L", (W, H), 0)
    m.paste(word, (ox, oy))
    grad = L._gradient((W, H), (255, 255, 255), (206, 228, 232))
    depth = int(CAP * 0.035)
    for k in range(depth, 0, -1):
        lock.paste(Image.new("RGBA", (W, H), (20, 70, 92, 255)), (0, 0), T._shift(m, k // 2, k))
    lock.paste(grad, (0, 0), m)
    # RUN in teal: recolour the right part (after the double space).
    split = ox + word.width * 0.53
    teal = Image.new("L", (W, H), 0)
    teal.paste(m.crop((int(split), 0, W, H)), (int(split), 0))
    lock.paste(L._gradient((W, H), (110, 236, 232), (32, 170, 172)), (0, 0), teal)
    # Hairline rules above and below, broken in the middle by a small fin on the lower one.
    d = ImageDraw.Draw(lock)
    t = max(2, int(CAP * 0.03))
    y1, y2 = oy - int(CAP * 0.22), oy + word.height + int(CAP * 0.22)
    d.rectangle((ox, y1, ox + word.width, y1 + t), fill=(238, 246, 247, 230))
    cx = ox + int(word.width * 0.505)   # under the space between the words
    fw, fh = int(CAP * 0.34), int(CAP * 0.19)
    d.rectangle((ox, y2, cx - fw // 2, y2 + t), fill=(44, 195, 196, 255))
    d.rectangle((cx + fw // 2, y2, ox + word.width, y2 + t), fill=(44, 195, 196, 255))
    pts = L._bezier((cx + fw // 2, y2 + t), (cx + fw * 0.25, y2 - fh * 0.7), (cx - fw * 0.05, y2 - fh))         + L._bezier((cx - fw * 0.05, y2 - fh), (cx - fw * 0.05, y2 - fh * 0.3), (cx - fw // 2, y2 + t))
    d.polygon(pts, fill=(44, 195, 196, 255))
    return lock.crop(lock.getchannel("A").getbbox())


# ------------------------------------------------------------------ H surface split

def _surface(base):
    """The waterline runs through the letters: above it they're crisp; below it they ripple and turn teal."""
    w, h = base.size
    line = int(h * 0.50)
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    out.paste(base.crop((0, 0, w, line)), (0, 0))
    below = base.crop((0, line, w, h))
    rip = Image.new("RGBA", below.size, (0, 0, 0, 0))
    amp, period = w * 0.0035, h * 0.16
    for y in range(below.height):
        dx = int(round(amp * math.sin(2 * math.pi * y / period) * (0.4 + 0.6 * min(1, y / (h * 0.15)))))
        rip.paste(below.crop((0, y, w, y + 1)), (dx, y))
    tint = Image.new("RGBA", rip.size, (40, 200, 205, 0))
    tint.putalpha(rip.getchannel("A").point(lambda v: int(v * 0.16)))   # light: the letters must still read small
    rip.alpha_composite(tint)
    out.paste(rip, (0, line))
    # The surface itself: a bright wavering line across the letters, only where there is ink.
    d = ImageDraw.Draw(out)
    t = max(2, int(h * 0.012))
    pts = [(x, line + amp * 0.8 * math.sin(2 * math.pi * x / (w * 0.09))) for x in range(0, w, 4)]
    surf = Image.new("L", (w, h), 0)
    ImageDraw.Draw(surf).line(pts, fill=255, width=t)
    surf = ImageChops.multiply(surf, base.getchannel("A").point(lambda v: 255 if v > 128 else 0).filter(ImageFilter.MaxFilter(5)))
    out.paste(Image.new("RGBA", (w, h), (235, 255, 255, 255)), (0, 0), surf)
    return out


# ------------------------------------------------------------------ I bitten

def _bite_poly(cx, cy, r, teeth=18, depth=0.17):
    pts = []
    n = teeth * 2
    for i in range(n):
        a = 2 * math.pi * i / n
        rr = r * (1 - depth if i % 2 else 1)
        pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    return pts


def _bitten(words, run_box):
    """Tear a bite out of the top-right of RUN, edged with the navy keyline."""
    x0, y0, x1, y1 = run_box
    r = (y1 - y0) * 0.58
    cx, cy = x1 + r * 0.42, y0 + r * 0.05
    w, h = words.size
    big = Image.new("L", (w, h), 0)
    ImageDraw.Draw(big).polygon(_bite_poly(cx, cy, r + CAP * 0.06), fill=255)
    small = Image.new("L", (w, h), 0)
    ImageDraw.Draw(small).polygon(_bite_poly(cx, cy, r), fill=255)
    ink = words.getchannel("A")
    rim = ImageChops.multiply(ImageChops.subtract(big, small), ink)
    out = words.copy()
    out.paste(Image.new("RGBA", (w, h), L.NAVY + (255,)), (0, 0), rim)
    a = ImageChops.subtract(out.getchannel("A"), small)
    out.putalpha(a)
    return out


# ------------------------------------------------------------------ J coral

_CORAL = None


def _coral_texture(size):
    """The green-teal brain coral from screenshot 01 (in-game asset), high-passed into a stony relief."""
    global _CORAL
    if _CORAL is None:
        src = Image.open(os.path.join(SHOTS, "01_hero_shark_charge.png")).convert("RGB")
        _CORAL = src.crop((390, 850, 740, 1040))
    return _CORAL.resize(size, Image.BICUBIC)


# ------------------------------------------------------------------ K plankton

def _plankton():
    f = _font("BarlowCondensed-ExtraBoldItalic.ttf", int(CAP * 1.38))
    l, t, r, b = f.getbbox("REEF RUN")
    pad = int(CAP * 0.5)
    W, H = r - l + 2 * pad, b - t + 2 * pad
    sw = int(CAP * 0.07)
    outer = Image.new("L", (W, H), 0)
    ImageDraw.Draw(outer).text((pad - l, pad - t), "REEF RUN", font=f, fill=255, stroke_width=sw, stroke_fill=255)
    inner = Image.new("L", (W, H), 0)
    ImageDraw.Draw(inner).text((pad - l, pad - t), "REEF RUN", font=f, fill=255)
    inner = inner.filter(ImageFilter.MinFilter(2 * (sw // 3) + 1))
    ring = ImageChops.subtract(outer, inner)
    fill = inner.point(lambda v: int(v * 0.16))
    lock = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    glow = ring.filter(ImageFilter.GaussianBlur(CAP * 0.10)).point(lambda v: min(255, int(v * 1.6)))
    g = Image.new("RGBA", (W, H), (60, 240, 235, 0)); g.putalpha(glow); lock.alpha_composite(g)
    f_ = Image.new("RGBA", (W, H), (60, 220, 220, 0)); f_.putalpha(fill); lock.alpha_composite(f_)
    core = Image.new("RGBA", (W, H), (210, 255, 252, 0)); core.putalpha(ring); lock.alpha_composite(core)
    # Specks drifting off the letters, like the plankton a moving body stirs up at night.
    rnd = random.Random(7)
    d = ImageDraw.Draw(lock)
    for _ in range(140):
        x, y = rnd.uniform(pad * 0.6, W - pad * 0.6), rnd.uniform(pad * 0.6, H - pad * 0.4)
        s = rnd.uniform(CAP * 0.008, CAP * 0.025)
        d.ellipse((x - s, y - s, x + s, y + s), fill=(170, 255, 250, rnd.randint(90, 220)))
    return lock


# ------------------------------------------------------------------ L sticker

def _sticker():
    f = _font("BarlowCondensed-ExtraBold.ttf", int(CAP * 1.45))
    reef, run = T._mask("REEF", f), T._mask("RUN", f)
    gap = int(CAP * 0.08)
    W = max(reef.width, run.width) + CAP
    H = reef.height + run.height + gap + CAP
    m_reef, m_run = Image.new("L", (W, H), 0), Image.new("L", (W, H), 0)
    m_reef.paste(reef, ((W - reef.width) // 2, CAP // 2))
    m_run.paste(run, ((W - run.width) // 2, CAP // 2 + reef.height + gap))
    words = ImageChops.lighter(m_reef, m_run)
    border = T._dilate(words, int(CAP * 0.14)).filter(ImageFilter.GaussianBlur(CAP * 0.03)).point(lambda v: 255 if v > 128 else 0)
    outline = T._dilate(border, int(CAP * 0.035))
    lock = Image.new("RGBA", (W, H + int(CAP * 0.12)), (0, 0, 0, 0))
    for k in range(int(CAP * 0.09), 0, -1):          # dropped edge of the die-cut sticker
        lock.paste(Image.new("RGBA", (W, H), (12, 60, 84, 255)), (0, k), outline)
    lock.paste(Image.new("RGBA", (W, H), L.NAVY + (255,)), (0, 0), outline)
    lock.paste(L._gradient((W, H), (255, 255, 255), (214, 232, 236)), (0, 0), border)
    lock.paste(L._gradient((W, H), (16, 52, 76), (5, 23, 41)), (0, 0), m_reef)
    lock.paste(L._gradient((W, H), (50, 210, 208), (22, 140, 146)), (0, 0), m_run)
    lock = lock.crop(lock.getchannel("A").getbbox())
    return lock.rotate(4, resample=Image.BICUBIC, expand=True)


# ------------------------------------------------------------------ entry

def _extruded_words_with_boxes(pal=None, caustic=False, face_tex=None):
    """REEF RUN extruded like style A; returns (lock with fin, box of RUN within it)."""
    font = ImageFont.truetype(L.FONT, int(CAP * 1.38))
    depth, direction = int(CAP * 0.11), (1, 1)
    reef = T._word("REEF", font, depth, direction, caustic=caustic, pal=pal)
    run = T._word("RUN", font, depth, direction, caustic=caustic, pal=pal)
    gap = int(CAP * 0.10)
    words = Image.new("RGBA", (reef.width + gap + run.width, max(reef.height, run.height)), (0, 0, 0, 0))
    words.alpha_composite(reef, (0, 0))
    words.alpha_composite(run, (reef.width + gap, 0))
    return words, (reef.width + gap, 0, reef.width + gap + run.width, run.height)


def _with_fin(words, pal=None):
    fin = T._fin(int(words.width * 0.92), CAP, pal)
    lock = Image.new("RGBA", (words.width, words.height + fin.height + int(CAP * 0.02)), (0, 0, 0, 0))
    lock.alpha_composite(fin, ((words.width - fin.width) // 2, words.height + int(CAP * 0.02)))
    lock.alpha_composite(words, (0, 0))
    return lock


def title(height, style):
    if style == "G":
        return _finish(_cinematic(), height, alpha=0.7, blur=0.18, drop=0.06)
    if style == "H":
        words, _ = _extruded_words_with_boxes()
        return _finish(_with_fin(_surface(words)), height)
    if style == "I":
        words, box = _extruded_words_with_boxes()
        return _finish(_with_fin(_bitten(words, box)), height)
    if style == "HI":   # the chosen title: bitten, then cut by the waterline
        words, box = _extruded_words_with_boxes()
        return _finish(_with_fin(_surface(_bitten(words, box))), height)
    if style == "J":
        coral = dict(T.PALETTES["game"])
        coral.update(face={"REEF": ((236, 226, 200), (170, 150, 118)), "RUN": ((120, 214, 196), (40, 130, 120))},
                     side={"REEF": ((96, 80, 62), (40, 30, 22)), "RUN": ((24, 88, 84), (8, 36, 36))},
                     key=(18, 26, 30), hi=(255, 250, 230), lo=(30, 20, 10))
        font = ImageFont.truetype(L.FONT, int(CAP * 1.38))
        parts = []
        for word in ("REEF", "RUN"):
            w = T._word(word, font, int(CAP * 0.14), (1, 1), pal=coral)
            tex = _coral_texture(w.size).convert("L")
            hp = ImageChops.subtract(tex, tex.filter(ImageFilter.GaussianBlur(6))).point(lambda v: min(255, v * 5))
            lo = ImageChops.subtract(tex.filter(ImageFilter.GaussianBlur(6)), tex).point(lambda v: min(255, v * 4))
            ink = w.getchannel("A").point(lambda v: 255 if v > 200 else 0)
            w.paste(Image.new("RGB", w.size, (255, 255, 240)), (0, 0), ImageChops.multiply(hp, ink).point(lambda v: v // 2))
            w.paste(Image.new("RGB", w.size, (20, 12, 6)), (0, 0), ImageChops.multiply(lo, ink).point(lambda v: v // 2))
            parts.append(w)
        gap = int(CAP * 0.10)
        words = Image.new("RGBA", (parts[0].width + gap + parts[1].width, max(p.height for p in parts)), (0, 0, 0, 0))
        words.alpha_composite(parts[0], (0, 0)); words.alpha_composite(parts[1], (parts[0].width + gap, 0))
        return _finish(_with_fin(words, coral), height)
    if style == "K":
        return _finish(_plankton(), height, shadow=(2, 10, 20), alpha=0.35, blur=0.25, drop=0.0)
    if style == "L":
        return _finish(_sticker(), height, alpha=0.55)
    return T.logo3d(height, style)
