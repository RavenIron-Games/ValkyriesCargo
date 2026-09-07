using System;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// Ingvar's state machine (design 3.3), PURE: ints and floats in, a decision out. No Unity type,
    /// no engine call, so every transition is provable off-game and `CargoMerchant` is left with
    /// nothing but "measure the world, apply the decision, write the ZDO".
    ///
    /// The states are the `VCargo_state` values `Spawner.MerchantState` already publishes, because the
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
        /// <summary>
        /// Design 3.3: the FLOOR of the scaled budget `ApproachBudget` computes, and what a close
        /// approach still uses outright — a merchant who lands a few metres from the player gets
        /// exactly the patience he always had. See `ApproachBudget` for the scaling (F5, audit
        /// 2026-09-07).
        /// </summary>
        public const float ApproachTimeoutSeconds = 20f;

        /// <summary>The CEILING `ApproachBudget` clamps to, however far he started.</summary>
        public const float ApproachTimeoutMaxSeconds = 90f;

        /// <summary>
        /// `ApproachBudget`'s divisor: the metres-per-second he is assumed able to close. Below BOTH the
        /// shipped Dverger's `Humanoid.m_walkSpeed` (2) and `m_runSpeed` (7) — WubarrksEye's live prefab
        /// dump, 2026-09-03, build21981559 (`Humanoid.m_walkSpeed=2, m_runSpeed=7`) — on purpose:
        /// `BaseAI.Follow` only asks for `m_runSpeed` past 10 m (`asm:4227-4239`,
        /// `run = distance > 10f`), so a real walk-up spends most of a long distance running and only
        /// the last stretch on foot, and even a walk-only worst case (a base cluttered enough that he
        /// never gets a clear 10 m run) needs less per second than this. The slack left over covers
        /// `BaseAI.FindPath`'s 1 s replan throttle (`asm:4751-4758`) and a path that is not a straight
        /// line.
        /// </summary>
        public const float ApproachBudgetSpeed = 1.5f;

        /// <summary>Beyond this, for `TradingLeashSeconds`, he walks after the player again — at most
        /// once a visit; see `Next`'s Trading branch.</summary>
        public const float TradingLeashDistance = 12f;
        public const float TradingLeashSeconds = 5f;

        /// <summary>A landing is only a landing once; below this he is still falling, not down.</summary>
        public const float GroundedGraceSeconds = 0.25f;

        /// <summary>The window `Next` judges pathing progress over (F5 part 2).</summary>
        public const float ProgressWindowSeconds = 1f;

        /// <summary>
        /// Net movement below this over one window is measurement noise, not a walk: a Rigidbody nudged
        /// by a collision, or an animator settling after a state change, reads as "not moving" on any
        /// ONE tick even mid-walk, but not over a full second of real forward progress.
        /// </summary>
        public const float ProgressEpsilonMeters = 0.2f;

        /// <summary>
        /// Windows this long in a row closing under `ProgressEpsilonMeters` is a pathing failure, not a
        /// patience one — `BaseAI.MoveTo` returns the same "arrived" for "no path" as for a real arrival
        /// (`asm:4774-4800`), so this is the only way `CargoMerchant` can tell the two apart before the
        /// (much longer) scaled timeout would.
        /// </summary>
        public const float StuckThresholdSeconds = 3f;

        /// <summary>
        /// How long he gets to walk before giving up, scaled to how far he started (F5, audit
        /// 2026-09-07). A flat 20 s — the old constant — cannot be met from much past 30 m; the live
        /// log in CLAUDE.md's INTEGRATED IN-GAME RUN caught a merchant re-declaring Trading 51 m from
        /// the player, on a loop, every 20 s. See `ApproachBudgetSpeed` for why 1.5 m/s, and
        /// `ApproachTimeoutSeconds`/`ApproachTimeoutMaxSeconds` for the floor and ceiling. A large
        /// budget is safe against a GENUINE pathing failure because `StuckThresholdSeconds` (3 s) gives
        /// up on that separately and much sooner — this number only ever gets spent by a walk that is
        /// slow but actually progressing.
        /// </summary>
        public static float ApproachBudget(float distanceAtEntry)
            => Clamp(distanceAtEntry / ApproachBudgetSpeed, ApproachTimeoutSeconds, ApproachTimeoutMaxSeconds);

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>
        /// The rolling window `Next` uses to tell "he is walking, just slowly" from "he has not moved"
        /// (F5 part 2): `CargoMerchant` feeds it the SAME per-tick displacement that builds
        /// `_approachMoved`, and it folds ticks into `ProgressWindowSeconds`-long windows and judges
        /// each window's NET distance rather than any one tick's, so jitter washes out instead of
        /// resetting or inflating a stuck reading. Reset by the caller exactly when `_approachMoved` is:
        /// on every transition INTO Approaching.
        /// </summary>
        public struct ApproachProgress
        {
            public float WindowMoved;
            public float WindowSeconds;
            /// <summary>Consecutive window-time closed under `ProgressEpsilonMeters` net movement.</summary>
            public float StuckSeconds;
        }

        /// <summary>
        /// One tick of the stuck timer. `movedThisTick` is his own displacement since the last call
        /// (never negative), `dt` the physics step. A window shorter than `ProgressWindowSeconds` just
        /// accumulates; a window that closes having covered less than `ProgressEpsilonMeters` adds its
        /// own length onto `StuckSeconds`, and one that closes having covered more resets it to zero —
        /// a merchant who was nearly stuck and then breaks free never reaches the threshold on a stale
        /// count, the same reset-on-progress rule `AccumulateFar` uses for the leash.
        /// </summary>
        public static ApproachProgress AccumulateProgress(ApproachProgress p, float movedThisTick, float dt)
        {
            p.WindowMoved += movedThisTick;
            p.WindowSeconds += dt;
            if (p.WindowSeconds < ProgressWindowSeconds) return p;

            p.StuckSeconds = p.WindowMoved < ProgressEpsilonMeters ? p.StuckSeconds + p.WindowSeconds : 0f;
            p.WindowMoved = 0f;
            p.WindowSeconds = 0f;
            return p;
        }

        /// <summary>What the caller should do this tick. `State` is always a valid `VCargo_state`.</summary>
        public struct Step
        {
            public int State;
            /// <summary>The state differs from the one passed in: write the ZDO and log.</summary>
            public bool Changed;
            /// <summary>Fire the arrival line and the landing effect. True on exactly one tick per visit.</summary>
            public bool CallOut;
            /// <summary>He should be walking toward the player this tick.</summary>
            public bool Follow;
            /// <summary>
            /// This tick's change to trading is the scaled-timeout FALLBACK, not an arrival: he called
            /// out from wherever he stood. Set here rather than re-derived by the caller because the
            /// caller would have to match on `Why` or re-compare the distance, and both drift away from
            /// this method the moment anyone edits it. `CargoMerchant` writes its walk diagnosis off
            /// this. Mutually exclusive with `Stuck` — only one fallback reason fires on a given tick.
            /// </summary>
            public bool TimedOut;
            /// <summary>
            /// This tick's change to trading is the PATHING fallback (F5 part 2): no net progress for
            /// `StuckThresholdSeconds`, not a timeout and not an arrival. A distinct flag from
            /// `TimedOut` because the two are different failures with different fixes — one says the
            /// visit ran out of patience, the other says vanilla could not find a way there at all.
            /// </summary>
            public bool Stuck;
            /// <summary>
            /// This tick's change back to Approaching is the trading leash (F5 part 3), so
            /// `CargoMerchant` knows to spend it: the leash fires at most once a visit (see `Next`'s
            /// Trading branch).
            /// </summary>
            public bool LeashFired;
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
        ///
        /// `distanceAtEntry`, `stuckSeconds` and `leashSpent` (F5) all default to values that reproduce
        /// the OLD, unscaled behaviour exactly — a flat `ApproachTimeoutSeconds` budget, never stuck,
        /// the leash always armed — so every caller written before F5 keeps its old answer untouched.
        /// `CargoMerchant` is the only caller that should ever pass the real numbers.
        /// </summary>
        public static Step Next(int state, bool carried, bool grounded, float distance,
                                float timeInState, float farSeconds, float approachDistance,
                                float distanceAtEntry = 0f, float stuckSeconds = 0f, bool leashSpent = false)
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
                // Close enough: an arrival always wins, even on the tick a stuck- or timed-out
                // fallback would otherwise also have fired (both checked below; either needs the
                // distance to still be beyond approachDistance).
                if (distance <= approachDistance)
                    return new Step { State = 2, Changed = true, CallOut = true, Why = "reached the player" };

                // No net progress for StuckThresholdSeconds: a PATHING failure, not a patience one.
                // BaseAI.MoveTo returns the same "arrived" for "FindPath found nothing" as for a real
                // arrival (asm:4774-4800), so without this a merchant wedged against his own doorstep
                // and one genuinely mid-walk look identical until the much later timeout.
                if (stuckSeconds >= StuckThresholdSeconds)
                    return new Step
                    {
                        State = 2, Changed = true, CallOut = true, Stuck = true,
                        Why = "stuck: no ground closed in " + Wire.Float(StuckThresholdSeconds) +
                              " s (a pathing failure), " + Wire.Float(distance) + " m out"
                    };

                // He has walked long enough, at the distance he started from, to stop looking foolish
                // about it. The scaled budget is what saves the visit when the pilot stands somewhere
                // far but genuinely reachable; StuckThresholdSeconds above is what saves it sooner when
                // he cannot path there at all.
                float budget = ApproachBudget(distanceAtEntry);
                if (timeInState >= budget)
                    return new Step
                    {
                        State = 2, Changed = true, CallOut = true, TimedOut = true,
                        Why = "gave up walking after " + Wire.Float(budget) + " s"
                    };

                return new Step { State = 1, Follow = true, Why = "approaching" };
            }

            // Trading. The leash is deliberately slow: a player circling him at 13 m is not leaving,
            // and a merchant who breaks into a walk every time you step back is worse than one who
            // waits. Only a sustained absence moves him.
            //
            // It fires AT MOST ONCE a visit (`leashSpent`, F5 part 3): a merchant who already gave up
            // once (TimedOut or Stuck) can give up again the same way, and TradingLeashDistance (12 m)
            // is well inside ApproachBudget's own floor — nothing about distance alone stops a second
            // failed walk from looping into a third the way the live log caught (51 m, 20 s, forever).
            // Spending the leash caps the whole machine at two approach attempts a visit: a player who
            // wanders back within `approachDistance` still reaches Trading normally either time; one
            // who does not just leaves a merchant standing wherever the second attempt ended — a
            // merchant trading from the wrong spot, not one stuck looping through "found him" forever.
            if (!leashSpent && farSeconds >= TradingLeashSeconds && distance > TradingLeashDistance)
                return new Step
                {
                    State = 1, Changed = true, Follow = true, LeashFired = true,
                    Why = "the player left: " + Wire.Float(distance) + " m for " + Wire.Float(farSeconds) + " s"
                };
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
        /// machine because `CargoFlight` cuts the link by writing BOTH `VCargo_carrier` and `VCargo_state`,
        /// and the two must never be read as disagreeing: the carrier id is the authority while the
        /// world is loaded, and the state is the authority after a restart, when every `ZDOID` in
        /// the save has been renumbered and the old carrier id means nothing at all.
        /// </summary>
        public static bool ShouldPin(int state, bool carrierResolved) => state <= 0 && carrierResolved;
    }
}
