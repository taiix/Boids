# Which way the ammonite's turning / climbing takes bend it: the tentacle tips and the shell, sideways (x)
# and up (z), averaged over each take. Blender: tentacles toward -y, the shell toward +y, up +z.
# blender -b --factory-startup -P measure_turns.py -- <file.glb>
import bpy, sys
from mathutils import Vector

src = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
scene = bpy.context.scene
arm = bpy.data.objects["Root"]
tips = [b.name for b in arm.pose.bones if b.name.startswith("Tentacle") and not any(c.name.startswith("Tentacle") for c in b.children)]

for name in ("Swim", "SwimLeft", "SwimRight", "SwimUp", "SwimDown", "SwimFast", "SwimFastLeft", "SwimFastRight", "Idle", "Float"):
    act = bpy.data.actions[name]
    arm.animation_data.action = act
    f0, f1 = int(act.frame_range[0]), int(act.frame_range[1])
    tx = tz = sx = sz = 0.0
    n = 0
    for f in range(f0, f1 + 1, 3):
        scene.frame_set(f)
        t = sum((arm.matrix_world @ arm.pose.bones[b].tail for b in tips), Vector()) / len(tips)
        s = arm.matrix_world @ arm.pose.bones["Shell"].head
        tx += t.x; tz += t.z; sx += s.x; sz += s.z
        n += 1
    print(f"[{name}] tips x={tx / n:+.4f} z={tz / n:+.4f}   shell x={sx / n:+.4f} z={sz / n:+.4f}")
