"""
Render contact sheets of ingvar.glb so the rig can be judged by eye, not by trust.

  flatpak run --filesystem=/home/rohan/WubarrkCODING org.blender.Blender \
      --background --python tools/preview_ingvar.py

Workbench + CPU: no GPU, no lighting rig, and solid shading is what you actually want for
reading deformation. Writes PNGs to models/preview/.
"""

import bpy, os, math

GLB = "/home/rohan/WubarrkCODING/ValkyriesCargo/models/ingvar.glb"
OUT = "/home/rohan/WubarrkCODING/ValkyriesCargo/models/preview"
SHOTS = {"Nod": 6, "Walk": 6, "Hello": 6, "Idle": 4, "Shrug": 4, "Talk": 4}
RES = 300


def log(m):
    print(f"[preview] {m}", flush=True)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=GLB)

arm = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
log(f"armature {arm.name if arm else None}, {len(meshes)} mesh(es), {len(bpy.data.actions)} action(s)")

# frame the character from its own bounds rather than a guessed camera position
lo = [1e9] * 3
hi = [-1e9] * 3
for m in meshes:
    for c in m.bound_box:
        w = m.matrix_world @ type(m.location)(c)
        for i in range(3):
            lo[i] = min(lo[i], w[i])
            hi[i] = max(hi[i], w[i])
cx, cy = (lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2
cz, height = (lo[2] + hi[2]) / 2, hi[2] - lo[2]
log(f"bounds height {height:.2f} m, centre ({cx:.2f},{cy:.2f},{cz:.2f})")

cam_data = bpy.data.cameras.new("cam")
cam = bpy.data.objects.new("cam", cam_data)
bpy.context.scene.collection.objects.link(cam)
dist = height * 2.4
cam.location = (cx, cy - dist, cz)
cam.rotation_euler = (math.radians(90), 0, 0)
bpy.context.scene.camera = cam

sc = bpy.context.scene
sc.render.engine = "BLENDER_WORKBENCH"
sc.render.resolution_x = RES
sc.render.resolution_y = int(RES * 1.5)
sc.render.film_transparent = False
sc.display.shading.light = "STUDIO"
sc.display.shading.color_type = "TEXTURE"
os.makedirs(OUT, exist_ok=True)

if arm and arm.animation_data is None:
    arm.animation_data_create()

for name, count in SHOTS.items():
    act = bpy.data.actions.get(name)
    if not act or not arm:
        log(f"  {name}: missing")
        continue
    arm.animation_data.action = act
    # slotted actions (Blender 4.4+) need the slot assigned or the action does not evaluate
    try:
        slots = getattr(act, "slots", None)
        if slots and len(slots):
            arm.animation_data.action_slot = slots[0]
    except Exception as e:
        log(f"  {name}: slot assign skipped ({e})")
    s, e = act.frame_range
    for i in range(count):
        f = int(s + (e - s) * i / max(1, count - 1))
        sc.frame_set(f)
        sc.render.filepath = os.path.join(OUT, f"{name}_{i:02d}.png")
        bpy.ops.render.render(write_still=True)
    log(f"  {name}: {count} frames over {s:.0f}-{e:.0f}")

log("done")
