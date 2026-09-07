# SoM `Patches/` + `ShadowsOfMidgard.cs` — Exhaustive Surveyor Report

Files surveyed (absolute paths):
- `c:\WubarrkCODING\ShadowsOfMidgard\ShadowsOfMidgard.cs`
- `c:\WubarrkCODING\ShadowsOfMidgard\Patches\BaseAI_CanHearTarget_Patch.cs`
- `c:\WubarrkCODING\ShadowsOfMidgard\Patches\BaseAI_CanSeeTarget_Patch.cs`
- `c:\WubarrkCODING\ShadowsOfMidgard\Patches\BaseAI_SetMoveDir_Patch.cs`
- `c:\WubarrkCODING\ShadowsOfMidgard\Patches\BaseAI_StealthBrain_IsAlerted_Patch.cs`
- `c:\WubarrkCODING\ShadowsOfMidgard\Patches\BaseAI_StealthBrain_Patch.cs`
- `c:\WubarrkCODING\ShadowsOfMidgard\Patches\Character_Damage_Patch.cs`
- `c:\WubarrkCODING\ShadowsOfMidgard\Patches\Character_OnDamaged_Patch.cs`
- `c:\WubarrkCODING\ShadowsOfMidgard\Patches\Character_OnDestroy_Patch.cs`
- `c:\WubarrkCODING\ShadowsOfMidgard\Patches\Humanoid_Attack_Patch.cs`
- `c:\WubarrkCODING\ShadowsOfMidgard\Patches\MonsterAI_StealthBrain_UpdateAI_Patch.cs`

Supporting reads (needed to judge patch correctness): `Systems\AIAuthority.cs`, `Systems\StealthExemption.cs`, `Systems\AwarenessSystem.cs`, `Systems\UnifiedStealthTypes.cs`, `Systems\StealthBrain.cs`, `Systems\StealthOvermind.cs`, `Systems\BehaviorSystem.cs`, `Systems\StealthBrain\{Sensing,State,Movement,Combat}Evaluator.cs`, `Config\SOMConfig.cs`, `MistsofAvalor\Compat\AvalorStealthGuards.cs`.

Vanilla ground truth quoted from `c:\WubarrkCODING\libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs` (class starts: `AnimalAI` 3714, `BaseAI` 3809, `MonsterAI` 5620, `NpcTalk` 6549, `Character` 6814, `Humanoid` 12806, `Player` 15320).

---

## 0. PLUGIN ENTRY POINT — `ShadowsOfMidgard.cs`

```csharp
[BepInPlugin(ModGUID, ModName, ModVersion)]           // :8
public const string ModGUID    = "wubarrk.shadowsofmidgard";   // :11
public const string ModVersion = "2.0.0";                      // :13
```
`Awake()` order (`:23-53`), all inside one `try/catch(Exception)` that only logs:
1. `SOMConfig.Init(Config)` — this is where ServerSync's `configSync.AddConfigEntry(...)` runs (`Config\SOMConfig.cs:92,165,169,173`).
2. `StealthUIRoot.Init(); StealthUIController.Init();`
3. `CoroutineManager.Bootstrap();` — brings up the host that runs `StealthOvermind.EvaluateLoop`.
4. `_harmony = new Harmony(ModGUID); _harmony.PatchAll(Assembly.GetExecutingAssembly());`

Notes that matter for the overhaul:
- **Harmony id is `"wubarrk.shadowsofmidgard"`** — load-bearing, MistsofAvalor hard-codes it in `HarmonyBefore`/`HarmonyAfter` (`AvalorStealthGuards.cs:45-46`, which also lists the alternate casing `"wubarrk.ShadowsOfMidgard"`).
- `PatchAll` is **all-or-nothing**. One unresolvable `[HarmonyPatch]` target (renamed method in Valheim v1.0) throws out of `PatchAll`, the `catch` at `:50` swallows it, and **every** patch after the failure point is silently absent while the plugin still reports "loaded successfully" at `:48`. There is no per-patch guarding and no `Harmony.CreateAndPatchAll` per-class fallback. This directly violates overhaul brief item 1.
- UI is initialised **unconditionally on both client and server processes** (`:29-30`) before the Harmony block.
- `OnDestroy` (`:56-67`) calls `_harmony?.UnpatchSelf()` but does **not** call `AwarenessSystem.ClearAll()` or `StealthBrain.ClearAllCaches()` — the static dictionaries survive a Harmony unpatch/re-patch cycle.

---

## 1. PATCH INVENTORY

### 1.1 `BaseAI_StealthBrain_Patch` — `BaseAI.CanSenseTarget(Character)`
File: `Patches\BaseAI_StealthBrain_Patch.cs`

| | |
|---|---|
| Target | `[HarmonyPatch(typeof(BaseAI), "CanSenseTarget", new Type[] { typeof(Character) })]` (`:7`) |
| Prefix | `bool Prefix(BaseAI __instance, Character target, ref bool __result)` (`:11`), `[HarmonyPriority(int.MaxValue)]` (`:10`) |
| Postfix | `void Postfix(BaseAI __instance, Character target, ref bool __result)` (`:48`), `[HarmonyPriority(int.MinValue)]` (`:47`) |
| Skips vanilla | **Yes**, both exit paths `return false` (`:39`, `:44`) |

Prefix guard sequence, in order (`:14-34`):
1. `!AIAuthority.IsAuthoritative(__instance)` → `return true`
2. `target == null || !target.IsPlayer()` → `return true`
3. `__instance as MonsterAI == null` → `return true`
4. `aiChar = __instance.GetComponent<Character>(); aiChar == null || aiChar.IsTamed()` → `return true`
5. `StealthExemption.IsExempt(aiChar)` → `return true`
6. `AwarenessSystem.GetData(aiChar) == null` → `return true` — **dead branch, see §3.1**

Writes:
```csharp
if (data.CurrentState >= VanillaAlertness.Alerted) { __result = true;  return false; }  // :36-40
__result = data.CanSense;                                             return false;    // :43-44
```
Postfix repeats guards 1-5 verbatim and re-asserts the identical `__result` (`:51-69`). **`target` is accepted as a parameter and never read.**

### 1.2 `BaseAI_CanSeeTarget_Patch` — `BaseAI.CanSeeTarget(Character)`
File: `Patches\BaseAI_CanSeeTarget_Patch.cs`

Target `[HarmonyPatch(typeof(BaseAI), "CanSeeTarget", new Type[] { typeof(Character) })]` (`:12`). Prefix `[HarmonyPriority(int.MaxValue)]` (`:15`), Postfix `[HarmonyPriority(int.MinValue)]` (`:41`). **Returns false** at `:38`.

Guard sequence identical to §1.1 guards 1-6 (`:18-35`). Single write:
```csharp
__result = data.CanSee;   // :37
return false;             // :38
```
Postfix same guards, `__result = data.CanSee;` (`:57`). `target` never read.

### 1.3 `BaseAI_CanHearTarget_Patch` — `BaseAI.CanHearTarget(Character)`
File: `Patches\BaseAI_CanHearTarget_Patch.cs`

Target `[HarmonyPatch(typeof(BaseAI), "CanHearTarget", new Type[] { typeof(Character) })]` (`:12`). Prefix `[HarmonyPriority(int.MaxValue)]` (`:15`), Postfix `[HarmonyPriority(int.MinValue)]` (`:41`). **Returns false** at `:38`. Guards identical. Writes `__result = data.CanHear;` (`:37`, `:57`). `target` never read.

### 1.4 `BaseAI_StealthBrain_IsAlerted_Patch` — `BaseAI.IsAlerted()`
File: `Patches\BaseAI_StealthBrain_IsAlerted_Patch.cs`

Target `[HarmonyPatch(typeof(BaseAI), "IsAlerted")]` (`:6`). Prefix `[HarmonyPriority(int.MaxValue)]` (`:9`), Postfix `[HarmonyPriority(int.MinValue)]` (`:28`). **Returns false** at `:25`.

Guards (`:12-21`): `!IsAuthoritative` → `!(__instance is MonsterAI)` → `c = GetComponent<Character>(); c == null || c.IsDead() || c.IsTamed()` → `StealthExemption.IsExempt(c)` → `data == null`. Note this one adds `IsDead()` which the sensing patches omit.

Write:
```csharp
__result = data.CurrentState == VanillaAlertness.Alerted ||
           data.CurrentState == VanillaAlertness.Engaged;   // :23-24 and :42-43
```
This is the single most invasive patch in the mod — see §4 for the full list of vanilla behaviours it hijacks.

### 1.5 `BaseAI_SetMoveDir_Patch` — `Character.SetMoveDir(Vector3)`
File: `Patches\BaseAI_SetMoveDir_Patch.cs` (**class name lies — it patches `Character`, not `BaseAI`**)

Target `[HarmonyPatch(typeof(Character), "SetMoveDir")]` (`:6`). Prefix only, `[HarmonyPriority(int.MinValue)]` (`:18`) — the only patch in the mod that deliberately runs *last* among prefixes. Signature `bool Prefix(Character __instance, ref Vector3 dir)` (`:19`). **Returns `true` always** — it mutates the by-ref argument instead of skipping.

Guards (`:21-35`):
1. `__instance == null || __instance is Player` → true
2. `ai = GetComponent<BaseAI>(); ai == null || !AIAuthority.IsAuthoritative(ai) || __instance.IsDead()` → true
3. `__instance.IsTamed()` → true
4. `StealthExemption.IsExempt(ai)` → true
5. `data == null` → true (dead)
6. `data.CurrentAction != Search && != Investigate` → true
7. `dir.magnitude <= 0.001f` → true

Write (`:37-49`):
```csharp
float factor = cfg != null ? cfg.SearchSpeedFactor : 0.5f;
float scaled = magnitude * factor;
if (magnitude >= 0.1f) scaled = Mathf.Max(scaled, 0.1f);
dir = dir / magnitude * scaled;
```

### 1.6 `Humanoid_Attack_Patch` — `Humanoid.StartAttack(Character, bool)`
File: `Patches\Humanoid_Attack_Patch.cs`

Target `[HarmonyPatch(typeof(Humanoid), "StartAttack")]` (`:7`) — **no argument-type array**. Prefix only, `bool Prefix(Humanoid __instance)` (`:16`), **no `[HarmonyPriority]`** → default `Priority.Normal` (400). This is the only sensing/combat patch not at an extreme priority.

Guard/gate sequence (`:18-84`):
1. `__instance is Player` → `return true`
2. `ai = GetComponent<BaseAI>(); ai == null` → `return true`
3. `!AIAuthority.IsAuthoritative(ai)` → `return true`
4. `c = __instance as Character; c == null || c.IsDead() || c.IsTamed()` → `return true`
5. `StealthExemption.IsExempt(c)` → `return true`
6. `data == null` → `return true` (dead)
7. **GATE 1** `data.CurrentState < VanillaAlertness.Alerted` → `LogCombatBlock` + **`return false`** (`:43-47`)
8. **GATE 2** `data.CurrentFleeReason != FleeReason.None` → **`return false`** (`:51-55`)
9. **GATE 3a** `monster = ai as MonsterAI ?? GetComponent<MonsterAI>(); monster == null` → **`return false`** (`:58-63`) — note: a non-MonsterAI Humanoid that got past guard 2 is *blocked outright*
10. `target = monster.GetTargetCreature(); p = target as Player; p == null || p.IsDead()` → `return true` (`:66-69`)
11. `if (!StealthBrain.TryGetCachedDecision(monster, out decision)) decision = StealthBrain.Evaluate(monster, p, Time.deltaTime);` (`:73-74`)
12. `!decision.Combat.ShouldAttack` → **`return false`** (`:76-80`)
13. else `LogCombatAllowed` + `return true`

Skips vanilla: **yes**, four separate `return false` paths. When skipped, `Humanoid.StartAttack` yields `default(bool) == false`, which propagates to `MonsterAI.DoAttack` (vanilla `:6343-6348`) so `m_timeSinceAttacking` is *not* reset — vanilla's 60 s "can't reach target, give up" timer keeps running.

### 1.7 `MonsterAI_StealthBrain_UpdateAI_Patch` — `MonsterAI.UpdateAI(float)`
File: `Patches\MonsterAI_StealthBrain_UpdateAI_Patch.cs`

Target `[HarmonyPatch(typeof(MonsterAI), "UpdateAI")]` (`:6`). Postfix only, `void Postfix(MonsterAI __instance, float dt)` (`:14`), `[HarmonyPriority(int.MinValue)]` (`:13`). Cannot skip vanilla (postfix). **`dt` is accepted and never used** — `BehaviorSystem` is handed `Time.deltaTime` instead (`BehaviorSystem.cs:284`).

Guards (`:16-27`):
1. `!AIAuthority.IsAuthoritative(__instance)` → return
2. `c = GetComponent<Character>(); c == null || c.IsDead() || c.IsTamed()` → return
3. `StealthExemption.IsExempt(__instance)` → return
4. **`Player player = Player.m_localPlayer; if (player == null || player.IsDead()) return;`** (`:25-27`)
5. `if (!StealthBrain.TryGetCachedDecision(__instance, out decision)) return;` (`:35-36`)

Writes/state (`:38-42`), wrapped in a bare `try { } catch { }` (`:29`,`:44`) that swallows everything:
```csharp
BehaviorSystem.ExecuteAction(__instance, decision.RecommendedAction, player, decision);
AwarenessData data = AwarenessSystem.GetData(c);
if (data != null) StealthDebugger.LogUpdateAI(c, data);
```
`ExecuteAction` is what actually mutates vanilla: `Pursue`/`Attack` → `SetTarget(ai, target)` which reflection-writes `BaseAI.m_targetCreature` and calls `BaseAI.SetTargetInfo(ZDOID)` (`BehaviorSystem.cs:141-170, 263-275`); `Idle` → `ResetAI` clears `m_targetCreature` (`:221-233`); `Search`/`Investigate` → `MoveToPathed` → reflected `BaseAI.MoveTo(dt, point, dist, run:true)` (`:82-105`).

**No `IsSleeping()` guard.** Vanilla returns early at `:5961-5964` when sleeping; SoM's postfix still runs and can re-drive a sleeping creature from a decision cached up to `MAX_DECISION_AGE_FRAMES = 60` (`StealthBrain.cs:166`).

### 1.8 `Character_Damage_Patch` — `Character.Damage(HitData)`
File: `Patches\Character_Damage_Patch.cs`

Target `[HarmonyPatch(typeof(Character), "Damage")]` (`:6`). Postfix `void Postfix(Character __instance, HitData hit)` (`:11`), **no `[HarmonyPriority]`**. Cannot skip vanilla.

Guards (`:13-27`): `null || IsDead()` → `is Player` → `ai == null || !IsAuthoritative(ai)` → `data == null`. **No `IsTamed()` guard and no `StealthExemption.IsExempt` guard** — this is the only awareness-writing patch that ignores the exemption key entirely.

Writes:
```csharp
data.CurrentState  = VanillaAlertness.Alerted;                       // :30
data.DetectionLevel = Mathf.Max(data.DetectionLevel, 0.50f);         // :34  hard-coded
if (hit?.GetAttacker() != null) data.LastKnownPosition = hit.GetAttacker().transform.position;  // :37-40
data.TimeSinceSeen = 0f;                                             // :43
```
`0.50f` is a magic number; the configured `AlertedThreshold` default is `0.40f` and `EngagedThreshold` is `0.75f` (`Config\SOMConfig.cs:169,173`). Also note `data.CurrentState = Alerted` is an **unconditional downgrade** — a creature already `Engaged` is demoted to `Alerted` by being hit.

### 1.9 `CharacterOnDamagedPatch` — `Character.OnDamaged(HitData)`
File: `Patches\Character_OnDamaged_Patch.cs` (class is `CharacterOnDamagedPatch`, no underscores — inconsistent with every sibling)

Target `[HarmonyPatch(typeof(Character), "OnDamaged")]` (`:10`). Postfix `void Postfix(Character __instance, HitData hit)` (`:15`), no priority. Guards identical to §1.8 (`:17-30`) — again **no tamed guard, no exemption guard**.

Writes (`:35-48`):
```csharp
data.DetectionLevel = Mathf.Max(data.DetectionLevel, 0.80f);   // hard-coded
if (data.DetectionLevel >= 0.75f) data.CurrentState = VanillaAlertness.Engaged;
else if (data.CurrentState < VanillaAlertness.Alerted) data.CurrentState = VanillaAlertness.Alerted;
data.TimeSinceSeen = 0f;
```
`0.80f` and the literal `0.75f` duplicate `EngagedThreshold` rather than reading `SOMConfig.Active.EngagedThreshold`.

### 1.10 `Character_OnDestroy_Patch` — `Character.OnDestroy()`
File: `Patches\Character_OnDestroy_Patch.cs`

Target `[HarmonyPatch(typeof(Character), "OnDestroy")]` (`:5`). Prefix `void Prefix(Character __instance)` (`:8`), no priority, `void` → never skips. Body: `if (__instance != null) AwarenessSystem.Clear(__instance);` (`:10-13`).

Does **not** call `StealthBrain.ClearCache(ai)` — the `_evalCache` entry keyed by `StableAIKey(networkUid, instanceId)` (`StealthBrain.cs:47`) is only reaped by the 600-frame `CleanupStaleCache` sweep (`:287-297`).

---

## 2. VANILLA GROUND TRUTH FOR EVERY PATCHED METHOD

### 2.1 `BaseAI.CanSenseTarget(Character)` — decompiled `:4519-4544`
```csharp
public bool CanSenseTarget(Character target)
{
    return CanSenseTarget(target, m_passiveAggresive);
}

public bool CanSenseTarget(Character target, bool passiveAggresive)
{
    return CanSenseTarget(base.transform, m_character.m_eye.position, m_hearRange, m_viewRange,
        m_viewAngle, IsAlerted(), m_mistVision, target, passiveAggresive, m_character.IsTamed());
}

public static bool CanSenseTarget(Transform me, Vector3 eyePoint, float hearRange, float viewRange,
    float viewAngle, bool alerted, bool mistVision, Character target, bool passiveAggresive, bool isTamed)
{
    if (!passiveAggresive && ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs) && (!isTamed || !target.GetBaseAI().IsAlerted()))
    {
        return false;
    }
    if (CanHearTarget(me, hearRange, target)) { return true; }
    if (CanSeeTarget(me, eyePoint, viewRange, viewAngle, alerted, mistVision, target)) { return true; }
    return false;
}
```
SoM patches only the **1-arg overload**. The 2-arg and 10-arg static overloads are unpatched. Callers of the 1-arg form: `BaseAI.FindEnemy()` `:5203`, `AnimalAI` `:3768`. Callers of the static form: `BaseAI.FindClosestCreature(...)` `:5266` — **completely un-intercepted by SoM**.

`FindEnemy()` (`:5191-5223`) is the acquisition loop SoM's override actually feeds:
```csharp
protected Character FindEnemy()
{
    List<Character> allCharacters = Character.GetAllCharacters();
    Character character = null;
    float num = 99999f;
    foreach (Character item in allCharacters)
    {
        if (!IsEnemy(m_character, item) || item.IsDead() || item.m_aiSkipTarget) { continue; }
        BaseAI baseAI = item.GetBaseAI();
        if ((!(baseAI != null) || !baseAI.IsSleeping()) && CanSenseTarget(item))
        {
            float num2 = Vector3.Distance(item.transform.position, base.transform.position);
            if (num2 < num || character == null) { character = item; num = num2; }
        }
    }
    ...
}
```
Called from `MonsterAI.UpdateTarget` `:5854`, throttled to every 2 s when a player is within 50 m, 6 s otherwise (`:5850-5854`).

### 2.2 `BaseAI.CanSeeTarget(Character)` — decompiled `:4577-4623`
```csharp
public bool CanSeeTarget(Character target)
{
    return CanSeeTarget(base.transform, m_character.m_eye.position, m_viewRange, m_viewAngle, IsAlerted(), m_mistVision, target);
}

public static bool CanSeeTarget(Transform me, Vector3 eyePoint, float viewRange, float viewAngle,
    bool alerted, bool mistVision, Character target)
{
    if (target == null || me == null) { return false; }
    if (target.IsPlayer())
    {
        Player player = target as Player;
        if (player.InDebugFlyMode() || player.InGhostMode()) { return false; }
    }
    float num = Vector3.Distance(target.transform.position, me.position);
    if (num > viewRange) { return false; }
    _ = num / viewRange;
    float stealthFactor = target.GetStealthFactor();
    float num2 = viewRange * stealthFactor;
    if (num > num2) { return false; }
    if (!alerted && Vector3.Angle(target.transform.position - me.position, me.forward) > viewAngle) { return false; }
    Vector3 vector = (target.IsCrouching() ? target.GetCenterPoint() : target.m_eye.position);
    Vector3 vector2 = vector - eyePoint;
    if (Physics.Raycast(eyePoint, vector2.normalized, vector2.magnitude, m_viewBlockMask)) { return false; }
    if (!mistVision && ParticleMist.IsMistBlocked(eyePoint, vector)) { return false; }
    return true;
}
```
Note the two **safety behaviours SoM discards** by returning `false` from its prefix: the `InDebugFlyMode() || InGhostMode()` bail-out, and the mist check. A ghost-mode/`debugmode fly` admin is fully visible to every SoM-controlled creature.

Also note there is a **second, non-virtual `CanSeeTarget(StaticTarget)`** at `:4625-4655`. SoM's explicit `new Type[] { typeof(Character) }` correctly disambiguates it — good, keep that.

Instance-overload callers: `MonsterAI.UpdateTarget` `:5921`, `Character.RPC_Damage` `:8736` (backstab), `NpcTalk.UpdateTarget` `:6676`.

### 2.3 `BaseAI.CanHearTarget(Character)` — decompiled `:4546-4575`
```csharp
public bool CanHearTarget(Character target)
{
    return CanHearTarget(base.transform, m_hearRange, target);
}

public static bool CanHearTarget(Transform me, float hearRange, Character target)
{
    if (target.IsPlayer())
    {
        Player player = target as Player;
        if (player.InDebugFlyMode() || player.InGhostMode()) { return false; }
    }
    float num = Vector3.Distance(target.transform.position, me.position);
    if (Character.InInterior(me)) { hearRange = Mathf.Min(12f, hearRange); }
    if (num > hearRange) { return false; }
    if (num < target.GetNoiseRange()) { return true; }
    return false;
}
```
The `Character.InInterior(me)` → 12 m hearing clamp is silently dropped by SoM. Callers: `MonsterAI.UpdateTarget` `:5920`, `NpcTalk.UpdateTarget` `:6677`.

### 2.4 `BaseAI.IsAlerted()` — decompiled `:5448-5451`
```csharp
public bool IsAlerted()
{
    return m_alerted;
}
```
Backing field `private bool m_alerted;` (`:3969`), loaded from ZDO for non-owners in `BaseAI.UpdateAI` (`:4113-4117`):
```csharp
if (!m_nview.IsOwner())
{
    m_alerted = m_nview.GetZDO().GetBool(ZDOVars.s_alert);
    return false;
}
```

### 2.5 `BaseAI.Alert()` / `SetAlerted()` — decompiled `:5327-5376` (**the getter/setter trap**)
```csharp
public void Alert()
{
    if (m_nview.IsValid() && !IsAlerted())      // <-- guarded by the getter SoM forces
    {
        if (m_nview.IsOwner()) { SetAlerted(alert: true); }
        else { m_nview.InvokeRPC("Alert"); }
    }
}

private void RPC_Alert(long sender)
{
    if (m_nview.IsOwner()) { SetAlerted(alert: true); }
}

protected virtual void SetAlerted(bool alert)
{
    if (m_alerted != alert)
    {
        m_alerted = alert;
        m_animator.SetBool("alert", m_alerted);
        if (m_nview.IsOwner()) { m_nview.GetZDO().Set(ZDOVars.s_alert, m_alerted); }
        if (m_alerted) { m_alertedEffects.Create(base.transform.position, Quaternion.identity); }
        if (m_character.IsBoss() && !m_nview.GetZDO().GetBool("bosscount"))
        {
            ZoneSystem.instance.GetGlobalKey(GlobalKeys.activeBosses, out float value);
            ZoneSystem.instance.SetGlobalKey(GlobalKeys.activeBosses, value + (float)(alert ? 1 : (-1)));
            m_nview.GetZDO().Set("bosscount", value: true);
        }
        if (alert && m_alertedMessage.Length > 0 && !m_nview.GetZDO().GetBool(ZDOVars.s_shownAlertMessage))
        {
            m_nview.GetZDO().Set(ZDOVars.s_shownAlertMessage, value: true);
            MessageHud.instance.MessageAll(MessageHud.MessageType.Center, m_alertedMessage);
        }
    }
}
```
`MonsterAI` overrides it (`:6513-6520`):
```csharp
protected override void SetAlerted(bool alert)
{
    if (alert) { m_timeSinceSensedTargetCreature = 0f; }
    base.SetAlerted(alert);
}
```

### 2.6 `MonsterAI.UpdateTarget(Humanoid, float, out bool, out bool)` — decompiled `:5846-5952`
Relevant tail (`:5916-5951`):
```csharp
canHearTarget = false;
canSeeTarget = false;
if ((bool)m_targetCreature)
{
    canHearTarget = CanHearTarget(m_targetCreature);
    canSeeTarget  = CanSeeTarget(m_targetCreature);
    if (canSeeTarget | canHearTarget) { m_timeSinceSensedTargetCreature = 0f; }
    if (m_targetCreature.IsPlayer())
    {
        m_targetCreature.OnTargeted(canSeeTarget | canHearTarget, IsAlerted());
    }
    SetTargetInfo(m_targetCreature.GetZDOID());
}
else { SetTargetInfo(ZDOID.None); }
m_timeSinceSensedTargetCreature += dt;
if (IsAlerted() || m_targetCreature != null)
{
    m_timeSinceAttacking += dt;
    float num = 60f;
    float num2 = Vector3.Distance(m_spawnPoint, base.transform.position);
    bool flag5 = HuntPlayer() && (bool)m_targetCreature && m_targetCreature.IsPlayer();
    if (m_timeSinceSensedTargetCreature > 30f || (!flag5 && (m_timeSinceAttacking > num || (m_maxChaseDistance > 0f && m_timeSinceSensedTargetCreature > 1f && num2 > m_maxChaseDistance))))
    {
        SetAlerted(alert: false);
        m_targetCreature = null;
        m_targetStatic = null;
        m_timeSinceAttacking = 0f;
        m_updateTargetTimer = 5f;
    }
}
```
**`MonsterAI.UpdateTarget` is NOT patched by SoM** (MistsofAvalor patches it; SoM does not). `m_targetCreature.OnTargeted(...)` at `:5928` is the vanilla stealth HUD feed — `Player.OnTargeted` (`:21627-21642`) RPCs it to the target player. Because SoM feeds `canSeeTarget/canHearTarget` from local-player state, **remote players get a stealth-eye HUD driven by someone else's stealth**.

### 2.7 `MonsterAI.UpdateAI(float)` — decompiled `:5954-6213`
Opening (`:5954-5970`):
```csharp
public override bool UpdateAI(float dt)
{
    if (!base.UpdateAI(dt)) { return false; }
    UpdateSleep(dt);
    if (IsSleeping()) { return true; }
    Humanoid humanoid = m_character as Humanoid;
    if (HuntPlayer()) { SetAlerted(alert: true); }
    UpdateTarget(humanoid, dt, out var canHearTarget, out var canSeeTarget);
    ...
```
The combat block SoM's `Humanoid.StartAttack` prefix sits inside (`:6116-6164`):
```csharp
else if ((bool)m_targetCreature)
{
    if (canHearTarget || canSeeTarget || (HuntPlayer() && m_targetCreature.IsPlayer()))
    {
        m_beenAtLastPos = false;
        m_lastKnownTargetPos = m_targetCreature.transform.position;
        float num = Vector3.Distance(m_lastKnownTargetPos, base.transform.position) - m_targetCreature.GetRadius();
        float num2 = m_alertRange * m_targetCreature.GetStealthFactor();
        if (canSeeTarget && num < num2) { SetAlerted(alert: true); }
        bool num3 = num < itemData.m_shared.m_aiAttackRange;
        if (!num3 || !canSeeTarget || itemData.m_shared.m_aiAttackRangeMin < 0f || !IsAlerted())
        {
            ...
            MoveTo(dt, lastKnownTargetPos, 0f, IsAlerted());
            ...
        }
        else { StopMoving(); }
        if (num3 && canSeeTarget && IsAlerted())
        {
            if (PheromoneFleeCheck(m_targetCreature)) { ... }
            else
            {
                LookAt(m_targetCreature.GetTopPoint());
                if (flag3 && IsLookingAt(m_lastKnownTargetPos, itemData.m_shared.m_aiAttackMaxAngle, itemData.m_shared.m_aiInvertAngleCheck))
                {
                    DoAttack(m_targetCreature, isFriend: false);
                }
            }
        }
    }
    else
    {
        ChargeStop();
        if (m_beenAtLastPos) { RandomMovement(dt, m_lastKnownTargetPos); ... }
        else if (MoveTo(dt, m_lastKnownTargetPos, 0f, IsAlerted())) { m_beenAtLastPos = true; }
    }
}
```
Attack pacing (`:6062-6065`) — this is the "vanilla owns rhythm" claim in SoM's comment, and it is accurate:
```csharp
ItemDrop.ItemData itemData = SelectBestAttack(humanoid, dt);
bool flag  = itemData != null && Time.time - itemData.m_lastAttackTime > itemData.m_shared.m_aiAttackInterval;
bool flag2 = m_character.GetTimeSinceLastAttack() >= m_minAttackInterval;
bool flag3 = itemData != null && flag && flag2 && !IsTakingOff();
```

**Scheduling fact for the perf brief**: `UpdateAI` is not per-frame. `MonoUpdaters.FixedUpdate` (`:60995-61000`) runs it on a fixed 0.05 s accumulator:
```csharp
m_updateAITimer += fixedDeltaTime;
if (m_updateAITimer >= 0.05f)
{
    m_ai.UpdateAI(BaseAI.Instances, "MonoUpdaters.FixedUpdate.BaseAI", 0.05f);
    m_updateAITimer -= 0.05f;
}
```
So `dt` is always exactly `0.05f`. SoM's `StealthOvermind` instead ticks on `yield return null` (per **frame**, `StealthOvermind.cs:133`) with tiers counted in frames (`:101-106`). The two clocks are unrelated, and SoM discards the `dt` vanilla hands it (`MonsterAI_StealthBrain_UpdateAI_Patch.cs:14`, unused).

### 2.8 `Humanoid.StartAttack(Character, bool)` — decompiled `:13073-13109`
```csharp
public override bool StartAttack(Character target, bool secondaryAttack)
{
    if ((InAttack() && !HaveQueuedChain()) || InDodge() || !CanMove() || IsKnockedBack() || IsStaggering() || InMinorAction()) { return false; }
    ItemDrop.ItemData currentWeapon = GetCurrentWeapon();
    if (currentWeapon == null) { return false; }
    if (secondaryAttack && !currentWeapon.HaveSecondaryAttack()) { return false; }
    if (!secondaryAttack && !currentWeapon.HavePrimaryAttack()) { return false; }
    if (m_currentAttack != null) { m_currentAttack.Stop(); m_previousAttack = m_currentAttack; m_currentAttack = null; }
    Attack attack = ((!secondaryAttack) ? (attack = currentWeapon.m_shared.m_attack.Clone()) : (attack = currentWeapon.m_shared.m_secondaryAttack.Clone()));
    if (attack.Start(this, m_body, m_zanim, m_animEvent, m_visEquipment, currentWeapon, m_previousAttack, m_timeSinceLastAttack, GetAttackDrawPercentage()))
    {
        ClearActionQueue();
        StartAttackGroundCheck();
        m_currentAttack = attack;
        m_currentAttackIsSecondary = secondaryAttack;
        m_lastCombatTimer = 0f;
        return true;
    }
    return false;
}
```
Base is `Character.StartAttack` (`:9675-9678`) → `return false;`. Only entry point for AI is `MonsterAI.DoAttack` (`:6332-6349`) via `m_character.StartAttack(target, charge: false)`.
**Parameter name is `secondaryAttack` on `Humanoid`, `charge` on `Character`** — a future SoM patch that injects the bool by name must use `Humanoid`'s name.

### 2.9 `Character.SetMoveDir(Vector3)` — decompiled `:9503-9506`
```csharp
public void SetMoveDir(Vector3 dir)
{
    m_moveDir = dir;
}
```
The magnitude→speed claim in SoM's comment is **verified** in `Character.UpdateWalking` (`:8179-8226`):
```csharp
private void UpdateWalking(float dt)
{
    Vector3 moveDir = m_moveDir;
    bool flag = IsCrouching();
    m_running = CheckRun(moveDir, dt);
    float speed = m_speed * GetJogSpeedFactor();
    if ((m_walk || InMinorActionSlowdown()) && !flag) { speed = m_walkSpeed; m_walking = moveDir.magnitude > 0.1f; }
    else if (m_running)
    {
        speed = m_runSpeed * GetRunSpeedFactor();
        if (IsPlayer() && moveDir.magnitude > 0f) { moveDir.Normalize(); }   // <-- players only
    }
    ...
    Vector3 vector = (CanMove() ? (moveDir * speed) : Vector3.zero);
```
The `0.1f` floor claim is also verified — `Character.CheckRun` (`:9923-9942`):
```csharp
protected virtual bool CheckRun(Vector3 moveDir, float dt)
{
    if (!m_run) { return false; }
    if (moveDir.magnitude < 0.1f) { return false; }
    if (IsCrouching() || IsEncumbered()) { return false; }
    if (InDodge()) { return false; }
    return true;
}
```
`SetMoveDir` call sites in `BaseAI`: `MoveTowardsSwoop` `:4668`, `MoveTowards` `:4682`/`:4692`, `StopMoving` `:4736`. `StopMoving()` passes `Vector3.zero` → SoM's `magnitude <= 0.001f` guard (`:35`) correctly lets it through.

Two `CheckRun` overrides exist (`:14439`, `:17630`) — creature-specific subclasses whose extra conditions SoM's floor does not account for.

### 2.10 `Character.Damage(HitData)` — decompiled `:8692-8699`
```csharp
public void Damage(HitData hit)
{
    if (m_nview.IsValid())
    {
        hit.m_weakSpot = FindWeakSpotIndex(hit.m_hitCollider);
        m_nview.InvokeRPC("RPC_Damage", hit);
    }
}
```
**This is the sender side only.** All real damage resolution is in `RPC_Damage` (`:8701-8802`), which bails for non-owners at `:8712-8715`:
```csharp
if (!m_nview.IsOwner()) { return; }
```
Backstab gate, `:8736-8741`:
```csharp
if (m_baseAI != null && !m_baseAI.IsAlerted() && hit.m_backstabBonus > 1f && Time.time - m_backstabTime > 300f
    && (!ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs) || !m_baseAI.CanSeeTarget(attacker)))
{
    m_backstabTime = Time.time;
    hit.ApplyModifier(hit.m_backstabBonus);
    m_backstabHitEffects.Create(hit.m_point, Quaternion.identity, base.transform);
}
```
Aggravate gate, `:8732-8735`:
```csharp
if (m_baseAI != null && m_baseAI.IsAggravatable() && !m_baseAI.IsAggravated() && (bool)attacker && attacker.IsPlayer() && hit.GetTotalDamage() > 0f)
{
    BaseAI.AggravateAllInArea(base.transform.position, 20f, BaseAI.AggravatedReason.Damage);
}
```

### 2.11 `Character.OnDamaged(HitData)` — decompiled `:9103-9105`
```csharp
protected virtual void OnDamaged(HitData hit)
{
}
```
Called from `Character.ApplyDamage` `:8866` (owner-only path). **`Humanoid` overrides it and does not chain** (`:13249-13252`):
```csharp
protected override void OnDamaged(HitData hit)
{
    SetCrouch(crouch: false);
}
```
`Player` overrides and *does* chain (`:21657-21659`: `base.OnDamaged(hit);`).

### 2.12 `Character.OnDestroy()` — decompiled `:7390-7398`
```csharp
protected virtual void OnDestroy()
{
    m_seman.OnDestroy();
    s_characters.Remove(this);
    if (EnemyHud.instance != null)
    {
        EnemyHud.instance.RemoveCharacterHud(this);
    }
}
```
`Humanoid` chains (`:12969-12972`: `base.OnDestroy();`), so this patch **does** fire for humanoids. This is the only virtual-override patch in the mod that is safe.

### 2.13 The vanilla alert-on-damage path SoM does **not** patch — `BaseAI.OnDamaged(float, Character)`
Subscribed in `BaseAI.Awake` (`:4027-4028`):
```csharp
Character character = m_character;
character.m_onDamaged = (Action<float, Character>)Delegate.Combine(character.m_onDamaged, new Action<float, Character>(OnDamaged));
```
Base (`:4506-4509`): `m_timeSinceHurt = 0f;`
`MonsterAI` override (`:5797-5814`):
```csharp
protected override void OnDamaged(float damage, Character attacker)
{
    base.OnDamaged(damage, attacker);
    Wakeup();
    SetAlerted(alert: true);
    SetTarget(attacker);
}

private void SetTarget(Character attacker)
{
    if (attacker != null && m_targetCreature == null && (!attacker.IsPlayer() || !m_character.IsTamed()))
    {
        m_targetCreature = attacker;
        m_lastKnownTargetPos = attacker.transform.position;
        m_beenAtLastPos = false;
        m_targetStatic = null;
    }
}
```
Invoked from `Character.ApplyDamage` `:8871-8874`. **This is the correct owner-side, attacker-aware hook that SoM should have used instead of `Character.Damage` and `Character.OnDamaged`.** It fires on the owner, carries the attacker, and already does the right vanilla thing.

---

## 3. DEFECT: THE `data == null` GUARD IS DEAD IN ALL EIGHT PATCHES

`AwarenessSystem.GetData` (`Systems\AwarenessSystem.cs:11-20`):
```csharp
public static AwarenessData GetData(Character c)
{
    if (!Data.TryGetValue(c, out AwarenessData d))
    {
        d = new AwarenessData();
        Data[c] = d;
    }
    return d;
}
```
It **always allocates and never returns null**. Consequences:

1. Every `if (data == null) return true;` in `BaseAI_StealthBrain_Patch.cs:32`, `BaseAI_CanSeeTarget_Patch.cs:34`, `BaseAI_CanHearTarget_Patch.cs:34`, `BaseAI_StealthBrain_IsAlerted_Patch.cs:21`, `BaseAI_SetMoveDir_Patch.cs:28`, `Humanoid_Attack_Patch.cs:38`, and the two damage patches is unreachable. There is **no "SoM has no opinion, fall through to vanilla" path** anywhere in the mod.
2. Therefore a MonsterAI the Overmind has never touched — out of `MAX_TRACKING_RANGE = 64f` of `Player.m_localPlayer` (`StealthOvermind.cs:13`, `:64-66`), asleep, or near a *remote* player — is served the default `AwarenessData`: `DetectionLevel = 0f`, `CanSense/CanSee/CanHear = false`, `CurrentState = Unaware` (`UnifiedStealthTypes.cs:62-72`). Vanilla `FindEnemy` can therefore **never** acquire it, and `Humanoid_Attack_Patch` GATE 1 (`:43`) blocks every attack. **Any creature outside the local player's 64 m bubble but owned by this client is permanently blind and permanently pacified.**
3. `AwarenessSystem.GetData` is also called from the *prefix path of a hot loop*: `FindEnemy` calls `CanSenseTarget(item)` once per `Character.GetAllCharacters()` entry per MonsterAI. With `Character` as the dictionary key and no eviction other than `Character.OnDestroy`, the dictionary grows to one entry per creature the client has ever had loaded.

---

## 4. DEFECT: FORCED `IsAlerted()` DISABLES THE SETTER GUARDED BY IT — AND SIXTEEN OTHER READERS

### 4.1 The confirmed `Alert()` trap
`BaseAI.Alert()` (`:5327-5340`) is guarded by `!IsAlerted()`. `BaseAI_StealthBrain_IsAlerted_Patch.Prefix` (`:23-25`) forces `true` whenever `data.CurrentState` is `Alerted` or `Engaged` and returns `false` so `m_alerted` is never consulted.

Result: while SoM says "alerted", `Alert()` becomes a **no-op**. `SetAlerted(true)` never runs, so all of `:5352-5375` is skipped:
- `m_alerted` stays `false` on the owner.
- `m_animator.SetBool("alert", ...)` never fires → the creature hunts you in its idle/unalerted animation set.
- `m_nview.GetZDO().Set(ZDOVars.s_alert, ...)` never fires → **every other client's `BaseAI.UpdateAI` non-owner branch (`:4113-4117`) keeps reading `false` forever.**
- `m_alertedEffects.Create(...)` (the roar/aggro sound) never plays.
- Boss `activeBosses` global key is never incremented.
- `m_alertedMessage` ("Eikthyr awakens") never shows.
- `MonsterAI.SetAlerted`'s override (`:6513-6520`) never resets `m_timeSinceSensedTargetCreature`.

MistsofAvalor already hit this exact bug and works around it with a suppression flag — see `AvalorStealthGuards.cs:64-69`:
```csharp
// Stand down while our own Alert() call is reading the real field - otherwise vanilla Alert()
// sees "already alerted", skips SetAlerted, and the mob hunts you in its idle animation.
if (AvalorStealthCompat.SuppressAlertForce) return;
```
This is third-party confirmation, in the field, of the failure mode.

`Alert()` call sites in the client assembly:
| Line | Caller |
|---|---|
| `:3511` | creature spawner, `m_alertSpawnedCreature` |
| `:5323` | `BaseAI.RPC_OnNearProjectileHit` — arrows whizzing past |
| `:5592` | `BaseAI.AggravateAllInArea` (reached from `Character.RPC_Damage:8734`) |
| `:7549` | `Character` pheromone/love effect → `monsterAI.Alert()` |
| `:117833` | boss altar `DelayedSpawnBoss`, `m_alertOnSpawn` |

All five are dead while SoM reports alerted.

### 4.2 The reverse direction — SoM forcing `IsAlerted() == false`
Whenever `data.CurrentState` is `Unaware`/`Suspicious` but vanilla `m_alerted == true`:

- **Backstab exploit.** `Character.RPC_Damage:8736` gates the backstab multiplier on `!m_baseAI.IsAlerted()`. This runs on the defender's **owner** client, where SoM's authority check passes. A creature in open melee that SoM has decayed to Suspicious grants full backstab damage (still rate-limited by `Time.time - m_backstabTime > 300f` per creature).
- **Sneak XP.** `Player.UpdateStealth`-adjacent code at `:21798-21808` calls `BaseAI.InStealthRange(this)`; `InStealthRange` (`:5378-5398`) returns `false` the moment any in-range AI `IsAlerted()`. Forcing false = permanent fast Sneak XP; forcing true = zero Sneak XP.
- **Give-up / leash never arms.** `MonsterAI.UpdateTarget:5937` — `if (IsAlerted() || m_targetCreature != null)` gates the entire block at `:5939-5950` that calls `SetAlerted(false)`, nulls `m_targetCreature`, resets `m_timeSinceAttacking`, and sets `m_updateTargetTimer = 5f`.

### 4.3 Full list of vanilla `IsAlerted()` readers hijacked
| Decompiled line | Consumer | Effect of SoM's override |
|---|---|---|
| `:4526` | `CanSenseTarget(target, passive)` → `alerted` arg | skips FOV cone check |
| `:4579` | `CanSeeTarget(target)` → `alerted` arg | skips FOV cone check |
| `:4637` | `CanSeeTarget(StaticTarget)` | **unpatched method, patched getter** — building-attack targeting changes |
| `:4531` | static `CanSenseTarget` PassiveMobs branch | tamed-vs-alerted interplay |
| `:5299-5302` | `BaseAI.HaveAlertedCreatureInRange(float)` | no callers in client asm; live for modded/prefab subclasses |
| `:5390` | `BaseAI.InStealthRange` | Sneak XP rate (§4.2) |
| `:5876` | `MonsterAI.UpdateTarget` random-static-target selection | forced true → creatures start smashing buildings while "alerted" |
| `:5928` | `m_targetCreature.OnTargeted(sensed, IsAlerted())` | player stealth-HUD "eye" open/closed state |
| `:5937` | `MonsterAI.UpdateTarget` give-up block gate | §4.2 |
| `:5988` | `MonsterAI.UpdateAI` event-creature despawn `m_targetCreature == null && !IsAlerted()` | forced true → **event creatures never despawn after the raid ends** |
| `:5994` | `m_fleeIfNotAlerted && ... && !IsAlerted()` | forced true → suppresses flee-if-not-alerted |
| `:6045` | `(!IsAlerted() \|\| no target) && UpdateConsumeItem(humanoid, dt)` | **forced true + target ⇒ creature refuses food ⇒ taming stalls.** SoM's `IsTamed()` guard does not help: a creature being tamed is not yet tamed |
| `:6058`, `:6073` | `RandomMovementArroundPoint(dt, ..., IsAlerted())` run flag | circling speed |
| `:6112`, `:6138`, `:6177`, `:6204` | `MoveTo(dt, point, 0f, IsAlerted())` run flag | walk-vs-run for the whole approach |
| `:6129` | `!num3 \|\| !canSeeTarget \|\| ... \|\| !IsAlerted()` | approach-vs-stop decision |
| `:6148` | `if (num3 && canSeeTarget && IsAlerted())` | **the actual vanilla attack gate**, in addition to SoM's own gate |
| `:8736` | `Character.RPC_Damage` backstab | §4.2 |
| `:23215` | `CharacterAnimEvent.GetRandomIdle` → `m_alertedIdle` | idle animation selection |
| `:38632-38635` | `EnemyHud` | see §5.5 |

### 4.4 Other getter/setter inversions of the same shape (checked, for completeness)
- `MonsterAI.IsSleeping()` / `Sleep()` / `Wakeup()` — `Wakeup()` (`:6473-6482`) is guarded by `if (IsSleeping())` and `Sleep()` (`:6495-6508`) by `if (!IsSleeping())`. **SoM does not patch `IsSleeping`**, so this trap is not currently sprung — but any future "force awake" feature must not patch the getter.
- `BaseAI.IsAggravated()` — `Character.RPC_Damage:8732` and `MonsterAI.UpdateTarget:5862` both gate on it; `SetAggravated` is the setter. **Not patched by SoM.** MistsofAvalor writes through the vanilla setter (`:3516`), so no conflict today.
- `BaseAI.HaveTarget()` (`:5458`) reads `ZDOVars.s_haveTargetHash`, written only by `SetTargetInfo` (`:5453-5456`). SoM writes it indirectly via `BehaviorSystem.SetAITarget` reflection (`BehaviorSystem.cs:110-136`) — correct direction, no inversion.

---

## 5. DEFECTS THAT ARE WRONG FOR MULTIPLE PLAYERS

### 5.1 THE CENTRAL BUG — per-target queries answered from single-player state
`BaseAI.CanSeeTarget(Character target)`, `CanHearTarget(Character target)`, `CanSenseTarget(Character target)` all take a **target** argument. SoM accepts it in all six methods and **never reads it**:

- `BaseAI_CanSeeTarget_Patch.cs:16` — `Character target` param, used only for `target.IsPlayer()` at `:21`; result at `:37` is `data.CanSee`.
- `BaseAI_CanHearTarget_Patch.cs:16,37` — same shape.
- `BaseAI_StealthBrain_Patch.cs:11,43` — same shape.

`data` is per-**creature** (`AwarenessSystem.cs:9` — `Dictionary<Character, AwarenessData>`), and its only writer is `StealthBrain.Evaluate(MonsterAI ai, Player p, float dt)` called from exactly one place with exactly one player:

```csharp
// StealthOvermind.cs:39
Player p = Player.m_localPlayer;
...
// StealthOvermind.cs:119
StealthBrain.Evaluate(entry.Monster, p, timeSinceEval);
```
and `SensingEvaluator.EvaluateSensing` reads that one player's stealth exclusively (`SensingEvaluator.cs:10-16`):
```csharp
float vis    = cfg.EnableVisibilitySystem ? VisibilitySystem.GetVisibility(p) : 0f;
float noise  = cfg.EnableNoiseSystem      ? NoiseSystem.GetNoise(p)           : 0f;
float hiding = cfg.EnableHidingSystem     ? HidingSystem.GetHidingFactor(p)   : 0f;
float camo   = cfg.EnableCamoSystem       ? CamoSystem.GetCamoFactor(p)       : 0f;
bool hasLOS  = AwarenessSystem.HasLineOfSight(c, p);
```

Concrete failures:
- Player A sneaks, player B stands in torchlight. `FindEnemy` (`:5203`) asks the creature "can you sense B?" → SoM answers with A's numbers → **B is invisible**. Group stealth is inverted rather than absent.
- Symmetrically, A sneaks and B is visible → the creature "senses" A and paths to A. Sneaking is punished by a teammate's carelessness.
- `MonsterAI.UpdateTarget:5920-5921` retains/drops `m_targetCreature` using the same wrong booleans, so `m_timeSinceSensedTargetCreature` (and the 30 s give-up at `:5943`) is driven by the wrong player.
- `FindEnemy` picks `min distance` among *sensed* candidates. Because SoM returns one constant boolean for all player candidates, the loop degenerates to "nearest player, stealth-blind" whenever `data.CanSense` is true.
- `NpcTalk.UpdateTarget` (`:6665-6683`) calls `m_monsterAI.CanSeeTarget(closestPlayer)` / `CanHearTarget(closestPlayer)` for the *closest* player, which is usually not the local one → dvergr greetings fire at the wrong person.

**Required fix shape:** the detection record must be keyed `(creature, playerZDOID)`; the three sensing patches must select the track for the `target` argument and fall through to vanilla when no track exists.

### 5.2 The Overmind's discovery set is local-player-centred
`StealthOvermind.EvaluateLoop` (`StealthOvermind.cs:38-45, 52-84`):
```csharp
Player p = Player.m_localPlayer;
if (p == null || p.IsDead()) { yield return new WaitForSeconds(1f); continue; }
...
float dist = Vector3.Distance(character.transform.position, playerPos);
if (dist > MAX_TRACKING_RANGE) continue;         // MAX_TRACKING_RANGE = 64f
```
Combined with §3 (no null fall-through), **creatures this client owns but that sit near a remote player are frozen at `Unaware` and cannot see, hear, sense, alert, or attack anybody.** ZDO ownership does migrate toward the nearest player over time, but it is not instantaneous and not guaranteed, so there is always a live population in this state.

Also: when the local player is dead, the loop `continue`s with a 1 s wait — **the entire mod stops evaluating for every creature this client owns** while other players are still fighting.

### 5.3 `MonsterAI_StealthBrain_UpdateAI_Patch` retargets to the local player
```csharp
Player player = Player.m_localPlayer;             // :25
if (player == null || player.IsDead()) return;    // :26-27
...
BehaviorSystem.ExecuteAction(__instance, decision.RecommendedAction, player, decision);   // :38
```
`ExecuteAction`'s `Pursue`/`Attack` branches (`BehaviorSystem.cs:263-275`) do `SetTarget(ai, target)` with that local player, and `SetTarget` reflection-writes `m_targetCreature` **and** calls `SetTargetInfo(targetZdo.m_uid)` (`BehaviorSystem.cs:141-170`). So a creature actively fighting remote player B gets its target **rewritten to local player A every AI tick**. In a two-client session both clients fight over `m_targetCreature` for creatures whose ownership is in flux.

Same file: the `Idle` branch calls `ResetAI(ai)` → `SetTarget(ai, null)` (`BehaviorSystem.cs:221-233`), which will strip a legitimately-acquired remote-player target the moment SoM's local-player-derived state decays to Unaware.

`BehaviorSystem.CallNearbyAllies` (`:175-216`) has the same defect: it stamps `allyData.LastKnownPosition = target.transform.position` and forces `CurrentState = Alerted` on a shared per-creature record with no notion of *which* player was seen.

### 5.4 The two damage patches are on the wrong side of the network
- `Character_Damage_Patch` postfixes `Character.Damage(HitData)` (`:8692-8699`) — that method **only sends `RPC_Damage`**. It executes on the *attacker's* machine. The patch then requires `AIAuthority.IsAuthoritative(ai)` (`Character_Damage_Patch.cs:22`) i.e. `nview.IsOwner()`. So:
  - Player B hits a creature owned by client A: on B, `IsOwner()==false` → early return. On A, `Damage()` is never called (only `RPC_Damage`). **The awareness bump is lost entirely.**
  - It only ever works when the attacker happens to own the creature.
  - It also fires for hits that `RPC_Damage` subsequently rejects (`:8721` — dead, dodge-invincible, PvP disabled, teleporting), so a creature can be "alerted" by damage that never landed.
- `CharacterOnDamagedPatch` postfixes `Character.OnDamaged(HitData)` (`:9103-9105`) which *is* the correct owner-side hook (reached from `ApplyDamage:8866` after the `!IsOwner()` bail at `:8712`) — **but `Humanoid` overrides it without chaining** (`:13249-13252`, quoted in §2.11). Greydwarves, Draugr, Fulings, Skeletons, Dvergr, Trolls, every Humanoid-derived enemy in the game: the patch never runs. It fires only for non-Humanoid `Character`s.

Net: for the majority of Valheim's enemies, **neither damage patch updates awareness on the authoritative client**. Combined with §4.1 (vanilla `Alert()` neutered) the only surviving "I've been hit" reaction is vanilla's `MonsterAI.OnDamaged(float, Character)` (`:5797-5803`), which calls `SetAlerted(true)` directly and therefore still works — but SoM's `IsAlerted` override then hides the resulting `m_alerted == true` from every vanilla reader in §4.3, and `Humanoid_Attack_Patch` GATE 1 still blocks the swing because `data.CurrentState` was never raised.

### 5.5 Divergent enemy HUD between clients
`EnemyHud` (`:38631-38636`):
```csharp
bool flag  = value.m_character.GetBaseAI().HaveTarget();
bool flag2 = value.m_character.GetBaseAI().IsAlerted();
value.m_alerted.gameObject.SetActive(flag2);
value.m_aware.gameObject.SetActive(!flag2 && flag);
```
Runs on **every** client for every visible enemy. SoM's `IsAlerted` override applies only where `nview.IsOwner()`. Therefore the owning client sees SoM's `!` icon while every other client sees vanilla `m_alerted` read from a ZDO that §4.1 guarantees is never updated. Two players looking at the same monster see contradictory alert icons.

### 5.6 `StealthBrain._evalCache` key is instance-local
`StableAIKey` (`StealthBrain.cs:18-45`) is `(NetworkUid, InstanceId)` where `InstanceId = ai.GetInstanceID()` — a per-process Unity handle. On ownership migration the new owner has a different `InstanceID`, so the cache cannot be shared or reasoned about across peers; and `MigrateCacheCoroutine` (`:135-158`) only patches the `uid == 0` → `uid` transition on the local process. `NetworkUid` is `(ulong)zdo.m_uid.UserID` — **the user id half only**, dropping `m_uid.ID`, so two ZDOs created by the same peer collide on that component (the `InstanceId` half is what actually disambiguates them locally).

---

## 6. PERFORMANCE FINDINGS (brief item 4)

1. **Every sensing patch does its full guard chain twice** — once in the prefix, once in the postfix that re-asserts the identical value. Per invocation that is 2× `GetComponent<ZNetView>()` (inside `AIAuthority.IsAuthoritative`, `AIAuthority.cs:14`), 2× `GetComponent<Character>()`, 2× `StealthExemption.IsExempt` (another `GetComponent<ZNetView>()` + `zdo.GetInt`, `StealthExemption.cs:10-13`), 2× dictionary lookup. That is ~8 `GetComponent` calls per `CanSenseTarget` call.
2. `FindEnemy` (`:5191-5223`) calls `CanSenseTarget(item)` **once per `Character.GetAllCharacters()` entry**, per MonsterAI, every 2 s. With 60 loaded creatures that is 3600 patched calls / 2 s ⇒ ~29k `GetComponent` calls per second from this patch family alone.
3. The postfixes are **pure overhead in the common case** — the prefix already set `__result` and returned `false`, so the postfix recomputes the same guards to write the same value. They exist only to beat other mods' postfixes.
4. `BaseAI_SetMoveDir_Patch` is a prefix on `Character.SetMoveDir`, which vanilla calls from `MoveTowards`/`StopMoving`/`MoveTo` for **every** non-player character every AI tick, plus once per player per frame. The `is Player` short-circuit is first (`:21`), which is right, but every AI still pays `GetComponent<BaseAI>()` + `IsAuthoritative` (another `GetComponent`) + `IsExempt` (another `GetComponent` + ZDO read) + dictionary lookup before the `CurrentAction` test at `:30` rejects the ~95 % of cases that are not Search/Investigate. **Reorder: cheapest discriminator (`data.CurrentAction`) cannot be first because it needs `data`; cache the component references on the creature instead.**
5. `BehaviorSystem.CallNearbyAllies` (`:180`) does an unfiltered `Physics.OverlapSphere(position, radius)` — **no layer mask** — at 30 m radius, on a 3 s cooldown per engaged creature (`StealthBrain.cs:9, 214`).
6. `AwarenessSystem.Data` and `StealthBrain._evalCache` are both plain `Dictionary` with `Character`/`StableAIKey` keys and no capacity hint; `_evalCache` is swept only every 600 frames (`StealthBrain.cs:49, 67-71`).

---

## 7. VALHEIM v1.0 BINDING FRAGILITY (brief item 1)

Every patch uses attribute-based targeting resolved at `PatchAll` time, with no `[HarmonyPatch]`-level `TargetMethod`/`Prepare` fallback and no per-class try/catch. Ranked by risk:

| Patch | Target spec | Failure mode in v1.0 |
|---|---|---|
| `Humanoid_Attack_Patch` | `[HarmonyPatch(typeof(Humanoid), "StartAttack")]` — **no type array** (`:7`) | If v1.0 adds any `StartAttack` overload on `Humanoid`, `AccessTools.Method` name-only lookup throws `AmbiguousMatchException` → `PatchAll` aborts → **all ten patches vanish**, swallowed by `ShadowsOfMidgard.cs:50` |
| `Character_Damage_Patch` | `[HarmonyPatch(typeof(Character), "Damage")]` — no type array (`:6`) | same ambiguity risk |
| `Character_OnDamaged_Patch` | `[HarmonyPatch(typeof(Character), "OnDamaged")]` — no type array (`:10`) | same; also already broken for Humanoids (§5.4) |
| `BaseAI_SetMoveDir_Patch` | `[HarmonyPatch(typeof(Character), "SetMoveDir")]` (`:6`) + `ref Vector3 dir` | depends on the **parameter being named `dir`**; a rename silently breaks the by-ref injection |
| `MonsterAI_StealthBrain_UpdateAI_Patch` | `"UpdateAI"` (`:6`), postfix takes `float dt` | depends on parameter name `dt` |
| `BaseAI_StealthBrain_IsAlerted_Patch` | `"IsAlerted"` (`:6`) | method removal/rename ⇒ abort |
| The three sensing patches | explicit `new Type[] { typeof(Character) }` — **correct and disambiguated** | only a rename breaks them |

Also soft-bound outside `Patches/` but load-bearing for two of them (`BehaviorSystem.cs` static ctor, `:25-74`) — these already do the right defensive thing and should be the template:
- `AccessTools.Method(typeof(BaseAI), "SetTargetInfo", new[]{ typeof(ZDOID) })` — matches vanilla `:5453`
- `AccessTools.Method(typeof(BaseAI), "MoveTo", new[]{ typeof(float), typeof(Vector3), typeof(float), typeof(bool) })` — matches vanilla `:4771 protected bool MoveTo(float dt, Vector3 point, float dist, bool run)`
- `AccessTools.FieldRefAccess<BaseAI, Character>("m_targetCreature")`

Hard-bound vanilla surface used directly by the patches, all of which must be probed defensively in v2: `BaseAI`, `MonsterAI`, `Character`, `Humanoid`, `Player`, `ZNetView.IsOwner()`, `ZNet.instance.IsServer()`, `ZDO.GetInt`, `Character.IsTamed/IsDead/IsPlayer/GetComponent`, `MonsterAI.GetTargetCreature()`, `Character.m_running` (written directly at `BehaviorSystem.cs:317-320`), `Character.SetMoveDir`.

---

## 8. UNPATCHED VANILLA SURFACE THAT LEAKS AROUND SoM

These are the holes an architect must close (or deliberately accept):

1. **`BaseAI.CanSenseTarget(Transform, Vector3, float, float, float, bool, bool, Character, bool, bool)`** static, `:4529` — used by `BaseAI.FindClosestCreature` `:5266`. Not patched. Any consumer of `FindClosestCreature` (turrets, tamed-AI helpers, other mods) gets pure vanilla stealth.
2. **`BaseAI.CanSeeTarget(Transform, ...)`** static `:4582` and **`CanHearTarget(Transform, float, Character)`** static `:4551` — not patched.
3. **`BaseAI.CanSenseTarget(Character, bool)`** 2-arg `:4524` — not patched; only the 1-arg forwarder is.
4. **`BaseAI.CanSeeTarget(StaticTarget)`** `:4625` — not patched, but reads the SoM-forced `IsAlerted()` at `:4637`.
5. **`MonsterAI.UpdateTarget`** — not patched by SoM (MistsofAvalor does patch it, `AvalorStealthGuards.cs:129-138`).
6. **`BaseAI.Alert()` / `SetAlerted(bool)`** — not patched; §4.1 shows why they must be.
7. **`MonsterAI.OnDamaged(float, Character)`** `:5797` — the correct owner-side damage hook, not patched (§2.13).
8. **`Character.RPC_Damage`** `:8701` — the only place damage is actually resolved on the owner; not patched.
9. **`Player.OnTargeted(bool, bool)`** `:21627` / `Character.OnTargeted` `:10333` — the vanilla stealth-HUD feed, fed wrong values via `MonsterAI.UpdateTarget:5928`; not patched.
10. **`BaseAI.HaveTarget()`** `:5458` and `SetTargetInfo` `:5453` — reached only through `BehaviorSystem`'s reflection, never through a patch.

---

## 9. CROSS-MOD ORDERING (MistsofAvalor) — CONCRETE INTERACTIONS

MistsofAvalor patches four of the same targets (`AvalorStealthGuards.cs`):

| Vanilla target | Avalor | SoM |
|---|---|---|
| `BaseAI.IsAlerted` | Prefix `Priority.First` + `HarmonyBefore(SoM)`; Postfix `Priority.Last` + `HarmonyAfter(SoM)` (`:51-71`) | Prefix `int.MaxValue`; Postfix `int.MinValue` |
| `BaseAI.CanSenseTarget(Character)` | Prefix `First`+`Before`; Postfix `Last`+`After` (`:76-110`) | Prefix `int.MaxValue`; Postfix `int.MinValue` |
| `Humanoid.StartAttack` | Prefix `First`+`Before` (`:115-125`) | Prefix **no priority** (Normal, 400) |
| `MonsterAI.UpdateTarget` | Prefix `First`+`Before` (`:129-138`) | *(not patched)* |
| `MonsterAI.UpdateAI` | Prefix `First`+`Before` (`:142-151`) | Postfix `int.MinValue` |

Facts an architect needs:
- **Prefix side works today.** SoM's prefixes return `false`, which skips the original *and every lower-priority prefix that does not declare `bool __runOriginal`*. Avalor's prefixes are void and land ahead of SoM only because of the `HarmonyBefore` edge (Harmony's `PatchSorter` honours before/after edges over raw priority). If that edge ever fails to resolve — e.g. SoM changes its Harmony id — Avalor's `int.MaxValue`-beaten prefix would be **skipped entirely** and Avalorian mobs would stop attacking. SoM v2 must keep the id `"wubarrk.shadowsofmidgard"` verbatim.
- **Postfix side is a genuine contested race.** Avalor asks for `HarmonyAfter(SoM)` at `Priority.Last (0)`; SoM asks for `int.MinValue`. Both want to write `__result` last. Which wins depends on whether the edge or the priority dominates the sort — this is exactly the kind of thing that changes between HarmonyX builds. **v2 should stop re-asserting in a postfix and instead expose an explicit "final say" hook**, which is also the ~50 % CPU saving in §6.
- `Humanoid_Attack_Patch` is SoM's only patch at default priority. If any third mod prefixes `Humanoid.StartAttack` at `Priority.First` and returns `false`, SoM's gate never runs at all — silently.
- **Avalor reflection-writes into `AwarenessData`** (`AvalorStealthCompat.cs:253-260, 417-423`):
  ```csharp
  _fCurrentState.SetValue(data, _valAlerted);
  _fDetectionLevel.SetValue(data, 1f);
  _fCanSense.SetValue(data, true);
  _fCanSee?.SetValue(data, true);
  _fCanHear?.SetValue(data, true);
  ```
  So `AwarenessSystem.GetData(Character)`, and the `AwarenessData` field names `CurrentState`, `DetectionLevel`, `CanSense`, `CanSee`, `CanHear`, `CurrentFleeReason`, plus the `VanillaAlertness` and `FleeReason` enum type names, are **de-facto public API**. A per-player track redesign breaks all of it unless a shim keeps `GetData(Character)` returning a "primary/aggregate" record.
- Avalor also probes `AccessTools.Field(data, "AggressionLevel")` (`AvalorStealthCompat.cs:260`) — **there is no `AggressionLevel` field on `AwarenessData`** (`UnifiedStealthTypes.cs:59-106`); it lives on `CombatDirective` (`:118-126`). That binding is already null on their side. Worth adding the field or telling them.
- Avalor's file header (`AvalorStealthGuards.cs:9-12`) asserts "SoM's `MonsterAI.UpdateAI` postfix calls `StealthBrain.Evaluate` on every AI tick". **That is stale** — SoM's current postfix is apply-only (`MonsterAI_StealthBrain_UpdateAI_Patch.cs:30-36`, `TryGetCachedDecision` then `return` on miss). Their 0.2 s `PinAll` timer is racing something that no longer exists at the frequency they assumed.
- `StealthExemption.IsExempt(Character)` / `IsExempt(BaseAI)` and the ZDO key `"SoMStealthExempt"` (`StealthExemption.cs:5-19`) are probed by name at `AvalorStealthCompat.cs:202-218`; DvergrAllies writes the same key. **Must survive verbatim.** Note the current implementation is `zdo.GetInt(ZDOKey, 0) == 1` — strictly `== 1`, so a tiered API must keep `1` meaning "fully exempt".
- **Exemption coverage gap:** `Character_Damage_Patch` and `Character_OnDamaged_Patch` are the only two patches that never call `StealthExemption.IsExempt`. An exempt Avalor/DvergrAllies mob still gets its `AwarenessData` overwritten by SoM on every hit (`Character_Damage_Patch.cs:30-43`, `Character_OnDamaged_Patch.cs:35-48`), fighting Avalor's pin. **Fix this regardless of the rest of the overhaul — it is a two-line change.**

---

## 10. SMALLER CORRECTNESS NOTES

- `Character_Damage_Patch.cs:30` sets `data.CurrentState = VanillaAlertness.Alerted` **unconditionally**, demoting an already-`Engaged` creature. `Character_OnDamaged_Patch.cs:38-45` does the same thing correctly (max-style). They disagree.
- Hard-coded thresholds `0.50f` (`Character_Damage_Patch.cs:34`), `0.80f` and `0.75f` (`Character_OnDamaged_Patch.cs:35,38`) duplicate ServerSync'd config (`SOMConfig.cs:169,173`). With ServerSync in play these will silently disagree with server-pushed values.
- `MonsterAI_StealthBrain_UpdateAI_Patch.cs:29,44` — bare `try { ... } catch { }` around `BehaviorSystem.ExecuteAction`. A reflection failure inside `SetTarget` becomes permanently invisible.
- `BaseAI_SetMoveDir_Patch` runs at `[HarmonyPriority(int.MinValue)]` so it applies its scale **after** other mods' prefixes — correct intent, but it silently multiplies whatever they set.
- `BaseAI_StealthBrain_IsAlerted_Patch` guards `c.IsDead()`; the three sensing patches do not. A dead-but-not-yet-destroyed creature answers sensing queries from stale data.
- `Humanoid_Attack_Patch.cs:62-63` returns `false` (blocks the attack) when `MonsterAI` is null but the Humanoid passed all earlier guards — a non-MonsterAI `BaseAI` Humanoid is silently disarmed rather than falling through to vanilla. Should be `return true`.
- `Humanoid_Attack_Patch.cs:74` calls `StealthBrain.Evaluate(monster, p, Time.deltaTime)` on a cache miss — a full sensing pass (raycast + four subsystem queries, `SensingEvaluator.cs:10-16`) executed from inside an attack prefix, defeating the Overmind's tiering for exactly the creatures that are in combat.
- `Character_OnDestroy_Patch` clears `AwarenessSystem` but not `StealthBrain._evalCache` — and `StealthBrain.ClearCache(MonsterAI)` exists (`StealthBrain.cs:299-309`) and is never called from anywhere in `Patches/`.

---

## 11. SUMMARY TABLE

| # | Patch class | Target | Kind | Priority | Skips vanilla | Central defect |
|---|---|---|---|---|---|---|
| 1 | `BaseAI_StealthBrain_Patch` | `BaseAI.CanSenseTarget(Character)` | Pre+Post | max / min | **yes** | ignores `target`; single-player state |
| 2 | `BaseAI_CanSeeTarget_Patch` | `BaseAI.CanSeeTarget(Character)` | Pre+Post | max / min | **yes** | ignores `target`; drops ghost/fly + mist checks |
| 3 | `BaseAI_CanHearTarget_Patch` | `BaseAI.CanHearTarget(Character)` | Pre+Post | max / min | **yes** | ignores `target`; drops interior 12 m clamp |
| 4 | `BaseAI_StealthBrain_IsAlerted_Patch` | `BaseAI.IsAlerted()` | Pre+Post | max / min | **yes** | neuters `Alert()`; breaks ZDO `s_alert` replication; 19 downstream readers |
| 5 | `BaseAI_SetMoveDir_Patch` | `Character.SetMoveDir(Vector3)` | Pre (`ref dir`) | min | no | hot path, 4 `GetComponent` before the cheap test |
| 6 | `Humanoid_Attack_Patch` | `Humanoid.StartAttack(Character,bool)` | Pre | **default 400** | **yes (×4)** | gate decided from local-player cache; ambiguous target spec |
| 7 | `MonsterAI_StealthBrain_UpdateAI_Patch` | `MonsterAI.UpdateAI(float)` | Post | min | n/a | retargets every creature to `Player.m_localPlayer`; no sleep guard; `dt` ignored |
| 8 | `Character_Damage_Patch` | `Character.Damage(HitData)` | Post | default | n/a | wrong network side; no tamed/exempt guard; demotes Engaged |
| 9 | `CharacterOnDamagedPatch` | `Character.OnDamaged(HitData)` | Post | default | n/a | **dead for all Humanoids** (override doesn't chain); no tamed/exempt guard |
| 10 | `Character_OnDestroy_Patch` | `Character.OnDestroy()` | Pre | default | no | doesn't clear `StealthBrain._evalCache` |