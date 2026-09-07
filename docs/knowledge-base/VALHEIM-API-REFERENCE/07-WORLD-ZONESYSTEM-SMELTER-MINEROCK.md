All citations: `C:\WubarrkCODING\libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs` (referred to below as `AV.cs`).
Values: `C:\WubarrkCODING\libs-Tools\WubarrksEye_Dumps\2026-07-31_21-06-43\Values_Dump.json` (referred to as `VD.json`).

---

## 1. ZoneSystem

`public class ZoneSystem : MonoBehaviour` — AV.cs:97705

```csharp
// AV.cs:98051
private static ZoneSystem m_instance;
// AV.cs:98179
public static ZoneSystem instance => m_instance;

// AV.cs:98110
public List<ZoneVegetation> m_vegetation = new List<ZoneVegetation>();   // declared type: List<ZoneSystem.ZoneVegetation>
// AV.cs:98112
public List<ZoneLocation> m_locations = new List<ZoneLocation>();
// AV.cs:98105 / 98107
public List<string> m_locationScenes = new List<string>();
public List<GameObject> m_locationLists = new List<GameObject>();

// AV.cs:98032
public enum SpawnMode { Full, Client, Ghost }
```

### `ZoneSystem.Awake()` — FULL BODY, AV.cs:98223-98235
```csharp
private void Awake()
{
    m_instance = this;
    m_terrainRayMask = LayerMask.GetMask("terrain");
    m_blockRayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
    m_solidRayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
    m_staticSolidRayMask = LayerMask.GetMask("static_solid", "terrain");
    foreach (GameObject locationList in m_locationLists)
    {
        UnityEngine.Object.Instantiate(locationList);
    }
    ZLog.Log("Zonesystem Awake " + Time.frameCount);
}
```
**`Awake` is `private`** (reachable only because the assembly is publicized; Harmony patches by `AccessTools` work regardless).

**CRITICAL: `m_vegetation` is EMPTY after `Awake`.** It is filled in `Start()` → `SetupLocations()` (AV.cs:98414-98423) via `m_vegetation.AddRange(item.m_vegetation)` over every `LocationList` MonoBehaviour instantiated from `m_locationLists`. So a `Postfix` on `ZoneSystem.Awake` adds entries that will be *followed* by the vanilla AddRange (fine, order-independent), but a `Prefix` on `Awake` sees an empty list. The safe injection point is a **Postfix on `ZoneSystem.SetupLocations` (private, AV.cs:98414) or on `ZoneSystem.Start` (private, AV.cs:98238)**, both of which run before `ValidateVegetation()` / first zone spawn.

```csharp
// AV.cs:98238-98255 (excerpt, order matters)
private void Start()
{
    ZLog.Log("Zonesystem Start " + Time.frameCount);
    UpdateWorldRates();
    SetupLocations();      // <-- m_vegetation gets AddRange'd here
    ValidateVegetation();  // <-- logs error if prefab has no ZNetView
    ...
}

// AV.cs:98503-98512
private void ValidateVegetation()
{
    foreach (ZoneVegetation item in m_vegetation)
        if (item.m_enable && (bool)item.m_prefab && item.m_prefab.GetComponent<ZNetView>() == null)
            ZLog.LogError("Vegetation " + item.m_prefab.name + " [ " + item.m_name + "] is missing ZNetView");
}
```

Also relevant: `public class LocationList : MonoBehaviour` AV.cs:89510 has `public List<ZoneSystem.ZoneVegetation> m_vegetation` at **AV.cs:89518** — patching that instead also works.

### `class ZoneVegetation` — AV.cs:97727-97825

**`[Serializable] public class`** (AV.cs:97727 `[Serializable]`, AV.cs:97728 `public class ZoneVegetation`). It is a **CLASS, not a struct** — reference semantics; `list.Add(veg)` shares the instance.

**`Clone()` EXISTS**:
```csharp
// AV.cs:97821-97824
public ZoneVegetation Clone()
{
    return MemberwiseClone() as ZoneVegetation;
}
```
`public`, instance, returns `ZoneVegetation`. Shallow copy — `m_prefab` reference is shared (fine, you want to swap it anyway).

**EVERY field (all `public`, all instance, exact declared type + default):**

| Line | Declaration | Default |
|---|---|---|
| 97730 | `public string m_name` | `"veg"` |
| 97732 | `public GameObject m_prefab` | null |
| 97734 | `public bool m_enable` | `true` |
| 97736 | `public float m_min` | 0 |
| 97738 | `public float m_max` | `10f` |
| 97740 | `public bool m_forcePlacement` | false |
| 97742 | `public float m_scaleMin` | `1f` |
| 97744 | `public float m_scaleMax` | `1f` |
| 97746 | `public float m_randTilt` | 0 |
| 97748 | `public float m_chanceToUseGroundTilt` | 0 |
| 97750-51 | `[BitMask(typeof(Heightmap.Biome))] public Heightmap.Biome m_biome` | 0 (`None`) |
| 97753-54 | `[BitMask(typeof(Heightmap.BiomeArea))] public Heightmap.BiomeArea m_biomeArea` | `Heightmap.BiomeArea.Everything` |
| 97756 | `public bool m_blockCheck` | `true` |
| 97758 | `public bool m_snapToStaticSolid` | false |
| 97760 | `public float m_minAltitude` | `-1000f` |
| 97762 | `public float m_maxAltitude` | `1000f` |
| 97764 | `public float m_minVegetation` | 0 |
| 97766 | `public float m_maxVegetation` | 0 |
| 97770 | `public bool m_surroundCheckVegetation` | false |
| 97773 | `public float m_surroundCheckDistance` | `20f` |
| 97776 | `public int m_surroundCheckLayers` | `2` |
| 97779 | `public float m_surroundBetterThanAverage` | 0 |
| 97782 | `public float m_minOceanDepth` | 0 |
| 97784 | `public float m_maxOceanDepth` | 0 |
| 97786 | `public float m_minTilt` | 0 |
| 97788 | `public float m_maxTilt` | `90f` |
| 97790 | `public float m_terrainDeltaRadius` | 0 |
| 97792 | `public float m_maxTerrainDelta` | `2f` |
| 97794 | `public float m_minTerrainDelta` | 0 |
| 97796 | `public bool m_snapToWater` | false |
| 97798 | `public float m_groundOffset` | 0 |
| 97800 | `public int m_groupSizeMin` | `1` |
| 97802 | `public int m_groupSizeMax` | `1` |
| 97804 | `public float m_groupRadius` | 0 |
| 97807 | `public float m_minDistanceFromCenter` | 0 |
| 97809 | `public float m_maxDistanceFromCenter` | 0 |
| 97812 | `public bool m_inForest` | false |
| 97814 | `public float m_forestTresholdMin` | 0 |
| 97816 | `public float m_forestTresholdMax` | `1f` |
| 97819 | `[HideInInspector] public bool m_foldout` | false |

**`m_clearArea` is NOT a member of `ZoneVegetation` — NOT FOUND.** The only `m_clearArea` in ZoneSystem is `public bool m_clearArea;` at **AV.cs:97875, inside `ZoneLocation`** (class starts AV.cs:97828). Do not write `veg.m_clearArea` — compile error.

Fields the prompt did not list but that EXIST and matter: `m_minTilt`, `m_maxTilt`, `m_groundOffset`, `m_surroundCheckVegetation/Distance/Layers`, `m_surroundBetterThanAverage`, `m_minDistanceFromCenter`, `m_maxDistanceFromCenter`, `m_foldout`.

### Placement pipeline (why donor-cloning works)
```csharp
// AV.cs:98791
private bool SpawnZone(Vector2i zoneID, SpawnMode mode, out GameObject root)
// AV.cs:98806-98812
if ((mode == SpawnMode.Ghost || mode == SpawnMode.Full) && !IsZoneGenerated(zoneID)) {
    ...
    PlaceLocations(...); PlaceVegetation(zoneID, zonePos, root.transform, componentInChildren2, m_tempClearAreas, mode, m_tempSpawnedObjects); PlaceZoneCtrl(...);
    ...
    SetZoneGenerated(zoneID);
}

// AV.cs:98749  (in PokeLocalZone)
SpawnMode mode = ((!ZNet.instance.IsServer() || IsZoneGenerated(zoneID)) ? SpawnMode.Client : SpawnMode.Full);

// AV.cs:98854
private void PlaceVegetation(Vector2i zoneID, Vector3 zoneCenterPos, Transform parent, Heightmap hmap,
                             List<ClearArea> clearAreas, SpawnMode mode, List<GameObject> spawnedObjects)
// AV.cs:98859-98868 (seed determinism)
foreach (ZoneVegetation item in m_vegetation) {
    if (!item.m_enable || !hmap.HaveBiome(item.m_biome)) continue;
    UnityEngine.Random.InitState(seed + zoneID.x * 4271 + zoneID.y * 9187 + item.m_prefab.name.GetStableHashCode());
    ...
}
```

---

## 2. Heightmap.Biome — AV.cs:110588-110603

```csharp
[Flags]
public enum Biome            // AV.cs:110589
{
    None       = 0,          // 110591
    Meadows    = 1,          // 110592
    Swamp      = 2,          // 110593
    Mountain   = 4,          // 110594
    BlackForest= 8,          // 110595
    Plains     = 0x10,       // 110596  (16)
    AshLands   = 0x20,       // 110597  (32)
    DeepNorth  = 0x40,       // 110598  (64)
    Ocean      = 0x100,      // 110600  (256)
    Mistlands  = 0x200,      // 110601  (512)
    All        = 0x37F       // 110602  (895)
}
```
**Casing confirmed: `AshLands`** — capital `A`, capital `L`. Not `Ashlands`, not `ASHLANDS`. Value `0x20` / `32`.
Note the gap: `0x80` (128) is unused; `All = 0x37F` = 0b11_0111_1111 = Meadows|Swamp|Mountain|BlackForest|Plains|AshLands|DeepNorth|Ocean|Mistlands.

```csharp
[Flags] public enum BiomeArea { Edge = 1, Median = 2, Everything = 3 }   // AV.cs:110605-110610
```

---

## 3. Smelter — AV.cs:123533

```csharp
public class Smelter : MonoBehaviour, IHasHoverMenuExtended   // AV.cs:123533

[Serializable]                        // AV.cs:123535
public class ItemConversion           // AV.cs:123536  -- CLASS, not struct; [Serializable] YES
{
    public ItemDrop m_from;           // AV.cs:123538
    public ItemDrop m_to;             // AV.cs:123540
}

public List<ItemConversion> m_conversion = new List<ItemConversion>();   // AV.cs:123591  (declared type List<Smelter.ItemConversion>)
```
Other public fields you will likely touch: `m_name` 123543, `m_fuelItem` (`ItemDrop`) 123571, `m_maxOre` (`int`) 123573, `m_maxFuel` 123575, `m_fuelPerProduct` 123577, `m_secPerProduct` (`float`) 123579, `m_spawnStack` (`bool`) 123581, `m_requiresRoof` 123583, `m_outputPoint` (`Transform`) 123555.

### Runtime validation — matches on **GameObject NAME**, not item name
```csharp
// AV.cs:123689-123699
private bool IsItemAllowed(string itemName)
{
    foreach (ItemConversion item in m_conversion)
        if (item.m_from.gameObject.name == itemName) return true;
    return false;
}
// AV.cs:123684-123687
private bool IsItemAllowed(ItemDrop.ItemData item) => IsItemAllowed(item.m_dropPrefab.name);

// AV.cs:124073-124082
private ItemConversion GetItemConversion(string itemName)
{
    foreach (ItemConversion item in m_conversion)
        if (item.m_from.gameObject.name == itemName) return item;
    return null;
}
```
Flow: `OnAddOre` → `IsItemAllowed(item.m_dropPrefab.name)`; on fail `user.Message(MessageHud.MessageType.Center, "$msg_wontwork")` (AV.cs:123790-123794). Then `m_nview.InvokeRPC("RPC_AddOre", item.m_dropPrefab.name)` (AV.cs:123800). Server side `RPC_AddOre(long sender, string name)` (AV.cs:124055) re-runs `IsItemAllowed(name)` and logs `"Item not allowed " + name` then returns. Output: `Spawn(string ore, int stack)` (AV.cs:124061) does `GetItemConversion(ore)`; **if it returns null or `m_to == null`, the ore is silently consumed and nothing spawns.**

Both `IsItemAllowed` and `GetItemConversion` are **private** — reachable only via publicized assembly / AccessTools.

Also `m_conversion` is enumerated for the hover/tooltip via `conversion.m_from.m_itemData.m_shared.m_name` (AV.cs:124175, 124200) — so `m_from` must be a fully-initialized `ItemDrop` (from `ObjectDB.instance.GetItemPrefab(...).GetComponent<ItemDrop>()`), never a bare GameObject, or those lines NRE.

### Real vanilla conversion tables (VD.json, `_Source == "Prefabs"`)
| prefab | m_fuelItem | m_maxOre | m_fuelPerProduct | m_secPerProduct | conversions (from → to) |
|---|---|---|---|---|---|
| `smelter` | Coal | 10 | 2 | 30 | CopperOre→Copper, IronOre→Iron, IronScrap→Iron, BronzeScrap→Bronze, TinOre→Tin, SilverOre→Silver, CopperScrap→Copper |
| `blastfurnace` | Coal | 10 | 2 | 30 | FlametalOreNew→FlametalNew, BlackMetalScrap→BlackMetal, **FlametalOreNew→FlametalNew (duplicate index 2)** |
| `charcoal_kiln` | (none) | 25 | 0 | 15 | Wood→Coal, FineWood→Coal, RoundLog→Coal |
| `eitrrefinery` | Sap | 20 | 1 | 40 | Softtissue→Eitr |
| `piece_spinningwheel` | (none) | 40 | 0 | 30 | Flax→LinenThread (m_requiresRoof=true) |
| `windmill` | (none) | 50 | 0 | 10 | Barley→BarleyFlour (m_spawnStack=true) |

---

## 4. MineRock (AV.cs:116064) vs MineRock5 (AV.cs:116335)

```csharp
public class MineRock : MonoBehaviour, IDestructible, Hoverable      // AV.cs:116064
    public string m_name = "";                       // 116066
    public float m_health = 2f;                      // 116068
    public bool m_removeWhenDestroyed = true;        // 116070
    public HitData.DamageModifiers m_damageModifiers;// 116072
    public int m_minToolTier;                        // 116074
    public GameObject m_areaRoot;                    // 116076
    public GameObject m_baseModel;                   // 116078
    public EffectList m_destroyedEffect = new EffectList();  // 116080
    public EffectList m_hitEffect = new EffectList();        // 116082
    public DropTable m_dropItems;                    // 116084   <-- NOT a list; a DropTable instance
    public Action m_onHit;                           // 116086
    private Collider[] m_hitAreas;                   // 116088   <-- NOT "m_areas"
    private MeshRenderer[][] m_areaMeshes;           // 116090
    private ZNetView m_nview;                        // 116092
```
`MineRock.Start()` AV.cs:116094 builds `m_hitAreas` from `m_areaRoot.GetComponentsInChildren<Collider>()` (or own children if `m_areaRoot` null); registers `"Hit"` and `"Hide"` RPCs.

```csharp
public class MineRock5 : MonoBehaviour, IDestructible, Hoverable     // AV.cs:116335
    public string m_name = "";                       // 116373
    public float m_health = 2f;                      // 116375
    public HitData.DamageModifiers m_damageModifiers;// 116377
    public int m_minToolTier;                        // 116379
    public bool m_supportCheck = true;               // 116381
    public bool m_triggerPrivateArea;                // 116383
    public EffectList m_destroyedEffect;             // 116385
    public EffectList m_hitEffect;                   // 116387
    public DropTable m_dropItems;                    // 116389
    public bool m_hitEffectAreaCenter = true;        // 116391
    private List<HitArea> m_hitAreas;                // 116393   (private nested class HitArea, AV.cs:116346)
    private List<Renderer> m_extraRenderers;         // 116395
    private ZNetView m_nview;                        // 116399
```
`MineRock5` has **no `m_removeWhenDestroyed`, no `m_areaRoot`, no `m_baseModel`, no `m_onHit`** — those are MineRock-only.
`MineRock5.Awake()` AV.cs:116418 walks `GetComponentsInChildren<Collider>()`, requires each collider to have a `MeshRenderer` (AV.cs:116447-116451 dereferences `hitArea2.m_meshRenderer.sharedMaterials` **without a null check** → a collider child without MeshRenderer NREs at Awake), adds its own `MeshFilter`+`MeshRenderer`, registers `"RPC_Damage"` and `"RPC_SetAreaHealth"`.
Per-area HP: `hitArea.m_health = m_health + (float)Game.m_worldLevel * m_health * Game.instance.m_worldLevelMineHPMultiplier;` (AV.cs:116429).

**`m_areas` does not exist on either class — NOT FOUND (grep over the whole decompile returns zero hits).** Use `m_hitAreas` (private on both, different types: `Collider[]` vs `List<HitArea>`).

### Which one does Flametal actually use?
**Neither `MineRock_Flametal` nor any prefab by that name exists.** From VD.json `_Source == "Prefabs"`, the *only* prefabs whose drop table yields `FlametalOreNew` are:

| prefab | components | mine component | notes |
|---|---|---|---|
| **`MineRock_Meteorite`** | `Transform\|ZNetView\|MineRock\|LODGroup\|StaticPhysics` | **`MineRock` (legacy, NOT MineRock5)** | the actual flametal vein |
| `LeviathanLava` | `Transform\|ZNetView\|ZSyncTransform\|Leviathan\|Rigidbody\|MineRock\|ZSyncAnimation\|StaticPhysics` | `MineRock` | lava leviathan back |
| `dvergrprops_crate_ashlands` | `DropOnDestroyed` | — | crate |
| `Fish11` | `Fish.m_extraDrops` | — | fish |
| `TreasureChest_charredfortress` | `Container.m_defaultItems` | — | chest |

`FlametalRockstand_frac` (the only prefab with "Flametal" in the name that carries `MineRock5`) drops **Coal**, not flametal — it is decor, do NOT use it as an ore donor.

**`MineRock_Meteorite` verbatim (VD.json, `_Source":"Prefabs"`):**
```
_components = Transform|ZNetView|MineRock|LODGroup|StaticPhysics
_children   = Point light|smoke|default
ZNetView.m_persistent = true ; m_distant = false ; m_type = Default ; m_syncInitialScale = false
MineRock.m_name = $item_flametalore
MineRock.m_health = 100
MineRock.m_removeWhenDestroyed = true
MineRock.m_damageModifiers = blunt/slash/pierce/chop/fire/frost/lightning/poison/spirit = Immune, m_pickaxe = Normal
MineRock.m_minToolTier = 3
MineRock.m_areaRoot = (null)     MineRock.m_baseModel = (null)
MineRock.m_destroyedEffect = [vfx_RockDestroyed, sfx_rock_destroyed]
MineRock.m_hitEffect       = [vfx_RockHit, sfx_rock_hit]
MineRock.m_dropItems.m_drops[0] = { m_item=FlametalOreNew, m_stackMin=3, m_stackMax=8, m_weight=1,   m_dontScale=false }
MineRock.m_dropItems.m_drops[1] = { m_item=Stone,          m_stackMin=1, m_stackMax=3, m_weight=0.5, m_dontScale=false }
MineRock.m_dropItems.m_dropMin = 2 ; m_dropMax = 2 ; m_dropChance = 1 ; m_oneOfEach = true
StaticPhysics.m_pushUp = true ; m_fall = true ; m_checkSolids = true ; m_fallCheckRadius = 1.5
```
`FlametalRockstand` (parent, for contrast) is `Transform|ZNetView|LODGroup|LodFadeInOut|Destructible`, `Destructible.m_health = 1`, `m_spawnWhenDestroyed = FlametalRockstand_frac`, and `FlametalRockstand_frac` is `MineRock5` with `m_health = 70`, `m_minToolTier = 0`, drops `Coal` ×4-8.

Nearest MineRock5 *ore-style* donor if you want MineRock5 semantics: `silvervein_frac`, `rock3_silver_frac`, `rock4_copper_frac`, `rock4_ashlands_frac` (`m_health=50`, `m_minToolTier=0`, drops Stone w=0.2 / Grausten w=1, dropMin 3 dropMax 6, oneOfEach=false).

---

## 5. DropTable — AV.cs:56713-56747

```csharp
[Serializable]                      // AV.cs:56713
public class DropTable              // AV.cs:56714   -- CLASS
{
    [Serializable]                  // AV.cs:56716
    public struct DropData          // AV.cs:56717   -- STRUCT (value type!)
    {
        public GameObject m_item;   // 56719
        public int   m_stackMin;    // 56721
        public int   m_stackMax;    // 56723
        public float m_weight;      // 56725
        public bool  m_dontScale;   // 56727
    }

    private static List<DropData>          drops     = new List<DropData>();  // 56730
    private static List<ItemDrop.ItemData> toDrop    = new List<ItemDrop.ItemData>(); // 56732
    private static List<DropData>          dropsTemp = new List<DropData>();  // 56734

    public List<DropData> m_drops = new List<DropData>();   // 56736
    public int   m_dropMin   = 1;                            // 56738
    public int   m_dropMax   = 1;                            // 56740
    [Range(0f, 1f)] public float m_dropChance = 1f;          // 56742-56743
    public bool  m_oneOfEach;                                // 56745

    public DropTable Clone() { return MemberwiseClone() as DropTable; }  // 56747-56750
}
```
`GetDropListItems()` (AV.cs:56752+): rolls `Random.Range(m_dropMin, m_dropMax + 1)` picks, weighted by `m_weight` over the sum; `m_oneOfEach` removes the picked entry and subtracts its weight. Early-outs if `m_drops.Count == 0` or `Random.value > m_dropChance`.
`DropTable.Clone()` is a **shallow** MemberwiseClone — the cloned table's `m_drops` is the SAME `List<DropData>` reference. Mutating `clone.m_drops` mutates the donor. Assign a fresh `new List<DropTable.DropData>(orig.m_drops)` after cloning.
Because `DropData` is a struct, `table.m_drops[0].m_item = x` does not compile — read out, modify, write back by index.

---

## 6. Two real AshLands ZoneVegetation records, verbatim (VD.json, `_Source == "Vegetation"`)

The dump's per-record key list is exactly the ZoneVegetation field set (minus `m_clearArea`, confirming its absence):
`m_name, m_prefab, m_enable, m_min, m_max, m_forcePlacement, m_scaleMin, m_scaleMax, m_randTilt, m_chanceToUseGroundTilt, m_biome, m_biomeArea, m_blockCheck, m_snapToStaticSolid, m_minAltitude, m_maxAltitude, m_minVegetation, m_maxVegetation, m_surroundCheckVegetation, m_surroundCheckDistance, m_surroundCheckLayers, m_surroundBetterThanAverage, m_minOceanDepth, m_maxOceanDepth, m_minTilt, m_maxTilt, m_terrainDeltaRadius, m_maxTerrainDelta, m_minTerrainDelta, m_snapToWater, m_groundOffset, m_groupSizeMin, m_groupSizeMax, m_groupRadius, m_minDistanceFromCenter, m_maxDistanceFromCenter, m_inForest, m_forestTresholdMin, m_forestTresholdMax, m_foldout`

**Donor A — `tree_ashlands1` (dense group vegetation, ENABLED):**
```json
{ "_Key":"tree_ashlands1", "_Source":"Vegetation",
  "m_name":"tree_ashlands1", "m_prefab":"AshlandsTree1", "m_enable":"true",
  "m_min":"6", "m_max":"10", "m_forcePlacement":"false",
  "m_scaleMin":"0.7", "m_scaleMax":"1.4", "m_randTilt":"4", "m_chanceToUseGroundTilt":"0.5",
  "m_biome":"AshLands", "m_biomeArea":"Everything", "m_blockCheck":"true", "m_snapToStaticSolid":"false",
  "m_minAltitude":"2", "m_maxAltitude":"12", "m_minVegetation":"0", "m_maxVegetation":"0.2",
  "m_surroundCheckVegetation":"false", "m_surroundCheckDistance":"20", "m_surroundCheckLayers":"2",
  "m_surroundBetterThanAverage":"0", "m_minOceanDepth":"0", "m_maxOceanDepth":"0",
  "m_minTilt":"0", "m_maxTilt":"30", "m_terrainDeltaRadius":"0", "m_maxTerrainDelta":"0",
  "m_minTerrainDelta":"0", "m_snapToWater":"false", "m_groundOffset":"0",
  "m_groupSizeMin":"4", "m_groupSizeMax":"8", "m_groupRadius":"40",
  "m_minDistanceFromCenter":"0", "m_maxDistanceFromCenter":"0",
  "m_inForest":"false", "m_forestTresholdMin":"0", "m_forestTresholdMax":"0", "m_foldout":"false" }
```

**Donor B — `ashstone` (pickable-node style, high altitude, Median area, ENABLED — closest shape to an ore-node placement):**
```json
{ "_Key":"ashstone", "_Source":"Vegetation",
  "m_name":"ashstone", "m_prefab":"Pickable_Ashstone", "m_enable":"true",
  "m_min":"7", "m_max":"15", "m_forcePlacement":"false",
  "m_scaleMin":"1", "m_scaleMax":"1", "m_randTilt":"180", "m_chanceToUseGroundTilt":"20",
  "m_biome":"AshLands", "m_biomeArea":"Median", "m_blockCheck":"true", "m_snapToStaticSolid":"false",
  "m_minAltitude":"30", "m_maxAltitude":"1000", "m_minVegetation":"0", "m_maxVegetation":"0.15",
  "m_surroundCheckVegetation":"false", "m_surroundCheckDistance":"20", "m_surroundCheckLayers":"2",
  "m_surroundBetterThanAverage":"0", "m_minOceanDepth":"0", "m_maxOceanDepth":"0",
  "m_minTilt":"0", "m_maxTilt":"99", "m_terrainDeltaRadius":"0", "m_maxTerrainDelta":"0",
  "m_minTerrainDelta":"0", "m_snapToWater":"false", "m_groundOffset":"0.1",
  "m_groupSizeMin":"10", "m_groupSizeMax":"20", "m_groupRadius":"40",
  "m_minDistanceFromCenter":"0", "m_maxDistanceFromCenter":"0",
  "m_inForest":"false", "m_forestTresholdMin":"0", "m_forestTresholdMax":"0", "m_foldout":"false" }
```

Bonus (disabled in vanilla, but the only AshLands entry that references a flametal-dropping prefab): `LeviathanLava` — `m_enable:"false"`, `m_min:"0"`, `m_max:"1"`, `m_minAltitude:"-10"`, `m_maxAltitude:"1000"`, `m_minVegetation:"0.5"`, `m_maxVegetation:"1"`, `m_surroundCheckVegetation:"true"`, `m_surroundCheckDistance:"30"`, `m_maxTilt:"40"`, `m_groupRadius:"40"`.

All 156 vegetation records / 27 AshLands entries; enabled AshLands `_Key`s: `cliff_ashlands2, cliff_ashlands3, cliff_ashlands5 (x2), cliff_ashlands6, cliff_ashlands8 (x2), tree_ashlands1..4, rock_ashlands2, rock_ashlands3, branch_ashlands1..3, bush_ashlands1, skull, pot_shard, ashstone, pot2_red, UnstableLavaRock, mushroom_SmokePuff, veg (x4)`. Disabled AshLands: `cliff_ashlands1, cliff_ashlands4, cliff_ashlands7, cliff_ashlands9, rock_ashlands1, LeviathanLava`.

**There is NO vegetation entry for `MineRock_Meteorite` (or any `MineRock_*` in AshLands).** The only MineRock-family vegetation entries in the whole dump are `flint → MineRock_Tin` (BlackForest) and `veg → MineRock_Obsidian` (Mountain). Flametal veins therefore reach the world only through Locations / the LeviathanLava prefab, not through ZoneSystem vegetation — so registering a new `ZoneVegetation` entry for a flametal-vein clone is a genuinely new placement path, not an override of an existing one.


## GOTCHAS
- ZoneVegetation has NO m_clearArea field. Writing veg.m_clearArea is a compile error. m_clearArea (public bool, AV.cs:97875) belongs to ZoneSystem.ZoneLocation only.
- Heightmap.Biome.AshLands is spelled with capital A and capital L, value 0x20 (32). Ashlands / ASHLANDS do not compile. Biome is [Flags]; All = 0x37F; 0x80 is an unused gap.
- There is NO prefab named MineRock_Flametal. The real flametal vein is MineRock_Meteorite and it uses the LEGACY MineRock component, not MineRock5. FlametalRockstand_frac is the only Flametal-named MineRock5 and it drops Coal, not ore.
- Neither MineRock nor MineRock5 has a field named m_areas (zero grep hits in the decompile). MineRock has `private Collider[] m_hitAreas` (AV.cs:116088); MineRock5 has `private List<HitArea> m_hitAreas` (AV.cs:116393) with a private nested HitArea class. Both private, reachable only via the publicized assembly.
- ZoneSystem.m_vegetation is EMPTY during Awake. It is only populated in Start() -> SetupLocations() (AV.cs:98423, `m_vegetation.AddRange(item.m_vegetation)`). Inject with a Postfix on ZoneSystem.SetupLocations or ZoneSystem.Start, before ValidateVegetation runs.
- MULTIPLAYER: PlaceVegetation only runs with SpawnMode.Full, and `SpawnMode mode = ((!ZNet.instance.IsServer() || IsZoneGenerated(zoneID)) ? SpawnMode.Client : SpawnMode.Full)` (AV.cs:98749). Only the SERVER/HOST generates vegetation. A client-only install adds nothing to the world; a server-only install works. But the client MUST still have the prefab registered in ZNetScene or the ZDO arrives with no prefab and the object never instantiates for that client.
- MULTIPLAYER DETERMINISM: PlaceVegetation seeds with `seed + zoneID.x*4271 + zoneID.y*9187 + item.m_prefab.name.GetStableHashCode()` (AV.cs:98866) and marks the zone SetZoneGenerated permanently (AV.cs:98819). Changing m_prefab.name, or adding/removing vegetation entries after a world has generated zones, changes the RNG stream for every entry and produces different results only in NOT-YET-generated zones. Already-generated zones are never re-rolled.
- Vegetation prefabs must carry a ZNetView or ValidateVegetation logs an error (AV.cs:98503-98512) and the object will not persist/replicate. Register the prefab in ZNetScene.m_prefabs on BOTH server and client with an identical stable-hash name, or joining clients desync.
- Smelter matches conversions by GameObject NAME (item.m_from.gameObject.name == itemName), not by m_shared.m_name. If your cloned ItemDrop prefab's GameObject name differs from the ItemDrop's m_dropPrefab.name, IsItemAllowed silently returns false and the player sees $msg_wontwork.
- Smelter.Spawn silently eats the ore if GetItemConversion returns null or m_to is null (AV.cs:124061-124071). A half-added conversion (m_from set, m_to null) destroys player ore with no error. Smelter's tooltip path dereferences conversion.m_from.m_itemData.m_shared.m_name (AV.cs:124175) so m_from must be a real ObjectDB ItemDrop, not a bare GameObject.
- Smelter RPC_AddOre re-validates server-side (AV.cs:124055-124060). If the server lacks the mod's conversion entry but the client has it, the ore is removed from the client's inventory (AV.cs:123799) and then rejected on the server -> item destroyed. Conversions MUST be config-synced via ServerSync or added identically on both sides.
- DropTable.DropData is a STRUCT (AV.cs:56717). `table.m_drops[0].m_item = x` will not compile; read the element into a local, mutate, write back by index. DropTable.Clone() is MemberwiseClone -> the clone SHARES the same List<DropData> instance; replace it with `new List<DropTable.DropData>(orig.m_drops)` or you mutate the donor prefab.
- ZoneVegetation is a reference-type [Serializable] class, so adding the same instance twice puts two aliases in the list. Always use veg.Clone() (AV.cs:97821) before mutating a donor entry.
- MineRock5.Awake dereferences hitArea2.m_meshRenderer.sharedMaterials with no null check (AV.cs:116447-116451). Any child Collider lacking a MeshRenderer NREs on spawn. MineRock5 also AddComponent<MeshFilter>/<MeshRenderer> onto the root at Awake, so cloning must not pre-add those.
- MineRock5 per-area HP is world-level scaled at Awake (`m_health + Game.m_worldLevel * m_health * Game.instance.m_worldLevelMineHPMultiplier`, AV.cs:116429) then overwritten by LoadHealth() from the ZDO. Changing m_health on the prefab on only some clients causes visible/mining desync until the owner's SaveHealth propagates.
- MineRock (legacy) registers RPCs "Hit"/"Hide"; MineRock5 registers "RPC_Damage"/"RPC_SetAreaHealth". They are not interchangeable — patching the wrong RPC name is a silent no-op.
- blastfurnace ships a DUPLICATE conversion (index 0 and index 2 are both FlametalOreNew -> FlametalNew). Any code that assumes m_conversion is a unique keyed set, or that dedupes/removes by predicate, will behave unexpectedly.

## NOT FOUND
- ZoneSystem.ZoneVegetation.m_clearArea — does not exist. Only ZoneSystem.ZoneLocation.m_clearArea (public bool, AV.cs:97875).
- MineRock.m_areas / MineRock5.m_areas — no member of that name anywhere in the decompile. Actual: MineRock.m_hitAreas (private Collider[], AV.cs:116088) and MineRock5.m_hitAreas (private List<HitArea>, AV.cs:116393).
- MineRock.m_dropItems is a single DropTable (AV.cs:116084), NOT a List; there is no MineRock.m_dropTable, no MineRock.DropTable property.
- MineRock5.m_removeWhenDestroyed, MineRock5.m_areaRoot, MineRock5.m_baseModel, MineRock5.m_onHit — MineRock-only, absent from MineRock5.
- Prefab 'MineRock_Flametal' — does not exist in the Prefabs dump (3458 records). Also absent: 'MineRock_FlametalNew', 'FlametalVein', 'flametal_vein'.
- Any ZoneVegetation entry whose m_prefab is MineRock_Meteorite (or any MineRock_* prefab in AshLands) — none in the 156 Vegetation records. Only 'flint -> MineRock_Tin' (BlackForest) and 'veg -> MineRock_Obsidian' (Mountain).
- Any Locations record referencing 'Flametal' or 'Meteorite' — zero matches across the 188 Location records.
- ZoneVegetation.m_clearArea, and no Clone() on ZoneLocation was checked — but ZoneVegetation.Clone() DOES exist (AV.cs:97821) and DropTable.Clone() DOES exist (AV.cs:56747).
- Smelter.ItemConversion has exactly two fields (m_from, m_to). No m_secPerProduct, no m_fuelPerProduct, no m_amount, no m_stack on the conversion itself — those live on the Smelter component.
- A public accessor for ZoneSystem.SpawnZone / PlaceVegetation / SetupLocations / ValidateVegetation / Awake / Start — all private (AV.cs:98791, 98854, 98414, 98503, 98223, 98238). Reachable only because the assembly is publicized, or via Harmony AccessTools.