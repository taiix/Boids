# Shared by the seagull scripts: load the .fbx (around Blender 5.1's FBX light bug), find the bird.
import bpy
from mathutils import Vector

def load(src):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    # Blender 5.1's FBX importer crashes on the file's light (cycles.cast_shadow is gone) - skip lights.
    from io_scene_fbx import import_fbx
    read_light = import_fbx.blen_read_light
    def safe_light(*a):
        try:
            return read_light(*a)
        except AttributeError:
            return bpy.data.lights.new("FbxLight", 'POINT')
    import_fbx.blen_read_light = safe_light
    bpy.ops.import_scene.fbx(filepath=src)
    import_fbx.blen_read_light = read_light
    # Only the bird: the file brings a light and a camera (and the camera's copies of every take).
    for o in list(bpy.data.objects):
        if o.type in ('LIGHT', 'CAMERA'):
            bpy.data.objects.remove(o, do_unlink=True)
    for a in list(bpy.data.actions):
        if a.name.startswith("Camera|") or a.users == 0:
            bpy.data.actions.remove(a)
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    body = next(o for o in bpy.data.objects if o.type == 'MESH')
    return arm, body

def use_action(arm, act):
    ad = arm.animation_data or arm.animation_data_create()
    ad.action = act
    if hasattr(ad, "action_slot") and act is not None and len(act.slots):
        ad.action_slot = act.slots[0]

def mesh_box(body):
    deps = bpy.context.evaluated_depsgraph_get()
    ev = body.evaluated_get(deps)
    m = ev.to_mesh()
    pts = [body.matrix_world @ v.co for v in m.vertices]
    ev.to_mesh_clear()
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    return lo, hi
