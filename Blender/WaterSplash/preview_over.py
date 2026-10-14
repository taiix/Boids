"""Composite a transparent PNG over a flat colour (to judge water sprites the way they'll be seen).
    blender -b --factory-startup -P preview_over.py -- <in.png> <out.png> [r g b]"""
import bpy, sys
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst = argv[0], argv[1]
bg = np.array([float(v) for v in argv[2:5]] if len(argv) >= 5 else [0.10, 0.25, 0.38], dtype=np.float32)

img = bpy.data.images.load(src)
w, h = img.size
px = np.empty(w * h * 4, dtype=np.float32)
img.pixels.foreach_get(px)
px = px.reshape(h, w, 4)
a = px[..., 3:4]
rgb = px[..., :3] * a + bg * (1.0 - a)
out = np.concatenate([rgb, np.ones_like(a)], axis=2)
o = bpy.data.images.new("Preview", w, h, alpha=True)
o.pixels.foreach_set(out.ravel())
o.filepath_raw = dst
o.file_format = 'PNG'
o.save()
print("[preview] wrote", dst)
