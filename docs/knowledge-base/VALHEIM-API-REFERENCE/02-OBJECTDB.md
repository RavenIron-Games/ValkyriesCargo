## ObjectDB — full class map

Decompile: `C:\WubarrkCODING\libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs`
**The entire class is lines 90498–90623.** There are no other members. Server build (`assembly_valheim_SERVER.decompiled.cs:88911–89036`) is byte-identical in shape — no client/server divergence.

### Declaration + singleton

```csharp
// :90498
public class ObjectDB : MonoBehaviour
{
    private static ObjectDB m_instance;                                   // :90500
    public static ObjectDB instance => m_instance;                        // :90512  (get-only property, no setter)
}
```

`m_instance` is set **only** in `Awake` (`:90516`). There is **no `OnDestroy`** in the class — `m_instance` is never nulled, so after leaving a world it is a *destroyed* Unity object. Vanilla guards with the Unity implicit bool, e.g. `if (m_hairs == null && (bool)ObjectDB.instance)` at `:49172`. Do the same; `ObjectDB.instance != null` (C# `!=`) is *not* enough if you keep a cached reference.

### Fields (all instance)

| Field | Exact type | Access | Line |
|---|---|---|---|
| `m_StatusEffects` | `List<StatusEffect>` | **public** | 90502 |
| `m_items` | `List<GameObject>` | **public** | 90504 |
| `m_recipes` | `List<Recipe>` | **public** | 90506 |
| `m_itemByHash` | `Dictionary<int, GameObject>` | **private** (needs publicizer) | 90508 |
| `m_itemByData` | `Dictionary<ItemDrop.ItemData.SharedData, GameObject>` | **private** (needs publicizer) | 90510 |
| `m_instance` | `ObjectDB` | **private static** | 90500 |

All five are initialized inline (`new List<>()` / `new Dictionary<>()`), so they are never null.
**There are exactly two lookup dictionaries.** No recipe cache, no status-effect cache, no name→prefab dictionary.

### Awake vs CopyOtherDB

```csharp
// :90514
private void Awake()
{
    m_instance = this;      // :90516
    UpdateRegisters();      // :90517
}

// :90520
public void CopyOtherDB(ObjectDB other)
{
    m_items        = other.m_items;         // :90522  REFERENCE assignment, not a copy
    m_recipes      = other.m_recipes;       // :90523  REFERENCE assignment
    m_StatusEffects= other.m_StatusEffects; // :90524  REFERENCE assignment
    UpdateRegisters();                      // :90525
}
```

**Which runs where:**

| Context | What happens |
|---|---|
| **Main menu** | `FejdStartup.Start()` → `SetupObjectDB()` (`:83272` → `:83621`). Body: `ObjectDB objectDB = base.gameObject.AddComponent<ObjectDB>();` (`:83623`) → **`Awake` fires immediately** on empty lists → `ObjectDB component = m_objectDBPrefab.GetComponent<ObjectDB>();` (`:83624`, field decl `public GameObject m_objectDBPrefab;` at `:83104`) → `objectDB.CopyOtherDB(component);` (`:83625`). So in the menu **both** run, `Awake` FIRST and `CopyOtherDB` SECOND. |
| **Joining a world / loading `_GameMain`** | The scene's ObjectDB component runs `Awake` only. `CopyOtherDB` is called **from exactly one call site in the whole assembly** (`:83625`) — grep confirms only 3 hits: decl 90520, call 83625, and `UpdateRegisters` inside it. |

Consequence: a Harmony **postfix on `Awake` alone is silently discarded in the main menu** (CopyOtherDB overwrites all three list references right after). Patch **both**:
- `[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))] postfix` — covers world load/join.
- `[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))] postfix` — covers main menu.
Both must be idempotent (see gotchas). `Awake` is private → use `"Awake"` string or `AccessTools.Method(typeof(ObjectDB), "Awake")` unless relying on the publicizer.

### The hash-rebuild method — EXACT

```csharp
// :90528   NOTE: private, and it is UpdateRegisters — NOT "UpdateItemHashes"
private void UpdateRegisters()
{
    m_itemByHash.Clear();                                        // :90530
    m_itemByData.Clear();                                        // :90531
    foreach (GameObject item in m_items)                         // :90532
    {
        m_itemByHash.Add(item.name.GetStableHashCode(), item);   // :90534  <-- .Add(), NOT indexer
        ItemDrop component = item.GetComponent<ItemDrop>();      // :90535
        if ((object)component != null)                           // :90536
            m_itemByData[component.m_itemData.m_shared] = item;  // :90538  <-- indexer, safe
    }
}
```

**Answer to "what MUST be called after adding a GameObject to `m_items`":**
`ObjectDB.instance.UpdateRegisters()` — it is the *only* thing that populates `m_itemByHash`, which is the *only* thing `GetItemPrefab(int)` reads (`:90583–90586`). Without it, `GetItemPrefab` returns null forever.
It is `private`, so either:
- publicized assembly: `ObjectDB.instance.UpdateRegisters();`
- or reflection: `AccessTools.Method(typeof(ObjectDB), "UpdateRegisters").Invoke(ObjectDB.instance, null);`

**Recipes and StatusEffects need NO rebuild** — `GetRecipe` (`:90612`) and `GetStatusEffect` (`:90543`) are linear scans over the public lists. Adding to `m_recipes` / `m_StatusEffects` takes effect instantly.

### Lookup API — every overload

```csharp
public StatusEffect GetStatusEffect(int nameHash)                                   // :90543  linear scan of m_StatusEffects, compares se.NameHash()
public GameObject   GetItemPrefab(string name)                                      // :90555  -> GetItemPrefab(name.GetStableHashCode())
public GameObject   GetItemPrefab(int hash)                                         // :90560  -> TryGetItemPrefab(hash), null if absent
public GameObject   GetItemPrefab(ItemDrop.ItemData.SharedData sharedData)          // :90569
public bool TryGetItemPrefab(string name, out GameObject prefab)                    // :90578
public bool TryGetItemPrefab(int hash, out GameObject prefab)                       // :90583  m_itemByHash.TryGetValue
public bool TryGetItemPrefab(ItemDrop.ItemData.SharedData sharedData, out GameObject prefab) // :90588  m_itemByData.TryGetValue
public int  GetPrefabHash(GameObject prefab)                                        // :90593  => prefab.name.GetStableHashCode()
public List<ItemDrop> GetAllItems(ItemDrop.ItemData.ItemType type, string startWith)// :90598  linear, calls GetComponent<ItemDrop>() with NO null check
public Recipe GetRecipe(ItemDrop.ItemData item)                                     // :90612
```

`GetRecipe` body (`:90614–90621`) matches on `recipe.m_item.m_itemData.m_shared.m_name == item.m_shared.m_name` — that is the **localization token**, not the prefab name. Two items sharing an `m_name` token collide and the first in `m_recipes` wins.

`Recipe` (`:60499`, `ScriptableObject`): `public ItemDrop m_item;` (60501), `public int m_amount = 1;` (60503), `public bool m_enabled = true;` (60505), `public CraftingStation m_craftingStation;` (60513), `public CraftingStation m_repairStation;` (60515), `public int m_minStationLevel = 1;` (60517), `public bool m_requireOnlyOneIngredient;` (60519), `public Piece.Requirement[] m_resources = new Piece.Requirement[0];` (60521).

### StatusEffect hashing — asset `name`, NOT `m_name`

```csharp
// :26393
public class StatusEffect : ScriptableObject
{
    public string m_name = "";   // :26405   localization token, e.g. "$se_adrenalinerush"
    private int m_nameHash;      // :26457   cached, private

    // :26731
    public int NameHash()
    {
        if (m_nameHash == 0)
            m_nameHash = base.name.GetStableHashCode();   // :26735  ScriptableObject.name (asset name)
        return m_nameHash;
    }
}
```

Confirmed against the values dump: `Values_Dump.json` StatusEffects records (89 of them) carry `"_Key": "AdrenalineRush"` with `"_displayName": "$se_adrenalinerush 1"` — the key is the **asset name**, `m_name` is the token.

Network path that depends on this: `SEMan.AddStatusEffect(int nameHash, ...)` → `m_nview.InvokeRPC("RPC_AddStatusEffect", nameHash, ...)` (`:24362`) → `RPC_AddStatusEffect` (`:24366`) → `Internal_AddStatusEffect` (`:24374`) → `ObjectDB.instance.GetStatusEffect(nameHash)` (`:24386`); returns null → **silently returns null, no log, no effect** (`:24387–24390`). `SEMan.GetStatusEffect(int nameHash)` at `:24506` is a separate per-character list, gated on `m_statusEffectsHashSet`.

### Minimal correct registration recipe (both patch points)

```csharp
static void RegisterAll(ObjectDB odb)
{
    if (odb == null || odb.m_items == null) return;
    if (odb.GetItemPrefab("MyItem".GetStableHashCode()) != null) return; // idempotency guard

    if (!odb.m_items.Contains(myItemPrefab)) odb.m_items.Add(myItemPrefab);
    if (!odb.m_recipes.Contains(myRecipe))   odb.m_recipes.Add(myRecipe);
    myStatusEffect.name = "MyEffect";                                    // MUST set before first NameHash()
    if (!odb.m_StatusEffects.Contains(myStatusEffect)) odb.m_StatusEffects.Add(myStatusEffect);

    odb.UpdateRegisters();   // private -> publicizer or AccessTools
}
```


## GOTCHAS
- `UpdateRegisters` uses `m_itemByHash.Add(...)` (line 90534), NOT the indexer. Any duplicate `item.name.GetStableHashCode()` throws `ArgumentException: An item with the same key has already been added`. The exception escapes mid-loop, so `m_itemByHash` is left PARTIALLY built and every vanilla item after the collision point becomes unresolvable -> mass 'Failed to find item prefab' and destroyed inventories. Always guard with `if (!odb.m_items.Contains(prefab))` AND ensure your prefab name is globally unique.
- `CopyOtherDB` assigns lists BY REFERENCE (`m_items = other.m_items;` :90522), so the runtime menu ObjectDB and the `FejdStartup.m_objectDBPrefab` asset's ObjectDB share the SAME `List<GameObject>` instance. Adding to `ObjectDB.instance.m_items` in the main menu mutates the prefab list, which persists for the whole process. Return to the main menu once and `CopyOtherDB` runs again -> your item is added a SECOND time -> `UpdateRegisters` throws on the duplicate hash. This is the single most common way this pattern bricks a save.
- In `FejdStartup.SetupObjectDB` (:83621) `Awake` fires on `AddComponent<ObjectDB>()` BEFORE `CopyOtherDB` replaces all three list references. A postfix on `Awake` only is silently thrown away on the main menu. Patch both `Awake` and `CopyOtherDB`.
- `UpdateRegisters` is `private` (:90528) and `Awake` is `private` (:90514). Calling/patching them by name requires the publicized assembly or `AccessTools`. Do not ship a build that references them publicly against the stock DLL.
- `ObjectDB` has NO `OnDestroy`, so `m_instance` (:90500) keeps pointing at a destroyed component after you leave a world. `ObjectDB.instance != null` is true while the object is dead. Use the Unity implicit bool `(bool)ObjectDB.instance` as vanilla does at :49172.
- `StatusEffect.NameHash()` hashes `base.name` (the ScriptableObject asset name, :26735), not `m_name`. A `ScriptableObject.CreateInstance<SE_Stats>()` has `name == ""`, so every such effect hashes identically and `GetStatusEffect` returns the wrong one. Set `.name` explicitly.
- `StatusEffect.m_nameHash` (:26457) caches on FIRST `NameHash()` call and is never invalidated. Changing `.name` afterwards leaves a stale hash. Set the name before anything touches the effect.
- MULTIPLAYER: ObjectDB is never network-synchronised — there is no serialization of it anywhere in the assembly. Server and each client build their own from their own local prefab. A joining client without the mod gets `ObjectDB.GetItemPrefab(hash) == null` and: `Inventory.AddItem(name,...)` logs 'Failed to find item prefab' and DROPS the item from the loaded character (:57735-57739, :57788-57792) -> permanent inventory loss on next save; `VisEquipment.AttachItem/AttachArmor` logs 'Missing attach item' and renders nothing (:29146, :29218, :28858, :28921, :29001); `Player.Load` food entries log 'Failed to find food item' (:19868).
- MULTIPLAYER: `SEMan.Internal_AddStatusEffect` (:24374) returns null with NO log when `ObjectDB.instance.GetStatusEffect(nameHash)` misses (:24386-24390). A modded status effect fired over `RPC_AddStatusEffect` (:24366) just never applies on an unmodded peer — a completely silent desync.
- `GetItemPrefab(string name)` (:90555) does `name.GetStableHashCode()` with no null check -> NullReferenceException on a null name.
- `GetAllItems(...)` (:90598) calls `item.GetComponent<ItemDrop>()` and dereferences `component.m_itemData` with NO null check (:90604). Adding a GameObject to `m_items` that has no `ItemDrop` component crashes the character-creation hair/beard load (:49174) and the `beard`/`hair` console commands (:37043).
- `GetRecipe(ItemDrop.ItemData)` (:90612) matches by `m_shared.m_name` (localization token), not prefab name. Reusing a vanilla token on a modded item hijacks/steals the vanilla repair+craft lookup.
- `GetStableHashCode` is NOT declared in assembly_valheim (334 usages, 0 declarations). It is the extension method in assembly_utils.dll — reference that DLL or you will not compile.
- Adding to `m_items` without calling `UpdateRegisters` also leaves `m_itemByData` empty for your item, breaking `TryGetItemPrefab(SharedData)` used by eating animations (:13669) and `Player.AddKnownItem` for trophies (:20218, which logs LogError).

## NOT FOUND
- ObjectDB.GetItemByHash — NOT FOUND. No such method exists. Use GetItemPrefab(int hash) (:90560) or TryGetItemPrefab(int, out GameObject) (:90583).
- ObjectDB.UpdateItemHashes — NOT FOUND. The real name is UpdateRegisters (private, :90528).
- ObjectDB.m_itemByName — NOT FOUND. Only m_itemByHash (:90508) and m_itemByData (:90510) exist.
- ObjectDB.m_recipeByHash / any recipe lookup dictionary or cache — NOT FOUND. GetRecipe is a linear scan (:90612).
- ObjectDB.m_StatusEffectsByHash / any status-effect dictionary or cache — NOT FOUND. GetStatusEffect is a linear scan (:90543).
- ObjectDB.GetStatusEffect(string) — NOT FOUND. Only the int-hash overload exists (:90543). Hash the asset name yourself.
- ObjectDB.GetStatusEffect(StatusEffect) / GetStatusEffectByName — NOT FOUND.
- ObjectDB.GetItemPrefab(GameObject) — NOT FOUND. The three overloads are string / int / ItemDrop.ItemData.SharedData.
- ObjectDB.OnDestroy, ObjectDB.Start, ObjectDB.OnEnable — NOT FOUND. Awake (:90514) is the only Unity message on the class.
- ObjectDB static events / OnObjectDBReady / any hook point — NOT FOUND. Harmony postfixes on Awake and CopyOtherDB are the only entry points.
- Any ObjectDB serialization / network sync (ZPackage read/write, RPC) — NOT FOUND anywhere in the assembly. It is purely process-local.
- A second CopyOtherDB call site — NOT FOUND. FejdStartup.SetupObjectDB (:83625) is the only one in the entire assembly.
- Any ObjectDB reference inside Game or ZNetScene — NOT FOUND. The in-world ObjectDB comes from the scene component's own Awake.
- StringExtensionMethods / GetStableHashCode declaration — NOT FOUND in assembly_valheim.decompiled.cs (used 334x, declared in assembly_utils.dll).