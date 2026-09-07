namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The one decision behind `Patches/Patch_Character_Damage`, PURE so that it can be proven
    /// off-game — which is the whole reason this file exists.
    ///
    /// It exists because the inline version of this decision shipped **inverted** on `main` and in
    /// `v0.1.0-rc1`. A Harmony prefix's return value means "run the original", so `false` SKIPS it;
    /// the guard was written `if (!__runOriginal || LiveCount == 0) return false;`, folding "another
    /// prefix already cancelled" (where `false` is right) together with "no visit is running, this
    /// is none of our business" (where `false` cancels `Character.RPC_Damage` for **every character
    /// in the world**). Nothing could take damage unless a merchant happened to be instanced.
    ///
    /// A clean build proves nothing about that, and no in-game check we had would have caught it
    /// either: the mod looks perfectly healthy, and it is the REST of the game that stops working.
    /// Track A's P4/P5 adversarial audit found it as F1 (`docs/AUDIT-P4P5-2026-09-07.md`).
    ///
    /// So the branch is here, in three lines with three checks on them, and the patch is left with
    /// nothing but a component lookup.
    /// </summary>
    public static class Immortality
    {
        /// <summary>
        /// Should vanilla's `Character.RPC_Damage` run for this character? `isMerchant` is "this
        /// character carries our `CargoMerchant`". The ONLY case that cancels is our own merchant:
        /// everything else in the world takes its damage exactly as it always did.
        /// </summary>
        public static bool RunOriginal(bool runOriginal, int liveCount, bool isMerchant)
        {
            if (!runOriginal) return false;   // another prefix cancelled first; honour it
            if (liveCount <= 0) return true;  // no visit running: not ours, and never our call to cancel
            return !isMerchant;               // ours is immortal; anything else is not
        }
    }
}
