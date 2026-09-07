# SoM v2 — Synthesised implementation plan

**Written 2026-07-31**, closing the design phase. Inputs: three competing architectures
(`DESIGN-pragmatic.md`, `DESIGN-throughput.md`, `DESIGN-resilient.md`), two adversarial reviews of the latter
two, and `REVIEW-verified-facts.md` — which is the tiebreaker wherever a design and this plan disagree.

---

## 0. Verdict

**Base: `DESIGN-pragmatic.md`.** Graft specific, named subsystems from the other two. Reasons, in order:

1. **The throughput design's advantage is unproven.** Its entire headline rests on an `18 µs` per-linecast
   constant *borrowed unchanged from the pragmatic design and never measured*. A short 7-layer linecast in
   Unity 2022 is typically 1–5 µs. At 3 µs the pragmatic design costs ~0.7 % of a frame and the gap largely
   closes. See WP-0.
2. **Both of the throughput design's differentiator subsystems have fatal defects** — the replication blob
   encodes `ZDOID.UserKey`, a process-local static list index, into a *saved* ZDO; and the deferred LOS batch
   carries raw track slot indices across a frame boundary that its own SoA compaction invalidates.
3. **Its riskiest feature lost its justification.** The creature-vs-creature early-out was sold on `FindEnemy`
   being O(N·C); `CanSenseTarget` is only reached *after* `IsEnemy`, and same-faction creatures reject there.
4. **The resilient design has the single best idea in the panel** (`long` playerID keying, §3 of the facts file)
   and a fatal bug of its own (`Wheel.Serve` appends to the list it iterates → unbounded loop).
5. Pragmatic ports the five evaluators verbatim. Both reviewers independently flagged evaluator-parity as a top
   risk of any rewrite; the base architecture should be the one that does not rewrite them.

**This is a provisional verdict on the scheduler only.** WP-0 can overturn it. Everything in WP-1..WP-4 is
identical under either outcome, so start there and let the measurement land in parallel.

### What is grafted

| From | What | Why |
|---|---|---|
| resilient | **`long` playerID as the sole player identity**, in memory and on the wire | Verified decisive. Facts §1.6, §2.1, §3 |
| resilient | **Policy triple-probe** replacing the `DataRevision` cache | Facts §1.7 — the cache never hits for a moving creature |
| resilient | **Capability model + declared degradation**, trimmed to the bindings actually used | Goal #1. Trimmed because a 31-row prose-verified table is itself a risk |
| resilient | **`SoM_Ack` contract acknowledgement** | Lets a consumer verify SoM reached a creature without reflection |
| throughput | **Single `byte[]` ZDO digest** instead of N scalar fields | `ZDO.Set(int, ZDOID)` does not exist (facts §2.1); one field = one revision bump |
| throughput | **Deferred batched LOS**, config-gated, **off by default** | Real mechanism (compile-verified) but only pays if WP-0 says so |

### What is rejected

- **`Compat/ExternalPin.cs`** (pragmatic §2.8). Nothing writes into `AwarenessData` any more (facts §2.4).
  `AwarenessData` stays frozen, public, v1 field names, and is *published to*; external writes are neither
  honoured nor fought.
- **SoA / row+generation handles** (throughput §2). Not justified until WP-0 says the budget is tight.
- **The `GetInstanceID()` direct-mapped probe** (throughput). A `Dictionary` is adequate at verified volumes.
- **The creature-vs-creature early-out** (throughput §5.4). Justification collapsed; it defeats other mods.
- **The timing wheel** (resilient §5.2). Fatal as written, and its benefit over a cursor is unproven.

---

## 1. Cross-cutting rules — every work package obeys these

These come from `REVIEW-verified-facts.md`. Violating one is a review failure regardless of which WP.

1. **Never skip the LOS cast because a track is Alerted.** `alerted` gates *only* the FOV cone (L4608); the
   view-block raycast (L4614) is unconditional. Skipping it latches the creature into seeing through walls
   forever. Alerted tracks may use a *longer* recheck period. Only an explicit `SoM_IgnoreLoS` directive skips.
   (Facts §2.5 — this defect is in two of the three designs.)
2. **Sample the mist gate.** `ParticleMist.IsMistBlocked` (L4618) must be evaluated alongside the view-block
   cast, honouring `BaseAI.m_mistVision`. It may only contribute "blocked", never "clear". (Facts §2.6 — omitted
   by all three designs.)
3. **The three sense patches are an atomic install group.** `CanSenseTarget` reaches the *static* helpers, not
   the instance ones. Install all three or none. (Facts §2.7.)
4. **`ZDOID` is never a player identity** — not in a track, not in a decision, not on the wire, not in a save.
   Use `long` playerID. (Facts §1.6, §2.1, §3.)
5. **Never key a cache on `ZDO.DataRevision`.** (Facts §1.7.)
6. **Cost models book the instance sense methods at 20 Hz per target-holding creature**, not at the 2 s/6 s
   `FindEnemy` cadence. (Facts §2.8.)
7. **Every loop that can `continue` past its only yield must yield unconditionally at the bottom.** Three
   separate instances of this bug class have now been found — v1's `UpdateNearbyAlliesCoroutine` (fixed
   2026-07-31), and `Wheel.Serve` in the resilient design. Applies equally to `while` loops that append to the
   collection they iterate.
8. **The failure floor is vanilla.** No default, no fallback, no missing binding may produce *more* perception
   than vanilla. Specifically: an unreadable stealth factor defaults to `0f` (vanilla's ZDO default, meaning
   *invisible*), never `1f`.
9. **"No opinion" ⇒ return `true`** and let the original run. There is no path where SoM skips the original and
   writes a value it is not confident about.
10. **Never hand out a shared mutable `_scratch`.** If a compat call cannot be answered, return a freshly
    defaulted instance.

---

## 2. Work packages

Ordered. Files are disjoint between packages so WPs at the same depth can proceed independently.

### WP-0 — Measure before choosing (blocks the scheduler decision only)

**Goal.** Replace the one assumed constant that the architecture choice depends on.

**Do.** In a loaded world with a populated area, `Stopwatch` around 10 000 `Physics.Linecast` calls at ~40 m
against `BaseAI.m_viewBlockMask`, warm. Record median ns. Then the same for `Physics.Raycast` with the
crouch/eye endpoints actually used. Report a single number.

**Decides.**
- `≥ 10 µs` → LOS dominates; adopt the throughput scheduler's *deferred batch* (WP-6b) as the default and
  revisit SoA.
- `3–10 µs` → keep the pragmatic cursor; ship deferred batching as an opt-in.
- `< 3 µs` → LOS is not the bottleneck; drop batching entirely and delete WP-6b.

**Note.** Requires the game running, so this is a human task, not a build task.

---

### WP-1 — Build hygiene *(no dependencies; do first, it is minutes)*

**Files.** `ShadowsOfMidgard.csproj`, `version.txt`.

- Set `version.txt` to `2.0.0` by hand (the tick only touches the patch component).
- Confirm exactly one `assembly_valheim` fusion identity is referenced — currently correct, keep the comment.
- Make `ShadowsOfMidgard.ModVersion = SoMBuild.Version` so `[BepInPlugin]`, the assembly and the ServerSync
  handshake cannot disagree.
- `MinimumRequiredVersion` is **hand-edited only** — never derived from `version.txt`, or every rebuild kicks
  every client.

**Acceptance.** `dotnet msbuild -t:Compile` clean at 0 warnings; assembly reports `2.0.0.0`.

---

### WP-2 — Frozen contract surface *(depends: WP-1)*

**Files.** `Api/StealthExemption.cs`, `Api/SoMKeys.cs`, `Compat/LegacyAwareness.cs`.

- `StealthExemption` verbatim: type name, `ZDOKey = "SoMStealthExempt"`, both `IsExempt` overloads. Cache the
  `ZNetView` on the creature record rather than `GetComponent` per call — the reviewer measured that as the real
  cost (~150 ns), not the string hash (~20 ns).
- `AwarenessData`: **public, not sealed**, all v1 field names *and types*, v1 order. Add `AggressionLevel`.
- `AwarenessSystem`: keep **every** v1 public static — `GetData(Character)`, `GetAllTrackedCharacters()`,
  `Clear`, `ClearAll`, `GetAllData()`, and **`HasLineOfSight(Character, Player)`**. The last two were dropped by
  a design without being scored; both are public v1 API.
- `GetData` never returns null and never throws, including before the registry exists, on null input, and for a
  creature SoM has never seen. Rule 10 applies.
- **Populate every field** the projection can supply, not the convenient subset. A partly-populated frozen type
  is a broken contract that still compiles.

**Acceptance.** A reflection probe mimicking MoA 0.0.9 resolves the type, both overloads, and every field, and
reads coherent values. `GetAllData()` is non-empty once creatures are tracked.

---

### WP-3 — Core state and registry *(depends: WP-2)*

**Files.** `Core/Track.cs`, `Core/TrackTable.cs`, `Core/CreatureState.cs`, `Core/CreatureRegistry.cs`,
`Core/PlayerProfile.cs`, `Core/PlayerRegistry.cs`, `Core/Pools.cs`.

- `Track.PlayerId` is a **`long`**. Track capacity = the player cap, not a hardcoded 8 — eviction by
  *lowest detection* evicts the best-hidden player, i.e. exactly the one the mod exists to serve. If a cap is
  needed, evict by staleness.
- `CreatureState` caches `Character`/`BaseAI`/`MonsterAI`/`ZNetView`/`Humanoid`/`Transform` once.
- `PlayerRegistry` resolves identity via `Player.GetPlayerID()` (ZDO-backed, remote-readable). Roster from
  `Player.GetAllPlayers()` is acceptable **for creatures we own** — document the ~2.5 s handover window where an
  owned creature may be near a player not in the local list.
- Registry lifetime is driven by a periodic sweep over `BaseAI.BaseAIInstances`, with `Awake`/`OnDestroy`
  patches as a latency optimisation only, never as the authority.

**Acceptance.** A player dies and respawns; the creature's track survives (its `long` id is unchanged) — the
exact case that ZDOID keying breaks.

---

### WP-4 — Sensing *(depends: WP-3)*

**Files.** `Sensing/SensingMath.cs`, `Sensing/VisibilitySystem.cs`, `Sensing/NoiseSystem.cs`,
`Sensing/HidingSystem.cs`, `Sensing/CamoSystem.cs`, `Sensing/VegetationSampler.cs`, `Bind/LayerMasks.cs`.

- Port the four v1 formulas **term for term** into `PlayerProfile`. Known quirks stay, documented, and are fixed
  in 2.1 — one behavioural variable at a time.
- LOS: cross-cutting rules 1 and 2. Cast for alerted tracks at a longer period; sample mist; honour
  `m_mistVision`.
- Endpoints must match vanilla exactly: `target.IsCrouching() ? target.GetCenterPoint() : target.m_eye.position`
  (L4612). A mismatch makes SoM and vanilla disagree about identical geometry.
- `LayerMasks.ViewBlock` resolves **lazily on first use**, not in plugin `Awake` — `BaseAI.m_viewBlockMask` is
  assigned in the first `BaseAI.Awake`, so an eager read is deterministically `0` and silently falls to the
  brittle hardcoded rung. Validate by comparing the two derivations and refusing on *disagreement*, not only on
  zero.
- Unreadable stealth factor ⇒ `0f` (rule 8).

**Acceptance.** A crouching player behind a closed door is not seen by an **Alerted** creature. A Mistlands
creature without `m_mistVision` does not see through mist. Both fail on the designs as written.

---

### WP-5 — Brain *(depends: WP-4)*

**Files.** `Brain/Decision.cs`, `Brain/StealthBrain.cs`, `Brain/StateEvaluator.cs`, `Brain/FleeEvaluator.cs`,
`Brain/MovementEvaluator.cs`, `Brain/CombatEvaluator.cs`, `Brain/GroupEvaluator.cs`.

- Five evaluators ported verbatim; only their *inputs* change to `(ref Track, in PlayerProfile, in Directives,
  CreatureState)`.
- **The flee roll must be rate-corrected.** v1 re-rolls `Lerp(0.7,0,health) > Random.value` every evaluation, so
  any tiered scheduler changes flee probability purely by tier. Roll against `1 - Pow(1 - p, dt)`, or once per
  state entry. This is a real behaviour change introduced by *any* tiering design, including the base.
- Preserve `SensingConfidence`; it feeds a frozen field.

**Acceptance.** A parity harness replays recorded `(detection, dt)` sequences through v1 and v2 evaluators and
asserts identical state ladders. Both reviewers named evaluator parity a top risk — this harness is the answer.

---

### WP-6a — Scheduler, cursor form *(depends: WP-5)*

**Files.** `Core/Scheduler.cs`, `Core/Budget.cs`, `ShadowsOfMidgard.cs` (the single `Update`).

- One `MonoBehaviour.Update`. **No coroutines** — `CoroutineManager` and `StealthOvermind` are deleted, and rule
  7's bug class goes with them.
- Time-budgeted cursor, tiers by distance to the **nearest tracked player**, never `Player.m_localPlayer`.
- **The budget must not be decorative.** When exhausted, SoM must answer from its last trustworthy bit with a
  decay-toward-`false` policy — *not* fall through to vanilla, because vanilla then performs the identical
  raycast at 20 Hz, uncapped, in the same frame (facts §2.8). Falling through is correct only on a *capability*
  failure.

**Acceptance.** 200 creatures, 10 players: measured full-sweep period within 2× the near-tier period, and a
logged warning when it is not. Zero steady-state allocation.

---

### WP-6b — Deferred batched LOS *(depends: WP-6a and WP-0; may be deleted)*

**Files.** `Sensing/LosBatch.cs`.

Only if WP-0 justifies it. `Allocator.Persistent`; `Complete()` before any `Dispose()`; drain on teardown.
**Carry `(creature handle, stable player id)` per queued cast, never a raw slot index** — track compaction
invalidates indices between schedule and consume, which is the throughput design's second fatal bug. Document
the added staleness (≥1 frame plus the epoch) and accept it only for the non-first-detection path.

---

### WP-7 — Patch layer *(depends: WP-5; parallel with WP-6)*

**Files.** `Patches/*.cs`, `Bind/PatchInstaller.cs`, `Bind/VanillaBind.cs`, `Act/AlertDriver.cs`,
`Act/BehaviorSystem.cs`, `Act/AllyCall.cs`.

- **Delete the `IsAlerted` patch.** Drive the real `Alert()` / `SetAlerted`, debounced. This single deletion
  repairs alert replication, the animator bool, aggro roars, boss counting, give-up/leash, taming, Sneak XP, the
  backstab exploit and cross-client `EnemyHud` agreement. Best-evidenced conclusion in the whole panel.
- **Delete both damage patches**; subscribe to `Character.m_onDamaged` from a `BaseAI.Awake` postfix.
- Sense patches: prefix-only, low priority, honour `__runOriginal`, atomic install group (rule 3).
- Explicit argument-type arrays on every target. Inject `__instance` only — never a named parameter whose name
  differs between `Character.StartAttack(…, bool charge)` and `Humanoid.StartAttack(…, bool secondaryAttack)`.
- No `PatchAll`. Per-class probe + `CreateClassProcessor`, with the atomic group honoured.
- Every patch body is `try`/`catch`-wrapped — an exception escaping into `FindEnemy` is unrecoverable.

**Acceptance.** With SoM inert (`Runtime.Active = false`), the game is byte-for-byte vanilla.

---

### WP-8 — Replication and contract *(depends: WP-3, WP-7)*

**Files.** `Net/TrackDigest.cs`, `Api/SoMInterop.cs`, `Api/Directives.cs`, `Api/ProfileTable.cs`.

- One `byte[]` digest per creature. **Encode `long` playerID** — never `ZDOID`, never `UserKey`. Version byte;
  validate total length against the declared slot count *before* indexing; clamp the count.
- Owner-gated, idempotent, absolute assignment. Gate on "detection changed by ≥ Δ", not on any bit changing —
  a stationary creature's ZDO has no other revision source, so a chatty digest forces full-ZDO resends vanilla
  would never make (`ZDO.Serialize` sends the whole field set, every time).
- `SoM_Ack` written owner-side only. Split the pure resolver from the acknowledging writer.
- Tiered directive keys with a total precedence order and a single resolver — `Describe()` renders the resolver's
  output, never a second implementation.

**Acceptance.** Kill ownership mid-chase; the new owner resumes with the same target and detection. A non-SoM
peer is unaffected. DvergrAllies and MoA 0.1.2 both work unchanged.

---

### WP-9 — Config / ServerSync *(depends: WP-1; parallel with everything)*

**Files.** `Config/SOMConfig.cs`, `Config/Binder.cs`, `Config/StealthConfigModel.cs`.

- `AddLockingConfigEntry` **before** any synced entry. Its absence in v1 left `IsLocked` permanently false, the
  admin gate dead, and *any* client able to broadcast its own values server-wide.
- `ModRequired = true`.
- One `Binder` call per entry: bind + sync + clamp + initial push + change push. Min/max written once.
- Section names, key names and defaults unchanged so existing TOMLs keep loading.

**Acceptance.** A non-admin client's edit does not propagate. A vanilla client cannot join.

---

### WP-10 — UI *(depends: WP-6a)*

**Files.** `UI/*.cs`.

- Lazy canvas on first `Player.m_localPlayer`, parented under vanilla `Hud`. **Never create an `EventSystem`** —
  v1 did, during the chainloader, permanently disabling Valheim's own.
- HUD reads **the local player's own track**, never the aggregate.
- Write `color`/`localScale`/`sizeDelta` only on change.
- Embedded PNG only; no disk path, no disk write.

---

### WP-11 — Docs *(last)*

README + internal changelog v1.0.0 → v1.9.0 to the TortalPortal/Fatty standard, plus migration notes for
MistsofAvalor and DvergrAllies once the contract is final.

---

## 3. Acceptance test for the whole redesign

Unchanged from the pragmatic design, plus two rows the panel added:

> Player A crouches two metres from three greydwarves actively fighting player B.
>
> - A's gem is blue, small and quiet; v1 reports `Engaged`.
> - The same creature answers `CanSeeTarget(A) == false` and `CanSeeTarget(B) == true` in the same frame.
> - `FindEnemy` continues to select B, not A, though A is closer.
> - A's backstab lands at full multiplier; B's does not.
> - Both clients' `EnemyHud` show the same alert icon.
> - A gains Sneak XP; B does not.
> - **A alerts a greydwarf, retreats indoors and shuts the door: the greydwarf loses him.** (Rule 1.)
> - **A dies and respawns: the greydwarf's memory of A is intact, not orphaned.** (Rule 4.)

---

## 4. Open items

1. **WP-0's measurement.** Human task; blocks only the scheduler choice.
2. **`GetStableHashCode` collisions.** SoM's ZDO keys share a 32-bit space with ~700 `ZDOVars.s_*`. Add a
   startup assertion that no SoM key collides with a known vanilla key.
3. **`AnimalAI`.** Out of scope for 2.0, behind a default-off synced flag. State it in the README rather than
   leaving it ambiguous.
