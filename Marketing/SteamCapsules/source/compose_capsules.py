"""Builds every REEF RUN Steam capsule from the in-engine art plates (../plates, rendered by Tools > Marketing >
Capture Capsule Plates: the "Eclipse" shot, composed separately for each format) and the chosen title
(reefrun_gametitle.title(h, "M3"): REEF RUN set exactly like the game's main-menu logo - Barlow Condensed ExtraBold
Italic, white REEF / teal RUN, tight spacing - with a short extrusion, a shark bite out of the N and a waterline).

    python compose_capsules.py

Writes to ../ (the SteamCapsules folder):
    store/       small, header, main, vertical capsules, page background  (exact Steam sizes, PNG)
    library/     library capsule, library header, library hero (no text), library logo (transparent PNG)
    master/      4K key art without and with the title, the title alone at high resolution (transparent)
    editable/    one layered SVG per titled capsule: art plate, scrim, title (each its own layer / file)
    previews/    contact sheet, every capsule at the size Steam shows it, a store-page mock

The layout of each format is the LAYOUTS table: where the title's centre sits (fraction of the image height). The
title is always centred horizontally and TITLE_WIDTH of the image wide.
"""
import os
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageEnhance, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
PLATES = os.path.join(ROOT, "plates")
sys.path.insert(0, HERE)
import reefrun_logo as L       # noqa: E402  (colours, gradient helpers)
import reefrun_gametitle as G  # noqa: E402  (the chosen title: the game's own logo lettering, style M3)

NAVY = L.NAVY
SCRIM = (6, 44, 64)            # deep teal-navy: a navy scrim turns lavender over the bright cyan water
TITLE = "M3"
TITLE_WIDTH = 0.58             # the title spans this share of the image width in every format

# Verified against partner.steamgames.com/doc/store/assets (+ /libraryassets, /rules) on 2026-10-16.
# Every format carries the same content (sun, shark, school, kelp, title), recomposed for its shape in Unity; the
# small capsule is deliberately simplified to the title and the shark. The title is TITLE_WIDTH of each image's width.
#  output                               plate                       size          centre-y of the title
LAYOUTS = {
    "store/main_capsule_1232x706":         ("main_capsule_1232x706",    (1232, 706),  0.58),
    "store/header_capsule_920x430":        ("header_capsule_920x430",   (920, 430),   0.60),
    "store/small_capsule_462x174":         ("small_capsule_462x174",    (462, 174),   0.50),
    "store/vertical_capsule_748x896":      ("vertical_capsule_748x896", (748, 896),   0.62),
    "library/library_capsule_600x900":     ("library_capsule_600x900",  (600, 900),   0.62),
    "library/library_header_920x430":      ("header_capsule_920x430",   (920, 430),   0.60),
    "master/master_keyart_logo_3840x2160": ("master_keyart_3840x2160",  (3840, 2160), 0.58),
}

_MASTER = None


def save(img, path):
    """Write via a temp file and swap it in, retrying: something on this machine (a previewer or indexer)
    briefly locks freshly written PNGs, and a direct overwrite then fails with Errno 22."""
    import time
    tmp = path + ".tmp.png"
    for attempt in range(20):
        try:
            img.save(tmp)
            os.replace(tmp, path)
            return
        except OSError:
            time.sleep(0.5)
    raise OSError(f"could not write {path}")


def master_title():
    """The title once, large; every format scales it down (exact width, no re-render)."""
    global _MASTER
    if _MASTER is None:
        _MASTER = G.title(1600, TITLE)
    return _MASTER


def finish(img, scrim, strength=0.40, centre_y=0.5):
    """Light grade on top of the render: a soft vignette, and (scrim=True) a soft pool behind the title."""
    w, h = img.size
    img = img.convert("RGB")
    vig = Image.new("L", (w, h), 0)
    ImageDraw.Draw(vig).ellipse((-w * 0.25, -h * 0.30, w * 1.25, h * 1.30), fill=255)
    vig = vig.filter(ImageFilter.GaussianBlur(min(w, h) * 0.22))
    img = Image.composite(img, ImageEnhance.Brightness(img).enhance(0.66), vig)
    if scrim:
        # Drawn small and scaled up (a smooth blob), so it costs nothing at 4K.
        sw = 200
        sh = max(2, round(sw * h / w))
        m = Image.new("L", (sw, sh), 0)
        ImageDraw.Draw(m).ellipse((sw * 0.16, sh * (centre_y - 0.22), sw * 0.84, sh * (centre_y + 0.22)),
                                  fill=int(255 * strength))
        m = m.filter(ImageFilter.GaussianBlur(sh * 0.10)).resize((w, h), Image.BILINEAR)
        img = Image.composite(Image.new("RGB", (w, h), SCRIM), img, m)
    return img


def place_title(img, centre_y, width_frac=TITLE_WIDTH):
    w, h = img.size
    big = master_title()
    tw = round(w * width_frac)
    logo = big.resize((tw, round(big.height * tw / big.width)), Image.LANCZOS)
    x, y = round((w - logo.width) / 2), round(h * centre_y - logo.height / 2)
    out = img.convert("RGBA")
    out.alpha_composite(logo, (x, y))
    return out.convert("RGB"), (x, y, logo.width, logo.height)


def plate(name, size):
    im = Image.open(os.path.join(PLATES, name + ".png")).convert("RGB")
    if im.size != size:
        raise SystemExit(f"plate {name} is {im.size}, expected {size}: re-render it (Capture Capsule Plates)")
    return im


def svg_layout(path, plate_rel, size, title_rel, box, centre_y):
    w, h = size
    x, y, lw, lh = box
    ry = 0.22 * h
    svg = f"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="{w}" height="{h}" viewBox="0 0 {w} {h}">
  <!-- REEF RUN capsule, layered: in-engine art plate / soft teal scrim behind the title / title (transparent PNG).
       Move or scale the title layer freely. The shipped PNG is made by compose_capsules.py (which also adds a
       light vignette); the title itself is generated by reefrun_gametitle.py (style "M3"). -->
  <defs>
    <filter id="soft" x="-50%" y="-50%" width="200%" height="200%"><feGaussianBlur stdDeviation="{h * 0.10:.1f}"/></filter>
  </defs>
  <image id="plate" x="0" y="0" width="{w}" height="{h}" xlink:href="{plate_rel}" href="{plate_rel}"/>
  <ellipse id="title-scrim" cx="{w / 2:.1f}" cy="{h * centre_y:.1f}" rx="{w * 0.34:.1f}" ry="{ry:.1f}" fill="#062c40" fill-opacity="0.40" filter="url(#soft)"/>
  <image id="title" x="{x}" y="{y}" width="{lw}" height="{lh}" xlink:href="{title_rel}" href="{title_rel}"/>
</svg>
"""
    with open(path, "w", encoding="utf-8") as f:
        f.write(svg)


def main():
    for sub in ("store", "library", "master", "editable", "previews"):
        os.makedirs(os.path.join(ROOT, sub), exist_ok=True)

    # The title alone: a high-res master, and the Steam library logo (1280 wide, at most 720 tall, transparent).
    # Their file names carry their size, which follows the title's shape: clear the previous build's copies first.
    import glob
    for old in glob.glob(os.path.join(ROOT, "master", "title_reefrun_*.png")) + glob.glob(os.path.join(ROOT, "library", "library_logo_*.png")):
        os.remove(old)
    big = master_title()
    big_name = f"title_reefrun_{big.width}x{big.height}.png"
    save(big, os.path.join(ROOT, "master", big_name))
    lib = big.resize((1280, round(big.height * 1280 / big.width)), Image.LANCZOS)
    assert lib.height <= 720, lib.size
    save(lib, os.path.join(ROOT, "library", f"library_logo_1280x{lib.height}.png"))

    # Clean art: master key art and library hero (no text allowed on the hero).
    save(finish(plate("master_keyart_3840x2160", (3840, 2160)), False), os.path.join(ROOT, "master", "master_keyart_3840x2160.png"))
    save(finish(plate("library_hero_3840x1240", (3840, 1240)), False), os.path.join(ROOT, "library", "library_hero_3840x1240.png"))

    # Store page background: the same water, quiet and dark so the store page reads on top of it.
    bg = plate("page_background_1438x810", (1438, 810))
    bg = ImageEnhance.Color(bg).enhance(0.75)
    bg = ImageEnhance.Brightness(bg).enhance(0.42)
    save(finish(bg, False), os.path.join(ROOT, "store", "page_background_1438x810.png"))

    # Titled capsules.
    for out, (plate_name, size, cy) in LAYOUTS.items():
        img = finish(plate(plate_name, size), True, centre_y=cy)
        img, box = place_title(img, cy)
        assert img.size == size, (out, img.size)
        save(img, os.path.join(ROOT, out + ".png"))
        svg_layout(os.path.join(ROOT, "editable", os.path.basename(out) + ".svg"), f"../plates/{plate_name}.png",
                   size, f"../master/{big_name}", box, cy)
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
    save(sheet, os.path.join(ROOT, "previews", "contact_sheet.png"))

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
    save(disp, os.path.join(ROOT, "previews", "display_sizes.png"))

    # Store-page mock: page background behind a centred column; main capsule in the media slot, header in the
    # sidebar slot, small capsule in a list row. Grey bars stand in for Steam's own text (none of it is ours).
    W, H = 1438, 810
    page = Image.open(os.path.join(ROOT, "store", "page_background_1438x810.png")).convert("RGB")
    d = ImageDraw.Draw(page, "RGBA")
    col_x, col_w = 150, 1138
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
    save(page, os.path.join(ROOT, "previews", "store_page_mock.png"))


if __name__ == "__main__":
    main()
    previews()
