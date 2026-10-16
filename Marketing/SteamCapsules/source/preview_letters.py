"""Letter designs on the chosen shot (S4 Sunburst), title in the middle."""
import os, sys
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import compose_capsules as C
import preview_shots as P
import reefrun_logo3d as T

PLATE = "option_s4_sunburst"
STYLES = [("A", "A  Extruded"), ("B", "B  Wet glass"), ("C", "C  Stacked"),
          ("D", "D  Low angle"), ("E", "E  Backlit silhouette"), ("F", "F  Sunlit orange")]
font = ImageFont.truetype(os.path.join(HERE, "fonts", "BarlowCondensed-ExtraBold.ttf"), 30)
small = ImageFont.truetype(os.path.join(HERE, "fonts", "BarlowCondensed-ExtraBold.ttf"), 18)


def titled(style):
    base = C.finish(C.plate(PLATE, (1232, 706)), None)
    if style != "E":                       # dark letters want the light behind them, not a scrim
        base = P.centre_scrim(base)
    img = base.convert("RGBA")
    w, h = img.size
    lg = T.logo3d(round(h * (0.44 if style == "C" else 0.34)), style)
    if lg.width > w * 0.86:
        lg = T.logo3d(round(lg.height * w * 0.86 / lg.width), style)
    img.alpha_composite(lg, ((w - lg.width) // 2, round(h * 0.42 - lg.height / 2)))
    return img.convert("RGB")


if __name__ == "__main__":
    out = os.path.join(ROOT, "previews", "letter_options"); os.makedirs(out, exist_ok=True)
    cw, ch = 900, 516
    sheet = Image.new("RGB", (cw * 2 + 60, (ch + 60) * 3 + 20), C.STEAM_BG)
    d = ImageDraw.Draw(sheet)
    for i, (s, label) in enumerate(STYLES):
        im = titled(s)
        im.save(os.path.join(out, f"s4_letters_{s}.png"))
        x, y = 20 + (i % 2) * (cw + 20), 20 + (i // 2) * (ch + 60)
        d.text((x, y), label, font=font, fill=(238, 246, 247))
        sheet.paste(im.resize((cw, ch), Image.LANCZOS), (x, y + 44))
        thumb = im.resize((231, 132), Image.LANCZOS)       # roughly a capsule at small display size
        sheet.paste(thumb, (x + cw - 231 - 10, y + 44 + 10))
        d.rectangle((x + cw - 231 - 11, y + 44 + 9, x + cw - 10, y + 44 + 132 + 10), outline=(238, 246, 247))
    sheet.save(os.path.join(out, "s4_letter_options.png"))
    print("ok")
