"""Round 3 boards: six new letter styles on the S4 sunburst, and four sunburst backgrounds."""
import os, sys
from PIL import Image, ImageDraw, ImageEnhance, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import compose_capsules as C
import preview_shots as P
import reefrun_letters as R

font = ImageFont.truetype(os.path.join(HERE, "fonts", "BarlowCondensed-ExtraBold.ttf"), 30)
SIZE = (1232, 706)


def deep(img):
    """'Deep' grade: darker, bluer water and a harder sun, done in post (the in-engine exposure barely moved)."""
    img = ImageEnhance.Brightness(img).enhance(0.62)
    img = ImageEnhance.Contrast(img).enhance(1.35)
    r, g, b = img.split()
    img = Image.merge("RGB", (r.point(lambda v: int(v * 0.70)), g.point(lambda v: int(v * 0.90)), b.point(lambda v: min(255, int(v * 1.08)))))
    return ImageEnhance.Color(img).enhance(1.15)


BACKGROUNDS = {
    "sunburst": ("option_s4_sunburst", None, 0.42),
    "deep": ("option_s4_sunburst", deep, 0.42),
    "eclipse": ("option_s4c_eclipse", None, 0.55),
    "rising": ("option_s4d_rising", None, 0.36),
}


def compose(bg, style, scrim=True, hfrac=0.34):
    plate, grade, yc = BACKGROUNDS[bg]
    img = C.plate(plate, SIZE)
    if grade: img = grade(img)
    img = C.finish(img, None)
    if scrim and style not in ("E", "K"):
        img = P.centre_scrim(img, 0.40 if grade is None else 0.30)
    img = img.convert("RGBA")
    w, h = img.size
    lg = R.title(round(h * hfrac * (1.25 if style == "L" else 1.0)), style)
    if lg.width > w * 0.84:
        lg = R.title(round(lg.height * w * 0.84 / lg.width), style)
    img.alpha_composite(lg, ((w - lg.width) // 2, round(h * yc - lg.height / 2)))
    return img.convert("RGB")


def board(cells, name, out):
    cw, ch = 900, 516
    rows = (len(cells) + 1) // 2
    sheet = Image.new("RGB", (cw * 2 + 60, (ch + 60) * rows + 20), C.STEAM_BG)
    d = ImageDraw.Draw(sheet)
    for i, (label, im) in enumerate(cells):
        x, y = 20 + (i % 2) * (cw + 20), 20 + (i // 2) * (ch + 60)
        d.text((x, y), label, font=font, fill=(238, 246, 247))
        sheet.paste(im.resize((cw, ch), Image.LANCZOS), (x, y + 44))
        thumb = im.resize((231, 132), Image.LANCZOS)
        sheet.paste(thumb, (x + cw - 231 - 10, y + 44 + 10))
        d.rectangle((x + cw - 242, y + 53, x + cw - 10, y + 44 + 142), outline=(238, 246, 247))
    sheet.save(os.path.join(out, name))


if __name__ == "__main__":
    out = os.path.join(ROOT, "previews", "round3"); os.makedirs(out, exist_ok=True)
    letters = [("G  Cinematic", "G"), ("H  Surface split", "H"), ("I  Bitten", "I"),
               ("J  Coral stone", "J"), ("K  Plankton glow (on Deep)", "K"), ("L  Sticker", "L")]
    cells = []
    for label, s in letters:
        im = compose("deep" if s == "K" else "sunburst", s)
        im.save(os.path.join(out, f"letters_{s}.png")); cells.append((label, im))
    board(cells, "round3_letter_styles.png", out)
    bgs = [("1  Sunburst", "sunburst"), ("2  Deep", "deep"), ("3  Eclipse", "eclipse"), ("4  Rising", "rising")]
    cells = []
    for label, b in bgs:
        im = compose(b, "A")
        im.save(os.path.join(out, f"background_{b}.png")); cells.append((label + "   (title A for comparison)", im))
    board(cells, "round3_backgrounds.png", out)
    print("ok")
