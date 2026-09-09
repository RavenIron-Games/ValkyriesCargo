using System;
using System.Collections.Generic;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The backpack add-on's body half (Wu'barrk, 2026-09-08; the owner's decision the same day: Smoothbrain's
    /// Backpacks). The shelf half is `Core/Shelf.Scaled` + `Server/BackpackMod`; this is the part that decides
    /// WHERE on Ingvar a pack hangs and HOW it is turned, kept pure so the whole of it is checked off-game.
    ///
    /// Why any of it is a decision at all. Smoothbrain's pack is one prefab, `bp_explorer`, whose geometry sits
    /// under `attach_skin/Mesh` as `SkinnedMeshRenderer`s rigged to VALHEIM's humanoid skeleton. Ingvar's is a
    /// different skeleton with 24 joints of its own (`Hips -> Spine -> Spine01 -> Spine02 -> neck -> Head`,
    /// read off `models/ingvar.glb`), and he is about 1.37 m to a player's ~1.8 m. So the pack cannot be worn
    /// the way vanilla wears one - `VisEquipment` would bind it to the DVERGER chassis, whose renderers
    /// `Client/BodyLoader.HideStandIn` switches off - and is instead hung as a rigid prop on a named bone of
    /// Ingvar's own rig, with the offset, turn and scale as client knobs so it can be dialled in on a screen
    /// without a rebuild.
    ///
    /// PURE: strings and floats only. Nothing here names a Unity or Valheim type.
    /// </summary>
    public static class Knapsack
    {
        /// <summary>The bone the shipped bake wants: the upper spine, where a pack's straps would sit.</summary>
        public const string DefaultBone = "Spine02";

        /// <summary>
        /// Tried in order when the configured bone is not on the rig, so a re-bake that renames or drops a
        /// spine joint still hangs the pack somewhere sensible rather than dropping it at his feet. Ordered
        /// from the best place downward; `Hips` is the last resort because every humanoid rig has one.
        /// </summary>
        public static readonly string[] Fallbacks = { "Spine02", "Spine2", "Spine01", "Spine1", "Chest", "Spine", "Hips" };

        /// <summary>
        /// Which bone to hang the pack on, given every bone name on the rig. The configured name wins when the
        /// rig has it; otherwise the first fallback the rig does have. Case-insensitive, because a bake's
        /// exporter decides the capitalisation and the knob is typed by a person. Empty when the rig has none
        /// of them, which the caller reports rather than guessing a transform.
        /// </summary>
        public static string ResolveBone(IList<string> boneNames, string preferred)
        {
            if (boneNames == null || boneNames.Count == 0) return "";

            string want = (preferred ?? "").Trim();
            if (want.Length > 0)
            {
                string hit = Find(boneNames, want);
                if (hit.Length > 0) return hit;
            }

            for (int i = 0; i < Fallbacks.Length; i++)
            {
                string hit = Find(boneNames, Fallbacks[i]);
                if (hit.Length > 0) return hit;
            }
            return "";
        }

        /// <summary>The rig's own spelling of <paramref name="name"/>, or "" when it has no such bone.</summary>
        private static string Find(IList<string> boneNames, string name)
        {
            for (int i = 0; i < boneNames.Count; i++)
            {
                string b = boneNames[i];
                if (b != null && string.Equals(b, name, StringComparison.OrdinalIgnoreCase)) return b;
            }
            return "";
        }

        /// <summary>
        /// A "x,y,z" knob to three floats. Whitespace anywhere, and any component that is missing, empty or
        /// not a finite number, reads as 0 - a knob typed wrong must leave the pack where the default put it,
        /// never at NaN, which in Unity propagates into the transform and takes the whole body with it.
        /// Returns false when the string was not three usable numbers, so the caller can say so once.
        /// </summary>
        public static bool Triple(string s, out float x, out float y, out float z)
        {
            x = 0f; y = 0f; z = 0f;
            if (string.IsNullOrEmpty(s)) return false;

            string[] parts = s.Split(',');
            if (parts.Length != 3) return false;

            bool ok = true;
            if (!Wire.TryFloat(parts[0], out x)) { x = 0f; ok = false; }
            if (!Wire.TryFloat(parts[1], out y)) { y = 0f; ok = false; }
            if (!Wire.TryFloat(parts[2], out z)) { z = 0f; ok = false; }
            return ok;
        }

/// <summary>
        /// The bone's own world scale, undone.
        ///
        /// This is the whole reason the first live test showed no pack at all while the log reported a
        /// perfectly attached one ("18 part(s), 11206 tris", 2026-09-09). `models/ingvar.glb` carries
        /// `Armature` at **scale 0.01** - the rig is authored in centimetres and scaled down by a
        /// hundred at its root - so every bone under it, `Spine02` included, has a world scale of 0.01.
        /// A prop parented to that bone with `localScale = 1` renders at a HUNDREDTH of its size: the
        /// pack was about five millimetres across, correctly attached and completely invisible.
        ///
        /// So the knobs are declared to be in WORLD units - metres, and a scale of 1 means the pack's
        /// own authored size - and this converts them into the bone's local space. It is general: it
        /// reads whatever scale the rig actually has, so a re-bake at metre scale needs no config edit
        /// and no code change.
        ///
        /// A bone scale of zero, negative or not-a-number is not a rig anybody can hang anything on;
        /// rather than divide by it and hand Unity an infinity, this falls back to 1 (no compensation),
        /// which leaves the pack visibly wrong rather than invisibly broken.
        /// </summary>
        public static float Uncompress(float boneWorldScale)
        {
            if (boneWorldScale <= 0f || float.IsNaN(boneWorldScale) || float.IsInfinity(boneWorldScale)) return 1f;
            return 1f / boneWorldScale;
        }

        /// <summary>
        /// The local scale to give the pack so it ends up <paramref name="configured"/> times its authored
        /// size in the WORLD, whatever the bone underneath is scaled to. `Scale` still clamps the knob
        /// itself, so a typo cannot produce a pack the size of a house.
        /// </summary>
        public static float LocalScale(float configured, float boneWorldScale)
        {
            return Scale(configured) * Uncompress(boneWorldScale);
        }

        /// <summary>
        /// The local offset for a displacement given in METRES. Same reason as `LocalScale`: on a rig at
        /// 0.01 an offset of "0,0.2,-0.15" would have moved the pack two millimetres, so the knob would
        /// have read as doing nothing at all and been dialled to absurd numbers to compensate.
        /// </summary>
        public static void LocalOffset(float boneWorldScale, ref float x, ref float y, ref float z)
        {
            float k = Uncompress(boneWorldScale);
            x *= k; y *= k; z *= k;
        }

                public const float MinScale = 0.05f;
        public const float MaxScale = 5f;

        /// <summary>
        /// The pack's scale on Ingvar's bone, clamped. A non-finite or non-positive knob reads as 1 rather
        /// than collapsing the mesh to a point or inverting its normals.
        /// </summary>
        public static float Scale(float configured)
        {
            if (float.IsNaN(configured) || float.IsInfinity(configured) || configured <= 0f) return 1f;
            if (configured < MinScale) return MinScale;
            if (configured > MaxScale) return MaxScale;
            return configured;
        }
    }
}
