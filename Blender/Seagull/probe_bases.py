import bpy, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import load, use_action
from mathutils import Vector
argv = sys.argv[sys.argv.index("--") + 1:]
arm, body = load(argv[0])
scene = bpy.context.scene
print("rotmode", {pb.rotation_mode for pb in arm.pose.bones})
act = next(a for a in bpy.data.actions if a.name.endswith("ANIM_Seagull_Idle"))
use_action(arm, act); scene.frame_set(1)
for pb in arm.pose.bones:
    q = pb.matrix_basis.to_quaternion(); l = pb.matrix_basis.to_translation()
    e = q.to_euler()
    print(f"[basis] {pb.name:14s} rot=({e.x*57.3:6.1f},{e.y*57.3:6.1f},{e.z*57.3:6.1f}) loc={tuple(round(v,3) for v in l)} poseYaxis={tuple(round(v,2) for v in pb.matrix.to_3x3().col[1])}")
# vertex groups and body/leg heights at rest
use_action(arm, None)
for pb in arm.pose.bones: pb.matrix_basis.identity()
scene.frame_set(1)
legs = {"Thigh_R","Knee_R","Foot_R","Toe_R","Toe_R_end","Thigh_L","Knee_L","Foot_L","Toe_L","Toe_L_end"}
wings = {n for n in (b.name for b in arm.data.bones) if any(k in n for k in ("Shoulder","Elbow","Hand","Fing"))}
gi = {g.index: g.name for g in body.vertex_groups}
print("vgroups", sorted(gi.values()))
zb, zw = [], []
for v in body.data.vertices:
    ws = {gi[g.group]: g.weight for g in v.groups}
    leg = sum(w for n, w in ws.items() if n in legs)
    wing = sum(w for n, w in ws.items() if n in wings)
    if leg < 0.2 and wing < 0.2: zb.append(v.co.copy())
print("body-only verts", len(zb), "zmin", round(min(p.z for p in zb),3), "zmax", round(max(p.z for p in zb),3),
      "ymin", round(min(p.y for p in zb),3), "ymax", round(max(p.y for p in zb),3), "xmax", round(max(p.x for p in zb),3))
# body underside height along the length
for y0 in (-0.2,-0.1,0.0,0.1,0.2,0.3,0.4,0.5):
    s = [p for p in zb if abs(p.y-y0) < 0.05]
    if s: print(f"  y={y0:+.1f}: zmin={min(p.z for p in s):.3f} zmax={max(p.z for p in s):.3f} halfwidth={max(abs(p.x) for p in s):.3f}")
