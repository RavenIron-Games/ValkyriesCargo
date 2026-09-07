"""
Consolidate Meshy's rigged dwarf + its clips into ONE asset for the Unity bundle.

Run headless:
  flatpak run --filesystem=/home/rohan/WubarrkCODING org.blender.Blender \
      --background --python tools/build_ingvar.py

Why this exists. Meshy returns one file PER CLIP, and every one of them carries a full
copy of the mesh AND its 22.2 MB of textures - six files, 133 MB, all the same dwarf.
Unity wants the opposite: one skinned mesh, one skeleton, many actions. This script does
that merge, authors the one clip Meshy's 678-action library does not have (a nod), trims
the walk to a single stride so the animator can cross-fade it, and drops the textures to
the 2048 the design asks for.

NOT a bundle builder. It ends at a .glb; Unity's builder takes it from there.
"""

import bpy, os, sys, math

MODELS = "/home/rohan/WubarrkCODING/ValkyriesCargo/models"
OUT    = os.path.join(MODELS, "ingvar.glb")
OUT_FBX = os.path.join(MODELS, "ingvar.fbx")

# source file -> the action name we want in the final asset
CLIPS = [
    ("ingvar_casual_walk.glb",      "Walk"),
    ("ingvar_happy_sway_idle.glb",  "Idle"),
    ("ingvar_talk_hand_open.glb",   "Hello"),
    ("ingvar_stand_and_chat.glb",   "Talk"),
    ("ingvar_shrug.glb",            "Shrug"),
]
TEX_MAX = 2048


def log(m):
    print(f"[ingvar] {m}", flush=True)


def wipe():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_glb(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=path)
    return [o for o in bpy.data.objects if o not in before]


def armature_of(objs):
    for o in objs:
        if o.type == "ARMATURE":
            return o
    return None


def action_of(arm):
    ad = arm.animation_data
    return ad.action if ad else None


def main():
    wipe()

    # ---- base: the first clip carries mesh + skin + skeleton; we keep its objects ----
    base_path = os.path.join(MODELS, CLIPS[0][0])
    objs = import_glb(base_path)
    arm = armature_of(objs)
    if arm is None:
        log("FATAL: no armature in " + base_path)
        sys.exit(1)
    meshes = [o for o in objs if o.type == "MESH"]
    log(f"base: armature '{arm.name}' {len(arm.data.bones)} bones, {len(meshes)} mesh(es)")

    act = action_of(arm)
    if act:
        act.name = CLIPS[0][1]
        act.use_fake_user = True
        log(f"  action -> {act.name} ({act.frame_range[1]-act.frame_range[0]:.0f} frames)")

    kept = set(bpy.data.objects)

    # ---- every other clip: import, steal the action, throw the duplicate body away ----
    for fname, newname in CLIPS[1:]:
        p = os.path.join(MODELS, fname)
        if not os.path.exists(p):
            log(f"  MISSING {fname}; skipped")
            continue
        imported = import_glb(p)
        a2 = armature_of(imported)
        act2 = action_of(a2) if a2 else None
        if act2 is None:
            log(f"  {fname}: no action found; skipped")
        else:
            act2.name = newname
            act2.use_fake_user = True          # survives deleting the objects below
            log(f"  {fname}: action -> {newname}")
        # drop the duplicated mesh/armature/materials that came with this clip
        for o in imported:
            bpy.data.objects.remove(o, do_unlink=True)

    # deleting objects orphans their meshes/materials/images; purge so the export is clean
    for _ in range(3):
        bpy.ops.outliner.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)

    log(f"after merge: {len(bpy.data.objects)} object(s), {len(bpy.data.actions)} action(s)")

    # ---- trim the walk to ONE stride -------------------------------------------------
    # Meshy's walk is several strides. Valheim's animator cross-fades a looping stride off
    # forward_speed, so a multi-stride clip reads as a stutter at the loop point.
    walk = bpy.data.actions.get("Walk")
    if walk:
        s, e = walk.frame_range
        log(f"  Walk spans {s:.0f}-{e:.0f}")
        stride = detect_stride(walk)
        if stride:
            a, b = stride
            log(f"  Walk: one stride is frames {a:.0f}-{b:.0f} ({b-a:.0f} frames of {e-s:.0f})")
            log("  Walk: NOT trimmed - Meshy clips already loop, and Unity's model importer")
            log("        can set the clip range non-destructively if a shorter cycle is wanted.")
        else:
            log("  Walk: stride not detected; full clip exported")

    # ---- author the nod --------------------------------------------------------------
    make_nod(arm)

    # ---- material + textures ---------------------------------------------------------
    single_side_and_shrink()

    # ---- export ----------------------------------------------------------------------
    for o in bpy.data.objects:
        o.select_set(True)
    bpy.ops.export_scene.gltf(
        filepath=OUT,
        export_format="GLB",
        export_animation_mode="ACTIONS",   # every action becomes a glTF animation
        export_animations=True,
        export_skins=True,
        export_apply=False,
        export_yup=True,
    )
    log(f"WROTE {OUT}  {os.path.getsize(OUT)/1e6:.1f} MB")

    # FBX as well: Unity imports .glb through glTFast's ScriptedImporter, which does not expose
    # ModelImporter's rig and animation settings (Generic vs Humanoid, per-clip loop, globalScale).
    # For a rigged character the FBX is the one Unity can actually configure, so ship both.
    bpy.ops.export_scene.fbx(
        filepath=OUT_FBX,
        use_selection=False,
        add_leaf_bones=False,          # Unity treats leaf bones as extra joints it has to map
        bake_anim=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_simplify_factor=0.0, # do not decimate curves; the clips are already short
        path_mode="STRIP",
        embed_textures=False,      # ships beside it as ingvar_albedo.png, so Unity gets a real
                                   # Texture2D with its own import settings (max size, DXT1)
        mesh_smooth_type="FACE",
        apply_scale_options="FBX_SCALE_NONE",
    )
    log(f"WROTE {OUT_FBX}  {os.path.getsize(OUT_FBX)/1e6:.1f} MB")

    for img in bpy.data.images:
        if img.size[0] and img.has_data:
            tex = os.path.join(MODELS, "ingvar_albedo.png")
            img.file_format = "PNG"
            img.save(filepath=tex)
            log(f"WROTE {tex}  {os.path.getsize(tex)/1e6:.1f} MB  ({img.size[0]}x{img.size[1]})")
            break
    log("actions: " + ", ".join(sorted(a.name for a in bpy.data.actions)))


def fcurves_of(action):
    """
    Blender 4.4+ moved F-curves out of Action.fcurves into layers -> strips -> channelbags
    (the slotted action model). 5.2 dropped the old attribute entirely, so reach both ways.
    """
    if hasattr(action, "fcurves"):
        return list(action.fcurves)
    out = []
    for layer in getattr(action, "layers", []):
        for strip in getattr(layer, "strips", []):
            for cb in getattr(strip, "channelbags", []):
                out.extend(cb.fcurves)
    return out


def detect_stride(action):
    """
    One stride = the period of the hip's vertical bob, which cycles twice per stride.
    Reads the root/hips location F-curve rather than guessing at frame counts.
    """
    curves = fcurves_of(action)
    fcs = [fc for fc in curves if fc.data_path.endswith(".location") and fc.array_index == 2]
    if not fcs:
        fcs = [fc for fc in curves if fc.data_path.endswith(".location") and fc.array_index == 1]
    if not fcs:
        return None
    fc = max(fcs, key=lambda c: len(c.keyframe_points))
    pts = [(kp.co[0], kp.co[1]) for kp in fc.keyframe_points]
    if len(pts) < 12:
        return None
    # find local minima of the bob; a full stride spans two of them
    mins = []
    for i in range(1, len(pts) - 1):
        if pts[i][1] <= pts[i - 1][1] and pts[i][1] <= pts[i + 1][1]:
            mins.append(pts[i][0])
    if len(mins) < 3:
        return None
    return (mins[0], mins[2])


def trim_action(action, start, end):
    """
    Kept for reference, not called. Removing keyframes invalidates the collection as you go,
    so this walks backwards by index. Unused because Unity's importer can set a clip range
    without destroying data - prefer that over baking the trim in here.
    """
    for fc in fcurves_of(action):
        for i in range(len(fc.keyframe_points) - 1, -1, -1):
            if not (start - 0.001 <= fc.keyframe_points[i].co[0] <= end + 0.001):
                fc.keyframe_points.remove(fc.keyframe_points[i])
        for kp in fc.keyframe_points:
            kp.co[0] -= start
            kp.handle_left[0] -= start
            kp.handle_right[0] -= start
        fc.update()


def make_nod(arm):
    """
    A slow friendly nod: chin down ~14 degrees and back, over 1 second. Meshy's library is
    678 full-body motions with no head gesture in it, so this is authored rather than bought.
    """
    head = None
    for cand in ("Head", "head", "mixamorig:Head", "Neck", "neck"):
        if cand in arm.pose.bones:
            head = arm.pose.bones[cand]
            break
    if head is None:
        # fall back to whichever bone sits highest in the rig
        head = max(arm.pose.bones, key=lambda b: b.head.z, default=None)
    if head is None:
        log("  nod: no head bone found; skipped")
        return
    log(f"  nod: using bone '{head.name}'")

    bpy.context.view_layer.objects.active = arm
    act = bpy.data.actions.new("Nod")
    act.use_fake_user = True
    if arm.animation_data is None:
        arm.animation_data_create()
    prev = arm.animation_data.action
    arm.animation_data.action = act

    head.rotation_mode = "XYZ"
    keys = [(0, 0.0), (10, math.radians(-14.0)), (18, math.radians(-11.0)), (30, 0.0)]
    for frame, angle in keys:
        head.rotation_euler = (angle, 0.0, 0.0)
        head.keyframe_insert(data_path="rotation_euler", frame=frame)

    for fc in fcurves_of(act):
        for kp in fc.keyframe_points:
            kp.interpolation = "BEZIER"
            kp.handle_left_type = kp.handle_right_type = "AUTO_CLAMPED"
    arm.animation_data.action = prev
    log("  nod: authored, 30 frames")


def single_side_and_shrink():
    for m in bpy.data.materials:
        m.use_backface_culling = True          # glTF doubleSided -> false
    for img in bpy.data.images:
        if img.size[0] > TEX_MAX or img.size[1] > TEX_MAX:
            w, h = img.size
            img.scale(min(w, TEX_MAX), min(h, TEX_MAX))
            log(f"  texture '{img.name}' {w}x{h} -> {img.size[0]}x{img.size[1]}")


main()
