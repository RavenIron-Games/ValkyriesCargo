I have everything needed. Here is the survey.

---

# SoM ⇄ Vanilla API Survey

**Sources**: `c:/WubarrkCODING/libs-Tools/DECOMPILED ASSEMBLY VALHEIM/assembly_valheim.decompiled.cs` (client, cited as `L####`), `assembly_valheim_SERVER.decompiled.cs` (server, cited as `svrL####`), `c:/WubarrkCODING/libs-Tools/VALHEIM-DEDICATED-SERVER-FACTS.md`.

---

## PART 0 — GROUND TRUTH ON THE REFERENCE ASSEMBLY (read this first; it invalidates an assumption in the brief)

The brief says "given the publicized assembly". I verified this empirically with a compile probe and an MSBuild `ResolveAssemblyReferences` run.

| File | Publicized? | Evidence |
|---|---|---|
| `c:\WubarrkCODING\libs-Tools\assembly_valheim.dll` | **NO** | `csc` probe → `CS0122: 'BaseAI.SetAlerted(bool)' is inaccessible`, `CS0122: BaseAI.MoveTo`, `CS0122: BaseAI.SetTargetInfo`, `CS0122: BaseAI.m_nview`, `CS1061: BaseAI does not contain 'm_alerted'`, `CS1061: MonsterAI ... 'm_targetCreature'`. Identical error set to the stock game DLL. |
| `c:\WubarrkCODING\libs-Tools\assembly_publicizer.dll` | **YES** | Same probe → `EXITCODE=0`. Its **fusion name is `assembly_valheim, Version=0.0.0.0`** — it is a publicized *copy* of assembly_valheim under a misleading filename. |
| `...\Valheim\valheim_Data\Managed\assembly_valheim.dll` | NO (stock) | Same CS0122/CS1061 set. |

The csproj (`c:/WubarrkCODING/ShadowsOfMidgard/ShadowsOfMidgard.csproj` L≈110-125) references **both**, and they share one assembly identity. Direct `csc` with both errors out:

```
error CS1704: An assembly with the same simple name 'assembly_valheim, Version=0.0.0.0, ...
              has already been imported.
```

MSBuild's RAR silently de-duplicates by fusion name and the **publicized one wins**:

```
ReferencePath=
    c:\WubarrkCODING\libs-Tools\assembly_publicizer.dll
            FusionName=assembly_valheim, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
            OriginalItemSpec=assembly_publicizer
```

`assembly_valheim.dll` never reaches the compiler. **Consequences the architects must act on:**

1. SoM *is* compiling against a publicized assembly today — but only by an undocumented RAR tie-break, not by design. If RAR ever picks the other one (item reordering, an SDK change, a `<Private>` tweak) the build breaks with a wall of CS0122 and the fix will look inexplicable.
2. **Action**: delete the `assembly_valheim` `<Reference>` from the csproj and keep only `assembly_publicizer`, or rename the file on disk to something honest (`assembly_valheim_publicized.dll`) and reference exactly one. This is a five-minute fix that removes a class of "works on my machine" failures.
3. Because the publicized assembly *is* what compiles, **every reflection binding in `Systems/BehaviorSystem.cs` is unnecessary** — see PART 1.

---

## PART 1 — MEMBER-BY-MEMBER CATALOGUE

Legend for "SoM reach": `direct` = plain C# call; `harmony` = patched; `reflect` = runtime reflection; `—` = unused.

### 1.1 `BaseAI` (`L3809`, `public class BaseAI : MonoBehaviour, IUpdateAI`)

#### Sensing trio

```csharp
// L4519
public bool CanSenseTarget(Character target)
{
    return CanSenseTarget(target, m_passiveAggresive);
}

// L4524
public bool CanSenseTarget(Character target, bool passiveAggresive)
{
    return CanSenseTarget(base.transform, m_character.m_eye.position, m_hearRange, m_viewRange,
                          m_viewAngle, IsAlerted(), m_mistVision, target, passiveAggresive,
                          m_character.IsTamed());
}

// L4529
public static bool CanSenseTarget(Transform me, Vector3 eyePoint, float hearRange, float viewRange,
                                  float viewAngle, bool alerted, bool mistVision, Character target,
                                  bool passiveAggresive, bool isTamed)
{
    if (!passiveAggresive && ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs)
        && (!isTamed || !target.GetBaseAI().IsAlerted())) return false;
    if (CanHearTarget(me, hearRange, target)) return true;
    if (CanSeeTarget(me, eyePoint, viewRange, viewAngle, alerted, mistVision, target)) return true;
    return false;
}
```

```csharp
// L4546
public bool CanHearTarget(Character target) => CanHearTarget(base.transform, m_hearRange, target);

// L4551
public static bool CanHearTarget(Transform me, float hearRange, Character target)
{
    if (target.IsPlayer()) {
        Player player = target as Player;
        if (player.InDebugFlyMode() || player.InGhostMode()) return false;
    }
    float num = Vector3.Distance(target.transform.position, me.position);
    if (Character.InInterior(me)) hearRange = Mathf.Min(12f, hearRange);   // m_interiorMaxHearRange
    if (num > hearRange) return false;
    if (num < target.GetNoiseRange()) return true;
    return false;
}
```

```csharp
// L4577
public bool CanSeeTarget(Character target)
{
    return CanSeeTarget(base.transform, m_character.m_eye.position, m_viewRange, m_viewAngle,
                        IsAlerted(), m_mistVision, target);
}

// L4582
public static bool CanSeeTarget(Transform me, Vector3 eyePoint, float viewRange, float viewAngle,
                                bool alerted, bool mistVision, Character target)
{
    if (target == null || me == null) return false;
    if (target.IsPlayer()) {
        Player player = target as Player;
        if (player.InDebugFlyMode() || player.InGhostMode()) return false;
    }
    float num = Vector3.Distance(target.transform.position, me.position);
    if (num > viewRange) return false;
    _ = num / viewRange;
    float stealthFactor = target.GetStealthFactor();
    float num2 = viewRange * stealthFactor;
    if (num > num2) return false;
    if (!alerted && Vector3.Angle(target.transform.position - me.position, me.forward) > viewAngle) return false;
    Vector3 vector = (target.IsCrouching() ? target.GetCenterPoint() : target.m_eye.position);
    Vector3 vector2 = vector - eyePoint;
    if (Physics.Raycast(eyePoint, vector2.normalized, vector2.magnitude, m_viewBlockMask)) return false;
    if (!mistVision && ParticleMist.IsMistBlocked(eyePoint, vector)) return false;
    return true;
}

// L4625 — StaticTarget overload, PROTECTED, different accessibility from the Character one
protected bool CanSeeTarget(StaticTarget target)
```

| Member | Visibility | Line | SoM reach | Reflection needed? |
|---|---|---|---|---|
| `CanSenseTarget(Character)` | **public** | 4519 | harmony (`Patches/BaseAI_StealthBrain_Patch.cs:7`) | No, and none used |
| `CanSenseTarget(Character,bool)` | **public** | 4524 | — | n/a |
| `CanSenseTarget(Transform,…,bool)` static 10-arg | **public static** | 4529 | — | n/a — **this is the real workhorse**; `FindClosestCreature` calls it directly (L5266), bypassing SoM's instance-method patch entirely |
| `CanHearTarget(Character)` | **public** | 4546 | harmony (`BaseAI_CanHearTarget_Patch.cs:12`) | No |
| `CanHearTarget(Transform,float,Character)` | **public static** | 4551 | — | n/a |
| `CanSeeTarget(Character)` | **public** | 4577 | harmony (`BaseAI_CanSeeTarget_Patch.cs:12`) | No |
| `CanSeeTarget(Transform,…,Character)` | **public static** | 4582 | — | n/a |
| `CanSeeTarget(StaticTarget)` | **protected** | 4625 | — | would need reflection or publicized asm |

**Critical for the multiplayer brief:** all three instance overloads take an explicit `Character target`. Vanilla always passes the *specific* creature being evaluated (`FindEnemy` L5203 passes `item`; `UpdateTarget` L5920-5921 passes `m_targetCreature`; `RPC_Damage` L8736 passes `attacker`). The API is already per-target. SoM's patches discard the `target` argument (`BaseAI_CanSeeTarget_Patch.cs:37` returns `data.CanSee` from a single per-creature `AwarenessData`). **The vanilla API imposes no obstacle to per-player tracks — the bug is purely on SoM's side.**

#### Alert state

```csharp
private bool m_alerted;                             // L3969  — PRIVATE

// L5327
public void Alert()
{
    if (m_nview.IsValid() && !IsAlerted())
    {
        if (m_nview.IsOwner()) SetAlerted(alert: true);
        else                   m_nview.InvokeRPC("Alert");
    }
}

// L5342
private void RPC_Alert(long sender) { if (m_nview.IsOwner()) SetAlerted(alert: true); }

// L5350
protected virtual void SetAlerted(bool alert)
{
    if (m_alerted != alert)
    {
        m_alerted = alert;
        m_animator.SetBool("alert", m_alerted);
        if (m_nview.IsOwner()) m_nview.GetZDO().Set(ZDOVars.s_alert, m_alerted);
        if (m_alerted) m_alertedEffects.Create(base.transform.position, Quaternion.identity);
        if (m_character.IsBoss() && !m_nview.GetZDO().GetBool("bosscount")) { … }
        if (alert && m_alertedMessage.Length > 0 && !m_nview.GetZDO().GetBool(ZDOVars.s_shownAlertMessage))
        { … MessageHud.instance.MessageAll(…); }
    }
}

// L5448
public bool IsAlerted() { return m_alerted; }
```

Overrides: `MonsterAI.SetAlerted` L6513 (zeroes `m_timeSinceSensedTargetCreature` on alert, then `base.SetAlerted`); `AnimalAI.SetAlerted` L3800 (zeroes `m_inDangerTimer`, then base).

| Member | Visibility | Line | SoM reach | Reflection needed? |
|---|---|---|---|---|
| `m_alerted` | **private** field | 3969 | — | yes, if ever touched |
| `Alert()` | **public** | 5327 | — | No. **Has a built-in non-owner RPC fallback — this is the vanilla-blessed cross-peer alert channel and SoM does not use it.** |
| `RPC_Alert(long)` | private | 5342 | — | registered under the name `"Alert"` at L4040 |
| `SetAlerted(bool)` | **protected virtual** | 5350 | — | yes (or publicized asm). **Not currently reached at all** — SoM only fakes `IsAlerted()`, so the ZDO `s_alert` value and `"alert"` animator bool never reflect SoM's state |
| `IsAlerted()` | **public** | 5448 | harmony (`BaseAI_StealthBrain_IsAlerted_Patch.cs:6`) | No |

`IsAlerted()` is consumed in ~18 places, several of which SoM's patch silently repoints: `CanSeeTarget`'s FOV bypass (L4608 via L4579), `MoveTo` run-flag (L4383, 4337, 6112, 6138, 6177, 6204), `InStealthRange` (L5390 → drives the player's Sneak skill XP at L21801), and — importantly — **backstab damage**:

```csharp
// L8736, inside RPC_Damage
if (m_baseAI != null && !m_baseAI.IsAlerted() && hit.m_backstabBonus > 1f
    && Time.time - m_backstabTime > 300f
    && (!ZoneSystem.instance.GetGlobalKey(GlobalKeys.PassiveMobs) || !m_baseAI.CanSeeTarget(attacker)))
{ m_backstabTime = Time.time; hit.ApplyModifier(hit.m_backstabBonus); … }
```

Note `CanSeeTarget(attacker)` here — a *per-target* question asked about a specific attacker, answered by SoM with the local player's flag.

#### Target info / movement / update

```csharp
// L5453
protected void SetTargetInfo(ZDOID targetID)
{
    m_nview.GetZDO().Set(ZDOVars.s_haveTargetHash, !targetID.IsNone());
}

// L5458 — note: only a BOOL survives replication. The ZDOID is discarded.
public bool HaveTarget()
{
    if (!m_nview.IsValid()) return false;
    return m_nview.GetZDO().GetBool(ZDOVars.s_haveTargetHash);
}

// L4771
protected bool MoveTo(float dt, Vector3 point, float dist, bool run)
{
    if (m_character.m_flying) { … return MoveAndAvoid(dt, point, dist, run); }
    float num = (run ? 1f : 0.5f);
    if (m_serpentMovement) num = 3f;
    if (Utils.DistanceXZ(point, base.transform.position) < Mathf.Max(dist, num)) { StopMoving(); return true; }
    if (!FindPath(point))    { StopMoving(); return true; }
    if (m_path.Count == 0)   { StopMoving(); return true; }
    Vector3 vector = m_path[0];
    …
    MoveTowards(normalized2, run);
    return false;
}

// L4672
public void MoveTowards(Vector3 dir, bool run)   // ← PUBLIC; calls m_character.SetMoveDir at L4682/4692

// L4107
public virtual bool UpdateAI(float dt)
{
    if (!m_nview.IsValid()) return false;
    if (!m_nview.IsOwner())
    {
        m_alerted = m_nview.GetZDO().GetBool(ZDOVars.s_alert);   // ← THE non-owner path, whole body
        return false;
    }
    UpdateTakeoffLanding(dt);
    if (m_jumpInterval > 0f) m_jumpTimer += dt;
    if (m_randomMoveUpdateTimer > 0f) m_randomMoveUpdateTimer -= dt;
    UpdateRegeneration(dt);
    m_timeSinceHurt += dt;
    return true;
}
```

| Member | Visibility | Line | SoM reach | Reflection needed? |
|---|---|---|---|---|
| `SetTargetInfo(ZDOID)` | **protected** | 5453 | **reflect** — `BehaviorSystem.cs:29-34`, `AccessTools.Method(typeof(BaseAI),"SetTargetInfo",new[]{typeof(ZDOID)})` → `Delegate.CreateDelegate` | **NO — publicized asm exposes it. Delete the reflection.** |
| `HaveTarget()` | **public** | 5458 | — | No |
| `MoveTo(float,Vector3,float,bool)` | **protected** | 4771 | **reflect** — `BehaviorSystem.cs:49-54` | **NO — publicized. Delete.** |
| `MoveTowards(Vector3,bool)` | **public** | 4672 | — | No |
| `UpdateAI(float)` | **public virtual** | 4107 | harmony indirectly (SoM patches `MonsterAI.UpdateAI`) | No |
| `m_viewRange` (=50f) | **public** field | 3844 | — | No |
| `m_viewAngle` (=90f) | **public** field | 3846 | — | No |
| `m_hearRange` (=9999f) | **public** field | 3848 | — | No |
| `m_mistVision` | **public** field | 3850 | — | No |
| `m_nview` | **protected** field | 3941 | — (`AIAuthority.cs:14` deliberately uses `GetComponent<ZNetView>()` instead) | No, if publicized |
| `m_character` | **protected** field | 3943 | — | No, if publicized |
| `m_spawnPoint` | **protected** field | 3977 | — | No, if publicized |
| `m_passiveAggresive` | **public** field | 3920 | — | No |
| `m_aggravatable` | **public** field | 3918 | — | No |

#### Static rosters / helpers

```csharp
private static List<BaseAI> m_instances = new List<BaseAI>();                      // L4007 private static
public static List<IUpdateAI> Instances { get; } = new List<IUpdateAI>();          // L4009 PUBLIC static prop
public static List<BaseAI> BaseAIInstances { get; } = new List<BaseAI>();          // L4011 PUBLIC static prop
public static List<BaseAI> GetAllInstances() { return m_instances; }               // L5476 public static

public static bool InStealthRange(Character me)                                    // L5378
public static bool HaveEnemyInRange(Character me, Vector3 point, float range)       // L5400
public static Character FindClosestEnemy(Character me, Vector3 point, float maxD)   // L5412
public static Character FindRandomEnemy(Character me, Vector3 point, float maxD)    // L5431
public bool  IsEnemy(Character other)                                              // L4990 public
public static bool IsEnemy(Character a, Character b)                               // L4995 public static
public virtual bool IsSleeping() { return false; }                                 // L5508 public virtual
```

Lifecycle: `m_instances.Add` in `Awake` L4015 / `Remove` in `OnDestroy` L4058 (**private** `OnDestroy`, L4056 — includes disabled objects). `Instances`/`BaseAIInstances` are `Add`ed in `OnEnable` L4063-4064 and `Remove`d in `OnDisable` L4069-4070 (enabled-only). For an Overmind iterating creatures, **`BaseAI.BaseAIInstances` is the correct public, allocation-free roster**.

#### `BaseAI.Awake` — the delegate hooks that matter (L4013-4054)

```csharp
Character character = m_character;
character.m_onDamaged = (Action<float, Character>)Delegate.Combine(
    character.m_onDamaged, new Action<float, Character>(OnDamaged));          // L4028
Character character2 = m_character;
character2.m_onDeath = (Action)Delegate.Combine(character2.m_onDeath, new Action(OnDeath));  // L4030
…
m_nview.Register("Alert", RPC_Alert);                                          // L4040
m_nview.Register<Vector3, float, ZDOID>("OnNearProjectileHit", RPC_OnNearProjectileHit);  // L4041
m_nview.Register<bool, int>("SetAggravated", RPC_SetAggravated);               // L4042
```

`protected virtual void OnDamaged(float damage, Character attacker) { m_timeSinceHurt = 0f; }` — **L4506**, distinct from `Character.OnDamaged(HitData)`. Overridden by `MonsterAI` L5797 and `AnimalAI` L3736.

---

### 1.2 `MonsterAI` (`L5620`, `public class MonsterAI : BaseAI`)

```csharp
private Character  m_targetCreature;              // L5727  PRIVATE
private Vector3    m_lastKnownTargetPos = Vector3.zero;   // L5729 PRIVATE
private bool       m_beenAtLastPos;               // L5731  PRIVATE
private StaticTarget m_targetStatic;              // L5733  PRIVATE
private float      m_timeSinceAttacking;          // L5735  PRIVATE
private float      m_timeSinceSensedTargetCreature; // L5737 PRIVATE
private float      m_updateTargetTimer;           // L5739  PRIVATE

public float m_alertRange = 9999f;                // L5641  public
public float m_maxChaseDistance;                  // L5675  public
public bool  m_enableHuntPlayer;                  // L5665  public
public bool  m_attackPlayerObjects = true;        // L5667  public
public bool  m_sleeping;                          // L5687  public
public float m_wakeupRange = 5f;                  // L5689  public

private const float m_giveUpTime = 30f;                 // L5628
private const float m_updateTargetFarRange = 50f;       // L5630
private const float m_updateTargetIntervalNear = 2f;    // L5632
private const float m_updateTargetIntervalFar = 6f;     // L5634
private const float m_unableToAttackTargetDuration = 15f; // L5638
```

```csharp
// L5846 — PRIVATE, the heart of target acquisition
private void UpdateTarget(Humanoid humanoid, float dt, out bool canHearTarget, out bool canSeeTarget)
{
    m_unableToAttackTargetTimer -= dt;
    m_updateTargetTimer -= dt;
    if (m_updateTargetTimer <= 0f && !m_character.InAttack())
    {
        bool flag = Player.IsPlayerInRange(base.transform.position, 50f);
        m_updateTargetTimer = (flag ? 2f : 6f);
        Character character = FindEnemy();
        if ((bool)character) { m_targetCreature = character; m_targetStatic = null; }
        …
    }
    …
    canHearTarget = false;
    canSeeTarget  = false;
    if ((bool)m_targetCreature)
    {
        canHearTarget = CanHearTarget(m_targetCreature);
        canSeeTarget  = CanSeeTarget(m_targetCreature);
        if (canSeeTarget | canHearTarget) m_timeSinceSensedTargetCreature = 0f;
        if (m_targetCreature.IsPlayer()) m_targetCreature.OnTargeted(canSeeTarget | canHearTarget, IsAlerted());
        SetTargetInfo(m_targetCreature.GetZDOID());
    }
    else SetTargetInfo(ZDOID.None);
    m_timeSinceSensedTargetCreature += dt;
    if (IsAlerted() || m_targetCreature != null)
    {
        m_timeSinceAttacking += dt;
        float num = 60f;
        float num2 = Vector3.Distance(m_spawnPoint, base.transform.position);
        bool flag5 = HuntPlayer() && (bool)m_targetCreature && m_targetCreature.IsPlayer();
        if (m_timeSinceSensedTargetCreature > 30f
            || (!flag5 && (m_timeSinceAttacking > num
                || (m_maxChaseDistance > 0f && m_timeSinceSensedTargetCreature > 1f && num2 > m_maxChaseDistance))))
        {
            SetAlerted(alert: false);
            m_targetCreature = null; m_targetStatic = null;
            m_timeSinceAttacking = 0f; m_updateTargetTimer = 5f;
        }
    }
}
```

```csharp
// L5191 — PROTECTED, on BaseAI (not MonsterAI); the O(n) scan over EVERY character
protected Character FindEnemy()
{
    List<Character> allCharacters = Character.GetAllCharacters();
    Character character = null;
    float num = 99999f;
    foreach (Character item in allCharacters)
    {
        if (!IsEnemy(m_character, item) || item.IsDead() || item.m_aiSkipTarget) continue;
        BaseAI baseAI = item.GetBaseAI();
        if ((!(baseAI != null) || !baseAI.IsSleeping()) && CanSenseTarget(item))
        {
            float num2 = Vector3.Distance(item.transform.position, base.transform.position);
            if (num2 < num || character == null) { character = item; num = num2; }
        }
    }
    if (character == null && HuntPlayer())
    {
        Player closestPlayer = Player.GetClosestPlayer(base.transform.position, 200f);
        if ((bool)closestPlayer && (closestPlayer.InDebugFlyMode() || closestPlayer.InGhostMode())) return null;
        return closestPlayer;
    }
    return character;
}

// L5225 — PUBLIC STATIC; calls the 10-arg static CanSenseTarget at L5266, NOT the instance one
public static Character FindClosestCreature(Transform me, Vector3 eyePoint, float hearRange,
    float viewRange, float viewAngle, bool alerted, bool mistVision, bool passiveAggresive,
    bool includePlayers = true, bool includeTamed = true, bool includeEnemies = true,
    List<Character> onlyTargets = null)
```

```csharp
// L5805 — PRIVATE
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

// L5797 — PROTECTED OVERRIDE, called via Character.m_onDamaged
protected override void OnDamaged(float damage, Character attacker)
{
    base.OnDamaged(damage, attacker);
    Wakeup();
    SetAlerted(alert: true);
    SetTarget(attacker);
}

// L6401 — PUBLIC OVERRIDE. The ONLY sanctioned read of m_targetCreature.
public override Character GetTargetCreature() { return m_targetCreature; }
// L5562 — base: public virtual Character GetTargetCreature() { return null; }
// L6406
public StaticTarget GetStaticTarget() { return m_targetStatic; }
```

```csharp
// L5954 — PUBLIC OVERRIDE
public override bool UpdateAI(float dt)
{
    if (!base.UpdateAI(dt)) return false;          // ← owner gate lives in base
    UpdateSleep(dt);
    if (IsSleeping()) return true;
    Humanoid humanoid = m_character as Humanoid;
    if (HuntPlayer()) SetAlerted(alert: true);
    UpdateTarget(humanoid, dt, out var canHearTarget, out var canSeeTarget);
    …
}
```

| Member | Visibility | Line | SoM reach | Reflection needed? |
|---|---|---|---|---|
| `m_targetCreature` | **private** field on **MonsterAI** | 5727 | **reflect (BROKEN — see PART 4.1)** `BehaviorSystem.cs:68` | **NO — publicized. And the current binding names the wrong declaring type.** |
| `m_lastKnownTargetPos` | **private** | 5729 | — | No, if publicized |
| `m_targetStatic` | **private** | 5733 | — | No, if publicized |
| `m_timeSinceSensedTargetCreature` | **private** | 5737 | — | No, if publicized. **Directly drives the 30 s give-up at L5943 — the field SoM must own to control leashing** |
| `m_updateTargetTimer` | **private** | 5739 | — | No, if publicized. **The natural per-creature LOD throttle knob** |
| `UpdateTarget(Humanoid,float,out,out)` | **private** | 5846 | — | yes/publicized. Patchable by name |
| `UpdateAI(float)` | **public override** | 5954 | harmony Postfix (`MonsterAI_StealthBrain_UpdateAI_Patch.cs:6`) | No |
| `FindEnemy()` | **protected** (declared on **BaseAI**) | 5191 | — | yes/publicized |
| `FindClosestCreature(...)` | **public static** | 5225 | — | No |
| `SetTarget(Character)` | **private** | 5805 | — | yes/publicized |
| `GetTargetCreature()` | **public override** | 6401 | direct (`Humanoid_Attack_Patch.cs:66`) | No |
| `GetStaticTarget()` | **public** | 6406 | — | No |
| `SetHuntPlayer(bool)` / `HuntPlayer()` | **public** / **public virtual** | 5279 / 5291, override 6522 | — | No |
| `IsSleeping()` | **public override** | 6508 | direct (`BehaviorSystem.cs:191`) | No |
| `OnDamaged(float,Character)` | **protected override** | 5797 | — | yes/publicized |

---

### 1.3 `AnimalAI` (`L3714`) — the blind spot

SoM patches only `MonsterAI.UpdateAI` and gates every sensing patch on `__instance is MonsterAI`. `AnimalAI` is a **sibling**, not a subclass, of `MonsterAI`. It has its own private `m_target` (L3724), its own `UpdateAI` (L3742), and calls `CanSenseTarget(m_target)` at L3768 and `SetAlerted(true)` at L3772. Boar/deer/etc. therefore run pure-vanilla stealth. Worth an explicit decision in v2 (support or documented non-goal).

---

### 1.4 `Character` (`L6814`, `public class Character : MonoBehaviour, IDestructible, Hoverable, IWaterInteractable, IMonoUpdater`)

```csharp
public Action<float, Character> m_onDamaged;   // L6875  PUBLIC delegate
public Action                    m_onDeath;    // L6877  PUBLIC delegate
public bool   m_aiSkipTarget;                  // L6899  PUBLIC
public Transform m_eye;                        // L6951  PUBLIC
private static readonly List<Character> s_characters = new List<Character>();  // L7264 PRIVATE
public static List<IMonoUpdater> Instances { get; } = new List<IMonoUpdater>();// L7302 PUBLIC (IMonoUpdater, not Character)
```

```csharp
protected virtual void Awake() { s_characters.Add(this); … }                    // L7304-7306

protected virtual void OnDestroy()                                             // L7390
{
    m_seman.OnDestroy();
    s_characters.Remove(this);
    if (EnemyHud.instance != null) EnemyHud.instance.RemoveCharacterHud(this);
}

public virtual bool IsPlayer() { return false; }                               // L7419

public Vector3 GetEyePoint()   { return m_eye.position; }                      // L8652
public Vector3 GetCenterPoint(){ return m_collider.bounds.center; }            // L8657

public void Damage(HitData hit)                                                // L8692
{
    if (m_nview.IsValid())
    {
        hit.m_weakSpot = FindWeakSpotIndex(hit.m_hitCollider);
        m_nview.InvokeRPC("RPC_Damage", hit);      // ← fires the RPC; does NOT apply damage locally
    }
}

private void RPC_Damage(long sender, HitData hit)                              // L8701
{
    …
    if (!m_nview.IsOwner()) return;                                            // L8712  OWNER GATE
    …
    OnDamaged(hit);                                                            // L8866
    …
    if (m_onDamaged != null) m_onDamaged(totalDamage2, hit.GetAttacker());     // L8871-8873
}

protected virtual void OnDamaged(HitData hit) { }                              // L9103  EMPTY

public void SetMoveDir(Vector3 dir) { m_moveDir = dir; }                       // L9503
public Vector3 GetMoveDir() { return m_moveDir; }                              // L10583

public bool IsOwner()  { … }                                                   // L9698
public long GetOwner() { … }                                                   // L9707

public ZDOID GetZDOID()                                                        // L9689
{
    if (!m_nview.IsValid()) return ZDOID.None;
    return m_nview.GetZDO().m_uid;
}

public virtual bool StartAttack(Character target, bool charge) { return false; }// L9675
public virtual bool IsCrouching() { return false; }                            // L9755
public bool IsBoss()                                                           // L9428

public virtual float GetStealthFactor() { return 1f; }                         // L10094
public void AddNoise(float range)                                              // L10110  owner-or-RPC
public float GetNoiseRange()                                                   // L10132
{
    if (!m_nview.IsValid()) return 0f;
    if (m_nview.IsOwner()) return m_noiseRange;
    return m_nview.GetZDO().GetFloat(ZDOVars.s_noise);                          // ← replicated
}

public static List<Character> GetAllCharacters() { return s_characters; }      // L10316
public static bool IsCharacterInRange(Vector3 point, float range)              // L10321
public virtual void OnTargeted(bool sensed, bool alerted) { }                  // L10333
public BaseAI GetBaseAI() { return m_baseAI; }                                 // L10587
public bool IsTamed() { return IsTamed(Time.time); }                           // L10644
```

| Member | Visibility | Line | SoM reach | Reflection needed? |
|---|---|---|---|---|
| `SetMoveDir(Vector3)` | **public** | 9503 | harmony Prefix (`BaseAI_SetMoveDir_Patch.cs:6`) | No |
| `GetEyePoint()` | **public** | 8652 | — (SoM uses `target.m_eye.position` at `AwarenessSystem.cs:49`) | No |
| `GetCenterPoint()` | **public** | 8657 | direct (`AwarenessSystem.cs:49` fallback) | No |
| `m_eye` | **public** field | 6951 | direct (`AwarenessSystem.cs:49`) | No |
| `IsCrouching()` | **public virtual** | 9755 (Player override L21129) | — | No |
| `IsTamed()` | **public** | 10644 | direct (all patches) | No |
| `IsPlayer()` | **public virtual** | 7419 | direct (all patches) | No |
| `GetAllCharacters()` | **public static** | 10316 | — | No |
| `OnDamaged(HitData)` | **protected virtual** | 9103 | harmony Postfix (`Character_OnDamaged_Patch.cs:10`) — **misfires, see PART 4.2** | patched by name; works but is the wrong hook |
| `Damage(HitData)` | **public** | 8692 | harmony Postfix (`Character_Damage_Patch.cs:6`) — **misfires, see PART 4.3** | No |
| `OnDestroy()` | **protected virtual** | 7390 | harmony Prefix (`Character_OnDestroy_Patch.cs:5`) — fires OK, see PART 4.4 | patched by name |
| `m_onDamaged` | **public** delegate | 6875 | — | No. **The correct damage hook** — it fires on the owner for every Character type |
| `m_onDeath` | **public** delegate | 6877 | — | No |
| `GetZDOID()` | **public** | 9689 | — | No |
| `GetStealthFactor()` | **public virtual** | 10094 (Player override L21842) | — | No |
| `GetNoiseRange()` | **public** | 10132 | — | No. Already replicated via `ZDOVars.s_noise` |
| `GetBaseAI()` | **public** | 10587 | — | No |
| `OnTargeted(bool,bool)` | **public virtual** | 10333 (Player override L21627) | — | No |
| `m_aiSkipTarget` | **public** field | 6899 | — | No. **A free, vanilla-native way to exclude a character from `FindEnemy` (L5198)** |

**`Player.GetStealthFactor` — the value SoM competes with:**

```csharp
// L21812 — Player.UpdateStealth, runs every 0.5 s
private void UpdateStealth(float dt)
{
    m_stealthFactorUpdateTimer += dt;
    if (m_stealthFactorUpdateTimer > 0.5f)
    {
        m_stealthFactorUpdateTimer = 0f;
        m_stealthFactorTarget = 0f;
        if (IsCrouching())
        {
            m_lastStealthPosition = base.transform.position;
            float skillFactor = m_skills.GetSkillFactor(Skills.SkillType.Sneak);
            float lightFactor = StealthSystem.instance.GetLightFactor(GetCenterPoint());
            m_stealthFactorTarget = Mathf.Lerp(0.5f + lightFactor * 0.5f, 0.2f + lightFactor * 0.4f, skillFactor);
            m_stealthFactorTarget = Mathf.Clamp01(m_stealthFactorTarget);
            m_seman.ModifyStealth(m_stealthFactorTarget, ref m_stealthFactorTarget);
            m_stealthFactorTarget = Mathf.Clamp01(m_stealthFactorTarget);
        }
        else m_stealthFactorTarget = 1f;
    }
    float num = Mathf.MoveTowards(m_stealthFactor, m_stealthFactorTarget, dt / 4f);
    if (!m_stealthFactor.Equals(num)) m_nview.GetZDO().Set(ZDOVars.s_stealth, num);
    m_stealthFactor = num;
}

// L21842
public override float GetStealthFactor()
{
    if (!m_nview.IsValid()) return 0f;
    if (m_nview.IsOwner()) return m_stealthFactor;
    return m_nview.GetZDO().GetFloat(ZDOVars.s_stealth);   // ← REPLICATED, readable by every peer
}
```

**This is the single most important multiplayer fact in the survey**: a player's stealth factor is already replicated per-player via `ZDOVars.s_stealth`, and `GetNoiseRange()` likewise via `ZDOVars.s_noise`. A creature-owning client can read *every* nearby player's true stealth and noise without owning that player. Per-player detection tracks are fully implementable from replicated state with zero new RPCs.

---

### 1.5 `Humanoid` (`L12806`) / `Player` (`L15320`)

```csharp
// L13073 — Humanoid override; param is named `secondaryAttack`, NOT `charge`
public override bool StartAttack(Character target, bool secondaryAttack)
{
    if ((InAttack() && !HaveQueuedChain()) || InDodge() || !CanMove() || IsKnockedBack()
        || IsStaggering() || InMinorAction()) return false;
    ItemDrop.ItemData currentWeapon = GetCurrentWeapon();
    if (currentWeapon == null) return false;
    if (secondaryAttack && !currentWeapon.HaveSecondaryAttack()) return false;
    if (!secondaryAttack && !currentWeapon.HavePrimaryAttack())  return false;
    …
}

// L12969
protected override void OnDestroy() { base.OnDestroy(); }          // ← DOES chain

// L13249
protected override void OnDamaged(HitData hit) { SetCrouch(crouch: false); }   // ← does NOT chain

// L15457
public static Player m_localPlayer = null;                          // PUBLIC STATIC FIELD
// L15459
private static readonly List<Player> s_players = new List<Player>();// PRIVATE
// L20441
public static List<Player> GetAllPlayers() { return s_players; }    // PUBLIC STATIC
// L20336
public static Player GetClosestPlayer(Vector3 point, float maxRange)
// L20399 / L20352 / L20411
public static bool IsPlayerInRange(Vector3 point, float range)
public static bool IsPlayerInRange(Vector3 point, float range, long playerID)
public static bool IsPlayerInRange(Vector3 point, float range, float minNoise)
// L20388
public static void GetPlayersInRange(Vector3 point, float range, List<Player> players)  // ← NON-ALLOCATING
// L21129
public override bool IsCrouching() { return GetCurrentAnimHash() == s_animatorTagCrouch; }
// L21627
public override void OnTargeted(bool sensed, bool alerted)  // rate-limited to 0.5 s, InvokeRPC("OnTargeted", …)
```

Roster lifecycle: `s_players.Add` L15882 (`Player.Awake`), `s_players.Remove` L16029 (`Player.OnDestroy`).

| Member | Visibility | Line | SoM reach | Reflection needed? |
|---|---|---|---|---|
| `Humanoid.StartAttack(Character,bool)` | **public override** | 13073 | harmony Prefix (`Humanoid_Attack_Patch.cs:7`) | No. Patch targets `typeof(Humanoid)` so it hits the override — correct |
| `Player.m_localPlayer` | **public static field** | 15457 | direct (`MonsterAI_StealthBrain_UpdateAI_Patch.cs:25`) — **the central single-player assumption** | No |
| `Player.GetAllPlayers()` | **public static** | 20441 | — | No. Per server-facts L20: local-instance list only |
| `Player.GetPlayersInRange(...)` | **public static** | 20388 | — | No. **Use this for the per-player track loop — it fills a caller-owned list** |
| `Player.GetClosestPlayer(...)` | **public static** | 20336 | — | No |

---

### 1.6 `ZNetView` (`L70199`), `ZDO` (`L62124`), `ZDOMan` (`L64722`), `ZNet` (`L66833`)

```csharp
// ZNetView
public bool IsOwner()   { if (!IsValid()) return false; return m_zdo.IsOwner(); }   // L70424 public
public bool HasOwner()  { if (!IsValid()) return false; return m_zdo.HasOwner(); }  // L70433 public
public void ClaimOwnership() { if (!IsOwner()) m_zdo.SetOwner(ZDOMan.GetSessionID()); } // L70442 public
public ZDO  GetZDO()    { return m_zdo; }                                           // L70450 public
public bool IsValid()   { if (m_zdo != null) return m_zdo.IsValid(); return false; }// L70455 public
private ZDO m_zdo;                                                                  // L70219 PRIVATE
public bool m_persistent;                                                           // L70205 public
public static long Everybody = 0L;                                                  // L70203 public static
```

```csharp
// ZDO
public ZDOID m_uid = ZDOID.None;                                    // L62147  PUBLIC FIELD
public bool  IsValid() { return Valid; }                            // L62346  public
public long  GetOwner(){ if (!Owned) return 0L; return ZDOExtraData.GetOwner(m_uid); } // L63608 public
public bool  IsOwner() { return Owner; }                            // L63617  public
public bool  HasOwner(){ return Owned; }                            // L63622  public
public void  SetOwner(long uid)                                     // L63627  public
public uint  DataRevision  { get; set; }                            // L62322  public prop
public void  SetPosition(Vector3 pos)                               // L62542  public
public Vector3 GetPosition()                                        // L62625
public Quaternion GetRotation()                                     // L62630

public void Set(string name, float value)   { Set(name.GetStableHashCode(), value); }   // L62417
public void Set(int hash,  float value)     { if (ZDOExtraData.Set(m_uid,hash,value)) IncreaseDataRevision(); } // L62422
public void Set(string name, Vector3 value) // L62430   /  public void Set(int hash, Vector3 value) // L62435
public void Set(string name, int value)     // L62464
public void Set(int hash, int value, bool okForNotOwner = false)                        // L62469
{ if (ZDOExtraData.Set(m_uid, hash, value)) IncreaseDataRevision(); }   // ← okForNotOwner is UNUSED in release
public void Set(string name, bool value)    // L62493  /  public void Set(int hash, bool value) => Set(hash, value?1:0) // L62498
public void Set(string name, long value)    // L62503  /  public void Set(int hash, long value)  // L62508
public void Set(string name, string value)  // L62529  /  public void Set(int hash, string value)// L62534
public void Set(string name, byte[] bytes)  // L62516
public void Set(string name, ZDOID id)      // L62385

public float   GetFloat(string name, float defaultValue = 0f)   // L62653  (+ int-hash and out-bool forms)
public Vector3 GetVec3(string name, Vector3 defaultValue)       // L62673  (+ hash / out forms)
public int     GetInt(string name, int defaultValue = 0)        // L62713  (+ hash / out forms)
public bool    GetBool(string name, bool defaultValue = false)  // L62733  → GetInt(...) != 0
public long    GetLong(string name, long defaultValue = 0L)     // L62753  (+ hash / out forms)
public string  GetString(string name, string defaultValue = "") // L62763
public ZDOID   GetZDOID(string name)                            // L62401

private void IncreaseDataRevision()                             // L62635  PRIVATE
{
    DataRevision++;
    if (!ZNet.instance.IsServer()) ZDOMan.instance.ClientChanged(m_uid);
}
```

```csharp
// ZDOMan
public static ZDOMan instance => s_instance;                    // L64848  public static prop
public static long GetSessionID() { return s_instance.m_sessionID; }  // L65750 PUBLIC STATIC
public ZDO GetZDO(ZDOID id)                                     // L65184  public
public void ClientChanged(ZDOID id) { m_clientChangeQueue.Add(id); }  // L65999 public
```

```csharp
// ZNet
public static ZNet instance => m_instance;                      // L67047  public static prop
private static bool m_isServer = true;                          // L66995
public bool IsServer() { return m_isServer; }                   // L68808  public
public bool IsDedicated() { return false; }                     // L68818 CLIENT  /  svrL68414 returns true on SERVER build
public Vector3 GetReferencePosition()                           // L68694  public
public List<ZDO> GetAllCharacterZDOS()                          // L68699  PUBLIC
{
    List<ZDO> list = new List<ZDO>();
    ZDO zDO = m_zdoMan.GetZDO(m_characterID);
    if (zDO != null) list.Add(zDO);
    foreach (ZNetPeer peer in m_peers)
        if (peer.IsReady() && !peer.m_characterID.IsNone())
        {
            ZDO zDO2 = m_zdoMan.GetZDO(peer.m_characterID);
            if (zDO2 != null) list.Add(zDO2);
        }
    return list;
}
```

| Member | Visibility | Line | SoM reach | Reflection needed? |
|---|---|---|---|---|
| `ZNetView.IsOwner()` | **public** | 70424 | direct (`AIAuthority.cs:18`) | No |
| `ZNetView.IsValid()` | **public** | 70455 | direct (`StealthExemption.cs:11`) | No |
| `ZNetView.GetZDO()` | **public** | 70450 | direct (`StealthExemption.cs:12`, `BehaviorSystem.cs:162`) | No |
| `ZNetView.ClaimOwnership()` | **public** | 70442 | — | No |
| `ZDO.GetInt(string,int)` | **public** | 62713 | direct (`StealthExemption.cs:13`) | No |
| `ZDO.Set(*)` all overloads | **public** | 62385-62534 | — | No |
| `ZDO.GetLong` / `GetVec3` / `GetFloat` / `GetBool` / `GetString` | **public** | 62753 / 62673 / 62653 / 62733 / 62763 | — | No |
| `ZDO.m_uid` | **public field** | 62147 | direct (`BehaviorSystem.cs:164`) | No |
| `ZDOMan.GetSessionID()` | **public static** | 65750 | — | No |
| `ZNet.instance` | **public static prop** | 67047 | direct (`AIAuthority.cs:16`) | No |
| `ZNet.IsServer()` | **public** | 68808 | direct (`AIAuthority.cs:16`) | No |
| `ZNet.GetAllCharacterZDOS()` | **public** | 68699 | — | No |
| `ZNet.IsDedicated()` | **public** | 68818 / svrL68414 | — | **Hardcoded `false` in the client reference DLL — see PART 3** |

---

## PART 2 — THE AI UPDATE DRIVER (constrains "lower overhead")

```csharp
// L60981 — MonoUpdaters.FixedUpdate
private void FixedUpdate()
{
    s_updateCount++;
    float fixedDeltaTime = Time.fixedDeltaTime;
    …
    m_updateAITimer += fixedDeltaTime;
    if (m_updateAITimer >= 0.05f)
    {
        m_ai.UpdateAI(BaseAI.Instances, "MonoUpdaters.FixedUpdate.BaseAI", 0.05f);   // L60998
        m_updateAITimer -= 0.05f;
    }
    …
}

// L61053
public interface IUpdateAI { bool UpdateAI(float deltaTime); }

// L61073 — extension. Note the AddRange/Clear copy every tick.
public static void UpdateAI(this List<IUpdateAI> container, List<IUpdateAI> source,
                            string profileScope, float deltaTime)
{
    container.AddRange(source);
    foreach (IUpdateAI item in container) item.UpdateAI(deltaTime);
    container.Clear();
}
```

Facts that follow:
- AI ticks at a **fixed 20 Hz** with `dt` hardcoded to `0.05f`, **not** `Time.fixedDeltaTime`. SoM's `MonsterAI_StealthBrain_UpdateAI_Patch.Postfix(MonsterAI, float dt)` receives `0.05f`; it currently passes `Time.deltaTime` into `StealthBrain.Evaluate` in `Humanoid_Attack_Patch.cs:74` — an inconsistent time base.
- **Every** `BaseAI` in `BaseAI.Instances` is ticked regardless of ownership; the owner check is the first thing inside `BaseAI.UpdateAI` (L4113). Non-owner cost is one ZDO bool read (L4115).
- SoM's Postfix on `MonsterAI.UpdateAI` runs even when the base returned `false` (Harmony Postfixes always run). It re-checks `AIAuthority` itself, which is correct but means an ownership check runs twice per creature per tick.
- **`BaseAI.Instances` (L4009, public static `List<IUpdateAI>`) and `BaseAI.BaseAIInstances` (L4011, public static `List<BaseAI>`)** are the zero-cost enumeration points for an Overmind. `GetAllInstances()` (L5476) returns the private `m_instances`, which is `Awake`/`OnDestroy`-scoped and therefore includes *disabled* AIs — prefer `BaseAIInstances`.

---

## PART 3 — DEDICATED-SERVER FACTS THAT CONSTRAIN SoM

Every numbered item below is from `VALHEIM-DEDICATED-SERVER-FACTS.md`, with the SoM-specific consequence spelled out. Line refs in the dossier are into the client decompile.

**Where the brain can run**

1. **(dossier L9)** Instances only exist near `ZNet.GetReferencePosition()`. On a dedicated server the reference position is only ever set by `Game.FindSpawnPoint` fallbacks (L85793-85847) → **~world origin, never near players**; all 6 `SetReferencePosition` call sites are client-side.
   → **There is no `MonsterAI` GameObject near any player on a dedicated server.** `MonoUpdaters.FixedUpdate` has nothing to tick. `AIAuthority.IsAuthoritative`'s fallback branch (`AIAuthority.cs:16`, "no ZNetView → trust `ZNet.IsServer()`") is dead in practice and dangerously misleading as a mental model.

2. **(dossier L11)** `ZoneSystem.Update` (L98652-68): server runs `CreateLocalZones(own refpos)` → real zones near origin only; `CreateGhostZones(peer refpos)` → **ZDO-only**; location objects run `Awake` via ghost-init (ZNetView L70291-98) but the root is destroyed the same frame → **`Start()` never runs in a ghost zone**.
   → Any SoM initialisation hung on `Start()` will never run server-side. `BaseAI.Awake` *may* run for one frame in a ghost zone and then vanish — SoM state keyed on `Character` instances must tolerate an `Awake` with no matching `OnDestroy`-in-a-live-world.

3. **(dossier L12)** `ZoneSystem.IsZoneLoaded(Vector3)` (L98760) is the clean "is this area REAL on this machine" test; guard server-side `Physics.*` with it.
   → `BaseAI.CanSeeTarget`'s `Physics.Raycast(…, m_viewBlockMask)` (L4614) and SoM's `Utils/RaycastUtils.cs` **cannot work on a dedicated server** — there is no collision geometry. LOS is intrinsically a client-side computation. Same for `BehaviorSystem.CallNearbyAllies`'s `Physics.OverlapSphere` (`BehaviorSystem.cs:180`).

4. **(dossier L13)** `ZoneSystem.GetGroundHeight` is a physics raycast (L99961/99972) — the float overload silently returns your input Y without a Heightmap. `WorldGenerator.instance.GetHeight` (L132281) is pure math and works anywhere.
   → Any SoM cover/elevation heuristic must use the `out bool` overload or `WorldGenerator`.

5. **(dossier L52)** *"The server decides, the nearest machine executes."*
   → Confirms the brief's architecture: **`AIAuthority.IsAuthoritative == ZNetView.IsOwner()` is the only correct rule.** Any "the server arbitrates" fallback must be deleted, not merely deprioritised.

**How state must be replicated**

6. **(dossier L24)** The only generic ownership assignment is server-driven: `ZDOMan.ReleaseZDOS` every 2 s (L65242/65301) → `ReleaseNearbyZDOS` (L65330-54). Owner left the area → `SetOwner(0)`; unowned in a peer's area → `SetOwner(peerUid)`. Handover ring = `m_activeArea - 1` zones (L65335). **Latency ≤ ~2.5 s.**
   → A creature's simulator **changes hands every time players move**. Any SoM per-creature state that lives only in a C# dictionary (today: `AwarenessSystem`'s `AwarenessData`, `StealthBrain`'s `_evalCache`) is **destroyed on every handover** — the new owner starts from a blank detection level. Per-player detection tracks *must* be serialised into the ZDO if stealth is to survive a player walking past.

7. **(dossier L5, runtime-measured)** `ZoneSystem.m_activeArea = 2` is the *serialized prefab* value; the C# initializer's `1` is not what ships. Active area = the 5×5 zone block; handover ring = 1 zone.
   → Read it at runtime; do not hardcode either constant.

8. **(dossier L25)** `!Persistent` ZDOs are **skipped entirely** by `ReleaseNearbyZDOS` (L65338) — a non-persistent ZDO created by the server is never handed to a client and never simulates. Vanilla monster prefabs are persistent.
   → Fine for vanilla mobs; a hard constraint for any SoM- or Avalor-spawned entity.

9. **(dossier L10)** `RemoveObjects` destroys the GameObject and **keeps** a persistent ZDO (L69878-86).
   → `Character.OnDestroy` fires on **unload**, not only on death. `Character_OnDestroy_Patch` calling `AwarenessSystem.Clear` therefore wipes stealth state when a player merely walks away, and there is no death/unload distinction. ZDO-backed state fixes this for free.

10. **(dossier L17)** `ZNet.GetAllCharacterZDOS()` (L68699) is the server-side "where is every player" API: local character ZDO + every ready peer's `m_characterID` ZDO. Position and rotation are first-class ZDO fields (`GetPosition` L62625, `GetRotation` L62630), kept fresh every physics tick by `ZSyncTransform.OwnerSync` (L75111).
    → The only correct player enumeration for server-side logic.

11. **(dossier L18-20)** `ZNetPeer.m_refPos` (L66783) is always populated, refreshed every 2 s (`SendPeriodicData` L68185), **not** gated by map visibility. `ZNet.GetPlayerList()` positions **are** gated by `m_publicPosition` (L68952-56) and read `(0,0,0)` with the map toggle off — **never use for game logic**. `Player.GetAllPlayers()` / `Player.IsPlayerInRange` iterate the **local instance list** — empty on a dedicated server; on a player-host, only players near the host.
    → `MonsterAI_StealthBrain_UpdateAI_Patch.cs:25` (`Player.m_localPlayer`) and vanilla's own `Player.IsPlayerInRange(pos, 50f)` in `UpdateTarget` (L5852) are both client-local. On an owning client, `Player.GetAllPlayers()` *is* the right source (the owning client has the relevant players loaded) — but SoM must never treat it as global.

12. **(dossier L32)** Mob death runs on the **owner**: `RPC_Damage` (owner-gated L8712) → `CheckDeath` → `Character.OnDeath` (L9251, owner-gated L9276) → `m_onDeath` → `ZNetScene.Destroy` → `DestroyZDO` broadcast (L65357-91). "The tracked ZDO vanished" is a reliable remote death signal.
    → Reinforces that all mutation must be owner-gated, and gives a cheap cross-peer liveness test.

13. **(dossier L27)** `ZDOMan.DestroyZDO` only acts if you own the ZDO — claim first or it is a silent no-op.

14. **(dossier L38-40)** Routed-RPC param types (`ZRpc.Serialize` L71603): int, uint, long, float, double, bool, string, ZPackage, List\<string\>, Vector3, Quaternion, ZDOID, HitData, ISerializableParameter. **No arrays or enums** — unsupported types are *silently dropped* and the receiver deserialises garbage. `Everybody` (0L) also invokes the handler locally on the caller (L71198-201). Registration is `Dictionary.Add` on a per-world-session `ZRoutedRpc` — double-register **throws**, and a stale "already registered" flag from a previous world leaves you with **no** handlers in the next one; key registration on the instance reference. RPC names share one global hash namespace — **prefix with the plugin GUID**.
    → If v2 adds any SoM RPC it must be `wubarrk.shadowsofmidgard.<name>`, registered against the live `ZRoutedRpc` instance, and must pack per-player track arrays into a `ZPackage`.

15. **(dossier L44)** `Console.instance` exists headless but output may not surface; handlers touching `Player.m_localPlayer` / `Hud` / `Chat` / `MessageHud` must null-guard.
    → Directly relevant: `BaseAI.SetAlerted` dereferences `MessageHud.instance` unguarded at **L5373** when `m_alertedMessage.Length > 0`. If SoM ever calls `SetAlerted` on a machine without a `MessageHud`, that is an NRE. Same class of risk in `BaseAI.OnDeath` (L4515).

16. **(dossier L48)** Headless detection: `SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null` — **compile-independent**. `ZNet.IsDedicated()` is hardcoded `false` in the client reference DLL (L68818) that SoM compiles against, even though the server build returns `true` (svrL68414).
    → **SoM must never call `ZNet.IsDedicated()`.** It is a compile-time constant `false` from SoM's reference assembly's point of view at the source level, and the runtime value depends on which binary loaded. Use the graphics-device check.

17. **(dossier L33)** `Character.SetMaxHealth` writes `ZDOVars.s_maxHealth` and `GetMaxHealth` reads it back live (L9372-88) → replicated state; per-machine multiplication **compounds**.
    → The general warning that applies to any SoM ZDO value: a read-modify-write of a replicated field executed on more than one peer compounds. All SoM ZDO writes must be idempotent-set, never accumulate.

18. **ZDO write semantics — verified beyond the dossier.** Non-owner writes *do* propagate. `IncreaseDataRevision` (L62635) bumps `DataRevision` and, on a client, enqueues `ZDOMan.ClientChanged(m_uid)`; `CreateSyncList` (L65631-42) pushes anything in `m_clientChangeQueue` to the server regardless of ownership; and the receiver only accepts data when `num4 > zDO.DataRevision`:
    ```csharp
    // L65533, RPC_ZDOData
    if (num4 <= zDO.DataRevision)
    {
        if (num3 > zDO.OwnerRevision) { zDO.SetOwnerInternal(ownerInternal); zDO.OwnerRevision = num3; … }
        continue;                       // ← incoming DATA discarded
    }
    ```
    → **This is why MistsofAvalor's and DvergrAllies' `zdo.Set("SoMStealthExempt", 1)` works from a non-owning peer.** But it is last-writer-by-revision, so two peers writing the same key race and can ping-pong. **API rule for v2: static/config keys may be written by any peer once; mutable brain state may be written only by the owner.** Document this in the cross-mod contract.

---

## PART 4 — DEFECTS FOUND WHILE SURVEYING (all verified against real code)

**4.1 — `BehaviorSystem.SetTarget` is dead code.**
`Systems/BehaviorSystem.cs:68`:
```csharp
_targetCreatureRef = AccessTools.FieldRefAccess<BaseAI, Character>("m_targetCreature");
```
`m_targetCreature` is declared on **`MonsterAI`** (`L5727`), a *derived* type. `AccessTools.Field` walks the type and its **base** types; it never searches derived types. The call throws `MissingFieldException`, is swallowed by the `catch` at `BehaviorSystem.cs:70`, and `_targetCreatureRef` stays `null` forever. `SetTarget` (`BehaviorSystem.cs:141-170`) then silently does nothing except write the `s_haveTargetHash` bool. **`BehaviorSystem.CallNearbyAllies` therefore never actually assigns a target to any ally** — the entire ally-coordination feature is inert. Fix: `AccessTools.FieldRefAccess<MonsterAI, Character>(...)`, or better, direct field access via the publicized assembly.

**4.2 — `Character_OnDamaged_Patch` never fires for humanoid monsters.**
`[HarmonyPatch(typeof(Character), "OnDamaged")]` targets `Character.OnDamaged(HitData)` (L9103, empty). `Humanoid.OnDamaged(HitData)` (**L13249**) is:
```csharp
protected override void OnDamaged(HitData hit) { SetCrouch(crouch: false); }
```
— **no `base.OnDamaged(hit)` call.** Greydwarves, Draugr, Fulings, Skeletons, Trolls, Goblins are all `Humanoid`. The patch fires only for non-Humanoid Characters. (`Player.OnDamaged` L21657 *does* chain, but SoM early-returns on `is Player`.) The robust hook is the public `Character.m_onDamaged` delegate (L6875), invoked unconditionally at L8871-8873 on the owner for every Character type — which is exactly what `BaseAI.Awake` itself subscribes to (L4028).

**4.3 — `Character_Damage_Patch` runs on the wrong machine.**
`Character.Damage(HitData)` (L8692) only does `m_nview.InvokeRPC("RPC_Damage", hit)`. It executes on the **attacker's** machine. The actual damage application is `RPC_Damage`, owner-gated at **L8712**. SoM's Postfix (`Character_Damage_Patch.cs:11`) therefore runs where the hit *originated*, then immediately fails its own `AIAuthority.IsAuthoritative(ai)` check (line 22) whenever the attacker is not the creature's owner. In multiplayer, a player hitting a mob owned by a different client produces **no awareness update at all**. Patch `Character.RPC_Damage` (private, L8701) or use `m_onDamaged`.

**4.4 — `Character_OnDestroy_Patch` is correct but semantically overbroad.**
`Character.OnDestroy` (L7390) is `protected virtual`; `Humanoid.OnDestroy` (L12969) and `Player.OnDestroy` (L16028) both chain to base, so the Prefix does fire. But per dossier L10, `OnDestroy` also fires on zone unload with the ZDO intact — so `AwarenessSystem.Clear` erases stealth state for a mob that is merely being culled.

**4.5 — The central per-target bug, quoted.**
`Patches/BaseAI_CanSeeTarget_Patch.cs:16-38`:
```csharp
public static bool Prefix(BaseAI __instance, Character target, ref bool __result)
{
    …
    AwarenessData data = AwarenessSystem.GetData(aiChar);
    if (data == null) return true;
    __result = data.CanSee;      // ← `target` is captured, validated, then DISCARDED
    return false;
}
```
Identical shape in `BaseAI_CanHearTarget_Patch.cs:37`, `BaseAI_StealthBrain_Patch.cs:43`. Combined with `StealthBrain.Evaluate(MonsterAI ai, Player p, float dt)` (`Systems/StealthBrain.cs:51`) taking a single `Player`, and `MonsterAI_StealthBrain_UpdateAI_Patch.cs:25` sourcing that player from `Player.m_localPlayer`, the whole pipeline computes one answer for the local player and returns it for every target asked about — including for `attacker` in the backstab check at L8736.

**4.6 — Attack pacing uses two different time bases.** `Humanoid_Attack_Patch.cs:74` calls `StealthBrain.Evaluate(monster, p, Time.deltaTime)` from inside a render-frame attack call, while the Overmind/UpdateAI path is driven at a fixed `0.05f` (L60998). Detection integration will run at different rates depending on which path last touched a creature.

**4.7 — `AnimalAI` is unhandled.** See §1.3.

---

## PART 5 — v1.0 RENAME/REMOVAL RISK, AND THE DEFENSIVE BINDING SHAPE FOR EACH

Ranked by likelihood of breaking. My reasoning: private members and members whose bodies encode a *design decision* churn; public members with many internal call sites and public members other mods depend on are near-frozen.

### Tier 1 — HIGH RISK (assume these will move; never bind them statically)

| Member | Why it is at risk | Defensive shape |
|---|---|---|
| **`MonsterAI.m_targetCreature`** (private, L5727) | Private field. A multi-target or threat-table AI rewrite — the single most-requested AI change — deletes or replaces it outright. | Never touch the field. Read through **`MonsterAI.GetTargetCreature()`** (public virtual, L6401 — already a virtual accessor, i.e. the authors' own abstraction seam). For *writing*, resolve `AccessTools.Field(typeof(MonsterAI), "m_targetCreature")` once into a cached `FieldRef`, null-check it, and if absent fall back to `MonsterAI.SetTarget` (also probed) and finally to no-op with one warning. Never let absence disable the rest of SoM. |
| **`MonsterAI.m_timeSinceSensedTargetCreature`, `m_updateTargetTimer`, `m_lastKnownTargetPos`, `m_beenAtLastPos`, `m_targetStatic`** (all private, L5729-5739) | Private timer/scratch state; exactly what a rewrite consolidates. | Same probe-and-degrade pattern. Treat each as an *optional optimisation*, never a requirement. Prefer influencing behaviour through the patched public methods (`CanSeeTarget`/`CanHearTarget` control `m_timeSinceSensedTargetCreature` indirectly via L5922-5924) rather than writing fields. |
| **`MonsterAI.UpdateTarget(Humanoid,float,out bool,out bool)`** (private, L5846) | Private, four-parameter, two `out`s — a signature shape that changes whenever the target model changes. | Do not patch it. SoM already achieves the same effect by patching the public `CanSeeTarget`/`CanHearTarget` that `UpdateTarget` calls (L5920-5921) — keep it that way. |
| **`BaseAI.MoveTo(float,Vector3,float,bool)`** (protected, L4771) | Protected; the pathfinding path is a plausible rewrite target (`m_path`, `FindPath`, serpent/fly special cases are all crammed in here). | Resolve via `AccessTools.Method` once; if `null`, fall back to the **public** `BaseAI.MoveTowards(Vector3,bool)` (L4672) — which is the primitive `MoveTo` itself calls at L4821 — and only then to `Character.SetMoveDir`. Three-level ladder, each level publicly reachable. |
| **`BaseAI.SetTargetInfo(ZDOID)`** (protected, L5453) | Protected, and its body already discards the ZDOID (`Set(s_haveTargetHash, !targetID.IsNone())`) — a strong hint it is transitional and will either gain a real ZDOID write or be deleted. | Probe-resolve. Fallback: write the ZDO key directly — `nview.GetZDO().Set(ZDOVars.s_haveTargetHash, hasTarget)` — after probing `ZDOVars.s_haveTargetHash` itself (see Tier 2). The observable effect (`HaveTarget()`, L5458, public) is what matters. |
| **`BaseAI.SetAlerted(bool)`** (protected virtual, L5350) | Protected. Its body mixes alert state, animator, ZDO, effects, boss counting and `MessageHud` — prime refactor material. | Prefer the **public `BaseAI.Alert()`** (L5327) for the alert direction; it already handles the non-owner case with `InvokeRPC("Alert")`. There is no public de-alert; probe `SetAlerted` for that and, failing that, write `ZDOVars.s_alert` directly plus keep SoM's own `IsAlerted` patch authoritative. |
| **`ZNet.IsDedicated()`** (L68818 client / svrL68414 server) | Not a rename risk — a **correctness** risk that already exists: it is hardcoded `false` in the reference assembly SoM compiles against. | **Do not use.** Use `SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null` (dossier L48), which is compile-independent. |
| **`Player.m_localPlayer`** (public static field, L15457) | Low rename risk, but **high semantic risk**: it is `null` on any headless machine and is the wrong answer in multiplayer regardless. | Delete every game-logic use. Replace with per-creature player enumeration: `Player.GetPlayersInRange(pos, range, reusableList)` (L20388, non-allocating) on a client; `ZNet.instance.GetAllCharacterZDOS()` (L68699) for anything that must work headless. Keep `m_localPlayer` only for HUD code, always null-guarded. |

### Tier 2 — MEDIUM RISK

| Member | Why | Defensive shape |
|---|---|---|
| **`ZDOVars.s_alert`, `s_haveTargetHash`, `s_stealth`, `s_noise`, `s_huntPlayer`, `s_sleeping`, `s_shownAlertMessage`** | `ZDOVars` is a generated constants table; entries get renamed and removed freely between versions. | Never reference `ZDOVars.*` symbolically at runtime. All of them are `"name".GetStableHashCode()` values, and every `ZDO` getter/setter has a **`string` overload** (L62417/62464/62493/62653/62713/62733/62753). Bind by literal string once — `static readonly int SoM_Alert = "alert".GetStableHashCode();` — after confirming the literal from the decompile, and use `ZDO.Set(int, …)`. The hash is stable across versions even if the C# constant is renamed. |
| **`Character.OnDamaged(HitData)`** (protected virtual, L9103) | Empty virtual with a non-chaining override in `Humanoid` — likely to be reworked or removed. Already broken for SoM (PART 4.2). | Switch to the **public `Character.m_onDamaged` delegate** (L6875). Subscribe in a `Character.Awake` Postfix exactly as `BaseAI.Awake` does at L4028: `c.m_onDamaged = (Action<float,Character>)Delegate.Combine(c.m_onDamaged, myHandler);`. Public field, invoked unconditionally on the owner at L8871-8873, and it survives any `OnDamaged` refactor. Unsubscribe in `OnDestroy`. |
| **`Character.OnDestroy()`** (protected virtual, L7390) | Unity lifecycle method; low rename risk but the chain from `Humanoid` (L12971) could be dropped. | Keep the patch but add `ZNetScene`-level ZDO-destroy detection as a cross-check, and stop treating it as "the creature died" — per dossier L10 it also means "unloaded". |
| **`Humanoid.StartAttack(Character,bool)`** (public override, L13073) | Public and heavily used, but the second parameter is `charge` on the base (L9675) and `secondaryAttack` on the override — a naming inconsistency that invites cleanup. **Harmony matches injected patch parameters by name.** | Never name a patch parameter `charge`/`secondaryAttack`. Take only `Humanoid __instance` (which is what `Humanoid_Attack_Patch.cs:16` already does — keep it that way) or use `object[] __args`. |
| **`BaseAI.Instances` / `BaseAIInstances`** (public static props, L4009/4011) | Recently-introduced perf plumbing alongside `MonoUpdaters`; such plumbing gets reshaped. | Wrap in one accessor. Primary: `BaseAI.BaseAIInstances`. Fallback: `BaseAI.GetAllInstances()` (L5476, public). Last resort: SoM's own registry maintained from a `BaseAI.Awake` Postfix — which SoM should keep anyway for per-creature track storage. |
| **`MonoUpdaters` / `IUpdateAI`** (L60955 / L61053) | New-ish scheduling layer; the hardcoded `0.05f` and the `AddRange`/`Clear` copy at L61075-61080 look provisional. | Do not patch `MonoUpdaters`. Stay on `MonsterAI.UpdateAI` (public virtual, L5954) as the tick source and **use the `dt` Harmony hands you** rather than `Time.deltaTime` — that automatically tracks whatever cadence the game switches to. |

### Tier 3 — LOW RISK (safe to bind statically and directly)

These are public, have double-digit internal call sites, and/or are consumed by the modding ecosystem. Bind them normally; a plain null/`try` guard at plugin init is sufficient.

- `BaseAI.CanSeeTarget(Character)` L4577, `CanHearTarget(Character)` L4546, `CanSenseTarget(Character)` L4519 — and their `static` counterparts L4582/4551/4529.
- `BaseAI.IsAlerted()` L4448→L5448, `Alert()` L5327, `HaveTarget()` L5458, `IsEnemy` L4990/4995, `IsSleeping()` L5508.
- `BaseAI.m_viewRange` L3844, `m_viewAngle` L3846, `m_hearRange` L3848, `m_mistVision` L3850, `m_passiveAggresive` L3920, `m_aggravatable` L3918.
- `MonsterAI.GetTargetCreature()` L6401, `GetStaticTarget()` L6406, `HuntPlayer()` L5291, `SetHuntPlayer(bool)` L5279, `IsSleeping()` L6508, `m_alertRange` L5641, `m_maxChaseDistance` L5675.
- `Character.SetMoveDir` L9503, `GetMoveDir` L10583, `GetEyePoint` L8652, `GetCenterPoint` L8657, `m_eye` L6951, `IsCrouching` L9755, `IsTamed` L10644, `IsPlayer` L7419, `GetAllCharacters` L10316, `GetZDOID` L9689, `GetBaseAI` L10587, `GetStealthFactor` L10094, `GetNoiseRange` L10132, `AddNoise` L10110, `m_onDamaged` L6875, `m_onDeath` L6877, `m_aiSkipTarget` L6899, `OnTargeted` L10333.
- `Player.GetAllPlayers` L20441, `GetPlayersInRange` L20388, `GetClosestPlayer` L20336, `IsPlayerInRange` L20399/20352/20411.
- **The entire networking layer** — `ZNetView.IsOwner`/`IsValid`/`GetZDO`/`ClaimOwnership` (L70424/70455/70450/70442), every `ZDO.Set`/`Get*` overload (L62385-62775), `ZDO.m_uid` (L62147), `ZDO.IsOwner`/`HasOwner`/`GetOwner`/`SetOwner` (L63617/63622/63608/63627), `ZDOMan.GetSessionID` (L65750), `ZDOMan.instance` (L64848), `ZDOMan.GetZDO(ZDOID)` (L65184), `ZNet.instance` (L67047), `ZNet.IsServer` (L68808), `ZNet.GetAllCharacterZDOS` (L68699), `ZNet.GetReferencePosition` (L68694). These are the save-format and wire-protocol surface; they change only with a save-format break, and every other mod on the planet uses them.

### The binding harness v2 should adopt

One `VanillaBindings` static class, resolved once in `Awake`, with three properties per risky member: `bool Available`, a cached fast delegate/`FieldRef`, and a documented fallback. Rules:

1. **Never resolve at static-constructor time inside a system class** (today `BehaviorSystem`'s `static BehaviorSystem()` at `BehaviorSystem.cs:25` runs on first touch, from inside a Harmony patch, with no ordering guarantee). Resolve explicitly in plugin `Awake` and log a single consolidated capability report.
2. **Every Harmony `[HarmonyPatch]` must be validated before `PatchAll`.** `AccessTools.Method(...)` returning `null` gives Harmony `HarmonyException` at patch time and can abort the *whole* plugin. Enumerate SoM's patch targets, probe each, and skip the ones whose target is missing — logging once — rather than letting one rename kill the mod. Given the brief's "never hard-crash on a missing member", this is the highest-leverage change.
3. **Expose the resolved capability set on the public compat API** so MistsofAvalor and DvergrAllies can feature-detect what SoM actually managed to bind, not just that SoM exists. This pairs naturally with the "capability/version constant" the brief already calls for, and gives the `HarmonyBefore("wubarrk.shadowsofmidgard")` consumers something better to branch on than type existence.
4. **`ShadowsOfMidgard.StealthExemption` must stay verbatim.** Current shape, `c:/WubarrkCODING/ShadowsOfMidgard/Systems/StealthExemption.cs`: `public const string ZDOKey = "SoMStealthExempt"`, `public static bool IsExempt(Character)`, `public static bool IsExempt(BaseAI)`. Note it reads via `zdo.GetInt(ZDOKey, 0) == 1` — the `string` overload (L62713), which hashes with `GetStableHashCode` on every call, per creature, per patch invocation, in six patches. Cache `"SoMStealthExempt".GetStableHashCode()` into a `static readonly int` and use `GetInt(int, int)` (L62718) — same wire value, no per-call hashing. The public signatures are unchanged, so the load-bearing contract survives.