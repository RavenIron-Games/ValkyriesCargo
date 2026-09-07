# SoM "brain" survey — StealthBrain / Awareness / Overmind / Behavior

All line cites are `file:line` against the current working tree. Vanilla cites are `assembly_valheim.decompiled.cs:LINE` (client decompile at `c:/WubarrkCODING/libs-Tools/DECOMPILED ASSEMBLY VALHEIM/`).

---

## 1. The decision pipeline, exactly

### 1.1 Who drives it

There is exactly **one** driver and **one** opportunistic fallback.

| Driver | Site | Cadence |
|---|---|---|
| `StealthOvermind.EvaluateLoop()` coroutine | `Systems/StealthOvermind.cs:33`, started from `CoroutineManager.Start()` at `Systems/CoroutineManager.cs:45` | One pass per Unity frame, ≤5 evaluations per frame |
| `Humanoid_Attack_Patch.Prefix` fallback | `Patches/Humanoid_Attack_Patch.cs:73-74` — `if (!TryGetCachedDecision(...)) decision = StealthBrain.Evaluate(monster, p, Time.deltaTime)` | Only on cache miss, at vanilla attack-start rate |

`BehaviorSystem` **never** calls `Evaluate`. `CoroutineManager.Bootstrap()` (`ShadowsOfMidgard.cs:40`) is what makes the coroutine host exist at all.

### 1.2 Overmind pass (`StealthOvermind.cs:33-135`)

```
loop forever:
  p = Player.m_localPlayer                                   // :39   <-- SINGLE PLAYER, BAKED IN
  if (p == null || p.IsDead()) { WaitForSeconds(1f); continue }
  tracked.Clear()                                            // :49
  foreach c in Character.GetAllCharacters():                 // :52,55  ALL loaded chars, no spatial index
      skip null / IsDead / is Player                         // :57
      skip IsTamed                                           // :62
      dist = Vector3.Distance(c.pos, playerPos)              // :64   sqrt
      skip dist > 64f (MAX_TRACKING_RANGE)                   // :65
      skip StealthExemption.IsExempt(c)                      // :68   GetComponent<ZNetView>+ZDO.GetInt(string)
      monster = c.GetComponent<MonsterAI>(); skip null/IsSleeping  // :71-72
      skip !AIAuthority.IsAuthoritative(monster)             // :75   ANOTHER GetComponent<ZNetView>
      tracked.Add(new EvaluatedCharacter{...})               // :78   HEAP ALLOC PER CREATURE PER FRAME
  foreach entry in tracked:                                  // :89
      data = AwarenessSystem.GetData(entry.AI)               // :94   inserts on miss
      data.FramesSinceLastEval++                             // :98
      tier: <=25m -> every 2 passes; <=50m -> 6; else 20     // :101-106
      dt = Clamp(Time.time - data.LastEvalTime, Time.deltaTime, 0.5f)  // :115-117
      StealthBrain.Evaluate(entry.Monster, p, dt)            // :119
      if (++processedThisFrame >= 5) { yield null; reset }    // :126-130
  yield null                                                 // :133
```

Note the `yield return null` at `:128` is **inside** the `foreach`, so on resume the loop continues the same `tracked` list with **stale `DistanceToPlayer` values** and stale `Character` refs (re-null-checked at `:91` only).

### 1.3 `StealthBrain.Evaluate` (`Systems/StealthBrain.cs:51-133`) — 10 phases

```
:53   decision = new StealthDecision()          <-- allocated BEFORE the cache-hit early-out
:56   null guards (ai, p, cfg)
:57   c = ai.GetComponent<Character>()
:58   bail if dead / IsSleeping
:60   data = AwarenessSystem.GetData(c)          <-- Dictionary<Character,...>, inserts on miss
:63   key = GetStableAIKey(ai)                   <-- GetComponent<ZNetView> + GetZDO
:67   every 600 frames -> CleanupStaleCache
:73   if cached.LastFullEvalFrame == currentFrame -> return cached.Decision  (throws away the :53 alloc)

P1 :80  SensingEvaluator.EvaluateSensing
P2 :83  StateEvaluator.EvaluateStateTransitions
P3 :86  data.SelfHealthPercent / PlayerHealthPercent / TargetDistance
P4 :91  FleeEvaluator.EvaluateFleeing
P5 :93  (no-op comment; ally counts come from the coroutine)
P6 :97  MovementEvaluator.EvaluateMovement
P7 :100 CombatEvaluator.EvaluateCombat
P8 :103 EvaluateGroupBehavior          (:201)
P9 :106 DetermineAction                (:239)  <-- SIDE EFFECT: calls BehaviorSystem.CallNearbyAllies (:259)
P10:109 data.CanSense/CanSee/CanHear = decision.*   <-- the ONLY writer of the 3 patch-visible flags
   :113 FinalizeDecision              (:263)  <-- unconditional interpolated string, :270
   :116 StealthDebugger.LogDetection / :117 LogCombat
   :119 _evalCache[key] = {frame, frame, decision}
   :127 if key.NetworkUid == 0 -> StartManagedCoroutine(MigrateCacheCoroutine)  <-- EVERY eval, unbounded
```

**Phase ordering hazard:** `data.SelfHealthPercent` is written at `:86` (phase 3) but `FleeEvaluator` at `:91` reads it — correct. However `SensingEvaluator` at `:80` already ran `StateEvaluator` inputs, and `EvaluateGroupBehavior` at `:103` reads `data.NearbyAlliesCount` which is written by a *different coroutine* on a different cadence (`CoroutineManager.cs:117`, one creature per frame — see §6.3).

### 1.4 Phase internals worth quoting

**SensingEvaluator (`Systems/StealthBrain/SensingEvaluator.cs:7-78`)** — the whole sensing model:
```csharp
:10  float vis    = cfg.EnableVisibilitySystem ? VisibilitySystem.GetVisibility(p) : 0f;
:11  float noise  = cfg.EnableNoiseSystem      ? NoiseSystem.GetNoise(p)          : 0f;
:12  float hiding = cfg.EnableHidingSystem     ? HidingSystem.GetHidingFactor(p)  : 0f;
:13  float camo   = cfg.EnableCamoSystem       ? CamoSystem.GetCamoFactor(p)      : 0f;
:15  float adjustedVis = vis * (1f - hiding) * (1f - camo);
:16  bool hasLOS = AwarenessSystem.HasLineOfSight(c, p);      // Physics.Linecast, ALWAYS run
:20  visFalloff  = 1 - Clamp01(dist / cfg.MaxVisualRange);    // linear, 40 m
:21  hearFalloff = 1 - Clamp01(dist / cfg.MaxHearingRange);   // linear, 25 m
:28  dot = Vector3.Dot(c.transform.forward, dirToPlayer);
:31  if (dot < cos(VisionConeHalfAngle)) adjustedVis *= peripheral * 0.1f;
:37  bool ignoreLoS = data.CurrentState >= VanillaAlertness.Alerted;   // <-- LOS ignored once alerted
:39  decision.CanSee  = (hasLOS || ignoreLoS) && adjustedVis > cfg.VisionThreshold;
:40  decision.CanHear = adjustedNoise > cfg.HearingThreshold;
:58  data.DetectionLevel = Clamp01(data.DetectionLevel + gain * dt);   // gain = AlertGain / SuspicionGain / -DetectionDecay
:62  if seen/heard: data.LastKnownPosition = p.transform.position; TimeSinceSeen = 0
```
Lines 10-13 are the load-bearing observation for the overhaul: **all four terms are pure functions of the *player*, not of the observing creature**, yet they are recomputed once per (creature, evaluation). See §5.

`hasLOS` at `:16` is computed unconditionally and then discarded when `ignoreLoS` is true — one wasted `Physics.Linecast` for every Alerted/Engaged creature, every evaluation.

**StateEvaluator (`StateEvaluator.cs:13-33`)** — hysteresis ladder, all thresholds from cfg, escalation is monotonic-with-floor, de-escalation is at `AlertedThreshold*0.7` and `SuspiciousThreshold*0.5`, drop to Unaware at a hard-coded `det < 0.02f` (`:32`).

**MovementEvaluator** deliberately emits `Vector3.zero` for `Alerted+CanSense` (`:60-63`) and `Engaged` (`:88-91`) so vanilla's pathfinder owns pursuit. Only `Suspicious`/`Alerted-without-sense` produce a direction, and it is consumed as `decision.SearchTarget` (`:42`, `:72`), not the direction.

**`DetermineAction` (`StealthBrain.cs:239-261`)** maps state→action, then at `:255-260` re-enters `BehaviorSystem.CallNearbyAllies` mid-evaluation, **before** the cache write at `:119`. That helper mutates *other* creatures' `AwarenessData` directly (`BehaviorSystem.cs:198-208`).

### 1.5 Where the decision is consumed

| Consumer | Reads | Site |
|---|---|---|
| `MonsterAI.UpdateAI` Postfix | `TryGetCachedDecision` → `BehaviorSystem.ExecuteAction(ai, decision.RecommendedAction, Player.m_localPlayer, decision)` | `Patches/MonsterAI_StealthBrain_UpdateAI_Patch.cs:35-38` |
| `BaseAI.CanSenseTarget(Character)` Prefix+Postfix | `data.CurrentState >= Alerted ? true : data.CanSense` | `Patches/BaseAI_StealthBrain_Patch.cs:36-44`, `:63-69` |
| `BaseAI.CanSeeTarget(Character)` Prefix+Postfix | `data.CanSee` | `Patches/BaseAI_CanSeeTarget_Patch.cs:37`, `:57` |
| `BaseAI.CanHearTarget(Character)` Prefix+Postfix | `data.CanHear` | `Patches/BaseAI_CanHearTarget_Patch.cs:37`, `:57` |
| `BaseAI.IsAlerted()` Prefix+Postfix | `CurrentState == Alerted \|\| Engaged` | `Patches/BaseAI_StealthBrain_IsAlerted_Patch.cs:23-24`, `:42-43` |
| `Character.SetMoveDir` Prefix | `data.CurrentAction ∈ {Search, Investigate}` → scale `dir` by `cfg.SearchSpeedFactor` | `Patches/BaseAI_SetMoveDir_Patch.cs:27-49` |
| `Humanoid.StartAttack` Prefix | `data.CurrentState`, `data.CurrentFleeReason`, `decision.Combat.ShouldAttack` | `Patches/Humanoid_Attack_Patch.cs:37-80` |
| `StealthUIController.Update` | walks the whole `AwarenessSystem` dictionary for max `CurrentState` | `Systems/SteathUI/StealthUIController.cs:62-86` |

Every one of those patches registers **both a Prefix that returns `false` and a Postfix**. Harmony runs postfixes even when a prefix skips the original, so the entire guard chain (`IsAuthoritative` → `GetComponent<Character>` → `IsTamed` → `IsExempt` → `GetData`) executes **twice per vanilla call**.

---

## 2. Per-creature mutable state: every field, every writer

`AwarenessData` (`Systems/UnifiedStealthTypes.cs:59-106`) is the single per-creature record. There is **no per-player dimension anywhere in the type**.

| Field | Written by | Read by |
|---|---|---|
| `DetectionLevel` | `SensingEvaluator.cs:58`; `BehaviorSystem.cs:203` (ally call, clamp-up to `AlertedThreshold`); `Character_OnDamaged_Patch.cs:35` (max 0.80); `Character_Damage_Patch.cs:34` (max 0.50); **MistsofAvalor via reflection = 1f** (`Compat/AvalorStealthCompat.cs:420`) | `StateEvaluator.cs:11`; debug |
| `LastKnownPosition` | `SensingEvaluator.cs:62`; `BehaviorSystem.cs:206`; `Character_Damage_Patch.cs:39` | `MovementEvaluator.cs:35,37,65,67,72,103` |
| `TimeSinceSeen` | `SensingEvaluator.cs:63,67`; `BehaviorSystem.cs:207`; `Character_OnDamaged_Patch.cs:48`; `Character_Damage_Patch.cs:43` | `MovementEvaluator.cs:35,65,102,105` |
| `CanSense` / `CanSee` / `CanHear` | **only** `StealthBrain.cs:109-111` | the 3 sensing patches; MistsofAvalor holds `FieldInfo`s for all three (`AvalorStealthCompat.cs:137`) |
| `CurrentState` | `StateEvaluator.cs:14,18,23,27,30,33`; `BehaviorSystem.cs:205`; `Character_OnDamaged_Patch.cs:40,44`; `Character_Damage_Patch.cs:30`; **MistsofAvalor = Alerted** (`AvalorStealthCompat.cs:419`) | everywhere; the 5 patches |
| `CurrentAction` | **only** `StealthBrain.cs:242-250` | `BaseAI_SetMoveDir_Patch.cs:30-31`; debug |
| `DesiredDirection`, `DesiredSpeed`, `ShouldRun` | `MovementEvaluator.cs:19-21, 95-97` | **nobody — dead state** |
| `FleeDirection` | `FleeEvaluator.cs:59` | `MovementEvaluator.cs:15` |
| `NearbyAlliesCount`, `IsPartOfGroup` | **only** `CoroutineManager.cs:117-118` (separate coroutine, ~1 creature/frame) | `StealthBrain.cs:205,212,217,220,234` |
| `TimeUntilNextAllyCall` | `StealthBrain.cs:207,214,219,226` | same |
| `TargetPriority` | `CombatEvaluator.cs:23,34,42,50` | `CombatEvaluator.cs:54` only |
| `TargetDistance` | `StealthBrain.cs:88` | **nobody — dead** |
| `PlayerHealthPercent` | `StealthBrain.cs:87` | `FleeEvaluator.cs:71` (debug only) |
| `SelfHealthPercent` | `StealthBrain.cs:86` | `FleeEvaluator.cs:16,27,29,41`; `CombatEvaluator.cs:21` |
| `CurrentFleeReason` | `FleeEvaluator.cs:24,40,46,52` | `Humanoid_Attack_Patch.cs:51` |
| `FleeStartHealth` | `FleeEvaluator.cs:25,41` | **nobody — dead** |
| `TimeSpentFleeing` | `FleeEvaluator.cs:60,64` | **nobody — dead** |
| `TimeInCurrentState` | `StateEvaluator.cs:36,38` | **nobody — dead** |
| `TimeInCurrentAction` | `StealthBrain.cs:253` (`+= Time.deltaTime`, wrong unit — should be the eval `dt`) | **nobody — dead, grows unbounded** |
| `FramesSinceLastEval` | `StealthOvermind.cs:98,121` | `StealthOvermind.cs:102-106` |
| `LastEvalTime` | `StealthOvermind.cs:122` | `StealthOvermind.cs:115-116` |
| `LastDecisionReason` | `StealthBrain.cs:267` | **nobody — dead** |
| `LastConfidence` | `StealthBrain.cs:268` | `StealthBrain.cs:265` — **self-referential loop** |

**Confirmed bug:** `StealthBrain.cs:265` `decision.Confidence = Clamp01(data.LastConfidence)` and `:268` `data.LastConfidence = decision.Confidence`. Nothing else ever writes `LastConfidence`. `SensingEvaluator.cs:44-48` computes `decision.SensingConfidence` and never feeds it in. **`decision.Confidence` is permanently 0.0** and `StealthDebugData.OverallConfidence` (`UnifiedStealthTypes.cs:228`) is never written at all.

**Other dead outputs** (written, never read): `decision.SensingConfidence`, `decision.SearchRadius` (`MovementEvaluator.cs:43,73`), `decision.ShouldPursue`, `decision.TargetPriority`, `Combat.OptimalAttackRange`/`ShouldKeepDistance`/`DamageThreshold`, `GroupBehavior.CoordinatedAttack`/`MinAlliesForGroupAction`, `Flee.FleeTarget`/`ReturnWaitTime`, `decision.FleeConfidence`, `decision.FrameCounter`. `BehaviorSystem.ExecuteCombat` (`:333`), `GetCombatCooldown` (`:419`) and `SetCombatStance` (`:391`, every case is an empty body) are **never called from anywhere**. `cfg.GrassBonus` is bound and synced (`SOMConfig.cs:105-107`) but `VisibilitySystem.GrassBonus` hardcodes `0.15f`/`0.6f` (`VisibilitySystem.cs:81`) — the config knob does nothing.

### 2.1 Damage-path double-write, with vanilla evidence

`Character.Damage(HitData)` (decompiled `:8692-8699`) is **only an RPC sender** — `m_nview.InvokeRPC("RPC_Damage", hit)`. `ZNetView.InvokeRPC` → `ZRoutedRpc.InvokeRoutedRPC` (`:71188`), and at `:71198-71201`:
```csharp
if (targetPeerID == m_id || targetPeerID == 0L) { HandleRoutedRPC(routedRPCData); }
```
i.e. **synchronous inline dispatch when the local machine owns the victim**. So `RPC_Damage` → `ApplyDamage` → `OnDamaged(hit)` (`:8866`) all run *inside* `Damage()`.

Consequence: when a self-owned mob is hit, Harmony fires `Character_OnDamaged_Patch.Postfix` (sets `CurrentState = Engaged`, `Character_OnDamaged_Patch.cs:40`) **before** `Character_Damage_Patch.Postfix`, which then unconditionally executes `data.CurrentState = VanillaAlertness.Alerted;` (`Character_Damage_Patch.cs:30`). **Every player hit on a self-owned mob downgrades Engaged→Alerted.** In pure-client MP the `Damage` postfix is usually filtered out by `AIAuthority` (the attacker rarely owns the victim), so this reproduces in single-player and on a player-host — i.e. the configurations most people test in.

---

## 3. Every place a single `Player` is baked in

This is the §2 brief item. The exhaustive list:

1. **`StealthOvermind.cs:39`** — `Player p = Player.m_localPlayer;`. The entire tracking pass is against one player. `p == null || p.IsDead()` → the brain **halts completely** (`:41-44`) — on a dedicated server this is the permanent state.
2. **`StealthOvermind.cs:64`** — `dist = Vector3.Distance(character.transform.position, playerPos)`, the tier and the 64 m cull are distance-to-local-player, not distance-to-nearest-player.
3. **`StealthBrain.Evaluate(MonsterAI ai, Player p, float dt)`** — `StealthBrain.cs:51`. The signature itself. `p` is threaded into all five evaluators.
4. **`SensingEvaluator.cs:10-13`** — all four sensing terms take `Player p`.
5. **`SensingEvaluator.cs:62`** — `data.LastKnownPosition = p.transform.position` — one last-known-position per creature, not per target.
6. **`StealthBrain.cs:87-88`** — `PlayerHealthPercent`, `TargetDistance` from `p`.
7. **`FleeEvaluator.cs:57`** — flee direction is away from `p`.
8. **`MovementEvaluator`** — takes `Player p` (`:7`) though it only uses `data`.
9. **`CombatEvaluator`** — takes `Player p` (`:10`), unused.
10. **`StealthBrain.cs:259`** — `BehaviorSystem.CallNearbyAllies(caller, pos, radius, p)` — force-targets every ally onto that one player.
11. **`BehaviorSystem.CallNearbyAllies(..., Player target)`** — `BehaviorSystem.cs:175`; `SetTarget(monsterAI, target)` at `:194` and `allyData.LastKnownPosition = target.transform.position` at `:206`.
12. **`BehaviorSystem.ExecuteAction(..., Player target, ...)`** — `BehaviorSystem.cs:238`; `SetTarget(ai, target)` at `:268` (Pursue) and `:274` (Attack).
13. **`MonsterAI_StealthBrain_UpdateAI_Patch.cs:25`** — `Player player = Player.m_localPlayer;` then `:38` `ExecuteAction(__instance, decision.RecommendedAction, player, decision)`. **This force-writes `m_targetCreature = local player` on every owned, alerted creature at 20 Hz**, regardless of who the creature was actually fighting. On a player-host this is a permanent aggro magnet pointed at the host; on a client it re-steals every mob that client owns onto that client's own player.
14. **`CoroutineManager.cs:67,70,79,80`** — the vegetation sampler only ever samples `Player.m_localPlayer` and stores it in `Dictionary<Player,int>`.
15. **`StealthUIController.cs:25,53-55`** — local-player UI (legitimate, but it re-runs the same player-global sensing every frame; see §5).
16. **`Humanoid_Attack_Patch.cs:66-68`** — the *only* correct site: `Character target = monster.GetTargetCreature(); Player p = target as Player;`. It then calls `StealthBrain.Evaluate(monster, p, Time.deltaTime)` (`:74`) which writes the same single `AwarenessData` with a **different player** and a **different `dt`** than the Overmind — two writers racing on one record with incompatible integration timesteps.

### 3.1 Why this is not just "single-player-shaped" but actively wrong in MP

`BaseAI_CanSeeTarget_Patch.Prefix` (`:16`) receives `Character target` and **never reads it** — it returns `data.CanSee` (`:37`). Vanilla's `MonsterAI.FindEnemy` (decompiled `:5191-5212`) iterates **every loaded `Character`** and calls the instance `CanSenseTarget(item)` per candidate:

```csharp
foreach (Character item in allCharacters) {
    if (!IsEnemy(m_character, item) || item.IsDead() || item.m_aiSkipTarget) continue;
    BaseAI baseAI = item.GetBaseAI();
    if ((!(baseAI != null) || !baseAI.IsSleeping()) && CanSenseTarget(item)) {   // <-- SoM's prefix
        float num2 = Vector3.Distance(item.transform.position, base.transform.position);
        if (num2 < num || character == null) { character = item; num = num2; }
    }
}
```

So with SoM installed, if the owner-client's local player is detected, `CanSenseTarget` returns `true` for **every candidate**, and FindEnemy resolves purely on `Vector3.Distance` — **the geometrically closest character wins, including a fully-sneaking second player, and including non-player enemies**. Conversely, if the local player is hidden, a second player sprinting in plate is invisible. That is the central bug, confirmed at the vanilla call site.

`BaseAI.CanSenseTarget(Transform, Vector3, …)` — the **static** overload at decompiled `:4529` — is used by `BaseAI.FindClosestCreature` (`:5225`, `:5266`). SoM patches only the **instance** overload, so `FindClosestCreature` (used by `MonsterAI` variants and by `Tameable`/`SpawnSystem`-adjacent code at `:127754`) bypasses SoM entirely. Asymmetric coverage.

`BaseAI_StealthBrain_IsAlerted_Patch` has **no target parameter at all**, so a local-player-derived alert state changes sensing for *all* targets: vanilla's static `CanSeeTarget` at `:4582` uses the `alerted` flag to skip the FOV test (`:4606`), so SoM's local-player alertness silently grants 360° vision against every other character.

### 3.2 Ownership handover wipes all state

`AwarenessData` lives only in a static `Dictionary<Character, AwarenessData>` on the owning client (`AwarenessSystem.cs:9`). Nothing is written to the ZDO. Per `VALHEIM-DEDICATED-SERVER-FACTS.md`, `ZDOMan.ReleaseNearbyZDOS` reassigns ownership every ~2 s as players move. When creature X moves from player A's area to player B's:

- On B's client `AwarenessSystem.GetData(X)` misses and **creates a fresh `AwarenessData` with `CurrentState = Unaware, DetectionLevel = 0`** (`AwarenessSystem.cs:13-19`).
- The creature instantly forgets it was chasing anyone.
- `AIAuthority.IsAuthoritative` (`AIAuthority.cs:18`, `nview.IsOwner()`) flips, so A stops simulating and B starts from zero.

This is a hard MP break **independent** of the per-player-track bug and cannot be fixed by adding tracks alone — the tracks must be replicated (ZDO) or reconstructed.

### 3.3 `AIAuthority` server fallback is wrong for the dedicated case

```csharp
// Systems/AIAuthority.cs:14-18
ZNetView nview = ai.GetComponent<ZNetView>();
if (nview == null) return ZNet.instance != null && ZNet.instance.IsServer();
return nview.IsOwner();
```
The `nview == null` branch claims authority for the server. Per the dossier the server has **no creature instances near players at all**, so this branch is either unreachable or grants authority to the wrong machine. `ZNetView.IsOwner()` is the right rule; the fallback should be `false`.

---

## 4. Hot-path allocations — exhaustive

`LangVersion=latest` on `net472` (`ShadowsOfMidgard.csproj:61-62`). `DefaultInterpolatedStringHandler` does not exist in the net472 reference set, so **every `$"..."` compiles to `string.Format(string, object[])` with boxing**. This matters a great deal below.

### 4.1 Per `StealthBrain.Evaluate` call

| # | Allocation | Site | Notes |
|---|---|---|---|
| 1 | `new StealthDecision()` | `StealthBrain.cs:53` | class; ~250-300 B incl. the fat `StealthDebugData` struct (3× `Vector3?`, 7× `float?`, 3 nullable enums, 3 string refs). **Allocated before the cache-hit early-out at `:73`, so a cache hit still allocates and discards one.** |
| 2 | **`decision.Debug.FullDecisionTrace = $"[State:{...}\|Det:{...:F2}\|Act:{...}\|HP:{...:P}\|Flee:{...}\|Allies:{...}]"`** | `StealthBrain.cs:270` | **UNCONDITIONAL — not gated on any debug flag.** 6 holes → 6 boxes (2 enums, 2 floats, 1 bool, 1 int) + `object[6]` + 6 intermediate `ToString`/format strings + the final ~70-char string. **≈600-700 B per evaluation, every evaluation, in release, with debug off.** |
| 3 | `$"vis={vis:F2} (light={light:F2} shadow={shadow:F2} grass={grass:F2} weather={weather:F2} move={movement:F2} armor={armor:F2})"` | `VisibilitySystem.cs:40` | **UNCONDITIONAL** — the argument is built at the call site before `LogPlayerSensing` can check `VisionDebugEnabled` (`StealthDebugger.cs:236`). 6 float boxes + array + 6 format strings + final ~90 chars ≈ 700 B. |
| 4 | `$"noise={noise:F2} (move=… armor=… crouch=… weather=…)"` | `NoiseSystem.cs:21` | Same pattern. ≈450 B. |
| 5 | `$"hiding={hiding:F2} (bush=… grass=… crouch=…)"` | `HidingSystem.cs:34` | Same. ≈350 B. |
| 6 | `$"camo={camo:F2} (biome=… armor=…)"` | `CamoSystem.cs:55` | Same. ≈250 B. |
| 7 | `p.GetInventory().GetEquippedItems()` ×3 | `ArmorUtils.cs:19` (visibility), `ArmorUtils.cs:52` (noise), `CamoSystem.cs:88` | Vanilla `:57488` does `List<ItemDrop.ItemData> list = new List<…>(); foreach(item in m_inventory) if(item.m_equipped) list.Add(item);` — **a fresh List + full inventory walk (32-40 slots) per call, three times per evaluation.** ≈3 × 120 B + 120 iterations. |
| 8 | `EnvMan.instance?.GetCurrentEnvironment()?.m_name.ToLowerInvariant()` ×3 | `EnvironmentUtils.cs:14,20,26` (via `VisibilitySystem.WeatherReduction:107,111`) | 3 string allocations per evaluation. |
| 9 | `env.GetCurrentEnvironment()?.m_name?.ToLowerInvariant() ?? ""` | `NoiseSystem.cs:53` | 4th string. |
| 10 | `item.m_shared?.m_name?.ToLowerInvariant()` per equipped item | `CamoSystem.cs:95` | ~5 more strings, plus a nested `foreach (kvp in ArmorBiomeMap)` (11 entries) × `itemName.Contains(kvp.Key)` (`:98-105`) ⇒ **~55 substring scans per evaluation**. |
| 11 | `Heightmap.FindHeightmap(p.transform.position)` ×2 | `CamoSystem.cs:62` called from `BiomeMatch:70` **and again** from `ArmorMatch:83` | The biome is computed twice per evaluation. |
| 12 | `Physics.Linecast` | `RaycastUtils.cs:35` via `AwarenessSystem.HasLineOfSight:51` | Non-alloc, but a real 40 m cast against 7 layers — and wasted whenever `ignoreLoS` (`SensingEvaluator.cs:37`) is true. |
| 13 | `Physics.Raycast` (sun/shadow, 20 m) | `VisibilitySystem.cs:68` | Non-alloc. |
| 14 | `CoroutineManager.Instance.StartManagedCoroutine(MigrateCacheCoroutine(ai, aiId))` | `StealthBrain.cs:127-130` | **Fires on EVERY evaluation while `key.NetworkUid == 0`.** Allocates an iterator state machine + 2 `WaitUntil` + 2 closures each time, and the coroutine's first `WaitUntil` (`:137`) can block forever. Unbounded coroutine accumulation on any creature whose ZNetView/ZDO is momentarily unresolved. |

**Structs (no heap):** `new FleeDirective()` `FleeEvaluator.cs:14`, `new MovementDirective()` `MovementEvaluator.cs:10`, `new CombatDirective()` `CombatEvaluator.cs:13`, `new GroupBehavior()` `StealthBrain.cs:203`, `new CachedEvaluation{…}` `StealthBrain.cs:119`. `GenerateDecisionReason`'s `$"Strategic retreat"`/`$"Fleeing"` (`:275`) are hole-free and fold to constants.

**Total: ≈2.5-3.5 KB of garbage and ~20-40 µs of CPU per evaluation**, of which **~2.4 KB is debug string formatting that is never printed.**

### 4.2 Per Overmind pass (every frame)

- `new EvaluatedCharacter { … }` per tracked creature — `StealthOvermind.cs:78-83`. 32 B × N × 60/s. **This is the only reason `EvaluatedCharacter` is a class**; it could be a struct in a pooled `List<>`.
- `Character.GetAllCharacters()` returns the live `s_characters` list by reference (decompiled `:10316-10319`) — no alloc, but the loop is O(all loaded characters).
- Per candidate: `StealthExemption.IsExempt` → `GetComponent<ZNetView>()` + `zdo.GetInt("SoMStealthExempt", 0)`, and `ZDO.GetInt(string)` is `GetInt(name.GetStableHashCode(), …)` (decompiled `:62713-62715`) — **a 16-character stable-hash walk on every call**, no cached hash anywhere in SoM.
- Per candidate: `GetComponent<MonsterAI>()` and a **second** `GetComponent<ZNetView>()` inside `AIAuthority.IsAuthoritative`.

### 4.3 Per `MonsterAI.UpdateAI` tick

Vanilla drives AI from `MonoUpdaters.FixedUpdate` at a **fixed 20 Hz with `dt = 0.05f`**, not per-frame (decompiled `:60994-60999`):
```csharp
m_updateAITimer += fixedDeltaTime;
if (m_updateAITimer >= 0.05f) { m_ai.UpdateAI(BaseAI.Instances, "MonoUpdaters.FixedUpdate.BaseAI", 0.05f); m_updateAITimer -= 0.05f; }
```
`BaseAI.UpdateAI` early-returns `false` for non-owners (`:4111-4115`) but the Harmony **Postfix still runs**. Per creature per tick, `MonsterAI_StealthBrain_UpdateAI_Patch.Postfix` costs:
`AIAuthority` `GetComponent<ZNetView>` → `GetComponent<Character>` → `StealthExemption.IsExempt` (`GetComponent<ZNetView>` + string hash + ZDO lookup) → `TryGetCachedDecision` → `GetStableAIKey` (**third** `GetComponent<ZNetView>` + `GetZDO`) → `ExecuteAction`.
**Four `GetComponent` calls and two ZDO string-hash lookups per creature per 20 Hz tick, for every loaded creature including remote-owned ones.**

### 4.4 `BehaviorSystem.CallNearbyAllies` (`BehaviorSystem.cs:175-216`)

```csharp
:180  Collider[] nearby = Physics.OverlapSphere(position, radius);   // ALLOCATING overload, 30 m
:183  var names = new List<string>();                                // UNCONDITIONAL
:187  Character character = col.GetComponent<Character>();           // per collider
:190  MonsterAI monsterAI = character.GetComponent<MonsterAI>();     // per candidate
:209  try { names.Add(character.name); } catch { }                   // UNCONDITIONAL Object.name -> native marshal, new string
:215  StealthDebugger.LogAllyCall(caller, target, radius, nearbyCount, affected, string.Join(",", names));  // UNCONDITIONAL string.Join
```
`Physics.OverlapSphere` at 30 m in a base/raid returns 50-200 colliders → a `Collider[]` of 400-1600 B **plus** one marshalled `name` string per affected ally **plus** the joined string, all built even when `AIDebugEnabled` is false (the check is inside `LogAllyCall` at `StealthDebugger.cs:215`, far too late). Called up to every 3 s per Engaged creature (`ALLY_CALL_COOLDOWN`, `StealthBrain.cs:9`) — with 20 engaged mobs, ~7 of these per second.

### 4.5 `CoroutineManager` coroutines

**`UpdatePlayerVegetationCoroutine` (`:61-86`)** — runs every 5 frames (12 Hz at 60 fps). Uses `OverlapSphereNonAlloc` with a reused `Collider[10]` (good), but `:75` `colBuffer[i].name.ToLowerInvariant()` allocates **two strings per collider** (Unity `Object.name` is a native marshal) — up to 20 strings × 12 Hz = **240 string allocs/sec** in the background, forever.

**`UpdateNearbyAlliesCoroutine` (`:88-125`)**:
```csharp
:95   var allAIs = new List<Character>(AwarenessSystem.GetAllTrackedCharacters());  // FULL COPY of the whole dictionary's keys, per outer iteration
:104  Physics.OverlapSphereNonAlloc(c.transform.position, 30f, hitBuffer)          // buffer is only Collider[30] -> SILENT TRUNCATION in dense areas
:108  Character other = hitBuffer[i].GetComponent<Character>();
:111  MonsterAI otherAI = other.GetComponent<MonsterAI>();                          // SECOND GetComponent on the same object (:109 already did it)
:120  yield return null;   // ONE creature per frame
```
It iterates **every character the `AwarenessSystem` dictionary has ever seen** (not the 64 m tracked set), one per frame. With 150 stale entries the ally count for any given creature refreshes once every **2.5 seconds**, and `EvaluateGroupBehavior` (`StealthBrain.cs:205,212,220`) is reading data that stale. The `hitBuffer[30]` truncation means `NearbyAlliesCount` saturates and is wrong exactly when it matters (a raid).

### 4.6 `StealthUIController.Update` (every frame, `StealthUIController.cs:23-59`)

`:53-54` call `VisibilitySystem.GetVisibility(p)` + `NoiseSystem.GetNoise(p)` **every frame** — that is 2 more `GetEquippedItems()` List allocs, 4 more `ToLowerInvariant()`, 2 more unconditional interpolated debug strings, and 1 more shadow `Physics.Raycast`, **60 times per second**, duplicating work the Overmind already did.

`:69-83 GetHighestAlertness` walks the **entire** `AwarenessSystem` dictionary with a `Vector3.Distance` per entry, every frame. That dictionary is unbounded in practice (see §6.1) — O(all-characters-ever-tracked) per frame.

### 4.7 Dictionaries keyed by `UnityEngine.Object`

Three of them:

| Dictionary | Site | Problem |
|---|---|---|
| `Dictionary<Character, AwarenessData> Data` | `AwarenessSystem.cs:9` | Holds a **strong managed reference** to every `Character` wrapper. `EqualityComparer<Character>.Default` → `UnityEngine.Object.Equals`/`GetHashCode` (instance-ID based), so a *destroyed* Character is still a distinct, live key — `c == null` (the Unity operator override) is true but the reference is not null, which is why `StealthUIController.cs:72` and `CoroutineManager.cs:99` both have to re-check `c == null` inside their loops. Only pruned by `Character_OnDestroy_Patch`. |
| `Dictionary<Player, int> PlayerBushHitCount` | `CoroutineManager.cs:36` | **Never pruned, no removal path anywhere.** Every player-object lifetime (death→respawn creates a new `Player`) leaves a permanent entry pinning a destroyed wrapper. |
| `Dictionary<Player, int> PlayerGrassHitCount` | `CoroutineManager.cs:37` | Same. |

`AwarenessSystem.GetData(Character c)` (`:11-20`) has **no null guard** — `Data.TryGetValue(null, …)` throws `ArgumentNullException`. Every current caller happens to pre-check, but it is public API that MistsofAvalor calls by reflection (`AvalorStealthCompat.cs:253`).

---

## 5. The single biggest structural inefficiency

`VisibilitySystem.GetVisibility(p)`, `NoiseSystem.GetNoise(p)`, `HidingSystem.GetHidingFactor(p)`, `CamoSystem.GetCamoFactor(p)` are **pure functions of the player and the environment** — no creature input whatsoever. They are called once per (creature, evaluation) at `SensingEvaluator.cs:10-13`, plus once per frame by the UI.

For N creatures evaluated against P players, the correct count is **P** computations per tick. The current count is **N** (and a per-player redesign naively becomes **N×P**). Every item in §4.1 rows 3-11 — 3 `GetEquippedItems()` Lists, ~9 `ToLowerInvariant()` strings, 2 `Heightmap.FindHeightmap`, 1 `Physics.Raycast`, 55 substring scans, 4 unconditional interpolated strings — is redundant work multiplied by N.

**Hoisting these into a per-player `StealthProfile` refreshed once per tick eliminates (N−1)/N of all of it, and is a prerequisite for per-player tracks not costing N×P.**

---

## 6. Cache lifetime and cleanup — what leaks

### 6.1 `AwarenessSystem.Data` (`AwarenessSystem.cs:9`)
- **Grows:** `GetData` inserts on miss (`:13-18`) from **9 distinct call sites** — `StealthBrain.cs:60`, `StealthOvermind.cs:94`, `CoroutineManager.cs:101`, `BehaviorSystem.cs:198`, and the five patches (`BaseAI_CanSeeTarget_Patch.cs:33,54`, `BaseAI_CanHearTarget_Patch.cs:33,54`, `BaseAI_StealthBrain_Patch.cs:31,60`, `BaseAI_StealthBrain_IsAlerted_Patch.cs:20,39`, `BaseAI_SetMoveDir_Patch.cs:27`, `Character_OnDamaged_Patch.cs:29`, `Character_Damage_Patch.cs:25`, `Humanoid_Attack_Patch.cs:37`) — plus MistsofAvalor by reflection. Any `IsAlerted()` call on any owned, non-tamed, non-exempt MonsterAI creates an entry; vanilla calls `IsAlerted()` from 34 sites.
- **Shrinks:** only `Character_OnDestroy_Patch.cs:12`. `Character.OnDestroy` is `protected virtual` (decompiled `:7390`) and `Humanoid` overrides it calling `base.OnDestroy()` (`:12969-12971`), so the patch does fire for creatures — this path works.
- **Never shrinks on:** world change / logout (no `ClearAll` call site exists — `AwarenessSystem.ClearAll()` at `:57` is **dead code**), or on plugin `OnDestroy` (`ShadowsOfMidgard.cs:63-74` only unpatches Harmony; the static dictionary and the `SOM_CoroutineManager` `DontDestroyOnLoad` GameObject both survive).
- **Consequence:** across a session the dictionary retains an entry for every character destroyed while the patch was inactive, and `StealthUIController.GetHighestAlertness` + `UpdateNearbyAlliesCoroutine` both iterate it in full.

### 6.2 `StealthBrain._evalCache` (`StealthBrain.cs:47`)
- **Key is broken.** `GetStableAIKey` (`:189-199`) builds `StableAIKey((ulong)zv.GetZDO().m_uid.UserID, ai.GetInstanceID())`. Per the decompile, `ZDOID.UserID => GetUserID(UserKey)` (`:64582`) is the **spawning peer's user id** — *identical for every object spawned by the same peer*. The unique component is `ZDOID.ID` (`uint`, `:64586`), which is never read. So `NetworkUid` is a constant per-peer salt and the key degenerates to `GetInstanceID()`, which is per-client and changes on every re-instantiate. `MigrateCacheCoroutine` (`:135-158`) migrates `(0, inst)` → `(peerUid, inst)`, which is a no-op rename that buys nothing. **The correct key is the whole `ZDOID`** — it implements `Equals`/`GetHashCode` (`:64658`, `:64694`).
- **Pruned by:** `CleanupStaleCache` (`:287-297`), triggered only from *inside* `Evaluate` (`:67-71`) every 600 frames. So **if evaluation stops (player sails away, player dies, dedicated server), cleanup never runs and the dictionary is frozen at its high-water mark forever.** It retains `StealthDecision` objects (~280 B each) — no Unity object references, so no GameObject leak, but unbounded managed growth across a long session.
- `CleanupStaleCache` allocates `new List<StableAIKey>()` (`:289`) every sweep.
- **`ClearCache(MonsterAI)` (`:299`) and `ClearAllCaches()` (`:311`) are dead code — zero call sites.** In particular `Character_OnDestroy_Patch` clears `AwarenessSystem` but **not** `_evalCache`, so a dead creature's decision lingers for up to 600 frames and `TryGetCachedDecision` will happily serve it for up to `MAX_DECISION_AGE_FRAMES = 60` (`:166`, `:182`).

### 6.3 `StealthDebugger._lastLogTime` / `_lastLogState` (`StealthDebugger.cs:54-55`)
- Keyed by `ThrottleKey(instanceId, slot)` — value type, no Unity refs. **Good.**
- `Prune()` (`:104-122`) runs every 30 s, drops entries older than 60 s, allocates a `List<ThrottleKey>` per sweep. Only reachable from `ShouldLog`, which is only reached when debug is on. **Clean.**

### 6.4 `ArmorProfileSystem.Cache` (`Armor/ArmorProfileSystem.cs:10`)
`Dictionary<string, ArmorProfile>` keyed by `m_shared.m_name`. Bounded by distinct item names. Never pruned, correctly so. **Fine.**

### 6.5 `CoroutineManager.PlayerBushHitCount` / `PlayerGrassHitCount`
See §4.7. **Never pruned, no removal path.**

### 6.6 Coroutines
`StopManagedCoroutine` (`CoroutineManager.cs:53`) is **dead code — zero call sites.** `MigrateCacheCoroutine` instances started at `StealthBrain.cs:129` are never tracked or stopped, and a stuck `WaitUntil(() => ZNet.instance != null && ZRoutedRpc.instance != null)` (`:137`) or `WaitUntil(() => zv.GetZDO() != null)` (`:143`) pins the closure and the `MonsterAI` reference forever.

---

## 7. Cost model — N creatures, P players

Cadences established from the vanilla decompile:
- **AI tick: 20 Hz**, `dt = 0.05f`, all `BaseAI.Instances` (`:60994-60999`).
- **`MonsterAI.UpdateTarget` → `FindEnemy`: every 2 s** when a player is within 50 m, else 6 s (`:5850-5853`), and `FindEnemy` iterates **all** loaded characters (`:5191-5196`).
- **`CanSeeTarget`/`CanHearTarget` on the current target: every AI tick = 20 Hz** when `m_targetCreature != null` (`:5919-5921`).
- **Overmind: 1 pass/frame, hard-capped at 5 evaluations/frame** (`StealthOvermind.cs:15`, `:126`).

Let **C** = loaded `Character` count (typ. 40-150; 200+ near a base), **N** = creatures inside 64 m passing the filters (typ. 5-25; 40-80 in a raid), **T** = creatures with an active target, **F** = 60 fps.

### Per-frame today (estimates; CPU figures are modelled, allocation counts are exact)

| Cost centre | Per frame | At C=120, N=20, T=8, F=60 |
|---|---|---|
| Overmind discovery (`StealthOvermind.cs:52-85`) | C × (dist + typechecks) + N × (2 `GetComponent<ZNetView>` + 1 `GetComponent<MonsterAI>` + 1 ZDO string-hash) | ~40-70 µs/frame ⇒ **2.4-4.2 ms/s** |
| Overmind alloc | N × 32 B (`EvaluatedCharacter`) | 640 B/frame ⇒ **38 KB/s** |
| Evaluations | min(demand, 5)/frame × (2 Physics casts + 3 `GetEquippedItems` + 2 `FindHeightmap` + ~9 strings + 5 interpolated strings + 55 substring scans) | 5 × ~30 µs = **150 µs/frame ⇒ 9 ms/s**, and 5 × ~3 KB = **~900 KB/s garbage** |
| UpdateAI postfix (20 Hz, all instances) | (N + remote-owned) × 4 `GetComponent` + 2 ZDO string-hashes, ×20/s | ~20-40 µs per tick ⇒ **0.4-0.8 ms/s** |
| Sensing patches (20 Hz per targeted creature, ×2 for prefix+postfix) | T × 2 × (guard chain: 2 `GetComponent` + ZDO hash + dict lookup) × 20/s | **~0.5 ms/s** |
| `FindEnemy` (every 2 s per creature) | N/2 per second × C candidates, each hitting SoM's `CanSenseTarget` prefix **and** postfix | 10/s × 120 × 2 × ~0.5 µs = **1.2 ms/s** |
| UI (`StealthUIController.Update`, per frame) | 2 `GetEquippedItems` + 4 strings + 2 interpolated + 1 Raycast + O(dict) walk | ~15 µs/frame ⇒ **0.9 ms/s**, ~1.5 KB/frame ⇒ **90 KB/s** |
| Vegetation coroutine (12 Hz) | 1 `OverlapSphereNonAlloc` + up to 20 string allocs | **240 strings/s** |
| Ally coroutine (1 creature/frame) | 1 `OverlapSphereNonAlloc` + 2×hits `GetComponent` + a full `List<Character>` copy of the dictionary per outer lap | 1 List copy per (dict-size) frames |
| `CallNearbyAllies` | per Engaged creature ≤ every 3 s: allocating `OverlapSphere` + `List<string>` + `Object.name` per ally + `string.Join` | at 20 engaged: **~7/s × ~1 KB = 7 KB/s** |

**Totals ≈ 15-20 ms of CPU per second (1-2% of a 60 fps frame budget on average, but spiky) and ~1.1 MB/s of managed garbage** — enough for roughly one Gen0 collection per second attributable to SoM alone. **~65% of that garbage (≈700 KB/s) is interpolated debug strings that are never printed.**

### Where it blows up

1. **Discovery is O(C) per frame with no spatial partitioning** (`StealthOvermind.cs:55`). Doubling C doubles the per-frame floor even if N is unchanged. A base with tames + a raid pushes C past 250 and this becomes 100+ µs/frame of pure filtering.
2. **The 5-evals/frame ceiling silently starves the model.** At N=20 within 25 m, the tier table (`:101-102`) demands ~30 evals/s each = 600/s, but the ceiling is 300/s at 60 fps. Each creature actually gets ~10/s. At N=60 it collapses to ~3/s, and `MAX_EVAL_TIMESTEP = 0.5f` (`:18`, `:116`) starts clamping — detection integration silently loses time and mobs become sluggish exactly when the fight is biggest. **The ceiling is on evaluations, not on cost, so it does not adapt to how expensive an evaluation is.**
3. **Frame-rate coupling.** `FramesSinceLastEval` counts Overmind passes ≈ frames, so at 30 fps every creature is evaluated half as often; the `dt` clamp partly compensates the integration but not the reaction latency.
4. **Adding P players naively multiplies evaluations by P** — N×P sensing passes, N×P LOS linecasts, and (unless §5 is fixed first) N×P recomputations of four player-global functions that only have P distinct values. At N=20, P=5 that is 100 pair-evaluations per pass against a 5/frame ceiling ⇒ one full round every 20 frames = 3 Hz per pair.
5. **`CallNearbyAllies` is O(colliders in 30 m) with an allocating overload** and fires from inside `Evaluate`, so a group fight adds allocation spikes on top of the evaluation budget.
6. **The ally coroutine's `Collider[30]` (`CoroutineManager.cs:90`) truncates** exactly in the dense case, so `IsPartOfGroup`/`NearbyAlliesCount` are wrong when they matter.

---

## 8. v1.0 fragility — hard bindings that will not survive a rename

| Binding | Site | Risk |
|---|---|---|
| `zdo.m_uid.UserID` | `StealthBrain.cs:147,196,307` | Public field on `ZDO` + property on `ZDOID`. Direct field access, no soft binding. Also semantically wrong today (§6.2). |
| `AccessTools.Method(typeof(BaseAI), "SetTargetInfo", …)` | `BehaviorSystem.cs:29` | **Already soft-bound with logging** — good pattern. |
| `AccessTools.Method(typeof(BaseAI), "MoveTo", …)` | `BehaviorSystem.cs:49` | Soft-bound, with a documented degraded fallback (`:87-99`). Good. |
| `AccessTools.FieldRefAccess<BaseAI, Character>("m_targetCreature")` | `BehaviorSystem.cs:68` | Soft-bound but the failure mode is silent: `SetTarget` (`:141-170`) swallows and continues. |
| `c.m_running = true/false` | `BehaviorSystem.cs:318,320` | Direct public-field write on `Character`, no guard. |
| `c.SetMoveDir(moveDir)` | `BehaviorSystem.cs:314` | Direct call (`Character.SetMoveDir` at decompiled `:9503`). |
| `target.m_eye.position` | `AwarenessSystem.cs:49` | Direct field. Null-checked (`:49`) — OK. |
| `RenderSettings.sun`, `RenderSettings.fogDensity`, `RenderSettings.ambientIntensity` | `VisibilitySystem.cs:48,62,68`; `EnvironmentUtils.cs:9,32` | Unity-side, stable. |
| `EnvMan.instance.GetCurrentEnvironment().m_name` string-matching on `"rain"`/`"snow"`/`"mist"` | `EnvironmentUtils.cs:14,20,26`; `NoiseSystem.cs:53-54` | **Content-name string matching** — breaks silently on any biome/weather addition or rename. |
| Armor classification by `m_shared.m_name.Contains("iron"/"bronze"/…)` | `ArmorProfileSystem.cs:34-38,56-67`; `CamoSystem.cs:26-41` | Same class of fragility, and locale-sensitive (`m_name` is a localisation token, so this is matching token text). |
| `LayerMask.GetMask("Default","static_solid","Default_small","piece","terrain","viewblock","vehicle")` | `RaycastUtils.cs:21-22`; `VisibilitySystem.cs:15`; `HidingSystem.cs:15`; `CoroutineManager.cs:63` | `GetMask` returns 0 for unknown names **silently** — a renamed layer degrades to "nothing blocks sight" with no error. No validation anywhere. |
| Harmony targets: `BaseAI.CanSenseTarget(Character)`, `CanSeeTarget(Character)`, `CanHearTarget(Character)`, `IsAlerted()`, `MonsterAI.UpdateAI`, `Character.SetMoveDir`, `Character.OnDestroy`, `Character.Damage`, `Character.OnDamaged`, `Humanoid.StartAttack` | all of `Patches/` | `_harmony.PatchAll` (`ShadowsOfMidgard.cs:44`) — **a single missing method throws and the whole `try` at `:30` catches, leaving the mod half-patched with no per-patch diagnostics.** |
| Static `BaseAI.CanSenseTarget(Transform, …)` / `CanSeeTarget(Transform, …)` | decompiled `:4529`, `:4582` | **Not patched at all** — `FindClosestCreature` (`:5266`) bypasses SoM. |

---

## 9. Cross-mod surface that touches brain state

- **`StealthExemption`** (`Systems/StealthExemption.cs`) — `ZDOKey = "SoMStealthExempt"`, `IsExempt(Character)` (`:7`), `IsExempt(BaseAI)` (`:16`). Checked at 7 sites: `StealthOvermind.cs:68`, and in every patch (`BaseAI_CanSeeTarget_Patch.cs:31,52`, `BaseAI_CanHearTarget_Patch.cs:31,52`, `BaseAI_StealthBrain_Patch.cs:29,58`, `BaseAI_StealthBrain_IsAlerted_Patch.cs:18,37`, `BaseAI_SetMoveDir_Patch.cs:25`, `MonsterAI_StealthBrain_UpdateAI_Patch.cs:23`, `Humanoid_Attack_Patch.cs:34`). **Every one of those calls re-hashes the 16-char key** (decompiled `:62713`). A cached `int` hash is free and must be in v2. Also: `Character_OnDamaged_Patch` and `Character_Damage_Patch` have **no exemption check** — exempt creatures still get their `AwarenessData` mutated on damage.
- **MistsofAvalor** reflection-writes `AwarenessData.CurrentState = Alerted` and `DetectionLevel = 1f` (`c:/WubarrkCODING/MistsofAvalor/Compat/AvalorStealthCompat.cs:419-420`), resolving `AwarenessSystem.GetData` (`:253`), and holds `FieldInfo`s for `CurrentState`, `DetectionLevel`, `CanSense`, `CanSee`, `CanHear` (`:137`, `:254-255`). **Those five field names on `AwarenessData` are de-facto external API.** A per-player-track redesign that moves them off `AwarenessData` breaks MistsofAvalor silently — its `IsUsable()` probe (`:265`) will return false and it will fall back to its own guards. v2 must either keep a shim `AwarenessData` view or coordinate a versioned migration (the brief's capability constant is the right vehicle).
- The comment at `AvalorStealthCompat.cs:312` ("SoM … recomputes AwarenessData every frame - its UpdateAI postfix calls StealthBrain.Evaluate on every AI") is **stale** — the UpdateAI postfix has been apply-only since `MonsterAI_StealthBrain_UpdateAI_Patch.cs:31-36`. Avalor is re-asserting on a 0.2 s timer against a threat that no longer exists.

---

## 10. Bug list (verified, not speculative)

1. **`decision.Confidence` is always 0** — self-referential at `StealthBrain.cs:265`/`:268`; `SensingConfidence` never feeds it.
2. **`Character_Damage_Patch.cs:30` downgrades Engaged→Alerted** on every self-owned-mob hit, because `OnDamaged` runs synchronously inside `Damage` (vanilla `:71198-71201`) and therefore its postfix runs first.
3. **`StableAIKey.NetworkUid` is the spawning peer id, not an object id** (vanilla `ZDOID.UserID` at `:64582`) — the cache key is effectively just `GetInstanceID()`, and `MigrateCacheCoroutine` is a no-op that leaks coroutines when `NetworkUid == 0` (`StealthBrain.cs:127-130`).
4. **`MonsterAI_StealthBrain_UpdateAI_Patch.cs:38` force-targets `Player.m_localPlayer`** on every owned creature at 20 Hz via `ExecuteAction`→`SetTarget` (`BehaviorSystem.cs:268,274`), overwriting legitimate targets.
5. **`Humanoid_Attack_Patch.cs:74` calls `Evaluate` with a different player and `Time.deltaTime`** while the Overmind uses a multi-frame `dt` — two writers, incompatible integration.
6. **`cfg.GrassBonus` is dead** — bound and ServerSync'd (`SOMConfig.cs:105-107`) but `VisibilitySystem.cs:81` hardcodes `0.15f`.
7. **`StealthBrain.cs:53` allocates a `StealthDecision` before the `:73` cache-hit early-out** and discards it.
8. **`SensingEvaluator.cs:16` runs the LOS linecast unconditionally** even when `:37 ignoreLoS` makes it irrelevant.
9. **`CamoSystem` computes the biome twice per evaluation** (`:70` and `:83` both call `GetPlayerBiome`).
10. **`CoroutineManager.cs:109` and `:111` call `GetComponent<MonsterAI>()` twice on the same object.**
11. **`hitBuffer = new Collider[30]` (`CoroutineManager.cs:90`) truncates silently** — `NearbyAlliesCount` under-reports in exactly the dense scenario the group logic exists for.
12. **`AwarenessSystem.GetData` has no null guard** (`:13`) — public, reflection-called API that throws on `null`.
13. **`AIAuthority.cs:16` grants authority to the server when `ZNetView` is missing** — contradicted by the dedicated-server dossier; should be `false`.
14. **Dead code with zero call sites:** `StealthBrain.ClearCache`, `StealthBrain.ClearAllCaches`, `AwarenessSystem.ClearAll`, `CoroutineManager.StopManagedCoroutine`, `BehaviorSystem.ExecuteCombat`, `BehaviorSystem.GetCombatCooldown`, `BehaviorSystem.SetCombatStance` (all branches empty), `RaycastUtils.SkyVisible`, `RaycastUtils.SampleOcclusion`, `RaycastUtils.InTallGrass`, `EnvironmentUtils.GetAmbientLight`, `AwarenessSystem.GetAllTrackedCharacters` (used only by the ally coroutine's copying constructor).
15. **`Character_OnDestroy_Patch` clears `AwarenessSystem` but not `StealthBrain._evalCache`** — a dead creature's decision remains servable for 60 frames.

---

## 11. What the architects must carry forward

- The record type must become `creature → (per-player track)`, keyed by `ZDOID` on both axes, not `Dictionary<Character, AwarenessData>`. `AwarenessData`'s five reflection-visible fields (`CurrentState`, `DetectionLevel`, `CanSense`, `CanSee`, `CanHear`) are external API held by MistsofAvalor and must survive as a view.
- The four player-global sensing functions must be hoisted to a per-player profile computed **P** times per tick, before any per-player tracking is added, or the cost goes N×P.
- All four sensing patches receive a `Character target` — `BaseAI_CanSeeTarget_Patch.cs:16`, `BaseAI_CanHearTarget_Patch.cs:16`, `BaseAI_StealthBrain_Patch.cs:11` — and currently ignore it. `IsAlerted()` has no target and must be derived as `max over tracks`.
- Detection state is client-local and is destroyed by every ownership handover (~2.5 s cadence). Any fix must replicate a compact track summary through the ZDO or accept the reset.
- `ZDOID` (not `ZDOID.UserID`) is the correct stable key; it already implements `Equals`/`GetHashCode` (vanilla `:64658`, `:64694`).
- Component handles (`Character`, `MonsterAI`, `ZNetView`, `ZDOID`, cached exemption hash) must be resolved once per creature and bundled — the current code performs 4+ `GetComponent` calls and 2 string-hash ZDO lookups per creature per 20 Hz tick, before any actual work.
- The `MAX_EVALS_PER_FRAME = 5` budget must become a time/cost budget, not a count.
- ~65% of current GC pressure is unconditional interpolated debug strings at `StealthBrain.cs:270`, `VisibilitySystem.cs:40`, `NoiseSystem.cs:21`, `HidingSystem.cs:34`, `CamoSystem.cs:55`, and `BehaviorSystem.cs:209,215`. All are argument-side, so no amount of gating inside `StealthDebugger` helps — the call sites must be wrapped in the flag check or made lazy.