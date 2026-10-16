"""REEF RUN title exactly as the game sets it, with optional depth / bite / waterline on top.

The in-game logo (main menu `.mm-logo-word`, pause menu, launch overlay - Assets/UI Toolkit/*.uss):
    Barlow Condensed ExtraBold Italic, font-size 190px, letter-spacing -2px, "RUN" 36px after "REEF",
    REEF #eef6f7 (--text), RUN #2cc3c4 (--teal-l), no outline, text-shadow 0 8px 40px rgba(0,0,0,0.55).
Measured from a capture of the running main menu: the two words' ink is 4.39 x as wide as it is tall; the word gap
below is solved so the lockup lands on that ratio (UI Toolkit's label metrics differ slightly from Pillow's).

    title(height, style) -> RGBA
      "M0"  Game logo     - exactly the menu's look: flat colours, tight, soft shadow
      "M1"  Game + depth  - same letters and spacing, a short solid extrusion (the "bit 3D")
      "M2"  Game + bite   - flat, with the shark bite out of the N and the waterline through the words
      "M3"  Game + all    - depth, bite and waterline together (the chosen idea, in the game's own lettering)
"""
import os
from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

import reefrun_logo as L
import reefrun_letters as R

SIZE = 900                       # working font size (px); everything else is in proportion to the menu's 190px
K = SIZE / 190.0
TEXT = (238, 246, 247)           # --text
TEAL = (44, 195, 196)            # --teal-l
TARGET_RATIO = 4.39              # ink width / ink height of the in-game logo


def _word_mask(text, font):
    """A word with the menu's -2px tracking, as an L mask cropped to its ink."""
    track = -2 * K
    w = int(sum(font.getlength(c) for c in text) + SIZE)
    m = Image.new("L", (w, int(SIZE * 1.4)), 0)
    d = ImageDraw.Draw(m)
    x = SIZE * 0.2
    for c in text:
        d.text((x, SIZE * 0.1), c, font=font, fill=255)
        x += font.getlength(c) + track
    return m.crop(m.getbbox())


def _lockup_masks():
    font = ImageFont.truetype(L.FONT, SIZE)
    reef, run = _word_mask("REEF", font), _word_mask("RUN", font)
    h = max(reef.height, run.height)
    # Solve the gap so the whole lockup matches the in-game proportions.
    gap = int(round(TARGET_RATIO * h - reef.width - run.width))
    gap = max(int(-0.02 * SIZE), gap)
    W = reef.width + gap + run.width
    pad = int(SIZE * 0.35)
    m_reef = Image.new("L", (W + 2 * pad, h + 2 * pad), 0)
    m_run = Image.new("L", m_reef.size, 0)
    m_reef.paste(reef, (pad, pad + h - reef.height))
    m_run.paste(run, (pad + reef.width + gap, pad + h - run.height))
    return m_reef, m_run, (pad + reef.width + gap, pad, pad + W, pad + h)


def _shift(m, dx, dy):
    out = Image.new("L", m.size, 0)
    out.paste(m, (dx, dy))
    return out


def _build(depth=False, bite=False, water=False):
    m_reef, m_run, run_box = _lockup_masks()
    if bite:   # bite the letter shape itself, so the extrusion follows the bite (and shards are true islands)
        m_run = _bite_mask(m_run, run_box)
    size = m_reef.size
    out = Image.new("RGBA", size, (0, 0, 0, 0))
    if depth:
        steps = int(SIZE * 0.06)
        sides = {id(m_reef): ((150, 176, 188), (52, 78, 96)), id(m_run): ((22, 128, 132), (8, 60, 70))}
        for m in (m_reef, m_run):
            top, bot = sides[id(m)]
            for k in range(steps, 0, -1):
                t = k / steps
                col = tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3))
                out.paste(Image.new("RGBA", size, col + (255,)), (0, 0), _shift(m, int(k * 0.55), k))
    # Faces: the game's flat colours (a whisper of top light so the depth reads as lit, not painted).
    for m, col in ((m_reef, TEXT), (m_run, TEAL)):
        face = L._gradient(size, tuple(min(255, int(c * 1.04)) for c in col), tuple(int(c * 0.93) for c in col)) if depth else Image.new("RGB", size, col)
        out.paste(face, (0, 0), m)
    if water:
        out = _surface(out, run_box)
    return out.crop(out.getchannel("A").getbbox())


def _surface(img, box):
    """The waterline through the words: crisp above, a gentle refraction ripple and a light sea tint below.
    Tuned to the letter height (the generic one in reefrun_letters is far too strong on this tight lockup)."""
    import math
    w, h = img.size
    letter_h = box[3] - box[1]
    line = int(box[1] + letter_h * 0.52)
    amp, period = letter_h * 0.018, letter_h * 0.30
    out = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    out.paste(img.crop((0, 0, w, line)), (0, 0))
    below = img.crop((0, line, w, h))
    rip = Image.new("RGBA", below.size, (0, 0, 0, 0))
    for y in range(below.height):
        fade = min(1.0, y / (letter_h * 0.10))                       # calm right at the surface
        dx = int(round(amp * fade * math.sin(2 * math.pi * y / period)))
        rip.paste(below.crop((0, y, w, y + 1)), (dx, y))
    tint = Image.new("RGBA", rip.size, (90, 215, 220, 0))
    tint.putalpha(rip.getchannel("A").point(lambda v: int(v * 0.22)))
    rip.alpha_composite(tint)
    out.paste(rip, (0, line))
    # The surface: a thin bright line, only across the letters.
    t = max(2, int(letter_h * 0.022))
    surf = Image.new("L", (w, h), 0)
    pts = [(x, line + letter_h * 0.010 * math.sin(2 * math.pi * x / (letter_h * 0.9))) for x in range(0, w, 3)]
    ImageDraw.Draw(surf).line(pts, fill=255, width=t)
    ink = img.getchannel("A").point(lambda v: 255 if v > 128 else 0)
    out.paste(Image.new("RGBA", (w, h), (245, 255, 255, 255)), (0, 0), ImageChops.multiply(surf, ink))
    return out


def _bite_mask(m, box):
    """The same serrated bite as style I, taken out of RUN's mask (clean cut: the game's logo has no outline)."""
    x0, y0, x1, y1 = box
    r = (y1 - y0) * 0.40
    cx, cy = x1 - r * 0.15, y0 + r * 0.05     # centred on the N's top-right corner: a clear scoop out of it
    cut = Image.new("L", m.size, 0)
    ImageDraw.Draw(cut).polygon(R._bite_poly(cx, cy, r, teeth=14, depth=0.20), fill=255)
    rgba = Image.new("RGBA", m.size, (0, 0, 0, 0))
    rgba.putalpha(ImageChops.subtract(m, cut))
    rgba = _drop_shards(rgba, (int(cx - r * 1.3), int(cy - r * 1.3), int(cx + r * 1.3), int(cy + r * 1.3)))
    return rgba.getchannel("A")


def _drop_shards(img, region):
    """Remove small islands of ink the teeth can leave behind inside `region` (they read as noise when small)."""
    a = img.getchannel("A")
    x0, y0 = max(0, region[0]), max(0, region[1])
    x1, y1 = min(a.width, region[2]), min(a.height, region[3])
    sub = a.crop((x0, y0, x1, y1)).point(lambda v: 1 if v > 40 else 0)
    w, h = sub.size
    px = list(sub.getdata())
    seen = [False] * (w * h)
    comps = []
    for i in range(w * h):
        if px[i] and not seen[i]:
            stack, comp = [i], []
            seen[i] = True
            while stack:
                j = stack.pop(); comp.append(j)
                x, y = j % w, j // w
                for k in ((j - 1) if x > 0 else -1, (j + 1) if x < w - 1 else -1, (j - w) if y > 0 else -1, (j + w) if y < h - 1 else -1):
                    if k >= 0 and px[k] and not seen[k]:
                        seen[k] = True; stack.append(k)
            comps.append(comp)
    if len(comps) < 2:
        return img
    biggest = max(len(c) for c in comps)
    kill = Image.new("L", (w, h), 0)
    kp = kill.load()
    for c in comps:
        if len(c) < biggest * 0.05:
            for j in c:
                kp[j % w, j // w] = 255
    kill = kill.filter(ImageFilter.MaxFilter(5))
    full = Image.new("L", a.size, 0)
    full.paste(kill, (x0, y0))
    img.putalpha(ImageChops.subtract(a, full))
    return img


def _shadow(lock, height):
    """text-shadow: 0 8px 40px rgba(0,0,0,0.55), in proportion."""
    pad = int(SIZE * 0.6)
    canvas = Image.new("RGBA", (lock.width + 2 * pad, lock.height + 2 * pad), (0, 0, 0, 0))
    a = Image.new("L", canvas.size, 0)
    a.paste(lock.getchannel("A").point(lambda v: int(v * 0.55)), (pad, pad + int(8 * K)))
    a = a.filter(ImageFilter.GaussianBlur(40 * K / 2)).point(lambda v: 0 if v < 3 else v)
    sh = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    sh.putalpha(a)
    canvas.alpha_composite(sh)
    canvas.alpha_composite(lock, (pad, pad))
    canvas = canvas.crop(canvas.getchannel("A").getbbox())
    s = height / canvas.height
    return canvas.resize((max(1, round(canvas.width * s)), round(height)), Image.LANCZOS)


def title(height, style="M0"):
    flags = {"M0": (False, False, False), "M1": (True, False, False),
             "M2": (False, True, True), "M3": (True, True, True)}[style]
    return _shadow(_build(*flags), height)
