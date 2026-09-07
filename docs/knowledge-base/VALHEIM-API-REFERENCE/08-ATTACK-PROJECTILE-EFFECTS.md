All citations are `assembly_valheim.decompiled.cs` at `C:\WubarrkCODING\libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs`. Line numbers are from that file.

---

## 1. `class Attack` — declaration

```csharp
[Serializable]              // :814
public class Attack         // :815   plain class, NOT a MonoBehaviour, NOT ScriptableObject
```

`public Attack Clone()` → `return MemberwiseClone() as Attack;` — **:2412-2415**
Used by `Humanoid.StartAttack` at **:13098**: `attack = currentWeapon.m_shared.m_attack.Clone()` / `m_secondaryAttack.Clone()`. **Every attack instance the player swings is a shallow copy** — patching fields on the live `m_currentAttack` does not persist; patch `SharedData.m_attack` (the template) instead.

### 1.1 Nested types

| Member | Line |
|---|---|
| `public class Attack.HitPoint` (fields: `GameObject go`, `Vector3 avgPoint`, `int count`, `Vector3 firstPoint`, `Collider collider`, `Dictionary<Collider,Vector3> allHits`, `Vector3 closestPoint`, `float closestDistance`) | :817-834 |
| `public enum Attack.AttackType` | :836-844 |
| `public enum Attack.HitPointType { Closest=0, Average=1, First=2 }` | :846-851 |

### 1.2 `enum Attack.AttackType` — EVERY member, implicit int values

```csharp
public enum AttackType   // :836
{
    Horizontal        = 0,   // :838
    Vertical          = 1,   // :839
    Projectile        = 2,   // :840
    None              = 3,   // :841
    Area              = 4,   // :842
    TriggerProjectile = 5    // :843
}
```

**`TriggerProjectile` is DEAD.** The one and only dispatch switch is `Attack.OnAttackTrigger()` **:1390-1405**:

```csharp
switch (m_attackType)                       // :1390
{
case AttackType.Horizontal:                 // :1392
case AttackType.Vertical:                   // :1393
    DoMeleeAttack();     break;             // :1394
case AttackType.Area:                       // :1396
    DoAreaAttack();      break;             // :1397
case AttackType.Projectile:                 // :1399
    ProjectileAttackTriggered(); break;     // :1400
case AttackType.None:                       // :1402
    DoNonAttack();       break;             // :1403
}
```
A full-file grep for `TriggerProjectile` returns exactly ONE hit: the enum declaration at :843. It appears in no switch, no `if`, no comparison anywhere in the assembly. **Setting `m_attackType = TriggerProjectile` produces an attack that plays its animation and does absolutely nothing on the trigger frame.** Do not use it.

### 1.3 Static/private caches (publicized-assembly reachable)

| Field | Type | Line |
|---|---|---|
| `s_hits` | `private static Collider[]` (128) | :853 |
| `s_hitSet` | `private static HashSet<GameObject>` | :855 |
| `s_hitList` | `private static List<RaycastHit>` | :857 |
| `s_hits2` | `private static RaycastHit[]` (50) | :859 |
| `s_pieceColliders` | `private static readonly Collider[]` (200) | :1057 |
| `m_attackMask`, `m_attackMaskTerrain`, `m_attackMaskCharacters`, `m_harvestRayMask` | `protected static int` | :1072-1078 |

### 1.4 `Attack` — EVERY serialized field, in declaration order

`[Header("Common")]` @ :861

| # | Field | Declared type | Default | Line |
|---|---|---|---|---|
| 1 | `m_attackType` | `AttackType` | `Horizontal` (0) | :862 |
| 2 | `m_attackAnimation` | `string` | `""` | :864 |
| 3 | `m_chargeAnimationBool` | `string` | `""` | :866 |
| 4 | `m_attackRandomAnimations` | `int` | 0 | :868 |
| 5 | `m_attackChainLevels` | `int` | 0 | :870 |
| 6 | `m_loopingAttack` | `bool` | false | :872 |
| 7 | `m_consumeItem` | `bool` | false | :874 |
| 8 | `m_hitTerrain` | `bool` | **true** | :876 |
| 9 | `m_hitFriendly` | `bool` | false | :878 |
| 10 | `m_isHomeItem` | `bool` | false | :880 |
| 11 | `m_attackStamina` | `float` | **20f** | :882 |
| 12 | `m_attackAdrenaline` | `float` | 1f | :884 |
| 13 | `m_attackUseAdrenaline` | `float` | 0 | :886 |
| 14 | `m_attackEitr` | `float` | 0 | :888 |
| 15 | `m_attackHealth` | `float` | 0 | :890 |
| 16 | `m_attackHealthPercentage` | `float` `[Range(0,100)]` | 0 | :892-893 |
| 17 | `m_attackHealthLowBlockUse` | `bool` | **true** | :895 |
| 18 | `m_attackHealthReturnHit` | `float` | 0 | :897 |
| 19 | `m_attackKillsSelf` | `bool` | false | :899 |
| 20 | `m_speedFactor` | `float` | **0.2f** | :901 |
| 21 | `m_speedFactorRotation` | `float` | 0.2f | :903 |
| 22 | `m_attackStartNoise` | `float` | 10f | :905 |
| 23 | `m_attackHitNoise` | `float` | 30f | :907 |
| 24 | `m_damageMultiplier` | `float` | **1f** | :909 |
| 25 | `m_damageMultiplierPerMissingHP` | `float` `[Tooltip]` | 0 | :911-912 |
| 26 | `m_damageMultiplierByTotalHealthMissing` | `float` `[Tooltip]` | 0 | :914-915 |
| 27 | `m_staminaReturnPerMissingHP` | `float` `[Tooltip]` | 0 | :917-918 |
| 28 | `m_forceMultiplier` | `float` | **1f** | :920 |
| 29 | `m_staggerMultiplier` | `float` | **1f** | :922 |
| 30 | `m_recoilPushback` | `float` | 0 | :924 |
| 31 | `m_selfDamage` | **`int`** (not float) | 0 | :926 |

`[Header("Misc")]` @ :928

| # | Field | Type | Default | Line |
|---|---|---|---|---|
| 32 | `m_attackOriginJoint` | `string` | `""` | :929 |
| 33 | `m_attackRange` | `float` | **1.5f** | :931 |
| 34 | `m_attackHeight` | `float` | **0.6f** | :933 |
| 35 | `m_attackHeightChar1` | `float` | 0 | :935 |
| 36 | `m_attackHeightChar2` | `float` | 0 | :937 |
| 37 | `m_attackOffset` | `float` | 0 | :939 |
| 38 | `m_spawnOnTrigger` | `GameObject` | null | :941 |
| 39 | `m_toggleFlying` | `bool` | false | :943 |
| 40 | `m_attach` | `bool` | false | :945 |
| 41 | `m_cantUseInDungeon` | `bool` | false | :947 |

`[Header("Loading")]` @ :949

| # | Field | Type | Default | Line |
|---|---|---|---|---|
| 42 | `m_requiresReload` | `bool` | false | :950 |
| 43 | `m_reloadAnimation` | `string` | `""` | :952 |
| 44 | `m_reloadTime` | `float` | 2f | :954 |
| 45 | `m_reloadStaminaDrain` | `float` | 0 | :956 |
| 46 | `m_reloadEitrDrain` | `float` | 0 | :958 |

`[Header("Draw")]` @ :960

| # | Field | Type | Default | Line |
|---|---|---|---|---|
| 47 | `m_bowDraw` | `bool` | false | :961 |
| 48 | `m_drawDurationMin` | `float` | 0 | :963 |
| 49 | `m_drawStaminaDrain` | `float` | 0 | :965 |
| 50 | `m_drawEitrDrain` | `float` | 0 | :967 |
| 51 | `m_drawAnimationState` | `string` | `""` | :969 |
| 52 | `m_drawVelocityCurve` | `AnimationCurve` | `AnimationCurve.Linear(0,0,1,1)` | :971 |

`[Header("Melee/AOE")]` @ :973

| # | Field | Type | Default | Line |
|---|---|---|---|---|
| 53 | `m_attackAngle` | `float` | **90f** | :974 |
| 54 | `m_attackRayWidth` | `float` | 0 | :976 |
| 55 | `m_attackRayWidthCharExtra` | `float` | 0 | :978 |
| 56 | `m_maxYAngle` | `float` | 0 | :980 |
| 57 | `m_lowerDamagePerHit` | `bool` | **true** | :982 |
| 58 | `m_hitPointtype` | `HitPointType` (note lowercase `t`) | `Closest` | :984 |
| 59 | `m_hitThroughWalls` | `bool` | false | :986 |
| 60 | `m_multiHit` | `bool` | **true** | :988 |
| 61 | `m_pickaxeSpecial` | `bool` | false | :990 |
| 62 | `m_lastChainDamageMultiplier` | `float` | 2f | :992 |
| 63 | `m_resetChainIfHit` | `DestructibleType` `[BitMask]` | None | :994-995 |

`[Header("Spawn on hit")]` @ :997

| # | Field | Type | Default | Line |
|---|---|---|---|---|
| 64 | `m_spawnOnHit` | `GameObject` | null | :998 |
| 65 | `m_spawnOnHitChance` | `float` | 0 | :1000 |

`[Header("Skill settings")]` @ :1002

| # | Field | Type | Default | Line |
|---|---|---|---|---|
| 66 | `m_raiseSkillAmount` | `float` | 1f | :1003 |
| 67 | `m_skillHitType` | `DestructibleType` `[BitMask]` | `DestructibleType.Character` | :1005-1006 |
| 68 | `m_specialHitSkill` | `Skills.SkillType` | None | :1008 |
| 69 | `m_specialHitType` | `DestructibleType` `[BitMask]` | None | :1010-1011 |

`[Header("Projectile")]` @ :1013

| # | Field | Type | Default | Line |
|---|---|---|---|---|
| 70 | `m_attackProjectile` | `GameObject` | null | :1014 |
| 71 | `m_projectileVel` | `float` | **10f** | :1016 |
| 72 | `m_projectileVelMin` | `float` | **2f** | :1018 |
| 73 | `m_randomVelocity` | `bool` `[Tooltip]` | false | :1020-1021 |
| 74 | `m_projectileAccuracy` | `float` | **10f** | :1023 |
| 75 | `m_projectileAccuracyMin` | `float` | **20f** | :1025 |
| 76 | `m_circularProjectileLaunch` | `bool` | false | :1027 |
| 77 | `m_distributeProjectilesAroundCircle` | `bool` | false | :1029 |
| 78 | `m_skillAccuracy` | `bool` | false | :1031 |
| 79 | `m_useCharacterFacing` | `bool` | false | :1033 |
| 80 | `m_useCharacterFacingYAim` | `bool` | false | :1035 |
| 81 | `m_launchAngle` | `float` `[FormerlySerializedAs("m_useCharacterFacingAngle")]` | 0 | :1037-1038 |
| 82 | `m_projectiles` | `int` | **1** | :1040 |
| 83 | `m_projectileBursts` | `int` | **1** | :1042 |
| 84 | `m_burstInterval` | `float` | 0 | :1044 |
| 85 | `m_destroyPreviousProjectile` | `bool` | false | :1046 |
| 86 | `m_perBurstResourceUsage` | `bool` | false | :1048 |

`[Header("Harvest")]` @ :1050

| # | Field | Type | Default | Line |
|---|---|---|---|---|
| 87 | `m_harvest` | `bool` | false | :1051 |
| 88 | `m_harvestRadius` | `float` | 0 | :1053 |
| 89 | `m_harvestRadiusMaxLevel` | `float` | 0 | :1055 |

`[Header("Attack-Effects")]` @ :1059 — **all `public EffectList`, all `= new EffectList()`**

| # | Field | Line |
|---|---|---|
| 90 | `m_hitEffect` | :1060 |
| 91 | `m_hitTerrainEffect` | :1062 |
| 92 | `m_startEffect` | :1064 |
| 93 | `m_triggerEffect` | :1066 |
| 94 | `m_trailStartEffect` | :1068 |
| 95 | `m_burstEffect` | :1070 |

> **`Attack.m_holdStartEffect` — NOT FOUND on `Attack`.** It lives on `ItemDrop.ItemData.SharedData`:
> `public EffectList m_holdStartEffect = new EffectList();` — **:58256**. Only consumer: `Player.UpdateAttackBowDraw` at **:17092**.

### 1.5 `Attack` — runtime/private state (private; reachable only via publicized assembly or reflection)

| Field | Type | Line |
|---|---|---|
| `m_character` | `private Humanoid` | :1080 |
| `m_baseAI` | `private BaseAI` | :1082 |
| `m_body` | `private Rigidbody` | :1084 |
| `m_zanim` | `private ZSyncAnimation` | :1086 |
| `m_animEvent` | `private CharacterAnimEvent` | :1088 |
| `m_weapon` | `[NonSerialized] private ItemDrop.ItemData` | :1090-1091 |
| `m_visEquipment` | `private VisEquipment` | :1093 |
| `m_lastUsedAmmo` | `[NonSerialized] private ItemDrop.ItemData` | :1095-1096 |
| `m_attackDrawPercentage` | `private float` | :1098 |
| `m_freezeFrameDuration` | `private const float = 0.15f` | :1100 |
| `m_chainAttackMaxTime` | `private const float = 0.2f` | :1102 |
| `m_nextAttackChainLevel` | `private int` | :1104 |
| `m_currentAttackCainLevel` | `private int` (sic — "Cain") | :1106 |
| `m_wasInAttack` | `private bool` | :1108 |
| `m_time` | `private float` | :1110 |
| `m_abortAttack` | `private bool` | :1112 |
| `m_attackTowardsCameraDir` | `private bool = true` | :1114 |
| `m_projectileAttackStarted` | `private bool` | :1116 |
| `m_projectileFireTimer` | `private float = -1f` | :1118 |
| `m_projectileBurstsFired` | `private int` | :1120 |
| `m_ammoItem` | `[NonSerialized] private ItemDrop.ItemData` | :1122-1123 |
| `m_attackDone` | `private bool` | :1125 |
| `m_isAttached` | `private bool` | :1127 |
| `m_attachTarget` | `private Transform` | :1129 |
| `m_attachOffset` | `private Vector3` | :1131 |
| `m_attachDistance` | `private float` | :1133 |
| `m_attachHitPoint` | `private Vector3` | :1135 |
| `m_detachTimer` | `private float` | :1137 |

### 1.6 `Attack` — public method surface (exact signatures)

```csharp
public bool StartDraw(Humanoid character, ItemDrop.ItemData weapon)                        // :1139
public bool Start(Humanoid character, Rigidbody body, ZSyncAnimation zanim,
                  CharacterAnimEvent animEvent, VisEquipment visEquipment,
                  ItemDrop.ItemData weapon, Attack previousAttack,
                  float timeSinceLastAttack, float attackDrawPercentage)                   // :1149
public void StartWithoutAnimation(Humanoid character, Rigidbody body,
                  VisEquipment visEquipment, ItemDrop.ItemData weapon,
                  float attackDrawPercentage = 0f)                                         // :1234
public void Update(float dt)                                                               // :1289
public bool IsDone()                                                                       // :1335
public void Stop()                                                                         // :1340
public void Abort()                                                                        // :1375
public void OnAttackTrigger()                                                              // :1380
public bool IsAttached()                                                                   // :2351
public bool GetAttachData(out ZDOID parent, out string attachJoint, out Vector3 relativePos,
                          out Quaternion relativeRot, out Vector3 relativeVel)             // :2356
public static GameObject SpawnOnHitTerrain(Vector3 hitPoint, GameObject prefab,
                          Character character, float attackHitNoise,
                          ItemDrop.ItemData weapon, ItemDrop.ItemData ammo,
                          bool randomRotation = false)                                     // :2379
public Attack Clone()                                                                      // :2412
public ItemDrop.ItemData GetWeapon()                                                       // :2417
public bool CanStartChainAttack()                                                          // :2422
public void OnTrailStart()                                                                 // :2431
public override string ToString()                                                          // :2448
```

Private methods worth patching (publicized assembly / `AccessTools`):
```csharp
private bool UseAmmo(out ItemDrop.ItemData ammoItem)                     // :1512
private void ProjectileAttackTriggered()                                // :1548
private void UpdateProjectile(float dt)                                 // :1567
private Transform GetAttackOrigin()                                     // :1581
private void GetProjectileSpawnPoint(out Vector3 spawnPoint, out Vector3 aimDir) // :1590
private void FireProjectileBurst()                                      // :1616
private void ModifyDamage(HitData hitData, float damageFactor = 1f)     // :1769
private void DoNonAttack()                                              // :1790
private float GetLevelDamageFactor()                                    // :1806
private void DoAreaAttack()                                             // :1811
private void DoMeleeAttack()                                            // :1992
private void SpawnOnHit(GameObject target)                              // :2272
private float GetAttackStamina()                                        // :1246
private float GetAttackEitr()                                           // :1267
private float GetAttackHealth()                                         // :1278
```

### 1.7 Where the key fields actually do work

| Field | Consumed at |
|---|---|
| `m_hitTerrain` | :1822 (`DoAreaAttack`) and :2005 (`DoMeleeAttack`): `int layerMask = (m_hitTerrain ? m_attackMaskTerrain : m_attackMask);` |
| `m_hitThroughWalls` | :2085 `if (!m_hitThroughWalls) break;` — stops the melee raycast at first hit |
| `m_lowerDamagePerHit` | :2132-2135 `if (m_multiHit && m_lowerDamagePerHit && list.Count > 1) num5 /= (float)list.Count * 0.75f;` |
| `m_attackHeight` / `m_attackRange` / `m_attackOffset` | :1594 `spawnPoint = attackOrigin.position + transform.up * m_attackHeight + transform.forward * m_attackRange + transform.right * m_attackOffset;` |
| `m_useCharacterFacing` / `m_useCharacterFacingYAim` | :1605-1613 |
| `m_projectileBursts` / `m_burstInterval` | :1557-1564 (`==1` → fire immediately) and :1569-1578 (`UpdateProjectile` timer loop) |
| `m_projectiles` | :1696 `for (int i = 0; i < m_projectiles; i++)` |
| `m_destroyPreviousProjectile` | :1698-1702, destroys `m_weapon.m_lastProjectile` via `ZNetScene.instance.Destroy` |
| `m_projectileVel/VelMin/Accuracy/AccuracyMin` + `m_drawVelocityCurve` | :1657-1685 — **ammo item's Attack values are ADDED, not replaced** (:1663-1671) |
| `m_bowDraw` in the fire path | :1674-1680: `num3 = Mathf.Lerp(num4, num3, Mathf.Pow(m_attackDrawPercentage, 0.5f)); num6 *= m_attackDrawPercentage; num = Mathf.Lerp(num2, num, drawVelocityCurve.Evaluate(m_attackDrawPercentage));` |
| `m_damageMultiplier`, `m_damageMultiplierPerMissingHP`, `m_damageMultiplierByTotalHealthMissing` | :1769-1788 `ModifyDamage` |
| `m_forceMultiplier` | :1723, :1886, :2142 (`hitData.m_pushForce = ... * m_forceMultiplier`) |
| `m_staggerMultiplier` | :1725, :1888, :2144 |
| `m_attackStamina` / `m_attackEitr` / `m_attackHealth` | consumed at :1308-1310 in `Update` (first frame of `InAttack`), or per-burst at :1618-1653 when `m_perBurstResourceUsage` |
| `m_startEffect` | :1313-1314 (shared first, then attack's) |
| `m_triggerEffect` | :1551-1552 (projectile) and :1797-1798 (`DoNonAttack`) |
| `m_hitEffect` | :1837-1838 (area) and :2115-2116 (melee) |
| `m_trailStartEffect` | :2436-2437 |
| `m_burstEffect` | :1692-1694 `if (m_burstEffect.HasEffects()) m_burstEffect.Create(spawnPoint, Quaternion.LookRotation(aimDir));` |
| `m_speedFactor` | NOT read inside `Attack`; read by `Character`/`Humanoid` movement code |

Resource-cost getters (all `private`, skill-scaled 33%):
```csharp
private float GetAttackStamina()  // :1246-1265  staminaUse -= staminaUse * 0.33f * skillFactor;  (:1259)
private float GetAttackEitr()     // :1267-1276  return attackEitr - attackEitr * 0.33f * skillFactor;  (:1275)
private float GetAttackHealth()   // :1278
```

---

## 2. `Attack.DoMeleeAttack` — status-effect roll

`private void DoMeleeAttack()` — **:1992**. The roll is inside the per-hit `HitData` build:

```csharp
// :2136-2138  (Attack.DoMeleeAttack)
HitData hitData = new HitData();
hitData.m_toolTier = (short)m_weapon.m_shared.m_toolTier;
hitData.m_statusEffectHash = (((bool)m_weapon.m_shared.m_attackStatusEffect
    && (m_weapon.m_shared.m_attackStatusEffectChance == 1f
        || UnityEngine.Random.Range(0f, 1f) < m_weapon.m_shared.m_attackStatusEffectChance))
    ? m_weapon.m_shared.m_attackStatusEffect.NameHash() : 0);
```

The **identical** expression appears in three other places:
- **:1727** — `Attack.FireProjectileBurst()`, rolled once per projectile inside the `for (i < m_projectiles)` loop.
- **:1882** — `Attack.DoAreaAttack()` (inside local func `checkHits`), rolled per target.
- **:2138** — `Attack.DoMeleeAttack()`, rolled per hit point.

Ammo override in the projectile path (**:1742-1745**) — note **no `(bool)` cast, `!= null` instead**, and it can only *set*, never clear:
```csharp
if (ammoItem.m_shared.m_attackStatusEffect != null
    && (ammoItem.m_shared.m_attackStatusEffectChance == 1f
        || UnityEngine.Random.Range(0f, 1f) < ammoItem.m_shared.m_attackStatusEffectChance))
{
    hitData.m_statusEffectHash = ammoItem.m_shared.m_attackStatusEffect.NameHash();
}
```

**The effect applies to the TARGET, not the attacker.** Field declarations: `public StatusEffect m_attackStatusEffect;` **:58171**, `public float m_attackStatusEffectChance = 1f;` **:58173** — both on `ItemDrop.ItemData.SharedData`, **not** on `Attack`.

Application site is `Character.RPC_Damage(long sender, HitData hit)` (**:8701**), running on the **victim's owner**:
```csharp
// :8756-8767
if (hit.m_statusEffectHash != 0)
{
    StatusEffect statusEffect = m_seman.GetStatusEffect(hit.m_statusEffectHash);
    if (statusEffect == null)
        statusEffect = m_seman.AddStatusEffect(hit.m_statusEffectHash, resetTime: false, hit.m_itemLevel, hit.m_skillLevel);
    else { statusEffect.ResetTime(); statusEffect.SetLevel(hit.m_itemLevel, hit.m_skillLevel); }
}
```
`m_seman` here is the **receiving** `Character`'s `SEMan`. A successful block wipes it: `hit.m_statusEffectHash = 0;` at **:14509**.

---

## 3. `class Projectile`

```csharp
public interface IProjectile                                             // :2453
{
    void Setup(Character owner, Vector3 velocity, float hitNoise, HitData hitData,
               ItemDrop.ItemData item, ItemDrop.ItemData ammo);          // :2455
    string GetTooltipString(int itemQuality);                            // :2457
}

public class Projectile : MonoBehaviour, IProjectile                     // :2459
```

### 3.1 Public serialized fields

| Field | Type | Default | Line |
|---|---|---|---|
| `m_type` | `ProjectileType` | — | :2461 |
| `m_damage` | **`HitData.DamageTypes`** (struct, by value) | default | :2463 |
| `m_aoe` | `float` | 0 | :2465 |
| `m_dodgeable` | `bool` | false | :2467 |
| `m_blockable` | `bool` | false | :2469 |
| `m_adrenaline` | `float` | 2f | :2471 |
| `m_attackForce` | `float` | 0 | :2473 |
| `m_backstabBonus` | `float` | **4f** | :2475 |
| `m_statusEffect` | **`string`** (NOT `StatusEffect`) | `""` | :2477 |
| `m_healthReturn` | `float` | 0 | :2481 |
| `m_canHitWater` | `bool` | false | :2483 |
| `m_ttl` | `float` | **4f** | :2485 |
| `m_gravity` | `float` | 0 | :2487 |
| `m_drag` | `float` | 0 | :2489 |
| `m_rayRadius` | `float` | 0 | :2491 |
| `m_hitNoise` | `float` | 50f | :2493 |
| `m_doOwnerRaytest` | `bool` | false | :2495 |
| `m_stayAfterHitStatic` | `bool` | false | :2497 |
| `m_stayAfterHitDynamic` | `bool` | false | :2499 |
| `m_stayTTL` | `float` | 1f | :2501 |
| `m_attachToRigidBody` | `bool` | false | :2503 |
| `m_attachToClosestBone` | `bool` | false | :2505 |
| `m_attachPenetration` | `float` | 0 | :2507 |
| `m_attachBoneNearify` | `float` | 0.25f | :2509 |
| `m_hideOnHit` | `GameObject` | null | :2511 |
| `m_stopEmittersOnHit` | `bool` | **true** | :2513 |
| `m_hitEffects` | `EffectList` | `new EffectList()` | :2515 |
| `m_hitWaterEffects` | `EffectList` | `new EffectList()` | :2517 |

`[Header("Bounce")]` @ :2519 — **there is NO field named `m_bounces`.**

| Field | Type | Default | Line |
|---|---|---|---|
| `m_bounce` | `bool` | false | :2520 |
| `m_bounceOnWater` | `bool` | false | :2522 |
| `m_bouncePower` | `float` `[Range(0,1)]` | 0.85f | :2524-2525 |
| `m_bounceRoughness` | `float` `[Range(0,1)]` | 0.3f | :2527-2528 |
| `m_maxBounces` | `int` `[Min(1)]` | **99** | :2530-2531 |
| `m_minBounceVel` | `float` `[Min(0.01)]` | 0.25f | :2533-2534 |

`[Header("Spawn on hit")]` @ :2536

| Field | Type | Default | Line |
|---|---|---|---|
| `m_respawnItemOnHit` | `bool` | false | :2537 |
| `m_spawnOnTtl` | `bool` | false | :2539 |
| `m_spawnOnHit` | `GameObject` | null | :2541 |
| `m_spawnOnHitChance` | `float` `[Range(0,1)]` | **1f** | :2543-2544 |
| `m_spawnCount` | `int` | 1 | :2546 |
| `m_randomSpawnOnHit` | `List<GameObject>` | `new List<GameObject>()` | :2548 |
| `m_randomSpawnOnHitCount` | `int` | 1 | :2550 |
| `m_randomSpawnSkipLava` | `bool` | false | :2552 |
| `m_showBreakMessage` | `bool` | false | :2554 |
| `m_staticHitOnly` | `bool` | false | :2556 |
| `m_groundHitOnly` | `bool` | false | :2558 |
| `m_spawnOffset` | `Vector3` | `Vector3.zero` | :2560 |
| `m_copyProjectileRotation` | `bool` | **true** | :2562 |
| `m_spawnRandomRotation` | `bool` | false | :2564 |
| `m_spawnFacingRotation` | `bool` | false | :2566 |
| `m_spawnOnHitEffects` | `EffectList` | `new EffectList()` | :2568 |
| `m_onHit` | `OnProjectileHit` (delegate) | null | :2570 |

`[Header("Projectile Spawning")]` @ :2572

| Field | Type | Default | Line |
|---|---|---|---|
| `m_spawnProjectileNewVelocity` | `bool` | false | :2573 |
| `m_spawnProjectileMinVel` | `float` | 1f | :2575 |
| `m_spawnProjectileMaxVel` | `float` | 5f | :2577 |
| `m_spawnProjectileRandomDir` | `float` `[Range(0,1)]` | 0 | :2579-2580 |
| `m_spawnProjectileHemisphereDir` | `bool` | false | :2582 |
| `m_projectilesInheritHitData` | `bool` | false | :2584 |
| `m_onlySpawnedProjectilesDealDamage` | `bool` | false | :2586 |
| `m_divideDamageBetweenProjectiles` | `bool` | false | :2588 |

`[Header("Rotate projectile")]` @ :2590

| Field | Type | Default | Line |
|---|---|---|---|
| `m_rotateVisual` | `float` | 0 | :2591 |
| `m_rotateVisualY` | `float` | 0 | :2593 |
| `m_rotateVisualZ` | `float` | 0 | :2595 |
| `m_visual` | `GameObject` | null | :2597 |
| `m_canChangeVisuals` | `bool` | false | :2599 |

### 3.2 Non-serialized / private runtime state

| Field | Type | Line |
|---|---|---|
| `m_statusEffectHash` | `private int` (hashed from `m_statusEffect` in `Awake` :2654-2657) | :2479 |
| `m_nview` | `private ZNetView` | :2601 |
| `m_attachParent` | `private GameObject` | :2603 |
| `m_attachParentOffset` | `private Vector3` | :2605 |
| `m_attachParentOffsetRot` | `private Quaternion` | :2607 |
| `m_hasLeftShields` | `private bool = true` | :2609 |
| `m_vel` | `private Vector3 = Vector3.zero` | :2611 |
| **`m_owner`** | **`private Character`** (NOT public) | :2613 |
| **`m_skill`** | **`[NonSerialized] public Skills.SkillType`** | :2615-2616 |
| `m_raiseSkillAmount` | `[NonSerialized] public float = 1f` | :2618-2619 |
| `m_weapon` | `private ItemDrop.ItemData` | :2621 |
| `m_ammo` | `private ItemDrop.ItemData` | :2623 |
| `m_spawnItem` | `[NonSerialized] public ItemDrop.ItemData` | :2625-2626 |
| `m_originalHitData` | `private HitData` | :2628 |
| `m_didHit` | `private bool` | :2630 |
| `m_bounceCount` | `private int` | :2632 |
| `m_didBounce` | `private bool` | :2634 |
| `m_changedVisual` | `private bool` | :2636 |
| `m_startPoint` | `[HideInInspector] public Vector3` | :2638-2639 |
| `m_haveStartPoint` | `private bool` | :2641 |
| `s_rayMaskSolids` | `private static int` | :2643 |
| `HasBeenOutsideShields` | `public bool => m_hasLeftShields` (get-only property) | :2645 |

### 3.3 `Projectile.Setup` — full signature and body semantics

```csharp
// :2811
public void Setup(Character owner, Vector3 velocity, float hitNoise, HitData hitData,
                  ItemDrop.ItemData item, ItemDrop.ItemData ammo)
{
    m_owner = owner;  m_vel = velocity;  m_ammo = ammo;  m_weapon = item;   // :2813-2816
    if (hitNoise >= 0f) m_hitNoise = hitNoise;                              // :2817-2820
    if (hitData != null) {                                                  // :2821
        m_originalHitData = hitData;
        m_damage        = hitData.m_damage;         // :2824
        m_blockable     = hitData.m_blockable;
        m_dodgeable     = hitData.m_dodgeable;
        m_attackForce   = hitData.m_pushForce;      // :2827
        m_backstabBonus = hitData.m_backstabBonus;
        m_healthReturn  = hitData.m_healthReturn;
        if (m_statusEffectHash != hitData.m_statusEffectHash) {              // :2830
            m_statusEffectHash = hitData.m_statusEffectHash;
            m_statusEffect = "";                                            // :2833
        }
        m_skill            = hitData.m_skill;                               // :2835
        m_raiseSkillAmount = hitData.m_skillRaiseAmount;
    }
    if (m_spawnOnHit != null && m_onlySpawnedProjectilesDealDamage) m_damage.Modify(0f);  // :2838-2841
    if (m_respawnItemOnHit) m_spawnItem = item;                             // :2842-2845
    ...
    m_hasLeftShields = !ShieldGenerator.IsInsideShield(base.transform.position);  // :2861
}
```
**Setup OVERWRITES `m_damage` wholesale from `hitData`.** Any prefab-authored `m_damage` on the Projectile component is discarded whenever the caller passes a non-null `hitData` (the normal weapon path). To buff projectile damage, patch the `HitData` in `Attack.FireProjectileBurst` (postfix on `Setup` also works — it runs after assignment).

### 3.4 `Projectile.OnHit` — full signature

```csharp
public void OnHit(Collider collider, Vector3 hitPoint, bool water, Vector3 normal)   // :2944
```
Flow: `FindHitObject` → `IsValidTarget` gate (:2959, early `return`, no hit consumed) → `IHitProjectile.OnProjectileHit` veto (:2966-2970) → bounce branch `if (flag && m_bounceCount < m_maxBounces && m_vel.magnitude > m_minBounceVel)` (:2972-2986, `return`s without damaging) → `if (m_aoe > 0f) DoAOE(...)` **else** single-target `HitData` build (:2993-3014) → effects (:3016-3023) → `SpawnOnHit` (:3024-3027) → `m_onHit?.Invoke(collider, hitPoint, water)` (:3028) → skill/adrenaline (:3033-3037) → `m_nview.InvokeRPC("RPC_OnHit")` (:3040) → `m_ttl = m_stayTTL` (:3041) → destroy unless `m_stayAfterHitStatic`/`Dynamic`.

The `HitData` OnHit builds (:2993-3008) sets `hitData.m_ranged = true;` (:3003) and `hitData.m_statusEffectHash = m_statusEffectHash;` (:3000).

Other `Projectile` methods:
```csharp
public Vector3 GetVelocity()                                          // :2790
private void UpdateRotation(float dt)                                 // :2803
private void DoAOE(Vector3 hitPoint, ref bool hitCharacter, ref bool didDamage)  // :2864
private bool IsValidTarget(IDestructible destr)                       // :2911
private void RPC_OnHit(long sender)                                   // :3060
private void RPC_Attach(long sender, ZDOID parent)                    // :3077
private void SpawnOnHit(GameObject go, Collider collider, Vector3 normal)  // :3107
public void SetStayTTL(float seconds)                                 // :3180
public void RPC_SetStayTTL(long sender, float sec)                    // :3197
public static GameObject FindHitObject(Collider collider)             // :3202
public void TriggerShieldsLeftFlag()                                  // :3216
public string GetTooltipString(int itemQuality) => ""                 // :2664
```
RPCs registered in `Awake` (:2658-2660): `"RPC_SetStayTTL"<float>`, `"RPC_OnHit"`, `"RPC_Attach"<ZDOID>`.

---

## 4. `class EffectList` and `EffectList.EffectData`

```csharp
[Serializable]                    // :29965
public class EffectList           // :29966
{
    [Serializable]                // :29968
    public class EffectData       // :29969   — a CLASS, not a struct
    {
        public GameObject m_prefab;                  // :29971
        public bool   m_enabled = true;              // :29973
        public int    m_variant = -1;                // :29975
        public bool   m_attach;                      // :29977
        public bool   m_follow;                      // :29979
        public bool   m_inheritParentRotation;       // :29981
        public bool   m_inheritParentScale;          // :29983
        public bool   m_multiplyParentVisualScale;   // :29985
        public bool   m_randomRotation;              // :29987
        public bool   m_scale;                       // :29989
        public string m_childTransform;              // :29991   (no initializer -> null)
    }

    public EffectData[] m_effectPrefabs = new EffectData[0];   // :29994

    public GameObject[] Create(Vector3 basePos, Quaternion baseRot,
                               Transform baseParent = null,
                               float scale = 1f, int variant = -1);   // :29996

    public bool HasEffects();                                          // :30080
}
```
`EffectData` is exactly 11 fields — the list in the request is complete and exact.

`Create` behavior worth knowing (:29999-30077):
- Skips when `!m_enabled` **or** `(variant >= 0 && effectData.m_variant >= 0 && variant != effectData.m_variant)` — :30002.
- `m_childTransform` only resolves when `baseParent != null` (`Utils.FindChild`) — :30009-30017.
- Uses raw `UnityEngine.Object.Instantiate(effectData.m_prefab, position, rotation)` — **:30026**. **No null check on `m_prefab`** → a null entry throws every time the effect fires. Always set `m_prefab`.
- `m_follow` adds a `UnityEngine.Animations.ParentConstraint` at runtime — :30063-30073.
- Returns `list.ToArray()`; never null, possibly empty — :30077.
- `HasEffects()` returns false if `m_effectPrefabs` is null/empty or no entry is `m_enabled` — :30080-30095.

`ItemDrop.ItemData.SharedData` `[Header("Effects")]` block (:58247-58268), all `public EffectList = new EffectList()`:
`m_hitEffect` :58248, `m_hitTerrainEffect` :58250, `m_blockEffect` :58252, `m_startEffect` :58254, **`m_holdStartEffect` :58256**, `m_equipEffect` :58258, `m_unequipEffect` :58260, `m_triggerEffect` :58262, `m_trailStartEffect` :58264, `m_buildEffect` :58266, `m_destroyEffect` :58268.

---

## 5. Eitr API

```csharp
// Character (base) — assembly_valheim.decompiled.cs
public bool TryUseEitr(float eitrUse = 0f)        // :9433   NON-virtual, defined on Character
public virtual void  AddEitr(float v)  {}         // :9959   base no-op
public virtual void  UseEitr(float eitr) {}       // :9963   base no-op  (param name "eitr")
public virtual bool  HaveEitr(float amount = 0f) => true;   // :9967   base ALWAYS TRUE
public virtual float GetMaxEitr() => 0f;          // :9413
public virtual float GetEitrPercentage() => 1f;   // :9418
```

`TryUseEitr` body (:9433-9453) — **it does NOT consume eitr**, it is a check with UI feedback only:
```csharp
if (eitrUse == 0f) return true;                                     // :9435-9438
if (GetMaxEitr() == 0f) { Message(MessageHud.MessageType.Center, "$hud_eitrrequired"); return false; }  // :9439-9443
if (!HaveEitr(eitrUse + 0.1f)) { if (IsPlayer()) Hud.instance.EitrBarEmptyFlash(); return false; }      // :9444-9451
return true;
```

Player overrides:
```csharp
public float GetEitr() => m_eitr;                              // :19416
public override float GetMaxEitr() => m_maxEitr;               // :19421
public override float GetEitrPercentage() => m_eitr/m_maxEitr; // :19426
public override void AddEitr(float v)                          // :19469   clamps to m_maxEitr
public override void UseEitr(float v)                          // :19557   PARAM NAME IS "v", NOT "eitr"
private void RPC_UseEitr(long sender, float v)                 // :19570
public override bool HaveEitr(float amount = 0f)               // :19583
```

`Player.UseEitr` is **networked** (:19557-19568):
```csharp
if (v != 0f && m_nview.IsValid()) {
    if (m_nview.IsOwner()) { RPC_UseEitr(0L, v); return; }
    m_nview.InvokeRPC("UseEitr", v);
}
```
`Player.HaveEitr` on a non-owned Player reads the ZDO: `m_nview.GetZDO().GetFloat(ZDOVars.s_eitr, m_maxEitr) > amount` (:19587). Note `>` not `>=`.

Harmony note: `UseEitr` is `virtual void UseEitr(float eitr)` on `Character` but `override void UseEitr(float v)` on `Player`. Harmony `__args`/named-injection by parameter name differs between the two — patch with positional `float ___`/`__0` or target `Player.UseEitr` explicitly.

---

## 6. Bow draw: `Player.UpdateAttackBowDraw`

```csharp
private void UpdateAttackBowDraw(ItemDrop.ItemData weapon, float dt)   // :17055
```
Called only from `Player.PlayerAttackInput(float dt)` (:16955), gated at **:16963-16966**:
```csharp
if (currentWeapon != null && currentWeapon.m_shared.m_attack.m_bowDraw)
    UpdateAttackBowDraw(currentWeapon, dt);
else { /* queued-attack path, :16969-16988 */ }
```
So **`m_bowDraw == true` on the PRIMARY attack is what swaps the whole input model to hold-to-charge.** With `m_bowDraw == false` the weapon uses `m_queuedAttackTimer` / `StartAttack` and never charges.

State field: `protected float m_attackDrawTime;` on `Humanoid` — **:12894**. Sentinels: `-1f` = suppressed, `0f` = idle/ready, `>0f` = charging.

Body (:17055-17115):
```csharp
if (m_blocking || InMinorAction() || IsAttached()) {                     // :17057
    m_attackDrawTime = -1f;                                             // :17059
    if (!string.IsNullOrEmpty(weapon.m_shared.m_attack.m_drawAnimationState))
        m_zanim.SetBool(weapon.m_shared.m_attack.m_drawAnimationState, value: false);
    return;
}
float num              = weapon.GetDrawStaminaDrain();                  // :17066
float drawEitrDrain    = weapon.GetDrawEitrDrain();                     // :17067
if ((double)GetAttackDrawPercentage() >= 1.0) num *= 0.5f;              // :17068-17071  full draw halves stamina drain
num += num * GetEquipmentAttackStaminaModifier();                       // :17072
m_seman.ModifyAttackStaminaUsage(num, ref num);                         // :17073
bool flag  = num <= 0f || HaveStamina();                                // :17074
bool flag2 = drawEitrDrain <= 0f || HaveEitr();                         // :17075   <-- eitr gate

if (m_attackDrawTime < 0f) { if (!m_attackHold) m_attackDrawTime = 0f; }        // :17076-17082
else if (m_attackHold && flag && m_attackDrawTime >= 0f) {                      // :17083
    if (m_attackDrawTime == 0f) {
        if (!weapon.m_shared.m_attack.StartDraw(this, weapon)) { m_attackDrawTime = -1f; return; }  // :17087-17091
        weapon.m_shared.m_holdStartEffect.Create(base.transform.position, Quaternion.identity, base.transform);  // :17092
    }
    m_attackDrawTime += Time.fixedDeltaTime;                                     // :17094
    if (!string.IsNullOrEmpty(weapon.m_shared.m_attack.m_drawAnimationState)) {
        m_zanim.SetBool(weapon.m_shared.m_attack.m_drawAnimationState, value: true);  // :17097
        m_zanim.SetFloat("drawpercent", GetAttackDrawPercentage());               // :17098
    }
    UseStamina(num * dt);                                                        // :17100
    UseEitr(drawEitrDrain * dt);                                                 // :17101
}
else if (m_attackDrawTime > 0f) {                                                // :17103   release
    if (flag && flag2) StartAttack(null, false);                                 // :17105-17108
    if (!string.IsNullOrEmpty(weapon.m_shared.m_attack.m_drawAnimationState))
        m_zanim.SetBool(weapon.m_shared.m_attack.m_drawAnimationState, value: false);
    m_attackDrawTime = 0f;                                                       // :17113
}
```

Charge percentage — `Humanoid.GetAttackDrawPercentage()` **:13154-13168**:
```csharp
ItemDrop.ItemData currentWeapon = GetCurrentWeapon();
if (currentWeapon != null && currentWeapon.m_shared.m_attack.m_bowDraw && m_attackDrawTime > 0f) {
    float skillFactor = GetSkillFactor(currentWeapon.m_shared.m_skillType);
    float num = Mathf.Lerp(currentWeapon.m_shared.m_attack.m_drawDurationMin,
                           currentWeapon.m_shared.m_attack.m_drawDurationMin * 0.2f, skillFactor);   // :13160
    if (!(num > 0f)) return 1f;                                                  // :13161-13164
    return Mathf.Clamp01(m_attackDrawTime / num);                                // :13165
}
return 0f;
```
**`m_drawDurationMin == 0f` ⇒ instantly 100% charged.** Skill maxes out shrinks the charge time to 20% of `m_drawDurationMin`.

Drain getters — `ItemDrop.ItemData`:
```csharp
public float GetDrawStaminaDrain()   // :58481-58490
{ if (m_shared.m_attack.m_drawStaminaDrain <= 0f) return 0f;
  float d = m_shared.m_attack.m_drawStaminaDrain;
  float skillFactor = Player.m_localPlayer.GetSkillFactor(m_shared.m_skillType);
  return d - d * 0.33f * skillFactor; }

public float GetDrawEitrDrain()      // :58492-58501   (same 33% skill discount)
{ if (m_shared.m_attack.m_drawEitrDrain <= 0f) return 0f;
  float d = m_shared.m_attack.m_drawEitrDrain;
  float skillFactor = Player.m_localPlayer.GetSkillFactor(m_shared.m_skillType);
  return d - d * 0.33f * skillFactor; }
```
Both dereference `Player.m_localPlayer` **unconditionally** — NRE if called on a dedicated server or before local player spawn, but only when the drain is `> 0f`.

`m_drawEitrDrain` semantics, precisely:
- **It never blocks starting or continuing the draw.** The hold branch is gated on `flag` (stamina) only (:17083). Eitr is spent every physics tick at :17101 with **no `HaveEitr` check** — `Player.UseEitr` → `RPC_UseEitr` clamps `m_eitr` to `0f` (:19574-19578), so eitr silently floors.
- **It blocks the RELEASE.** `flag2` (:17075) is only consulted at :17105 — out of eitr means the hold ends and `StartAttack` is never called: the shot is cancelled and the ammo/stamina spent during the draw is lost.
- Separately, `Attack.Start` still calls `character.TryUseEitr(GetAttackEitr())` at :1188 for the per-shot `m_attackEitr` cost, which returns `false` and aborts the attack when short.

Related: `Player.IsDrawingBow()` **:14452-14459** → `m_attackDrawTime > 0f && GetCurrentWeapon()?.m_shared.m_attack.m_bowDraw`. `Humanoid.UnequipItem` resets `m_attackDrawTime = 0f` (:14053). `UpdateWeaponLoading` (:16996) uses `m_reloadEitrDrain` via `TryUseEitr` before queueing a reload (:17002).


## GOTCHAS
- `Attack.AttackType.TriggerProjectile` (=5) is NEVER dispatched. The only switch on m_attackType is Attack.OnAttackTrigger at :1390-1405 and it has cases for Horizontal/Vertical/Area/Projectile/None only. A full-file grep for 'TriggerProjectile' returns exactly one hit — the enum member declaration at :843. Setting it produces a silent no-op attack.
- `Attack` is `[Serializable] public class Attack` (:814-815) — a PLAIN CLASS, not a MonoBehaviour and not a ScriptableObject. It is a reference type: `sharedData.m_attack = otherItem.m_shared.m_attack` ALIASES, it does not copy. Use `.Clone()` (:2412, MemberwiseClone) — but Clone is SHALLOW, so the cloned Attack shares the same EffectList and AnimationCurve object references as the template.
- Humanoid.StartAttack (:13098) clones the Attack every swing: `attack = currentWeapon.m_shared.m_attack.Clone()`. Mutating `Player.m_localPlayer.m_currentAttack.<field>` affects only the in-flight swing. Persistent changes must go to `ItemDrop.ItemData.SharedData.m_attack` / `m_secondaryAttack`.
- `Attack.m_holdStartEffect` DOES NOT EXIST. It is `ItemDrop.ItemData.SharedData.m_holdStartEffect` (:58256). Same for the status-effect fields: `m_attackStatusEffect` (:58171) and `m_attackStatusEffectChance` (:58173) are on SharedData, NOT on Attack.
- `Projectile` has NO field `m_bounces`. The bounce fields are m_bounce/m_bounceOnWater/m_bouncePower/m_bounceRoughness/m_maxBounces (int, default 99)/m_minBounceVel, plus `private int m_bounceCount` (:2632). Referencing `m_bounces` is a compile error.
- `Projectile.m_owner` is `private Character` (:2613) — reachable ONLY because the assembly is publicized (or via AccessTools/`___m_owner`). `Projectile.m_skill` is `[NonSerialized] public Skills.SkillType` (:2616), so it is public but never survives prefab serialization; it is set exclusively by Setup (:2835).
- `Projectile.m_statusEffect` is a **string** (:2477), hashed once in Awake into `private int m_statusEffectHash` (:2479, :2654-2657). Assigning a StatusEffect object is a compile error, and assigning the string AFTER Awake does nothing — you must set m_statusEffectHash directly (publicized) or set the string on the prefab before instantiation.
- `Projectile.Setup` OVERWRITES m_damage, m_blockable, m_dodgeable, m_attackForce, m_backstabBonus, m_healthReturn, m_statusEffectHash, m_skill and m_raiseSkillAmount from the incoming HitData (:2821-2837). Prefab-authored values on the Projectile component are discarded on any weapon-fired projectile. Buff via the HitData in Attack.FireProjectileBurst, or a Harmony POSTFIX on Setup.
- `Character.TryUseEitr` (:9433) does NOT consume eitr — it is a check plus UI flash. Actual spend is `UseEitr`. Also `Character.HaveEitr` base implementation returns TRUE unconditionally (:9967), so any non-Player Character always 'has' eitr.
- `Player.UseEitr(float v)` (:19557) is networked: owner path calls RPC_UseEitr(0L, v) locally, non-owner path does `m_nview.InvokeRPC("UseEitr", v)`. Calling it from a Harmony patch on a remote/non-owned Player fires a real RPC every invocation — do not call it in Update-frequency code on non-owned characters.
- Parameter-name mismatch: `Character.UseEitr(float eitr)` (:9963) vs `Player.UseEitr(float v)` (:19557). Harmony named-parameter injection (`float eitr`) will fail to bind on the Player override. Use `__0` / positional or patch the concrete override.
- `m_drawEitrDrain` never blocks the draw — eitr is drained every FixedUpdate at :17101 with no HaveEitr guard and floors at 0 (RPC_UseEitr :19574-19578). The eitr check `flag2` (:17075) is consulted ONLY at release (:17105), so running dry silently CANCELS the shot after the player already paid stamina and equipped ammo.
- `GetAttackDrawPercentage` returns 1f immediately when `m_drawDurationMin <= 0f` (:13161-13164). A bow with m_bowDraw=true and m_drawDurationMin=0 fires at full power with zero charge time.
- `ItemData.GetDrawStaminaDrain` / `GetDrawEitrDrain` (:58481, :58492) dereference `Player.m_localPlayer` with no null check whenever the drain is > 0f. NRE on a dedicated server or pre-spawn.
- `EffectList.Create` calls `UnityEngine.Object.Instantiate(effectData.m_prefab, ...)` with NO null check (:30026). An EffectData with a null m_prefab throws on every effect trigger. Also: a prefab you add to an EffectList must be registered in ZNetScene if it has a ZNetView, otherwise joining clients that instantiate it get an unrecognised-prefab error and the object never replicates.
- MULTIPLAYER — the status-effect roll is done on the ATTACKER (:1727 / :1882 / :2138) but applied on the VICTIM'S OWNER in Character.RPC_Damage (:8756-8767). Only the int hash crosses the wire. If your mod adds a StatusEffect that is not in the ObjectDB of the receiving client, `m_seman.AddStatusEffect(hash, ...)` finds nothing and the effect silently does not apply — the attacker sees no error. Register the StatusEffect in ObjectDB on EVERY client, not just the host.
- MULTIPLAYER — Attack/SharedData field edits are LOCAL ONLY. Nothing in Attack or SharedData is ZDO-synced; a joining client with different m_attackEitr/m_damageMultiplier/m_projectiles values gets desynced damage numbers and desynced projectile counts. Gate every value through ServerSync.
- MULTIPLAYER — `m_destroyPreviousProjectile` calls `ZNetScene.instance.Destroy(m_weapon.m_lastProjectile)` (:1700) which requires ownership; and `Projectile.OnHit` runs only under `m_nview.IsOwner()` (FixedUpdate gate :2676). Projectile logic is owner-authoritative — do not patch OnHit expecting it to run on every client.
- Field name typos in vanilla you must reproduce exactly: `m_hitPointtype` (lowercase t, :984) and the private `m_currentAttackCainLevel` ("Cain", :1106).
- In FireProjectileBurst the ammo item's Attack values are ADDED to the weapon's, not substituted: `num += ammoItem.m_shared.m_attack.m_projectileVel;` etc. (:1663-1671). Authoring a nonzero m_projectileVel on custom ammo stacks on top of the bow's.
- `m_attackHealthPercentage` (:893) is declared with `[Range(0f, 100f)]` — it is a 0-100 percentage, not 0-1.

## NOT FOUND
- Attack.m_holdStartEffect — NOT FOUND on class Attack. Exists only as ItemDrop.ItemData.SharedData.m_holdStartEffect at :58256.
- Attack.m_attackStatusEffect / Attack.m_attackStatusEffectChance — NOT FOUND on class Attack. They are ItemDrop.ItemData.SharedData fields at :58171 and :58173.
- Projectile.m_bounces — NOT FOUND. No such field. See m_bounce (:2520), m_maxBounces (:2531), private m_bounceCount (:2632).
- Projectile.m_statusEffect as a StatusEffect reference — NOT FOUND. The field is `public string m_statusEffect = ""` (:2477), hashed into private int m_statusEffectHash.
- Projectile.m_owner as a public field — NOT FOUND as public. It is `private Character m_owner;` (:2613); reachable only via the publicized assembly or AccessTools.
- Projectile.m_hitEffect (singular) — NOT FOUND. The fields are m_hitEffects (:2515) and m_hitWaterEffects (:2517), both plural.
- Attack.m_speedFactor consumer inside Attack — NOT FOUND. m_speedFactor (:901) and m_speedFactorRotation (:903) are declared on Attack but never read anywhere inside the Attack class body; they are consumed by Character/Humanoid movement code.
- A dispatch/switch case for AttackType.TriggerProjectile — NOT FOUND anywhere in the assembly. Only the enum member declaration at :843 exists.
- A public Attack constructor other than the implicit default — NOT FOUND. Instances are produced by Unity deserialization or Attack.Clone() (:2412).
- EffectList.EffectData as a struct — NOT FOUND. It is `[Serializable] public class EffectData` (:29968-29969), a reference type.