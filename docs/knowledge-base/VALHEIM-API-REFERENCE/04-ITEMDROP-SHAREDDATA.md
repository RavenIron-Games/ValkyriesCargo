All citations from `C:\WubarrkCODING\libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs`.

## 0. Reference-type verdict (decides deep-copy strategy)

| Type | Kind | Line |
|---|---|---|
| `ItemDrop` | `public class ItemDrop : MonoBehaviour, Hoverable, Interactable` | 57918 |
| `ItemDrop.ItemData` | **`class`** (reference type), `[Serializable]` | 57920-57921 |
| `ItemDrop.ItemData.SharedData` | **`class`** (reference type), `[Serializable]` | 58004-58005 |
| `ItemDrop.ItemData.HelmetHairSettings` | `class`, `[Serializable]` | 57996-57997 |
| `HitData.DamageTypes` | **`struct`** (value type), `[Serializable]` | 58379-58380 (in HitData, line 112379-112380) |

Consequence: `SharedData` is shared **by reference** between the ObjectDB prefab and every `ItemData` cloned from it. Mutating `item.m_shared.m_weight` mutates the prefab and every other stack of that item. A real per-item override requires allocating a **new** `SharedData` and copying all ~120 fields yourself — there is **no** `SharedData.Clone()` (NOT FOUND). `HitData.DamageTypes` being a struct means `var d = shared.m_damages; d.m_fire = 5;` does **not** write back.

## 1. `ItemDrop.ItemData.SharedData` — full public field list, in declaration order, grouped by `[Header]`

Every field below is `public` and **instance** (no statics in SharedData).

### (no header — top of class)
| Line | Declaration |
|---|---|
| 58007 | `public string m_name = "";` |
| 58009 | `public string m_subtitle = "";` |
| 58011 | `public string m_dlc = "";` |
| 58013 | `public ItemType m_itemType = ItemType.Misc;` |
| 58015 | `public Sprite[] m_icons = Array.Empty<Sprite>();` |
| 58017 | `public ItemType m_attachOverride;` |
| 58019-58020 | `[TextArea] public string m_description = "";` |
| 58022 | `public int m_maxStackSize = 1;` |
| 58024 | `public bool m_autoStack = true;` |
| 58026 | `public int m_maxQuality = 1;` |
| 58028 | `public float m_scaleByQuality;` |
| 58030 | `public float m_weight = 1f;` |
| 58032 | `public float m_scaleWeightByQuality;` |
| 58034 | `public int m_value;` |
| 58036 | `public bool m_teleportable = true;` |
| 58038 | `public bool m_questItem;` |
| 58040 | `public float m_equipDuration = 1f;` |
| 58042 | `public int m_variants;` |
| 58044 | `public Vector2Int m_trophyPos = Vector2Int.zero;` |
| 58046 | `public PieceTable m_buildPieces;` |
| 58048 | `public bool m_centerCamera;` |
| 58050 | `public string m_setName = "";` |
| 58052 | `public int m_setSize;` |
| 58054 | `public StatusEffect m_setStatusEffect;` |
| 58056 | `public StatusEffect m_equipStatusEffect;` |

### `[Header("Stat modifiers")]` — 58058
| Line | Declaration |
|---|---|
| 58059 | `public float m_eitrRegenModifier;` |
| 58061 | `public float m_movementModifier;` |
| 58063 | `public float m_homeItemsStaminaModifier;` |
| 58065 | `public float m_heatResistanceModifier;` |
| 58067 | `public float m_jumpStaminaModifier;` |
| 58069 | `public float m_attackStaminaModifier;` |
| 58071 | `public float m_blockStaminaModifier;` |
| 58073 | `public float m_dodgeStaminaModifier;` |
| 58075 | `public float m_swimStaminaModifier;` |
| 58077 | `public float m_sneakStaminaModifier;` |
| 58079 | `public float m_runStaminaModifier;` |

### `[Header("Food settings")]` — 58081
| Line | Declaration |
|---|---|
| 58082 | `public float m_food;` |
| 58084 | `public float m_foodStamina;` |
| 58086 | `public float m_foodEitr;` |
| 58088 | `public float m_foodBurnTime;` |
| 58090 | `public float m_foodRegen;` |
| 58092 | `public float m_foodEatAnimTime = 1f;` |
| 58094 | `public bool m_isDrink;` |

### `[Header("Armor settings")]` — 58096
| Line | Declaration |
|---|---|
| 58097 | `public Material m_armorMaterial;` |
| 58099 | `public HelmetHairType m_helmetHideHair = HelmetHairType.Hidden;` |
| 58101 | `public HelmetHairType m_helmetHideBeard;` |
| 58103 | `public List<HelmetHairSettings> m_helmetHairSettings = new List<HelmetHairSettings>();` |
| 58105 | `public List<HelmetHairSettings> m_helmetBeardSettings = new List<HelmetHairSettings>();` |
| 58107 | `public float m_armor = 10f;` |
| 58109 | `public float m_armorPerLevel = 1f;` |
| 58111 | `public List<HitData.DamageModPair> m_damageModifiers = new List<HitData.DamageModPair>();` |

### `[Header("Shield settings")]` — 58113
| Line | Declaration |
|---|---|
| 58114 | `public float m_blockPower = 10f;` |
| 58116 | `public float m_blockPowerPerLevel;` |
| 58118 | `public float m_deflectionForce;` |
| 58120 | `public float m_deflectionForcePerLevel;` |
| 58122 | `public float m_timedBlockBonus = 1.5f;` |
| 58124 | `public float m_perfectBlockStaminaRegen;` |
| 58126 | `public StatusEffect m_perfectBlockStatusEffect;` |
| 58128-58129 | `[Space(8f)] public bool m_buildBlockCharges;` |
| 58131 | `public int m_maxBlockCharges = 5;` |
| 58133 | `public float m_blockChargeDecayTime = 1f;` |
| 58135 | `public float m_blockChargeBlockingDecayMult = 0.25f;` |
| 58137 | `public EffectList m_blockChargeEffects = new EffectList();` |

### `[Header("Adrenaline")]` — 58139
| Line | Declaration |
|---|---|
| 58140 | `public float m_maxAdrenaline;` |
| 58142 | `public StatusEffect m_fullAdrenalineSE;` |
| 58144 | `public float m_blockAdrenaline = 2f;` |
| 58146 | `public float m_perfectBlockAdrenaline = 5f;` |

### `[Header("Weapon")]` — 58148
| Line | Declaration |
|---|---|
| 58149 | `public AnimationState m_animationState = AnimationState.OneHanded;` |
| 58151 | `public Skills.SkillType m_skillType = Skills.SkillType.Swords;` |
| 58153 | `public int m_toolTier;` |
| 58155 | `public HitData.DamageTypes m_damages;` |
| 58157 | `public HitData.DamageTypes m_damagesPerLevel;` |
| 58159 | `public float m_attackForce = 30f;` |
| 58161 | `public float m_backstabBonus = 4f;` |
| 58163 | `public bool m_dodgeable;` |
| 58165 | `public bool m_blockable;` |
| 58167 | `public bool m_tamedOnly;` |
| 58169 | `public bool m_alwaysRotate;` |
| 58171 | `public StatusEffect m_attackStatusEffect;` |
| 58173 | `public float m_attackStatusEffectChance = 1f;` |
| 58175 | `public GameObject m_spawnOnHit;` |
| 58177 | `public GameObject m_spawnOnHitTerrain;` |
| 58179 | `public bool m_projectileToolTip = true;` |

### `[Header("Ammo")]` — 58181
| Line | Declaration |
|---|---|
| 58182 | `public string m_ammoType = "";` |

### `[Header("Attacks")]` — 58184
| Line | Declaration |
|---|---|
| 58185 | `public Attack m_attack;` |
| 58187 | `public Attack m_secondaryAttack;` |

### `[Header("ItemStand")]` — 58189
| Line | Declaration |
|---|---|
| 58191 | `public List<ItemStand.OrientationSettings> m_itemStandOffsets = new List<ItemStand.OrientationSettings>();` (preceded by a long `[Tooltip]`, 58190) |

### `[Header("Durability")]` — 58193
| Line | Declaration |
|---|---|
| 58194 | `public bool m_useDurability;` |
| 58196 | `public bool m_destroyBroken = true;` |
| 58198 | `public bool m_canBeReparied = true;` *(sic — misspelled in vanilla)* |
| 58200 | `public float m_maxDurability = 100f;` |
| 58202 | `public float m_durabilityPerLevel = 50f;` |
| 58204 | `public float m_useDurabilityDrain = 1f;` |
| 58206 | `public float m_durabilityDrain;` |
| 58208 | `public Skills.SkillType m_placementDurabilitySkill;` |
| 58210 | `public float m_placementDurabilityMax = 0.5f;` |

### `[Header("AI")]` — 58212
| Line | Declaration |
|---|---|
| 58213 | `public float m_aiAttackRange = 2f;` |
| 58215 | `public float m_aiAttackRangeMin;` |
| 58217 | `public float m_aiAttackInterval = 2f;` |
| 58219 | `public float m_aiAttackMaxAngle = 5f;` |
| 58221 | `public bool m_aiInvertAngleCheck;` |
| 58223 | `public bool m_aiWhenFlying = true;` |
| 58225 | `public float m_aiWhenFlyingAltitudeMin;` |
| 58227 | `public float m_aiWhenFlyingAltitudeMax = 999999f;` |
| 58229 | `public bool m_aiWhenWalking = true;` |
| 58231 | `public bool m_aiWhenSwiming = true;` *(sic)* |
| 58233 | `public bool m_aiPrioritized;` |
| 58235 | `public bool m_aiInDungeonOnly;` |
| 58237 | `public bool m_aiInMistOnly;` |
| 58239-58240 | `[Range(0f, 1f)] public float m_aiMaxHealthPercentage = 1f;` |
| 58242-58243 | `[Range(0f, 1f)] public float m_aiMinHealthPercentage;` |
| 58245 | `public AiTarget m_aiTargetType;` |

### `[Header("Effects")]` — 58247
| Line | Declaration |
|---|---|
| 58248 | `public EffectList m_hitEffect = new EffectList();` |
| 58250 | `public EffectList m_hitTerrainEffect = new EffectList();` |
| 58252 | `public EffectList m_blockEffect = new EffectList();` |
| 58254 | `public EffectList m_startEffect = new EffectList();` |
| 58256 | `public EffectList m_holdStartEffect = new EffectList();` |
| 58258 | `public EffectList m_equipEffect = new EffectList();` |
| 58260 | `public EffectList m_unequipEffect = new EffectList();` |
| 58262 | `public EffectList m_triggerEffect = new EffectList();` |
| 58264 | `public EffectList m_trailStartEffect = new EffectList();` |
| 58266 | `public EffectList m_buildEffect = new EffectList();` |
| 58268 | `public EffectList m_destroyEffect = new EffectList();` |

### `[Header("Consumable")]` — 58270
| Line | Declaration |
|---|---|
| 58271 | `public StatusEffect m_consumeStatusEffect;` |
| 58273 | `public ItemDrop m_appendToolTip;` |

Only method: `public override string ToString()` — 58275.

## 2. `enum ItemDrop.ItemData.ItemType` — 57923-57949 (NOT `[Flags]`; value **8 is missing**)

```csharp
None = 0, Material = 1, Consumable = 2, OneHandedWeapon = 3, Bow = 4, Shield = 5,
Helmet = 6, Chest = 7, /* 8 absent */ Ammo = 9, Customization = 10, Legs = 11,
Hands = 12, Trophy = 13, TwoHandedWeapon = 14, Torch = 15, Misc = 16, Shoulder = 17,
Utility = 18, Tool = 19, Attach_Atgeir = 20, Fish = 21, TwoHandedWeaponLeft = 22,
AmmoNonEquipable = 23, Trinket = 24
```

## 3. `enum ItemDrop.ItemData.AnimationState` — 57951-57971 (implicit values 0..17)

| Value | Member | | Value | Member |
|---|---|---|---|---|
| 0 | `Unarmed` | | 9 | `FishingRod` |
| 1 | `OneHanded` | | 10 | `Crossbow` |
| 2 | `TwoHandedClub` | | 11 | `Knives` |
| 3 | `Bow` | | 12 | `Staves` |
| 4 | `Shield` | | 13 | `Greatsword` |
| 5 | `Torch` | | 14 | `MagicItem` |
| 6 | `LeftTorch` | | 15 | `DualAxes` |
| 7 | `Atgeir` | | 16 | `Feaster` |
| 8 | `TwoHandedAxe` | | 17 | `Scythe` |

Other nested enums: `AiTarget { Enemy, FriendHurt, Friend }` (57973), `HelmetHairType { Default, Hidden, HiddenHat, HiddenHood, HiddenNeck, HiddenScarf }` (57980), `AccessoryType { Hair, Beard }` (57990).

## 4. `ItemDrop.ItemData` instance fields — 58281-58319

```csharp
private static StringBuilder m_stringBuilder = new StringBuilder(256);   // 58281 (private static)
public int    m_stack      = 1;                                          // 58283
public float  m_durability = 100f;                                       // 58285
public int    m_quality    = 1;                                          // 58287
public int    m_variant;                                                 // 58289
public int    m_worldLevel = Game.m_worldLevel;                          // 58291
public bool   m_pickedUp;                                                // 58293
public SharedData m_shared;                                              // 58295
[NonSerialized] public long   m_crafterID;                               // 58297-58298
[NonSerialized] public string m_crafterName = "";                        // 58300-58301
[NonSerialized] public Dictionary<string,string> m_customData = new Dictionary<string,string>(); // 58303-58304
[NonSerialized] public Vector2i m_gridPos = Vector2i.zero;               // 58306-58307  (Vector2i, NOT Vector2Int)
[NonSerialized] public bool   m_equipped;                                // 58309-58310
[NonSerialized] public GameObject m_dropPrefab;                          // 58312-58313
[NonSerialized] public float  m_lastAttackTime;                          // 58315-58316
[NonSerialized] public GameObject m_lastProjectile;                      // 58318-58319
```

### `Clone()` — 58321-58326 (SHALLOW)
```csharp
public ItemData Clone()
{
    ItemData obj = MemberwiseClone() as ItemData;
    obj.m_customData = new Dictionary<string, string>(m_customData);
    return obj;
}
```
`m_shared` and `m_dropPrefab` are copied **by reference**; only `m_customData` is re-allocated.

### Other `ItemData` public methods (signatures verified)
```csharp
public bool  IsEquipable()                                      // 58328
public bool  IsWeapon()                                         // 58337
public bool  IsTwoHanded()                                      // 58346
public bool  HavePrimaryAttack()                                // 58355
public bool  HaveSecondaryAttack()                              // 58360
public float GetArmor()                                         // 58365
public float GetArmor(int quality, float worldLevel)            // 58370
public bool  TryGetArmorDifference(out float difference)        // 58375
public int   GetValue()                                         // 58385
public float GetWeight(int stackOverride = -1)                  // 58390
public float GetNonStackedWeight()                              // 58401
public HitData.DamageTypes GetDamage()                          // 58411
public float GetDurabilityPercentage()                          // 58416
public float GetMaxDurability()                                 // 58426
public float GetMaxDurability(int quality)                      // 58431
public HitData.DamageTypes GetDamage(int quality, float worldLevel) // 58436
public float GetBaseBlockPower()                                // 58450
public float GetBaseBlockPower(int quality)                     // 58455
public float GetBlockPower(float skillFactor)                   // 58460
public float GetBlockPower(int quality, float skillFactor)      // 58465
public float GetBlockPowerTooltip(int quality)                  // 58471
public float GetDrawStaminaDrain()                              // 58481
public float GetDrawEitrDrain()                                 // 58492
public float GetWeaponLoadingTime()                             // 58503
public float GetDeflectionForce()                               // 58513
public float GetDeflectionForce(int quality)                    // 58518
public string GetTooltip(int stackOverride = -1)                // 58534
public Sprite GetIcon()                                         // 58539
public override string ToString()                               // 58923
```
Scaling formulas (for patching): `GetArmor` = `m_armor + max(0,q-1)*m_armorPerLevel + worldLevel*Game.instance.m_worldLevelGearBaseAC` (58372); `GetMaxDurability` = `m_maxDurability + max(0,q-1)*m_durabilityPerLevel` (58433); `GetDamage(q,wl)` copies `m_damages`, then `damages.Add(m_damagesPerLevel, q-1)` and `IncreaseEqually(wl*Game.instance.m_worldLevelGearBaseDamage, true)` (58436-58448).

## 5. `ItemDrop` component fields — 58929-58982

```csharp
private static List<ItemDrop> s_instances = new List<ItemDrop>();   // 58929 (private static; publicizer needed)
private int  m_myIndex = -1;                                        // 58931 (private)
public bool  m_autoPickup  = true;                                  // 58933
public bool  m_autoDestroy = true;                                  // 58935
public ItemData m_itemData = new ItemData();                        // 58937
[HideInInspector] public Action<ItemDrop> m_onDrop;                 // 58939-58940
public GameObject m_pieceEnableObj;                                 // 58942
public GameObject m_pieceDisabledObj;                               // 58944
private int m_nameHash;            // 58946      private Floating m_floating;   // 58948
private Rigidbody m_body;          // 58950      private ZNetView m_nview;      // 58952
private Character m_pickupRequester;// 58954     private float m_lastOwnerRequest;// 58956
private int m_ownerRetryCounter;   // 58958      private float m_ownerRetryTimeout;// 58960
private float m_spawnTime;         // 58962      private Piece m_piece;         // 58964
private WearNTear m_wnt;           // 58966      private uint m_loadedRevision = uint.MaxValue; // 58968
private const double c_AutoDestroyTimeout = 3600.0;   // 58970
private const double c_AutoPickupDelay    = 0.5;      // 58972
private const float  c_AutoDespawnBaseMinAltitude = -2f; // 58974
private const int    c_AutoStackThreshold = 200;      // 58976
private const float  c_AutoStackRange     = 4f;       // 58978
private bool m_haveAutoStacked;    // 58980      private static int s_itemMask = 0; // 58982
```
**There is no `ItemDrop.m_dropPrefab`** — `m_dropPrefab` lives on `ItemData` (58313) and is assigned in `ItemDrop.Awake` from ObjectDB (58994).

Relevant statics:
```csharp
public  static void SaveToZDO(ItemData itemData, ZDO zdo)              // 59485
private static void LoadFromZDO(ItemData itemData, ZDO zdo)            // 59504 (private)
public  static void SaveToZDO(int index, ItemData itemData, ZDO zdo)   // 59522
public  static void LoadFromZDO(int index, ItemData itemData, ZDO zdo) // 59541
public  static ItemDrop DropItem(ItemData item, int amount, Vector3 position, Quaternion rotation) // 59558
```

## 6. `HitData.DamageTypes` (struct) — 112379-112443+

```csharp
[Serializable]
public struct DamageTypes            // 112380
{
    public float m_damage;      // 112382
    public float m_blunt;       // 112384
    public float m_slash;       // 112386
    public float m_pierce;      // 112388
    public float m_chop;        // 112390
    public float m_pickaxe;     // 112392
    public float m_fire;        // 112394
    public float m_frost;       // 112396
    public float m_lightning;   // 112398
    public float m_poison;      // 112400
    public float m_spirit;      // 112402
    private static StringBuilder m_sb = new StringBuilder();  // 112404
}
```

| Signature | Line | Behaviour |
|---|---|---|
| `public bool HaveDamage()` | 112406 | true if any of the 11 > 0 |
| `public float GetTotalPhysicalDamage()` | 112415 | blunt+slash+pierce |
| `public float GetTotalStaggerDamage()` | 112420 | blunt+slash+pierce+**lightning** |
| `public float GetTotalBlockableDamage()` | 112425 | all except m_damage, m_chop, m_pickaxe |
| `public float GetTotalElementalDamage()` | 112430 | fire+frost+lightning |
| `public float GetTotalDamage()` | 112435 | sum of **all 11** including m_damage/m_chop/m_pickaxe |
| `public DamageTypes Clone()` | 112440 | `(DamageTypes)MemberwiseClone()` — boxes; plain `=` is equivalent |
| `public void Add(DamageTypes other, int multiplier = 1)` | 112445 | `this.X += other.X * multiplier` for all 11 |
| `public void Modify(float multiplier)` | 112460 | `X *= multiplier` for all 11 |
| `public void Modify(DamageTypes multipliers)` | 112475 | `X *= (1f + multipliers.X)` for all 11 — **percentage semantics, not absolute** |
| `public void IncreaseEqually(float totalDamageIncrease, bool seperateUtilityDamage = false)` | 112490 | proportional distribution; no-op if total <= 0 |
| `public static float ApplyArmor(float dmg, float ac)` | 112522 | |
| `public void ApplyArmor(float ac)` | 112532 | |
| `public DamageType GetMajorityDamageType()` | 112552 | |
| `public DamageType GetMajorityDamageType(out float damage)` | 112558 | |

Note the two `Modify` overloads have different meanings — `Modify(float)` is a flat multiplier, `Modify(DamageTypes)` treats each field as a fractional bonus.

## 7. `enum HitData.DamageType` — 112172-112188, **`[Flags]`**

```csharp
[Flags] public enum DamageType {
    Blunt = 1, Slash = 2, Pierce = 4, Chop = 8, Pickaxe = 0x10 (16),
    Fire = 0x20 (32), Frost = 0x40 (64), Lightning = 0x80 (128),
    Poison = 0x100 (256), Spirit = 0x200 (512), Damage = 0x400 (1024),
    Physical = 0x1F (31), Elemental = 0xE0 (224)
}
```
`Physical` and `Elemental` are composite masks, not distinct types.

Adjacent (often needed): `public enum DamageModifier { Normal, Resistant, Weak, Immune, Ignore, VeryResistant, VeryWeak, SlightlyResistant, SlightlyWeak }` — 112190-112201; `public enum HitType : byte { Undefined, EnemyHit, PlayerHit, Fall, Drowning, Burning, Freezing, ... }` — 112203+.

## 8. Networking surface (what actually crosses the wire)

`Inventory.Save(ZPackage pkg)` — 57606-57638, version `106`, per item:
`m_dropPrefab.name (string; "" if null!)`, `m_stack`, `m_durability`, `m_gridPos`, `m_equipped`, `m_quality`, `m_variant`, `m_crafterID`, `m_crafterName`, `m_customData.Count` + KV string pairs, `m_worldLevel`, `m_pickedUp`.

`ItemDrop.SaveToZDO` — 59485-59502: `s_durability, s_stack, s_quality, s_variant, s_crafterID, s_crafterName, s_dataCount + data_N/data__N, s_worldLevel, s_pickedUp`.

**Nothing from `SharedData` is ever serialized or networked.** Items are identified on the wire solely by `m_dropPrefab.name`.

## GOTCHAS
- SharedData is a CLASS and is shared by reference. `ItemData.Clone()` (line 58321) is `MemberwiseClone()` + a new `m_customData` dictionary only — `m_shared` and `m_dropPrefab` are copied by reference. Writing `item.m_shared.m_damages.m_fire = 10f` mutates the ObjectDB prefab and every existing stack of that item type in the world. There is no `SharedData.Clone()` in the decompile (NOT FOUND); a per-item override requires `new SharedData()` + manual copy of ~120 fields, and even then `Inventory` code re-points `m_shared` back at the prefab (Player line 13840: `item.m_shared = item.m_dropPrefab.GetComponent<ItemDrop>().m_itemData.m_shared;`, and ItemDrop.Awake line 58997 in editor).
- HitData.DamageTypes is a STRUCT. `HitData.DamageTypes d = shared.m_damages; d.m_fire = 5f;` silently discards the write. Mutate in place (`shared.m_damages.m_fire = 5f;` works because `m_damages` is a field of a class) or assign the whole struct back.
- `DamageTypes.Modify(DamageTypes)` (112475) multiplies by `1f + other.X` — percentage semantics. `Modify(float)` (112460) is a flat multiplier. Passing an absolute damage table to the first overload silently produces garbage.
- `ItemType` has NO member with value 8 (Chest = 7, Ammo = 9). Never assume contiguity when casting ints or iterating.
- `HitData.DamageType` is `[Flags]` and includes the composite masks `Physical = 0x1F` and `Elemental = 0xE0`. `Enum.GetValues` yields them as if they were real damage types — filter them out or you will double-count.
- `ItemData.m_gridPos` is `Vector2i` (Valheim's own struct), NOT Unity's `Vector2Int`. `SharedData.m_trophyPos` IS `Vector2Int`. They are different types.
- MULTIPLAYER: nothing from SharedData is serialized. `Inventory.Save` (57606) and `ItemDrop.SaveToZDO` (59485) write only prefab name, stack, durability, quality, variant, crafterID/Name, customData, worldLevel, pickedUp. If your mod changes m_damages/m_armor/m_maxStackSize only on some clients, every client computes different numbers with no error — you MUST ServerSync the config and apply identical SharedData edits on server and all clients.
- JOINING CLIENT: `Inventory.Save` writes an EMPTY STRING for any item whose `m_dropPrefab` is null (57612-57616) — the item is silently destroyed on the next load. Any ItemData a mod constructs by hand must have `m_dropPrefab` set.
- `ItemDrop.Awake` (58992-58994) does `ObjectDB.instance.GetItemPrefab(GetPrefabName(gameObject.name))` and assigns to `m_itemData.m_dropPrefab`; line 58997 dereferences `itemPrefab` unguarded in editor. A custom item prefab must be in ObjectDB (and ZNetScene for network spawn) BEFORE any instance exists, or you get null/NRE paths.
- `m_worldLevel` is declared `public int` (58291) but LoadFromZDO casts through `(byte)` (59518, 59554) and `Inventory.AddItem` assigns `(byte)Game.m_worldLevel` (56979). Values > 255 will wrap.
- `m_canBeReparied` (58198) and `m_aiWhenSwiming` (58231) are misspelled in vanilla — copying the correct spelling will not compile.
- `ItemDrop.s_instances` (58929) and `m_myIndex` (58931) are private statics/fields — reachable only because the assembly is publicized. Same for `private static void LoadFromZDO(ItemData, ZDO)` (59504); the public overload is the indexed one at 59541.

## NOT FOUND
- SharedData.m_setStatusEffectHash — NOT FOUND anywhere in the decompile. Only `public StatusEffect m_setStatusEffect;` (58054) exists; there is no cached hash field.
- SharedData.m_holdDurationMin — NOT FOUND. No `m_holdDurationMin` / `HoldDurationMin` token in the assembly. The only `m_hold*` members are `SharedData.m_holdStartEffect` (58256) and unrelated `m_holdRepeatInterval` fields on UI/other classes (108177, 124674, 127533).
- SharedData.m_holdStaminaDrain — NOT FOUND. Draw/hold stamina lives on `Attack`: `m_drawStaminaDrain` (line 965) and `m_reloadStaminaDrain` (line 956), surfaced via `ItemData.GetDrawStaminaDrain()` (58481).
- ItemDrop.m_dropPrefab — NOT FOUND as a field of the ItemDrop MonoBehaviour. `m_dropPrefab` exists only on ItemDrop.ItemData (58313, [NonSerialized] public GameObject).
- SharedData.Clone() / any copy-constructor on SharedData — NOT FOUND. Only `ToString()` (58275).
- ItemDrop.ItemData.SharedData has no static members at all — no shared caches or hash tables to invalidate.