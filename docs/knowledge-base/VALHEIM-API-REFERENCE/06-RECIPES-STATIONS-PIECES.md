All citations are `assembly_valheim.decompiled.cs:LINE` from
`C:\WubarrkCODING\libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs`.
Values from `C:\WubarrkCODING\libs-Tools\WubarrksEye_Dumps\2026-07-31_21-06-43\Values_Dump.json`.

## 1. `class Recipe : ScriptableObject` — :60499

```csharp
public class Recipe : ScriptableObject                          // :60499
{
    public ItemDrop m_item;                                     // :60501
    public int      m_amount = 1;                               // :60503
    public bool     m_enabled = true;                           // :60505
    [Tooltip("Only supported when using m_requireOnlyOneIngredient")]
    public float    m_qualityResultAmountMultiplier = 1f;       // :60508
    public int      m_listSortWeight = 100;                     // :60510
    [Header("Requirements")]
    public CraftingStation m_craftingStation;                   // :60513
    public CraftingStation m_repairStation;                     // :60515
    public int      m_minStationLevel = 1;                      // :60517
    public bool     m_requireOnlyOneIngredient;                 // :60519
    public Piece.Requirement[] m_resources = new Piece.Requirement[0]; // :60521

    public int GetRequiredStationLevel(int quality);             // :60523  => Mathf.Max(1,m_minStationLevel)+(quality-1)
    public CraftingStation GetRequiredStation(int quality);      // :60528  => m_craftingStation, else (quality>1 ? m_repairStation : null)
    public int GetAmount(int quality, out int need, out ItemDrop.ItemData singleReqItem, int craftMultiplier = 1); // :60541
}
```
Every field is **public instance**; no publicizer needed. `Recipe` has no `m_dlc` — the DLC gate is read from `m_item.m_itemData.m_shared.m_dlc` (:17842).
`Recipe.GetAmount` dereferences `Player.m_localPlayer` unconditionally when `m_requireOnlyOneIngredient` is true (:60548) — null-ref outside gameplay.
Create with `ScriptableObject.CreateInstance<Recipe>()`; nothing reads `recipe.name` (sorting uses `m_listSortWeight` :42186 and the localized `m_item...m_shared.m_name` :42181).

## 2. `class Piece.Requirement` — :117954 (nested in `Piece`)

```csharp
[Serializable]
public class Requirement          // CLASS, not struct — reference type, [Serializable]   :117953-117954
{
    [Header("Resource")] public ItemDrop m_resItem;             // :117957
    public int  m_amount = 1;                                   // :117959
    public int  m_extraAmountOnlyOneIngredient;                 // :117961
    [Header("Item")]  public int  m_amountPerLevel = 1;         // :117964
    [Header("Piece")] public bool m_recover = true;             // :117967

    public int GetAmount(int qualityLevel)                      // :117969
    {
        if (qualityLevel <= 1) return m_amount;
        return (qualityLevel - 1) * m_amountPerLevel;           // NOT m_amount + ...
    }
}
```
Full name for `using`-free code: `Piece.Requirement`. Because it is a class, array elements must be `new Piece.Requirement { ... }` (nulls in the array are legal — consumers skip `!requirement.m_resItem`, :17858, :17962).

## 3. `class CraftingStation : MonoBehaviour, Hoverable, Interactable, IMonoUpdater` — :56331

| Member | Decl | Line |
|---|---|---|
| `m_name` | `public string = ""` | :56333 |
| `m_icon` | `public Sprite` | :56335 |
| `m_discoverRange` | `public float = 4f` | :56337 |
| `m_rangeBuild` | `public float = 10f` | :56339 |
| `m_extraRangePerLevel` | `public float` | :56341 |
| `m_craftRequireRoof` | `public bool = true` | :56343 |
| `m_craftRequireFire` | `public bool = true` | :56345 |
| `m_roofCheckPoint` / `m_connectionPoint` | `public Transform` | :56347-56349 |
| `m_showBasicRecipies` | `public bool` | :56351 |
| `m_useDistance` | `public float = 2f` | :56353 |
| `m_effectAreaCollider` | `public Collider` | :56355 |
| `m_useAnimation` | `public int` | :56357 |
| `m_craftingSkill` | `public Skills.SkillType = Crafting` | :56359 |
| `m_areaMarker`, `m_inUseObject`, `m_haveFireObject` | `public GameObject` | :56361-56365 |
| `m_craftItemEffects` / `m_craftItemDoneEffects` / `m_repairItemDoneEffects` | `public EffectList` | :56367-56371 |
| **`m_allStations`** | **`private static List<CraftingStation>`** | :56389 |
| `m_attachedExtensions` | `private List<StationExtension>` | :56387 |
| `Instances` | `public static List<IMonoUpdater> { get; }` | :56395 |

`m_allStations` is **private static** — reachable only via the publicized assembly. It is filled in `Start()` (:56397-56402), only when `!m_nview || m_nview.GetZDO() != null`, and removed in `OnDestroy` (:56426).

Statics / helpers:
```csharp
public static CraftingStation GetCraftingStation(Vector3 point);                                  // :56567 (OverlapSphere, tag "StationUseArea")
public static CraftingStation HaveBuildStationInRange(string name, Vector3 point);                // :56588  MATCHES BY m_name STRING
public static void FindStationsInRange(string name, Vector3 point, float range, List<CraftingStation> stations); // :56605
public static CraftingStation FindClosestStationInRange(string name, Vector3 point, float range); // :56616
public bool  CheckUsable(Player player, bool showMessage);                                        // :56459
public int   GetLevel(bool checkExtensions = true);                                               // :56688 => 1 + GetExtentionCount()
public int   GetExtentionCount(bool checkExtensions = true);                                      // :56693
public float GetStationBuildRange();                                                              // :56702 => m_rangeBuild + extensions*m_extraRangePerLevel (:56642)
public bool  InUseDistance(Humanoid human);                                                       // :56708 (< m_useDistance)
public void  PokeInUse();                                                                         // :56561
```
`StationExtension` (:124497): `public CraftingStation m_craftingStation` (:124499), `public float m_maxStationDistance = 5f` (:124501), `public bool m_stack` (:124503) — each attached extension adds +1 station level.

## 4. How a Recipe resolves its station — **BY NAME STRING, not by reference**

```csharp
public bool RequiredCraftingStation(Recipe recipe, int qualityLevel, bool checkLevel)  // Player :17800
{
    CraftingStation requiredStation = recipe.GetRequiredStation(qualityLevel);          // :17802
    if (requiredStation != null) {
        if (m_currentStation == null) return false;                                     // :17805
        if (requiredStation.m_name != m_currentStation.m_name) return false;            // :17809  <-- STRING COMPARE
        if (checkLevel && m_currentStation.GetLevel() < recipe.GetRequiredStationLevel(qualityLevel)) return false; // :17815
    }
    else if (m_currentStation != null && !m_currentStation.m_showBasicRecipies) ...     // :17822
}
```
So: `Recipe.m_craftingStation` must be a **CraftingStation component reference** (the one on the station prefab), but at runtime only `m_name` is compared. Reusing a vanilla station = set `m_craftingStation` to the component on the vanilla prefab (or any component whose `m_name` matches, e.g. `"$piece_workbench"`, `"$piece_forge"`).

Discovery gate (:17833): `KnowStationLevel(recipe.m_craftingStation.m_name, recipe.m_minStationLevel)` — `m_knownStations` is `Dictionary<string,int>` keyed by **m_name** (:15580, :20130 `AddKnownStation`, :20151 `KnowStationLevel`).

Related Player API:
```csharp
public bool HaveRequirements(Recipe recipe, bool discover, int qualityLevel, int amount = 1); // :17829
private bool HaveRequirementItems(Recipe piece, bool discover, int qualityLevel, int amount = 1); // :17853
public ItemDrop.ItemData GetFirstRequiredItem(Inventory inventory, Recipe recipe, int qualityLevel, out int amount, out int extraAmount, int craftMultiplier = 1); // :17910
public bool HaveRequirements(Piece piece, RequirementMode mode);           // :17935
public void ConsumeResources(Piece.Requirement[] requirements, int qualityLevel, int itemQuality = -1, int multiplier = 1); // :17991
public void SetCraftingStation(CraftingStation station);                  // :19343 (calls AddKnownStation)
public CraftingStation GetCurrentCraftingStation();                       // :19357
public void AddKnownStation(CraftingStation station);                     // :20130
public bool IsRecipeKnown(string name);                                   // :20104
public void GetAvailableRecipes(ref List<Recipe> available);              // :20455
public enum Player.RequirementMode { CanBuild, IsKnown, CanAlmostBuild }  // :15322
```

## 5. `class Piece : StaticTarget, IPlaced` — :117926

```csharp
public enum PieceCategory {   // :117928
    Misc = 0, Crafting = 1, BuildingWorkbench = 2, BuildingStonecutter = 3,
    Furniture = 4, Feasts = 5, Food = 6, Meads = 7, Max = 8, All = 100 }
public enum ComfortGroup { None, Fire, Bed, Banner, Chair, Table, Carpet }  // :117942
```
| Field | Decl | Line |
|---|---|---|
| `m_icon` | `public Sprite` | :117982 |
| `m_name` | `public string = ""` | :117984 |
| `m_description` | `public string = ""` | :117986 |
| `m_enabled` | `public bool = true` | :117988 |
| `m_category` | `public PieceCategory` | :117990 |
| `m_isUpgrade` | `public bool` | :117992 |
| `m_comfort` | `public int` | :117995 |
| `m_comfortGroup` | `public ComfortGroup` | :117997 |
| `m_comfortObject` | `public GameObject` | :117999 |
| `m_groundPiece` | `public bool` | :118002 |
| `m_allowAltGroundPlacement`/`m_groundOnly`/`m_cultivatedGroundOnly`/`m_waterPiece`/`m_clipGround`/`m_clipEverything`/`m_noInWater`/`m_notOnWood`/`m_notOnTiltingSurface`/`m_inCeilingOnly`/`m_notOnFloor`/`m_noClipping`/`m_onlyInTeleportArea` | `public bool` | :118004-118028 |
| `m_allowedInDungeons` | `public bool` | :118030 |
| `m_spaceRequirement` | `public float` | :118032 |
| `m_repairPiece`,`m_removePiece`,`m_canRotate=true`,`m_randomInitBuildRotation`,`m_canBeRemoved=true`,`m_canRockJade`,`m_allowRotatedOverlap`,`m_vegetationGroundOnly` | `public bool` | :118034-118048 |
| `m_blockingPieces` | `public List<Piece>` | :118050 |
| `m_mustConnectTo` | `public ZNetView` | :118054 |
| `m_onlyInBiome` | `public Heightmap.Biome` (`[BitMask]`) | :118065 |
| `m_placeEffect` | `public EffectList` | :118075 |
| `m_dlc` | `public string = ""` | :118078 |
| `m_craftingStation` | `public CraftingStation` | :118080 |
| `m_resources` | `public Requirement[] = Array.Empty<Requirement>()` | :118084 |
| `m_destroyedLootPrefab` | `public GameObject` | :118086 |
| `s_allPieces` | `private static readonly List<Piece>` | :118102 |

Build gate for a piece (:17937-17949): if `m_craftingStation` set → mode `IsKnown`/`CanAlmostBuild` needs `m_knownStations.ContainsKey(piece.m_craftingStation.m_name)`; otherwise `CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, transform.position)` (again **by name**), bypassed by global key `NoWorkbench`. Same check at placement time (:18026 → `"$msg_missingstation"`).

## 6. `class PieceTable : MonoBehaviour` — :60189

```csharp
public const int m_gridWidth = 15;                              // :60191
public const int m_gridHeight = 6;                              // :60193
public List<GameObject> m_pieces = new List<GameObject>();      // :60195   <-- add your prefab HERE
public List<Piece.PieceCategory> m_categories = new List<...>();// :60197   (visible tabs / tab order)
public List<string> m_categoryLabels = new List<string>();      // :60199
public bool m_canRemovePieces = true;                           // :60201
public bool m_canRemoveFeasts;                                  // :60203
public Skills.SkillType m_skill;                                // :60205
[NonSerialized] private List<List<Piece>> m_availablePieces;    // :60208 (always 8 lists, :60225)
private Piece.PieceCategory m_selectedCategory = Max;           // :60210
[NonSerialized] public Vector2Int[] m_selectedPiece  = new Vector2Int[8];  // :60213
[NonSerialized] public Vector2Int[] m_lastSelectedPiece = new Vector2Int[8]; // :60216
[HideInInspector] public List<Piece.PieceCategory> m_categoriesFolded;      // :60219

public void UpdateAvailable(HashSet<string> knownRecipies, Player player, bool hideUnavailable, bool noPlacementCost); // :60221
public GameObject GetSelectedPrefab();                                        // :60256
public Piece GetPiece(Piece.PieceCategory category, Vector2Int p);            // :60266
public Piece GetPiece(Vector2Int p);                                          // :60289
public bool IsPieceAvailable(Piece piece);                                    // :60294
public Piece.PieceCategory GetSelectedCategory();                             // :60306
public Piece GetSelectedPiece();                                              // :60319
public int GetAvailablePiecesInCategory(Piece.PieceCategory cat);             // :60325
public List<Piece> GetPiecesInSelectedCategory();                             // :60330
public int GetAvailablePiecesInSelectedCategory();                            // :60335
public Vector2Int GetSelectedIndex();                                         // :60340
public bool GetPieceIndex(Piece p, out Vector2Int index, out int category);   // :60345
public void SetSelected(Vector2Int p);                                        // :60380
public void LeftPiece()/RightPiece()/DownPiece()/UpPiece();                   // :60385/:60400/:60415/:60430
public void NextCategory()/PrevCategory();                                    // :60445/:60468
public void SetCategory(int index);                                           // :60491 (index into m_categories)
```
There is **no `AddPiece` method** — you mutate `m_pieces` directly.

### The Hammer's PieceTable — exact prefab name
`_HammerPieceTable` — verbatim from the values dump, item record `{"_Key":"Hammer","_Source":"Items"}`:
`"m_shared.m_buildPieces": "_HammerPieceTable"` (Values_Dump.json:227123; the Prefabs-source copy of the same object is `"ItemDrop.m_itemData.m_shared.m_buildPieces": "_HammerPieceTable"`, :679152).
Other tables in the dump: `_CultivatorPieceTable` (:112553), `_FeasterPieceTable` (:147454), `_HoePieceTable` (:244087).

**Yes**, reachable exactly as asked:
```csharp
public PieceTable m_buildPieces;   // ItemDrop.ItemData.SharedData  :58046  (public instance)
// ObjectDB.instance.GetItemPrefab("Hammer").GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces.m_pieces.Add(myPiecePrefab);
```
`ObjectDB.GetItemPrefab(string)` :90555 → `GetItemPrefab(int hash)` :90560 → `m_itemByHash` (private dict, :90508) built in `UpdateRegisters()` :90528. So the hammer must already be in `ObjectDB.m_items` when you run — patch **after** `ObjectDB.Awake` (:90514) *and* `ObjectDB.CopyOtherDB(ObjectDB other)` (:90520), because `CopyOtherDB` **replaces** `m_items`/`m_recipes`/`m_StatusEffects` wholesale with the other DB's lists on scene load.

### How a piece actually shows up in the Hammer menu (no Jotunn)
1. Prefab must be registered in `ZNetScene.m_prefabs` (`public List<GameObject>` :69584) — hashed into `m_namedPrefabs` in `ZNetScene.Awake` (:69604-69610) via `prefab.name.GetStableHashCode()`.
2. `PieceTable.m_pieces.Add(prefab)` on `_HammerPieceTable`.
3. `Player.UpdateKnownRecipesList()` (**private**, :20239) walks `m_inventory.GetAllPieceTables(...)` (`public void GetAllPieceTables(List<PieceTable> tables)` Inventory :57270 — it collects tables from *held/inventory items*, so the player must own a Hammer) and calls private `AddKnownPiece(Piece)` (:20119) which adds `piece.m_name` to `m_knownRecipes` when `HaveRequirements(component, RequirementMode.IsKnown)` (:20262).
4. `PieceTable.UpdateAvailable` skips any piece whose `component.m_name` is not in `knownRecipies` (:60238) unless `noPlacementCost`.
So the piece is only visible after the player has *discovered every m_resItem material* (`m_knownMaterial`, :17969) and knows the station name — same rule as vanilla. Force a refresh by calling the private `Player.m_localPlayer.UpdateKnownRecipesList()` / `UpdateAvailablePiecesList()` (:20275) through the publicizer or Harmony reverse patch.

## 7. Registering a Recipe (no Jotunn)
`ObjectDB` :90498
```csharp
private static ObjectDB m_instance;                      // :90500
public List<StatusEffect> m_StatusEffects;               // :90502
public List<GameObject>   m_items;                       // :90504
public List<Recipe>       m_recipes;                     // :90506   <-- Add() here
private Dictionary<int,GameObject> m_itemByHash;         // :90508
public static ObjectDB instance => m_instance;           // :90512
private void Awake();                                    // :90514  (m_instance = this; UpdateRegisters())
public void CopyOtherDB(ObjectDB other);                 // :90520  (REPLACES the three lists, then UpdateRegisters())
private void UpdateRegisters();                          // :90528
public GameObject GetItemPrefab(string name / int hash / ItemDrop.ItemData.SharedData); // :90555/:90560/:90569
public bool TryGetItemPrefab(...out GameObject prefab);  // :90578/:90583/:90588
public int  GetPrefabHash(GameObject prefab);            // :90593
public List<ItemDrop> GetAllItems(ItemDrop.ItemData.ItemType type, string startWith); // :90598
public Recipe GetRecipe(ItemDrop.ItemData item);         // :90612 (matches by m_item...m_shared.m_name)
```
There is **no `ObjectDB.AddRecipe`/`AddItem`** — `m_recipes.Add(recipe)` in a Harmony postfix on `Awake` and on `CopyOtherDB`.

Recipe visibility in the crafting GUI: `Player.GetAvailableRecipes` (:20455) requires `m_enabled`, non-null `m_item`, `m_knownRecipes.Contains(m_item.m_itemData.m_shared.m_name)`, and `RequiredCraftingStation(recipe, 1, checkLevel:false)`; `InventoryGui.UpdateRecipeList(List<Recipe>)` (:42030) then calls `HaveRequirements(recipe, discover:false, 1)`.

## 8. Verbatim VALUES-dump records

**Weapon recipe** (Values_Dump.json:417572):
```json
{
  "_Key": "SwordIron",
  "_Source": "Recipes",
  "m_item": "SwordIron",
  "m_amount": "1",
  "m_enabled": "true",
  "m_qualityResultAmountMultiplier": "1",
  "m_listSortWeight": "100",
  "m_craftingStation": "forge",
  "m_repairStation": "",
  "m_minStationLevel": "2",
  "m_requireOnlyOneIngredient": "false",
  "m_resources[0].m_resItem": "Wood",
  "m_resources[0].m_amount": "2",
  "m_resources[0].m_extraAmountOnlyOneIngredient": "0",
  "m_resources[0].m_amountPerLevel": "1",
  "m_resources[0].m_recover": "true",
  "m_resources[1].m_resItem": "Iron",
  "m_resources[1].m_amount": "20",
  "m_resources[1].m_extraAmountOnlyOneIngredient": "0",
  "m_resources[1].m_amountPerLevel": "10",
  "m_resources[1].m_recover": "true",
  "m_resources[2].m_resItem": "LeatherScraps",
  "m_resources[2].m_amount": "3",
  "m_resources[2].m_extraAmountOnlyOneIngredient": "0",
  "m_resources[2].m_amountPerLevel": "2",
  "m_resources[2].m_recover": "true",
  "m_resources.<count>": "3"
}
```

**Armor recipe** (Values_Dump.json:408716):
```json
{
  "_Key": "ArmorBerserkerChest",
  "_Source": "Recipes",
  "m_item": "ArmorBerserkerChest",
  "m_amount": "1",
  "m_enabled": "true",
  "m_qualityResultAmountMultiplier": "1",
  "m_listSortWeight": "100",
  "m_craftingStation": "piece_workbench",
  "m_repairStation": "",
  "m_minStationLevel": "2",
  "m_requireOnlyOneIngredient": "false",
  "m_resources[0].m_resItem": "BjornHide",
  "m_resources[0].m_amount": "5",
  "m_resources[0].m_extraAmountOnlyOneIngredient": "0",
  "m_resources[0].m_amountPerLevel": "2",
  "m_resources[0].m_recover": "true",
  "m_resources[1].m_resItem": "BjornPaw",
  "m_resources[1].m_amount": "2",
  "m_resources[1].m_extraAmountOnlyOneIngredient": "0",
  "m_resources[1].m_amountPerLevel": "0",
  "m_resources[1].m_recover": "true",
  "m_resources[2].m_resItem": "Blueberries",
  "m_resources[2].m_amount": "4",
  "m_resources[2].m_extraAmountOnlyOneIngredient": "0",
  "m_resources[2].m_amountPerLevel": "1",
  "m_resources[2].m_recover": "true",
  "m_resources.<count>": "3"
}
```
Note `m_craftingStation` is dumped as the **prefab/GameObject name** of the station (`forge`, `piece_workbench`), not its `m_name` token.

**Build piece** — build pieces are NOT `Recipe` assets; cost lives on the `Piece` component of the prefab. Verbatim from prefab record `wood_wall_log` (Values_Dump.json:961523+):
```json
{
  "_Key": "wood_wall_log",
  "_Source": "Prefabs",
  "_components": "Transform|BoxCollider|Piece|ZNetView|WearNTear",
  "Piece.m_icon": "wood_logwall",
  "Piece.m_name": "$piece_logbeam2",
  "Piece.m_description": "",
  "Piece.m_enabled": "true",
  "Piece.m_category": "BuildingWorkbench",
  "Piece.m_comfort": "0",
  "Piece.m_groundPiece": "false",
  "Piece.m_allowedInDungeons": "false",
  "Piece.m_dlc": "",
  "Piece.m_craftingStation": "piece_workbench",
  "Piece.m_resources[0].m_resItem": "RoundLog",
  "Piece.m_resources[0].m_amount": "1",
  "Piece.m_resources[0].m_extraAmountOnlyOneIngredient": "0",
  "Piece.m_resources[0].m_amountPerLevel": "1",
  "Piece.m_resources[0].m_recover": "true",
  "Piece.m_resources.<count>": "1"
}
```
And a real station prefab, `piece_workbench` (Values_Dump.json:784873+, `_components: Transform|ZNetView|Piece|CraftingStation|WearNTear`):
`Piece.m_name "$piece_workbench"`, `Piece.m_category "Crafting"`, `Piece.m_craftingStation ""`, `Piece.m_resources[0] = Wood x10`;
`CraftingStation.m_name "$piece_workbench"`, `m_discoverRange "4"`, `m_rangeBuild "20"`, `m_extraRangePerLevel "4"`, `m_craftRequireRoof "true"`, `m_craftRequireFire "false"`, `m_showBasicRecipies "true"`, `m_useDistance "2"`, `m_useAnimation "1"`, `m_craftingSkill "Crafting"`.
(Disabled leftovers exist, e.g. `Recipe_Adze` with `m_item ""`, `m_enabled "false"` — Values_Dump.json:408693.)


## GOTCHAS
- Station matching is by STRING, not reference: Player.RequiredCraftingStation compares `requiredStation.m_name != m_currentStation.m_name` (:17809) and Piece build checks use CraftingStation.HaveBuildStationInRange(name, pos) (:56588, called at :17946 and :18026). A modded station with the same m_name as a vanilla one is fully interchangeable with it; a modded station with an empty/duplicate m_name will silently hijack or be hijacked.
- ObjectDB.CopyOtherDB(other) (:90520) REPLACES m_items/m_recipes/m_StatusEffects with the other DB's lists. Patching only ObjectDB.Awake means your recipes vanish when the world scene loads. Postfix BOTH Awake and CopyOtherDB, and guard against double-adding.
- ObjectDB.UpdateRegisters uses m_itemByHash.Add(item.name.GetStableHashCode(), item) (:90534) — a duplicate prefab name throws ArgumentException and aborts the whole registration loop, leaving ObjectDB half-built. Same hazard in ZNetScene.Awake with m_namedPrefabs.Add (:69609/:69613).
- MULTIPLAYER: a build piece prefab MUST be in ZNetScene.m_prefabs (:69584) on EVERY client. Prefabs are resolved by prefab.name.GetStableHashCode() (:69609); a joining client without the mod receives a ZDO with an unknown prefab hash and cannot instantiate the piece. Recipes are client-side (ObjectDB) but the resulting item ZDO/prefab is not.
- CraftingStation.m_allStations is `private static` (:56389) — only reachable because the assembly is publicized. It is populated in Start() (:56397) only when `!m_nview || m_nview.GetZDO() != null`, and Start() runs after Awake, so a station queried too early is invisible to HaveBuildStationInRange.
- PieceTable.m_availablePieces is hard-coded to 8 lists (`for (int i = 0; i < 8; i++)`, :60225) and UpdateAvailable indexes `m_availablePieces[(int)component.m_category]` (:60251). Any custom PieceCategory value >= 8 (other than All=100, which is special-cased at :60242) throws ArgumentOutOfRangeException every time the build menu refreshes. PieceCategory.Max = 8, All = 100 (:117928).
- Piece.Requirement.GetAmount(qualityLevel) returns `(qualityLevel-1)*m_amountPerLevel` for quality>1 — it does NOT include m_amount (:117969). Upgrade costs are therefore m_amountPerLevel-only.
- A new build piece will NOT appear until Player.UpdateKnownRecipesList() runs (private, :20239) AND the player has discovered every m_resItem material (m_knownMaterial, :17969) and the station name (m_knownStations, :17941). Adding to m_pieces mid-session shows nothing until that refresh; both UpdateKnownRecipesList and UpdateAvailablePiecesList (:20275) are private (publicizer/Harmony required).
- Inventory.GetAllPieceTables (:57270) only collects PieceTables from items IN THE PLAYER'S INVENTORY. Piece discovery for a custom table requires the player to actually carry the tool item.
- Recipe.GetAmount (:60541) dereferences Player.m_localPlayer with no null check when m_requireOnlyOneIngredient is true — never call it off the main gameplay path or on a dedicated server.
- Recipe.m_craftingStation == null means 'basic recipe': it is then only listed when no station is open or the open station has m_showBasicRecipies == true (:17822). m_repairStation only applies for quality > 1 (:60534).
- m_knownRecipes / m_knownStations are keyed by localization tokens (piece.m_name, station.m_name, item m_shared.m_name) and are SERIALIZED into the character save (:19642-19652). Changing a modded piece's m_name later orphans the player's unlock and re-locks it.

## NOT FOUND
- ObjectDB.AddRecipe / ObjectDB.AddItem — NOT FOUND (no such methods; mutate ObjectDB.instance.m_recipes / m_items directly)
- PieceTable.AddPiece / RemovePiece — NOT FOUND (no such methods; mutate PieceTable.m_pieces directly)
- CraftingStation.GetAllStations() or any public accessor for m_allStations — NOT FOUND (private static field only, :56389)
- Recipe field for station by name/hash (e.g. m_craftingStationName / m_craftingStationHash) — NOT FOUND; Recipe stores a CraftingStation object reference only (:60513). `m_craftingStationName` exists only as a TMP_Text UI field on InventoryGui (:41211).
- Recipe.m_dlc — NOT FOUND on Recipe; the DLC check reads m_item.m_itemData.m_shared.m_dlc (:17842). Piece DOES have m_dlc (:118078).
- Piece.Requirement as a struct — NOT FOUND; it is a [Serializable] CLASS (:117953-117954).
- Recipe.m_qualityResultAmountMultiplier being applied to normal (non-single-ingredient) crafts — NOT FOUND; it is only used inside the m_requireOnlyOneIngredient branch (:60549).
- Any per-recipe 'category'/'tab' field on Recipe — NOT FOUND; the craft list is ordered by m_listSortWeight (:60510, used at :42186).