"""REEF RUN title, dimensional versions (for a centred title). Same font and colours as reefrun_logo.py
(Barlow Condensed ExtraBold Italic; --text #eef6f7, --teal-l #2cc3c4, --navy #051729), plus depth:

    logo3d(height, style) -> RGBA PIL image on transparency

Styles (the title options shown to the art director):
    "A"  Extruded   - wide, solid block extrusion down-right, bevelled face, fin waterline below
    "B"  Wet glass  - wide, shallow extrusion, the game's own sand caustics shimmering across the letter faces
    "C"  Stacked    - REEF over RUN, deep extrusion, fin waterline below (for tall and small formats)
    "D"  Low angle  - wide, tilted back in perspective as if seen from below, extrusion falling toward the reef
    "E"  Backlit    - wide, dark letters silhouetted against the sun, top edges and outline caught by its light
    "F"  Sunlit     - wide, extruded; REEF in the menu's warm accent orange, RUN in teal
"""
import os
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

import reefrun_logo as L

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SS = 3
CAP = 200 * SS

FACE = {"REEF": ((255, 255, 255), (196, 208, 212)), "RUN": ((92, 232, 230), (28, 160, 164))}
SIDE = {"REEF": ((128, 152, 166), (40, 62, 80)), "RUN": ((18, 120, 126), (6, 56, 66))}

# Colour schemes. "game" is the main menu's (white REEF, teal RUN, navy keyline).
PALETTES = {
    "game": dict(face=FACE, side=SIDE, key=L.NAVY, hi=(255, 255, 255), lo=(4, 30, 44), fin=L.TEAL,
                 shadow=(2, 10, 20), shadow_alpha=0.6),
    # Backlit: the letters are dark shapes against the sun, their top edges caught by it, a glow around them.
    "backlit": dict(face={"REEF": ((22, 58, 84), (6, 24, 42)), "RUN": ((14, 92, 100), (4, 40, 52))},
                    side={"REEF": ((10, 34, 52), (3, 12, 22)), "RUN": ((6, 52, 60), (2, 20, 28))},
                    key=(200, 250, 250), hi=(170, 250, 248), lo=(0, 0, 0), fin=(14, 92, 100),
                    shadow=(150, 240, 240), shadow_alpha=0.85, key_w=0.085),
    # Sunlit: REEF in the menu's warm accent (--orange #ffb84d), lit by the sun; RUN stays teal.
    "sunlit": dict(face={"REEF": ((255, 226, 150), (247, 160, 52)), "RUN": FACE["RUN"]},
                   side={"REEF": ((186, 96, 30), (92, 40, 10)), "RUN": SIDE["RUN"]},
                   key=L.NAVY, hi=(255, 255, 235), lo=(60, 20, 0), fin=L.TEAL,
                   shadow=(2, 10, 20), shadow_alpha=0.6),
}


def _mask(text, font):
    l, t, r, b = font.getbbox(text)
    m = Image.new("L", (r - l, b - t), 0)
    ImageDraw.Draw(m).text((-l, -t), text, font=font, fill=255)
    return m


def _shift(m, dx, dy):
    out = Image.new("L", m.size, 0)
    out.paste(m, (dx, dy))
    return out


def _grad(size, top, bottom):
    return L._gradient(size, top, bottom)


def _dilate(m, r):
    return m.filter(ImageFilter.GaussianBlur(r * 0.55)).point(lambda v: 255 if v > 10 else v * 25)


_CAUSTIC = None


def _caustics(size):
    """Bright caustic network lifted from the game's own sand (screenshot 04), as a 0..255 highlight mask."""
    global _CAUSTIC
    if _CAUSTIC is None:
        src = Image.open(os.path.join(os.path.dirname(ROOT), "Screenshots", "04_task_turtle_rescue.png")).convert("L")
        crop = src.crop((300, 760, 1700, 1080))
        hp = ImageChops.subtract(crop, crop.filter(ImageFilter.GaussianBlur(14)))
        _CAUSTIC = hp.point(lambda v: min(255, v * 7))
    return _CAUSTIC.resize(size, Image.BICUBIC)


def _word(text, font, depth, direction, bevel=True, caustic=False, pal=None):
    """One word, extruded. Returns an RGBA image (face + sides + navy keyline) with room for the extrusion."""
    pal = pal or PALETTES["game"]
    m = _mask(text, font)
    dx, dy = direction
    steps = max(1, depth)
    pad = int(CAP * 0.10)
    W = m.width + 2 * pad + abs(dx) * steps
    H = m.height + 2 * pad + abs(dy) * steps
    ox, oy = pad, pad
    face_m = Image.new("L", (W, H), 0)
    face_m.paste(m, (ox, oy))

    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    silhouette = face_m.copy()
    side_top, side_bot = pal["side"][text]
    for k in range(steps, 0, -1):  # back to front
        t = k / steps
        col = tuple(int(side_top[i] + (side_bot[i] - side_top[i]) * t) for i in range(3))
        sm = _shift(face_m, dx * k, dy * k)
        layer = Image.new("RGBA", (W, H), col + (255,))
        out.paste(layer, (0, 0), sm)
        silhouette = ImageChops.lighter(silhouette, sm)
    # Edge lines between extrusion steps read as ridges: a slight darkening along the far silhouette.
    top, bot = pal["face"][text]
    face = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    face.paste(_grad((W, H), top, bot).crop((0, 0, W, H)), (0, 0), face_m)
    if caustic:
        c = _caustics((W, H))
        c = ImageChops.multiply(c, face_m).point(lambda v: int(v * 0.55))
        face.paste(Image.new("RGB", (W, H), (255, 255, 255)), (0, 0), c)
    if bevel:
        t = max(2, int(CAP * 0.035))
        hi = ImageChops.subtract(face_m, _shift(face_m, 0, t)).filter(ImageFilter.GaussianBlur(t * 0.4))
        lo = ImageChops.subtract(face_m, _shift(face_m, 0, -t)).filter(ImageFilter.GaussianBlur(t * 0.4))
        face.paste(Image.new("RGB", (W, H), pal["hi"]), (0, 0), ImageChops.multiply(hi, face_m).point(lambda v: int(v * 0.85)))
        face.paste(Image.new("RGB", (W, H), pal["lo"]), (0, 0), ImageChops.multiply(lo, face_m).point(lambda v: int(v * 0.35)))
    out.alpha_composite(face)

    # Navy keyline around the whole solid (face + extrusion), as in the flat logo.
    key = _dilate(silhouette, int(CAP * pal.get("key_w", 0.06)))
    framed = Image.new("RGBA", (W, H), pal["key"] + (0,))
    framed.putalpha(key)
    framed.alpha_composite(out)
    return framed.crop(framed.getchannel("A").getbbox())


def _fin(width, cap, pal=None):
    pal = pal or PALETTES["game"]
    fin = L._fin_mark(width, cap, pal["fin"])
    stroke = int(cap * 0.06)
    a = fin.getchannel("A")
    side = Image.new("RGBA", fin.size, (0, 0, 0, 0))
    d = int(cap * 0.05)
    big = Image.new("RGBA", (fin.width + d, fin.height + d), (0, 0, 0, 0))
    for k in range(d, 0, -1):
        big.paste(Image.new("RGBA", fin.size, pal["side"]["RUN"][1] + (255,)), (k // 2, k), a)
    big.alpha_composite(fin)
    key = _dilate(big.getchannel("A"), stroke)
    framed = Image.new("RGBA", big.size, pal["key"] + (0,))
    framed.putalpha(key)
    framed.alpha_composite(big)
    return framed


def _solve(A, B):
    """Gaussian elimination with partial pivoting (8x8; keeps the script free of numpy)."""
    n = len(B)
    M = [list(map(float, A[i])) + [float(B[i])] for i in range(n)]
    for c in range(n):
        p = max(range(c, n), key=lambda r: abs(M[r][c]))
        M[c], M[p] = M[p], M[c]
        for r in range(n):
            if r != c:
                f = M[r][c] / M[c][c]
                M[r] = [a - f * b for a, b in zip(M[r], M[c])]
    return [M[i][n] / M[i][i] for i in range(n)]


def _perspective(img, tilt):
    """Lean the top edge back: the top is `tilt` x narrower than the bottom (seen from below)."""
    w, h = img.size
    inset = w * tilt / 2
    # Map output corners to input corners (PIL wants the inverse mapping coefficients).
    src = [(0, 0), (w, 0), (w, h), (0, h)]
    dst = [(inset, 0), (w - inset, 0), (w, h), (0, h)]
    A, B = [], []
    for (x, y), (u, v) in zip(dst, src):
        A.append([x, y, 1, 0, 0, 0, -u * x, -u * y]); B.append(u)
        A.append([0, 0, 0, x, y, 1, -v * x, -v * y]); B.append(v)
    coeffs = _solve(A, B)
    return img.transform((w, h), Image.PERSPECTIVE, tuple(coeffs), Image.BICUBIC)


def logo3d(height, style="A"):
    font = ImageFont.truetype(L.FONT, int(CAP * 1.38))
    if style == "B":
        depth, direction, caustic = int(CAP * 0.06), (1, 1), True
    elif style == "D":
        depth, direction, caustic = int(CAP * 0.22), (0, 1), False
    else:
        depth, direction, caustic = int(CAP * 0.11), (1, 1), False
    pal = PALETTES["backlit" if style == "E" else "sunlit" if style == "F" else "game"]
    reef = _word("REEF", font, depth, direction, caustic=caustic, pal=pal)
    run = _word("RUN", font, depth, direction, caustic=caustic, pal=pal)

    gap = int(CAP * 0.10)
    if style == "C":
        lead = int(CAP * 0.06)
        w = max(reef.width, run.width + int(CAP * 0.42))
        words = Image.new("RGBA", (w, reef.height + run.height - lead), (0, 0, 0, 0))
        words.alpha_composite(run, (w - run.width, reef.height - lead))
        words.alpha_composite(reef, (0, 0))   # REEF's extrusion sits over RUN's top
    else:
        words = Image.new("RGBA", (reef.width + gap + run.width, max(reef.height, run.height)), (0, 0, 0, 0))
        words.alpha_composite(reef, (0, 0))
        words.alpha_composite(run, (reef.width + gap, 0))

    fin = _fin(int(words.width * 0.92), CAP, pal)
    lock = Image.new("RGBA", (words.width, words.height + fin.height + int(CAP * 0.02)), (0, 0, 0, 0))
    lock.alpha_composite(fin, ((words.width - fin.width) // 2, words.height + int(CAP * 0.02)))
    lock.alpha_composite(words, (0, 0))
    if style == "D":
        lock = _perspective(lock, 0.30)

    pad = int(CAP * 0.5)
    canvas = Image.new("RGBA", (lock.width + 2 * pad, lock.height + 2 * pad), (0, 0, 0, 0))
    a = Image.new("L", canvas.size, 0)
    glow = style == "E"   # backlit: a halo around the letters instead of a dropped shadow
    a.paste(lock.getchannel("A").point(lambda v: int(v * pal["shadow_alpha"])), (pad, pad + (0 if glow else int(CAP * 0.10))))
    a = a.filter(ImageFilter.GaussianBlur(CAP * (0.22 if glow else 0.14))).point(lambda v: 0 if v < 3 else v)
    sh = Image.new("RGBA", canvas.size, pal["shadow"] + (0,))
    sh.putalpha(a)
    canvas.alpha_composite(sh)
    canvas.alpha_composite(lock, (pad, pad))
    canvas = canvas.crop(canvas.getchannel("A").getbbox())
    s = height / canvas.height
    return canvas.resize((max(1, round(canvas.width * s)), round(height)), Image.LANCZOS)
