# SoM v2 — Coordinator-verified facts

**Established 2026-07-31 during the design panel.** Every entry below was checked directly against
`libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs` (client build) or by an actual compile,
not taken from any design document. Line numbers are that file's.

These are the shared ground truth for synthesis and implementation. Where a design document contradicts an entry
here, this file wins.

---

## 1. Facts that CONFIRM claims made by the designs

| # | Fact | Evidence | Consequence |
|---|---|---|---|
| 1.1 | `Character.m_onDamaged` is a **public** `Action<float, Character>`, invoked unconditionally from `ApplyDamage` with `(totalDamage, hit.GetAttacker())` | field L6875; invoke L8871-8874; `BaseAI.Awake` itself subscribes at L4028 | The proposed `BaseAI.Awake` + `m_onDamaged` damage hook is sound and is what vanilla already does. Replaces both v1 damage patches. |
| 1.2 | `BaseAI.Alert()` is **public** and gated on `!IsAlerted()` | L5327-5329 | Confirms the central v1 defect: patching `IsAlerted` to return a computed value neuters `Alert()` for every caller, including other mods. |
| 1.3 | `BaseAI.SetAlerted(bool)` is `protected virtual`, overridden by `MonsterAI` | decl L5350; overrides L3800, L6513 | Callable directly under the publicized assembly, but it is virtual — bind it, do not assume the base body runs. |
| 1.4 | `Character.OnDamaged(HitData)` is `protected virtual` and **empty**; `Humanoid.OnDamaged(HitData)` overrides it **without calling base** | L9103-9105; L13249-13252 | Confirms v1's `Character_OnDamaged_Patch` never fired for greydwarves, draugr, fulings, skeletons, dvergr or trolls — every Humanoid. `Player.OnDamaged` (L21657) *does* chain, which is why it looked like it worked. |
| 1.5 | `m_targetCreature` is declared **private on `MonsterAI`**, not `BaseAI` | field L5727 inside `public class MonsterAI : BaseAI` at L5620 | Confirms v1's `FieldRefAccess<BaseAI, Character>` bound the wrong declaring type, threw, was swallowed, and left ally-calling inert. |
| 1.6 | `Game.SpawnPlayer` instantiates a **fresh** Player prefab and calls `ZNet.SetCharacterID(component.GetZDOID())` | L85762-85771 | **A player's `ZDOID` changes on every respawn.** Any per-player track or replicated target key that uses `ZDOID` orphans on death. |
| 1.7 | `ZDO.SetPosition` calls `IncreaseDataRevision()` when owner; `ZSyncTransform.OwnerSync` calls `SetPosition` on every position change | L62548-62556; L75161-75166 | **`ZDO.DataRevision` churns continuously for any moving owned creature.** A directive/policy cache keyed on `DataRevision` never hits for exactly the creatures that matter; it is stable only for stationary ones. |
| 1.8 | `RaycastCommand`, `NativeArray`, `Allocator`, `JobHandle` and `RaycastCommand.ScheduleBatch` all resolve against SoM's **existing** csproj reference set | Compile probe run inside the real project, 2026-07-31: compiles, zero new references. Valheim itself calls `ScheduleBatch` at L115123 | Batched raycasts are available with no new dependency. See §2.3 for the caveat that decides whether they actually help. |

## 2. Facts that REFUTE or qualify claims made by the designs

### 2.1 `ZDO.Set(int hash, ZDOID)` does not exist

The only ZDOID setter is `Set(string name, ZDOID id)` at **L62385**. Every other type carries both a `string`
and an `int hash` overload — `float` (L62417/L62422), `Vector3` (L62430/L62435), `Quaternion`, `int`, `bool`,
`long`, `byte[]`, `string`. ZDOID has **only** the string form.

**Consequence.** `DESIGN-pragmatic.md` §6.2 writes `zdo.Set(SoMKeys.DetTgt, st.AggTarget)` where `SoMKeys` is
specified (§1 layout, §7.3) as cached `GetStableHashCode()` ints. **That does not compile.** Any design
replicating a target identity must either use the string overload (re-hashing per write, which §7.3 explicitly
sets out to avoid) or not store a raw `ZDOID`.

Combined with fact 1.6, storing a player `ZDOID` is doubly wrong: unhashable by the fast path *and* unstable
across respawn. A stable `long` player id is the right key — but see the open question in §3.

### 2.2 `ZDOID.GetHashCode()` is not a free struct hash

```
public override int GetHashCode() => GetUserID(UserKey).GetHashCode() ^ ID.GetHashCode();
public static long GetUserID(ushort userKey) => m_userIDs[userKey];   // static List<long>
```
(~L64697 and L64612.)

Every hash resolves through a **static list indexer**. This is not catastrophic — a `List<T>` indexer is a bounds
check and a load — but it is not the register-only operation a `struct` key is usually assumed to be, and it puts
a shared static on the hot path. `Dictionary<ZDOID, …>` on a per-patch-call path is worth avoiding.

The specific figure of "~25 ns/probe" quoted in `DESIGN-throughput.md` is **not verified** — the mechanism is
real, the number is an estimate. Do not repeat it as measured.

### 2.3 Batched raycasts only move work off the main thread if `.Complete()` is deferred

`RaycastCommand.ScheduleBatch(cmds, hits, n).Complete()` — the form Valheim itself uses at **L115123** — blocks
the calling thread until the batch finishes. It parallelises *across workers*, which is a real win over serial
`Physics.Linecast`, but the main thread still waits for the slowest ray.

A cost model claiming main-thread LOS drops to sub-millisecond therefore holds **only** if the batch is scheduled
in one frame and completed in a later one. That is achievable but is not free:

- `Allocator.TempJob` has a ~4-frame lifetime; cross-frame retention wants `Allocator.Persistent` plus explicit
  disposal on scene unload and mod teardown, or it leaks native memory and logs Unity leak warnings.
- Results describe the physics scene **as of schedule time**, while creatures and players keep moving — one frame
  of LOS staleness on top of any existing recheck period.

This is flagged for the reviewer of `DESIGN-throughput.md` to resolve against what that document actually
specifies. **Unresolved at time of writing.**

### 2.4 The MistsofAvalor premise in `DESIGN-pragmatic.md` is obsolete

Verified in `c:\WubarrkCODING\MistsofAvalor` (v0.1.2, `PluginVersion` at `Core\MistsofAvalorPlugin.cs:74`):

- `Compat\AvalorStealthGuards.cs` **does not exist**.
- `AwarenessData` / `AwarenessSystem` appear only in **past-tense comments** at `Compat\AvalorStealthCompat.cs:21-22`
  describing the removed mechanism.
- **No** `HarmonyBefore` or `HarmonyAfter` anywhere in the tree; no Harmony patch on `BaseAI.IsAlerted`,
  `BaseAI.CanSenseTarget`, `Humanoid.StartAttack` or `MonsterAI.UpdateTarget`.
- MoA now does exactly what DvergrAllies does: stamps `SoMStealthExempt = 1`, owner-gated, re-asserted on a timer.
  It probes for the `StealthExemption` type only to choose a log line.

**Consequences.** `DESIGN-pragmatic.md` §2.8 `Compat/ExternalPin.cs` — which that document calls "the single
highest-leverage compat decision in v2" — reads MoA's writes into `AwarenessData` back as authoritative input.
Nothing writes there any more. And its §5.7 coexistence table enumerates MoA prefixes that do not exist.

What survives the correction: `AwarenessData` must stay a public class with v1 field names, and
`AwarenessSystem.GetData(Character)` must stay static and never return null — **old builds of both siblings are
in the wild** and the type is still reflection-probed. And SoM must still coexist with *arbitrary* third-party
mods. The error was over-fitting to one sibling's retired workaround, not caring about compat.

### 2.5 "Alerted ⇒ skip the LOS raycast" is a behavioural regression, and TWO designs share it

`BaseAI.CanSeeTarget(Transform, Vector3, float, float, bool alerted, bool, Character)` at **L4582-4621**:

```csharp
if (!alerted && Vector3.Angle(target.transform.position - me.position, me.forward) > viewAngle)
    return false;                                                        // L4608 - the ONLY use of `alerted`
...
if (Physics.Raycast(eyePoint, vector2.normalized, vector2.magnitude, m_viewBlockMask))
    return false;                                                        // L4614 - UNCONDITIONAL
```

`alerted` suppresses **only the field-of-view cone**. The view-block raycast and the mist check run regardless.
An alerted vanilla creature still loses sight of a target behind a wall.

**Both `DESIGN-pragmatic.md` §4.4 (`needLos = !Dir.IgnoreLos && trk.State < VanillaAlertness.Alerted`) and
`DESIGN-throughput.md` §4.3 (`ignoreLos = alerted || Dir.IgnoreLos`) skip the cast once a track reaches
Alerted**, and both justify it with a comment asserting that vanilla ignores LOS when alerted. It does not.

**The failure is a latch, not a one-off.** Once `needLos` is false the LOS bit never refreshes, so "can see"
degenerates to a pure distance/visibility test. That keeps detection pinned at maximum, which keeps the state at
Alerted or above, which keeps `needLos` false. There is no exit except the player leaving visual range entirely.
A player who alerts a greydwarf, runs indoors, shuts the door and crouches is still seen through the wall;
`MonsterAI.UpdateTarget` keeps resetting `m_timeSinceSensedTargetCreature`, so vanilla's 30 s give-up never
fires either.

This directly contradicts the acceptance criterion both documents share — that a creature should lose a player
who breaks line of sight — and it contradicts v1, which computed LOS unconditionally
(`Systems/StealthBrain/SensingEvaluator.cs`) and was correct on this point.

**Any synthesised design must keep casting for alerted tracks** (a longer recheck period is fine) and use
`los = HadLos || Dir.IgnoreLos`. Only an explicit `SoM_IgnoreLoS` directive may skip the cast.

### 2.6 The mist gate is silently deleted by every design

The last thing vanilla's `CanSeeTarget` does, **after** the view-block raycast (L4614), is:

```csharp
if (!mistVision && ParticleMist.IsMistBlocked(eyePoint, vector)) return false;   // L4618
```

`BaseAI.m_mistVision` is the per-creature opt-out. A sight prefix that sets `__result` and returns `false`
skips the original, so **L4618 never runs**. Every non-`m_mistVision` creature in the Mistlands then sees
through the mist whenever SoM holds an opinion.

`grep -i mist` returns **zero hits in all three design documents**. This is a shared omission, not a flaw in
any one of them, and it lands in the biome where sight-blocking is the core mechanic.

Any synthesised design must sample `ParticleMist.IsMistBlocked` alongside the view-block cast and read
`m_mistVision` off the cached `BaseAI`. It may only ever contribute "blocked", never "clear".

### 2.7 Patching the instance sense methods does NOT affect `CanSenseTarget`

```csharp
public bool CanSenseTarget(Character target) => CanSenseTarget(target, m_passiveAggresive);        // L4519
public bool CanSenseTarget(Character target, bool passiveAggresive)
    => CanSenseTarget(transform, m_character.m_eye.position, m_hearRange, m_viewRange, m_viewAngle,
                      IsAlerted(), m_mistVision, target, passiveAggresive, m_character.IsTamed());  // L4524
public static bool CanSenseTarget(...)                                                              // L4529
{
    ...
    if (CanHearTarget(me, hearRange, target)) return true;          // L4534 - STATIC
    if (CanSeeTarget(me, eyePoint, viewRange, viewAngle, alerted, mistVision, target)) return true; // L4538 - STATIC
    return false;
}
```

The instance `CanSenseTarget` reaches the **static** `CanHearTarget`/`CanSeeTarget`, never the instance
overloads. So a design that patches the instance `CanSeeTarget`/`CanHearTarget` and *not* `CanSenseTarget`
leaves `FindEnemy` (which calls `CanSenseTarget`, L5203) answering from vanilla while `UpdateTarget` (which
calls the instance `CanSeeTarget`, L5920) answers from SoM — **two perception models on one creature in one
frame.**

Consequence: the three sense patches are an **atomic install group**. Either all three install or none may.
A per-patch "skip on missing target" installer must treat them as one unit.

### 2.8 `UpdateTarget` runs the instance sense methods at 20 Hz, not every 2 s

The `m_updateTargetTimer` block (L5849-5860) throttles **`FindEnemy` only** — it closes before:

```csharp
if ((bool)m_targetCreature)
{
    canHearTarget = CanHearTarget(m_targetCreature);     // L5919
    canSeeTarget  = CanSeeTarget(m_targetCreature);      // L5920
```

and `UpdateTarget` is called unconditionally from `MonsterAI.UpdateAI`, which `MonoUpdaters.FixedUpdate` drives
at a hard 20 Hz. So every owned, awake, target-holding creature runs both instance sense methods **20×/s**.

Consequence: any cost model that books vanilla sensing at the 2 s/6 s `FindEnemy` cadence is wrong, and any
design whose "budget exhausted ⇒ fall through to vanilla" path claims the fallback is cheap is wrong — the
fallback is the same `Physics.Raycast` at 20 Hz per creature, uncapped.

## 3. Open question — RESOLVED

**The stable `long` player key exists, is ZDO-backed, and is remote-readable.**

```csharp
public void SetPlayerID(long playerID, string name)                 // L15973
{
    if (m_nview.GetZDO() != null && GetPlayerID() == 0L)
    {
        m_nview.GetZDO().Set(ZDOVars.s_playerID, playerID);
        ...
    }
}
public long GetPlayerID()                                           // L15982
{
    if (!m_nview.IsValid()) return 0L;
    return m_nview.GetZDO().GetLong(ZDOVars.s_playerID, 0L);
}
```

It is read straight off the ZDO, so it resolves for a **remote** player from a replicated character ZDO with no
loaded `Player` instance — `ZNet.GetAllCharacterZDOS()` (L68699) is enough. The profile-level `m_playerID` is
generated once per character profile and persisted, and `Game.SpawnPlayer` re-applies it on every respawn, so it
is stable across death, zone unload and ownership handover — everything `ZDOID` is not (fact 1.6).

**Decision: per-player tracks key on `long` playerID. `ZDOID` is not used as a player identity anywhere,
neither in memory nor on the wire.**

## 4. Verified non-vanilla facts

- SoM currently compiles clean at 0 warnings (`dotnet msbuild -t:Compile`). `version.txt` = **1.9.0**.
- Building via the `Build` target has two side effects: `SoMTickVersion` bumps the patch component in
  `version.txt`, and `SoMStageHexium` copies the DLL into `HexiumDistrib\plugins`. Use `-t:Compile`, or
  `-p:SoMSkipVersionTick=true`, for a check that does not mutate the tree.
- All eight `StealthExemption` guard sites are present and correct; a ninth was added at
  `Systems\CoroutineManager.cs:105` on 2026-07-31, closing the gap recorded in `AVALOR_COMPAT_HANDOFF.md` §10.
- `Systems\CoroutineManager.cs` `UpdateNearbyAlliesCoroutine` had a reachable infinite-loop hang: every `continue`
  skipped the only per-iteration `yield`, and the fallback yield was gated on `allAIs.Count == 0`. Fixed the same
  day by yielding unconditionally per lap. Relevant to v2 because that coroutine is slated for deletion — if it
  is deleted, the bug goes with it; if any part is ported, the yield discipline must come too.
