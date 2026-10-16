"""Title options board: each 3D style centred on the real capsule art, at the size Steam shows it."""
import os, sys
from PIL import Image, ImageDraw, ImageFont
HERE = os.path.dirname(os.path.abspath(__file__)); ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import compose_capsules as C
import reefrun_logo3d as T

NAMES = {"A": "A  Extruded", "B": "B  Wet glass (game caustics)", "C": "C  Stacked", "D": "D  Low angle"}
font = ImageFont.truetype(os.path.join(HERE, "fonts", "BarlowCondensed-ExtraBold.ttf"), 30)
small_font = ImageFont.truetype(os.path.join(HERE, "fonts", "BarlowCondensed-ExtraBold.ttf"), 20)


def capsule(plate, size, style, hfrac, yfrac, scrim):
    img = C.finish(C.plate(plate, size), scrim).convert("RGBA")
    w, h = size
    lg = T.logo3d(round(h * hfrac), style)
    if lg.width > w * 0.9:
        lg = T.logo3d(round(h * hfrac * w * 0.9 / lg.width), style)
    img.alpha_composite(lg, ((w - lg.width) // 2, round(h * yfrac)))
    return img.convert("RGB")


def board(style):
    stacked = style == "C"
    main = capsule("main_capsule_1232x706", (1232, 706), style, 0.42 if stacked else 0.32, 0.04, "top")
    vert = capsule("vertical_capsule_748x896", (748, 896), style, 0.36 if stacked else 0.22, 0.04, "top")
    small = capsule("small_capsule_462x174", (462, 174), style, 0.92 if stacked else 0.62, 0.04, "left")
    B = Image.new("RGB", (1100, 560), C.STEAM_BG)
    d = ImageDraw.Draw(B)
    d.text((24, 14), NAMES[style], font=font, fill=(238, 246, 247))
    B.paste(main.resize((616, 353), Image.LANCZOS), (24, 64))
    B.paste(vert.resize((374, 448), Image.LANCZOS), (24 + 616 + 40, 64))
    B.paste(small.resize((231, 87), Image.LANCZOS), (24, 64 + 353 + 40))
    d.text((24 + 231 + 20, 64 + 353 + 40 + 30), "main / vertical / small capsule at Steam display size", font=small_font, fill=(143, 176, 187))
    return B, main


if __name__ == "__main__":
    out = os.path.join(ROOT, "previews", "title_options")
    os.makedirs(out, exist_ok=True)
    boards = []
    for s in "ABCD":
        b, main = board(s)
        b.save(os.path.join(out, f"title_option_{s}.png"))
        main.save(os.path.join(out, f"title_option_{s}_main_capsule_full.png"))
        boards.append(b)
    sheet = Image.new("RGB", (2200, 1120), C.STEAM_BG)
    for i, b in enumerate(boards):
        sheet.paste(b, ((i % 2) * 1100, (i // 2) * 560))
    sheet.save(os.path.join(out, "title_options_all.png"))
    print("ok")
