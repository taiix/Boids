# Preview stills with the camera riding the bird's chest (views in the rest pose's frame: bird faces -Y, up +Z).
# blender -b --factory-startup -P preview_rel.py -- <file.fbx> <tex.png> <out_dir> <views> <Action@f,f,...> ...
import bpy, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from mathutils import Vector
from common import load, use_action

argv = sys.argv[sys.argv.index("--") + 1:]
src, tex, out, views, wants = argv[0], argv[1], argv[2], argv[3].split(","), argv[4:]
os.makedirs(out, exist_ok=True)
arm, body = load(src)
scene = bpy.context.scene
img = bpy.data.images.load(tex)
mat = body.data.materials[0]; mat.use_nodes = True; nt = mat.node_tree
bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None) or nt.nodes.new('ShaderNodeBsdfPrincipled')
tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = img
nt.links.new(tn.outputs[0], bsdf.inputs['Base Color'])
scene.render.engine = 'BLENDER_WORKBENCH'
scene.display.shading.light = 'STUDIO'; scene.display.shading.color_type = 'TEXTURE'
scene.render.resolution_x, scene.render.resolution_y = 300, 240
if hasattr(scene.render.image_settings, "media_type"): scene.render.image_settings.media_type = 'IMAGE'
scene.render.image_settings.file_format = 'PNG'
cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam")); scene.collection.objects.link(cam); scene.camera = cam
cam.data.clip_start, cam.data.clip_end = 0.01, 100
DIRS = {"side": Vector((1, 0, 0.1)), "front": Vector((0, -1, 0.15)), "top": Vector((0.0, 0.01, 1)),
        "three": Vector((0.8, -0.8, 0.45)), "under": Vector((0.25, -0.15, -1)), "back": Vector((0, 1, 0.2))}
ch_rest = arm.data.bones["Chest"].matrix_local
C = Vector((0, 0.12, 0.45))
for want in wants:
    name, _, frames = want.partition("@")
    act = next(a for a in bpy.data.actions if a.name.endswith(name))
    use_action(arm, act)
    for f in [int(x) for x in frames.split(",")]:
        scene.frame_set(f)
        M = arm.matrix_world @ arm.pose.bones["Chest"].matrix @ ch_rest.inverted()
        for v in views:
            d = DIRS[v]
            eye = M @ (C + d.normalized() * 2.0)
            at = M @ C
            up = M.to_3x3() @ (Vector((0, -1, 0)) if v in ("top", "under") else Vector((0, 0, 1)))
            cam.location = eye
            fwd = (at - eye).normalized()
            cam.rotation_euler = fwd.to_track_quat('-Z', 'Y').to_euler()
            # roll the camera so its up matches the chest's up
            q = fwd.to_track_quat('-Z', 'Y')
            cur_up = q @ Vector((0, 1, 0))
            import math
            ang = cur_up.angle(up - fwd * up.dot(fwd)) if (up - fwd * up.dot(fwd)).length > 1e-6 else 0
            sgn = 1 if fwd.dot(cur_up.cross(up)) > 0 else -1
            from mathutils import Quaternion
            cam.rotation_euler = (Quaternion(fwd, -sgn * ang) @ q).to_euler() if False else (Quaternion(fwd, sgn * ang) @ q).to_euler()
            short = name.replace("ANIM_Seagull_", "")
            scene.render.filepath = os.path.join(out, f"{short}_{f:03d}_{v}.png")
            bpy.ops.render.render(write_still=True)
