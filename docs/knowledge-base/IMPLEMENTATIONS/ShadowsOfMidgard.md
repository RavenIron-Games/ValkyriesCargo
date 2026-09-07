# ShadowsOfMidgard — Technical Reference Report

---

# ⚠️ 2.1.0 REWRITE — READ THIS BEFORE THE REST OF THIS FILE

**Written 2026-08-04. Everything below the "Overview" heading describes SoM 1.x, which is now
`_legacy\` in the repo and is NOT what ships.** The 1.x writeup is kept because it is an accurate
record of what was built and of several patterns catalogued from it — but a dozen of those patterns
turned out to be defective, and this section is the correction. Where the two disagree, this wins.

The stealth *model* (the four sensing formulas, the five evaluators, the alertness ladder) was
ported **formula for formula** and is unchanged. The engine underneath it is new.

## The 1.x defects worth carrying to other projects

Each of these shipped for the whole 1.x line without being noticed. Every one is a general Valheim
modding lesson, not a SoM-specific bug.

### 1. Never fake `BaseAI.IsAlerted()`
1.x patched it to return SoM's own state. `m_alerted` is read by *far* more of the game than it
looks: alert replication (`ZDOVars.s_alert`), the animator bool, `m_alertedEffects` (the aggro
roar), boss `activeBosses` counting, the give-up/leash block in `UpdateTarget`, event-creature
despawn, `UpdateConsumeItem` (taming), Sneak XP, cross-client `EnemyHud` agreement — and
**`RPC_Damage`'s backstab gate at decompile L8740**, `if (m_baseAI != null && !m_baseAI.IsAlerted()
&& hit.m_backstabBonus > 1f)`. With the flag frozen off, backstabs paid full multiplier
permanently. Patching it also neuters vanilla `Alert()` **for every other mod**.

**Do instead:** drive the real `Alert()` / `SetAlerted(bool)`, debounced. `BaseAI.Alert()` (L5330)
forwards to the owner by RPC when called on an unowned creature, so **the call site does not need
ownership** — which removes the main objection to driving it directly.

### 2. `Humanoid.OnDamaged` does not call base — so `Character.OnDamaged` patches are dead code
`Character.OnDamaged(HitData)` is `protected virtual` with an **empty body** (L9101).
`Humanoid.OnDamaged` overrides it at L13242 and does **not** call `base.OnDamaged(hit)`. The only
call site (L8870) dispatches virtually. So a Harmony patch on `Character.OnDamaged` **never fires
for any Humanoid** — greydwarves, draugr, fulings, skeletons, dvergr, trolls: most of the game.

`Character.Damage(HitData)` (L8696) is not a fix either — its whole body is
`m_nview.InvokeRPC("RPC_Damage", hit)`, so it runs on the **attacker's** machine at the moment the
packet is *sent*, and fires even for hits `RPC_Damage` then rejects (dodge, teleport, PVP off,
L8721-8728).

**Do instead:** subscribe to `Character.m_onDamaged` from a `BaseAI.Awake` postfix. That is where
vanilla itself subscribes (L4032), and it is invoked owner-side after every rejection test.

> **Check `base` chaining before patching any Valheim virtual.** `Humanoid.OnDestroy` (L12962) *does*
> chain; `Humanoid.OnDamaged` does not. There is no rule — grep the decompile every time.

### 3. Reflection into vanilla made a build error into a silent no-op
1.x built `AccessTools.FieldRefAccess<BaseAI, Character>("m_targetCreature")` in a static
constructor. The field is declared on **`MonsterAI`**, not `BaseAI` (L5730). It threw
`MissingFieldException` into a `catch (Exception) { LogError(...) }`, the ref stayed null, every
`SetTarget` silently did nothing, and **ally-calling was inert for the entire 1.x line.**

**Do instead:** compile against the publicized assembly and make direct calls, which the compiler
checks against the real declaring type. Then add a **startup capability probe** for the members you
depend on — see §"the capability model" below. Reflection does not make a binding safer; it moves
the failure from build time to never.

### 4. `ZDOID` is not a player identity
It changes on every respawn. Keying creature memory to it orphans every creature's memory of every
player, every death. Use `Player.GetPlayerID()` (a `long`, ZDO-backed, remote-readable).

### 5. `AddLockingConfigEntry` must come before any synced entry
Its absence left `IsLocked` permanently false, the admin gate dead, and **any client able to
broadcast its own config values server-wide.** Applies to every ServerSync consumer in this
workspace — worth auditing the others.

### 6. Creating an `EventSystem` in the chainloader breaks the game's mouse input
1.x guarded it with `if (FindAnyObjectByType<EventSystem>() == null)`. At BepInEx `Awake` time
Valheim has not built its UI, so that guard **always passes**, the mod's `EventSystem` +
`StandaloneInputModule` is created `DontDestroyOnLoad`, and when the game later creates its own,
Unity disables one of them — usually the game's. This is the swallowed-clicks bug.

**Do instead:** parent your UI under `Hud.instance.m_rootObject`. You inherit vanilla's `Canvas`,
`CanvasScaler`, `GraphicRaycaster` and the game's `EventSystem`, your widgets hide when the player
hides the HUD (vanilla moves that object off-screen, L39367/L39371), and they are destroyed with it.
Create nothing.

### 7. `Player.m_localPlayer` in an AI loop makes the mod inert on dedicated servers
1.x's scheduler opened with `Player p = Player.m_localPlayer; if (p == null) yield return
WaitForSeconds(1f)`. On a dedicated server that field is null forever — **the entire mod did nothing
there.** It also meant a creature chasing a remote player was tiered by its distance to *you*.

**Do instead:** "world is up" is `ZNet.instance != null && ZNetScene.instance != null`. Tier by
distance to the nearest *tracked player*.

### 8. The `while(true) { ... continue; ... yield }` coroutine hang
Three independent instances were found across the design panel; one reached production (the ally
loop). A body that can `continue` past its only `yield` hard-locks the game.

**Do instead:** for anything that runs for the session's lifetime, use a **time-budgeted cursor
driven from one `Update`** rather than a coroutine. 2.1.0 has no coroutines at all, on the reasoning
that a rule you must remember at every future edit will eventually be forgotten. If you keep a
coroutine, `yield` unconditionally at the bottom.

### 9. Max-priority prefix + min-priority postfix "sandwich" — retracted
Catalogued from 1.x as a recommended pattern; it is not. Costs: the postfix re-runs the whole guard
chain to re-assert what the prefix already wrote (~50 % of the patch layer's CPU); `int.MaxValue`
defeats sibling mods that carry an explicit `HarmonyBefore` on you; and the duplicated guard chain
**drifts** — 1.x's seven copies had become three different chains, two of them missing the cross-mod
exemption check.

**Do instead:** prefix only, `Priority.Low`, honour `__runOriginal`, and "no opinion" ⇒ `return
true`. One shared guard helper, not one per patch.

### 10. A raw `SetMoveDir` write makes a monster moonwalk — facing is `m_lookYaw`-only, and `m_running` is derived state
Shipped in 2.0.x's flee branch and found in play testing 2026-08-23: a creature ordered to retreat
glided away **backwards at speed while facing the player**, bosses included. Two independent wrong
beliefs, both invisible at compile time:

- **A monster's body only ever rotates toward `m_lookYaw`.** `Character.AlwaysRotateCamera()`
  returns `true` unconditionally (L9719; only `Player` overrides it), which forces
  `UpdateRotation` (L8451) onto the `m_lookYaw` branch **for every non-player** — the
  `Quaternion.LookRotation(m_moveDir)` alternative is dead code for them. Write `m_moveDir`
  without `SetLookDir` and the body keeps its stale facing forever while world velocity drags it
  anywhere: `forward_speed` (world velocity dotted onto body forward, L8298) goes negative and
  the animator plays the backpedal blend. There is **no backpedal speed penalty for NPCs** —
  velocity is pure world-space (L8225) — so the moonwalk is *fast*.
- **`Character.m_running` is an output, not an input.** `UpdateMotion` zeroes it every physics
  frame (L7950) and `UpdateWalking` recomputes it from `CheckRun` (L8187), which reads **`m_run`**
  — the field only `SetRun` writes. Assigning `m_running = true` from a patch is a per-frame
  no-op, so the "sprinting" flee actually jogged and nothing said why.

**Do instead:** drive movement through vanilla's own layer — `BaseAI.Flee(dt, from)` /
`MoveTo` / `MoveTowards` — which does `LookTowards`, the turn-in-place throttle
(`moveDir = transform.forward * (1 - angle/m_moveMinAngle)`, L4679-4686) and `SetRun` for you. If
you must write fields directly, you owe all three writes: `SetMoveDir`, `SetLookDir`, `SetRun`.
Related fact: for non-players `UpdateWalking` **never renormalises `m_moveDir`** (the normalise at
L8196 is `IsPlayer()`-gated), so the magnitude *is* a velocity throttle — that is the mechanism
SoM's `SetMoveDir` prefix uses to slow searches and flees, and it is also why an accidental
overlong vector overspeeds a creature.

## Vanilla facts established against the decompile (client build)

| Member | Line | Fact |
|---|---|---|
| `BaseAI.CanSeeTarget(Character)` | 4580 | instance; delegates to the 7-arg **static** at 4585 |
| `BaseAI.CanHearTarget(Character)` | 4549 | instance; delegates to the 3-arg **static** at 4554 |
| `BaseAI.CanSenseTarget(Character)` | 4522 | → 2-arg instance 4527 → 10-arg **static** 4532 |
| `BaseAI.CanSenseTarget(static)` | 4532 | calls the **STATIC** `CanHearTarget` (4538) and **STATIC** `CanSeeTarget` (4542) |
| `BaseAI.m_viewBlockMask` | 3954 | `private **static**` int, assigned in the first `BaseAI.Awake` (L4027) — reads as 0 before that |
| `BaseAI.Awake` | 4016 | `protected virtual`; `MonsterAI.Awake` (5762) **does** call `base.Awake()` |
| `BaseAI.OnDestroy` | 4059 | `private`, non-virtual, **not** overridden by MonsterAI — always fires |
| `Character.SetMoveDir` | 9501 | body is `m_moveDir = dir;` (note: there is **no** `SetMoveDir` on `BaseAI`) |
| `Humanoid.StartAttack` | 13066 | final bool is named **`secondaryAttack`**; `Character.StartAttack` (9673) names it **`charge`** |
| `MonsterAI.OnDamaged` | 5800 | vanilla already does `Wakeup(); SetAlerted(true); SetTarget(attacker);` on every hit |
| `MonsterAI.UpdateAI` | 5957 | opens `if (!base.UpdateAI(dt)) return false;` — a postfix fires on the early-out too |
| `Player.OnSpawned` | 17604 | `public void OnSpawned(bool spawnValkyrie)` |
| `Hud.m_rootObject` | 38985 | `public GameObject`; `Hud.instance` at 39295 |
| `ZNet.GetAllCharacterZDOS()` | 68491 | **instance** method, and it allocates — never call per-tick |
| `Character.GetStealthFactor()` | 10092 | `virtual`, base returns `1f`; vanilla multiplies it into **view range** (L4605-4611) |
| `Character.AlwaysRotateCamera()` | 9719 | returns `true` unconditionally; only `Player` overrides — NPC body rotation targets `m_lookYaw` **only** (see defect 10) |
| `Character.SetRun` | 9507 | body is `m_run = run;` — `m_running` (7204) is private **derived** state, zeroed at 7950 and recomputed at 8187 every physics frame |
| `BaseAI.Flee(float,Vector3)` | 4361 | `protected`; picks a path-validated flee point every `m_fleeInterval` (2 s), then `MoveTo(dt, target, 1f, IsAlerted())` |
| `BaseAI.MoveTowards` | 4675 | `LookTowards(dir)` first, then `moveDir = transform.forward * (1 - angle/m_moveMinAngle)`, then `SetRun` — bypassing it deletes all three |
| `Character.m_boss` | 6893 | **nothing** in BaseAI/MonsterAI flee logic tests it; vanilla bosses never flee only because their prefabs ship `m_fleeIfLowHealth = 0` etc. Force-flee a boss and it complies |

**Vanilla `CanSeeTarget` (static, L4596-4625), which any perception override must agree with:**
`alerted` gates the **FOV cone and nothing else**; the view-block `Physics.Raycast` is
**unconditional**; the `ParticleMist.IsMistBlocked` gate runs after it, conditioned only on
`m_mistVision`. The aim point is `target.IsCrouching() ? target.GetCenterPoint() :
target.m_eye.position`, cast from the observer's eye, and the mist check uses the *same* two points.

Two consequences that are easy to get wrong:
- **Never skip the LOS cast because a creature is alerted.** That latches it into seeing through
  walls forever. Alerted may use a longer *recheck period* — a bounded rate limit — never a skip.
- **A prefix that writes `__result = true` and skips the original deletes both the raycast and the
  mist gate.** So a positive answer is only safe if you evaluated every term vanilla evaluates. A
  *negative* answer is always safe. That asymmetry is the whole design of a perception override.

## Patterns worth stealing from 2.1.0

**Atomic patch groups.** Because `CanSenseTarget` reaches the *static* helpers, patching two of the
three sense methods leaves target *acquisition* on one perception model and target *retention* on
another. Install all three or none, and roll the group back if any member fails.

**The capability model (`Bind/VanillaBind.cs`).** A startup table of every vanilla member the mod
touches: name, impact (`Fatal` / `FeatureOff` / `TermDegraded` / `Advisory`), and a prose
consequence. *Gates* — not individual rows — decide whether the patch layer installs, because that is
the only place a fallback ladder ("either roster rung will do") can be expressed. Calls stay direct;
the probe only decides install-or-not. A future game update then produces **one log line at startup
and a clean fall-back to vanilla**, instead of a `MissingMethodException` from inside `FindEnemy` at
20 Hz per creature. Includes four deliberate *canaries* — members the mod never calls, whose
disappearance means an assumption the design rests on has changed.

**"The failure floor is vanilla."** Every default, fallback and missing binding must produce *less*
perception than vanilla, never more. An unreadable stealth factor defaults to `0f` (vanilla's ZDO
default) and never `1f`. A missing mist gate turns SoM's positives into "no opinion" rather than
into "clear".

**Don't fall through to vanilla on budget exhaustion.** Counter-intuitive and measured: vanilla's
instance sense methods run at **20 Hz per target-holding creature** (`UpdateTarget` calls them every
tick, *not* at the 2 s/6 s `FindEnemy` cadence) and end in an unconditional raycast. "Let vanilla
handle it" therefore costs *more* than the capped work you just declined, and perceives more too.
Keep your last measurement and decay it toward false. Falling through is correct on a **capability**
failure only.

**One `byte[]` ZDO digest, not N scalar fields.** `ZDO.Serialize` resends the whole field set on any
revision bump, so a chatty replication makes a *stationary* creature — which otherwise has no
revision source at all — generate full-ZDO resends vanilla would never make. Gate on "the value
changed by ≥ Δ", not on "any bit changed".

**Design-doc trail.** Six surveys, three competing architectures, two adversarial reviews and the
synthesised plan are in `libs-Tools\IMPLEMENTATIONS\SoM-v2-Surveys\`. `PLAN-v2-implementation.md`
holds the ten cross-cutting rules; `REVIEW-verified-facts.md` and `API-CORRECTIONS.md` are ground
truth and win over any design document.

---

## Overview

**Shadows of Midgard (SoM)** is a BepInEx/Harmony mod for Valheim (GUID `wubarrk.shadowsofmidgard`) that replaces vanilla monster perception and target-acquisition with a custom stealth-detection simulation. It computes a per-player "how visible/audible am I right now" score from lighting, movement, crouching, armor, weather, and vegetation, feeds that into a per-AI "StealthBrain" state machine (Unaware → Suspicious → Alerted → Engaged) that drives search/pursue/flee/attack behavior and ally-calling, and overrides Valheim's `BaseAI` sensing/alert/attack methods via Harmony so vanilla AI acts on SoM's decisions instead of its own. A screen-space HUD (a draggable "eye" gem and a noise-level bar) shows the player their current visibility/noise and the highest alertness state of any nearby tracked creature. A lightweight "armor profile" classifier infers stealth-relevant properties (weight, material, camo affinity) from any equipped item's name so the system works with vanilla and modded armor alike without needing per-item registration.

---

### System / Folder Map (source only)

- `ShadowsOfMidgard.cs` — plugin entry point / bootstrap
- `Config/` — `SOMConfig.cs` (BepInEx config binding + ServerSync wiring), `StealthConfigModel.cs` (POCO snapshot)
- `Systems/` — core detection/AI systems (`StealthBrain.cs`, `AwarenessSystem.cs`, `StealthOvermind.cs`, `VisibilitySystem.cs`, `NoiseSystem.cs`, `HidingSystem.cs`, `CamoSystem.cs`, `BehaviorSystem.cs`, `CoroutineManager.cs`, `AIAuthority.cs`, `StealthExemption.cs`, `StealthDebugger.cs`, `UnifiedStealthTypes.cs`)
- `Systems/StealthBrain/` — the 5 decision-phase evaluators (`SensingEvaluator`, `StateEvaluator`, `FleeEvaluator`, `MovementEvaluator`, `CombatEvaluator`)
- `Systems/SteathUI/` (folder name typo in repo) — HUD (`StealthUIRoot`, `StealthUIController`, `StealthGem`, `NoiseMeter`, `SpriteLoader`)
- `Patches/` — 10 Harmony patch classes hijacking `BaseAI`/`MonsterAI`/`Character`/`Humanoid`
- `Armor/` — `ArmorProfile.cs`, `ArmorProfileSystem.cs`, `ArmorUtils.cs` — heuristic item classifier
- `Utils/` — `EnvironmentUtils.cs`, `RaycastUtils.cs`
- `Assets/eye.png` — embedded HUD sprite
- `libs/ServerSync.cs` — vendored third-party config-sync helper
- `AVALOR_COMPAT_HANDOFF.md`, `AVALOR_COMPAT_PLAN_REVIEW.md` — planning/handoff docs for a cross-mod compatibility feature (`StealthExemption`) between this mod and a sibling mod ("Mists of Avalor")

---

## Detailed Systems

### 1. StealthBrain Decision Engine (core AI evaluator pipeline)

- **Purpose:** Central per-AI "brain" that turns raw sensing signals into a complete behavioral decision (alertness state, movement directive, combat directive, flee directive, group-call directive) once per evaluation tick for a given `MonsterAI`.
- **Key files:** `Systems/StealthBrain.cs` (orchestrator), `Systems/StealthBrain/SensingEvaluator.cs`, `Systems/StealthBrain/StateEvaluator.cs`, `Systems/StealthBrain/FleeEvaluator.cs`, `Systems/StealthBrain/MovementEvaluator.cs`, `Systems/StealthBrain/CombatEvaluator.cs`, `Systems/UnifiedStealthTypes.cs`, `Systems/AwarenessSystem.cs`.
- **Architecture:**
  - `AwarenessData` (class) is the **persistent per-Character state**: `DetectionLevel` (0-1 float), `CurrentState` (`VanillaAlertness` enum: Unaware/Suspicious/Alerted/Engaged), `CurrentAction` (`AIBehaviorAction` enum), `LastKnownPosition`, `TimeSinceSeen`, health percents, flee bookkeeping, ally-count/group bookkeeping, and eval-scheduling fields. Stored in `AwarenessSystem`'s `Dictionary<Character, AwarenessData>` — created lazily via `AwarenessSystem.GetData(c)`.
  - `StealthDecision` (class, deliberately not struct) is the **transient per-evaluation output**: sensing flags, state, movement/combat/flee/group directives, confidence, and a `StealthDebugData` struct for diagnostics.
  - `StealthBrain.Evaluate(MonsterAI ai, Player p, float dt)` runs a fixed **10-phase pipeline** every time it's called:
    1. Null/dead/sleeping guard, fetch `AwarenessData`.
    2. Frame-level cache check (`_evalCache: Dictionary<StableAIKey, CachedEvaluation>`) — if this exact AI was already evaluated this exact `Time.frameCount`, return the cached `StealthDecision`.
    3. **Phase 1 – Sensing** (`SensingEvaluator.EvaluateSensing`): computes `CanSee`/`CanHear`/`CanSense`, updates `DetectionLevel`.
    4. **Phase 2 – State Transitions** (`StateEvaluator.EvaluateStateTransitions`): moves `CurrentState` through the 4-tier alertness ladder with hysteresis.
    5. **Phase 3 – Health snapshot**.
    6. **Phase 4 – Fleeing** (`FleeEvaluator.EvaluateFleeing`): health-based flee/strategic-retreat decision.
    7. **Phase 5 – Nearby allies**: read from `AwarenessData.NearbyAlliesCount`, filled asynchronously by `CoroutineManager`'s `UpdateNearbyAlliesCoroutine`.
    8. **Phase 6 – Movement** (`MovementEvaluator.EvaluateMovement`): produces a `MovementDirective` only for states where SoM should be steering; leaves `Direction = Vector3.zero` (a documented "no opinion, vanilla owns this" sentinel) when Alerted-with-sense or Engaged.
    9. **Phase 7 – Combat** (`CombatEvaluator.EvaluateCombat`): produces a `CombatDirective` keyed on `CurrentState`.
    10. **Phase 8 – Group behavior**: decides whether to call allies, gated by state + a per-AI cooldown timer.
    11. **Phase 9 – Primary action** (`StealthBrain.DetermineAction`): a priority-ordered if/else chain mapping (Flee flag, GiveUp flag, CurrentState, CanSense/ShouldSearch) → single `AIBehaviorAction`.
    12. **Phase 10 – Finalize**: syncs sensing flags back onto `AwarenessData`, stores the result into `_evalCache` keyed by `StableAIKey`, and kicks off a one-shot coroutine that re-keys the cache entry once the object's ZDO becomes available.
  - **Stable AI identity**: `StableAIKey` is a `(ulong NetworkUid, int InstanceId)` struct used as the cache dictionary key instead of raw `GetInstanceID()`, since Unity instance IDs aren't stable across respawns.
  - **Stale-decision protection for read-only callers**: `StealthBrain.TryGetCachedDecision(ai, out decision)` lets other code (attack-gate patch, `UpdateAI` postfix) read the last computed decision **without** triggering a fresh full evaluation — critical for call sites that run every frame for every creature. Returns `false` if the cached decision is older than `MAX_DECISION_AGE_FRAMES` (60 frames).
- **How to implement (step-by-step):**
  1. Define enums for alertness tiers, behavior actions, movement strategies, target priority, flee reason.
  2. Define a persistent per-creature data class holding detection level, alertness state, timers, last-known-position, and a transient decision output class. Use a **class** (not struct) for the decision object if any downstream code needs to mutate fields after receiving it by reference.
  3. Build a `Dictionary<Character, AwarenessData>` store with lazy-create `GetData()`, plus a `Clear(Character)` hook wired to the creature's `OnDestroy`.
  4. Split evaluation into small, single-responsibility static evaluator classes each taking `(ai, player, character, data, config, decision, dt)` and mutating in place.
  5. Implement detection level as a **simple integrator**: `DetectionLevel = Clamp01(DetectionLevel + gain*dt)` where `gain` is positive when seen/heard and negative when neither — produces natural rise/fall behavior with no separate timer logic.
  6. Implement the alertness state machine as threshold comparisons **with hysteresis**: escalate freely as detection crosses thresholds, but only de-escalate when detection drops *well below* the entry threshold.
  7. Cache the per-frame decision keyed by a network-stable identity, expose both a "force full evaluate" API and a "read last cached decision" API, and make performance-critical callers use only the cached-read API.
  8. Wire a periodic cache-cleanup pass to avoid an ever-growing dictionary.
- **Reusable pattern/snippet:**
```csharp
// Detection integrator + hysteresis state machine
float gain = decision.CanSee ? cfg.AlertGain
           : decision.CanHear ? cfg.SuspicionGain
           : -cfg.DetectionDecay;
data.DetectionLevel = Mathf.Clamp01(data.DetectionLevel + gain * dt);

if (data.DetectionLevel >= cfg.EngagedThreshold) data.CurrentState = VanillaAlertness.Engaged;
else if (data.DetectionLevel >= cfg.AlertedThreshold && data.CurrentState < VanillaAlertness.Alerted)
    data.CurrentState = VanillaAlertness.Alerted;
// de-escalation uses lower thresholds than escalation (hysteresis band)
```

---

### 2. Sensing Sub-Systems (Visibility / Noise / Hiding / Camo)

- **Purpose:** Compute four independent 0–1 scores describing how detectable the *player* currently is. These feed `SensingEvaluator` and are also read directly by the HUD.
- **Key files:** `Systems/VisibilitySystem.cs`, `Systems/NoiseSystem.cs`, `Systems/HidingSystem.cs`, `Systems/CamoSystem.cs`.
- **Architecture / formulas:**
  - **VisibilitySystem.GetVisibility(Player)**: `vis = Clamp01(light - shadow - grass - weather + movement + armor)`. `light` = a night floor plus a day-scaled bonus from `RenderSettings.sun.intensity`. `shadow` = a bonus if a raycast toward `-sun.transform.forward` hits terrain (standing in shadow). `grass` = up to a cap from background vegetation-sample hit counts. `movement` = normalized velocity magnitude. `armor` = `ArmorUtils.GetTotalStealthPenalty`. `weather` = rain/snow/mist reductions from `RenderSettings.fogDensity`/environment name.
  - **NoiseSystem.GetNoise(Player)**: `noise = Clamp01(movement + armor - crouch - weather)`.
  - **HidingSystem.GetHidingFactor(Player)**: `hiding = Clamp01(bush + grass + crouch)` — applied *multiplicatively* against visibility in `SensingEvaluator` (`adjustedVis = vis * (1-hiding) * (1-camo)`), i.e. hiding/camo are cover multipliers on raw visibility, not additive.
  - **CamoSystem.GetCamoFactor(Player)**: `camo = Clamp01(biome + armor)`. `biome` from a hardcoded "camo-friendly" biome set via `Heightmap.FindHeightmap`. `armor` from equipped items tagged `HasCamoTag` matched by name substring against a biome map.
  - All four call `StealthDebugger.LogPlayerSensing(...)` with a computed detail string, throttled/deduplicated centrally.
- **How to implement:**
  1. Each sensing axis is an independent static function `float Get<X>(Player p)` returning 0–1, built from small named sub-terms — makes the aggregate formula read as a simple sum/product of named values.
  2. Normalize velocity-based terms against the game's known max speed constant.
  3. Use a lightweight environment tag lookup (substring match on the current environment's name) rather than an enum, since Valheim's weather system doesn't expose a clean "is it raining" enum.
  4. For "am I in a shadow" checks, raycast from an elevated position toward `-sun.transform.forward` on the terrain/piece layer mask; exclude the vegetation layer (handled separately) to avoid double-counting.
  5. For biome-conditional bonuses, use `Heightmap.FindHeightmap(pos)?.GetBiome(pos)` and a `HashSet<Heightmap.Biome>` allow-list per bonus tier.
  6. Apply hiding/camo as **multiplicative discounts** on the raw visibility score rather than folding them into the same additive sum — keeps "how visible am I in principle" separate from "how much is my cover reducing that."

---

### 3. Vegetation & Nearby-Ally Background Sampling (CoroutineManager)

- **Purpose:** Provide cheap, spread-over-time environmental sampling (bush/grass overlap, nearby-ally counts) that hot per-frame sensing/decision code just reads from a dictionary — a general "amortize expensive physics queries across frames" pattern.
- **Key files:** `Systems/CoroutineManager.cs`.
- **Architecture:**
  - `MonoBehaviour` singleton (`Instance` getter lazily creates a `DontDestroyOnLoad` GameObject). `Bootstrap()` static method exists purely to force `_ = Instance;` at plugin `Awake()` time, because otherwise the singleton would only be created the first time some deeply-nested static call happened to touch it — too late, since `Start()` (which launches background coroutines) needs to run before evaluation logic expects data to be populated.
  - `Start()` launches: `UpdatePlayerVegetationCoroutine`, `UpdateNearbyAlliesCoroutine`, and `StealthOvermind.EvaluateLoop()` (the main AI scheduler — also driven from here).
  - `UpdatePlayerVegetationCoroutine`: every 5 frames, does one `Physics.OverlapSphereNonAlloc(playerPos, 1f, colBuffer, layerMask)` and counts colliders whose name contains "bush"/"grass", writing counts into static dictionaries read synchronously by `VisibilitySystem`/`HidingSystem`.
  - `UpdateNearbyAlliesCoroutine`: iterates every tracked `Character` (one per `yield return null`, spreading cost across frames), runs `Physics.OverlapSphereNonAlloc(pos, 30f, hitBuffer)`, counts other live non-sleeping `MonsterAI`-bearing characters in range, writes results onto that creature's `AwarenessData`.
- **How to implement:**
  1. Create a `MonoBehaviour` singleton with a static lazy `Instance` property, plus a `Bootstrap()` no-op-return static method that plugin `Awake()` calls explicitly — don't rely on implicit lazy creation for anything whose `Start()` other systems depend on.
  2. Use `Physics.OverlapSphereNonAlloc` with a reusable pre-sized buffer array inside coroutines that run indefinitely, to avoid GC pressure.
  3. Spread expensive per-entity work across frames with `yield return null` inside the loop body (one entity per frame).
  4. Store sampled results in plain static dictionaries keyed by the sampled object, read synchronously by hot-path systems.

---

### 4. StealthOvermind (distance-tiered evaluation scheduler)

- **Purpose:** Decide, every frame, *which* nearby creatures actually get a full `StealthBrain.Evaluate` this frame, so CPU cost scales with "creatures near the player" rather than "all loaded creatures," and closer creatures get evaluated more often than distant ones.
- **Key files:** `Systems/StealthOvermind.cs`.
- **Architecture:**
  - Static `EvaluateLoop()` coroutine (started once from `CoroutineManager.Start()`) runs forever. Each pass:
    1. Bails if there's no local player or the player is dead.
    2. Rebuilds a candidate list from **vanilla's own registry** (`Character.GetAllCharacters()`), not from `AwarenessSystem`. Filters out: null/dead, players, tamed creatures, anything beyond `MAX_TRACKING_RANGE` (64m — deliberately wider than the outermost 50m tier so a creature walking into range is already "warm"), exempt creatures, anything without a non-sleeping `MonsterAI`, and anything failing `AIAuthority.IsAuthoritative`.
    3. For each candidate, increments `FramesSinceLastEval` and decides whether to evaluate based on **three distance tiers**: ≤25m → every 2 frames ("Priority"), ≤50m → every 6 frames ("Active"), else → every 20 frames ("Background").
    4. When evaluating, computes a **real elapsed-time dt** since that AI's last evaluation, clamped between `Time.deltaTime` and `MAX_EVAL_TIMESTEP` (0.5s) — keeps the detection integrator's gain/decay frame-rate- and tier-interval-independent, while the clamp prevents a creature that was out of range for minutes from resolving that whole gap in one giant detection jump.
    5. Calls `StealthBrain.Evaluate(monster, player, dt)` directly.
    6. Caps evaluations at `MAX_EVALS_PER_FRAME` (5); once hit, yields a frame and resets the counter, so a sudden cluster of nearby creatures doesn't spike a single frame.
- **How to implement:**
  1. Never gate your AI system's *acquisition* of new targets on a data store that only your own patches populate — seed candidate discovery from the game's own authoritative registry.
  2. Bucket candidates into 2–3 distance tiers with geometrically increasing "frames between evaluations" — the single biggest lever for scaling to many creatures.
  3. Track "frames since last full evaluation" per entity and compute a **wall-clock dt** for that entity's own evaluation, clamped to a sane maximum.
  4. Impose a hard per-frame cap on the number of full evaluations, yielding mid-loop when hit.
  5. Exclude irrelevant populations early before the expensive distance/tier math.

---

### 5. Harmony Patch Suite (vanilla AI sensing/behavior override)

- **Purpose:** Make vanilla `BaseAI`/`MonsterAI`/`Humanoid`/`Character` methods defer to SoM's computed state instead of their own built-in sensing/alert/attack logic, while staying multiplayer-safe and coexisting with other mods.
- **Key files:** all of `Patches/*.cs`.
- **Architecture / the recurring "hard override" pattern**: For each sensing method (`CanSeeTarget`, `CanHearTarget`, `CanSenseTarget`, `IsAlerted`), the same shape is used:
  - A **Prefix at `[HarmonyPriority(int.MaxValue)]`** runs first among *all* patches from *any* mod. Does authority/guard checks and, if applicable, sets `__result` and **returns `false`**, skipping vanilla's own method body *and* any other mod's prefixes.
  - A **Postfix at `[HarmonyPriority(int.MinValue)]`** runs last among all patches from any mod, and unconditionally re-writes `__result` to SoM's value again — a deliberate defensive measure ("re-assert our state to stop other mods from changing it") since other patch orderings could still let another mod's postfix run between SoM's prefix and the natural end of the call.
  - **Common guard sequence**: `AIAuthority.IsAuthoritative(instance)` → `target.IsPlayer()` → `instance is MonsterAI` → not tamed → `!StealthExemption.IsExempt(aiChar)` → `AwarenessSystem.GetData(aiChar) != null`. Any guard failure falls through to vanilla.
  - `BaseAI_StealthBrain_Patch` (`CanSenseTarget`) has one extra branch: if `data.CurrentState >= Alerted`, force-returns `true` regardless of the live `CanSense` flag — lets an Alerted/Engaged creature keep "sensing" the player through brief line-of-sight breaks.
  - `BaseAI_SetMoveDir_Patch` (patches `Character.SetMoveDir`, priority `int.MinValue` prefix, applies to **all** characters) — when the moving character is a non-player, non-tamed, authoritative, non-exempt AI currently in `Search`/`Investigate`, scales the incoming direction vector's magnitude by `cfg.SearchSpeedFactor` (default 0.5). Because this is a **Prefix that mutates a ref parameter and then returns `true`**, this is the correct shape for a "modify vanilla's input, then let vanilla actually run" patch — contrast with the sensing patches, which return `false` to fully replace vanilla.
  - `MonsterAI_StealthBrain_UpdateAI_Patch` (Postfix, priority `int.MinValue`) is the **apply-only** patch: after vanilla's `UpdateAI` body runs, fetches the Overmind's *cached* decision and calls `BehaviorSystem.ExecuteAction(...)`. Does **not** require an existing vanilla target, since target acquisition itself depends on `CanSenseTarget` reporting true first.
  - `Humanoid_Attack_Patch` (Prefix) is the **combat gate**: returns `false` (blocks the attack) unless `CurrentState >= Alerted`, not fleeing, and `Combat.ShouldAttack` is true.
  - `Character_Damage_Patch` / `Character_OnDamaged_Patch`: force-bump `DetectionLevel` and `CurrentState` on the AI *taking* damage, guaranteeing a creature fights back even if it never sensed the hit coming. These two patches are deliberately **not** gated by `StealthExemption` — intentionally universal reactions.
  - `Character_OnDestroy_Patch` (Prefix): calls `AwarenessSystem.Clear(character)` — the cleanup hook.
- **How to implement:**
  1. For any vanilla boolean-returning "sense/query" method you want to fully replace, patch with a `[HarmonyPriority(int.MaxValue)]` Prefix that sets `__result` and returns `false` when guard conditions hold, **plus** a `[HarmonyPriority(int.MinValue)]` Postfix that unconditionally re-asserts the same `__result`.
  2. For methods where you want to *modify vanilla's input* rather than replace its output, use a Prefix that mutates a `ref`/passed parameter and still `return true`.
  3. For "apply a computed decision to vanilla state," use a **Postfix** on the AI's main tick method rather than a Prefix, so your directive is the last word for that frame and vanilla's own logic has already run first.
  4. Gate *every* AI-facing patch behind an ownership/authority check first.
  5. Add an early `target.IsPlayer()` check to any patch that should only affect player-vs-monster interactions.
  6. Build a small set of universal guard predicates and apply them identically, in the same order, across every patch.
  7. When gating a downstream consequence on a decision computed elsewhere, prefer reading a *cached* decision and only compute fresh if no cache exists.
- **Reusable pattern/snippet:**
```csharp
[HarmonyPatch(typeof(BaseAI), "CanSeeTarget", new Type[] { typeof(Character) })]
public static class BaseAI_CanSeeTarget_Patch
{
    [HarmonyPriority(int.MaxValue)]
    public static bool Prefix(BaseAI __instance, Character target, ref bool __result)
    {
        if (!AIAuthority.IsAuthoritative(__instance)) return true;
        if (target == null || !target.IsPlayer()) return true;
        if (!(__instance is MonsterAI)) return true;
        Character aiChar = __instance.GetComponent<Character>();
        if (aiChar == null || aiChar.IsTamed()) return true;
        if (StealthExemption.IsExempt(aiChar)) return true;

        AwarenessData data = AwarenessSystem.GetData(aiChar);
        if (data == null) return true;

        __result = data.CanSee;
        return false; // fully replace vanilla + lower-priority patches
    }

    [HarmonyPriority(int.MinValue)]
    public static void Postfix(BaseAI __instance, Character target, ref bool __result)
    {
        // identical guards, then unconditionally re-assert __result = data.CanSee;
    }
}
```

---

### 6. AIAuthority (multiplayer ownership gate)

- **Purpose:** A single, centralized predicate answering "should this peer's copy of the mod actually be driving this creature's AI logic," so every patch and system checks the same rule instead of duplicating ad-hoc `ZNetView`/`ZNet.instance` checks.
- **Key files:** `Systems/AIAuthority.cs`.
- **Architecture:** `IsAuthoritative(BaseAI ai)`: null → false. If the creature has no `ZNetView`, falls back to `ZNet.instance.IsServer()`. Otherwise returns `nview.IsOwner()`.
- **How to implement:** Add one static utility method, `IsAuthoritative(Component withZNetView)`, that checks `GetComponent<ZNetView>()?.IsOwner()` with a `ZNet.instance.IsServer()` fallback for objects without a network view, and call it as the very first guard in every AI-affecting patch/system.

---

### 7. StealthExemption (cross-mod opt-out hook / interop contract)

- **Purpose:** Let an *entirely separate mod* mark specific creatures as excluded from SoM's stealth brain, falling back to pure vanilla AI. A clean, minimal pattern for exposing an opt-out surface to sibling mods without coupling.
- **Key files:** `Systems/StealthExemption.cs`, plus every guarded patch calling into it.
- **Architecture:**
  - The entire contract is a single **ZDO integer key**: `public const string ZDOKey = "SoMStealthExempt";`. Any mod that owns/spawns a creature can set `zdo.Set("SoMStealthExempt", 1)` — no reference to SoM's assembly required.
  - `StealthExemption.IsExempt(Character c)`: resolves the `ZNetView`, checks `IsValid()`, reads `zdo.GetInt(ZDOKey, 0) == 1`. No caching — a flag flip is visible on the very next check.
  - The **type name and its `public` visibility are themselves part of the API contract**: the consuming mod reflection-probes for a public type named exactly `ShadowsOfMidgard.StealthExemption` to detect SoM's presence before even calling into the ZDO-flag mechanism.
  - Every stealth-sensing/behavior patch inserts an exemption check *after* the authority/null/dead checks but *before* the first `AwarenessSystem.GetData(...)` call. `StealthOvermind.EvaluateLoop()` also filters exempt creatures out of its candidate list entirely. Three patches (`Character_Damage_Patch`, `Character_OnDamaged_Patch`, `Character_OnDestroy_Patch`) are deliberately left **unguarded**.
- **How to implement (for any future mod wanting the same interop pattern):**
  1. Pick a namespaced string constant as your ZDO key, expose it as a `public const string` on a `public static class` with a stable, documented name — treat the type name itself as API surface.
  2. Implement `IsExempt(Character/BaseAI)` as a simple `ZNetView.GetZDO().GetInt(key, 0) == 1` read.
  3. Insert the exemption check as an early-return guard in every patch that overrides per-creature sensing/behavior, positioned right before you'd otherwise create/read your mod's per-creature data.
  4. Also filter exempt creatures out of any background scheduler/discovery loop, not just the reactive patches.
  5. Deliberately leave universal reactive hooks (damage taken, destroy/cleanup) unguarded unless there's a specific reason a consuming mod would want those suppressed too.
  6. Consuming-mod side: reflection-probe for the type by full name at startup, and only rely on the flag mechanism if found; otherwise fall back to a self-contained compatibility shim.

---

### 8. BehaviorSystem (decision → vanilla-AI bridge)

- **Purpose:** Translate a computed `StealthDecision`/`AIBehaviorAction` into actual vanilla `BaseAI`/`MonsterAI` state changes (target, movement, attack posture), using reflection to reach protected/private vanilla members, while being careful never to fight vanilla's own pathfinder.
- **Key files:** `Systems/BehaviorSystem.cs`.
- **Architecture:**
  - **Cached reflection delegates**, built once in a static constructor: `SetTargetInfoDelegate` (bound to `BaseAI.SetTargetInfo(ZDOID)` via `AccessTools.Method` + `Delegate.CreateDelegate`), `MoveToDelegate` (bound to `BaseAI.MoveTo(...)` — the real pathfinder), `_targetCreatureRef` (a `FieldRefAccess<BaseAI,Character>` for direct field read/write). Each binding wrapped in its own try/catch.
  - `MoveToPathed(ai, c, point, stopDistance, dt)`: calls the reflected `MoveTo` with `run:true` so searching creatures route around obstacles instead of beelining through walls. Falls back to a straight-line `SetMoveDir` call if reflection failed.
  - `SetTarget(BaseAI, Character target)`: writes `m_targetCreature` directly via the field-ref delegate, then also calls `SetAITarget` with the target's ZDOID — keeping both the in-memory field and the network-replicated target info in sync.
  - `CallNearbyAllies(caller, position, radius, target)`: for each other live authoritative `MonsterAI` within range, `SetTarget(it, target)` **and** boosts that ally's own `AwarenessData.DetectionLevel`/state directly — group awareness is implemented as directly writing into the ally's own StealthBrain state rather than as a separate signaling/message system.
  - `ExecuteAction(ai, action, target, decision)`: a switch over `AIBehaviorAction`. For Pursue/Attack it deliberately does **not** drive movement directly — comment explains this runs from an `UpdateAI` postfix, so calling `SetMoveDir` would stomp the path vanilla's pathfinder just computed this same frame.
  - `ApplyMovement(ai, c, MovementDirective)`: `Vector3.zero` direction is a documented sentinel "SoM has no opinion, don't touch vanilla movement" and does nothing.
- **How to implement:**
  1. Cache `AccessTools.Method`/`FieldRefAccess` reflection bindings once in a static constructor with individual try/catch blocks, converting method reflection into typed delegates via `Delegate.CreateDelegate` for near-native call performance.
  2. Prefer the game's own pathfinding entry point over a manual straight-line `SetMoveDir` whenever you need creatures to navigate around obstacles; keep a straight-line fallback for when reflection fails to resolve the method.
  3. Use a `Vector3.zero` direction as an explicit "no directive, leave vanilla alone" sentinel.
  4. Never call direct movement APIs from a code path that runs *before* vanilla's own per-frame AI tick for target-following states.
  5. Implement "ally alerting" by directly writing into the ally's own per-entity awareness/detection state rather than building a separate messaging bus.
  6. Guard every mutation with the same authority check used elsewhere.

---

### 9. Stealth HUD (Systems/SteathUI)

- **Purpose:** Screen-space UI showing the local player their current visibility (an eye icon that scales/colors) and noise level (a fill bar), plus the highest alertness state of any nearby tracked creature. Both elements are user-draggable and persist their position to config.
- **Key files:** `Systems/SteathUI/StealthUIRoot.cs`, `StealthUIController.cs`, `StealthGem.cs`, `NoiseMeter.cs`, `SpriteLoader.cs`.
- **Architecture:**
  - `StealthUIRoot` (`MonoBehaviour` singleton, `DontDestroyOnLoad`): builds a `Canvas` (`ScreenSpaceOverlay`, `sortingOrder=9999`), a `CanvasScaler`, and a `GraphicRaycaster` (required for drag input). Also ensures a Unity `EventSystem` + `StandaloneInputModule` exist in the scene if none is found — necessary because without it no `IDragHandler` callbacks fire.
  - `StealthUIController` (own `Update()` loop): each frame, if disabled, hides both elements and returns. Otherwise computes `VisibilitySystem.GetVisibility(player)`, `NoiseSystem.GetNoise(player)`, and `GetHighestAlertness(player)` (scans **all** entries in `AwarenessSystem.GetAllData()` directly rather than calling `GetData()` per creature, since `GetData()` inserts-on-miss and would mutate the dictionary mid-enumeration), pushes into the gem/meter.
  - `StealthGem` (`MonoBehaviour` + `IBeginDragHandler,IDragHandler,IEndDragHandler`): loads its sprite via `SpriteLoader.LoadEmbeddedPNG("eye.png")` falling back to a disk path (dev iteration). `UpdateGem`: smooths visibility via `Mathf.Lerp`, maps to a uniform scale 0.5–1.4 (except while Engaged, driven by a pulse instead); color-codes by state (blue/yellow/red). Drag handlers reposition live and, on drag end, persist to config and explicitly `.ConfigFile.Save()` immediately.
  - `NoiseMeter`: same drag-handler pattern; a dark background `Image` plus a child fill `Image` anchored to grow left-to-right, colored via a three-stop gradient.
  - `SpriteLoader`: prefers a **disk file** (fast dev iteration) if present, else falls back to an **embedded manifest resource**, and when it does fall back, writes the bytes back out to disk for future dev convenience. PNG bytes decoded via **reflection into `UnityEngine.ImageConversion.LoadImage`** (tries multiple overloads, resolving the type from multiple possible assemblies) — resilient to the module split across Unity engine DLL versions.
- **How to implement:**
  1. Build one `MonoBehaviour` "UI root" singleton that owns a `ScreenSpaceOverlay` `Canvas` + `CanvasScaler` + `GraphicRaycaster`, and defensively creates an `EventSystem`/`StandaloneInputModule` if none exists in-scene.
  2. Build a second `MonoBehaviour` "controller" singleton with its own `Update()` that reads live game-state values every frame and pushes them into UI element instances.
  3. Implement each on-screen indicator as its own small `MonoBehaviour` implementing `IBeginDragHandler/IDragHandler/IEndDragHandler` directly on the `Image`'s GameObject; on drag, update `RectTransform.anchoredPosition` by `eventData.delta / canvas.scaleFactor`; on drag end, persist and `ConfigFile.Save()` explicitly.
  4. Smooth any rapidly-changing numeric value with `Mathf.Lerp(current, target, Time.deltaTime * speedConstant)` before mapping it to a visual property.
  5. For an embedded image asset: mark it `<EmbeddedResource Include="Assets/yourfile.png" />`, search manifest resource names for one ending in your short filename, decode via reflection into `UnityEngine.ImageConversion.LoadImage`, cache the resulting `Sprite` statically.
  6. Optionally prefer a disk-file override before falling back to the embedded resource, writing the embedded bytes out to disk on first load for dev iteration.
- **Reusable pattern/snippet:**
```csharp
public class MyIndicator : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private RectTransform _rect;
    public void OnDrag(PointerEventData e) =>
        _rect.anchoredPosition += e.delta / StealthUIRoot.Instance.canvas.scaleFactor;
    public void OnEndDrag(PointerEventData e)
    {
        MyConfig.PosX.Value = _rect.anchoredPosition.x;
        MyConfig.PosY.Value = _rect.anchoredPosition.y;
        MyConfig.PosX.ConfigFile.Save(); // persist immediately, don't wait for normal save
    }
}
```

---

### 10. Armor / Item Stealth-Property Classifier

- **Purpose:** Derive stealth-relevant properties (visibility weight tier, noise material tier, camo affinity) from **any** equipped item — vanilla or from another mod — purely by inspecting its shared item data (name substring + armor value), without requiring per-item registration.
- **Key files:** `Armor/ArmorProfile.cs` (data model + enums), `Armor/ArmorProfileSystem.cs` (classification + cache), `Armor/ArmorUtils.cs` (aggregation across equipped items).
- **Architecture:**
  - `ArmorProfile`: `ArmorWeight Weight` (Light/Medium/Heavy), `ArmorMaterial Material` (Cloth/Leather/Metal/etc.), `bool HasCamoTag`.
  - `ArmorProfileSystem.GetProfile(ItemDrop.ItemData item)`: keyed cache (`Dictionary<string,ArmorProfile>` keyed by `item.m_shared.m_name`) so classification only runs once per distinct item name. Three private detectors: `DetectMaterial` (lowercase name substring match: "iron"/"bronze"/"metal"→Metal, "leather"/"hide"→Leather, else Cloth); `DetectWeight` (purely numeric on `m_armor`: ≤12→Light, ≤28→Medium, else Heavy); `DetectCamo` (name contains any of a fixed keyword list → `HasCamoTag = true`, keywords double as the biome-map keys used later in `CamoSystem`).
  - `ArmorUtils.GetTotalStealthPenalty(Player)`: sums a fixed penalty per `Weight` tier over every equipped item, `Clamp01`'d. `ArmorUtils.GetTotalArmorNoise(Player)`: same iteration, summing a fixed value per `Material`.
- **How to implement:**
  1. Define a small profile struct/class with the properties you actually need to drive gameplay math.
  2. Build a pure-function classifier keyed on the item's canonical name/shared data using **substring keyword matching** on the lowercased name — works automatically against any mod's armor without a lookup table, at the cost of being heuristic/best-effort.
  3. Cache classification results in a dictionary keyed by item name (not instance).
  4. Aggregate per-equipped-item contributions with simple per-tier constant sums, then `Clamp01` the total.
  5. Reuse the same keyword list both for "does this item have a camo tag at all" and for "which biome does this item's camo match."

---

### 11. Line-of-Sight & Environment Utilities

- **Purpose:** Small stateless helpers used across the sensing systems: raycasting for line-of-sight/occlusion, and weather/lighting queries against vanilla's environment manager.
- **Key files:** `Utils/RaycastUtils.cs`, `Utils/EnvironmentUtils.cs`.
- **Architecture:**
  - `RaycastUtils.HasLineOfSight(from, to)`: `!Physics.Linecast(from, to, TerrainMask)` where `TerrainMask` is a lazily-cached `LayerMask.GetMask(...)` — explicitly **excludes** the vegetation layer (mirroring vanilla `BaseAI.m_viewBlockMask`), since vegetation is handled as a soft visibility/hiding penalty rather than a hard LOS blocker, avoiding double-penalizing forest cover.
  - `AwarenessSystem.HasLineOfSight(Character c, Player target)` wraps the raycast but aims at `target.GetCenterPoint()` if crouching, else `target.m_eye.position` — matches vanilla's own aim point and avoids false "blocked" results from tracing to the player's feet.
  - `EnvironmentUtils`: thin wrappers — `GetFogDensity()`, `IsRaining/IsSnowing/IsMisty()` (substring match on current environment name), `GetAmbientLight()`.
- **How to implement:**
  1. Cache your `LayerMask` as a static lazily-initialized int rather than recomputing `LayerMask.GetMask(...)` every call.
  2. When building a LOS mask for AI sensing, match the target game's own internal view-block mask where possible, and exclude any layer whose blocking effect you're already modeling as a separate soft penalty.
  3. When raycasting toward a humanoid target, aim at their eye/head transform (or an explicit center point when crouched) rather than their base transform position.
  4. Wrap third-party/engine "current weather" state behind small named boolean queries using substring matching.

---

### 12. Configuration System (SOMConfig / StealthConfigModel / ServerSync integration)

- **Purpose:** Expose ~35 gameplay tunables as BepInEx config entries, keep them server-synchronized across multiplayer, and provide the hot-path code a plain POCO snapshot to read instead of dereferencing `ConfigEntry<T>.Value` every frame.
- **Key files:** `Config/SOMConfig.cs`, `Config/StealthConfigModel.cs`, `libs/ServerSync.cs`.
- **Architecture:**
  - `StealthConfigModel` is a plain mutable class with public fields for every tunable.
  - `SOMConfig` holds one `ServerSync.ConfigSync` instance (`ModRequired = false` so clients without the mod aren't kicked from a server running it) and a static `StealthConfigModel Active` snapshot.
  - **Per-value pattern** repeated ~35 times: `var entry = configSync.AddConfigEntry(file.Bind(section, key, default, description));` then `cfg.Field = Clamp(entry.Value, min, max);` then `entry.SourceConfig.SettingChanged += (_, _) => cfg.Field = Clamp(entry.Value, min, max);` — every config value is bound normally, wrapped for server-sync, and mirrored into the plain POCO snapshot both immediately and on every future change.
  - This means all hot-path code reads `SOMConfig.Active.VisionThreshold` etc. — a plain field read — rather than going through `ConfigEntry<T>.Value` property indirection, while still getting live updates the moment a server pushes a synced change.
  - Debug-only entries are plain unsynced `file.Bind` calls, exposed as public static `ConfigEntry<T>` fields directly (not mirrored into the model).
- **How to implement:**
  1. Vendor a copy of the community `ServerSync.cs` helper.
  2. Construct one `ConfigSync` per mod, keyed by GUID, with `ModRequired=false` unless you want to hard-require the mod on connecting clients.
  3. For every gameplay-affecting value, wrap the `Bind(...)` call in `configSync.AddConfigEntry(...)`, and if maintaining a plain POCO snapshot for hot-path reads, subscribe a `SettingChanged` handler to re-copy the (clamped) value.
  4. Keep purely-local/per-client settings on plain unsynced `Bind` calls.
  5. Guard any float clamp helper against `NaN`/`Infinity` before calling `Mathf.Clamp`.
  6. Do not gate config initialization behind any "am I the server yet" check performed at plugin `Awake()` time — every peer needs its own bound `ConfigEntry<T>` objects for `ServerSync` to sync data into regardless of role.

---

### 13. Plugin Bootstrap

- **Purpose:** Standard BepInEx plugin entry point wiring every other system's initialization into the correct order.
- **Key files:** `ShadowsOfMidgard.cs`.
- **Architecture:** `Awake()`, wrapped in a single try/catch, runs in this specific order: (1) `SOMConfig.Init(Config)`; (2) `StealthUIRoot.Init()` then `StealthUIController.Init()`; (3) `CoroutineManager.Bootstrap()` — **explicitly called** rather than relying on lazy creation, since it launches `StealthOvermind.EvaluateLoop()` which must be up before any `StealthBrain.Evaluate` call could occur; (4) `new Harmony(ModGUID)` + `_harmony.PatchAll(...)` — patches applied **last**, after all the systems they depend on already exist. `OnDestroy()` calls `_harmony?.UnpatchSelf()`.
- **How to implement:**
  1. Order `Awake()` as: config → UI/system singletons → background scheduler explicitly bootstrapped → Harmony `PatchAll` last, so every patch's dependencies are guaranteed initialized before the first patched method can possibly fire.
  2. Wrap the whole `Awake()` body in try/catch logging.
  3. Unpatch in `OnDestroy()` via `Harmony.UnpatchSelf()`.

---

### 14. Debug Logging Framework (StealthDebugger)

- **Purpose:** A single centralized gate for all mod debug output that avoids the "AI logs its unchanged state every frame → hundreds of thousands of log lines" failure mode, by logging on state *transitions* always, and otherwise throttling to a configurable heartbeat interval per (object, message-type) pair.
- **Key files:** `Systems/StealthDebugger.cs`.
- **Architecture:**
  - `ThrottleKey` struct = `(int InstanceId, int Slot)` where `Slot` is one of ~11 `const int` discriminators identifying the *message type* — so the same AI can independently throttle its "detection" log line and its "movement" log line on different cadences.
  - `ShouldLog(instanceId, slot, state)`: if a non-null `state` string is provided and differs from the last recorded state, **always logs** (treats it as a transition — "always interesting") and resets. Otherwise falls back to a heartbeat: logs only if `DebugLogInterval` seconds have elapsed since the last log for that key.
  - Periodic `Prune()` (every 30s wall-clock) removes throttle-dictionary entries not touched in the last 60s.
  - Each public `Log*` method additionally applies a domain-specific pre-filter before calling `ShouldLog`.
- **How to implement:**
  1. Build one static class as the sole entry point for all diagnostic logging.
  2. Key throttle state by `(objectInstanceId, messageTypeDiscriminator)` using small integer constants.
  3. Treat "the state string changed since last log" as an unconditional bypass of the throttle.
  4. Periodically prune stale throttle-dictionary entries on a wall-clock timer.
  5. Apply a cheap pre-filter before the throttle check for message types dominated by an uninteresting steady state.

---

### 15. Offline Log-Analysis Tooling (not part of the runtime mod)

- **Purpose:** Two standalone Python scripts used during development to cross-check debug log consistency, not compiled/shipped with the mod.
- **Key files:** `tools/check_can_sense_mismatch.py`, `tools/validate_markers.py`.
- **Technique worth carrying forward:** emit structured, greppable log lines (`[Tag] key=value key=value ...`) from parallel code paths that are supposed to agree, then write a small offline script that regexes both line types out of the log by a shared key tuple and diffs them — cheap way to catch cache/consistency bugs that are hard to spot by eye in a live session.
