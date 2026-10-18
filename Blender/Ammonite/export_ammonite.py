"""
The ammonite (animated-ammonite-marine-creature-asset, a .glb), exported for Unity.

    blender -b --factory-startup -P export_ammonite.py -- <Untitled.glb> <out_dir>

Writes (the .glb is only read):
  Ammonite.fbx           the rigged ammonite, every take (Swim, SwimFast, the Left/Right/Up/Down turns of both,
                         Idle, Float, Attack, Hit, ...), its shell riding the Shell bone, thinned to a game-sized mesh
  Textures/*.png         its four textures (packed in the .glb), at 1024

It swims shell first (jetting backwards, as cephalopods do): its tentacles trail. Blender: tentacles toward -y,
the shell toward +y, up +z. Its takes are named from its own point of view (facing its tentacles), so SwimLeft
turns it toward +x - Unity's builder measures the turns rather than trusting the names.
"""
import bpy, os, sys

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = argv[0], os.path.abspath(argv[1])
TEX = os.path.join(OUT, "Textures")
os.makedirs(TEX, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
scene = bpy.context.scene
scene.render.fps = 24

# Only the ammonite: a stray icosphere came along in the .glb.
for o in list(bpy.data.objects):
    if o.name == "Icosphere":
        bpy.data.objects.remove(o, do_unlink=True)
holder = bpy.data.objects["PeltocerasRockTwoLODs"]
arm = bpy.data.objects["Root"]
body = bpy.data.objects["AmmoniteLOD0"]
shell = bpy.data.objects["PeltocerasShellModelLOD0"]

# The armature to the top, keeping its size (the .glb's holder empty scales it to 1/100: the source was in
# centimetres). Its own scale stays on its node - applying it wouldn't rescale the bones' animated moves.
mw = arm.matrix_world.copy()
arm.parent = None
arm.matrix_world = mw
bpy.data.objects.remove(holder, do_unlink=True)

# The shell is rigid (on the Shell bone) and three times the body's triangles: thin it.
dec = shell.modifiers.new("Thin", 'DECIMATE')
dec.ratio = 0.3
bpy.context.view_layer.objects.active = shell
shell.select_set(True)
bpy.ops.object.modifier_apply(modifier=dec.name)
print(f"[ammonite] shell: {sum(len(p.vertices) - 2 for p in shell.data.polygons)} triangles, body "
      f"{sum(len(p.vertices) - 2 for p in body.data.polygons)}")

# Textures out of the .glb, at 1024.
for img in bpy.data.images:
    if img.size[0] == 0:
        continue
    img.scale(1024, 1024)
    img.filepath_raw = os.path.join(TEX, img.name + ".png")
    img.file_format = 'PNG'
    img.save()
    print(f"[ammonite] texture {img.name}.png")

# Every take on the armature, none playing through the NLA.
if arm.animation_data:
    for t in arm.animation_data.nla_tracks:
        t.mute = True
    arm.animation_data.action = bpy.data.actions["Swim"]
for a in bpy.data.actions:
    a.use_fake_user = True
scene.frame_start, scene.frame_end = 1, 31

for o in bpy.data.objects:
    o.select_set(o in (arm, body, shell))
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(
    filepath=os.path.join(OUT, "Ammonite.fbx"), use_selection=True, object_types={'ARMATURE', 'MESH'},
    use_mesh_modifiers=True, mesh_smooth_type='FACE', use_armature_deform_only=False,
    axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
    bake_space_transform=False, add_leaf_bones=False, path_mode='STRIP',
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.5)
print(f"[ammonite] wrote Ammonite.fbx with {len(bpy.data.actions)} takes")
