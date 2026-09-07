using System;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// Ingvar's state machine (design 3.3), PURE: ints and floats in, a decision out. No Unity type,
    /// no engine call, so every transition is provable off-game and `CargoMerchant` is left with
    /// nothing but "measure the world, apply the decision, write the ZDO".
    ///
    /// The states are the `vc_state` values `Spawner.MerchantState` already publishes, because the
    /// ZDO is the only thing that survives an ownership handover mid-visit: a nearer client takes the
    /// merchant over and carries on from the number, not from a field.
    ///
    /// One rule is worth stating because it is the reason this is a machine at all rather than a pair
    /// of ifs: **transitions only ever move forward, except the trading-to-approaching leash.** A
    /// merchant who has called out has called out; nothing re-runs his arrival because the pilot
    /// walked away and came back.
    /// </summary>
    public static class MerchantPlan
    {
        /// <summary>Design 3.3: he gives up walking after this and calls out where he stands.</summary>
        public const float ApproachTimeoutSeconds = 20f;

        /// <summary>Beyond this, for `LeashSeconds`, he walks after the player again.</summary>
        public const float TradingLeashDistance = 12f;
        public const float TradingLeashSeconds = 5f;

        /// <summary>A landing is only a landing once; below this he is still falling, not down.</summary>
        public const float GroundedGraceSeconds = 0.25f;

        /// <summary>What the caller should do this tick. `State` is always a valid `vc_state`.</summary>
        public struct Step
        {
            public int State;
            /// <summary>The state differs from the one passed in: write the ZDO and log.</summary>
            public bool Changed;
            /// <summary>Fire the arrival line and the landing effect. True on exactly one tick per visit.</summary>
            public bool CallOut;
            /// <summary>He should be walking toward the player this tick.</summary>
            public bool Follow;
            public string Why;

            public override string ToString() =>
                Name(State) + (Changed ? " (entered: " + Why + ")" : "") + (Follow ? " [following]" : "") + (CallOut ? " [callout]" : "");
        }

        public static string Name(int state)
        {
            switch (state)
            {
                case 0: return "carried";
                case 1: return "approaching";
                case 2: return "trading";
                case 3: return "leaving";
                default: return "state " + state;
            }
        }

        /// <summary>
        /// One tick. `carried` is "the ZDO still names a carrier", `grounded` is the character's own
        /// ground contact, `distance` is to the nearest player (or `float.MaxValue` when there is
        /// none instantiated here), `timeInState` counts seconds since the last change, and
        /// `farSeconds` counts how long the nearest player has been beyond the leash.
        /// </summary>
        public static Step Next(int state, bool carried, bool grounded, float distance,
                                float timeInState, float farSeconds, float approachDistance)
        {
            // Leaving is terminal. Nothing measured on the ground pulls him back out of it: the
            // departure is the server's decision and the vanish is already running.
            if (state >= 3) return new Step { State = 3, Why = "leaving" };

            // Carried: he hangs until the bird cuts the link AND he is actually standing. The
            // grounded test is what stops the landing effect firing at 10 m while he still falls.
            if (state <= 0)
            {
                if (carried) return new Step { State = 0, Why = "carried" };
                if (!grounded) return new Step { State = 0, Why = "dropped, still falling" };
                return new Step { State = 1, Changed = true, Follow = true, Why = "landed" };
            }

            if (state == 1)
            {
                // Close enough, or he has walked long enough to stop looking foolish about it. The
                // timeout is what saves the visit when the pilot stands somewhere he cannot path to.
                if (distance <= approachDistance)
                    return new Step { State = 2, Changed = true, CallOut = true, Why = "reached the player" };
                if (timeInState >= ApproachTimeoutSeconds)
                    return new Step { State = 2, Changed = true, CallOut = true, Why = "gave up walking after " + Wire.Float(ApproachTimeoutSeconds) + " s" };
                return new Step { State = 1, Follow = true, Why = "approaching" };
            }

            // Trading. The leash is deliberately slow: a player circling him at 13 m is not leaving,
            // and a merchant who breaks into a walk every time you step back is worse than one who
            // waits. Only a sustained absence moves him.
            if (farSeconds >= TradingLeashSeconds && distance > TradingLeashDistance)
                return new Step { State = 1, Changed = true, Follow = true, Why = "the player left: " + Wire.Float(distance) + " m for " + Wire.Float(farSeconds) + " s" };
            return new Step { State = 2, Why = "trading" };
        }

        /// <summary>
        /// The leash timer, kept here so its one edge case is testable: it resets the moment he is
        /// inside the leash, and only accumulates while he is outside it. A caller that accumulates
        /// unconditionally sends him walking after a player who never left.
        /// </summary>
        public static float AccumulateFar(float farSeconds, float distance, float dt)
            => distance > TradingLeashDistance ? farSeconds + dt : 0f;

        /// <summary>
        /// True when this state means the carry pin should be driving his transform. Kept beside the
        /// machine because `CargoFlight` cuts the link by writing BOTH `vc_carrier` and `vc_state`,
        /// and the two must never be read as disagreeing: the carrier id is the authority while the
        /// world is loaded, and the state is the authority after a restart, when every `ZDOID` in
        /// the save has been renumbered and the old carrier id means nothing at all.
        /// </summary>
        public static bool ShouldPin(int state, bool carrierResolved) => state <= 0 && carrierResolved;
    }
}
