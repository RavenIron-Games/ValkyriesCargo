namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The one decision behind `Patches/Patch_BaseAI_IsEnemy`, PURE so it is provable off-game — the
    /// same shape as <see cref="Immortality"/>, for the same reason.
    ///
    /// F11 of the P4/P5 audit (`docs/AUDIT-P4P5-2026-09-07.md`): a tamed creature is a legitimate target
    /// for every hostile in the game (`BaseAI.IsEnemy(a, b)`, the static one every targeting path, melee
    /// and area hit filter and the enemy HUD go through), and Ingvar is immortal and — because his
    /// `RPC_Damage` is cancelled whole — never staggered or pushed, so a raid parks on him for the visit.
    /// The owner's decision, 2026-09-07: **ghost mode**. Ingvar is to hostiles what a player in vanilla's
    /// `ghost` mode is: not a target, not a threat. Neither faction-only nor the aggro magnet.
    ///
    /// Any pair that has the merchant in it answers "not enemies" while a visit is running. Every other
    /// pair is the world's own business and vanilla decides it exactly as before.
    /// </summary>
    public static class Ghost
    {
        public enum Verdict
        {
            /// <summary>Vanilla's `IsEnemy` runs and answers.</summary>
            RunOriginal,
            /// <summary>Another prefix already cancelled the original; its answer is left alone.</summary>
            Cancelled,
            /// <summary>Our merchant is one of the pair: the answer is false, and vanilla does not run.</summary>
            NotEnemies,
        }

        /// <summary>
        /// `liveCount` is `CargoMerchant.LiveCount`, a fast path and not a decision: with no visit running
        /// nothing here is consulted, whatever the two flags say. `aIsMerchant` / `bIsMerchant` is "this
        /// character carries our `CargoMerchant`".
        /// </summary>
        public static Verdict Decide(bool runOriginal, int liveCount, bool aIsMerchant, bool bIsMerchant)
        {
            if (!runOriginal) return Verdict.Cancelled;                 // somebody ahead of us already answered
            if (liveCount <= 0) return Verdict.RunOriginal;             // no visit: not ours, vanilla runs
            return (aIsMerchant || bIsMerchant) ? Verdict.NotEnemies    // Ingvar in the pair: a ghost
                                                : Verdict.RunOriginal;  // anyone else: the world's business
        }
    }
}
