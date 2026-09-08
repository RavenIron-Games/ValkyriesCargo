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
