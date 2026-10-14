"""
Re-render a baked splash (no new simulation): the 32 flipbook frames spread over a chosen stretch of the
simulation, and the held-out water below the waterline only hidden from the camera - water seen through the
splash then bends the sky instead of showing black.

    blender -b <splash_xxx.blend> -P rerender.py -- <out_atlas.png> <first_sim_frame> <last_sim_frame>
"""
import bpy, os, sys
import numpy as np

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.abspath(argv[0])
FIRST, LAST = int(argv[1]), int(argv[2])
COLS, ROWS, FW, FH = 4, 8, 512, 256
scene = bpy.context.scene

# The cut below the waterline: camera rays only.
for mat in bpy.data.materials:
    if not mat.use_nodes:
        continue
    nt = mat.node_tree
    mix = next((n for n in nt.nodes if n.type == 'MIX_SHADER'), None)
    if mix is None or not mix.inputs["Fac"].is_linked:
        continue
    src = mix.inputs["Fac"].links[0].from_socket
    if src.node.type == 'MATH':
        continue                                            # already done
    path = nt.nodes.new("ShaderNodeLightPath")
    inv = nt.nodes.new("ShaderNodeMath")
    inv.operation = 'SUBTRACT'
    inv.inputs[0].default_value = 1.0
    nt.links.new(path.outputs["Is Camera Ray"], inv.inputs[1])
    # fac = 1 (show the water) above the cut, or for any ray that isn't the camera's.
    mx = nt.nodes.new("ShaderNodeMath")
    mx.operation = 'MAXIMUM'
    nt.links.new(src, mx.inputs[0])
    nt.links.new(inv.outputs["Value"], mx.inputs[1])
    nt.links.new(mx.outputs["Value"], mix.inputs["Fac"])
    print("[rerender] camera-only hold-out on", mat.name)

frames_dir = os.path.join(os.path.dirname(OUT), "frames")
os.makedirs(frames_dir, exist_ok=True)
paths = []
n = COLS * ROWS
for i in range(n):
    f = FIRST + round(i * (LAST - FIRST) / (n - 1))
    scene.frame_set(f)
    p = os.path.join(frames_dir, f"frame_{i:02d}.png")
    scene.render.filepath = p
    bpy.ops.render.render(write_still=True)
    paths.append(p)

atlas = np.zeros((ROWS * FH, COLS * FW, 4), dtype=np.float32)
for i, p in enumerate(paths):
    img = bpy.data.images.load(p)
    px = np.empty(FW * FH * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    c, r = i % COLS, i // COLS
    y0 = (ROWS - 1 - r) * FH
    atlas[y0:y0 + FH, c * FW:(c + 1) * FW] = px.reshape(FH, FW, 4)
    bpy.data.images.remove(img)
out = bpy.data.images.new("Atlas", COLS * FW, ROWS * FH, alpha=True)
out.pixels.foreach_set(atlas.ravel())
out.filepath_raw = OUT
out.file_format = 'PNG'
out.save()
bpy.ops.wm.save_mainfile()
print("[rerender] wrote", OUT)
