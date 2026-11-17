# Preview stills of the seagull's takes from the side, front and top.
# blender -b --factory-startup -P preview_seagull.py -- <file.fbx> <tex.png> <out_dir> <Action|REST>[@frame,frame] ...
import bpy, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Vector
from common import load, use_action, mesh_box

argv = sys.argv[sys.argv.index("--") + 1:]
src, tex, out, wants = argv[0], argv[1], argv[2], argv[3:]
os.makedirs(out, exist_ok=True)
arm, body = load(src)
scene = bpy.context.scene

# The diffuse texture on the bird's material, so the workbench draws it.
img = bpy.data.images.load(tex)
mat = body.data.materials[0]
mat.use_nodes = True
nt = mat.node_tree
bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None) or nt.nodes.new('ShaderNodeBsdfPrincipled')
tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = img
nt.links.new(tn.outputs[0], bsdf.inputs['Base Color'])

scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x, scene.render.resolution_y = 360, 280
if hasattr(scene.render.image_settings, "media_type"):
    scene.render.image_settings.media_type = 'IMAGE'
scene.render.image_settings.file_format = 'PNG'
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
scene.collection.objects.link(cam)
scene.camera = cam
VIEWS = [v for v in os.environ.get("VIEWS", "side,front,top").split(",")]
DIRS = {"side": Vector((1, 0, 0.12)), "front": Vector((0, -1, 0.12)), "top": Vector((0.02, 0.0, 1)),
        "three": Vector((0.8, -0.8, 0.45)), "under": Vector((0.3, -0.2, -1))}
SIZE = float(os.environ.get("FRAME_SIZE", "0"))

for want in wants:
    name, _, frames = want.partition("@")
    if name == "REST":
        use_action(arm, None)
        for pb in arm.pose.bones:
            pb.matrix_basis.identity()
        fl = [1]
    else:
        act = next(a for a in bpy.data.actions if a.name.endswith(name))
        use_action(arm, act)
        f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
        fl = [int(f) for f in frames.split(",")] if frames else [f0, (f0 + f1) // 2, f1]
    for f in fl:
        scene.frame_set(f)
        lo, hi = mesh_box(body)
        c = (lo + hi) / 2
        size = SIZE or max((hi - lo).length, 1e-4)
        root = arm.matrix_world @ arm.pose.bones["Root"].head
        print(f"[{name} f{f}] lo={tuple(round(v, 3) for v in lo)} hi={tuple(round(v, 3) for v in hi)} root={tuple(round(v, 3) for v in root)}")
        cam.data.clip_start, cam.data.clip_end = 0.01, 100
        for view in VIEWS:
            d = DIRS[view]
            cc = c if not SIZE else Vector((0, 0.1, 0.5))
            cam.location = cc + d.normalized() * size * 1.5
            cam.rotation_euler = (cc - cam.location).to_track_quat('-Z', 'Y').to_euler()
            short = name.replace("ANIM_Seagull_", "")
            scene.render.filepath = os.path.join(out, f"{short}_{f:03d}_{view}.png")
            bpy.ops.render.render(write_still=True)
