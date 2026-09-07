All citations: `C:\WubarrkCODING\libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs` (abbrev. `AV`).
Class spans: `StatusEffect` AV:26393–26739 · `SE_Stats` AV:25585–26352 · `SEMan` AV:24219–24752 · `SE_Rested` AV:25351–25446 · `SE_Shield` AV:25447–25584 · `ObjectDB` AV:90498+ · `Humanoid` AV:12806+

---

## 1. `class StatusEffect : ScriptableObject` — AV:26393

**Base type is `UnityEngine.ScriptableObject`, NOT MonoBehaviour.** No `Awake()`, no `Start()` — see gotchas.

### Nested enum — AV:26395
```csharp
public enum StatusAttribute
{
    None = 0, ColdResistance = 1, DoubleImpactDamage = 2, SailingPower = 4, TamingBoost = 8
}
```

### EVERY field (exact order, exact modifiers)
| Line | Decl |
|---|---|
| 26405 | `[Header("__Common__")] public string m_name = "";` |
| 26407 | `public string m_category = "";` |
| 26409 | `public Sprite m_icon;` |
| 26411 | `public bool m_flashIcon;` |
| 26413 | `public bool m_cooldownIcon;` |
| 26416 | `[TextArea] public string m_tooltip = "";` |
| 26419 | `[BitMask(typeof(StatusAttribute))] public StatusAttribute m_attributes;` |
| 26421 | `public MessageHud.MessageType m_startMessageType = MessageHud.MessageType.TopLeft;` |
| 26423 | `public string m_startMessage = "";` |
| 26425 | `public MessageHud.MessageType m_stopMessageType = MessageHud.MessageType.TopLeft;` |
| 26427 | `public string m_stopMessage = "";` |
| 26429 | `public MessageHud.MessageType m_repeatMessageType = MessageHud.MessageType.TopLeft;` |
| 26431 | `public string m_repeatMessage = "";` |
| 26433 | `public float m_repeatInterval;` |
| 26435 | `public float m_ttl;` |
| 26437 | `public EffectList m_startEffects = new EffectList();` |
| 26439 | `public EffectList m_stopEffects = new EffectList();` |
| 26442 | `[Header("__Guardian power__")] public float m_cooldown;` |
| 26444 | `public string m_activationAnimation = "gpower";` |
| 26447 | `[NonSerialized] public bool m_isNew = true;` |
| 26449 | `private float m_msgTimer;` |
| 26451 | `public Character m_character;` |
| 26453 | `protected float m_time;` |
| 26455 | `protected GameObject[] m_startEffectInstances;` |
| 26457 | `private int m_nameHash;` |

`m_flags` — **NOT FOUND.** No such field on StatusEffect.

### Methods — exact signatures
```csharp
// AV:26459  NOT virtual, NOT overridable
public StatusEffect Clone()                                    // => MemberwiseClone() as StatusEffect

public virtual bool CanAdd(Character character)                                     // AV:26464 (returns true)
public virtual void Setup(Character character)                                      // AV:26469
public virtual void SetAttacker(Character attacker)                                 // AV:26479
public virtual string GetTooltipString()                                            // AV:26483 (returns m_tooltip)
protected virtual void OnApplicationQuit()                                          // AV:26488
public virtual void OnDestroy()                                                     // AV:26493
protected void TriggerStartEffects()                                                // AV:26498 (non-virtual)
private void RemoveStartEffects()                                                   // AV:26511
public virtual void Stop()                                                          // AV:26533
public virtual void UpdateStatusEffect(float dt)                                    // AV:26543
public virtual bool IsDone()                                                        // AV:26557
public virtual void ResetTime()                                                     // AV:26566
public virtual void SetLevel(int itemLevel, float skillLevel)                       // AV:26571
public float GetDuration()                                                          // AV:26575 -> m_time
public float GetRemaningTime()                                                      // AV:26580 (sic, "Remaning") -> m_ttl - m_time
public virtual string GetIconText()                                                 // AV:26585
public static string GetTimeString(float time, bool sufix = false, bool alwaysShowMinutes = false) // AV:26594 (sic, "sufix")
public bool HaveAttribute(StatusAttribute value)                                    // AV:26726
public int NameHash()                                                               // AV:26731
```

### EVERY `Modify*` virtual — exact `ref`/param lists
```csharp
public virtual void ModifyAttack(Skills.SkillType skill, ref HitData hitData)                  // AV:26618
public virtual void ModifyHealthRegen(ref float regenMultiplier)                               // AV:26622
public virtual void ModifyStaminaRegen(ref float staminaRegen)                                 // AV:26626
public virtual void ModifyEitrRegen(ref float eitrRegen)                                       // AV:26630
public virtual void ModifyDamageMods(ref HitData.DamageModifiers modifiers)                    // AV:26634
public virtual void ModifyTimedBlockBonus(ref float timedBlockBonus)                           // AV:26638
public virtual void ModifyArmorMods(ref float armor)                                           // AV:26642
public virtual void ModifyRaiseSkill(Skills.SkillType skill, ref float value)                  // AV:26646
public virtual void ModifySkillLevel(Skills.SkillType skill, ref float level)                  // AV:26650
public virtual void ModifySpeed(float baseSpeed, ref float speed, Character character, Vector3 dir) // AV:26654  (4 params!)
public virtual void ModifyJump(Vector3 baseJump, ref Vector3 jump)                             // AV:26658
public virtual void ModifyWalkVelocity(ref Vector3 vel)                                        // AV:26662
public virtual void ModifyFallDamage(float baseDamage, ref float damage)                       // AV:26666
public virtual void ModifyNoise(float baseNoise, ref float noise)                              // AV:26670
public virtual void ModifyStealth(float baseStealth, ref float stealth)                        // AV:26674
public virtual void ModifyMaxCarryWeight(float baseLimit, ref float limit)                     // AV:26678
public virtual void ModifyRunStaminaDrain(float baseDrain, ref float drain, Vector3 dir)       // AV:26682  (3 params!)
public virtual void ModifyJumpStaminaUsage(float baseStaminaUse, ref float staminaUse)         // AV:26686
public virtual void ModifyAttackStaminaUsage(float baseStaminaUse, ref float staminaUse)       // AV:26690
public virtual void ModifyBlockStaminaUsage(float baseStaminaUse, ref float staminaUse)        // AV:26694
public virtual void ModifyAdrenaline(float baseValue, ref float use)                           // AV:26698
public virtual void ModifyStagger(float baseValue, ref float use)                              // AV:26702
public virtual void ModifyDodgeStaminaUsage(float baseStaminaUse, ref float staminaUse)        // AV:26706
public virtual void ModifySwimStaminaUsage(float baseStaminaUse, ref float staminaUse)         // AV:26710
public virtual void ModifyHomeItemStaminaUsage(float baseStaminaUse, ref float staminaUse)     // AV:26714
public virtual void ModifySneakStaminaUsage(float baseStaminaUse, ref float staminaUse)        // AV:26718
public virtual void OnDamaged(HitData hit, Character attacker)                                 // AV:26722
```
`ModifyAttackSpeed` — **NOT FOUND** anywhere in the assembly.

### CRITICAL: what `NameHash()` hashes — AV:26731
```csharp
public int NameHash()
{
    if (m_nameHash == 0)
    {
        m_nameHash = base.name.GetStableHashCode();
    }
    return m_nameHash;
}
```
**It hashes `base.name` — i.e. `UnityEngine.Object.name` (the ScriptableObject asset name). NOT `m_name`.**
`m_name` is only the localization token used for display. The value is cached in `private int m_nameHash` (AV:26457) on first call.

`GetStableHashCode` (string extension) is **NOT declared in assembly_valheim** — it lives in `assembly_utils.dll` (`StringExtensionMethods`). Reference it, don't reimplement.

---

## 2. `class SE_Stats : StatusEffect` — AV:25585

### EVERY field with declared type + literal default
| Line | Decl | Default |
|---|---|---|
| 25589 | `public float m_tickInterval;` | 0 |
| 25591 | `public float m_healthPerTickMinHealthPercentage;` | 0 |
| 25593 | `public float m_healthPerTick;` | 0 |
| 25595 | `public HitData.HitType m_hitType;` | 0 |
| 25598 | `public float m_healthUpFront;` | 0 |
| 25600 | `public float m_healthOverTime;` | 0 |
| 25602 | `public float m_healthOverTimeDuration;` | 0 |
| 25604 | `public float m_healthOverTimeInterval = 5f;` | **5f** |
| 25607 | `public float m_staminaUpFront;` | 0 |
| 25609 | `public float m_staminaOverTime;` | 0 |
| 25611 | `public float m_staminaOverTimeDuration;` | 0 |
| 25613 | `public bool m_staminaOverTimeIsFraction;` | false |
| 25615 | `public float m_staminaDrainPerSec;` | 0 |
| 25617 | `public float m_runStaminaDrainModifier;` | 0 |
| 25619 | `public float m_jumpStaminaUseModifier;` | 0 |
| 25621 | `public float m_attackStaminaUseModifier;` | 0 |
| 25623 | `public float m_blockStaminaUseModifier;` | 0 |
| 25625 | `public float m_blockStaminaUseFlatValue;` | 0 |
| 25627 | `public float m_dodgeStaminaUseModifier;` | 0 |
| 25629 | `public float m_swimStaminaUseModifier;` | 0 |
| 25631 | `public float m_homeItemStaminaUseModifier;` | 0 |
| 25633 | `public float m_sneakStaminaUseModifier;` | 0 |
| 25635 | `public float m_runStaminaUseModifier;` | 0 (declared, **never read anywhere in SE_Stats** — dead field) |
| 25638 | `public float m_adrenalineUpFront;` | 0 |
| 25640 | `public float m_adrenalineModifier;` | 0 |
| 25643 | `public float m_staggerModifier;` | 0 |
| 25645 | `public float m_timedBlockBonus;` | 0 |
| 25648 | `public float m_eitrUpFront;` | 0 |
| 25650 | `public float m_eitrOverTime;` | 0 |
| 25652 | `public float m_eitrOverTimeDuration;` | 0 |
| 25655 | `public float m_healthRegenMultiplier = 1f;` | **1f** |
| 25657 | `public float m_staminaRegenMultiplier = 1f;` | **1f** |
| 25659 | `public float m_eitrRegenMultiplier = 1f;` | **1f** |
| 25662 | `public float m_addArmor;` | 0 |
| 25664 | `public float m_armorMultiplier;` | **0f** (not 1f — it is `armor *= 1f + m_armorMultiplier`, and skipped entirely when 0) |
| 25667 | `public Skills.SkillType m_raiseSkill;` | 0 (`None`) |
| 25669 | `public float m_raiseSkillModifier;` | 0 |
| 25672 | `public Skills.SkillType m_skillLevel;` | 0 |
| 25674 | `public float m_skillLevelModifier;` | 0 |
| 25676 | `public Skills.SkillType m_skillLevel2;` | 0 |
| 25678 | `public float m_skillLevelModifier2;` | 0 |
| 25681 | `public List<HitData.DamageModPair> m_mods = new List<HitData.DamageModPair>();` | empty list |
| 25684 | `public Skills.SkillType m_modifyAttackSkill;` | 0 |
| 25686 | `public float m_damageModifier = 1f;` | **1f** |
| 25688 | `public HitData.DamageTypes m_percentigeDamageModifiers;` | default struct |
| 25691 | `public float m_noiseModifier;` | 0 |
| 25693 | `public float m_stealthModifier;` | 0 |
| 25696 | `public float m_addMaxCarryWeight;` | 0 |
| 25699 | `public float m_speedModifier;` | 0 |
| 25701 | `public float m_swimSpeedModifier;` | 0 |
| 25703 | `public Vector3 m_jumpModifier;` | (0,0,0) |
| 25706 | `public float m_maxMaxFallSpeed;` | 0 |
| 25708 | `public float m_fallDamageModifier;` | 0 |
| 25711 | `public float m_windMovementModifier;` | 0 |
| 25713 | `public float m_windRunStaminaModifier;` | 0 |
| 25716 | `public GameObject m_pheromoneTarget;` | null |
| 25718 | `public float m_pheromoneSpawnChanceOverride;` | 0 |
| 25720 | `public int m_pheromoneSpawnMinLevel;` | 0 |
| 25722 | `public float m_pheromoneLevelUpMultiplier = 1f;` | **1f** |
| 25724 | `public int m_pheromoneMaxInstanceOverride;` | 0 |
| 25726 | `public bool m_pheromoneFlee;` | false |
| 25728 | `private float m_tickTimer;` | — |
| 25730 | `private float m_healthOverTimeTimer;` | — |
| 25732 | `private float m_healthOverTimeTicks;` | — |
| 25734 | `private float m_healthOverTimeTickHP;` | — |

### SPELLING CONFIRMATIONS (do not "fix" these)
- **`m_percentigeDamageModifiers`** — AV:25688. The misspelling is REAL and shipping. Confirmed independently in the values dump: keys `m_percentigeDamageModifiers.m_damage`, `.m_blunt`, `.m_slash`, `.m_pierce`, `.m_chop` in `Values_Dump.json`. Type is `HitData.DamageTypes` (a struct of floats), **not** a list.
- **`m_homeItemsStaminaModifier` is NOT an SE_Stats field.** The SE_Stats field is `m_homeItemStaminaUseModifier` (AV:25631, no "s" after Item, and it ends in `UseModifier`). `m_homeItemsStaminaModifier` exists only on `ItemDrop.ItemData.SharedData` at AV:58063.
- `GetRemaningTime` (StatusEffect AV:26580) and `sufix` param (AV:26594) are also genuine typos.

### SE_Stats overrides
```csharp
public override void Setup(Character character)                            // AV:25736
public void StartupEffects()                                               // AV:25759 (non-virtual, public)
public override void ResetTime()                                           // AV:25779 (calls StartupEffects() then base)
public override void UpdateStatusEffect(float dt)                          // AV:25785
public override void ModifyHealthRegen(ref float regenMultiplier)          // AV:25844
public override void ModifyStaminaRegen(ref float staminaRegen)            // AV:25856
public override void ModifyEitrRegen(ref float staminaRegen)               // AV:25868 (param named staminaRegen)
public override void ModifyDamageMods(ref HitData.DamageModifiers modifiers)// AV:25880 -> modifiers.Apply(m_mods)
public override void ModifyTimedBlockBonus(ref float timedBlockBonus)      // AV:25885
public override void ModifyRaiseSkill(Skills.SkillType skill, ref float value)  // AV:25893
public override void ModifySkillLevel(Skills.SkillType skill, ref float value)  // AV:25901 (param renamed value)
public override void ModifyNoise(float baseNoise, ref float noise)         // AV:25916
public override void ModifyStealth(float baseStealth, ref float stealth)   // AV:25921
public override void ModifyMaxCarryWeight(float baseLimit, ref float limit)// AV:25926
public override void ModifyAttack(Skills.SkillType skill, ref HitData hitData) // AV:25935
public override void ModifyRunStaminaDrain(float baseDrain, ref float drain, Vector3 dir) // AV:25944
public override void ModifyJumpStaminaUsage(...)   // AV:25957
public override void ModifyAttackStaminaUsage(...) // AV:25962
public override void ModifyBlockStaminaUsage(...)  // AV:25967
public override void ModifyAdrenaline(...)         // AV:25973
public override void ModifyStagger(...)            // AV:25978
public override void ModifyDodgeStaminaUsage(...)  // AV:25983
public override void ModifySwimStaminaUsage(...)   // AV:25988
public override void ModifyHomeItemStaminaUsage(...) // AV:25993
public override void ModifySneakStaminaUsage(...)  // AV:25998
public override void ModifyArmorMods(ref float armor)  // AV:26003
public override void ModifySpeed(float baseSpeed, ref float speed, Character character, Vector3 dir) // AV:26012
public override void ModifyJump(Vector3 baseJump, ref Vector3 jump)        // AV:26037
public override void ModifyWalkVelocity(ref Vector3 vel)                   // AV:26042 (this is where m_maxMaxFallSpeed applies)
public override void ModifyFallDamage(float baseDamage, ref float damage)  // AV:26050
public override string GetTooltipString()                                  // AV:26059
public static string GetDamageModifiersTooltipString(List<HitData.DamageModPair> mods) // AV:26278
```
SE_Stats does **NOT** override `CanAdd`, `IsDone`, `Stop`, `SetLevel`, `OnDamaged`, `SetAttacker`.

Key math to know (AV:25844–25878): regen multipliers are **additive above 1, multiplicative at/below 1**:
```csharp
if (m_healthRegenMultiplier > 1f) regenMultiplier += m_healthRegenMultiplier - 1f;
else                              regenMultiplier *= m_healthRegenMultiplier;
```
`ModifyAttack` (AV:25935) applies `m_percentigeDamageModifiers` **unconditionally**, but `m_damageModifier` only when `skill == m_modifyAttackSkill || m_modifyAttackSkill == Skills.SkillType.All`.

---

## 3. `class SEMan` — AV:24219 (plain C# class, NOT a Component; get via `Character.GetSEMan()` AV:10654)

### State (all private; reachable only because you publicize)
```csharp
private readonly HashSet<int>        m_statusEffectsHashSet   = new HashSet<int>();     // AV:24221
private readonly List<StatusEffect>  m_statusEffects          = new List<StatusEffect>(); // AV:24223
private readonly List<StatusEffect>  m_removeStatusEffects    = new List<StatusEffect>(); // AV:24225
private int      m_statusEffectAttributes;        // AV:24227
private int      m_statusEffectAttributesOld = -1;// AV:24229
private Character m_character;                    // AV:24231
private ZNetView  m_nview;                        // AV:24233
public SEMan(Character character, ZNetView nview) // AV:24267 — registers RPC_AddStatusEffect<int,bool,int,float>
```
Static cached hashes AV:24235–24265: `s_statusEffectRested, s_statusEffectEncumbered, s_statusEffectSoftDeath, s_statusEffectWet, s_statusEffectShelter, s_statusEffectCampFire, s_statusEffectResting, s_statusEffectCold, s_statusEffectFreezing, s_statusEffectBurning, s_statusEffectFrost, s_statusEffectLightning, s_statusEffectPoison, s_statusEffectSmoked, s_statusEffectSpirit, s_statusEffectTared` — all `public static readonly int`.

### Add / remove / query
```csharp
// AV:24352 — hash overload. OWNERSHIP-AWARE: if !IsOwner it fires an RPC and RETURNS NULL.
public StatusEffect AddStatusEffect(int nameHash, bool resetTime = false, int itemLevel = 0, float skillLevel = 0f)

// AV:24394 — instance overload. NO ownership check, NO RPC. Purely local.
public StatusEffect AddStatusEffect(StatusEffect statusEffect, bool resetTime = false, int itemLevel = 0, float skillLevel = 0f)

private StatusEffect Internal_AddStatusEffect(int nameHash, bool resetTime, int itemLevel, float skillLevel) // AV:24374
private void RPC_AddStatusEffect(long sender, int nameHash, bool resetTime, int itemLevel, float skillLevel) // AV:24366

public bool RemoveStatusEffect(StatusEffect se, bool quiet = false)  // AV:24422 -> RemoveStatusEffect(se.NameHash(), quiet)
public bool RemoveStatusEffect(int nameHash, bool quiet = false)     // AV:24427  (LOCAL ONLY — no RPC)
public void RemoveAllStatusEffects(bool quiet = false)               // AV:24451

public bool HaveStatusEffect(int nameHash)                           // AV:24496 -> m_statusEffectsHashSet.Contains(nameHash)
public bool HaveStatusEffectCategory(string cat)                     // AV:24466
public bool HaveStatusAttribute(StatusEffect.StatusAttribute value)  // AV:24483 (reads ZDOVars.s_seAttrib when not owner)
public StatusEffect GetStatusEffect(int nameHash)                    // AV:24506
public List<StatusEffect> GetStatusEffects()                         // AV:24501 (returns the LIVE backing list)
public void GetHUDStatusEffects(List<StatusEffect> effects)          // AV:24526 (only those with m_icon)
public void Update(ZDO zdo, float dt)                                // AV:24318
public void OnDestroy()                                              // AV:24274
```
There is **no** `AddStatusEffect(string)` / `RemoveStatusEffect(string)` / `HaveStatusEffect(string)` overload — **NOT FOUND.** Hash your name yourself with `"Name".GetStableHashCode()`.

### Aggregation methods (all `public void`, instance)
```csharp
public void ApplyStatusEffectSpeedMods(ref float speed, Vector3 dir)          // AV:24284
public void ApplyStatusEffectJumpMods(ref Vector3 jump)                       // AV:24293
public void ApplyDamageMods(ref HitData.DamageModifiers mods)                 // AV:24302
public void ApplyArmorMods(ref float armor)                                   // AV:24310
public void ModifyFallDamage(float baseDamage, ref float damage)              // AV:24537
public void ModifyWalkVelocity(ref Vector3 vel)                               // AV:24545
public void ModifyNoise(float baseNoise, ref float noise)                     // AV:24553
public void ModifySkillLevel(Skills.SkillType skill, ref float level)         // AV:24561
public void ModifyRaiseSkill(Skills.SkillType skill, ref float multiplier)    // AV:24569
public void ModifyStaminaRegen(ref float staminaMultiplier)                   // AV:24577
public void ModifyEitrRegen(ref float eitrMultiplier)                         // AV:24585
public void ModifyHealthRegen(ref float regenMultiplier)                      // AV:24593
public void ModifyMaxCarryWeight(float baseLimit, ref float limit)            // AV:24601
public void ModifyStealth(float baseStealth, ref float stealth)               // AV:24609
public void ModifyAttack(Skills.SkillType skill, ref HitData hitData)         // AV:24617
public void ModifyTimedBlockBonus(ref float timedBlockBonus)                  // AV:24625
public void ModifyRunStaminaDrain(float baseDrain, ref float drain, Vector3 dir, bool minZero = true) // AV:24633
public void ModifyJumpStaminaUsage(float baseStaminaUse, ref float staminaUse, bool minZero = true)   // AV:24645
public void ModifyAttackStaminaUsage(float baseStaminaUse, ref float staminaUse, bool minZero = true) // AV:24657
public void ModifyBlockStaminaUsage(float baseStaminaUse, ref float staminaUse, bool minZero = true)  // AV:24669
public void ModifyAdrenaline(float baseValue, ref float use)                  // AV:24681
public void ModifyStagger(float baseValue, ref float use)                     // AV:24689
public void ModifyDodgeStaminaUsage(float, ref float, bool minZero = true)    // AV:24697
public void ModifySwimStaminaUsage(float, ref float, bool minZero = true)     // AV:24709
public void ModifyHomeItemStaminaUsage(float, ref float, bool minZero = true) // AV:24721
public void ModifySneakStaminaUsage(float, ref float, bool minZero = true)    // AV:24733
public void OnDamaged(HitData hit, Character attacker)                        // AV:24745
```
Note: `SEMan.ModifyNoise/ModifyStealth/ModifyAttack` take **no** `minZero` param; the stamina-usage ones do.

### `SEMan.Update` — the lifecycle you must not fight — AV:24318
```csharp
statusEffect.UpdateStatusEffect(dt);
if (statusEffect.IsDone()) m_removeStatusEffects.Add(statusEffect);
else m_statusEffectAttributes |= (int)statusEffect.m_attributes;
...
removeStatusEffect.Stop();
m_statusEffects.Remove(removeStatusEffect);
m_statusEffectsHashSet.Remove(removeStatusEffect.NameHash());
...
if (m_statusEffectAttributes != m_statusEffectAttributesOld) zdo.Set(ZDOVars.s_seAttrib, m_statusEffectAttributes);
```

---

## 4. `ObjectDB` — how a custom StatusEffect becomes resolvable
```csharp
public List<StatusEffect> m_StatusEffects = new List<StatusEffect>();  // AV:90502 (public instance field)
public static ObjectDB instance => m_instance;                          // AV:90512
private void Awake() { m_instance = this; UpdateRegisters(); }           // AV:90514
public void CopyOtherDB(ObjectDB other)                                 // AV:90520
{
    m_items = other.m_items; m_recipes = other.m_recipes;
    m_StatusEffects = other.m_StatusEffects;                            // AV:90524 — WHOLESALE REPLACE
    UpdateRegisters();
}
public StatusEffect GetStatusEffect(int nameHash)                       // AV:90543 — linear scan of m_StatusEffects
```
Registration path with no Jotunn: `ScriptableObject.CreateInstance<YourSE>()`, set `.name`, then `ObjectDB.instance.m_StatusEffects.Add(se)` — patched as a **Harmony postfix on BOTH `ObjectDB.Awake` and `ObjectDB.CopyOtherDB`** (see gotchas). `UpdateRegisters()` (AV:90528) does NOT index status effects, so no re-index call is needed.

---

## 5. `Humanoid` equipment/set effects — AV:12806

```csharp
private readonly HashSet<StatusEffect> m_equipmentStatusEffects = new HashSet<StatusEffect>(); // AV:12920

private void SetupEquipment()                 // AV:14175 — calls UpdateEquipmentStatusEffects() at AV:14183,
                                              //   but ONLY inside `if (m_nview.GetZDO() != null)` (AV:14181)
private void UpdateEquipmentStatusEffects()   // AV:14230  — PRIVATE, instance
private bool HaveSetEffect(ItemDrop.ItemData item)  // AV:14311 — PRIVATE, instance
private int  GetSetCount(string setName)            // AV:14328 — PRIVATE, instance
```
All three are **private** — reachable only via the publicized assembly (or `AccessTools`).

`UpdateEquipmentStatusEffects` (AV:14230) builds `HashSet<StatusEffect>` from `m_shared.m_equipStatusEffect` on **8 slots**: left, right, chest, leg, helmet, shoulder, utility, **trinket** (AV:14261). Then adds `m_shared.m_setStatusEffect` for slots where `HaveSetEffect(...)` is true — **only 7 slots**: left, right, chest, leg, helmet, shoulder, utility (AV:14265–14292). **The trinket slot is NOT checked for set effects.** Diffing:
```csharp
foreach (StatusEffect equipmentStatusEffect in m_equipmentStatusEffects)   // AV:14293
    if (!hashSet.Contains(equipmentStatusEffect)) m_seman.RemoveStatusEffect(equipmentStatusEffect.NameHash());
foreach (StatusEffect item in hashSet)                                     // AV:14300
    if (!m_equipmentStatusEffects.Contains(item)) m_seman.AddStatusEffect(item);   // INSTANCE overload -> local, no RPC
m_equipmentStatusEffects.Clear();
m_equipmentStatusEffects.UnionWith(hashSet);
```

`HaveSetEffect` — AV:14311:
```csharp
if (item == null) return false;
if (item.m_shared.m_setStatusEffect == null || item.m_shared.m_setName.Length == 0 || item.m_shared.m_setSize <= 1) return false;
if (GetSetCount(item.m_shared.m_setName) >= item.m_shared.m_setSize) return true;
return false;
```

**`GetSetCount` — DOES IT COUNT THE WEAPON SLOT? YES.** AV:14328, verbatim slot list:
```csharp
private int GetSetCount(string setName)
{
    int num = 0;
    if (m_leftItem     != null && m_leftItem.m_shared.m_setName     == setName) num++;   // AV:14331  <-- offhand/shield
    if (m_rightItem    != null && m_rightItem.m_shared.m_setName    == setName) num++;   // AV:14335  <-- WEAPON
    if (m_chestItem    != null && m_chestItem.m_shared.m_setName    == setName) num++;   // AV:14339
    if (m_legItem      != null && m_legItem.m_shared.m_setName      == setName) num++;   // AV:14343
    if (m_helmetItem   != null && m_helmetItem.m_shared.m_setName   == setName) num++;   // AV:14347
    if (m_shoulderItem != null && m_shoulderItem.m_shared.m_setName == setName) num++;   // AV:14351
    if (m_utilityItem  != null && m_utilityItem.m_shared.m_setName  == setName) num++;   // AV:14355
    if (m_trinketItem  != null && m_trinketItem.m_shared.m_setName  == setName) num++;   // AV:14359  <-- counted here
    return num;
}
```
So `GetSetCount` counts **8** slots including both hands and trinket, while the set-effect *application* loop only queries 7 (no trinket). A trinket bearing the same `m_setName` therefore contributes to the count but can never itself grant the set effect.

Related item fields (`ItemDrop.ItemData.SharedData`):
```csharp
public string m_setName = "";              // AV:58050
public int m_setSize;                      // AV:58052
public StatusEffect m_setStatusEffect;     // AV:58054
public StatusEffect m_equipStatusEffect;   // AV:58056
public float m_homeItemsStaminaModifier;   // AV:58063  (item field, NOT SE_Stats)
```

---

## 6. `SE_Rested.cs` — the exact shape a custom subclass must follow

File `C:\WubarrkCODING\libs-Tools\SE_Rested.cs` is UTF-16LE and matches AV:25351–25446 verbatim. Canonical shape:

```csharp
using System.Collections.Generic;
using UnityEngine;

public class SE_Rested : SE_Stats            // subclass SE_Stats (or StatusEffect) — NO MonoBehaviour, NO ctor
{
    [Header("__SE_Rested__")]
    public float m_baseTTL = 300f;                                    // AV:25354 — public inspector fields
    public float m_TTLPerComfortLevel = 60f;                          // AV:25356
    private const float c_ComfortRadius = 10f;                        // AV:25358
    private float m_timeSinceComfortUpdate;                           // AV:25360 — instance state, per-clone
    private static readonly List<Piece> s_tempPieces = new List<Piece>(); // AV:25362 — static scratch buffer

    public override void Setup(Character character)                   // AV:25364
    {
        base.Setup(character);                                        // ALWAYS call base first
        UpdateTTL();
        Player player = m_character as Player;
        m_character.Message(MessageHud.MessageType.Center,
            "$se_rested_start ($se_rested_comfort:" + player.GetComfortLevel() + ")");
    }

    public override void UpdateStatusEffect(float dt)                 // AV:25372
    {
        base.UpdateStatusEffect(dt);                                  // base advances m_time and repeat msgs
        m_timeSinceComfortUpdate -= dt;
    }

    public override void ResetTime()                                  // AV:25378
    {
        UpdateTTL();                                                  // NOTE: deliberately does NOT call base.ResetTime()
    }

    private void UpdateTTL()                                          // AV:25383
    {
        Player player = m_character as Player;
        float num  = m_baseTTL + (float)(player.GetComfortLevel() - 1) * m_TTLPerComfortLevel;
        float num2 = m_ttl - m_time;                                  // m_ttl public, m_time protected
        if (num > num2) { m_ttl = num; m_time = 0f; }                 // writes protected m_time directly — legal in subclass
    }

    private static int PieceComfortSort(Piece x, Piece y)             // AV:25395
    public static int CalculateComfortLevel(Player player)            // AV:~25409 (overload -> InShelter/position)
    public static int CalculateComfortLevel(bool inShelter, Vector3 position)
    private static List<Piece> GetNearbyComfortPieces(Vector3 point)  // uses Piece.GetAllComfortPiecesInRadius(point, 10f, s_tempPieces)
}
```
**Rules extracted:** no constructor, no `Awake`/`Start` (base is a ScriptableObject and nothing calls them), all tunables are `public` serialized fields with inline defaults, `[Header]` groups them, per-instance mutable state is `private` non-static, shared scratch is `private static readonly`, and every override chains to `base.` except where the vanilla author deliberately replaces behavior (`ResetTime`).



## GOTCHAS
- NameHash() hashes base.name (UnityEngine.Object.name), NOT m_name — AV:26735 `m_nameHash = base.name.GetStableHashCode();`. If you `ScriptableObject.CreateInstance<MySE>()` and forget `se.name = "MyEffect";`, Unity gives the object the type name (or empty), and every AddStatusEffect("MyEffect".GetStableHashCode()) silently resolves to null via ObjectDB.GetStatusEffect. Set .name BEFORE anything calls NameHash().
- NameHash() caches into `private int m_nameHash` (AV:26457) on first call and never recomputes. Renaming `.name` after any NameHash()/AddStatusEffect/GetStatusEffect call leaves a stale hash. You must zero m_nameHash (reachable only via publicized assembly / AccessTools) to force recompute.
- StatusEffect.Clone() is `MemberwiseClone()` (AV:26461) — a SHALLOW copy, and it is NOT virtual so you cannot override it. Reference-type fields are SHARED with the template asset: m_mods (List<HitData.DamageModPair>), m_startEffects/m_stopEffects (EffectList), m_icon. Mutating `m_mods` on a live per-character instance mutates the ScriptableObject for every character and persists for the session. Only mutate value-type fields (floats, structs) on live instances.
- SEMan.AddStatusEffect(StatusEffect, ...) at AV:24394 does NO ownership check and sends NO RPC — it applies purely locally. Only the int-hash overload at AV:24352 is ownership-aware (`if (m_nview.IsOwner()) Internal_Add... else InvokeRPC("RPC_AddStatusEffect", ...)`). Applying an effect to a remote-owned Character via the instance overload desyncs: the owning client never gets it. Use the hash overload for anything cross-character.
- MULTIPLAYER: the hash overload RETURNS NULL when you are not the owner (AV:24363) even though the RPC was sent. Code that does `var se = seman.AddStatusEffect(hash); se.m_ttl = x;` NullReferences on every non-owner. Always null-check.
- RemoveStatusEffect (both overloads, AV:24422/24427) and RemoveAllStatusEffects (AV:24451) are LOCAL ONLY — there is no RPC_RemoveStatusEffect. You cannot remotely strip an effect from another player through SEMan; you must run the removal on the owning client.
- JOINING CLIENT: ObjectDB.CopyOtherDB (AV:90520) does `m_StatusEffects = other.m_StatusEffects;` — a wholesale reference replace on scene/world load. A custom StatusEffect injected only in a postfix of ObjectDB.Awake is silently dropped when CopyOtherDB runs. Patch BOTH ObjectDB.Awake and ObjectDB.CopyOtherDB (postfix), and guard against double-adding by checking ObjectDB.instance.GetStatusEffect(hash) == null first.
- ObjectDB.GetStatusEffect (AV:90543) is an O(n) linear scan calling NameHash() on every entry — it is NOT hash-indexed (UpdateRegisters at AV:90528 only indexes items). Do not call it per-frame.
- StatusEffect derives from ScriptableObject (AV:26393). There is NO Awake() and NO Start() on it — do not write `public override void Awake()` (compile error) and do not rely on Unity lifecycle callbacks firing. All init goes in `Setup(Character)`. OnDestroy() (AV:26493) IS declared `public virtual` on StatusEffect and is called manually from SEMan.OnDestroy (AV:24278) — it is not the Unity message.
- ModifySpeed takes FOUR params: `(float baseSpeed, ref float speed, Character character, Vector3 dir)` (AV:26654) and ModifyRunStaminaDrain takes THREE: `(float baseDrain, ref float drain, Vector3 dir)` (AV:26682). Getting these wrong produces a silent NON-override (method hides nothing, compiles with a `new` warning at most if names match partially) — your modifier just never runs.
- `ModifyAttackSpeed` does NOT exist — NOT FOUND in the assembly. Do not write an override for it.
- `m_percentigeDamageModifiers` (AV:25688) is the real, misspelled, shipping field name; confirmed in Values_Dump.json. Its type is HitData.DamageTypes (struct of per-type floats), not a List. Also: SE_Stats.ModifyAttack (AV:25941) applies it UNCONDITIONALLY regardless of m_modifyAttackSkill — only m_damageModifier is skill-gated.
- There is no `m_flags` field on StatusEffect — NOT FOUND. The bitmask you probably want is `public StatusEffect.StatusAttribute m_attributes` (AV:26419), and it is replicated to other clients via ZDOVars.s_seAttrib in SEMan.Update (AV:24347) — write to m_attributes only on the owner or the ZDO write is meaningless.
- m_armorMultiplier (AV:25664) defaults to 0f, and ModifyArmorMods (AV:26006) does `if (m_armorMultiplier != 0f) armor *= 1f + m_armorMultiplier;`. Setting it to 1f DOUBLES armor, it does not mean 'no change'. Contrast m_damageModifier / m_healthRegenMultiplier / m_staminaRegenMultiplier / m_eitrRegenMultiplier which all default to 1f meaning no-op.
- Regen multipliers are asymmetric (AV:25846, 25858, 25870): values >1 are ADDED (`regen += mult - 1f`), values <=1 are MULTIPLIED. Stacking two +50% effects gives +100%, not +125%. Do not assume multiplicative stacking.
- m_maxMaxFallSpeed is applied in ModifyWalkVelocity (AV:26044), NOT in ModifyFallDamage. m_fallDamageModifier is the ModifyFallDamage one (AV:26050).
- SE_Rested.ResetTime() (AV:25378) deliberately does NOT call base.ResetTime(), so `m_time = 0f` only happens inside UpdateTTL's `if (num > num2)` branch, and SE_Stats.StartupEffects() (up-front heal/stamina/eitr/adrenaline) is skipped. If you subclass SE_Stats and override ResetTime without calling base, you silently lose the up-front grants.
- m_time and m_startEffectInstances are `protected` (AV:26453/26455) and m_msgTimer/m_nameHash are `private` (AV:26449/26457). Reading/writing them from a Harmony patch (rather than from inside your subclass) relies on the publicized assembly — fine at compile time against your publicized ref, but note the shipped game DLL is not publicized, so use AccessTools/reflection if you ever target the stock assembly.
- Humanoid.UpdateEquipmentStatusEffects, HaveSetEffect and GetSetCount are all PRIVATE instance methods (AV:14230/14311/14328). Harmony patching them requires AccessTools.Method on the private target; `[HarmonyPatch(typeof(Humanoid), "UpdateEquipmentStatusEffects")]` (string name) works, the typeof-nameof form against a non-publicized reference does not.
- Humanoid.UpdateEquipmentStatusEffects applies set effects via `m_seman.AddStatusEffect(item)` — the LOCAL instance overload (AV:14304) — and is only reached when `m_nview.GetZDO() != null` (AV:14181). Set/equip effects are therefore recomputed independently on each machine that simulates the character; do not try to sync them by hand or you double-apply.
- GetSetCount (AV:14328) counts m_rightItem (weapon), m_leftItem (offhand) AND m_trinketItem, but the set-effect application block (AV:14265-14292) never queries m_trinketItem. A trinket sharing m_setName inflates the count toward m_setSize but can never itself carry the set bonus — an easy source of 'set completes with 4 pieces instead of 5' bugs if you add trinkets with a set name.
- SEMan.GetStatusEffects() (AV:24501) returns the LIVE m_statusEffects List reference, not a copy. Adding/removing during a foreach over it (e.g. in a Harmony patch) throws InvalidOperationException. Note SEMan.Update itself guards this with the m_removeStatusEffects deferral list.
- The `GetStableHashCode` string extension is NOT declared in assembly_valheim — it lives in assembly_utils.dll. Reference that assembly; do not reimplement it, or your hashes will not match the game's and RPCs will resolve nothing.

## NOT FOUND
- StatusEffect.m_flags — no such field; the bitmask is `public StatusEffect.StatusAttribute m_attributes` (AV:26419)
- StatusEffect.ModifyAttackSpeed — no member of that name anywhere in assembly_valheim
- StatusEffect.Awake() — does not exist (base is ScriptableObject; init happens in Setup(Character))
- StatusEffect.Start() — does not exist
- virtual/overridable StatusEffect.Clone() — Clone() exists at AV:26459 but is NON-virtual (`public StatusEffect Clone()` = MemberwiseClone)
- SE_Stats.m_homeItemsStaminaModifier — does not exist on SE_Stats. The SE_Stats field is `m_homeItemStaminaUseModifier` (AV:25631). `m_homeItemsStaminaModifier` exists only on ItemDrop.ItemData.SharedData (AV:58063)
- SE_Stats.m_percentageDamageModifiers (correctly-spelled variant) — does not exist; only the misspelled `m_percentigeDamageModifiers` (AV:25688)
- SEMan.AddStatusEffect(string name, ...) — no string overload; only (int nameHash,...) AV:24352 and (StatusEffect,...) AV:24394
- SEMan.RemoveStatusEffect(string) — no string overload; only (StatusEffect, bool) AV:24422 and (int, bool) AV:24427
- SEMan.HaveStatusEffect(string) — no string overload; only HaveStatusEffect(int nameHash) AV:24496 (and HaveStatusEffectCategory(string cat) AV:24466, which matches m_category, not the name)
- SEMan.RPC_RemoveStatusEffect — does not exist; only RPC_AddStatusEffect is registered (AV:24271). Removal is never networked.
- ObjectDB.GetStatusEffect(string) — no string overload; only GetStatusEffect(int nameHash) AV:90543
- ObjectDB.AddStatusEffect / RegisterStatusEffect — no such helper; you must add directly to the public List<StatusEffect> m_StatusEffects (AV:90502)
- StringExtensionMethods.GetStableHashCode — not declared in assembly_valheim.decompiled.cs (334 call sites, zero declarations); it lives in assembly_utils.dll
- SE_Stats override of CanAdd / IsDone / Stop / SetLevel / OnDamaged / SetAttacker — none exist; SE_Stats inherits the StatusEffect base behavior for all six
- StatusEffect.m_startEffectInstances as public — it is `protected GameObject[]` (AV:26455), not public