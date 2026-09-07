# Audit: the StormTest session of 2026-09-07 — the walk-up, the reclaim, the cooldown, and the eleven merged fixes

For Track B (Wu'barrk). Read-only: nothing in your files changed; every fix below is a proposed diff against
main `161743b`. The evidence is `docs/proofs/2026-09-07-stormtest-session.md` and the log excerpt beside it;
the full server log of the session (560 KB) and the client's second boot are on Don's machine.

**How this was made.** Three Opus auditors, one per section, read the code, the decompiled 0.221.12 game and
the logs; one Opus refuter per finding then tried to knock each one down, reading the same lines itself. Four
findings survived intact, four were upheld in their code half and corrected in their story, and every proposed
diff was reviewed a second time. What follows is what survived, with the corrections applied. Where the
refuters disagreed with the auditors, the refuter's reading is the one here, and the doubt is stated.

## 0. In one screen

| id | what | severity | the fix |
|---|---|---|---|
| **D1** | The merchant's first approach after the drop never starts cleanly (6/6): the state change arrives through the ZDO and skips every entry reset `Decide` does | behavioural, seen by every tester | route the ZDO-driven change through the same reset, print the elapsed clock, warn on the silent miss (§1) |
| **D1b** | On visits 4–6 the give-up came never / +55 s / +105 s after the drop, with the merchant 148 m and 43 m from a pilot he was dropped 13 m from | **unexplained by the code alone**; best-fit reconstruction below | one log line at the ZDO-driven transition settles it next session (§1.3) |
| **D3** | `restart sweep: 1 stranded merchant(s) destroyed` at every visit end. **The reclaim works**; `ZDOMan.DestroyZDO` only queues, and the sweep in the same call sees the ZDO still in the table | reporting only | run the sweep one director tick after the reclaim; name the sweep per call site (§2) |
| **D2** | The per-player cooldown is keyed on `zdo.GetOwner()`, a per-world-join session uid: three `cool` rows for one player after two relogs | behavioural, narrow: the base cooldown at the dispatch point masks it within 60 m | key on `ZDOVars.s_playerID` with the probe rows that go with it (§3) |
| **D4a** | `admin wire registered for ? (0)` / `deal wire registered for ? (0)` on every connect | wording | register once the peer `IsReady()` (§4) |
| **D4b** | A client logs `session ended: sidecar flushed; director … dropped` on leaving a world | wording | branch the line on the director (§4) |
| **F-table** | Of the eleven merged audit fixes, two are confirmed on a machine, two contradicted, two half-confirmed, six never exercised | — | one line each on what would exercise them (§5) |
| **P10a** | Steam has a `default_pre1_0` branch on both apps as of today, pinned to 21981559 / 21981590, "Last stable build before 1.0" | fact for your 1.0 work | §6 |

**Three corrections to the proofs record**, applied there in the same commit: the sweep line follows the `ended` line by about 2 s (`VanishGraceSeconds`, two director ticks), not 6 s — our lines carry no timestamps and the 6 s was a read-time; "six flights at 16.60 / 16.96 / 16.02 / 16.02 / 16.72 s" lists five, visit 3's flight line did not survive the client restart; and "597.9 m in 20 s = 30 m/s" is wrong, because `in 20 s` in the diagnosis prints the BUDGET, not the elapsed time — the wall clock for that walk was 60–80 s, about 8–10 m/s, still above a Dverger's `m_runSpeed` 7 and still unexplained, but not a fourfold anomaly.

## 1. D1 — the first approach after the drop

### 1.1 What is certain (code, high confidence, upheld by the refuters)

`CargoMerchant` has two ways into `Approaching` and only one of them does the bookkeeping.

- `Decide` resets `_timeInState`, `_farSeconds`, `_approachMoved`, and — on entry to Approaching — `_distanceAtApproachEntry` and `_progress`, inside its `if (!step.Changed) return;` block (`Client/CargoMerchant.cs:434-450`). That is the only assignment site of `_distanceAtApproachEntry` (`:448`).
- `ResolveCarrier` copies `VCargo_state` off the ZDO straight into `_state` every physics step (`:376`, `_state = zdo.GetInt(Spawner.StateHash, _state);`), above that block. A state that arrives this way gets no reset.
- The drop writes that value: `CargoFlight.Drop` sets `VCargo_carrier = None` and `VCargo_state = Approaching` on the merchant's ZDO (`Client/CargoFlight.cs:230-231`). On a client `Spawner.Merchant` is always `ZDOID.None` (`Spawner.Author` runs only behind `znet.IsServer()`, `Core/CargoTick.cs:119`), so `FindMerchantByCarrier` (`CargoFlight.cs:246-259`, a scan of `Character.GetAllCharacters()`) is the only lookup, and a miss is silent — no `else`, no log, and `dropped at (…)` prints afterwards regardless (`:241`).

The session's own line proves the path: `budget scaled from 0 m at entry` is `Wire.Float(_distanceAtApproachEntry)` printed verbatim (`:517`), 6 of 6, and no `approaching (entered: landed)` line exists in any log of the session. So on every visit the state went Carried → Approaching through the ZDO read, not through the plan — which also proves the drop's write DID land on visits 5 and 6 (the only other writers of that value log, or run on the server at a visit's end).

What the missed reset actually corrupts, at this session's geometry: **not the budget**. The drops were 12.9 m (visit 5) and 13.6 m (visit 6) from the pilot, and `ApproachBudget(13) = 20 s`, the floor, with or without the reset. It corrupts `_timeInState` (which still holds every second since `Awake`, i.e. the whole 16–17 s flight), `_approachMoved` (which then charges the carry's displacement to the walk) and `_progress`. PR #42's F5 was built around "a transition INTO Approaching (landing OR the leash re-arming)"; the drop is a third way in, and it is the one every visit uses. **F5's scaled budget has never run on a machine.**

### 1.2 The first regime: visits 1–3, the give-up at the drop

With `_timeInState` carrying the flight and the budget at its 20 s floor, the give-up fires 3–4 s after the drop. That is what the live watch saw on visits 1–3 (the give-up in the same second as the drop; those client lines did not survive the restart). Driven off-game against the real `Core/MerchantPlan.cs` at 50 Hz with a Dverger's `m_walkSpeed` 2: a 16.02 s flight and a 12.7 m drop gives `gave up walking after 20 s` at t = 20.00 s, 8 m moved, 4.8 m short; with the entry reset he reaches the player at t = 20.66 s. Model, not session — but the model matches what was watched.

### 1.3 The second regime: visits 4–6 — unexplained by the code alone

| visit | drop (client clock) | `Destroying valkyrie` | walk-up gave up | where he was |
|---|---|---|---|---|
| 4 | ≈11:18:19 | 11:18:47 (+28 s) | **never** in 64 s; dismissed | no merchant state line at all |
| 5 | ≈11:19:58 | 11:20:43 (+45 s) | after 11:20:52 (≈+55 s) | `moved 597.9 m`, `stopped 148.1 m away` |
| 6 | ≈11:31:28 | 11:31:54 (+26 s) | 11:33:12–11:33:24 (≈+105 s) | `moved 144.1 m`, `stopped 43.3 m away` |

If the write landed at the drop and `Decide` ran every step, the give-up would have come 3–4 s after the drop on these too. It did not. `_timeInState` advances only inside `Decide`, which is owner-gated (`:348`), so **on these three visits `Decide` did not run for 50–100 s of wall clock, or the ZDO write itself landed late** — and the merchant ended up 148 m and 43 m from a pilot he was dropped 13 m from, which no walk from that drop explains. The one client log that survives records neither ownership nor position over time, so the auditors' reconstruction — the merchant rides away with the bird because the pin is not owner-gated (`:347`) while `Decide` is, his persistent ZDO leaves the pilot's one-zone ownership ring (`ZDOMan.ReleaseNearbyZDOS`, `m_activeArea - 1` with the runtime `m_activeArea = 2`), `Decide` stops, and vanilla's `ZSyncTransform` teleports him past 5 m gaps — fits the numbers but is **medium confidence at best**, and the refuters did not accept it as shown. Do not build on it. Build the log line that settles it.

Two things the record got wrong in the meantime, both corrected there: `moved 597.9 m in 20 s` is not a speed (`in 20 s` is the budget; the wall clock was 60–80 s), and staleness of `_lastApproachPos` across unowned ticks UNDER-reports path length (the chord), so it cannot manufacture the 598 m either; that number is real displacement over the unowned stretch, folded in at the next owned tick.

### 1.4 The fix (proposed diff, `Client/CargoMerchant.cs` and `Client/CargoFlight.cs`)

Three hunks, no change to the pure machine and none needed: `MerchantPlan` is right, the caller feeds it stale numbers.

```diff
--- a/ValkyriesCargo/Client/CargoMerchant.cs
+++ b/ValkyriesCargo/Client/CargoMerchant.cs
@@ private void ResolveCarrier()
         {
             ZDO zdo = _nview.GetZDO();
             if (zdo == null) { Pinned = false; return; }
-            _state = zdo.GetInt(Spawner.StateHash, _state);
+            int was = _state;
+            _state = zdo.GetInt(Spawner.StateHash, _state);

             ZDOID carrier = zdo.GetZDOID(Spawner.CarrierKey);
-            if (carrier.IsNone() || ZNetScene.instance == null) { Pinned = false; _pin = null; return; }
-
-            GameObject bird = ZNetScene.instance.FindInstance(carrier);
-            if (bird == null) { Pinned = false; _pin = null; return; }
-
-            CargoFlight flight = bird.GetComponent<CargoFlight>();
-            _pin = flight != null ? flight.AttachPoint : bird.transform;
-            _pinOffset = flight != null ? flight.AttachOffset : new Vector3(0f, 0.3f, 0.4f);
-            Pinned = MerchantPlan.ShouldPin(_state, true);
+            GameObject bird = (carrier.IsNone() || ZNetScene.instance == null) ? null : ZNetScene.instance.FindInstance(carrier);
+            if (bird == null) { Pinned = false; _pin = null; }
+            else
+            {
+                CargoFlight flight = bird.GetComponent<CargoFlight>();
+                _pin = flight != null ? flight.AttachPoint : bird.transform;
+                _pinOffset = flight != null ? flight.AttachOffset : new Vector3(0f, 0.3f, 0.4f);
+                Pinned = MerchantPlan.ShouldPin(_state, true);
+            }
+
+            // A state the PLAN did not choose arrived through the ZDO: the bird's drop
+            // (CargoFlight.Drop writes Approaching), the server's sweep, or another machine's
+            // Decide. It must get the same entry bookkeeping Decide gives its own transitions,
+            // or the first walk-up runs on the flight's clock and the carry's displacement
+            // (StormTest 2026-09-07, 6/6: `budget scaled from 0 m at entry`). Logged once, with
+            // the numbers the session could not give: when it landed relative to waking, whether
+            // the talons still held him, how far out he was, and who owns him.
+            if (_state != was)
+            {
+                float distance = EnterState(_state);
+                ValkyriesCargo.Log.LogInfo("cargo merchant #" + _visitId + ": " + MerchantPlan.Name(was) + " -> " +
+                    MerchantPlan.Name(_state) + " via the ZDO, " + Wire.Float(_age) + " s after waking; carrier " +
+                    (carrier.IsNone() ? "none" : (bird != null ? "still instanced" : "gone")) + ", " +
+                    Wire.Float(distance) + " m from the player, " + (_nview.IsOwner() ? "ours" : "watching") +
+                    ", grounded " + (_character == null || _character.IsOnGround() ? "yes" : "no") +
+                    (_state == MerchantState.Approaching ? "; walk-up budget " + Wire.Float(MerchantPlan.ApproachBudget(distance)) + " s" : ""));
+            }
         }
+
+        /// <summary>
+        /// The one place a state is entered from, whichever way it arrived: the plan's own step in
+        /// Decide, or a value read off the ZDO in ResolveCarrier. Resets the clocks the plan judges
+        /// by and, on entry to Approaching, starts THIS approach's budget and stuck window from where
+        /// he actually is now. Returns the distance to the nearest player, measured here so both
+        /// callers use the same number.
+        /// </summary>
+        private float EnterState(int state)
+        {
+            Player near = Player.GetClosestPlayer(transform.position, 9999f);
+            float distance = near != null ? Vector3.Distance(transform.position, near.transform.position) : float.MaxValue;
+            _timeInState = 0f;
+            _farSeconds = 0f;
+            _approachMoved = 0f;
+            _lastApproachPos = transform.position;      // the carry's displacement is not a walk
+            if (state == MerchantState.Approaching)
+            {
+                _distanceAtApproachEntry = distance;
+                _progress = default;
+            }
+            return distance;
+        }
@@ private void Decide(float dt)
             string diagnosis = (step.TimedOut || step.Stuck) ? WalkDiagnosis(distance) : null;

             _state = step.State;
-            _timeInState = 0f;
-            _farSeconds = 0f;
-            _approachMoved = 0f;
             if (step.LeashFired) _leashSpent = true;      // F5 part 3: spent, never re-arms this visit
-            if (_state == MerchantState.Approaching)
-            {
-                // A fresh budget and a fresh stuck timer for THIS approach (F5), whether it began at
-                // the landing above or at the leash re-arm just above that.
-                _distanceAtApproachEntry = distance;
-                _progress = default;
-            }
+            EnterState(_state);                            // the same bookkeeping as the ZDO path
             ZDO zdo = _nview.GetZDO();
             if (zdo != null) zdo.Set(Spawner.StateHash, _state);
@@ private string WalkDiagnosis(float distance)
             float budget = MerchantPlan.ApproachBudget(_distanceAtApproachEntry);
-            return "moved " + Wire.Float(_approachMoved) + " m in " + Wire.Float(budget) +
-                   " s (budget scaled from " + Wire.Float(_distanceAtApproachEntry) +
+            return "walked " + Wire.Float(_timeInState) + " s of a " + Wire.Float(budget) +
+                   " s budget (scaled from " + Wire.Float(_distanceAtApproachEntry) +
+                   " m at entry), moved " + Wire.Float(_approachMoved) +
                    " m at entry; stuck " + Wire.Float(_progress.StuckSeconds) +
```

(The last hunk's tail needs the obvious re-join of the string; the point is that the line now prints the counted seconds AND the budget, so `moved / seconds` is a speed again.)

```diff
--- a/ValkyriesCargo/Client/CargoFlight.cs
+++ b/ValkyriesCargo/Client/CargoFlight.cs
@@ public void Drop()
                 if (npc != null && npc.IsValid())
                 {
                     npc.Set(Spawner.CarrierKey, ZDOID.None);
                     npc.Set(Spawner.StateHash, MerchantState.Approaching);
                 }
+                else
+                {
+                    // Spawner.Merchant is server-side state, so on a client the scan above is the
+                    // only lookup, and a miss used to be silent. He stays pinned until this bird is
+                    // gone and then lands on his own (MerchantPlan: carried -> landed once the
+                    // carrier no longer resolves), so the visit survives; but the walk-up starts
+                    // from wherever the bird left him, and the log must say so.
+                    ValkyriesCargo.Log.LogWarning("cargo flight #" + _visitId + ": the drop found no merchant naming this bird as his carrier " +
+                        "among " + Character.GetAllCharacters().Count + " instanced characters; he stays carried until the bird is gone");
+                }
```

Why not a retry: `CutCarry`-style retries were proposed and refuted — an unconditional rewrite of `VCargo_state = Approaching` that lands after another machine has walked him to Trading drags him backwards, which breaks the machine's forward-only rule (`MerchantPlan.cs:14-17`), and the miss already self-heals through `landed`. If a retry is ever wanted, guard it on the ZDO's current state being `Carried`.

Why not stop the clock while airborne: also proposed and refuted — gating `_timeInState` on `IsOnGround()` makes Approaching terminal for a merchant who never reads grounded (water, wedged geometry), and the leash cannot rescue him from Approaching. With the entry reset the gate is unnecessary: the clock starts at zero when the state does.

**Harness:** none for this diff — the reset is Unity-side bookkeeping and the pure machine is unchanged. The proof is the next session's log: on every visit, one `Carried -> Approaching via the ZDO, N s after waking …` line with a non-zero distance and a budget, then either `reached the player` or a diagnosis that reads `walked 20 s of a 20 s budget`. If visits still show the second regime, that line's "carrier still instanced / gone", "ours / watching" and the `N s after waking` beside the drop's own timestamp are what settle §1.3.

## 2. D3 — the reclaim works; the sweep double-counts it

**Mechanism** (confirmed in the decompile by auditor and refuter alike): `VisitDirector.End` takes the deferred branch when a merchant is bound (`Server/VisitDirector.cs:509`: send the vanish, wait `VanishGraceSeconds` = 2 s); `FinishDeparture` (`:537-543`) then calls `Spawner.ClearIfStillOurs` and `Spawner.Sweep` **in one synchronous call**. `Reclaim` (`Server/Spawner.cs:397-412`) does `SetOwner(ZDOMan.GetSessionID())` — synchronous, `IsOwner()` true one line later — and `ZDOMan.DestroyZDO`, which in 0.221.12 only adds the id to `m_destroySendList` (`ZDOMan.cs:630-636`). Removal from `m_objectsBySector` / `m_objectsByID` happens in `HandleDestroyedZDO` (`:664-695`), reached from `SendDestroyed` on the next `ZDOMan.Update` (`:515-523`). So the sweep's walk finds the merchant Clear just destroyed, still tagged `VCargo_ingvar`, counts it as stranded, and queues the same destroy again (harmless). Six visit ends, six lines (`server-LogOutput.log` 4663, 4674, 4800, 4814, 4839, 4858). Nothing between `End` and `FinishDeparture` touches `Spawner.VisitId` or `Merchant`; the `Reassert` stagger in `CargoMerchant` never calls `ClaimOwnership`; the "no-op for a non-owner" theory in the proofs record was wrong and is withdrawn.

**Fix.** Two diffs were proposed. A `LastReclaimed` skip list was refuted twice for the same reason: `Reclaim` swallows its own exceptions, so a reclaim that FAILED would be skipped by the very sweep that exists to catch it. The variant with no blind spot: run the sweep one director tick later, after `ZDOMan.Update` has removed the ZDO, and name the sweep per call site.

```diff
--- a/ValkyriesCargo/Server/VisitDirector.cs
+++ b/ValkyriesCargo/Server/VisitDirector.cs
@@ fields
+        /// <summary>The visit whose departure finished last tick; its sweep runs THIS tick, after
+        /// ZDOMan.Update has actually removed what Clear destroyed (DestroyZDO only queues).</summary>
+        private int _pendingSweepFor;
@@ Tick(...)   // beside the existing _pendingClearAt handling
+            if (_pendingSweepFor != 0)
+            {
+                int endedId = _pendingSweepFor;
+                _pendingSweepFor = 0;
+                int liveNow = _session.Active ? _session.VisitId : 0;
+                string swept = Spawner.Sweep(liveNow, "after visit #" + endedId);
+                if (swept != null) ValkyriesCargo.Log.LogInfo(swept);
+            }
@@ private void FinishDeparture(int endedVisitId)
         {
-            Spawner.ClearIfStillOurs(endedVisitId);
-            int liveNow = _session.Active ? _session.VisitId : 0;
-            string swept = Spawner.Sweep(liveNow);
-            if (swept != null) ValkyriesCargo.Log.LogInfo(swept);
+            bool reclaimed = Spawner.ClearIfStillOurs(endedVisitId);
+            ValkyriesCargo.Log.LogInfo("visit #" + endedVisitId + ": " + (reclaimed
+                ? "merchant and bird reclaimed and their destroy queued (it lands on the next ZDOMan.Update)"
+                : "nothing bound to reclaim"));
+            // F4's belt-and-braces, one tick later: a sweep in THIS call would find the merchant
+            // the reclaim just destroyed still in the sector table and report him as stranded
+            // (StormTest 2026-09-07, every visit end).
+            _pendingSweepFor = endedVisitId;
         }
--- a/ValkyriesCargo/Server/Spawner.cs
+++ b/ValkyriesCargo/Server/Spawner.cs
-        public static string Sweep(int liveVisitId)
+        public static string Sweep(int liveVisitId, string when)
 ...
-                return "restart sweep: " + stranded + " stranded merchant(s) destroyed" +
+                return when + " sweep: " + stranded + " stranded merchant(s) destroyed" +
```

with the boot call sites passing `"boot"` and `"boot, after giving up on the carry"`. After the fix a clean visit end reads `visit #N: merchant and bird reclaimed …` and **no sweep line at all** (Sweep returns null when nothing was found); a sweep line after a visit end then means something really was left behind. The harness is untouched (`Spawner` is outside it); the one-tick deferral is the same shape as the existing `_pendingClearAt`.

Note for the docs: every quoted `restart sweep: 1 stranded merchant(s) destroyed` in CLAUDE.md and the proofs record becomes historical once the label changes; the proofs record says so.

## 3. D2 — the cooldown key

> **Built 2026-09-07 evening as PR #48**, as below, with the harness checks and the mutation proof in the PR. §4 the same.

**Finding** (code high, consequence narrower than first recorded). `VisitDirector.Gather` builds `Candidate.Uid = zdo.GetOwner()` (`Server/VisitDirector.cs:672`); `Scheduler` stamps and checks `_playerCooldownUntil` on that long (`Core/Scheduler.cs:181, 195, 245-254`) and persists it as `cool\t<uid>\t<remaining>` (`:288`). `zdo.GetOwner()` is the ZDO's CURRENT owner (`ZDO.cs:1491-1498`, 0 when none is recorded), which equals the client's `ZDOMan.m_sessionID` here only because a client owns its own character ZDO — and that session id is minted per **world join**, not per process: the server log shows one Steam id under three uids (`-794915846`, `860278520`, `73796573`) with the second and third joins 12 s apart inside one client process. Hence three `cool` rows for one player.

**Why it did not bite today:** `StampCooldown` also stamps a base cooldown at the dispatch point, 60 m for the same 3600 s (`Scheduler.cs:248`, checked at `:196`, persisted as `coolbase`), and all six dispatches were within 2 m of each other. The hole opens for a player who relogs AND rolls more than `CooldownRadius` from every live dispatch point — a second base. Narrow, real, and `roll: no eligible player: 1 on cooldown` (`server-LogOutput.log:4813`) only reads that way because the player bucket is checked before the base bucket.

**Which identity.** `ZDOVars.s_playerID` is on the character ZDO the server already reads, is written once by `Player.SetPlayerID` beside `s_playerName`, and survives relogs (PLAYER-IDENTITY-FACTS §1–2). It is per **character profile**, not per human: a second character on the same account dodges it. The per-human key is the platform id (`Steam_7656…`, what the export and the trader ledger already use), which lives on the peer's socket, not the ZDO, and would need `ZNet.GetPeer(zdo.GetOwner())` in `Gather`. Recommendation: `s_playerID` now (zero plumbing, the same shape as the ledger's fallback), the platform id if a second-character bypass ever matters.

```diff
--- a/ValkyriesCargo/Core/Scheduler.cs
+++ b/ValkyriesCargo/Core/Scheduler.cs
@@ public sealed class Candidate
         public long Uid;
+        /// <summary>`ZDOVars.s_playerID` off the character ZDO: the same long after a relog. `Uid` is
+        /// the ZDO's current owner, a per-world-join session id (three `cool` rows for one player,
+        /// StormTest 2026-09-07). 0 for a character that has not been given one yet.</summary>
+        public long PlayerId;
 ...
+        /// <summary>What a cooldown is kept under: the stable identity when there is one, the session
+        /// uid when there is not (a character mid-load, or a bare stub in the harness).</summary>
+        public long CooldownKey { get { return PlayerId != 0L ? PlayerId : Uid; } }
@@ Dispatch
-            StampCooldown(d.Pilot.Uid, d.Pilot.X, d.Pilot.Z, now);
+            StampCooldown(d.Pilot.CooldownKey, d.Pilot.X, d.Pilot.Z, now);
@@ Bucket
-            if (!skipCooldowns && OnPlayerCooldown(c.Uid, now)) return OnCooldown;
+            if (!skipCooldowns && OnPlayerCooldown(c.CooldownKey, now)) return OnCooldown;
--- a/ValkyriesCargo/Server/VisitDirector.cs
+++ b/ValkyriesCargo/Server/VisitDirector.cs
@@ Gather
                     Uid = zdo.GetOwner(),
+                    PlayerId = zdo.GetLong(ZDOVars.s_playerID, 0L),
@@ Describe
-                   (_scheduler.OnPlayerCooldown(c.Uid, now) ? " on cooldown" : "")
+                   (_scheduler.OnPlayerCooldown(c.CooldownKey, now) ? " on cooldown" : "")
--- a/ValkyriesCargo/EngineCheck.cs
+++ b/ValkyriesCargo/EngineCheck.cs
@@ CheckComfort
             NeedField(typeof(ZDOVars), "s_playerName", typeof(int), true, bad);
             NeedHash(typeof(ZDOVars), "s_playerName", "playerName", bad);
+            NeedField(typeof(ZDOVars), "s_playerID", typeof(int), true, bad);
+            NeedHash(typeof(ZDOVars), "s_playerID", "playerID", bad);
 ...
-            return 13;
+            return 15;
--- a/ValkyriesCargo/Core/EngineProbes.cs
-            Declare(Comfort, 8, "... ZDOVars.s_baseValue / s_dead / s_playerName",
+            Declare(Comfort, 8, "... ZDOVars.s_baseValue / s_dead / s_playerName / s_playerID",
```

`Scheduler.Force` keeps matching on `c.Uid` — the admin's uid arrives from the socket, i.e. the live session, and Force skips cooldowns anyway. `s_playerID` is not in `ZDOHelper.s_stripOldLongData`, so the server's replica carries it. Old sidecar `cool` rows need no migration: they are opaque longs no live candidate will present again and they expire inside `PlayerCooldownSeconds`. The probe rows are not optional: every ZDOVars key `Gather` reads is declared in `CheckComfort`, and a key read without a probe is exactly what the registry exists to forbid. The sidecar format does not change (`docs/ENGINE-PROBES.md` §8's "a NEWER format is refused" is untouched).

Harness (`tests/CoreTests/Program.cs`, after the `cool` block at ~1811; the existing checks pass unchanged because the `Player(...)` helper leaves `PlayerId` at 0 and `CooldownKey` falls back to `Uid`):

```csharp
Scheduler relog = new Scheduler(SchedulerRules.Default);
Candidate before = Player(-794915846, "Nomadtest", 0f, 0f); before.PlayerId = 4242;
Candidate after  = Player( 860278520, "Nomadtest", 0f, 0f); after.PlayerId  = 4242;
relog.StampCooldown(before.CooldownKey, 0f, 0f, 1000);
Check(relog.OnPlayerCooldown(after.CooldownKey, 1000), "a relog does NOT clear the cooldown: the key is s_playerID, not the session uid (StormTest 2026-09-07, three cool rows for one player)");
Check(!relog.OnPlayerCooldown(after.Uid, 1000), "and the session uid is no longer a key at all");
Check(Player(77, "x", 0f, 0f).CooldownKey == 77, "a character with no s_playerID yet falls back to the session uid");
```

## 4. D4 — two wording lines

**D4a.** `ZNet.OnNewConnection` adds a peer to `m_peers` at socket accept; `m_uid` and `m_playerName` are filled only by `ZNet.RPC_PeerInfo` (`ZNet.cs:931-933`), about 7 s later in this session. `AdminRpc.SweepPeers` (`Net/AdminRpc.cs:74-82`) and `DealWire`'s sweep register on the accept tick, so the line names nobody. The engine's own predicate is `ZNetPeer.IsReady()` (`ZNetPeer.cs:36-39`; this repo already uses it in `Libs/ServerSync.cs:747`). Gate the registration on it — and count `live` BELOW the gate, or the "someone left" guard (`_serverSide.Count > live`) stops meaning what its comment says:

```diff
--- a/ValkyriesCargo/Net/AdminRpc.cs
+++ b/ValkyriesCargo/Net/AdminRpc.cs
@@ SweepPeers
             foreach (ZNetPeer peer in peers)
             {
                 if (peer == null || peer.m_rpc == null) continue;
+                // A peer has no uid or name until ZNet.RPC_PeerInfo; registering on the accept tick
+                // wrote `admin wire registered for ? (0)` on 2026-09-07. This sweep runs every tick,
+                // so waiting for the peer to say who it is costs nothing and the line names them.
+                if (!peer.IsReady()) continue;
                 live++;
                 if (_serverSide.Contains(peer.m_rpc)) continue;
```

and the same two lines in `DealWire`'s sweep, above its `live.Add`.

**D4b.** `CargoTick.EndSession` (`Core/CargoTick.cs:180-195`) prints one fixed sentence on every machine; on a client `_director` is null (built only under `znet.IsServer()`, `:118-121`) and there is no client-side sidecar. `if (_director != null) _director.Flush(...)` shows the author knew. Branch the line: `"session ended: " + (hadDirector ? "sidecar flushed; director, " : "") + "wire, reporter, routed RPCs and the terminal surface dropped"`, with `bool hadDirector = _director != null;` taken before the null-out. The session's capture could not show the line (the client quit rather than leaving to the menu, and the first boot's log was overwritten); the proofs record quotes it from the live watch.

## 5. The eleven merged fixes against the session

The fix PRs (#30, #37, #38, #39, #41, #42) were verified against the harness. The session ran the build that contains all of them. Verdicts, most of them cheap to close:

| id | what the fix claims | verdict | the line, or what would exercise it |
|---|---|---|---|
| **F1** | with no merchant instanced, vanilla `RPC_Damage` runs | **not exercised** | neither log records damage being applied to anything, all session; only the install is proven (`patches 18/18 applied`). Needs a screen: with no visit running, hit a creature, take a fall |
| **F2** | hover text/name postfixes draw the crosshair | **not exercised** (installed) | `Patch_Character_Hover` is among the 18 applied; `terminal opened … on Dverger(Clone)` proves Interact, which worked before the fix. Still a screen question |
| **F3** | the server sends `VCargo_vanish` and defers the reclaim 2 s; the arrival index is broadcast | **half confirmed** | the server half fired 6/6: the sweep never shares a tick with the `ended` line, which only happens down the `SendVanish` + grace branch. The client half (`RPC_Say`, `RPC_Vanish`) does not log and no `threw` warning appeared; the bubble and the vanish are screen questions |
| **F4** | rebind on adopt after a restart; sweep after every departure | **contradicted in part** | the rebind never ran (one `Load world:` line all session; the mid-visit event was a CLIENT relog, not a restart). The sweep half runs and misreports — D3 |
| **F5** | budget scaled from the distance at entry; a 3 s stuck detector; the leash spent once | **contradicted** | `budget scaled from 0 m at entry` 6/6 — D1. The leash-once part did run (`the player left: … for 5.02 s`, then a second approach, never a third) |
| **F6** | second prefix on the 4-arg `ApplyDamage` closes the DoT hole | **not exercised** (installed) | nothing burned, smoked or poisoned him; a visit forced onto a hearth |
| **F7** | unknown ground → leave the altitude alone | **confirmed** | `ground unknown at (…); the altitude is left alone` appears once per flight AFTER the drop line, on the outbound leg, 3/3 in the surviving client log; every inbound raycast succeeded; all six drops came back with the authored X and Z bit-identical and only Y replaced by terrain. The predicted runaway climb never happened |
| **F8** | `if (zdo == null) zdo = ZNetView.m_initZDO;` in both Awake patches | **not distinguishable** | the relog adopted the merchant correctly (`awake as trading, ours, body=Ingvar`), but nothing says which path supplied the ZDO; one `LogInfo` on the fallback branch would |
| **F9** | `LiveCount++` first; `_counted` on `OnDestroy` | **not exercised** | only differs when `Awake` throws, and `Awake threw` appears nowhere; a deliberate throw is the only test |
| **F10** | `Reassert` split into a local half and an owned half | **half confirmed** | `tamed yes` in both walk diagnoses: the owned half reached the owner. The local half needs a second client standing beside him |
| **F11** | ghost mode: `IsEnemy` prefix | **not exercised** (installed) | `Patch_BaseAI_IsEnemy` is among the 18; no hostile came near. Item 25: a raid or a greydwarf pack, and the enemy HUD bar |
| **N1** | `FlightPlan.Make`'s descent slide is unreachable at `activeArea ≥ 2` | **confirmed** | all six plans have `DescentDistance == min(50, 0.75·run)` exactly (`66 m out` → `49.5 m short`, `65.064 m out` → `48.798 m short`, the rest `50 m short`); a slide would have added a 12 m `ShrinkStep` |

Two machine facts worth keeping from the same pass: `ZoneSystem.m_activeArea == 2` live, derived from the plans (visits 4 and 5 shrank the 90 m start to 78 m without turning the bearing, which only happens at exactly 2); and `patches 18/18` is the nine Harmony classes in `Patches/` plus ServerSync's eight and `UIFocusPatch`, so F2's, F6's and F11's patch classes all bound on a real 0.221.12 client AND a real dedicated server — the install half of three fixes whose behaviour was never exercised.

One code observation from this pass, **not** a cause of anything seen: `MerchantPlan.AccumulateProgress` sums per-tick magnitudes (`Core/MerchantPlan.cs:110-119`) while its own doc comment and the harness comment (`tests/CoreTests/Program.cs:3814`) promise NET displacement per window. `stuck 0 s` on both failed walk-ups is the CORRECT reading either way (both merchants were moving). A merchant pacing in place would read as progressing under the sum; a net window would catch him. Fix the comment or the code; the refuter's note is that a net window must be driven from wall clock, not owner ticks, or the same owner-gate gap that hides `_timeInState` hides it too.

## 6. For P10a: Steam already has the pre-1.0 branch

As of 2026-09-07, `steamcmd +app_info_print` shows on **both** apps a new branch `default_pre1_0`, description "Last stable build before 1.0", pinned to today's builds (client 892970: 21981559; server 896660: 21981590), beside `default_old`, `default_preal`, `default_prebw`, `default_precta`, `default_preml`. No branch carries a 1.0 build yet; `public` is still today's. Two things follow for your work: after 1.0 lands, the 0.221.12 shadows for the comparative decompile can be re-fetched from `default_pre1_0` forever (`-beta default_pre1_0`, no password), and a server can pin itself there if the mod ever needs a day. The baseline the probe registry is built against is therefore reproducible without keeping the shadow copies.

## 7. What is NOT in this audit

The screen questions (the release over the drop point, the fall, the vanish, the bubble, the hover prompt, the terminal's look, daylight, the slope) — a recording of one visit answers most of them. The two-client items. Yggdrasil's Reckoning, whose source is not on this machine. And anything that needs the game running: the six "not exercised" fixes above have one-line recipes each, all for the next session.
