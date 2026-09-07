## Source

All `L####` cites are lines in
`C:\WubarrkCODING\libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs` (called **DECOMP** below).
Value cites are `C:\WubarrkCODING\libs-Tools\WubarrksEye_Dumps\2026-07-31_21-06-43\Values_Dump.json`.

---

## 1. The three hash surfaces — every one is keyed off `GameObject.name`

| Register | Declaration | Built by | Key |
|---|---|---|---|
| `ZNetScene.m_namedPrefabs` | `private readonly Dictionary<int, GameObject> m_namedPrefabs = new Dictionary<int, GameObject>();` — DECOMP L69588 | `ZNetScene.Awake()` L69604–69618 | `prefab.name.GetStableHashCode()` L69609 |
| `ObjectDB.m_itemByHash` | `private Dictionary<int, GameObject> m_itemByHash` — L90508 | `ObjectDB.UpdateRegisters()` L90528–90541 | `item.name.GetStableHashCode()` L90534 |
| `ObjectDB.m_itemByData` | `private Dictionary<ItemDrop.ItemData.SharedData, GameObject> m_itemByData` — L90510 | same, L90538 | the **`SharedData` object reference** |

Source lists (both `public`, both `List<GameObject>`):
```csharp
public List<GameObject> m_prefabs = new List<GameObject>();            // ZNetScene, L69584
public List<GameObject> m_nonNetViewPrefabs = new List<GameObject>();  // ZNetScene, L69586
public List<GameObject> m_items    = new List<GameObject>();           // ObjectDB,  L90504
public List<Recipe>     m_recipes  = new List<Recipe>();               // ObjectDB,  L90506
public List<StatusEffect> m_StatusEffects = new List<StatusEffect>();  // ObjectDB,  L90502
```

**`ZNetScene.m_prefabs` is read exactly once, in `Awake()`.** Grep for `m_prefabs` across DECOMP returns only L69584 (decl) and L69607 (Awake foreach) for `ZNetScene` (the other hits at L95563/95679/95686/95692/95700/95729 are `SpawnSystem.m_prefabs`, unrelated). Everything at runtime resolves through `m_namedPrefabs` only:

```csharp
public bool HasPrefab(int hash)  { return m_namedPrefabs.ContainsKey(hash); }        // L69702
public GameObject GetPrefab(int hash){ if (m_namedPrefabs.TryGetValue(hash, out var value)) return value; return null; }  // L69707
public GameObject GetPrefab(string name){ return GetPrefab(name.GetStableHashCode()); } // L69716
public int GetPrefabHash(GameObject go){ return go.name.GetStableHashCode(); }         // L69721
public List<string> GetPrefabNames()                                                   // L70013
```
⇒ **Adding to `m_prefabs` after `Awake` does nothing.** You must write `m_namedPrefabs` directly (private + `readonly` — reachable only because you publicize; `readonly` blocks reassignment, not `.Add`/indexer).

`ObjectDB` API surface (complete — L90498–90623):
```csharp
private void Awake()                                    // L90514  -> m_instance = this; UpdateRegisters();
public  void CopyOtherDB(ObjectDB other)                // L90520  -> m_items = other.m_items; (BY REFERENCE) ... UpdateRegisters();
private void UpdateRegisters()                          // L90528
public  StatusEffect GetStatusEffect(int nameHash)      // L90543
public  GameObject   GetItemPrefab(string name)         // L90555
public  GameObject   GetItemPrefab(int hash)            // L90560
public  GameObject   GetItemPrefab(ItemDrop.ItemData.SharedData sharedData) // L90569
public  bool TryGetItemPrefab(string, out GameObject)   // L90578
public  bool TryGetItemPrefab(int, out GameObject)      // L90583
public  bool TryGetItemPrefab(SharedData, out GameObject)// L90588
public  int  GetPrefabHash(GameObject prefab)           // L90593
public  List<ItemDrop> GetAllItems(ItemDrop.ItemData.ItemType type, string startWith) // L90598
public  Recipe GetRecipe(ItemDrop.ItemData item)        // L90612
```

---

## 2. What must be TRUE of a cloned item GameObject

| Requirement | Why / cite |
|---|---|
| `.name` unique, no `(Clone)`, **no `(` and no space** | `ItemDrop.GetPrefabName(string)` truncates at the first `'('` or `' '`: L59236–59245. `ZNetView.Awake` names the ZDO via `Utils.GetPrefabName(gameObject)` L70414–70417. |
| `ZNetView` component present, `m_persistent = true`, `m_type = Default`, `m_distant = false` | Real vanilla values (`SwordIron`, `_Source: "Prefabs"`): `ZNetView.m_persistent: "true"`, `ZNetView.m_distant: "false"`, `ZNetView.m_type: "Default"`, `ZNetView.m_syncInitialScale: "false"` — Values_Dump.json L883979–883982 |
| `Rigidbody` present | `Humanoid.DropItem` does `itemDrop.GetComponent<Rigidbody>().linearVelocity = …` with **no null check** — DECOMP L13621 → NRE if stripped. Vanilla `SwordIron` components: `Transform\|Rigidbody\|ZNetView\|ZSyncTransform\|ItemDrop\|…` (Values_Dump L883977) |
| `ZSyncTransform` present | same component list; without it a dropped item does not move for remote clients |
| `ItemDrop` component present | `Inventory.AddItem(string,…)`: `if (component == null) { ZLog.Log("Missing itemdrop in " + name); … return false; }` L57797–57803 |
| `m_itemData.m_shared` is its **own** `SharedData` instance | `m_itemByData` is keyed by reference (L90510, L90538). Sharing the donor's instance silently overwrites the donor's mapping — breaks `Humanoid` eat-visual L13669 and `Player.AddTrophy` L20218. Unity `Instantiate` deep-copies `[Serializable] class ItemData`/`SharedData` (L57920, L58004), so a straight `Instantiate` is correct — **do not** hand-assign `m_shared = donor.m_shared`. |
| `m_shared.m_name` unique | Stacking, "known" sets and recipe lookup are all keyed on `m_shared.m_name`: `Inventory.AddItem(item,amount,x,y)` L56927; `FindFreeStackItem(item.m_shared.m_name,…)` L56991; `CanAddItem` L56971; `ObjectDB.GetRecipe` L90616; `Player.m_knownRecipes`/`m_knownMaterial` (`HashSet<string>`) L15578/L15582, L17870, L17969. |
| `m_shared.m_icons.Length > 0` | `public Sprite GetIcon() { return m_shared.m_icons[m_variant]; }` — L58539–58542, unguarded index |
| in `ObjectDB.m_items` **and** `m_itemByHash` rebuilt | otherwise `ItemDrop.Awake` L58993 gets `null`, `Inventory.AddItem` bails L57788–57793 |
| in `ZNetScene.m_namedPrefabs` **on every peer incl. dedicated server** | `ZNetScene.CreateObject` L69659–69684 → `GetPrefab(hash)`; null ⇒ object never spawns, and on the server the ZDO is **deleted** (L69798–69804) |
| `m_itemData.m_dropPrefab` — leave it `null` on the template | it is `[NonSerialized]` (L58312–58313) and is set at runtime by `ItemDrop.Awake` L58994 |

`ItemDrop` public fields you touch: `public bool m_autoPickup = true;` L58933, `public bool m_autoDestroy = true;` L58935, `public ItemData m_itemData = new ItemData();` L58937.

---

## 3. DROP path — exact trace

```
InventoryGui  L41788 / L41893 / L41899   Player.m_localPlayer.DropItem(inv, item, amount)
   ↓
Humanoid.DropItem(Inventory, ItemDrop.ItemData, int)          DECOMP L13566
   L13611  ItemDrop itemDrop = ItemDrop.DropItem(item, amount, pos, rot);
   L13614  itemDrop.OnPlayerDrop();                            (L59353: m_autoPickup = false)
   L13621  itemDrop.GetComponent<Rigidbody>().linearVelocity = …   << NRE if no Rigidbody
   ↓
public static ItemDrop DropItem(ItemData item, int amount, Vector3 position, Quaternion rotation)  L59558
   L59560  UnityEngine.Object.Instantiate(item.m_dropPrefab, position, rotation).GetComponent<ItemDrop>()
   L59561  component.m_itemData = item.Clone();
   L59574  component.Save();                                   (L59456 → SaveToZDO L59485)
```

**The dropping client does NOT look anything up by hash.** It instantiates `m_dropPrefab` directly. The hash lookup happens on *everyone else*:

```csharp
// ZNetView.Awake, L70278-70296  (fresh object, no init ZDO)
string prefabName = GetPrefabName();                                   // L70280
m_zdo = ZDOMan.instance.CreateNewZDO(base.transform.position, prefabName.GetStableHashCode()); // L70281
m_zdo.SetPrefab(prefabName.GetStableHashCode());                        // L70285
```
```csharp
// ZNetScene.CreateObject, L69659 — runs on every OTHER peer + the server
int prefab = zdo.GetPrefab();            // L69661
GameObject prefab2 = GetPrefab(prefab);  // L69666  -> m_namedPrefabs
if (prefab2 == null) return null;        // L69667-69670
```
```csharp
// ZNetScene.CreateObjectsSorted, L69790-69804  — the classic "item vanishes" line
else if (ZNet.instance.IsServer())
{
    item.SetOwner(ZDOMan.GetSessionID());
    ZLog.Log("Destroyed invalid predab ZDO:" + uid.ToString());
    ZDOMan.instance.DestroyZDO(item);          // L69803  <<<<<< PERMANENT DELETION
}
```
Same in `CreateDistantObjects` L69837–69843 (`"Destroyed invalid predab ZDO:… prefab hash:"`).

**Conclusion:** a modded item dropped on a server that lacks the prefab in `m_namedPrefabs` is destroyed by the server, permanently. `ItemDrop.m_itemData.m_dropPrefab` being correct is necessary but **not** sufficient — the clone must be in `ZNetScene.m_namedPrefabs` on the server and on every client.

Other hash-driven spawn paths (same dictionary):
- `ZNetScene.SpawnObject(Vector3, Quaternion, GameObject)` L70007 → RPC `"SpawnObject"`
- `ZNetScene.RPC_SpawnObject(long, Vector3, Quaternion, int)` L70023 → `if (prefab == null) ZLog.Log("Missing prefab " + prefabHash);` L70026–70029
- `CharacterDrop`/Ragdoll loot: `ZNetScene.instance.GetPrefab(hash)` → `ZLog.LogWarning("Ragdoll: Missing prefab:" + hash + " when dropping loot");` L23028–23031

---

## 4. `ItemDrop.ItemData.m_dropPrefab` — writers and readers

**Declaration** (`[NonSerialized]` ⇒ **not** copied by `Object.Instantiate`, **not** written to ZDO, **not** in the ZPackage as an object):
```csharp
[NonSerialized]
public GameObject m_dropPrefab;      // DECOMP L58312-58313
```

**Writers**
| Site | Code |
|---|---|
| `ItemDrop.Awake` L58992–58994 (**the main one**) | `string prefabName = GetPrefabName(base.gameObject.name); GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(prefabName); m_itemData.m_dropPrefab = itemPrefab;` |
| `Inventory.AddItem(GameObject prefab, int amount)` L56974–56977 | `itemData.m_dropPrefab = prefab;` |
| `DropTable` drop-list build L56801–56803 | `itemData2.m_dropPrefab = data.m_item;` |
| `ArmorStand` repair L101124–101130 | `if (component2.m_itemData.m_dropPrefab == null) component2.m_itemData.m_dropPrefab = itemPrefab.gameObject;` |

Note L58995–58998: `if (Application.isEditor) { m_itemData.m_shared = itemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared; }` — **editor only**, so in a shipped build a missing ObjectDB entry does not throw here; it just leaves `m_dropPrefab == null` and fails later, silently.

**Readers (all `.name`-based)**
| Site | Consequence if null / wrong |
|---|---|
| `Inventory.Save(ZPackage)` L57612–57620 | `if (item.m_dropPrefab == null) { ZLog.Log("Item missing prefab " + item.m_shared.m_name); pkg.Write(""); }` → item written as empty string → **dropped on load** (L57666 / L57719 `if (text != "")`) |
| `ItemDrop.DropItem` L59560 | `Instantiate(null)` → exception, or spawns the **donor** prefab |
| `Humanoid.SetupVisEquipment` L14154–14167 | wrong/`NullReferenceException` visual on all clients (`m_leftItem.m_dropPrefab.name`, etc.) |
| `Player.EatFood` L17509 / L17522 | food slot keyed by `item.m_dropPrefab.name`, saved/loaded via `ObjectDB.GetItemPrefab(food.m_name)` L19868 → `"Failed to find food item"` L19871 |
| `Player.AddTrophy` L20212–20224 | falls back to `GetItemPrefab(item.m_shared)`; on miss: `ZLog.LogError("Trying to add known trophy that is missing itemprefab in the database: "…)` |
| `Inventory.GetItem(name, …, isPrefabName)` L57342, `GetAmmoItem` L57356 | NRE on null |
| `ArmorStand` L101045/L101049, `Turret` L102297/L102320/L102405, `CookingStation.CookItem` L104635, `Smelter` L107777, `Fermenter`/`FishingFloat` L109662, `InventoryGui` upgrade compare L42284 | all `.name` → NRE on null |

**If it points at the donor instead of the clone:** every one of the above writes/reads the *donor's* name ⇒ the item saves as the donor, drops as the donor, and the clone silently converts to the donor on the next inventory load.

---

## 5. Serialization / restore — **by NAME string**, never by hash

### Player + Container inventory (ZPackage, version 106)
```csharp
public void Save(ZPackage pkg)                   // DECOMP L57606
{
    pkg.Write(106);
    pkg.Write(m_inventory.Count);
    foreach (ItemDrop.ItemData item in m_inventory)
    {
        if (item.m_dropPrefab == null) { ZLog.Log("Item missing prefab " + item.m_shared.m_name); pkg.Write(""); }
        else                            { pkg.Write(item.m_dropPrefab.name); }   // L57619  << NAME
        pkg.Write(item.m_stack); pkg.Write(item.m_durability); pkg.Write(item.m_gridPos);
        pkg.Write(item.m_equipped); pkg.Write(item.m_quality); pkg.Write(item.m_variant);
        pkg.Write(item.m_crafterID); pkg.Write(item.m_crafterName);
        pkg.Write(item.m_customData.Count); /* k/v pairs */
        pkg.Write(item.m_worldLevel); pkg.Write(item.m_pickedUp);
    }
}

public void Load(ZPackage pkg)                   // L57640
{ … if (text != "") AddItem(text, stack, durability, pos, equipped, quality, variant, crafterID, crafterName, dictionary, worldLevel, pickedUp); }  // L57666-57669
```
```csharp
private bool AddItem(string name, int stack, float durability, Vector2i pos, bool equipped, int quality,
                     int variant, long crafterID, string crafterName,
                     Dictionary<string,string> customData, int worldLevel, bool pickedUp)   // L57786
{
    GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(name);
    if (itemPrefab == null) { ZLog.Log("Failed to find item prefab " + name); return false; }   // L57788-57793
    ZNetView.m_forceDisableInit = true;
    GameObject gameObject = UnityEngine.Object.Instantiate(itemPrefab);   // L57795  (ItemDrop.Awake runs here → sets m_dropPrefab)
    ZNetView.m_forceDisableInit = false;
    …
    AddItem(component.m_itemData, component.m_itemData.m_stack, pos.x, pos.y);   // L57814
    UnityEngine.Object.Destroy(gameObject);                                       // L57815
}
```
Public overload used by crafting/console: `public ItemDrop.ItemData AddItem(string name, int stack, int quality, int variant, long crafterID, string crafterName, Vector2i position, bool pickedUp = false)` L57733, same `GetItemPrefab(name)` guard at L57735–57739.

### Chest / container contents = **base64 ZPackage inside a ZDO string**
```csharp
private void Save()    // Container, DECOMP L103973
{ ZPackage zPackage = new ZPackage(); m_inventory.Save(zPackage);
  string @base = zPackage.GetBase64(); m_nview.GetZDO().Set(ZDOVars.s_items, @base); }   // L103978
private bool Load()    // L103983
{ string text = m_nview.GetZDO().GetString(ZDOVars.s_items); … m_inventory.Load(pkg); }  // L103990, L104003
```
(`Container` class L103544; `ZDOVars.s_items = "items".GetStableHashCode()` L66536.)

### The world-item ZDO stores **no name at all**
`ItemDrop.SaveToZDO(ItemData, ZDO)` L59485 and `LoadFromZDO(ItemData, ZDO)` L59504 store only `s_durability, s_stack, s_quality, s_variant, s_crafterID, s_crafterName, s_dataCount, data_N/data__N, s_worldLevel, s_pickedUp`. Identity comes solely from `ZDO.SetPrefab(hash)` (`ZDO.SetPrefab(int)` L62611, `GetPrefab()` L62620, `private int m_prefab` L62151). Indexed variants for multi-slot holders: `SaveToZDO(int index, …)` L59522, `LoadFromZDO(int index, …)` L59541. `ItemDrop.Save()` L59456, `Load()` L59464, `LoadFromExternalZDO(ZDO)` L59478.

### Failure modes
| Scenario | Result |
|---|---|
| **Mod removed, player inventory** | `AddItem` L57788 logs `"Failed to find item prefab <name>"`, returns false. Item is **silently gone**. On the next `Save()` it is not re-written ⇒ permanent loss. |
| **Mod removed / vanilla client touches a CHEST** | `Container.Load` drops the item; the next `Container.Save` on that peer writes the truncated package into the shared ZDO ⇒ **the item is destroyed for everyone, including modded clients.** This is the single worst multiplayer data-loss path. |
| **Mod-less dedicated server, item dropped on ground** | `ZNetScene.CreateObjectsSorted` L69798–69804 destroys the ZDO. Item gone. |
| **Name collides with a vanilla prefab, added pre-`Awake`** | `ZNetScene.Awake` L69609 `m_namedPrefabs.Add(...)` → `ArgumentException: An item with the same key has already been added`. `Awake` aborts before `ZDOMan` hookup (L69615–69617) ⇒ **game is bricked.** Identically `ObjectDB.UpdateRegisters` L90534 `m_itemByHash.Add(...)` throws. |
| **Name collides, added post-`Awake` via dictionary indexer** | your prefab silently replaces the vanilla one everywhere; the vanilla item becomes unspawnable. |
| **`m_shared.m_name` collides with vanilla** | clone stacks into the vanilla item's stack (`Inventory.AddItem` L56927 / `FindFreeStackItem` L56991) and the merged stack keeps whichever `m_dropPrefab` won ⇒ item mutates into the other one on drop/save. |

---

## 6. Does `Object.Instantiate(donor)` at `ZNetScene.Awake` time produce an ACTIVE object? **YES — and it creates a live ZDO.**

`ZNetScene.m_prefabs` entries are *project assets*, never scene objects, so their `Awake` never runs. A runtime `Instantiate` produces an enabled scene object, so:

1. `ZNetView.Awake` (L70233) runs with `m_forceDisableInit == false` and `ZDOMan.instance != null`, falls to L70278 and calls
   `ZDOMan.instance.CreateNewZDO(pos, hash)` L70281 + `ZNetScene.instance.AddInstance(m_zdo, this)` L70298 — **a real networked entity now exists at world origin and replicates to every peer.**
2. `ItemDrop.Awake` (L58984) runs: registers into `static List<ItemDrop> s_instances` (L58929, L58991) — leaks; hits `ObjectDB.instance.GetItemPrefab(...)` L58993 (NRE if `ObjectDB.instance` is null at that moment); `InvokeRepeating("SlowUpdate", …)` L59030.
3. `ItemDrop.Start` L59042 calls `Save()` L59044 → writes the template's stats into the live ZDO.

**Neither vanilla escape hatch is usable for a template:**
```csharp
// ZNetView.Awake, L70233-70239
if (m_forceDisableInit || ZDOMan.instance == null) { UnityEngine.Object.Destroy(this); return; }
```
`ZNetView.m_forceDisableInit` (`public static bool`, L70217) **destroys the ZNetView component** — used by `Inventory.AddItem` L57794–57796 / L57755–57757 precisely because that instance is throwaway. A template that loses its `ZNetView` can never be dropped or spawned.
`ZNetView.StartGhostInit()` / `FinishGhostInit()` (L70531 / L70536, `private static bool m_ghostInit` L70229) still executes `CreateNewZDO` at L70281 before bailing at L70291–70295 — also creates a ZDO. Used by `ZoneSystem.PlaceZoneCtrl` L98835 and `PlaceVegetation` L99013.

**Correct inert-template pattern (no vanilla helper exists — NOT FOUND):** parent the clone under a permanently *inactive* container so no `Awake` on the clone ever fires.
```csharp
// once, in Awake of your plugin
var holder = new GameObject("YourMod_PrefabContainer");
holder.SetActive(false);                    // MUST be before anything is parented
UnityEngine.Object.DontDestroyOnLoad(holder);

// per item
GameObject clone = UnityEngine.Object.Instantiate(donor, holder.transform, worldPositionStays: false);
clone.name = "YourItemName";                // strips "(Clone)" — REQUIRED, this is the identity
```
`Instantiate` deep-copies the serialized `[Serializable] ItemData` (L57920) / `SharedData` (L58004), so the clone gets its own `m_itemData` and `m_shared` while `Sprite[] m_icons`, `StatusEffect`, `GameObject m_spawnOnHit`, `Attack` etc. stay as shared references — exactly what you want. `[NonSerialized]` members (`m_dropPrefab` L58313, `m_crafterID` L58298, `m_customData` L58304) come back at defaults, which is correct for a template.

---

## 7. Vanilla validation that could reject / punish a late-added prefab

| Check | Line | Trigger | Effect |
|---|---|---|---|
| `m_namedPrefabs.Add(hash, prefab)` | L69609 / L69613 | duplicate `name.GetStableHashCode()` at `ZNetScene.Awake` | `ArgumentException`, `Awake` aborts (no `ZDOMan` hook, no `"SpawnObject"` RPC) — fatal |
| `m_itemByHash.Add(hash, item)` | L90534 | duplicate name in `ObjectDB.m_items` | `ArgumentException` inside `UpdateRegisters` — ObjectDB half-built |
| `m_itemByData[shared] = item` | L90538 | **indexer**, does not throw — silently overwrites if two entries share a `SharedData` instance | `GetItemPrefab(SharedData)` returns the wrong prefab |
| `ObjectDB.GetAllItems` | L90603–90604 | `item.GetComponent<ItemDrop>()` used **unguarded** | NRE if you add a GameObject without `ItemDrop` to `m_items` |
| `ZNetScene.CreateObject` → `GetPrefab(hash)==null` | L69666–69670 | prefab absent on this peer | object never instantiated |
| `ZNetScene.CreateObjectsSorted` server branch | L69798–69804 | above, on the server | `ZLog.Log("Destroyed invalid predab ZDO:"…)` + **`ZDOMan.instance.DestroyZDO`** |
| `ZNetScene.CreateDistantObjects` server branch | L69837–69843 | same, distant objects | `"Destroyed invalid predab ZDO:… prefab hash:"` + DestroyZDO |
| `ZNetScene.IsPrefabZDOValid` | L69649–69657 | used by `IsAreaReady` L69726 | area never reports ready if instance can't be made |
| `ZDOMan.FilterZDO` | L64927–64939 | `if (!ZNetScene.instance.HasPrefab(zdo.GetPrefab())) warningZDOs.Add(zdo); zdos.Add(zdo);` | **kept**, only warned |
| `ZDOMan.WarnAndRemoveBrokenZDOs` | L64941–64998 | `ZLog.LogWarning("Found N ZDOs with unknown prefabs. Will load anyway.")` L64948 + `"    Hash H appeared C times."` L64964 | **non-destructive** — world load tolerates unknown prefabs |
| `s_brokenPrefabsToFilterOut` | L64929 | hard-coded hash blacklist | `"Found N ZDOs with prefabs not supported. Removing."` L64974 — irrelevant to modded hashes |
| `ZNetScene.RPC_SpawnObject` | L70023–70029 | `ZLog.Log("Missing prefab " + prefabHash);` | no spawn |
| `ZNet.RPC_PeerInfo` handshake | L67647, L67667–67687 | reads uid, version string, `uint networkVersion`; `if (num != 36) → Error 3 / ErrorVersion` | **only** game-version + network-version 36. **There is NO prefab-list or ObjectDB exchange/validation on connect.** |

Net: the *world save* tolerates unknown prefabs; the *live scene on the server* does not — it deletes their ZDOs. So the prefab must exist in `m_namedPrefabs` on the dedicated server, not merely in the save file.

---

## 8. Recipe registration — **no register rebuild needed**

Every consumer of `ObjectDB.m_recipes` does a linear `foreach`; there is no dictionary, no `UpdateRegisters` involvement:
```csharp
public Recipe GetRecipe(ItemDrop.ItemData item)          // L90612
{ foreach (Recipe recipe in m_recipes)
    if (!(recipe.m_item == null) && recipe.m_item.m_itemData.m_shared.m_name == item.m_shared.m_name) return recipe;   // L90616
  return null; }
```
```csharp
Player.UpdateKnownRecipesList()  L20239-20249:  foreach (Recipe recipe in ObjectDB.instance.m_recipes)   // L20245
Player.GetAvailableRecipes(ref List<Recipe>) L20455-20462: foreach (Recipe recipe in ObjectDB.instance.m_recipes) // L20458
```
⇒ **`ObjectDB.instance.m_recipes.Add(myRecipe)` is sufficient at any time after `Awake`.** `UpdateRegisters()` (L90528) does not touch `m_recipes` at all.

`Recipe` (DECOMP L60499, a `ScriptableObject` — create with `ScriptableObject.CreateInstance<Recipe>()` and set `.name`):
```csharp
public ItemDrop m_item;                                   // L60501
public int      m_amount = 1;                             // L60503
public bool     m_enabled = true;                         // L60505
public float    m_qualityResultAmountMultiplier = 1f;     // L60508
public int      m_listSortWeight = 100;                   // L60510
public CraftingStation m_craftingStation;                 // L60513
public CraftingStation m_repairStation;                   // L60515
public int      m_minStationLevel = 1;                    // L60517
public bool     m_requireOnlyOneIngredient;               // L60519
public Piece.Requirement[] m_resources = new Piece.Requirement[0];  // L60521
public int GetRequiredStationLevel(int quality)           // L60523
public CraftingStation GetRequiredStation(int quality)    // L60528
public int GetAmount(int quality, out int need, out ItemDrop.ItemData singleReqItem, int craftMultiplier = 1) // L60541
```
`Piece.Requirement` (DECOMP L117954):
```csharp
public ItemDrop m_resItem;                  // L117957
public int  m_amount = 1;                   // L117959
public int  m_extraAmountOnlyOneIngredient; // L117961
public int  m_amountPerLevel = 1;           // L117964
public bool m_recover = true;               // L117967
public int GetAmount(int qualityLevel)      // L117969
```

**Crafting resolves the produced item by NAME, not by the `m_item` reference:**
```csharp
// InventoryGui.DoCrafting(Player), DECOMP L42606
player.GetInventory().AddItem(m_craftRecipe.m_item.gameObject.name, num3, num, variant, playerID, playerName, position)   // L42659
```
⇒ `Recipe.m_item` **must** be `clone.GetComponent<ItemDrop>()`. If it is the donor's `ItemDrop`, the recipe crafts the donor item. Also `HaveRequirements` resolves ingredients through `ObjectDB.instance.GetItemPrefab(Utils.GetPrefabName(requirement.m_resItem.name))` (L118208), so every `m_resItem` must itself be an ObjectDB-registered `ItemDrop`.

`Recipe` discovery is keyed on `recipe.m_item.m_itemData.m_shared.m_name` into `Player.m_knownRecipes` (`private readonly HashSet<string>` L15578, saved at L19642–19646, loaded at L19752–19756) — another reason `m_shared.m_name` must be unique.

---

## 9. REQUIRED ORDER OF OPERATIONS

**Phase A — plugin `Awake` (once)**
1. Create the inert container: `var holder = new GameObject("YourMod_PrefabContainer"); holder.SetActive(false); Object.DontDestroyOnLoad(holder);` — inactive **before** anything is parented, so no cloned `Awake` ever runs (§6).
2. Apply Harmony patches: **postfix** `ObjectDB.Awake` (L90514), **postfix** `ObjectDB.CopyOtherDB` (L90520), **postfix** `ZNetScene.Awake` (L69604). Do **not** prefix — you need the vanilla registers already built so your `.Add` cannot corrupt the vanilla foreach.

**Phase B — inside the `ObjectDB` postfix (runs twice: FejdStartup L83621–83626, then the main-scene instance)**
3. Sanity-gate: `if (ObjectDB.instance == null || ObjectDB.instance.m_items.Count == 0 || ObjectDB.instance.GetItemPrefab("Wood") == null) return;` — the FejdStartup `ObjectDB.Awake` fires with an empty list *before* `CopyOtherDB` populates it (L90514 then L90520). Note `CopyOtherDB` assigns `m_items = other.m_items` **by reference** (L90522), so the FejdStartup DB and the `m_objectDBPrefab` DB share one `List<GameObject>`.
4. Idempotency gate: `if (ObjectDB.instance.m_items.Contains(myClone)) { /* still fall through to step 9 */ }` and never `Add` twice — `m_itemByHash.Add` (L90534) throws on the duplicate hash.
5. Resolve the donor: `GameObject donor = ObjectDB.instance.GetItemPrefab("SwordIron");` (L90555). Abort if null.
6. **Collision check before creating anything:** `string name = "YourItemName"; int h = name.GetStableHashCode();`
   - `ObjectDB.instance.GetItemPrefab(h) == null` (L90560)
   - `ZNetScene.instance == null || !ZNetScene.instance.HasPrefab(h)` (L69702)
   - name contains no `'('`, no `' '` (L59236)
   If either register already owns the hash → abort and log; do **not** overwrite (§7).
7. Clone once and cache: `clone = Object.Instantiate(donor, holder.transform, false); clone.name = name;`
8. Mutate `clone.GetComponent<ItemDrop>().m_itemData.m_shared`: set a **unique** `m_shared.m_name` (localization token), `m_description`, stats. Leave `m_shared.m_icons` non-empty (L58541). Leave `m_itemData.m_dropPrefab` null (L58313; `ItemDrop.Awake` L58994 fills it). Never assign `m_shared = donor…m_shared` (§2).
9. Register in ObjectDB, then rebuild:
   ```csharp
   if (!ObjectDB.instance.m_items.Contains(clone)) ObjectDB.instance.m_items.Add(clone);   // L90504
   // ObjectDB.UpdateRegisters() is PRIVATE (L90528) — reachable only via publicized assembly:
   ObjectDB.instance.UpdateRegisters();   // rebuilds m_itemByHash (L90530-90540)
   ```
   *(Publicizer dependency. If you do not publicize, use `AccessTools.Method(typeof(ObjectDB), "UpdateRegisters").Invoke(ObjectDB.instance, null)`, or hand-insert into `m_itemByHash`/`m_itemByData` via `AccessTools.FieldRefAccess` — the dictionaries themselves are private too.)*
10. Register recipes (order-independent, no rebuild — §8):
    ```csharp
    var r = ScriptableObject.CreateInstance<Recipe>(); r.name = "Recipe_YourItemName";
    r.m_item = clone.GetComponent<ItemDrop>();      // MUST be the clone (L42659)
    r.m_enabled = true; r.m_amount = 1; r.m_minStationLevel = 1;
    r.m_craftingStation = ObjectDB.instance.GetItemPrefab("Hammer")… /* resolve a real CraftingStation */;
    r.m_resources = new Piece.Requirement[] { new Piece.Requirement { m_resItem = woodDrop, m_amount = 10 } };
    if (!ObjectDB.instance.m_recipes.Contains(r)) ObjectDB.instance.m_recipes.Add(r);
    ```
11. If `ZNetScene.instance != null` at this point (ObjectDB may awake after ZNetScene), immediately run step 13.

**Phase C — inside the `ZNetScene.Awake` postfix**
12. Guard `ZNetScene.instance != null` and `clone != null` (if ObjectDB has not run yet, do steps 5–10 here first; the two `Awake`s have **no guaranteed order** — both live in the "main" scene and nothing in the decompile sequences them).
13. Register into the runtime dictionary — **`m_prefabs` alone is useless post-Awake** (§1):
    ```csharp
    int h = clone.name.GetStableHashCode();
    if (!ZNetScene.instance.m_prefabs.Contains(clone)) ZNetScene.instance.m_prefabs.Add(clone);   // L69584, cosmetic/for other mods
    if (!ZNetScene.instance.m_namedPrefabs.ContainsKey(h))
        ZNetScene.instance.m_namedPrefabs.Add(h, clone);      // L69588 — private readonly; publicizer needed
    ```
    Use `ContainsKey` + `Add` (or the indexer) — never a bare `.Add` on a hash you have not checked.

**Phase D — multiplayer correctness (non-negotiable)**
14. Ship and load the **same** plugin on the dedicated server. Without it, `ZNetScene.CreateObjectsSorted` L69798–69804 deletes every dropped modded item's ZDO.
15. Enforce a client/server lockstep with ServerSync (or a version RPC) so a vanilla or stale client cannot open a chest containing modded items — `Container.Load`/`Save` (L103983/L103973) round-trips the whole inventory through `Inventory.Save` L57606 and will erase unknown entries for **all** players.
16. Keep `clone.name` byte-stable across releases forever. It is the ZDO prefab hash (L70281/L70285), the ObjectDB key (L90534), and the inventory save string (L57619). Renaming it orphans every existing item.

---

## 10. Notable NOT-FOUND / negative results
- `ZNetScene.AddPrefab(...)` — **NOT FOUND**. Full member list of `ZNetScene` L69576–70035 contains no add/register method.
- `ObjectDB.AddItem` / `RegisterItem` / `AddItemPrefab` — **NOT FOUND** (grep across DECOMP returns nothing).
- Any prefab-manifest / ObjectDB exchange or validation during connect — **NOT FOUND**. `ZNet.RPC_PeerInfo` L67647 validates only the version string and `networkVersion == 36` (L67676–67687).
- Any hash-collision guard other than the raw `Dictionary.Add` throws at L69609 and L90534 — **NOT FOUND**.
- `ItemDrop.NameHash()` (L59584) returns `m_nameHash` built from `base.name` in `Awake` (L58986–58989), i.e. it includes `"(Clone)"` on world instances. It has **zero consumers** in `assembly_valheim` — ignore it.


## GOTCHAS
- ZNetScene.m_prefabs (List<GameObject>, public, L69584) is read ONLY inside ZNetScene.Awake (L69607). Adding to it after Awake registers NOTHING. All runtime lookups go through the private readonly Dictionary<int,GameObject> m_namedPrefabs (L69588) via GetPrefab(int) L69707 / HasPrefab L69702. You MUST insert into m_namedPrefabs directly — requires the publicized assembly (or AccessTools field ref).
- ZNetScene.Awake uses m_namedPrefabs.Add(hash, prefab) (L69609). A duplicate name hash throws ArgumentException and aborts Awake BEFORE the ZDOMan.m_onZDODestroyed hookup (L69616) and the 'SpawnObject' RPC registration (L69617) — the game is bricked. Never inject a colliding name into m_prefabs pre-Awake; always check HasPrefab(hash) first.
- ObjectDB.UpdateRegisters uses m_itemByHash.Add(item.name.GetStableHashCode(), item) (L90534) — same ArgumentException on any duplicate name. UpdateRegisters is PRIVATE (L90528); calling it relies on the publicizer. It runs from Awake (L90517) AND CopyOtherDB (L90525), so a non-idempotent Add to m_items will crash the DB on the second pass.
- Object.Instantiate(donor) at runtime produces an ACTIVE scene object: ZNetView.Awake (L70233) reaches L70281 ZDOMan.instance.CreateNewZDO(...) + L70298 ZNetScene.instance.AddInstance(...) — a live replicated entity spawns at world origin. ItemDrop.Awake (L58984) also leaks it into static List<ItemDrop> s_instances (L58929/58991) and ItemDrop.Start (L59042) calls Save(). Parent the clone under a GameObject that was SetActive(false) BEFORE parenting.
- ZNetView.m_forceDisableInit = true (public static, L70217) is NOT a template trick: ZNetView.Awake L70235-70239 calls Object.Destroy(this) — it DESTROYS the ZNetView component. Vanilla only uses it for throwaway instances in Inventory.AddItem (L57755/L57794). A template that loses its ZNetView can never be dropped. ZNetView.StartGhostInit()/FinishGhostInit() (L70531/L70536) still calls CreateNewZDO at L70281 before bailing at L70291 — also unusable.
- MULTIPLAYER KILLER: ZNetScene.CreateObjectsSorted L69798-69804 and CreateDistantObjects L69837-69843 — when GetPrefab(zdo.GetPrefab()) returns null AND ZNet.instance.IsServer(), the server calls ZDOMan.instance.DestroyZDO(zdo) and logs 'Destroyed invalid predab ZDO:'. A dedicated server without the mod PERMANENTLY DELETES every dropped modded item.
- MULTIPLAYER DATA LOSS: chest contents live in a single ZDO string (Container.Save L103978 -> ZDOVars.s_items; Load L103990 -> Inventory.Load L57640). Inventory.Load drops any entry whose name misses ObjectDB (L57788-57793, 'Failed to find item prefab X'). A vanilla/stale client that opens the chest and then triggers Container.Save writes the truncated package back to the shared ZDO — erasing the modded items for EVERY player, modded ones included.
- Humanoid.DropItem L13621 does itemDrop.GetComponent<Rigidbody>().linearVelocity = ... with no null check. Strip the Rigidbody from the clone and every drop is a NullReferenceException. Vanilla item prefabs carry Transform|Rigidbody|ZNetView|ZSyncTransform|ItemDrop (Values_Dump.json L883977).
- Item identity is the GameObject.name string everywhere. ItemDrop's private GetPrefabName(string) truncates at the first '(' or ' ' (L59236-59245). A clone name containing a space or parenthesis is truncated by ItemDrop.Awake (L58992) but hashed IN FULL by ZNetScene.Awake (L69609) and ObjectDB.UpdateRegisters (L90534) — the two disagree and m_dropPrefab silently ends up null. Use a single bare token.
- m_dropPrefab is [NonSerialized] (L58312-58313): it is NOT copied by Object.Instantiate and NOT stored in the ZDO. It is set exclusively at runtime by ItemDrop.Awake L58994 = ObjectDB.instance.GetItemPrefab(GetPrefabName(gameObject.name)). If ObjectDB lacks the entry it stays null and Inventory.Save L57612-57616 writes an empty string ('Item missing prefab X'), which Inventory.Load skips (L57666/L57719) — the item is silently and permanently destroyed on the next save/load.
- The 'this fixes itself' line at L58995-58998 (m_itemData.m_shared = itemPrefab.GetComponent<ItemDrop>().m_itemData.m_shared) is inside if (Application.isEditor). In a shipped build it never runs, so a missing ObjectDB entry produces NO exception at Awake — the failure only surfaces later as a vanished item.
- Reusing the donor's m_shared.m_name makes the clone stack with the donor: Inventory.AddItem(item,amount,x,y) compares itemAt.m_shared.m_name (L56927) and FindFreeStackItem keys on m_shared.m_name (L56991), as do CanAddItem L56971, ObjectDB.GetRecipe L90616, Player.m_knownRecipes/m_knownMaterial (L15578/L15582, L17870, L17969). The merged stack keeps one m_dropPrefab, so the item mutates into the other prefab on drop or save.
- ObjectDB.m_itemByData (L90510) is keyed by the SharedData OBJECT REFERENCE and populated with the indexer (L90538, not Add) — no exception, silent overwrite. If the clone shares the donor's SharedData instance, GetItemPrefab(SharedData) returns the wrong prefab, breaking Humanoid eat-visuals (L13669) and Player.AddTrophy (L20218). Let Unity's Instantiate deep-copy [Serializable] ItemData/SharedData (L57920/L58004); never hand-assign m_shared.
- Recipe.m_item MUST be the CLONE's ItemDrop. InventoryGui.DoCrafting crafts via player.GetInventory().AddItem(m_craftRecipe.m_item.gameObject.name, ...) (L42659) — a name lookup, not the reference. Point it at the donor and the recipe produces the donor item.
- ObjectDB.GetAllItems (L90598) does item.GetComponent<ItemDrop>().m_itemData... with NO null check (L90603-90604). Adding any GameObject lacking an ItemDrop to m_items causes a NullReferenceException there.
- ItemData.GetIcon() is 'return m_shared.m_icons[m_variant];' (L58539-58542) — unguarded index. Empty m_icons or a variant out of range throws in the inventory GUI.
- ObjectDB.Awake and ZNetScene.Awake have NO guaranteed ordering (nothing in the decompile sequences them). Each postfix must handle 'the other one has not run yet'. Also FejdStartup.SetupObjectDB (L83621-83626) makes ObjectDB.Awake fire with an EMPTY m_items before CopyOtherDB (L90520) populates it — gate on GetItemPrefab("Wood") != null, and patch CopyOtherDB too. Note CopyOtherDB assigns m_items = other.m_items BY REFERENCE (L90522).
- There is NO prefab or ObjectDB validation during connect. ZNet.RPC_PeerInfo (L67647) checks only the version string and networkVersion == 36 (L67676-67687). A vanilla client will connect happily and then silently destroy your items — you must enforce the lockstep yourself (ServerSync).

## NOT FOUND
- ZNetScene.AddPrefab / RegisterPrefab — no such member. Full ZNetScene body read at DECOMP L69576-70035; the only mutation points are the public List<GameObject> m_prefabs (L69584, consumed only in Awake) and the private readonly Dictionary m_namedPrefabs (L69588).
- ObjectDB.AddItem / RegisterItem / AddItemPrefab / AddRecipe — grep for 'AddPrefab|RegisterItem|AddItemPrefab' over the whole decompile returns zero hits. Full ObjectDB body L90498-90623 has no add API; you mutate m_items/m_recipes directly.
- Utils.GetPrefabName(GameObject) and Utils.GetPrefabName(string) — used at ZNetView.Awake L70416 and 8 other sites, but DECLARED IN assembly_utils.dll, which is not present in this workspace as source (only the binary at C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed\assembly_utils.dll; C:\WubarrkCODING\libs-Tools\decompiled_core contains only UnityEngine.CoreModule). Its exact truncation rule is UNVERIFIED. The only truncator visible in assembly_valheim is ItemDrop's private GetPrefabName(string) at L59236-59245, which cuts at the first '(' or ' '. Treat any space or parenthesis in a prefab name as unsafe.
- string.GetStableHashCode() — the extension method used at L69609, L69718, L69723, L70281, L70285, L90534, L90557, L90595. Also lives in assembly_utils.dll; NOT declared anywhere in assembly_valheim.decompiled.cs. Reference it from assembly_utils, do not reimplement (a mismatched implementation silently desynchronizes every hash).
- Any vanilla helper for building an inert runtime prefab template — no 'prefab container', 'DontDestroyOnLoad holder', or disabled-parent pattern exists in assembly_valheim. Vanilla prefab references are project assets whose Awake never runs, so the engine never needed one. The inactive-parent pattern in section 6 is original work.
- Any ZNetScene/ObjectDB consistency or hash-collision validator beyond the raw Dictionary.Add throws at L69609 and L90534. Searched 'Missing prefab', 'missing prefab', 'Invalid prefab', 'unknown prefab', 'invalid predab', 'Failed to find', 'prefab hash'. The only prefab-integrity code is ZDOMan.FilterZDO L64927 / WarnAndRemoveBrokenZDOs L64941, which WARNS and loads anyway ('Found N ZDOs with unknown prefabs. Will load anyway.' L64948) and only deletes hashes in the hard-coded s_brokenPrefabsToFilterOut blacklist (L64929).
- Any prefab-list, ObjectDB-hash, or mod-manifest handshake on connect. ZNet.RPC_PeerInfo L67647 was read through L67687: uid, version string, uint networkVersion, then 'if (num != 36)' -> Error 3 / ConnectionStatus.ErrorVersion. Nothing else is validated.
- A name string stored in the world-item ZDO. ItemDrop.SaveToZDO(ItemData, ZDO) L59485-59502 and LoadFromZDO L59504-59520 write only durability/stack/quality/variant/crafterID/crafterName/dataCount/data_N/data__N/worldLevel/pickedUp. Identity is carried solely by ZDO.SetPrefab(int) (L62611) / GetPrefab() (L62620) over the private int m_prefab (L62151).
- Consumers of ItemDrop.NameHash() (L59584). Zero call sites in assembly_valheim — note it is built from base.name in Awake (L58986-58989) so on a world instance it hashes 'Name(Clone)'. Do not rely on it.