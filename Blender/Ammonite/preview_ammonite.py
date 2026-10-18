# Preview stills of the ammonite's takes (side and top), and where its parts are.
# blender -b --factory-startup -P preview_ammonite.py -- <file.glb> <out_dir> <Action> [<Action> ...]
import bpy, sys, os
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out, actions = argv[0], argv[1], argv[2:]
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
scene = bpy.context.scene
bpy.data.objects.remove(bpy.data.objects["Icosphere"], do_unlink=True)
arm = bpy.data.objects["Root"]
shell = bpy.data.objects["PeltocerasShellModelLOD0"]
body = bpy.data.objects["AmmoniteLOD0"]
print(f"[shell] parent_type={shell.parent_type} parent_bone={shell.parent_bone}")
for b in ("Body", "Shell", "Eye_L", "Eye_R", "UpperFunnel", "Tentacle1_5_L", "Tentacle5_8_L"):
    pb = arm.pose.bones[b]
    print(f"[bone] {b} head={tuple(round(v, 3) for v in arm.matrix_world @ pb.head)} tail={tuple(round(v, 3) for v in arm.matrix_world @ pb.tail)}")

scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x, scene.render.resolution_y = 420, 300
if hasattr(scene.render.image_settings, "media_type"):
    scene.render.image_settings.media_type = 'IMAGE'
scene.render.image_settings.file_format = 'PNG'
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
scene.collection.objects.link(cam)
scene.camera = cam

def box():
    deps = bpy.context.evaluated_depsgraph_get()
    pts = []
    for o in (body, shell):
        ev = o.evaluated_get(deps)
        m = ev.to_mesh()
        pts += [o.matrix_world @ v.co for v in m.vertices]
        ev.to_mesh_clear()
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi

for name in actions:
    act = bpy.data.actions[name]
    arm.animation_data.action = act
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    for f in (f0, (f0 + f1) // 2):
        scene.frame_set(f)
        lo, hi = box()
        c = (lo + hi) / 2
        size = max((hi - lo).length, 1e-4)
        cam.data.clip_start, cam.data.clip_end = size * 0.01, size * 100
        body_pos = arm.matrix_world @ arm.pose.bones["Body"].head
        print(f"[{name} f{f}] box={tuple(round(v, 3) for v in hi - lo)} centre={tuple(round(v, 3) for v in c)} body={tuple(round(v, 3) for v in body_pos)}")
        for view, d in (("side", Vector((1, 0, 0.15))), ("top", Vector((0.05, 0, 1))), ("front", Vector((0, -1, 0.1)))):
            cam.location = c + d.normalized() * size * 1.7
            cam.rotation_euler = (c - cam.location).to_track_quat('-Z', 'Y').to_euler()
            scene.render.filepath = os.path.join(out, f"{name}_{f:02d}_{view}.png")
            bpy.ops.render.render(write_still=True)
