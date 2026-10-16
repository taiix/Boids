"""Builds every REEF RUN Steam capsule from the in-engine art plates (../plates, rendered by Tools > Marketing >
Capture Capsule Plates) and the logotype (reefrun_logo.py).

    python compose_capsules.py

Writes to ../ (the SteamCapsules folder):
    store/       small, header, main, vertical capsules, page background  (exact Steam sizes, PNG)
    library/     library capsule, library header, library hero (no text), library logo (transparent PNG)
    master/      4K key art without and with the title, high-res logotypes (wide + stacked, transparent)
    editable/    one SVG per titled capsule (plate + live-text logotype) and the logotypes as SVG
    previews/    contact sheet, and every capsule at the size Steam actually shows it, on Steam's own colours

The layout of each format is the LAYOUTS table below: which logo, how tall (fraction of the image height), where
(fractions of the image), and on which side the dark scrim sits that keeps the title readable over the water.
"""
import os
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageEnhance, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
PLATES = os.path.join(ROOT, "plates")
sys.path.insert(0, HERE)
import reefrun_logo as L  # noqa: E402

NAVY = L.NAVY

# Verified against partner.steamgames.com/doc/store/assets (+ /libraryassets, /rules) on 2026-10-16.
#  name               plate                              size          logo       height  x      y      scrim
LAYOUTS = {
    "store/small_capsule_462x174":     ("small_capsule_462x174",   (462, 174),   "wide",  0.50, 0.045, 0.22, "left"),
    "store/header_capsule_920x430":    ("header_capsule_920x430",  (920, 430),   "wide",  0.33, 0.045, 0.30, "left"),
    "store/main_capsule_1232x706":     ("main_capsule_1232x706",   (1232, 706),  "wide",  0.30, 0.045, 0.07, "topleft"),
    "store/vertical_capsule_748x896":  ("vertical_capsule_748x896", (748, 896),  "wide",  0.255, None, 0.045, "top"),
    "library/library_capsule_600x900": ("library_capsule_600x900", (600, 900),   "wide",  0.225, None, 0.045, "top"),
    "library/library_header_920x430":  ("header_capsule_920x430",  (920, 430),   "wide",  0.33, 0.045, 0.30, "left"),
    "master/master_keyart_logo_3840x2160": ("master_keyart_3840x2160", (3840, 2160), "wide", 0.28, 0.05, 0.08, "topleft"),
}


def finish(img, scrim, strength=0.62):
    """Light grade on top of the render: a soft vignette, and a navy scrim on the title's side."""
    w, h = img.size
    img = img.convert("RGB")
    # Vignette: the corners settle by ~22%, the centre is untouched.
    vig = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(vig)
    d.ellipse((-w * 0.25, -h * 0.30, w * 1.25, h * 1.30), fill=255)
    vig = vig.filter(ImageFilter.GaussianBlur(min(w, h) * 0.22))
    dark = ImageEnhance.Brightness(img).enhance(0.66)
    img = Image.composite(img, dark, vig)
    if scrim:
        # Computed small and scaled up: it's a smooth ramp, so nothing is lost and it takes no time at 4K.
        sw, sh = 160, max(2, round(160 * h / w))
        mask = Image.new("L", (sw, sh), 0)
        px = mask.load()
        for y in range(sh):
            for x in range(sw):
                u, v = x / (sw - 1), y / (sh - 1)
                if scrim == "left":
                    t = 1 - u / 0.62
                elif scrim == "top":
                    t = 1 - v / 0.42
                else:  # topleft
                    t = 1 - ((u / 0.70) ** 2 + (v / 0.65) ** 2) ** 0.5
                t = max(0.0, min(1.0, t))
                px[x, y] = int(255 * strength * t * t * (3 - 2 * t))
        mask = mask.resize((w, h), Image.BILINEAR)
        navy = Image.new("RGB", (w, h), NAVY)
        img = Image.composite(navy, img, mask)
    return img


def place_logo(img, layout, height_frac, x_frac, y_frac):
    w, h = img.size
    logo = L.logo(round(h * height_frac), layout)
    if logo.width > w * 0.9:  # never wider than the image allows
        logo = L.logo(round(h * height_frac * (w * 0.9) / logo.width), layout)
    x = round((w - logo.width) / 2) if x_frac is None else round(w * x_frac)
    y = round(h * y_frac)
    out = img.convert("RGBA")
    out.alpha_composite(logo, (x, y))
    return out.convert("RGB"), (x, y, logo.width, logo.height)


def plate(name, size):
    im = Image.open(os.path.join(PLATES, name + ".png")).convert("RGB")
    if im.size != size:
        raise SystemExit(f"plate {name} is {im.size}, expected {size}: re-render it (Capture Capsule Plates)")
    return im


def svg_layout(path, plate_rel, size, logo_svg_rel, box, scrim):
    w, h = size
    x, y, lw, lh = box
    grad = {"left": ('x1="0" y1="0" x2="0.62" y2="0"'), "top": ('x1="0" y1="0" x2="0" y2="0.42"'),
            "topleft": ('x1="0" y1="0" x2="0.6" y2="0.6"')}.get(scrim, 'x1="0" y1="0" x2="1" y2="0"')
    svg = f"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="{w}" height="{h}" viewBox="0 0 {w} {h}">
  <!-- REEF RUN capsule layout. Layers: in-engine art plate, navy title scrim, logotype (live text in {logo_svg_rel}).
       The PNG export is made by compose_capsules.py, which also adds a soft vignette. -->
  <defs>
    <linearGradient id="scrim" {grad}>
      <stop offset="0" stop-color="#051729" stop-opacity="0.62"/>
      <stop offset="1" stop-color="#051729" stop-opacity="0"/>
    </linearGradient>
  </defs>
  <image id="plate" x="0" y="0" width="{w}" height="{h}" xlink:href="{plate_rel}" href="{plate_rel}"/>
  <rect id="title-scrim" x="0" y="0" width="{w}" height="{h}" fill="url(#scrim)"/>
  <image id="logo" x="{x}" y="{y}" width="{lw}" height="{lh}" xlink:href="{logo_svg_rel}" href="{logo_svg_rel}"/>
</svg>
"""
    with open(path, "w", encoding="utf-8") as f:
        f.write(svg)


def logo_svg(path, layout):
    """The logotype as editable SVG: live text in the game's font, navy keyline, gradient fills, fin mark."""
    # Proportions match reefrun_logo.py (cap 200, stroke 15, word gap 32).
    if layout == "wide":
        w, h = 1000, 420
        words = ('<text x="40" y="250" class="w">REEF</text>'
                 '<text x="500" y="250" class="w run">RUN</text>')
        fin = "M 60 330 L 680 316 L 800 316 L 960 324 L 800 340 L 680 340 Z M 864 318 Q 836 282 696 236 Q 750 282 760 318 Z"
    else:
        w, h = 560, 560
        words = ('<text x="40" y="230" class="w">REEF</text>'
                 '<text x="110" y="420" class="w run">RUN</text>')
        fin = "M 40 500 L 380 488 L 470 488 L 540 494 L 470 508 L 380 508 Z M 520 490 Q 500 462 410 426 Q 446 462 452 490 Z"
    svg = f"""<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">
  <!-- REEF RUN logotype ({layout}). Font: Barlow Condensed ExtraBold Italic (SIL OFL 1.1, fonts/).
       Colours from the game UI: --text #eef6f7, --teal-l #2cc3c4, --navy #051729. -->
  <defs>
    <linearGradient id="reef" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#ffffff"/><stop offset="1" stop-color="#c3c9ca"/></linearGradient>
    <linearGradient id="run" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#2fd2d3"/><stop offset="1" stop-color="#24a0a1"/></linearGradient>
    <filter id="shadow" x="-10%" y="-10%" width="120%" height="140%"><feDropShadow dx="0" dy="14" stdDeviation="22" flood-color="#020a14" flood-opacity="0.55"/></filter>
    <style>
      .w {{ font-family: 'Barlow Condensed', sans-serif; font-weight: 800; font-style: italic; font-size: 276px;
            fill: url(#reef); stroke: #051729; stroke-width: 30px; stroke-linejoin: round; paint-order: stroke fill; }}
      .run {{ fill: url(#run); }}
    </style>
  </defs>
  <g filter="url(#shadow)">
    {words}
    <path d="{fin}" fill="#2cc3c4" stroke="#051729" stroke-width="16" stroke-linejoin="round" paint-order="stroke fill"/>
  </g>
</svg>
"""
    with open(path, "w", encoding="utf-8") as f:
        f.write(svg)


def main():
    for sub in ("store", "library", "master", "editable", "previews"):
        os.makedirs(os.path.join(ROOT, sub), exist_ok=True)

    # Logotypes: high-res masters and the Steam library logo (1280 wide, <= 720 tall, transparent).
    wide = L.logo(1200, "wide")
    wide.save(os.path.join(ROOT, "master", f"logo_wide_{wide.width}x{wide.height}.png"))
    stacked = L.logo(1600, "stacked")
    stacked.save(os.path.join(ROOT, "master", f"logo_stacked_{stacked.width}x{stacked.height}.png"))
    lib = L.logo(1000, "wide")
    lib = lib.resize((1280, round(lib.height * 1280 / lib.width)), Image.LANCZOS)
    assert lib.height <= 720, lib.size
    lib.save(os.path.join(ROOT, "library", f"library_logo_1280x{lib.height}.png"))
    logo_svg(os.path.join(ROOT, "editable", "logo_wide.svg"), "wide")
    logo_svg(os.path.join(ROOT, "editable", "logo_stacked.svg"), "stacked")

    # Clean art: master key art and library hero (no text allowed on the hero).
    finish(plate("master_keyart_3840x2160", (3840, 2160)), None).save(os.path.join(ROOT, "master", "master_keyart_3840x2160.png"))
    finish(plate("library_hero_3840x1240", (3840, 1240)), None).save(os.path.join(ROOT, "library", "library_hero_3840x1240.png"))

    # Store page background: the reef, quiet and dark so the page reads on top of it.
    bg = plate("page_background_1438x810", (1438, 810))
    bg = ImageEnhance.Color(bg).enhance(0.75)
    bg = ImageEnhance.Brightness(bg).enhance(0.42)
    finish(bg, None).save(os.path.join(ROOT, "store", "page_background_1438x810.png"))

    # Titled capsules.
    for out, (plate_name, size, layout, hf, xf, yf, scrim) in LAYOUTS.items():
        img = finish(plate(plate_name, size), scrim)
        img, box = place_logo(img, layout, hf, xf, yf)
        assert img.size == size, (out, img.size)
        img.save(os.path.join(ROOT, out + ".png"))
        svg_layout(os.path.join(ROOT, "editable", os.path.basename(out) + ".svg"),
                   f"../plates/{plate_name}.png", size, f"logo_{layout}.svg", box, scrim)
        print("wrote", out, size)


# ------------------------------------------------------------------ previews

STEAM_BG = (27, 40, 56)      # Steam store dark blue
STEAM_PANEL = (22, 32, 45)
LABEL = (198, 212, 223)


def _font(size, bold=True):
    return ImageFont.truetype(os.path.join(HERE, "fonts", "BarlowCondensed-ExtraBold.ttf"), size)


def _fit(im, box):
    im = im.copy()
    im.thumbnail(box, Image.LANCZOS)
    return im


def previews():
    """Contact sheet of every final, the capsules at the size Steam shows them, and a store-page mock."""
    finals = [
        ("store/main_capsule_1232x706.png", "Main capsule 1232x706"),
        ("store/header_capsule_920x430.png", "Header capsule 920x430"),
        ("store/small_capsule_462x174.png", "Small capsule 462x174"),
        ("store/vertical_capsule_748x896.png", "Vertical capsule 748x896"),
        ("store/page_background_1438x810.png", "Page background 1438x810"),
        ("library/library_capsule_600x900.png", "Library capsule 600x900"),
        ("library/library_header_920x430.png", "Library header 920x430"),
        ("library/library_hero_3840x1240.png", "Library hero 3840x1240 (no text)"),
        ("library/" + [f for f in os.listdir(os.path.join(ROOT, "library")) if f.startswith("library_logo_")][0], "Library logo (transparent)"),
        ("master/master_keyart_3840x2160.png", "Master key art 3840x2160"),
        ("master/master_keyart_logo_3840x2160.png", "Master key art + title"),
    ]
    # Contact sheet: 3 across, each in a 600x420 cell, labelled with its real size.
    cw, ch, cols = 600, 420, 3
    rows = (len(finals) + cols - 1) // cols
    sheet = Image.new("RGB", (cw * cols + 40, ch * rows + 90), STEAM_BG)
    d = ImageDraw.Draw(sheet)
    d.text((20, 18), "REEF RUN  -  Steam capsule set", font=_font(40), fill=(238, 246, 247))
    for i, (rel, label) in enumerate(finals):
        im = Image.open(os.path.join(ROOT, rel))
        x0, y0 = 20 + (i % cols) * cw, 80 + (i // cols) * ch
        thumb = _fit(im, (cw - 30, ch - 60))
        if thumb.mode == "RGBA":  # show transparency on a checker
            chk = Image.new("RGB", thumb.size, (60, 70, 80))
            cd = ImageDraw.Draw(chk)
            for yy in range(0, thumb.height, 16):
                for xx in range(0, thumb.width, 16):
                    if (xx // 16 + yy // 16) % 2: cd.rectangle((xx, yy, xx + 15, yy + 15), fill=(90, 100, 110))
            chk.paste(thumb, (0, 0), thumb)
            thumb = chk
        sheet.paste(thumb, (x0 + (cw - 30 - thumb.width) // 2, y0))
        d.text((x0, y0 + ch - 52), f"{label}   [{im.width}x{im.height} {im.mode}]", font=_font(22), fill=LABEL)
    sheet.save(os.path.join(ROOT, "previews", "contact_sheet.png"))

    # Display sizes: Steam shows each capsule at half its upload size on a standard-DPI screen (the uploads are
    # 2x for high-DPI). Plus a stress test far smaller than that.
    shown = [
        ("store/main_capsule_1232x706.png", (616, 353), "Main capsule as shown: 616x353"),
        ("store/header_capsule_920x430.png", (460, 215), "Header capsule as shown: 460x215"),
        ("store/small_capsule_462x174.png", (231, 87), "Small capsule as shown: 231x87"),
        ("store/vertical_capsule_748x896.png", (374, 448), "Vertical capsule as shown: 374x448"),
        ("library/library_capsule_600x900.png", (300, 450), "Library capsule (Steam's half-size): 300x450"),
        ("store/small_capsule_462x174.png", (120, 45), "Stress test: 120x45"),
        ("store/header_capsule_920x430.png", (184, 86), "Stress test: 184x86"),
    ]
    W, H = 1700, 1080
    disp = Image.new("RGB", (W, H), STEAM_BG)
    d = ImageDraw.Draw(disp)
    d.text((24, 16), "At the size Steam shows them (1:1 pixels, standard DPI)", font=_font(34), fill=(238, 246, 247))
    x, y, rowh = 24, 74, 0
    for rel, size, label in shown:
        im = Image.open(os.path.join(ROOT, rel)).convert("RGB").resize(size, Image.LANCZOS)
        if x + im.width > W - 24:
            x, y, rowh = 24, y + rowh + 56, 0
        disp.paste(im, (x, y))
        d.text((x, y + im.height + 8), label, font=_font(20), fill=LABEL)
        x += im.width + 30
        rowh = max(rowh, im.height)
    disp.save(os.path.join(ROOT, "previews", "display_sizes.png"))

    # Store-page mock: page background behind a centred column; main capsule in the media slot, header in the
    # sidebar slot, small capsule in a list row. Grey bars stand in for Steam's own text (none of it is ours).
    W, H = 1438, 810
    page = Image.open(os.path.join(ROOT, "store", "page_background_1438x810.png")).convert("RGB")
    d = ImageDraw.Draw(page, "RGBA")
    col_x, col_w = 175, 1088
    d.rectangle((col_x, 0, col_x + col_w, H), fill=(14, 22, 33, 150))
    main = Image.open(os.path.join(ROOT, "store", "main_capsule_1232x706.png")).convert("RGB").resize((616, 353), Image.LANCZOS)
    head = Image.open(os.path.join(ROOT, "store", "header_capsule_920x430.png")).convert("RGB").resize((460, 215), Image.LANCZOS)
    small = Image.open(os.path.join(ROOT, "store", "small_capsule_462x174.png")).convert("RGB").resize((231, 87), Image.LANCZOS)
    d.rectangle((col_x + 20, 30, col_x + 360, 52), fill=(198, 212, 223, 90))           # title bar placeholder
    page.paste(main, (col_x + 20, 80))
    page.paste(head, (col_x + 20 + 616 + 20, 80))
    for i in range(4):                                                                   # sidebar text placeholders
        d.rectangle((col_x + 656, 315 + i * 26, col_x + 656 + 420 - i * 60, 330 + i * 26), fill=(198, 212, 223, 60))
    for r in range(3):                                                                   # "more like this" row
        yy = 470 + r * 100
        d.rectangle((col_x + 20, yy, col_x + col_w - 20, yy + 87), fill=(22, 32, 45, 230))
        if r == 1:
            page.paste(small, (col_x + 20, yy))
        else:
            d.rectangle((col_x + 20, yy, col_x + 251, yy + 87), fill=(40, 55, 72, 255))
        d.rectangle((col_x + 270, yy + 20, col_x + 520, yy + 36), fill=(198, 212, 223, 70))
        d.rectangle((col_x + 270, yy + 50, col_x + 420, yy + 62), fill=(198, 212, 223, 45))
    page.save(os.path.join(ROOT, "previews", "store_page_mock.png"))


if __name__ == "__main__":
    main()
    previews()
