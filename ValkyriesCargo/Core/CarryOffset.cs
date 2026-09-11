namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// PURE. Where Ingvar hangs while the Valkyrie carries him, as three numbers and the text that
    /// carries them through the config.
    ///
    /// The pin is `talon.position - talon.TransformVector(offset)` (`CargoMerchant.PinToTalon`), so the
    /// offset is measured in the TALON's own space, not the world's, and it is SUBTRACTED: a bigger
    /// number is further from the talon, zero puts his origin - the soles of his feet, a character's
    /// origin - on the talon itself. The attach point is a bone under the Valkyrie's right foot
    /// (`m_attachPoint` = 'Attach', read live 2026-09-07), so "y" is how far below the foot as the foot
    /// is turned, and "z" how far behind it.
    ///
    /// Vanilla's own numbers are tuned for the intro, which carries a PLAYER; Ingvar is about 1.37 m,
    /// a head shorter, so the same offset leaves him hanging further off the talons than a player does.
    /// `Server.CarryOffset` is the knob, empty means the prefab's own, and this is the parse and the
    /// sanity bound behind it. Synced and locked, because the pin runs on every machine that has him
    /// instanced and every screen must agree (design section 2).
    /// </summary>
    public static class CarryOffset
    {
        /// <summary>The SHIPPED `Valkyrie` prefab's `m_attachOffset`, read live 2026-09-07. The field
        /// initialiser in the assembly says (0, 0, 1) and the prefab overrides it; this is the override,
        /// kept as the last-resort fallback for a bird with no `Valkyrie` component to read.</summary>
        public const float PrefabX = 0f;
        public const float PrefabY = 0.3f;
        public const float PrefabZ = 0.4f;

        /// <summary>What `Server.CarryOffset` says when it means "whatever the prefab says".</summary>
        public const string FollowThePrefab = "";

        /// <summary>
        /// What ships: his feet ON the talon. TUNED ON A MACHINE 2026-09-11 (Storm10, 1.0.12, visits 2 to 4),
        /// the owner walking it down live from Configuration Manager while the bird was in the air -
        /// (0, 0.3, 0.4) the prefab's, then (0, 0.2, 0.25), then this, each "needs to be closer". The prefab's
        /// own numbers are not wrong, they are for the intro's full-height PLAYER; Ingvar is about 1.37 m and
        /// the same offset reads as a merchant dangling under the talons rather than held in them.
        /// `FollowThePrefab` is still honoured and is one edit away, for anyone who wants vanilla's framing.
        /// </summary>
        public const string TunedDefault = "0, 0, 0";

        /// <summary>
        /// The bound on one component, metres. A guard on an edited config, not a number anyone will
        /// reach: the talon is on a bird, and past a few metres he is not being carried by it any more,
        /// he is being towed beside it. Out of range is REFUSED whole rather than clamped, because a
        /// clamp of a typo is a silently different flight and a refusal says so in the log.
        /// </summary>
        public const float MaxComponent = 5f;

        /// <summary>What <see cref="Resolve"/> answered: the three numbers, where they came from, and,
        /// when the config had something to say and it was not usable, why it was not.</summary>
        public struct Resolved
        {
            public float X, Y, Z;

            /// <summary>True when the config supplied these numbers, false when they are the prefab's.</summary>
            public bool FromConfig;

            /// <summary>Null unless the config held text that could not be used; the reason, for one log line.</summary>
            public string Problem;
        }

        /// <summary>
        /// "x, y, z" with any spacing, invariant culture (`Wire.TryFloat`, which also refuses NaN and
        /// infinity - either would put his transform somewhere no later frame recovers from). Exactly
        /// three numbers: two is a typo and four is a different idea, and both are refused rather than
        /// half-read.
        /// </summary>
        public static bool TryParse(string text, out float x, out float y, out float z)
        {
            x = 0f; y = 0f; z = 0f;
            if (text == null) return false;
            string[] parts = text.Split(',');
            if (parts.Length != 3) return false;
            return Wire.TryFloat(parts[0], out x) && Wire.TryFloat(parts[1], out y) && Wire.TryFloat(parts[2], out z);
        }

        /// <summary>The text form this writes into a config file, and what <see cref="TryParse"/> reads back.</summary>
        public static string Format(float x, float y, float z) =>
            Wire.Float(x) + ", " + Wire.Float(y) + ", " + Wire.Float(z);

        /// <summary>
        /// The whole decision in one pure call: the config's text against the prefab's own three
        /// numbers. Empty or whitespace follows the prefab and is not a problem; anything else that
        /// does not parse, or that reaches past <see cref="MaxComponent"/>, ALSO follows the prefab and
        /// says why. The offset never fails closed into a hang of (0,0,0), because that is a merchant
        /// standing inside the bird's foot on every screen.
        /// </summary>
        public static Resolved Resolve(string configText, float prefabX, float prefabY, float prefabZ)
        {
            Resolved r = new Resolved { X = prefabX, Y = prefabY, Z = prefabZ, FromConfig = false, Problem = null };
            if (configText == null || configText.Trim().Length == 0) return r;

            float x, y, z;
            if (!TryParse(configText, out x, out y, out z))
            {
                r.Problem = "'" + configText.Trim() + "' is not three numbers separated by commas (x, y, z)";
                return r;
            }
            if (Bigger(x) || Bigger(y) || Bigger(z))
            {
                r.Problem = "'" + configText.Trim() + "' reaches past " + Wire.Float(MaxComponent) +
                            " m on an axis; the talon is on a bird";
                return r;
            }

            r.X = x; r.Y = y; r.Z = z; r.FromConfig = true;
            return r;
        }

        private static bool Bigger(float v) => (v < 0f ? -v : v) > MaxComponent;

        /// <summary>One line for `cargo status`, in the terms the number is actually in.</summary>
        public static string Describe(Resolved r) =>
            "carry: he hangs at the talon less (" + Format(r.X, r.Y, r.Z) + ") in the talon's own space, " +
            (r.FromConfig ? "set (Server.CarryOffset)" : "the Valkyrie prefab's own (Server.CarryOffset is empty)") +
            (r.Problem == null ? "" : "; REFUSED: " + r.Problem);
    }
}
