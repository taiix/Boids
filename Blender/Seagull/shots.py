# Rendering helpers for the seagull scripts: workbench stills of the posed bird, and contact sheets.
import bpy, os, math
import numpy as np
from mathutils import Vector, Quaternion

DIRS = {"side": Vector((1, 0, 0.0)), "front": Vector((0, -1, 0.0)), "top": Vector((0.0, 0.001, 1)),
        "three": Vector((0.8, -0.8, 0.4)), "under": Vector((0.3, -0.2, -1)), "back": Vector((0.15, 1, 0.3)),
        "low": Vector((0.7, -0.6, -0.25))}

def setup(body, tex, res=(300, 240)):
    scene = bpy.context.scene
    img = bpy.data.images.load(tex)
    mat = body.data.materials[0]; mat.use_nodes = True; nt = mat.node_tree
    bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None) or nt.nodes.new('ShaderNodeBsdfPrincipled')
    tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = img
    nt.links.new(tn.outputs[0], bsdf.inputs['Base Color'])
    scene.render.engine = 'BLENDER_WORKBENCH'
    if scene.world is None:
        scene.world = bpy.data.worlds.new('ShotWorld')
    scene.world.color = (0.62, 0.66, 0.72)
    scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'TEXTURE'
    scene.render.resolution_x, scene.render.resolution_y = res
    if hasattr(scene.render.image_settings, "media_type"): scene.render.image_settings.media_type = 'IMAGE'
    scene.render.image_settings.file_format = 'PNG'
    cam = bpy.data.objects.new("ShotCam", bpy.data.cameras.new("ShotCam"))
    scene.collection.objects.link(cam); scene.camera = cam
    cam.data.clip_start, cam.data.clip_end = 0.01, 100
    cam.data.lens = 50
    return cam

def water_plane(z=0.0, size=3.0):
    """The waterline as a thin grid (wireframe bars), to judge how a floating pose sits."""
    bpy.ops.mesh.primitive_grid_add(x_subdivisions=12, y_subdivisions=12, size=size, location=(0, 0.1, z))
    pl = bpy.context.active_object
    pl.name = "Waterline"
    w = pl.modifiers.new("Wire", 'WIREFRAME')
    w.thickness = 0.004
    m = bpy.data.materials.new("WaterlineMat"); m.diffuse_color = (0.1, 0.45, 0.9, 1)
    pl.data.materials.append(m)
    return pl

ORTHO = {"side", "front", "top", "under", "back"}

def shoot(cam, path_base, views, centre=Vector((0, 0.1, 0.3)), dist=2.4, ortho_scale=1.9):
    scene = bpy.context.scene
    files = []
    for v in views:
        cam.data.type = 'ORTHO' if v in ORTHO else 'PERSP'
        cam.data.ortho_scale = ortho_scale
        d = DIRS[v].normalized()
        cam.location = centre + d * dist
        up = Vector((0, -1, 0)) if v in ("top", "under") else Vector((0, 0, 1))
        fwd = (centre - cam.location).normalized()
        rot = fwd.to_track_quat('-Z', 'Y')
        cur = rot @ Vector((0, 1, 0))
        upp = (up - fwd * up.dot(fwd)).normalized()
        ang = cur.angle(upp)
        if fwd.dot(cur.cross(upp)) < 0: ang = -ang
        cam.rotation_euler = (Quaternion(fwd, ang) @ rot).to_euler()
        scene.render.filepath = f"{path_base}_{v}.png"
        bpy.ops.render.render(write_still=True)
        files.append(scene.render.filepath)
    return files

def sheet(out, cols, files):
    imgs = []
    for f in files:
        im = bpy.data.images.load(f)
        w, h = im.size
        imgs.append(np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4))
        bpy.data.images.remove(im)
    h = max(a.shape[0] for a in imgs); w = max(a.shape[1] for a in imgs)
    rows = (len(imgs) + cols - 1) // cols
    s = np.full((rows * h, cols * w, 4), 1.0, dtype=np.float32)
    for i, a in enumerate(imgs):
        r, c = i // cols, i % cols
        y0 = (rows - 1 - r) * h
        s[y0:y0 + a.shape[0], c * w:c * w + a.shape[1]] = a
        s[y0:y0 + h, c * w:c * w + 1] = (0.25, 0.25, 0.25, 1)
        s[y0:y0 + 1, c * w:c * w + w] = (0.25, 0.25, 0.25, 1)
    im = bpy.data.images.new("sheet", cols * w, rows * h, alpha=True)
    im.pixels = s.ravel()
    im.filepath_raw = out; im.file_format = 'PNG'; im.save()
    bpy.data.images.remove(im)
    for f in files:
        try: os.remove(f)
        except OSError: pass
    return out
