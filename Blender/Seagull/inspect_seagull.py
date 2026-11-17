# Prints what's in the seagull .fbx: objects, armature (bones with head/tail/parent), actions, materials, size.
# blender -b --factory-startup -P inspect_seagull.py -- <file.fbx>
import bpy, sys
from mathutils import Vector

src = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
# Blender 5.1's FBX importer crashes on the file's light (cycles.cast_shadow is gone) - skip lights.
from io_scene_fbx import import_fbx
_read_light = import_fbx.blen_read_light
def _safe_light(*a):
    try: return _read_light(*a)
    except AttributeError: return bpy.data.lights.new("FbxLight", 'POINT')
import_fbx.blen_read_light = _safe_light
bpy.ops.import_scene.fbx(filepath=src)
scene = bpy.context.scene
deps = bpy.context.evaluated_depsgraph_get()

for o in scene.objects:
    line = f"[obj] {o.name} type={o.type} parent={o.parent.name if o.parent else '-'} pbone={o.parent_bone} loc={tuple(round(v, 3) for v in o.location)} rot={tuple(round(v, 3) for v in o.rotation_euler)} scale={tuple(round(v, 4) for v in o.scale)}"
    if o.type == 'MESH':
        me = o.data
        line += f" verts={len(me.vertices)} tris={sum(len(p.vertices) - 2 for p in me.polygons)} mats={[m.name for m in me.materials if m]} uvs={len(me.uv_layers)} vgroups={len(o.vertex_groups)} shapekeys={len(me.shape_keys.key_blocks) if me.shape_keys else 0} mods={[m.type for m in o.modifiers]}"
        pts = [o.matrix_world @ v.co for v in o.evaluated_get(deps).to_mesh().vertices]
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        line += f" lo={tuple(round(v, 3) for v in lo)} hi={tuple(round(v, 3) for v in hi)}"
    print(line)
    if o.type == 'ARMATURE':
        mw = o.matrix_world
        for b in o.data.bones:
            h = mw @ b.head_local; t = mw @ b.tail_local
            print(f"   [bone] {b.name} parent={b.parent.name if b.parent else '-'} head={tuple(round(v, 3) for v in h)} tail={tuple(round(v, 3) for v in t)} deform={b.use_deform} roll_axisZ={tuple(round(v,2) for v in (mw.to_3x3() @ b.matrix_local.to_3x3().col[2]).normalized())}")
        if o.animation_data:
            print(f"   anim action={o.animation_data.action.name if o.animation_data.action else None} nla={[t.name for t in o.animation_data.nla_tracks]}")

for a in bpy.data.actions:
    groups = sorted({fc.data_path.split('"')[1] for fc in a.fcurves if '"' in fc.data_path}) if hasattr(a, 'fcurves') else []
    print(f"[action] {a.name} frames={tuple(round(v, 1) for v in a.frame_range)} users={a.users} bones={len(groups)}")
print(f"[scene] fps={scene.render.fps} frames={scene.frame_start}-{scene.frame_end} unit_scale={scene.unit_settings.scale_length}")
for m in bpy.data.materials:
    texs = []
    if m.use_nodes:
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image:
                texs.append(f"{n.image.name}->{[l.to_socket.name for l in n.outputs[0].links]}")
    print(f"[material] {m.name} textures={texs}")
