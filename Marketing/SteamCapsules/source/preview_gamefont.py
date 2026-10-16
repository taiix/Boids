"""Font round: the title in the game's own logo lettering (M0-M3) on the Eclipse main capsule, next to the
in-game logo (captured from the running main menu) and the previous capsule title (HI)."""
import os, sys
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import compose_capsules as C
import reefrun_gametitle as G
import reefrun_letters as R

font = ImageFont.truetype(os.path.join(HERE, "fonts", "BarlowCondensed-ExtraBold.ttf"), 30)
SIZE, CY = (1232, 706), 0.56


def capsule(make):
    img = C.finish(C.plate("main_capsule_1232x706", SIZE), True, centre_y=CY).convert("RGBA")
    w, h = img.size
    lg = make(round(h * 0.30))
    if lg.width > w * 0.66:                   # same visual width for every variant
        lg = make(round(lg.height * w * 0.66 / lg.width))
    img.alpha_composite(lg, ((w - lg.width) // 2, round(h * CY - lg.height / 2)))
    return img.convert("RGB")


if __name__ == "__main__":
    out = os.path.join(ROOT, "previews", "exploration", "game_font"); os.makedirs(out, exist_ok=True)
    ref_path = sys.argv[1] if len(sys.argv) > 1 else None
    cells = [("M0  Game logo, exactly", lambda h: G.title(h, "M0")),
             ("M1  Game logo + depth", lambda h: G.title(h, "M1")),
             ("M2  Game logo + bite + waterline", lambda h: G.title(h, "M2")),
             ("M3  Game logo + depth + bite + waterline", lambda h: G.title(h, "M3")),
             ("Previous capsule title (HI)", lambda h: R.title(h, "HI"))]
    cw, ch = 900, 516
    top = 300
    sheet = Image.new("RGB", (cw * 2 + 60, top + (ch + 60) * 3 + 20), C.STEAM_BG)
    d = ImageDraw.Draw(sheet)
    d.text((20, 16), "In the game (main menu, captured live) - Barlow Condensed ExtraBold Italic, flat, tight, soft shadow",
           font=font, fill=(238, 246, 247))
    if ref_path:
        ref = Image.open(ref_path).convert("RGB")
        ref.thumbnail((cw * 2 + 20, top - 70), Image.LANCZOS)
        sheet.paste(ref, (20, 60))
    for i, (label, make) in enumerate(cells):
        im = capsule(make)
        im.save(os.path.join(out, f"gamefont_{label.split()[0]}.png"))
        x, y = 20 + (i % 2) * (cw + 20), top + 20 + (i // 2) * (ch + 60)
        d.text((x, y), label, font=font, fill=(238, 246, 247))
        sheet.paste(im.resize((cw, ch), Image.LANCZOS), (x, y + 44))
        thumb = im.resize((231, 132), Image.LANCZOS)
        sheet.paste(thumb, (x + cw - 241, y + 54))
        d.rectangle((x + cw - 242, y + 53, x + cw - 10, y + 186), outline=(238, 246, 247))
    sheet.save(os.path.join(out, "game_font_options.png"))
    print("ok")
