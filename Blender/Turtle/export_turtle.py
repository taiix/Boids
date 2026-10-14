"""
The loggerhead turtle for the turtle rescue task, exported for Unity, and the fishing net it's tangled in.

    blender -b --disable-autoexec <Loggerhead_turtle.blend> -P export_turtle.py -- <out_dir>

Writes (the .blend itself is never saved):
  LoggerheadTurtle.fbx  the rigged turtle (deform bones only) with its swim cycle baked in
  TurtleNet.fbx         a net draped over its shell by a cloth simulation - its four corners pinned below the
                        body, the way the ropes pull it - as cords (wireframe), with empties Corner_0..3 where
                        the ropes tie on: 0 by the head, 1 its left (+x), 2 the tail, 3 its right

Both in the same space (the turtle's, at the start of its swim cycle), so they line up in Unity when placed
together. Blender: head toward -y, up +z.
"""
import bpy, bmesh, math, os, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = os.path.abspath(argv[0])
os.makedirs(OUT, exist_ok=True)

scene = bpy.context.scene
arm = bpy.data.objects["Armature"]
body = bpy.data.objects["Loggerhead_LP"]

# ------------------------------------------------------------------ the turtle

# Only the turtle: the scene's camera, light and rig helpers stay behind.
for o in list(bpy.data.objects):
    if o not in (arm, body):
        bpy.data.objects.remove(o, do_unlink=True)

# The swim cycle as the animation (the old NLA takes are muted already; make sure).
arm.animation_data.action = bpy.data.actions["Swim_cycle"]
for t in arm.animation_data.nla_tracks:
    t.mute = True
scene.frame_start, scene.frame_end = 0, 50
scene.frame_set(0)

for o in bpy.data.objects:
    o.select_set(o in (arm, body))
bpy.context.view_layer.objects.active = arm

FBX = dict(axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
           bake_space_transform=False, add_leaf_bones=False, path_mode='STRIP')
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT, "LoggerheadTurtle.fbx"), use_selection=True, object_types={'ARMATURE', 'MESH'},
    use_mesh_modifiers=True, mesh_smooth_type='FACE', use_armature_deform_only=True,
    bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.5, **FBX)
print("[turtle] wrote LoggerheadTurtle.fbx")

# ------------------------------------------------------------------ the net

# The turtle as it is at the start of the swim cycle, as something solid for the net to fall on.
deps = bpy.context.evaluated_depsgraph_get()
shell_mesh = bpy.data.meshes.new_from_object(body.evaluated_get(deps))
shell = bpy.data.objects.new("Shell", shell_mesh)
shell.matrix_world = body.matrix_world.copy()
scene.collection.objects.link(shell)
col = shell.modifiers.new("Collision", 'COLLISION')
shell.collision.thickness_outer = 0.012
shell.collision.cloth_friction = 20.0
top = max((shell.matrix_world @ v.co).z for v in shell_mesh.vertices)

# A square of net 1.6 m across, turned 45 degrees so its corners point head, left, tail and right.
SIZE, CELLS = 1.6, 36
bpy.ops.mesh.primitive_grid_add(x_subdivisions=CELLS, y_subdivisions=CELLS, size=SIZE, location=(0, 0, top + 0.12))
net = bpy.context.object
net.name = "Net"
net.rotation_euler = (0, 0, math.radians(45))
bpy.ops.object.transform_apply(rotation=True)

# Its corners, pulled down below the body where the ropes will tie on (pinned: the cloth won't move them).
CORNERS = [Vector((0.0, -0.66, 0.02)), Vector((0.64, 0.0, 0.02)), Vector((0.0, 0.62, 0.02)), Vector((-0.64, 0.0, 0.02))]
me = net.data
corner_ids = []
for target in CORNERS:
    direction = Vector((target.x, target.y, 0)).normalized()
    best = max(me.vertices, key=lambda v: v.co.x * direction.x + v.co.y * direction.y)
    corner_ids.append(best.index)
pin = net.vertex_groups.new(name="Pin")
for i, target in zip(corner_ids, CORNERS):
    me.vertices[i].co = net.matrix_world.inverted() @ target
    pin.add([i], 1.0, 'REPLACE')

cloth = net.modifiers.new("Cloth", 'CLOTH')
cs = cloth.settings
cs.quality = 8
cs.mass = 0.03
cs.air_damping = 2.0
cs.tension_stiffness = 12.0
cs.compression_stiffness = 12.0
cs.shear_stiffness = 2.0
cs.bending_stiffness = 0.02             # a loose net, not a sheet
cs.vertex_group_mass = "Pin"
cloth.collision_settings.distance_min = 0.006
cloth.collision_settings.collision_quality = 4
cloth.point_cache.frame_start = 1
cloth.point_cache.frame_end = 90
for f in range(1, 91):
    scene.frame_set(f)
deps = bpy.context.evaluated_depsgraph_get()
draped = bpy.data.meshes.new_from_object(net.evaluated_get(deps))
corner_points = [net.matrix_world @ draped.vertices[i].co for i in corner_ids]

# Cords: every edge of the draped net as a strand about 7 mm thick.
cords = bpy.data.objects.new("TurtleNet", draped)
cords.matrix_world = net.matrix_world.copy()
scene.collection.objects.link(cords)
wire = cords.modifiers.new("Cords", 'WIREFRAME')
wire.thickness = 0.007
wire.use_even_offset = False         # even thickness throws spikes off sharp corners
wire.use_replace = True
wire.use_boundary = True
deps = bpy.context.evaluated_depsgraph_get()
cord_mesh = bpy.data.meshes.new_from_object(cords.evaluated_get(deps))
cords.modifiers.clear()
cords.data = cord_mesh
for p in cord_mesh.polygons:
    p.use_smooth = True
print(f"[turtle] net: {len(cord_mesh.polygons)} faces, corners {[tuple(round(c, 3) for c in p) for p in corner_points]}")

for o in list(bpy.data.objects):
    o.select_set(False)
cords.select_set(True)
for i, p in enumerate(corner_points):
    e = bpy.data.objects.new(f"Corner_{i}", None)
    e.location = p
    scene.collection.objects.link(e)
    e.select_set(True)
bpy.context.view_layer.objects.active = cords
scene.frame_set(0)
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT, "TurtleNet.fbx"), use_selection=True, object_types={'MESH', 'EMPTY'},
    use_mesh_modifiers=False, mesh_smooth_type='FACE', bake_anim=False, **FBX)
print("[turtle] wrote TurtleNet.fbx")
