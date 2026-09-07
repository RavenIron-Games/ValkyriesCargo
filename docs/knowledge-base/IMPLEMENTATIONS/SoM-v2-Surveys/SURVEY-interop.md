# SoM v2 INTEROP SURVEY — consumers, coupling surface, and a proposed compat standard

Everything below was read, not inferred. Line numbers are exact as of the files on disk today.

---

## PART 1 — ZDO KEY INVENTORY (who writes what, who reads what)

### 1.1 The one shared key today

| Key string | Type | Writers | Readers | Values seen |
|---|---|---|---|---|
| `"SoMStealthExempt"` | ZDO **int** | MoA ×3 sites, DvergrAllies ×1 site | SoM ×7 patch sites (via `StealthExemption.IsExempt`), MoA ×4 sites | `1` = exempt, `0` = explicitly not exempt |

**Declaration sites (three independent copies of the literal — no shared header):**

- SoM: `c:\WubarrkCODING\ShadowsOfMidgard\Systems\StealthExemption.cs:5` — `public const string ZDOKey = "SoMStealthExempt";`
- MoA: `c:\WubarrkCODING\MistsofAvalor\Mobs\AvalorMobDirector.cs:282` — `public const string SoMStealthExemptKey = "SoMStealthExempt";`
- DvergrAllies: `c:\WubarrkCODING\DvergrAllies\DvergrGenetics.cs:17` — `public const string StealthExemptKey = "SoMStealthExempt";`

**Write sites:**

| File:line | Guard | Value |
|---|---|---|
| `MistsofAvalor\Mobs\AvalorMobDirector.cs:291` (`MarkAsAvalorMob`) | `znv.IsValid() && GetZDO()!=null` — **no `IsOwner()` guard** | `1` |
| `MistsofAvalor\Core\MistsofAvalorPlugin.cs:300-301` (`Character.Awake` postfix) | `inMaze && nview.IsOwner() && GetInt(...)!=1` | `1` |
| `MistsofAvalor\Compat\AvalorStealthCompat.cs:472-474` (`VerifyAll` re-stamp repair) | `nview.IsOwner() && zdo!=null` | `1` |
| `DvergrAllies\DvergrGenetics.cs:40` (`RefreshStealthExemption`) | caller-gated `IsOwner()` (`:58` Awake, `:130` SetTamed postfix) | `isAlly ? 1 : 0` — **explicit 0** |

**Read sites in SoM** — all via `StealthExemption.IsExempt`, all with the identical shape `zdo.GetInt(ZDOKey, 0) == 1` (`StealthExemption.cs:13`):

`BaseAI_CanHearTarget_Patch.cs:31,52` · `BaseAI_CanSeeTarget_Patch.cs:31,52` · `BaseAI_StealthBrain_Patch.cs:29,58` (CanSenseTarget) · `BaseAI_StealthBrain_IsAlerted_Patch.cs:18,37` · `BaseAI_SetMoveDir_Patch.cs:25` · `Humanoid_Attack_Patch.cs:34` · `MonsterAI_StealthBrain_UpdateAI_Patch.cs:23` · `StealthOvermind.cs:68`.

Two SoM patches **do not** check exemption and write `AwarenessData` for exempt creatures anyway: `Character_Damage_Patch.cs:25-43` and `Character_OnDamaged_Patch.cs:29-48`. Harmless today (nothing reads it for an exempt creature) but it is why `AwarenessSystem`'s dictionary accumulates entries for creatures SoM has opted out of.

**Load-bearing detail confirmed against the decompiled assembly** (`assembly_valheim.decompiled.cs:62464-62500, 62713-62741`): `ZDO.Set(string,bool)` writes into the *same int store* as `Set(string,int)` (`Set(hash, value ? 1 : 0)`), and `GetBool(hash, def)` is literally `GetInt(...) != 0`. So bool and int are wire-interchangeable for this key — a future writer using `Set(key,true)` and SoM's `GetInt(key,0)==1` agree. Also: `GetInt(string, out int)` returns *whether the key exists*, which is the only primitive that distinguishes "absent" from "explicitly 0". Nobody uses it today; the design in Part 5 does.

### 1.2 Other ZDO keys in the neighbourhood (not SoM's, but they gate SoM-relevant behaviour)

| Key | Owner | Relevance |
|---|---|---|
| `"AvalorWarden"` (`AvalorMobDirector.cs:376`) | MoA | The one creature MoA deliberately does **not** give hunt senses. Read at `AvalorStealthCompat.cs:383`, `AvalorHuntEnforcer.cs:78`, `MistsofAvalorPlugin.cs:312`. Today this exception is expressed as an `if` in `ReassertVanillaHunt`; in the proposal it becomes a different set of SoM keys. |
| `"AvalorRevenant"` (`:275`), `"Avalor_HPBuffed"` (`MistsofAvalorPlugin.cs:252`), `SubBossZDOKey` | MoA | Not SoM-facing. Listed only because they share the same `Character.Awake` postfix and the same try/catch, which matters for the failure analysis in Part 4. |
| `"dvergr_gender"` (`DvergrGenetics.cs:60,81,111`) | DvergrAllies | Not SoM-facing. |

---

## PART 2 — SoM'S REFLECTION SURFACE AS CONSUMED BY MoA

All of it lives in `c:\WubarrkCODING\MistsofAvalor\Compat\AvalorStealthCompat.cs`. Nothing else in MoA reflects into SoM. DvergrAllies reflects **nothing** — it writes one ZDO int and is otherwise completely decoupled.

### 2.1 Types probed by name (`AccessTools.TypeByName`)

| # | Type name (exact string) | Site | Required? | Consequence if missing |
|---|---|---|---|---|
| 1 | `ShadowsOfMidgard.AwarenessSystem` | `:184` | **YES — this is MoA's entire SoM-presence detector** | `Mode = NoStealthMod`, logs `[Avalor][OK] no stealth mod detected`, **returns at `:191` before ever probing `StealthExemption`** |
| 2 | `ShadowsOfMidgard.AwarenessData` | `:247` | YES for pinning | `ResolvePinningHandles` returns false |
| 3 | `ShadowsOfMidgard.VanillaAlertness` | `:248` | YES for pinning | `ResolvePinningHandles` returns false |
| 4 | `ShadowsOfMidgard.FleeReason` | `:249` | optional (`if (flee != null)`) | `_valFleeNone` stays null, `_fFleeReason?.SetValue` skipped |
| 5 | `ShadowsOfMidgard.StealthExemption` | `:202` | optional | falls through to `Mode = Pinning` at `:223` |

> **Finding A (ordering bug in MoA, worth telling the MoA side about):** the presence check is `AwarenessSystem`, not `StealthExemption`. If SoM v2 renames or re-namespaces `AwarenessSystem` while keeping `StealthExemption` verbatim — exactly what the brief mandates — MoA prints a **green** `[OK] no stealth mod detected` line and never verifies the exemption again. The type-name `ShadowsOfMidgard.AwarenessSystem` is therefore just as load-bearing to MoA today as `StealthExemption` is.

### 2.2 Methods probed

| Handle | Resolved as | Site | Signature required |
|---|---|---|---|
| `_getData` | `AwarenessSystem.GetData(Character)` | `:253` | **static**, exactly one `Character` parameter, returns something whose fields are settable |
| `_isExempt` | `StealthExemption.IsExempt(Character)` | `:205` | **static**, returns `bool`; invoked at `:480` and unboxed with a hard `(bool)` cast |

Both are invoked with `Invoke(null, …)` (`:416`, `:480`) — an instance method would throw `TargetException`.

### 2.3 **Every field name MoA reflection-writes into `AwarenessData`** (`Pin()`, `:412-445`)

| MoA handle | Field name string | Resolved at | Required for `_canDriveSoM` | Write in `Pin()` | Exists in SoM today? |
|---|---|---|---|---|---|
| `_fCurrentState` | `"CurrentState"` | `:254` | **YES** (`:265`) | `= VanillaAlertness.Alerted` (`:419`) | ✅ `UnifiedStealthTypes.cs:72` |
| `_fDetectionLevel` | `"DetectionLevel"` | `:255` | **YES** (`:265`) | `= 1f` (`:420`) | ✅ `:62` |
| `_fCanSense` | `"CanSense"` | `:256` | **YES** (`:266`) | `= true` (`:421`) | ✅ `:67` |
| `_fCanSee` | `"CanSee"` | `:257` | no (`?.`) | `= true` (`:422`) | ✅ `:68` |
| `_fCanHear` | `"CanHear"` | `:258` | no (`?.`) | `= true` (`:423`) | ✅ `:69` |
| `_fFleeReason` | `"CurrentFleeReason"` | `:259` | no (`?.`) | `= FleeReason.None` (`:428`) | ✅ `:93` |
| `_fAggression` | `"AggressionLevel"` | `:260` | no (`?.`) | `= 1f` (`:429`) | ❌ **DOES NOT EXIST** |

> **Finding B (dead write, live today):** `AwarenessData` has no `AggressionLevel` field. `AggressionLevel` exists only on `CombatDirective` (`UnifiedStealthTypes.cs:123`) and `StealthDebugData` (`:216`). `AccessTools.Field` returns `null`, the `?.SetValue` is a silent no-op, and it has been one since the line was written. The comment justifying it (`AvalorStealthCompat.cs:425-427`: *"StartAttack … derives its cooldown from AggressionLevel (1.5s / max(0.5, level))"*) describes an SoM that no longer exists — current `Humanoid_Attack_Patch.cs:9-13` explicitly states *"Attack pacing is left to vanilla (m_aiAttackInterval / m_minAttackInterval)"*. This is a documented-in-comments contract that drifted with zero signal on either side. It is the cleanest single argument for the whole proposal.

### 2.4 Enum members parsed by name — the only **throwing** coupling

```csharp
// AvalorStealthCompat.cs:262-263
_valAlerted = System.Enum.Parse(alertness, "Alerted");
if (flee != null) _valFleeNone = System.Enum.Parse(flee, "None");
```

`Enum.Parse` throws `ArgumentException` on a missing member. `ResolvePinningHandles` has **no try/catch**, nor does `Init()`, nor `EnsureInit()`. See Part 4 scenario S5 for what that costs.

### 2.5 Behavioural assumptions MoA encodes about SoM (not reflected, but depended on)

- `GetData` **creates on demand** and never returns null — `AvalorStealthCompat.cs:417` treats null as "should not happen" and returns silently. True today (`AwarenessSystem.cs:11-20`).
- `AwarenessData` is a **reference type** — `Pin()` mutates the object returned by `Invoke`. True today (`UnifiedStealthTypes.cs:59` `public class AwarenessData`).
- SoM's gates are keyed on `CurrentState >= Alerted` — asserted at `AvalorStealthCompat.cs:22`, `:420`. True today (`Humanoid_Attack_Patch.cs:43`, `BaseAI_StealthBrain_Patch.cs:36`).
- SoM's patches bail unless `ZNetView.IsOwner()` — asserted at `AvalorStealthCompat.cs:342`, which is why MoA's own pin is owner-gated (`:325-326`, `:356`). True today (`AIAuthority.cs:9-19`).
- SoM re-evaluates **every frame** via a `MonsterAI.UpdateAI` postfix + the Overmind coroutine — asserted at `AvalorStealthGuards.cs:9-12`. Broadly true (`StealthOvermind.cs:100-131` tiers at 2/6/20 frames by distance; `MonsterAI_StealthBrain_UpdateAI_Patch` is apply-only).

---

## PART 3 — HARMONY PATCH MAP AND ORDERING

MoA's Harmony instance id is whatever `MistsofAvalorPlugin.harmony` was built with; SoM's is `"wubarrk.shadowsofmidgard"` (`ShadowsOfMidgard.cs:11,36`). MoA names **both casings** defensively (`AvalorStealthGuards.cs:45-46`):

```csharp
private const string SoM    = "wubarrk.shadowsofmidgard";
private const string SoMAlt = "wubarrk.ShadowsOfMidgard";
```

Only the lowercase one matches today. Harmony ignores an id that matches nothing, so the alt is free.

### 3.1 Contested methods

| Vanilla target | MoA patch (`AvalorStealthGuards.cs`) | MoA kind / priority / ordering | SoM patch | SoM priority | Effective order |
|---|---|---|---|---|---|
| `BaseAI.IsAlerted` | `Avalor_IsAlerted.Prefix` `:54-59` | **void** prefix, `Priority.First` (800), `HarmonyBefore(SoM, SoMAlt)` | `BaseAI_StealthBrain_IsAlerted_Patch.Prefix` (returns `bool`, skips original) | `int.MaxValue` | **MoA first** — the explicit `HarmonyBefore` edge is applied as a topological constraint over the priority ordering, so it beats `int.MaxValue` |
| ″ | `Avalor_IsAlerted.Postfix` `:61-70` | postfix, `Priority.Last` (0), `HarmonyAfter(SoM, SoMAlt)` | SoM `Postfix` | `int.MinValue` | **MoA last** |
| `BaseAI.CanSenseTarget(Character)` | `Avalor_CanSenseTarget.Prefix` `:79-84` | void prefix, `Priority.First`, `HarmonyBefore` | `BaseAI_StealthBrain_Patch.Prefix` (skips original) | `int.MaxValue` | MoA first |
| ″ | `Avalor_CanSenseTarget.Postfix` `:86-109` | postfix, `Priority.Last`, `HarmonyAfter` | SoM `Postfix` | `int.MinValue` | MoA last |
| `Humanoid.StartAttack` | `Avalor_StartAttack.Prefix` `:118-124` | void prefix, `Priority.First`, `HarmonyBefore` | `Humanoid_Attack_Patch.Prefix` (returns `bool`) | **none declared → `Priority.Normal` (400)** | MoA first by *both* mechanisms |
| `MonsterAI.UpdateAI` | `Avalor_UpdateAI.Prefix` `:145-150` | void prefix, `Priority.First`, `HarmonyBefore` | `MonsterAI_StealthBrain_UpdateAI_Patch.Postfix` | `int.MinValue` | no real contest — different injection points |
| `MonsterAI.UpdateTarget` | `Avalor_UpdateTarget.Prefix` `:132-137` | void prefix, `Priority.First`, `HarmonyBefore` | **SoM does not patch `UpdateTarget` at all** | — | `HarmonyBefore` names a patch that isn't there; the prefix serves purely as a pin point ahead of vanilla's own `CanSeeTarget`/`CanHearTarget` calls |

All five MoA guard classes **are** registered — individually, by hand, at `MistsofAvalorPlugin.cs:207-211` via `ApplyPatch` → `harmony.CreateClassProcessor(patchType).Patch()` (`:218-230`). (The `IMPLEMENTATIONS/MistsofAvalor.md:273-275` "never registered, dead code" note refers to the state **before** that block was added; the registration lines and their long comment at `:190-206` are the fix.)

### 3.2 SoM patches MoA does **not** contest

`BaseAI.CanHearTarget(Character)`, `Character.SetMoveDir`, `Character.Damage`, `Character.OnDamaged`, `Character.OnDestroy`.

> **Finding C:** hearing is the asymmetry. SoM's `CanHearTarget` prefix answers from `data.CanHear` (`BaseAI_CanHearTarget_Patch.cs:37`) and MoA has **no Harmony guard for it** — its only lever is the *optional* `_fCanHear` reflection write (`AvalorStealthCompat.cs:423`). So the moment reflection stops working, MoA loses hearing entirely with no fallback, and it loses it *silently*, because `_fCanHear` is an optional handle whose absence is not counted anywhere.

### 3.3 The re-entrancy bracket

`AvalorStealthCompat.cs:290-296` (`_readingRealAlert` / `BeginRealAlertRead` / `EndRealAlertRead` / `SuppressAlertForce`), consumed at `AvalorStealthGuards.cs:68` and driven from `AvalorMobDirector.cs:808-810` and `AvalorStealthCompat.cs:388-390`.

It exists **only** because MoA forces `IsAlerted()` true, and vanilla `Alert()` is `if (m_nview.IsValid() && !IsAlerted()) SetAlerted(true);` — so the force makes `Alert()` a permanent no-op and `SetAlerted` (animator bool, ZDO replication, `m_alertedEffects`) never runs. Kill the force and this entire mechanism, plus both of its call sites, becomes deletable. That is a real, concrete deliverable of the proposal.

---

## PART 4 — WHAT BREAKS IN MoA IF SoM v2 RESTRUCTURES `AwarenessData`

The overhaul brief mandates **per-player detection tracks**. That is precisely the restructure that breaks the surface in Part 2.3. Scenario by scenario:

**S1 — `AwarenessSystem` renamed or moved namespace.**
`Init()` takes the `:189-191` branch. `Mode = NoStealthMod`; log line is `[Avalor][OK] no stealth mod detected - vanilla AI, no compat needed.` `_isExempt` never resolved → `NeedsVerify` false (`:121`) → `VerifyAll` never runs → the missed-stamp **repair** at `:472-474` never runs, so mobs whose `Character.Awake` ran before `ZNetView` was ready stay permanently invisible to SoM's exemption. Vanilla pinning continues, so nothing looks broken. **Failure mode: a green log line that is a lie.**

**S2 — `GetData(Character)` replaced by a per-player accessor (`GetTrack(Character, Player)`, `GetData(Character, long playerId)`, …).**
`AccessTools.Method(awareness, "GetData", new[]{typeof(Character)})` → `null` → `ResolvePinningHandles` returns false at `:265` → `_canDriveSoM = false`. Then `:202-213` finds `StealthExemption.IsExempt` and **returns at `:213` with the healthy `FlagHonoured` message**.

> **Finding D — the single most important one for the architects.** The `[Avalor][FAIL] … its awareness internals did not resolve …` warning at `:231-235` is **unreachable whenever `StealthExemption.IsExempt(Character)` exists**, because the `FlagHonoured` branch returns first. Since the brief freezes `StealthExemption` verbatim, **every** `AwarenessData` restructure will be reported by MoA as fully healthy. MoA's loudest diagnostic about the exact thing v2 is going to change is dead code.

**S3 — required field renamed** (`CurrentState`→`State`, `DetectionLevel`→`Level`, `CanSense`→`SensedThisTick`). Identical to S2: `ResolvePinningHandles` false at `:265-266`, healthy log, no pin, no warning.

**S4 — fields survive but become non-authoritative** (per-player tracks own the real state; `AwarenessData.CurrentState` demoted to a cached aggregate recomputed every eval, or to a `{ get; }` computed property).
`_canDriveSoM = true`. `Pin()` succeeds, throws nothing, logs nothing. The write is overwritten within one frame — or, if the member became a property, `AccessTools.Field` returns `null` for the *required* handles and we degrade to S3. **The pure-silent case.** MoA has zero detectors for it: `VerifyAll` only asks `IsExempt`, which is independent of `AwarenessData` and will keep answering `true`. This is the exact shape MoA's own v0.0.9 post-mortem (`IMPLEMENTATIONS/MistsofAvalor.md:260-264`) says must never be shipped again — and SoM v2 would be shipping it *for* them.

**S5 — `VanillaAlertness` keeps its name but loses/renames the member `Alerted`** (e.g. `Alerted`→`Aware`, or the enum is replaced by a float).
`System.Enum.Parse(alertness, "Alerted")` at `:262` **throws**. Nothing catches it in `ResolvePinningHandles`, `Init`, or `EnsureInit`. `Mode` is still `Undetermined` (it is only assigned *after* the parse), so:
- from `AvalorStealthPinner.Update()` (`:570`) — **throws every frame, forever**, and aborts the rest of `Update` so `PinAll` and `VerifyAll` never run;
- from `Register()` (`:276`, called at `MistsofAvalorPlugin.cs:308`) — caught by the `Character.Awake` postfix's try/catch (`:256`, `:381-384`) and reported as `"[Avalor] enemy-buff patch threw (skipped this spawn, no harm)"` — which is wrong twice: it is not harmless, and the abort **also skips** the `AvalorHuntEnforcer.Enqueue`, the boss-bar `Track`, the Warden/Revenant naming and the sub-boss local mutations for that spawn (`MistsofAvalorPlugin.cs:328-378`).

Same applies to `Enum.Parse(flee, "None")` at `:263` if `FleeReason` keeps its name but drops `None`.

**S6 — `AwarenessData` becomes a `struct`.**
`_getData.Invoke` returns a **boxed copy**. `FieldInfo.SetValue` mutates the box. `Pin()` throws nothing and writes nothing. Silent, total.

**S7 — `AwarenessSystem.GetData` becomes an instance method.**
`Invoke(null, …)` throws `TargetException` → caught at `:432` → `_canDriveSoM = false` + one clear warning. **Acceptable failure.**

**S8 — `GetData` starts returning `null`** for creatures the simulator does not own / exempt creatures / creatures with no track yet.
`Pin()` returns silently at `:417`. No strike, no log, no counter. Silent.

**S9 — `GetData(Character)` survives but returns the track for a "primary"/local player.**
MoA pins only that one track. Every remote player is unaffected → in co-op the labyrinth hunts the host and ignores everyone else. Silent, and specifically a *multiplayer-only* regression, i.e. the hardest kind to catch in a solo playtest.

**S10 — `StealthExemption` survives but `IsExempt(Character)` changes shape** (adds a parameter, becomes non-static, returns a nullable/enum).
`:218-220` logs a proper `[FAIL] … the exemption contract has changed`, `Mode = Pinning`, pinning continues. **The one restructure MoA handles correctly.** Note this is only true for the `Character` overload — MoA never probes `IsExempt(BaseAI)`, so that overload is load-bearing for *other* consumers' existence-probes but not for MoA's.

### Summary table

| Scenario | `_canDriveSoM` | Log | Real effect | Verdict |
|---|---|---|---|---|
| S1 rename `AwarenessSystem` | false | green `[OK] no stealth mod` | no verify, no stamp repair | **silent + misleading** |
| S2 `GetData` signature change | false | green `FlagHonoured` | no pin at all | **silent (Finding D)** |
| S3 field rename | false | green `FlagHonoured` | no pin at all | **silent (Finding D)** |
| S4 fields demoted to cache | **true** | green `FlagHonoured` | pin overwritten each frame | **silent, worst case** |
| S5 enum member removed | n/a | exception spam | pinner Update dies; spawns half-initialised | loud but catastrophic |
| S6 `AwarenessData` → struct | true | green | writes discarded | **silent** |
| S7 `GetData` → instance | false | correct `[FAIL]` | vanilla pin only | acceptable |
| S8 `GetData` returns null | true | none | no-op | **silent** |
| S9 per-player, local-player-shaped | true | green | host-only hunting in MP | **silent** |
| S10 `IsExempt` shape change | either | correct `[FAIL]` | vanilla pin only | correct |

**Net:** 7 of 10 realistic restructures are silent, 1 is catastrophic, 2 are handled. And because MoA v0.0.9 pins **unconditionally** and no longer bets the labyrinth on SoM, the *player-visible* damage in most of these is confined to SoM's `Humanoid.StartAttack` gate (`Humanoid_Attack_Patch.cs:43`) — mobs that chase and never swing. Which is exactly the symptom the whole compat file was written to fix in the first place.

### What DvergrAllies loses under the same restructures

**Nothing**, provided two things stay true:
1. `StealthExemption.IsExempt` keeps reading `GetInt("SoMStealthExempt", 0) == 1`. DvergrAllies writes an explicit `0` for wild Dvergr (`DvergrGenetics.cs:40`, rationale at `:15-16`), so any move to an *existence* check (`GetInt(key, out v)`) must still treat present-and-0 as not-exempt.
2. SoM keeps skipping tamed creatures independently (it does: `IsTamed()` at seven sites plus `StealthOvermind.cs:61-62`). The flag is genuinely load-bearing only for DvergrAllies' **custom ally prefabs**, which are `Character.Faction.Players` but *not* tamed (`DvergrGenetics.cs:39`).

---

## PART 5 — PROPOSAL: THE SoM INTEROP CONTRACT v2

### 5.0 Design axioms

1. **SoM knows nothing about any mod.** No GUID checks, no `Chainloader.PluginInfos` lookups, no per-mod branches. Ever.
2. **Zero assembly coupling both ways.** No SoM-defined type appears in any interop signature. Every parameter and return is a primitive (`int`, `float`, `bool`, `string`, `string[]`) or a **vanilla** type (`Character`, `BaseAI`, `Player`, `ZDO`). Consumers may use the contract with *no reflection at all* — plain ZDO reads and writes suffice for everything except version discovery.
3. **`StealthExemption` is frozen.** Type name, both `IsExempt` overloads, and the `ZDOKey` const survive verbatim and keep their *current* semantics: "does this ZDO carry `SoMStealthExempt == 1`". It must **not** be redefined as "is the brain off for this creature" — MoA uses it at `AvalorStealthCompat.cs:480` to verify its own stamp round-tripped, and re-pointing it at a resolved answer would make MoA start striking against a working SoM. The resolved question gets a new name (§5.3, `IsBrainOff`).
4. **Unconditional on the writer's side.** A consumer writes its keys once, at spawn, owner-gated, and never branches on whether SoM is installed or which version it is. Absent SoM the writes are inert ZDO ints. This is the direct application of MoA's lesson: *a conditional remedy has as many silent failure modes as it has conditions* — so the contract must have **zero** conditions on the consumer side.
5. **Fail safe AND loud.** Safe = fall back to the older, already-shipped behaviour, never to a guess. Loud = a log line, plus a machine-readable signal the consumer can act on without reflecting into SoM.
6. **One resolver, no parallel implementation.** The probe in §5.3 must be produced by the same code path the patches gate on. MoA's lesson #2 — *a self-test that measures your own patch is worse than no self-test* — generalises to: a self-description computed by a second implementation is worse than no self-description.

### 5.1 Tier 1 — the boolean (frozen forever, v1)

| Key | Type | Values | Default | Semantics |
|---|---|---|---|---|
| `SoMStealthExempt` | int | `1` = exempt; anything else (incl. explicit `0` and absent) = not exempt | absent ≡ 0 | SoM's stealth brain does not run for this creature. Every SoM patch returns control to vanilla; the Overmind skips it. |

Read shape frozen as `zdo.GetInt("SoMStealthExempt", 0) == 1`. **DvergrAllies is correct today and correct forever under this contract with zero changes.**

### 5.2 Tier 2 — declarative directives (contract v2)

The brain **runs** and honours these. All keys are per-creature and, after the per-player-track refactor, apply **identically to every player's track on that creature** — that is part of the documented semantics, not an implementation detail.

| Key | Type | Range | Absent ⇒ | Semantics |
|---|---|---|---|---|
| `SoM_Contract` | int | ≥ 2 | v1 mode (see §5.4) | **Mandatory whenever any other `SoM_*` key is written.** The contract version the writer authored against. This is the whole handshake. |
| `SoM_BrainOff` | int | 0/1 | 0 | v2 spelling of Tier 1. A v2 writer never has to touch the legacy key. |
| `SoM_AlertFloor` | int | 0..3 (`Unaware/Suspicious/Alerted/Engaged` ordinals) | none | Every track on this creature is floored at this alertness. Decay may not take it lower. `2` is what opens `Humanoid.StartAttack`. |
| `SoM_DetectFloor` | float | 0.0..1.0 | 0 | Floor on `DetectionLevel` per track. |
| `SoM_SightRange` | float | metres, > 0 | `cfg.MaxVisualRange` (40) | Per-creature override of visual range. |
| `SoM_HearRange` | float | metres, > 0 | `cfg.MaxHearingRange` (25) | Per-creature override of hearing range. |
| `SoM_ConeHalf` | float | 0..180 degrees | `cfg.VisionConeHalfAngle` (60) | Vision cone half-angle. `180` = omnidirectional. |
| `SoM_IgnoreLoS` | int | 0/1 | 0 | Skip the line-of-sight raycast requirement for sight. (`SensingEvaluator.cs:16,37-39`.) |
| `SoM_NoFlee` | int | 0/1 | 0 | `FleeEvaluator` suppressed; `CurrentFleeReason` pinned to `None`. Removes gate 2 of `Humanoid_Attack_Patch.cs:51`. |
| `SoM_GiveUpMul` | float | > 0 | 1.0 | Multiplier on `SearchDuration` / `GiveUpTime`. |
| `SoM_Profile` | string | see §5.5 | none | Named bundle. **Sugar only. Never subtracts.** |
| `SoM_Ack` | int | — | — | **Written by SoM only.** Consumers read, never write. See §5.6. |

Namespace rule: `SoM_*` is reserved for SoM. Consumers must not invent keys in it. Anything unrecognised is reported, never guessed (§5.6.3).

### 5.3 The public static interop class

```csharp
namespace ShadowsOfMidgard
{
    // FROZEN. Do not rename, do not change semantics, do not add required parameters.
    // Third-party mods reflection-probe for this TYPE's existence to decide whether to
    // run their own fallback. Two known consumers as of 2026-07.
    public static class StealthExemption
    {
        public const string ZDOKey = "SoMStealthExempt";
        public static bool IsExempt(Character c);   // == legacy key is 1. NOT "is the brain off".
        public static bool IsExempt(BaseAI ai);
    }

    // Additive. Everything here is primitives or vanilla types - no SoM type ever crosses
    // the boundary, so a consumer needs no assembly reference and no type resolution
    // beyond this one class.
    public static class SoMInterop
    {
        public const int ContractVersion = 2;

        // Methods, not just consts: a reflected method call cannot be constant-folded,
        // cannot be read off a stale const in a consumer's own IL, and gives us one
        // obvious place to breakpoint.
        public static int      GetContractVersion();
        public static string[] SupportedKeys();       // exact ZDO key strings, this build
        public static string[] SupportedProfiles();   // exact profile strings, this build
        public static bool     Supports(string keyOrProfile);

        // The resolved question, as opposed to StealthExemption's legacy question.
        public static bool IsBrainOff(Character c);

        // THE PROBE. Returns a stable, parseable, human-readable line describing exactly
        // what SoM will do for this creature. HARD INVARIANT: this MUST be rendered from
        // the same ResolveDirectives(Character) struct the patches gate on. It must never
        // become a second implementation. (See MoA v0.0.9 lesson 2.)
        //   "som=2;src=zdo;brain=on;legacy_exempt=0;profile=hunter;alertfloor=2;
        //    detectfloor=0.50;sight=60.0;hear=60.0;cone=180.0;los=ignore;flee=off;giveup=1.0"
        public static string Describe(Character c);

        // Per-player, for the post-refactor world. Safe to call with a null Player
        // (falls back to the creature-level resolution) so it works on a dedicated
        // server where Player.m_localPlayer does not exist.
        public static string DescribeTrack(Character c, Player p);
    }
}
```

Why methods over a bare `const`: a `const int` read via `AccessTools.Field(...).GetValue(null)` does work, but it is the one member kind where a consumer that *did* take a compile-time reference would silently bake in an old value. `GetContractVersion()` cannot.

### 5.4 Precedence — a total order, no conditionals anywhere

Evaluated per creature, in this exact order:

1. **`SoM_Contract >= 2` present** → resolve Tier 2. `SoMStealthExempt` is **ignored for gating** and reported in `Describe` as `legacy_exempt=1 (superseded)`. SoM logs **INFO once per prefab**.
2. **Else `SoMStealthExempt == 1`** → brain off (v1 behaviour, frozen).
3. **Else** → global config defaults, brain on.
4. **Any `SoM_*` key present with no `SoM_Contract`** → **WARN once per prefab**, ignore all Tier-2 keys, fall through to rule 2/3.

Within rule 1: explicit Tier-2 keys **always beat** `SoM_Profile`; the profile only fills what was not explicitly set; config defaults fill the rest.

> **Why rule 1 inverts the obvious precedence.** Making the legacy flag win would force every v2 consumer to branch — *"is SoM new enough for me to drop the exempt stamp?"* — and that branch has exactly the failure mode MoA's post-mortem indicts. Under this ordering a consumer writes `SoMStealthExempt=1` **and** the Tier-2 keys, unconditionally, forever:
> - an **old** SoM sees `exempt=1`, stands aside → today's known-good behaviour, byte-identical;
> - a **new** SoM sees `SoM_Contract=2`, takes over and honours the directives → strictly better;
> - **no** SoM → both are inert ints.
>
> Zero conditions on the consumer side. Zero mod knowledge on SoM's side. This is the load-bearing decision of the whole design.

### 5.5 Profiles (v2 set)

| `SoM_Profile` | Expands to |
|---|---|
| `"default"` | nothing (config defaults) |
| `"vanilla"` | `BrainOff=1` |
| `"sentinel"` | `AlertFloor=2, DetectFloor=0.5, IgnoreLoS=1, NoFlee=1` — awake and fighting, does not roam |
| `"hunter"` | sentinel + `SightRange=60, HearRange=60, ConeHalf=180, GiveUpMul=99` |
| `"ambusher"` | `AlertFloor=0, DetectFloor=0, ConeHalf=30, SightRange=15, GiveUpMul=0.25` |
| `"oblivious"` | `DetectFloor=0, SightRange=1, HearRange=1` — present but effectively blind |

Profiles are documented as **hints that can only add**, so an unrecognised profile on an older SoM degrades to "whatever the explicit keys said" — which the consumer chose deliberately — rather than to a guess.

### 5.6 Fail-loud machinery (four independent channels)

**5.6.1 `SoM_Ack` write-back — the reflection-free verifier.**
When SoM resolves a creature that carries **at least one** interop key, and it owns the ZDO, it writes `SoM_Ack = ContractVersion` — once, guarded by a read-compare so it does not churn replication. For the ~100% of creatures carrying no keys, nothing is written and nothing is spent.

A consumer then verifies with **no reflection, no type probing, no Harmony**:

```csharp
int ack = zdo.GetInt("SoM_Ack", 0);
if (ack == 0)  { /* SoM never resolved this creature. Log [FAIL]. */ }
if (ack < 2)   { /* older SoM: my v2 directives are being ignored. Log [FAIL]. */ }
```

This is genuinely independent of anything the consumer patches — the property MoA's deleted alert probe lacked (`AvalorStealthCompat.cs:525-529`) and the property its surviving `IsExempt` check has. It is strictly stronger than `IsExempt`, because it proves SoM *reached and resolved* this creature, not merely that it can read a ZDO.

**5.6.2 Version mismatch.** `SoM_Contract > ContractVersion` → **ERROR, once per (prefab, claimed version)**, naming both versions and listing the present-but-unrecognised keys. `SoM_Ack` is still written with SoM's own lower version, so the consumer sees the downgrade in channel 1 too.

**5.6.3 Malformed input.** Unknown `SoM_Profile` string, out-of-range `SoM_AlertFloor`, negative range, `SoM_ConeHalf > 180` → **ERROR once per distinct bad value**, and that key alone is ignored. Never clamped-and-forgotten, never guessed.

**5.6.4 The dropped-key rule.** If a future SoM retires a Tier-2 key, it does **not** silently ignore it. `SupportedKeys()` stops listing it, and encountering it on a ZDO logs a **WARN once per prefab** naming the key and the version that dropped it. Retired keys stay in a documented `RetiredKeys` list so the message can say what replaced them.

**5.6.5 One-resolver invariant.** `ResolveDirectives(Character)` returns one struct; the patches gate on it; `Describe`/`DescribeTrack` render it. Enforce with a comment at the top of the resolver *and* with the only sanctioned consumer-side self-test being `Describe`, which by construction cannot be a parallel implementation.

**Performance:** `ResolveDirectives` caches per `Character`, invalidated on the ZDO's data-revision counter (soft-bound: if the member is renamed or absent — the v1.0-readiness clause — fall back to a 1-second re-read, never to a hard crash). Cold path is two hashed dictionary lookups (`SoM_Contract`, `SoMStealthExempt`); if both are default, the creature short-circuits to config defaults and nothing else is read. This is cheaper per creature than today's `StealthExemption.IsExempt` being called seven times per creature per tick from seven separate patch sites.

### 5.7 What MoA deletes, and what it writes instead

**Deleted outright:**

- `Compat\AvalorStealthGuards.cs` — **all five patch classes, the whole file**, including both `HarmonyBefore`/`HarmonyAfter` id constants (`:45-46`).
- `Core\MistsofAvalorPlugin.cs:190-211` — the five `ApplyPatch` registrations and their comment block.
- From `AvalorStealthCompat.cs`: `_getData`, `_fCurrentState`, `_fDetectionLevel`, `_fCanSense`, `_fCanSee`, `_fCanHear`, `_fFleeReason`, `_fAggression`, `_valAlerted`, `_valFleeNone` (`:136-139`); `ResolvePinningHandles` (`:245-267`); `Pin` (`:412-445`); `PinNow` (`:320-328`); `_canDriveSoM` (`:114`); `CompatMode` (`:102-110`) and every `Mode` branch; `_strikes` / `VerifyStrikesBeforeFallback` / `FallBackToPinning` (`:145,130,538-552`); **`_readingRealAlert` / `BeginRealAlertRead` / `EndRealAlertRead` / `SuppressAlertForce`** (`:290-296`) and their call sites at `AvalorMobDirector.cs:808-810` and `AvalorStealthCompat.cs:388-390`. The bracket exists only to undo MoA's own `IsAlerted` force; with the force gone the whole re-entrancy hazard evaporates.
- Every `AccessTools.TypeByName` / `AccessTools.Method` / `AccessTools.Field` / `Enum.Parse` call in the mod. **Reflection surface goes to zero.**

**Kept, and now purely vanilla-facing:** `AvalorHuntEnforcer` and `AvalorMobDirector.MakeItHunt`. These solve a problem SoM does not cause and cannot fix — vanilla alertness decays with no stealth mod installed (`IMPLEMENTATIONS/MistsofAvalor.md:253-259`). They keep the 60 m sense widening, `SetHuntPlayer`, `m_fleeIfNotAlerted=false` and the periodic re-assert. What changes is that `Alert()` can now be called plainly, with no bracket, because nothing forces `IsAlerted()` any more.

**Written at stamp time** — `MarkAsAvalorMob` (`AvalorMobDirector.cs:287-294`) and the `Character.Awake` claim (`MistsofAvalorPlugin.cs:300-301`), both owner-gated, unconditional, no SoM detection:

```csharp
zdo.Set("SoMStealthExempt", 1);   // legacy, kept forever - old SoM stands aside
zdo.Set("SoM_Contract",     2);   // new SoM takes over instead
zdo.Set("SoM_Profile", isWarden ? "sentinel" : "hunter");
```

The Warden's design exception — *alerted and fighting, but never hunting, so it cannot be lured off the prize chest* (`AvalorStealthCompat.cs:377-380`, `AvalorHuntEnforcer.cs:71-79`) — stops being an `if` in `ReassertVanillaHunt` and becomes a different profile string. Three branch sites collapse into one declarative value on the ZDO.

**`VerifyAll` slims to a reflection-free ack check** (`AvalorStealthCompat.cs:453-523` → ~15 lines): for every owned tracked mob, re-stamp missing keys (the genuine repair for the `Awake`-inside-`Instantiate` race is still needed and still correct), then read `SoM_Ack`. `ack >= 2` → healthy. `ack == 0` with SoM installed → `[FAIL] SoM never resolved N maze mob(s)`. `ack == 1` → `[FAIL] older SoM, directives ignored`. The strike counter can stay or go; it no longer gates anything.

**Optional, once, at first Update:** `SoMInterop.GetContractVersion()` by reflection, purely to write one honest log line. It gates **nothing** — which is the entire point.

### 5.8 What DvergrAllies does

Nothing. `DvergrGenetics.cs` is already correct under this contract and remains correct: it writes only Tier 1, never writes `SoM_Contract`, and therefore always resolves through precedence rule 2 or 3. Its explicit-`0`-for-non-allies discipline (`:15-16, :40`) and its `SetTamed`-parameter correctness (`:22-31, :124-133`) are unaffected. It gains, for free, the option of later reading `SoM_Ack` to verify — with no reflection and no new dependency.

### 5.9 Residual risks, stated plainly

- **ZDO string keys are hashed** (`GetStableHashCode`, `assembly_valheim.decompiled.cs:62464-62466`). Collisions across mods are unlikely but not impossible; the `SoM_` prefix plus documented reservation is the whole mitigation available.
- **`SoM_Ack` costs one extra ZDO int on flagged creatures only.** With MoA's ~7-mob population cap that is negligible; a mod that flags thousands of creatures should be told in the docs that Ack is opt-out via a config toggle on SoM's side.
- **The contract cannot restore vanilla alertness for players with no SoM installed.** Nothing SoM ships can. MoA's `AvalorHuntEnforcer` layer must survive, and the docs should say so explicitly so nobody deletes it expecting the contract to cover it.
- **`Describe` is a string.** Structured returns would need a shared type, which violates axiom 2. The format must therefore be treated as API: documented, versioned by `SoM_Contract`, keys never renamed, new fields appended only.

---

## KEY FILE PATHS

- `c:\WubarrkCODING\MistsofAvalor\Compat\AvalorStealthCompat.cs` (590 lines — the entire reflection surface)
- `c:\WubarrkCODING\MistsofAvalor\Compat\AvalorStealthGuards.cs` (153 lines — the entire Harmony-fighting layer)
- `c:\WubarrkCODING\MistsofAvalor\Core\MistsofAvalorPlugin.cs` (`:160,163` component wiring; `:190-211` guard registration; `:254-386` the `Character.Awake` postfix that stamps and claims)
- `c:\WubarrkCODING\MistsofAvalor\Mobs\AvalorMobDirector.cs` (`:282` key const, `:287-294` `MarkAsAvalorMob`, `:376` Warden key, `:745-828` `MakeItHunt`)
- `c:\WubarrkCODING\MistsofAvalor\Mobs\AvalorHuntEnforcer.cs` (108 lines — the vanilla-only layer that must survive)
- `c:\WubarrkCODING\DvergrAllies\DvergrGenetics.cs` (`:17` key const, `:21-41` `RefreshStealthExemption`, `:118-134` `SetTamed` postfix)
- `c:\WubarrkCODING\ShadowsOfMidgard\Systems\StealthExemption.cs` (21 lines — the frozen API)
- `c:\WubarrkCODING\ShadowsOfMidgard\Systems\UnifiedStealthTypes.cs` (`:59-106` `AwarenessData` — the reflection target)
- `c:\WubarrkCODING\ShadowsOfMidgard\Systems\AwarenessSystem.cs` (`:11-20` `GetData` — the reflection entry point)
- `c:\WubarrkCODING\libs-Tools\IMPLEMENTATIONS\MistsofAvalor.md:238-276` (section 11 + the three v0.0.9 lessons)
- `c:\WubarrkCODING\libs-Tools\IMPLEMENTATIONS\DvergrAllies.md:160-190` (genetics / cross-mod signal)