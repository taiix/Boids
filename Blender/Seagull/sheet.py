# Tiles PNG stills into one contact sheet (rows of `cols`), with numpy inside Blender.
# blender -b --factory-startup -P sheet.py -- <out.png> <cols> <in.png> [<in.png> ...]
import bpy, sys
import numpy as np
argv = sys.argv[sys.argv.index("--") + 1:]
out, cols, files = argv[0], int(argv[1]), argv[2:]
imgs = []
for f in files:
    im = bpy.data.images.load(f)
    w, h = im.size
    a = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)
    imgs.append(a)
h = max(a.shape[0] for a in imgs); w = max(a.shape[1] for a in imgs)
rows = (len(imgs) + cols - 1) // cols
sheet = np.ones((rows * h, cols * w, 4), dtype=np.float32)
for i, a in enumerate(imgs):
    r, c = i // cols, i % cols
    # Blender's pixel rows run bottom-up: the first image goes in the top row.
    y0 = (rows - 1 - r) * h
    sheet[y0:y0 + a.shape[0], c * w:c * w + a.shape[1]] = a
    sheet[y0:y0 + h, c * w:c * w + 1] = (0.2, 0.2, 0.2, 1)
out_img = bpy.data.images.new("sheet", cols * w, rows * h, alpha=True)
out_img.pixels = sheet.ravel()
out_img.filepath_raw = out
out_img.file_format = 'PNG'
out_img.save()
print("[sheet]", out, cols * w, rows * h)
