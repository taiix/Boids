"""REEF RUN logotype, built from the game's own branding (see Assets/UI Toolkit/ReefRunMainMenu.uss):
Barlow Condensed ExtraBold Italic, "REEF" in --text #eef6f7 and "RUN" in --teal-l #2cc3c4, on the navy #051729.

    logo(height, layout="wide"|"stacked", mark=True, shadow=True) -> RGBA PIL image, transparent background

Everything is drawn at SS x and averaged down, so edges stay clean at every capsule size. The fin-and-waterline
mark under the words is optional (mark=False gives the bare wordmark).
"""
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageChops

HERE = os.path.dirname(os.path.abspath(__file__))
FONT = os.path.join(HERE, "fonts", "BarlowCondensed-ExtraBoldItalic.ttf")

TEXT = (238, 246, 247)      # --text
TEAL = (44, 195, 196)       # --teal-l
TEAL_D = (23, 148, 153)     # --teal
NAVY = (5, 23, 41)          # --navy
SS = 4                      # supersampling


def _gradient(size, top, bottom):
    w, h = size
    g = Image.new("RGB", (1, h))
    for y in range(h):
        t = y / max(1, h - 1)
        g.putpixel((0, y), tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    return g.resize((w, h))


def _shade(c, k):
    return tuple(max(0, min(255, int(v * k))) for v in c)


def _word_layer(text, font, colour, stroke):
    """RGBA layer of one word: gradient fill with a navy outline. Returns (layer, ink box)."""
    l, t, r, b = font.getbbox(text, stroke_width=stroke)
    w, h = r - l + 2 * stroke, b - t + 2 * stroke
    origin = (-l + stroke, -t + stroke)
    outline = Image.new("L", (w, h), 0)
    ImageDraw.Draw(outline).text(origin, text, font=font, fill=255, stroke_width=stroke, stroke_fill=255)
    fill = Image.new("L", (w, h), 0)
    ImageDraw.Draw(fill).text(origin, text, font=font, fill=255)
    layer = Image.new("RGBA", (w, h), NAVY + (0,))
    layer.putalpha(outline)
    # Lit from above like everything underwater: lighter at the top, a touch deeper at the bottom.
    grad = _gradient((w, h), _shade(colour, 1.08), _shade(colour, 0.82))
    layer.paste(grad, (0, 0), fill)
    return layer


def _bezier(p0, p1, p2, steps=24):
    return [((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t ** 2 * p2[0],
             (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t ** 2 * p2[1]) for t in (k / steps for k in range(steps + 1))]


def _fin_mark(width, cap, colour):
    """A waterline swept under the words, with a shark's dorsal fin breaking it, travelling right (the way the
    words read): convex leading edge in front, the tip hooked back, a concave trailing edge."""
    th = max(2, int(cap * 0.12))            # waterline thickness
    fin_h = int(cap * 0.46)
    fin_w = int(cap * 0.52)
    h = fin_h + th * 2
    img = Image.new("RGBA", (width, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    base = h - th * 1.5                     # top of the waterline
    fx = width * 0.70                       # fin's trailing foot
    # Waterline: thick through the fin, tapering to points at both ends.
    d.polygon([(width * 0.04, base + th * 0.5), (fx, base), (fx + fin_w, base), (width, base + th * 0.45),
               (fx + fin_w, base + th), (fx, base + th)], fill=colour + (255,))
    trail_foot = (fx, base + 1)
    lead_foot = (fx + fin_w, base + 1)
    tip = (fx + fin_w * 0.10, base - fin_h)
    lead = _bezier(lead_foot, (fx + fin_w * 0.78, base - fin_h * 0.70), tip)     # convex front
    trail = _bezier(tip, (fx + fin_w * 0.30, base - fin_h * 0.30), trail_foot)  # concave back
    d.polygon(lead + trail, fill=colour + (255,))
    return img


def logo(height, layout="wide", mark=True, shadow=True, outline=True):
    """The logotype `height` px tall (whole lockup), on transparency."""
    # Work at a nominal cap height, then scale the finished lockup to the requested height.
    cap = 200 * SS
    font = ImageFont.truetype(FONT, int(cap * 1.38))
    stroke = int(cap * 0.075) if outline else 0
    reef = _word_layer("REEF", font, TEXT, stroke)
    run = _word_layer("RUN", font, TEAL, stroke)
    gap = int(cap * 0.16)
    if layout == "wide":
        w = reef.width + gap + run.width
        h = max(reef.height, run.height)
        words = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        words.alpha_composite(reef, (0, h - reef.height))
        words.alpha_composite(run, (reef.width + gap, h - run.height))
    else:  # stacked: RUN tucked under REEF, shifted right along the italic slant
        lead = int(cap * 0.10)
        w = max(reef.width, run.width + int(cap * 0.36))
        h = reef.height + run.height - lead
        words = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        words.alpha_composite(reef, (0, 0))
        words.alpha_composite(run, (w - run.width, reef.height - lead))

    parts = [words]
    if mark:
        fin = _fin_mark(int(words.width * 0.96), cap, TEAL)
        if outline:  # the same navy keyline as the letters
            # Dilate by the stroke width (blur + threshold: same result as a max filter, far faster at this size).
            a = fin.getchannel("A").filter(ImageFilter.GaussianBlur(stroke * 0.6)).point(lambda v: 255 if v > 8 else v * 32)
            keyed = Image.new("RGBA", fin.size, NAVY + (0,))
            keyed.putalpha(a)
            keyed.alpha_composite(fin)
            fin = keyed
        drop = int(cap * 0.04)                # small gap under the outlined letters
        lock = Image.new("RGBA", (words.width, words.height + fin.height + drop), (0, 0, 0, 0))
        lock.alpha_composite(words, (0, 0))
        lock.alpha_composite(fin, (int(words.width * 0.02), words.height + drop))
        parts = [lock]
    art = parts[0]

    pad = int(cap * 0.5) if shadow else int(cap * 0.05)
    canvas = Image.new("RGBA", (art.width + 2 * pad, art.height + 2 * pad), (0, 0, 0, 0))
    if shadow:  # soft, slightly dropped shadow so the title holds on bright water too
        # Built at canvas size, so the blur has room to fade out instead of being clipped into a box.
        a = Image.new("L", canvas.size, 0)
        a.paste(art.getchannel("A").point(lambda v: int(v * 0.55)), (pad, pad + int(cap * 0.07)))
        a = a.filter(ImageFilter.GaussianBlur(cap * 0.12)).point(lambda v: 0 if v < 3 else v)
        sh = Image.new("RGBA", canvas.size, (2, 10, 20, 0))
        sh.putalpha(a)
        canvas.alpha_composite(sh)
    canvas.alpha_composite(art, (pad, pad))
    canvas = canvas.crop(canvas.getchannel("A").getbbox())

    scale = height / canvas.height
    out = canvas.resize((max(1, round(canvas.width * scale)), max(1, round(height))), Image.LANCZOS)
    return out


if __name__ == "__main__":
    import sys
    out = sys.argv[1] if len(sys.argv) > 1 else HERE
    for layout in ("wide", "stacked"):
        im = logo(400, layout)
        bg = Image.new("RGBA", (im.width + 80, im.height + 80), (60, 120, 150, 255))
        bg.alpha_composite(im, (40, 40))
        bg.save(os.path.join(out, f"_preview_logo_{layout}.png"))
