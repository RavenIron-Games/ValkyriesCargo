# Valheim vanilla API reference — consolidated (BepInEx, no Jotunn)

> **This is the front door.** The nine per-area files beside it hold the full detail with every
> signature and citation; this file is the reconciled summary plus the failure modes that matter
> most. First written for the `Runic` mod, but nothing here is Runic-specific.

Consolidated from 9 independent decompile passes. Unless stated otherwise every `:NNNNN` /
`LNNNNN` / `AV:NNNNN` citation refers to
`C:\WubarrkCODING\libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs`.
Values dumps refer to `C:\WubarrkCODING\libs-Tools\WubarrksEye_Dumps\2026-07-31_21-06-43\Values_Dump.json`.

Assumptions baked into every code sample below: **the assembly is publicized** (many required members
are `private`/`protected`), and **`assembly_utils.dll` is referenced** (`GetStableHashCode` lives there).

---

# READ THIS FIRST — the 10 that crash, desync, or brick a save

| # | Failure | Why | Cite |
|---|---|---|---|
| **1** | **Duplicate prefab-name hash bricks the game.** `ZNetScene.Awake` uses `m_namedPrefabs.Add(hash, prefab)`, not the indexer. A collision throws `ArgumentException` **inside the foreach**, so `Awake` aborts *before* the `ZDOMan.m_onZDODestroyed` hookup and the `"SpawnObject"` routed-RPC registration — and every later prefab in `m_prefabs` goes unregistered. Identical hazard in `ObjectDB.UpdateRegisters` (`m_itemByHash.Add`), which leaves the item DB half-built → mass "Failed to find item prefab" → destroyed inventories. Always `HasPrefab(hash)` / `ContainsKey(hash)` / `m_items.Contains(prefab)` first. | `Dictionary.Add` throws on duplicate key | ZNetScene :69609, :69613, :69616, :69617 · ObjectDB :90534 |
| **2** | **`CopyOtherDB` aliases the lists → your item is added twice.** `m_items = other.m_items;` is a **reference** assignment, so the runtime menu `ObjectDB` and the `FejdStartup.m_objectDBPrefab` asset share one `List<GameObject>` for the whole process. Add in the main menu, return to the main menu once, `CopyOtherDB` runs again, you add again → `UpdateRegisters` throws (gotcha #1). This is the single most common way this pattern bricks a save. Registration must be **idempotent**, not just "run once". | `:90522`–`:90524` | ObjectDB :90520 · FejdStartup :83625 |
| **3** | **A server missing the prefab PERMANENTLY DELETES the object for everyone.** `CreateObject` returns null → `if (ZNet.instance.IsServer())` → `SetOwner(ZDOMan.GetSessionID())` + `ZDOMan.instance.DestroyZDO(zdo)`, logged `"Destroyed invalid predab ZDO:"`. On a **client** the same miss is completely silent and retried at 1/30 s forever (invisible object, no log). There is **no prefab-table handshake at connect** — `ZNet.RPC_PeerInfo` validates only the version string and `networkVersion == 36`. | | ZNetScene :69798–69804, :69837–69843, :69667–69670, Update :69918 · ZNet :67647, :67676–67687 |
| **4** | **Chest round-trip destroys modded items for *all* players.** Container contents are one base64 `ZPackage` in one ZDO string. `Inventory.Load` skips any entry whose name misses `ObjectDB` ("Failed to find item prefab X"); the next `Container.Save` on that peer writes the truncated package back to the **shared** ZDO. One vanilla/stale client opening the chest erases the items for modded clients too. | | Container.Save :103978 / Load :103990 · Inventory.Save :57606 / Load :57640 · AddItem :57788–57793 |
| **5** | **`m_dropPrefab == null` ⇒ silent permanent inventory loss.** It is `[NonSerialized]` (not copied by `Instantiate`, not in the ZDO) and is set only at runtime by `ItemDrop.Awake` from `ObjectDB`. If ObjectDB lacks the entry it stays null, `Inventory.Save` writes an **empty string** (`"Item missing prefab X"`), and `Inventory.Load` skips empty strings. The item is gone on the next save/load with no exception. The "self-heal" line right below it is inside `if (Application.isEditor)` and never runs in a shipped build. | | decl :58312–58313 · ItemDrop.Awake :58992–58994, editor-only :58995–58998 · Inventory.Save :57612–57620 · Load :57666, :57719 |
| **6** | **`Object.Instantiate` at runtime is NOT inert — it spawns a live, replicated entity at world origin.** `ZNetView.Awake` takes the fresh-object branch and calls `ZDOMan.instance.CreateNewZDO(...)` + `ZNetScene.instance.AddInstance(...)`. `ZDOMan.instance` is already non-null by `ZNetScene.Awake` (that method dereferences it). Neither vanilla escape hatch works for a template: `m_forceDisableInit` runs `Object.Destroy(this)` and **destroys the ZNetView component**; `StartGhostInit()/FinishGhostInit()` still calls `CreateNewZDO` and only skips `LoadFields`/`AddInstance`. **Use an inactive `DontDestroyOnLoad` parent** (see checklist step 1). | | ZNetView.Awake :70233–70299, :70237, :70278–70298 · ghost :70531/:70536, :70291–70295 · ZNet.Awake :67075–67080 |
| **7** | **`StatusEffect.NameHash()` hashes `base.name` (the ScriptableObject asset name), not `m_name` — and caches forever.** `CreateInstance<T>()` gives an empty/type name, so every un-named effect hashes identically and `GetStatusEffect` resolves the wrong one or null. `m_nameHash` is written on the **first** `NameHash()` call and never invalidated, so renaming afterwards leaves a stale hash. Set `.name` before anything touches the effect. | `m_nameHash = base.name.GetStableHashCode();` | :26731, :26735 · field :26457 |
| **8** | **Patching only `ObjectDB.Awake` is silently discarded in the main menu.** `FejdStartup.SetupObjectDB` does `AddComponent<ObjectDB>()` (→ `Awake` fires on **empty** lists) and *then* `CopyOtherDB(...)`, which replaces all three list references wholesale. Postfix **both** `Awake` and `CopyOtherDB`. `Awake` and `UpdateRegisters` are `private`. | | :90514, :90517, :90520–90525 · FejdStartup :83272 → :83621 → :83623–83625 |
| **9** | **`ZNetScene.m_prefabs` is dead after `Awake`.** It is read at exactly two lines in the whole assembly: the declaration and the `Awake` loop. `m_namedPrefabs` (private readonly) is the only runtime lookup table. A postfix that only appends to `m_prefabs` registers **nothing**. (`readonly` blocks reassignment, not `.Add`.) | | :69584 decl, :69607 loop · :69588 |
| **10** | **A postfix on `Character.RPC_Damage` runs on every peer that has the victim instanced.** The `if (!m_nview.IsOwner()) return;` gate is at `:8712`; lines `:8703–8711` execute everywhere. Any state mutation in a naive postfix double-applies across N clients. Also `HitData.m_hitCollider` is **never serialized** and is always `null` on the receiving side — dereferencing it NREs on the victim's machine (vanilla converts it to `m_weakSpot` before sending, precisely for this reason). | | RPC_Damage :8701, :8707, :8712 · Damage :8692, :8696 · m_hitCollider :112810 · Serialize :112826 / Deserialize :112942 |

### Also fatal, one line each

| Failure | Cite |
|---|---|
| `clone.name` containing `'('` or `' '` — `ItemDrop.GetPrefabName`/`Utils.GetPrefabName` truncate at the first one, but `ZNetScene.Awake` and `ObjectDB.UpdateRegisters` hash the name **in full** → the two disagree and `m_dropPrefab` silently ends up null. Unity appends `" (Clone)"`; rename immediately after `Instantiate`, as vanilla does. | :59236–59245 · :70414 · :69609 · :90534 · vanilla rename :18504 |
| Reusing a vanilla `m_shared.m_name` — the clone stacks into the vanilla item, and the merged stack keeps one `m_dropPrefab`, so the item mutates into the other prefab on drop/save. Recipes, `m_knownRecipes`, `m_knownMaterial` and `ObjectDB.GetRecipe` are all keyed on this token. | :56927, :56991, :56971 · :90616 · :15578, :15582, :17870, :17969 |
| `SEMan.AddStatusEffect(int hash, …)` **returns null when you are not the owner** even though the RPC was sent. `var se = seman.AddStatusEffect(h); se.m_ttl = x;` NREs on every non-owner. | :24352, :24363 |
| A modded `StatusEffect` fired over `RPC_AddStatusEffect` at a peer whose ObjectDB lacks it: `Internal_AddStatusEffect` returns null with **no log**. Totally silent desync. | :24366, :24374, :24386–24390 |
| `ObjectDB.GetAllItems` calls `GetComponent<ItemDrop>()` and dereferences `m_itemData` **unguarded** → adding any `ItemDrop`-less GameObject to `m_items` NREs character creation and the `hair`/`beard` console commands. | :90598, :90603–90604 · :49174 · :37043 |
| `ItemData.GetIcon()` is `return m_shared.m_icons[m_variant];` — unguarded index. Empty `m_icons` throws in the inventory GUI. | :58539–58542 |
| `Humanoid.DropItem` does `itemDrop.GetComponent<Rigidbody>().linearVelocity = …` with no null check. Never strip the Rigidbody from a clone. | :13621 |
| `EffectList.Create` calls `Instantiate(effectData.m_prefab, …)` with no null check → a null `m_prefab` throws every time the effect fires. | :30026 |
| `SharedData` is a **class**, shared by reference. `ItemData.Clone()` is `MemberwiseClone()` + a fresh `m_customData` only. Writing `item.m_shared.X` mutates the ObjectDB prefab and every stack in the world. There is no `SharedData.Clone()`. | :58004 · :58321–58326 |
| `ObjectDB.instance` is never nulled (**no `OnDestroy`**), so after leaving a world it points at a *destroyed* Unity object. `!= null` is true; use the Unity implicit bool `(bool)ObjectDB.instance` as vanilla does. | :90500 · vanilla idiom :49172 |

---

# Registration order checklist

The single authoritative sequence. Merged primarily from the Jotunn-free cloning pass (most complete),
cross-checked against the ObjectDB and ZNetScene passes.

### Phase A — plugin `Awake`, once per process

1. **Create the inert container.** `SetActive(false)` **before** anything is parented, so no cloned
   `Awake` ever fires (gotcha #6). No vanilla helper for this exists — NOT FOUND; the pattern is original.
   ```csharp
   var holder = new GameObject("Runic_PrefabContainer");
   holder.SetActive(false);
   UnityEngine.Object.DontDestroyOnLoad(holder);   // else a scene change destroys the clones
   ```                                             // and m_namedPrefabs keeps fake-null references
2. **Apply Harmony patches** — postfix `ObjectDB.Awake` (:90514, private), postfix
   `ObjectDB.CopyOtherDB` (:90520), postfix `ZNetScene.Awake` (:69604, private).
   Use string names or `AccessTools.Method` for the private ones.
   **Do NOT prefix `ZNetScene.Awake`** — see ⚠ CONFLICT C-1 below.

### Phase B — inside the `ObjectDB` postfix (fires at least twice: FejdStartup, then the game scene)

3. **Sanity gate.** `ObjectDB.Awake` fires from `FejdStartup.SetupObjectDB` with an **empty** `m_items`
   before `CopyOtherDB` populates it.
   ```csharp
   var odb = ObjectDB.instance;
   if (!(bool)odb || odb.m_items == null || odb.m_items.Count == 0) return;
   if (odb.GetItemPrefab("Wood") == null) return;      // vanilla content actually present
   ```
4. **Idempotency gate** — never `Add` twice; `m_itemByHash.Add` throws on the duplicate hash (:90534).
   Cheapest correct guard: `if (odb.GetItemPrefab(hash) != null) return;` plus
   `if (!odb.m_items.Contains(clone))` on the add itself.
5. **Resolve the donor:** `GameObject donor = odb.GetItemPrefab("SwordIron");` (:90555). Abort if null.
6. **Collision check BEFORE creating anything.**
   ```csharp
   const string name = "RunicMyItem";               // no '(' , no ' ' , globally unique
   int hash = name.GetStableHashCode();
   if (odb.GetItemPrefab(hash) != null) return;                                   // :90560
   if (ZNetScene.instance != null && ZNetScene.instance.HasPrefab(hash)) return;  // :69702
   ```
7. **Clone once and cache the reference** (module-static; you will need it again in Phase C).
   ```csharp
   clone = UnityEngine.Object.Instantiate(donor, holder.transform, worldPositionStays: false);
   clone.name = name;                                // strips "(Clone)". THIS IS THE IDENTITY.
   ```
   `Instantiate` deep-copies the `[Serializable]` `ItemData`/`SharedData` (:57920, :58004) — the clone
   gets its **own** `m_shared`, while `Sprite[]`, `StatusEffect`, `Attack`, `GameObject` refs stay shared.
   **Never hand-assign `clone…m_shared = donor…m_shared`** — `m_itemByData` is keyed by that object
   reference and uses the indexer, so it silently overwrites the donor's mapping (:90510, :90538).
8. **Mutate the clone's `SharedData`:** unique `m_shared.m_name` token, description, stats.
   Keep `m_shared.m_icons` non-empty (:58541). Leave `m_itemData.m_dropPrefab` **null** — `ItemDrop.Awake`
   fills it (:58994). Keep `ZNetView` (`m_persistent = true`, `m_type = Default`, `m_distant = false`),
   `Rigidbody`, `ZSyncTransform`, `ItemDrop` on the object.
9. **Register in ObjectDB, then rebuild the hash register.** This is the "what MUST be called" answer:
   ```csharp
   if (!odb.m_items.Contains(clone)) odb.m_items.Add(clone);   // :90504
   odb.UpdateRegisters();                                      // :90528 — PRIVATE
   // stock assembly: AccessTools.Method(typeof(ObjectDB), "UpdateRegisters").Invoke(odb, null);
   ```
   `UpdateRegisters` is the **only** thing that populates `m_itemByHash`, which is the **only** thing
   `GetItemPrefab(int)` reads (:90583–90586). Skip it and `GetItemPrefab` returns null forever, and
   `m_itemByData` stays empty for your item (breaks eat animations :13669 and `Player.AddKnownItem` :20218).
10. **Register recipes — no rebuild needed.** `UpdateRegisters` does not touch `m_recipes`; every consumer
    is a linear `foreach` (:90612/:90616, :20245, :20458). Order-independent, any time after `Awake`.
    ```csharp
    var r = ScriptableObject.CreateInstance<Recipe>();
    r.name = "Recipe_RunicMyItem";                       // cosmetic — see ⚠ C-5
    r.m_item = clone.GetComponent<ItemDrop>();           // MUST be the CLONE — :42659 crafts by name
    r.m_enabled = true; r.m_amount = 1; r.m_minStationLevel = 1;
    r.m_craftingStation = /* a real CraftingStation component; matched by m_name at runtime */;
    r.m_resources = new Piece.Requirement[] {
        new Piece.Requirement { m_resItem = woodDrop, m_amount = 10, m_amountPerLevel = 1, m_recover = true }
    };
    if (!odb.m_recipes.Contains(r)) odb.m_recipes.Add(r);
    ```
    Every `m_resItem` must itself be an ObjectDB-registered `ItemDrop` (:118208).
11. **StatusEffects** — set `.name` first (gotcha #7), then
    `if (!odb.m_StatusEffects.Contains(se)) odb.m_StatusEffects.Add(se);`. No rebuild; `GetStatusEffect`
    is a linear scan (:90543).
12. If `ZNetScene.instance != null` at this point, run step 14 immediately — **the two `Awake`s have no
    guaranteed order** (nothing in the decompile sequences them).

### Phase C — inside the `ZNetScene.Awake` postfix

13. Guard `ZNetScene.instance != null && clone != null`. If ObjectDB has not run yet, do steps 5–11 here first.
14. **Write the runtime dictionary** — `m_prefabs` alone is useless post-`Awake` (gotcha #9):
    ```csharp
    var zns = ZNetScene.instance;
    int hash = clone.name.GetStableHashCode();
    if (!zns.m_prefabs.Contains(clone)) zns.m_prefabs.Add(clone);      // :69584 — for other mods/tools
    if (!zns.m_namedPrefabs.ContainsKey(hash))
        zns.m_namedPrefabs.Add(hash, clone);                           // :69588 — publicizer required
    ```
    `ContainsKey` + `Add` (or the indexer). Never a bare `.Add` on an unchecked hash.
15. **Re-entrancy:** `ZNetScene.Awake` runs fresh on every game-scene load. The hook must be safe to run
    repeatedly against a surviving `DontDestroyOnLoad` clone.

### Phase D — multiplayer, non-negotiable

16. Ship and load the **same** plugin on the dedicated server (gotcha #3).
17. Enforce client/server lockstep with **ServerSync** (or a version RPC). Vanilla validates nothing at
    connect. Every stat you change on `SharedData`/`Attack`/`Smelter.m_conversion` must be identical on
    every peer — none of it is networked.
18. **`clone.name` is byte-stable forever.** It is the ZDO prefab hash (:70281/:70285), the ObjectDB key
    (:90534), and the inventory save string (:57619). Renaming it orphans every existing item.
    Same for `m_shared.m_name` / `Piece.m_name` / `CraftingStation.m_name` — those are serialized into the
    character save as `m_knownRecipes` / `m_knownStations` keys (:19642–19652) and renaming re-locks them.

---

# ⚠ CONFLICT register

Contradictions between reports, resolved but **not** silently.

### ⚠ C-1 — Prefix vs postfix on `ZNetScene.Awake`
- **Claim A** (ZNetScene pass): "Prefix on `ZNetScene.Awake` → append to `m_prefabs`; vanilla loop
  registers it. Simplest, one code path."
- **Claim B** (Jotunn-free cloning pass): "Do **not** prefix — you need the vanilla registers already
  built so your `.Add` cannot corrupt the vanilla foreach", because a colliding name injected pre-`Awake`
  throws inside the loop and aborts `Awake` before the `ZDOMan` hookup and `"SpawnObject"` RPC (:69609,
  :69615–69617) — the game is bricked.
- **Resolution: B is stronger**, and both reports independently document the abort. The decisive detail
  is that at prefix time `m_namedPrefabs` is **empty**, so `HasPrefab(hash)` cannot detect a collision —
  you would have to scan `m_prefabs` by name yourself. Use **postfix + direct `m_namedPrefabs` write**
  (checklist step 14). Claim A is only safe with a hand-rolled O(n) name scan over `m_prefabs`.

### ⚠ C-2 — `Smelter` line region
- **Claim A** (world-content pass): `public class Smelter : MonoBehaviour, IHasHoverMenuExtended` at
  **AV:123533**, with `m_conversion` :123591, `IsItemAllowed` :123689/:123684, `RPC_AddOre` :124055,
  `Spawn` :124061, `GetItemConversion` :124073, tooltip :124175/:124200.
- **Claim B** (Jotunn-free cloning pass): lists `Smelter L107777` in a table of `m_dropPrefab.name` readers.
- **Resolution: A is stronger** — it is a dedicated, internally consistent 700-line span cross-checked
  against the values dump. B's figure is one incidental row in a list whose neighbours are also imprecise
  (it cites a single line, `L109662`, for both `Fermenter` *and* `FishingFloat`). Treat `L107777` as
  unverified; use the 123533+ range. Both agree on the *behaviour* (`.name` deref → NRE if null).

### ⚠ C-3 — `HitData.DamageTypes` declaration line
- **Claim A** (ItemDrop pass) writes the row as "58379-58380 (in HitData, line 112379-112380)".
- **Claim B** (damage-pipeline pass): `[Serializable] public struct DamageTypes` at **:112380**, fields
  :112382–112402, methods :112406–112558.
- **Resolution: 112379–112380 is correct**; the "58379-58380" figure is a transcription slip (58379 falls
  inside `SharedData`, which is a different class). B corroborates every member line individually.

### ⚠ C-4 — `SEMan.ModifyAttack` call-site labels inside `Attack`
- **Claim A** (damage-pipeline pass): called at Attack `:1757` (projectile spawn), `:1907` (melee sweep),
  `:2163` (secondary/AOE path).
- **Claim B** (Attack pass) maps the methods: `FireProjectileBurst` :1616, `DoAreaAttack` :1811,
  `DoMeleeAttack` :1992 (running to ~:2270).
- **Resolution: the line numbers agree; A's labels are swapped.** By B's method map, `:1757` is inside
  `FireProjectileBurst`, `:1907` is inside `DoAreaAttack`, and `:2163` is inside `DoMeleeAttack`.
  B is stronger (full method-by-method map of the class). The actionable fact is unaffected:
  **all three run attacker-side, pre-serialization** — the correct place to buff outgoing damage.

### ⚠ C-5 — Does anything read `Recipe.name`?
- **Claim A** (recipes pass): "nothing reads `recipe.name`" — sorting uses `m_listSortWeight` (:60510,
  used :42186) and the localized `m_item…m_shared.m_name` (:42181).
- **Claim B** (cloning pass): instructs `r.name = "Recipe_YourItemName"`.
- **Resolution: not a contradiction.** A is correct that no vanilla code path reads it; B's assignment is
  harmless and worth keeping for debugging/`Values_Dump` legibility. Do **not** rely on it for identity.

### ⚠ C-6 — `ZoneSystem.m_locationScenes` / `m_locationLists` line numbers
- **Claim A** (ZNetScene pass, in passing): `m_locationScenes` :98106, `m_locationLists` :98108.
- **Claim B** (world-content pass): :98105 and :98107.
- **Resolution: B is stronger** — it is the dedicated ZoneSystem pass and cites the neighbouring
  declarations consistently (`m_vegetation` :98110, `m_locations` :98112). A cites them only inside a
  NOT-FOUND note. One-line discrepancy, no behavioural impact.

### ⚠ C-7 — `ItemDrop.Awake` start line
- **Claim A** (ItemDrop pass): "`ItemDrop.Awake` (58992–58994)".
- **Claim B** (cloning pass): `Awake` begins **L58984**; `m_nameHash` built :58986–58989;
  `s_instances` registration :58991; the ObjectDB lookup :58992–58994; editor-only block :58995–58998.
- **Resolution: B is stronger** (it enumerates the intervening statements). A's range is the ObjectDB
  lookup *within* `Awake`, not the method's start.

### ⚠ C-8 — minor citation drift (no behavioural disagreement)
| Item | Claim A | Claim B | Use |
|---|---|---|---|
| `Inventory.AddItem` "Failed to find item prefab" guard | :57788–57792 | L57788–57793 | either; same statement |
| `Character.RPC_Damage` status-effect block | :8756–8767 (code quoted) | :8756–8772 (table row, incl. `SetAttacker`) | :8756–8772 — B's range covers `statusEffect.SetAttacker(attacker)` |
| `ZNetView.LoadFields` | table row ":70341 (public)" | body discussed at :70386–70407 | decl :70341, the prefab-resolution loop :70386–70407 (same report, internally elided) |
| `ZNetScene` span | ":69095–69523" in the SERVER decompile header | :69576–70035 in the client decompile | **:69576+ is the client file** — the 69095 figure is `assembly_valheim_SERVER.decompiled.cs`, a different file. Do not mix. |

---

# 1. ObjectDB

`assembly_valheim.decompiled.cs` **lines 90498–90623 are the entire class. There are no other members.**
The server build (`assembly_valheim_SERVER.decompiled.cs:88911–89036`) is byte-identical in shape — no
client/server divergence.

## 1.1 Declaration + singleton

```csharp
// :90498
public class ObjectDB : MonoBehaviour
{
    private static ObjectDB m_instance;                                   // :90500
    public static ObjectDB instance => m_instance;                        // :90512  (get-only, no setter)
}
```

`m_instance` is set **only** in `Awake` (:90516). There is **no `OnDestroy`** — after leaving a world it
is a *destroyed* Unity object. Vanilla guards with the implicit bool: `if (m_hairs == null && (bool)ObjectDB.instance)` (:49172).

## 1.2 Fields (all instance)

| Field | Exact type | Access | Line |
|---|---|---|---|
| `m_StatusEffects` | `List<StatusEffect>` | **public** | 90502 |
| `m_items` | `List<GameObject>` | **public** | 90504 |
| `m_recipes` | `List<Recipe>` | **public** | 90506 |
| `m_itemByHash` | `Dictionary<int, GameObject>` | **private** (publicizer) | 90508 |
| `m_itemByData` | `Dictionary<ItemDrop.ItemData.SharedData, GameObject>` | **private** (publicizer) | 90510 |
| `m_instance` | `ObjectDB` | **private static** | 90500 |

All five are initialized inline, so never null. **There are exactly two lookup dictionaries.** No recipe
cache, no status-effect cache, no name→prefab dictionary.

## 1.3 Awake vs CopyOtherDB

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
    m_items         = other.m_items;         // :90522  REFERENCE assignment, not a copy
    m_recipes       = other.m_recipes;       // :90523  REFERENCE assignment
    m_StatusEffects = other.m_StatusEffects; // :90524  REFERENCE assignment
    UpdateRegisters();                       // :90525
}
```

| Context | What happens |
|---|---|
| **Main menu** | `FejdStartup.Start()` (:83272) → `SetupObjectDB()` (:83621). Body: `ObjectDB objectDB = base.gameObject.AddComponent<ObjectDB>();` (:83623) → **`Awake` fires immediately on empty lists** → `ObjectDB component = m_objectDBPrefab.GetComponent<ObjectDB>();` (:83624, field decl `public GameObject m_objectDBPrefab;` :83104) → `objectDB.CopyOtherDB(component);` (:83625). Both run, `Awake` FIRST. |
| **Joining a world / `_GameMain`** | The scene's ObjectDB component runs `Awake` only. |

`CopyOtherDB` is called from **exactly one call site in the whole assembly** (:83625) — grep returns 3
hits: decl 90520, call 83625, and the `UpdateRegisters` inside it.

## 1.4 The hash-rebuild method — EXACT

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
            m_itemByData[component.m_itemData.m_shared] = item;  // :90538  <-- indexer, silent overwrite
    }
}
```

Recipes and StatusEffects need **no** rebuild — `GetRecipe` (:90612) and `GetStatusEffect` (:90543) are
linear scans over the public lists. Adding takes effect instantly.

## 1.5 Lookup API — every overload

```csharp
public StatusEffect GetStatusEffect(int nameHash)                                   // :90543  linear scan, compares se.NameHash()
public GameObject   GetItemPrefab(string name)                                      // :90555  -> GetItemPrefab(name.GetStableHashCode())
public GameObject   GetItemPrefab(int hash)                                         // :90560  -> TryGetItemPrefab(hash), null if absent
public GameObject   GetItemPrefab(ItemDrop.ItemData.SharedData sharedData)          // :90569
public bool TryGetItemPrefab(string name, out GameObject prefab)                    // :90578
public bool TryGetItemPrefab(int hash, out GameObject prefab)                       // :90583  m_itemByHash.TryGetValue
public bool TryGetItemPrefab(ItemDrop.ItemData.SharedData sharedData, out GameObject prefab) // :90588  m_itemByData.TryGetValue
public int  GetPrefabHash(GameObject prefab)                                        // :90593  => prefab.name.GetStableHashCode()
public List<ItemDrop> GetAllItems(ItemDrop.ItemData.ItemType type, string startWith)// :90598  linear, GetComponent<ItemDrop>() with NO null check
public Recipe GetRecipe(ItemDrop.ItemData item)                                     // :90612
```

`GetRecipe` body (:90614–90621) matches on
`recipe.m_item.m_itemData.m_shared.m_name == item.m_shared.m_name` — the **localization token**, not the
prefab name. Two items sharing a token collide and the first in `m_recipes` wins; reusing a vanilla token
hijacks the vanilla craft+repair lookup.

`GetItemPrefab(string)` (:90555) calls `name.GetStableHashCode()` with **no null check** → NRE on a null name.

## 1.6 Minimal correct registration (both patch points)

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

## 1.7 What a missing ObjectDB entry does to a joining client

ObjectDB is **never network-synchronised** — no serialization anywhere in the assembly. Server and each
client build their own from their own local prefab.

| Path | Symptom | Cite |
|---|---|---|
| `Inventory.AddItem(name, …)` | logs "Failed to find item prefab", **drops the item** from the loaded character → permanent loss on next save | :57735–57739, :57788–57792 |
| `VisEquipment.AttachItem` / `AttachArmor` | logs "Missing attach item", renders nothing | :29146, :29218, :28858, :28921, :29001 |
| `Player.Load` food entries | logs "Failed to find food item" | :19868 |
| `SEMan.Internal_AddStatusEffect` | returns null, **no log at all** | :24374, :24386–24390 |

