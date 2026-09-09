using System;
using System.Collections.Generic;
using UnityEngine;
using RavenIron.ValkyriesCargo.Config;
using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo.Client
{
    /// <summary>
    /// The backpack add-on's body half (the owner's decision 2026-09-08: Smoothbrain's Backpacks). A server
    /// running the backpack mod sells from a bigger shelf (`Core/Shelf.Scaled`, `Server/BackpackMod`); this
    /// hangs the mod's OWN pack on Ingvar, so on those servers he wears exactly what the players wear, and on
    /// every other server he wears nothing and this file does nothing at all.
    ///
    /// Why it is a prop and not equipment. `bp_explorer` (one prefab, one bundle, registered into ObjectDB by
    /// the mod's own ItemManager) carries its geometry under `attach_skin/Mesh` as `SkinnedMeshRenderer`s
    /// rigged to VALHEIM's humanoid skeleton. Ingvar's rig is his own - 24 joints, `Hips -> Spine -> Spine01
    /// -> Spine02 -> neck -> Head` - so vanilla's own way of wearing a pack is closed twice over: equipping it
    /// would have `VisEquipment` bind it to the DVERGER chassis, whose renderers `BodyLoader.HideStandIn`
    /// switches off, and even visible it would follow the chassis's bones and not the body anyone can see. So
    /// the skinned parts are BAKED to static meshes once, at their bind pose (a pack does not deform), and
    /// hung on a named bone of Ingvar's own rig. Where exactly is `Core/Knapsack`'s, and every number is a
    /// client knob because no off-game check can tell you a pack sits right on a shoulder.
    ///
    /// It attaches UNDER the Ingvar body object, which is what keeps it visible: `HideStandIn` switches off
    /// every renderer under the character except those below the body's own transform, on the 2 s cadence as
    /// well as at the attach.
    ///
    /// Nothing here is load-bearing. Every failure - no mod, no prefab, a renamed child, a rig with no spine -
    /// leaves Ingvar exactly as he is today and says so once.
    /// </summary>
    internal static class BackpackProp
    {
        /// <summary>The child this file adds under the body; also how a re-attach finds its own last one.</summary>
        public const string ChildName = "IngvarPack";

        /// <summary>Where Smoothbrain's prefab keeps its geometry. A rename upstream turns the add-on off, loudly.</summary>
        public const string MeshPath = "attach_skin/Mesh";

        /// <summary>What the last attach found, for `cargo body` and the boot-time question "why is he bare?".</summary>
        public static string Detail { get; private set; } = "not looked for yet";

        /// <summary>True when the last attach actually put a pack on him.</summary>
        public static bool Worn { get; private set; }

        private static int _throws;

        /// <summary>
        /// Hang the pack on <paramref name="bodyRoot"/> (the `IngvarBody` object, NOT the character), or leave
        /// him bare and record why. Safe to call again: the previous pack is removed first, so a re-attach
        /// after a config edit replaces rather than stacks.
        /// </summary>
        public static bool Attach(Transform bodyRoot)
        {
            Worn = false;
            if (bodyRoot == null) return Bare("no body to hang it on");

            try
            {
                Transform had = bodyRoot.Find(ChildName);
                if (had != null) UnityEngine.Object.Destroy(had.gameObject);

                if (ModConfig.BackpackOnIngvar != null && !ModConfig.BackpackOnIngvar.Value)
                {
                    return Bare("switched off (Client.BackpackOnIngvar)");
                }

                string prefabName = Name();
                GameObject source = FindPrefab(prefabName);
                if (source == null)
                {
                    // The honest reading of a miss: the backpack mod is not on THIS machine. The prefab is in
                    // ObjectDB only because the mod put it there, so its absence is the detection - a stronger
                    // test than the chainloader, which can say "loaded" for a mod that failed to register.
                    return Bare("no prefab '" + prefabName + "' in ObjectDB (the backpack mod is not loaded here)");
                }

                Transform meshRoot = source.transform.Find(MeshPath);
                if (meshRoot == null)
                {
                    return Bare("prefab '" + prefabName + "' has no '" + MeshPath + "' (the mod's layout has moved); it has " +
                                Children(source.transform));
                }

                // The rig, by name, as `Core/Knapsack` decides it.
                List<string> boneNames = new List<string>();
                Transform[] all = bodyRoot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++) boneNames.Add(all[i].name);

                string wanted = ModConfig.BackpackBone != null ? ModConfig.BackpackBone.Value : Knapsack.DefaultBone;
                string boneName = Knapsack.ResolveBone(boneNames, wanted);
                if (boneName.Length == 0)
                {
                    return Bare("this rig has no bone named '" + wanted + "' and none of the fallbacks " +
                                string.Join("/", Knapsack.Fallbacks) + "; the rig has " + Rig(all));
                }

                Transform bone = null;
                for (int i = 0; i < all.Length; i++)
                    if (all[i].name == boneName) { bone = all[i]; break; }
                if (bone == null) return Bare("bone '" + boneName + "' vanished between the read and the attach");

                GameObject holder = Bake(meshRoot, prefabName);
                if (holder == null) return false;                     // Bake set Detail

                holder.name = ChildName;
                holder.transform.SetParent(bone, false);
                Place(holder.transform, bone);

                Worn = true;
                _said = null;                                  // a later failure is news again
                Detail = "'" + prefabName + "' on " + boneName + ", " + _parts + " part(s), " + _triangles + " tris" +
                         "; bone scale " + _boneScale.ToString("0.####") +
                         (Math.Abs(_boneScale - 1f) > 0.0001f ? " (undone: the rig is authored at that scale)" : "") +
                         ", world scale " + holder.transform.lossyScale.x.ToString("0.###") +
                         ", offset " + Words(holder.transform.localPosition * _boneScale) + " m" +
                         ", turned " + Words(holder.transform.localEulerAngles) +
                         " (Client.BackpackOffset / BackpackRotation / BackpackScale)";
                ValkyriesCargo.Log.LogInfo("body: backpack " + Detail);
                return true;
            }
            catch (Exception ex)
            {
                Worn = false;
                Detail = "attaching threw, he goes bare: " + ex.Message;
                if (_throws++ < 3) ValkyriesCargo.Log.LogWarning("body: backpack " + Detail);
                return false;
            }
        }

        private static string _said;

        /// <summary>
        /// He goes bare, and the log SAYS SO. Every path out of `Attach` that is not a success now
        /// comes through here, because the version that only spoke on success and on a throw taught us
        /// nothing on the one night it mattered: the 2026-09-08 two-client session ran with a build
        /// that had no backpack code in it at all, and a reader could not tell that from a build whose
        /// add-on had quietly declined. House rule, from `docs/knowledge-base/`: a silent success and a
        /// silent no-op look the same from outside the game.
        ///
        /// Repeats are held down rather than counted: the same reason logs once, a CHANGED reason logs
        /// again, so a config edit or a mod arriving mid-session is visible without the attach cadence
        /// filling the log.
        /// </summary>
        private static bool Bare(string why)
        {
            Worn = false;
            Detail = why;
            if (_said != why)
            {
                _said = why;
                ValkyriesCargo.Log.LogInfo("body: no backpack - " + why);
            }
            return false;
        }

        /// <summary>The prefab's own top-level children, so a rename upstream reads as a rename and not as a mystery.</summary>
        private static string Children(Transform t)
        {
            var names = new List<string>();
            for (int i = 0; i < t.childCount && names.Count < 12; i++) names.Add(t.GetChild(i).name);
            return names.Count == 0 ? "no children at all" : string.Join("/", names.ToArray());
        }

        /// <summary>The bone names actually on this rig, so a miss names the alternatives instead of implying none exist.</summary>
        private static string Rig(Transform[] all)
        {
            var names = new List<string>();
            for (int i = 0; i < all.Length && names.Count < 12; i++)
                if (all[i] != null && all[i] != all[0]) names.Add(all[i].name);
            return names.Count == 0 ? "no child transforms at all"
                                    : names.Count + "+ bones incl. " + string.Join("/", names.ToArray());
        }

        private static int _parts;
        private static int _triangles;

        /// <summary>
        /// Smoothbrain's parts are skinned to a skeleton Ingvar does not have, so each is baked ONCE at its
        /// bind pose into a static mesh and re-hung with a plain `MeshRenderer`. A pack is rigid enough that
        /// nothing is lost, and it means no bone rebinding, no second skeleton and no per-frame skinning cost
        /// on a character that already carries one skinned body.
        ///
        /// The bake runs on a throwaway instance rather than on the prefab asset: `BakeMesh` reads a live
        /// renderer's current pose, and writing to a prefab in ObjectDB would change the pack for every player
        /// on the machine - the house rule against mutating what is not ours, in its sharpest form.
        /// </summary>
        private static GameObject Bake(Transform meshRoot, string prefabName)
        {
            _parts = 0;
            _triangles = 0;

            GameObject scratch = UnityEngine.Object.Instantiate(meshRoot.gameObject);
            scratch.name = "vc_pack_scratch";
            try
            {
                GameObject holder = new GameObject(ChildName);
                SkinnedMeshRenderer[] skins = scratch.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                for (int i = 0; i < skins.Length; i++)
                {
                    SkinnedMeshRenderer smr = skins[i];
                    if (smr == null || smr.sharedMesh == null) continue;

                    Mesh baked = new Mesh { name = "vc_" + smr.name };
                    smr.BakeMesh(baked);

                    GameObject part = new GameObject(smr.name);
                    part.transform.SetParent(holder.transform, false);
                    // BakeMesh returns vertices in the renderer's own local space, so the part carries that
                    // renderer's placement relative to the mesh root and nothing else.
                    part.transform.localPosition = smr.transform.localPosition;
                    part.transform.localRotation = smr.transform.localRotation;
                    part.transform.localScale = smr.transform.localScale;

                    part.AddComponent<MeshFilter>().sharedMesh = baked;
                    MeshRenderer mr = part.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = smr.sharedMaterials;   // the mod's own Valheim-lit materials, untouched
                    mr.shadowCastingMode = smr.shadowCastingMode;
                    mr.receiveShadows = smr.receiveShadows;

                    _parts++;
                    _triangles += baked.triangles.Length / 3;
                }

                if (_parts == 0)
                {
                    UnityEngine.Object.Destroy(holder);
                    Bare("prefab '" + prefabName + "' has '" + MeshPath + "' but no skinned mesh under it");
                    return null;
                }
                return holder;
            }
            finally
            {
                UnityEngine.Object.Destroy(scratch);
            }
        }

        /// <summary>
        /// The three knobs, applied - in WORLD units, not the bone's.
        ///
        /// `models/ingvar.glb` carries `Armature` at scale 0.01 (the rig is authored in centimetres), so
        /// every bone under it has a world scale of a hundredth. Hanging the pack with `localScale = 1`
        /// put a five-millimetre backpack on him: attached, reported, invisible - which is exactly what
        /// the first live test showed on 2026-09-09. `Core/Knapsack` undoes whatever the rig's scale
        /// actually is, so a scale of 1 means the pack's own authored size and an offset of 0.2 means
        /// twenty centimetres, on this rig or any re-bake of it.
        /// </summary>
        private static void Place(Transform t, Transform bone)
        {
            // lossyScale is the bone's true world scale, the whole chain folded in - which is what a
            // prop parented to it actually inherits. Uniform on this rig; x is the honest read of it.
            float boneScale = bone != null ? bone.lossyScale.x : 1f;

            float x, y, z;
            Knapsack.Triple(ModConfig.BackpackOffset != null ? ModConfig.BackpackOffset.Value : "", out x, out y, out z);
            Knapsack.LocalOffset(boneScale, ref x, ref y, ref z);
            t.localPosition = new Vector3(x, y, z);

            float rx, ry, rz;
            Knapsack.Triple(ModConfig.BackpackRotation != null ? ModConfig.BackpackRotation.Value : "", out rx, out ry, out rz);
            t.localRotation = Quaternion.Euler(rx, ry, rz);   // rotation is scale-free

            float s = Knapsack.LocalScale(ModConfig.BackpackScale != null ? ModConfig.BackpackScale.Value : 1f, boneScale);
            t.localScale = new Vector3(s, s, s);

            _boneScale = boneScale;
        }

        /// <summary>What the last attach found the bone scaled to, so `cargo body` can say it out loud.</summary>
        private static float _boneScale = 1f;

        private static string Name()
        {
            string n = ModConfig.BackpackPrefab != null ? (ModConfig.BackpackPrefab.Value ?? "").Trim() : "";
            return n.Length > 0 ? n : "bp_explorer";
        }

        /// <summary>
        /// The mod's pack, if this machine has it. `ObjectDB.GetItemPrefab(string)` is the public lookup and
        /// answers null for a name it does not know, which is exactly the "no backpack mod here" answer.
        /// </summary>
        private static GameObject FindPrefab(string name)
        {
            if (ObjectDB.instance == null) return null;
            return ObjectDB.instance.GetItemPrefab(name);
        }

        private static string Words(Vector3 v) =>
            "(" + v.x.ToString("0.###") + ", " + v.y.ToString("0.###") + ", " + v.z.ToString("0.###") + ")";
    }
}
