# Prints what's in the ammonite .glb: objects, armature, actions, materials, size.
# blender -b --factory-startup -P inspect_ammonite.py -- <file.glb>
import bpy, sys
from mathutils import Vector

src = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
scene = bpy.context.scene

for o in scene.objects:
    line = f"[obj] {o.name} type={o.type} parent={o.parent.name if o.parent else '-'} loc={tuple(round(v, 3) for v in o.location)} rot={tuple(round(v, 3) for v in o.rotation_euler)} scale={tuple(round(v, 3) for v in o.scale)}"
    if o.type == 'MESH':
        me = o.data
        line += f" verts={len(me.vertices)} tris={sum(len(p.vertices) - 2 for p in me.polygons)} mats={[m.name for m in me.materials if m]} uvs={len(me.uv_layers)} shapekeys={len(me.shape_keys.key_blocks) if me.shape_keys else 0} mods={[m.type for m in o.modifiers]}"
        deps = bpy.context.evaluated_depsgraph_get()
        pts = [o.matrix_world @ v.co for v in o.evaluated_get(deps).to_mesh().vertices]
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        line += f" box={tuple(round(v, 3) for v in (hi - lo))} centre={tuple(round(v, 3) for v in (lo + hi) / 2)}"
    if o.type == 'ARMATURE':
        line += f" bones={len(o.data.bones)}: " + ", ".join(b.name for b in o.data.bones)
    if o.animation_data and o.animation_data.action:
        line += f" action={o.animation_data.action.name}"
    print(line)

for a in bpy.data.actions:
    print(f"[action] {a.name} frames={tuple(round(v, 1) for v in a.frame_range)} users={a.users}")
print(f"[scene] fps={scene.render.fps} frames={scene.frame_start}-{scene.frame_end}")
for m in bpy.data.materials:
    texs = []
    if m.use_nodes:
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image:
                links = [l.to_socket.name for l in n.outputs[0].links] + [l.to_node.type for l in n.outputs[0].links]
                texs.append(f"{n.image.name}->{links}")
    print(f"[material] {m.name} blend={getattr(m, 'blend_method', '?')} textures={texs}")
for img in bpy.data.images:
    print(f"[image] {img.name} size={tuple(img.size)} packed={img.packed_file is not None}")
