## Source

All citations: `C:\WubarrkCODING\libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs`
Verified identical in `assembly_valheim_SERVER.decompiled.cs` (ZNetScene at :69095–69523, same logic).

---

## 1. `ZNetScene` fields — exact declared types

| Line | Declaration |
|---|---|
| 69576 | `public class ZNetScene : MonoBehaviour` |
| 69578 | `private static ZNetScene s_instance;` |
| 69580 | `private const int m_maxCreatedPerFrame = 10;` |
| 69584 | `public List<GameObject> m_prefabs = new List<GameObject>();` |
| 69586 | `public List<GameObject> m_nonNetViewPrefabs = new List<GameObject>();` |
| 69588 | `private readonly Dictionary<int, GameObject> m_namedPrefabs = new Dictionary<int, GameObject>();` |
| 69590 | `private readonly Dictionary<ZDO, ZNetView> m_instances = new Dictionary<ZDO, ZNetView>();` |
| 69602 | `public static ZNetScene instance => s_instance;` |

`m_prefabs` and `m_nonNetViewPrefabs` are **public** (no publicizer needed).
`m_namedPrefabs` is **private readonly** — reachable only via publicized assembly / reflection. `readonly` does not block `.Add()`.

---

## 2. `ZNetScene.Awake` — full body (:69604–69618)

```csharp
private void Awake()
{
    s_instance = this;
    foreach (GameObject prefab in m_prefabs)
    {
        m_namedPrefabs.Add(prefab.name.GetStableHashCode(), prefab);
    }
    foreach (GameObject nonNetViewPrefab in m_nonNetViewPrefabs)
    {
        m_namedPrefabs.Add(nonNetViewPrefab.name.GetStableHashCode(), nonNetViewPrefab);
    }
    ZDOMan zDOMan = ZDOMan.instance;
    zDOMan.m_onZDODestroyed = (Action<ZDO>)Delegate.Combine(zDOMan.m_onZDODestroyed, new Action<ZDO>(OnZDODestroyed));
    ZRoutedRpc.instance.Register<Vector3, Quaternion, int>("SpawnObject", RPC_SpawnObject);
}
```

**Order:** `s_instance` → `m_prefabs` loop → `m_nonNetViewPrefabs` loop → ZDOMan destroy-hook → routed RPC registration.

**Prerequisite ordering:** `ZDOMan.instance` and `ZRoutedRpc.instance` are already alive — both are constructed in `ZNet.Awake` (:67075–67080: `m_routedRpc = new ZRoutedRpc(m_isServer); m_zdoMan = new ZDOMan(m_zdoSectorsWidth);`). So by the time `ZNetScene.Awake` runs, **ZDO creation is fully live**.

Harmony hook points:
- **Prefix on `ZNetScene.Awake`** → append to `m_prefabs`; vanilla loop registers it. Simplest, one code path.
- **Postfix on `ZNetScene.Awake`** → must add to `m_prefabs` **and** `m_namedPrefabs` yourself (see §5).

---

## 3. Lookup API + hash derivation

```csharp
// :69702
public bool HasPrefab(int hash) => m_namedPrefabs.ContainsKey(hash);

// :69707
public GameObject GetPrefab(int hash)
{
    if (m_namedPrefabs.TryGetValue(hash, out var value)) return value;
    return null;
}

// :69716
public GameObject GetPrefab(string name) => GetPrefab(name.GetStableHashCode());

// :69721
public int GetPrefabHash(GameObject go) => go.name.GetStableHashCode();

// :70013
public List<string> GetPrefabNames()   // iterates m_namedPrefabs, returns .Value.name
```

**Hash is `GameObject.name.GetStableHashCode()` — the raw `.name` of the asset in the list, verbatim, no cleaning.** Awake (:69609, :69613), `GetPrefabHash` (:69723), and `ObjectDB.GetPrefabHash` (:90595 `prefab.name.GetStableHashCode()`) all agree.

`GetStableHashCode(string)` — **NOT FOUND in assembly_valheim**. It is an extension method in `assembly_utils.dll` (`C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed\assembly_utils.dll`, 198144 bytes). Do not reimplement it; call the game's own extension so hashes always match.

---

## 4. Missing / mismatched prefab on one side — exact failure paths

```csharp
// :69649
private bool IsPrefabZDOValid(ZDO zdo)
{
    int prefab = zdo.GetPrefab();
    if (prefab == 0) return false;
    return GetPrefab(prefab) != null;
}

// :69659
private GameObject CreateObject(ZDO zdo)
{
    int prefab = zdo.GetPrefab();
    if (prefab == 0) return null;
    GameObject prefab2 = GetPrefab(prefab);
    if (prefab2 == null) return null;          // <-- missing prefab bails here
    Vector3 position = zdo.GetPosition();
    Quaternion rotation = zdo.GetRotation();
    ZNetView.m_useInitZDO = true;
    ZNetView.m_initZDO = zdo;
    GameObject result = UnityEngine.Object.Instantiate(prefab2, position, rotation);
    if (ZNetView.m_initZDO != null)
    {
        ZDOID uid = zdo.m_uid;
        ZLog.LogWarning("ZDO " + uid.ToString() + " not used when creating object " + prefab2.name);
        ZNetView.m_initZDO = null;
    }
    ZNetView.m_useInitZDO = false;
    return result;
}
```

**The consequential branch — `CreateObjectsSorted` (:69790–69804):**

```csharp
if (CreateObject(item) != null)
{
    created++;
    if (created > num) break;
}
else if (ZNet.instance.IsServer())
{
    item.SetOwner(ZDOMan.GetSessionID());
    ZDOID uid = item.m_uid;
    ZLog.Log("Destroyed invalid predab ZDO:" + uid.ToString());
    ZDOMan.instance.DestroyZDO(item);
}
```

Identical logic in `CreateDistantObjects` (:69837–69843), which additionally logs `"  prefab hash:" + @object.GetPrefab()`.

| Scenario | Result |
|---|---|
| Prefab on **server**, missing on **client** | `CreateObject` returns null. Client is not server → **no destroy**. The ZDO is silently retried every `1/30f` sec (`Update` :69918 → `CreateDestroyObjects` :69929) forever. Invisible object, no crash, no log. |
| Prefab on **client**, missing on **server** (dedicated server without the mod, or mod load order differs) | Server's `CreateObjectsSorted`/`CreateDistantObjects` calls `SetOwner(GetSessionID())` then **`ZDOMan.instance.DestroyZDO(item)`** — the object is **permanently deleted from the world for everyone**. This is the mod-killing case. |
| Hash **mismatch** (e.g. `"MyThing(Clone)"` on one peer, `"MyThing"` on the other) | Byte-identical to "missing" — a different `int` key. Same two outcomes above. |
| Duplicate name added to `m_prefabs` | `Dictionary.Add` at :69609 throws `ArgumentException` **inside the foreach**, aborting `Awake` → `m_namedPrefabs` is left partially populated, ZDOMan hook and `SpawnObject` RPC never registered. Catastrophic; use `ContainsKey` guard. |
| `ZNetScene.RPC_SpawnObject` (:70023) with unknown hash | `ZLog.Log("Missing prefab " + prefabHash);` and nothing spawns. |
| `ZNetView.LoadFields` (:70386–70407) resolving a `GameObject`/`ItemDrop` field by name | `ZNetScene.instance.GetPrefab(value)` returns null → field silently left at prefab default. No error. |

Non-blockers confirmed: `IsAreaReady` (:69726) *skips* invalid-prefab ZDOs (`IsPrefabZDOValid(...) && !FindInstance(...)`), so a missing prefab does **not** hang zone loading.

---

## 5. Is there a `m_prefabs` → `m_namedPrefabs` sync method?

**NO. NOT FOUND.** There is no `AddPrefab`, `RegisterPrefab`, `UpdateRegisters`, or equivalent on `ZNetScene` (grep for `AddPrefab|RegisterPrefab` over the whole decompile: zero matches).

`m_prefabs` is referenced at exactly **two** lines in the entire assembly: the declaration (:69584) and the `Awake` loop (:69607). (`m_prefabs` at :95565+ is `SpawnSystem.m_prefabs`, `List<SpawnData>` — unrelated class.) `m_nonNetViewPrefabs`: :69586 and :69611 only.

Consequences:
- **Adding to `m_prefabs` after `Awake` has run does absolutely nothing at runtime.**
- `m_namedPrefabs` is the only runtime lookup table.
- **Add to both manually.** `m_namedPrefabs` for the game to work; `m_prefabs` for interoperability with other mods/console tools that enumerate it.

Contrast: `ObjectDB` *does* have a rebuild method — `private void UpdateRegisters()` (:90528) clears and refills `m_itemByHash` / `m_itemByData` from `m_items`, and is called from `Awake` (:90517) and `CopyOtherDB` (:90520). If your cloned prefab is an item, register it in **both** `ZNetScene.m_namedPrefabs` and `ObjectDB.m_items` + `UpdateRegisters()`, and note `ObjectDB.UpdateRegisters` also uses `Dictionary.Add` (:90534) → same duplicate-key throw.

---

## 6. `ZNetView` — fields and how a clone gets a valid prefab hash

```csharp
// :70199
public class ZNetView : MonoBehaviour, IReferenceHolder
{
    public const string CustomFieldsStr = "HasFields";   // :70201
    public static long Everybody = 0L;                   // :70203
    public bool m_persistent;                            // :70205
    public bool m_distant;                               // :70207
    public ZDO.ObjectType m_type;                        // :70209
    public bool m_syncInitialScale;                      // :70211
    public static bool m_useInitZDO = false;             // :70213
    public static ZDO m_initZDO = null;                  // :70215
    public static bool m_forceDisableInit = false;       // :70217
    private ZDO m_zdo;                                   // :70219
    private static bool m_ghostInit = false;             // :70229
```

`m_persistent`, `m_distant`, `m_type`, `m_syncInitialScale` are all **public instance fields** — settable on a clone with no publicizer.

`ZDO.ObjectType` (:62139): `public enum ObjectType : byte { Default, Prioritized, Solid, Terrain }`.

### `ZNetView.Awake` (:70233–70299) — the two paths

```csharp
private void Awake()
{
    if (m_forceDisableInit || ZDOMan.instance == null)
    {
        UnityEngine.Object.Destroy(this);   // destroys the ZNetView COMPONENT
        return;
    }
    m_body = GetComponent<Rigidbody>();
    if (m_useInitZDO && m_initZDO == null)
        ZLog.LogWarning("Double ZNetview when initializing object " + base.gameObject.name);
    if (m_initZDO != null)
    {
        // --- PATH A: spawned by ZNetScene.CreateObject from an existing ZDO ---
        m_zdo = m_initZDO;
        m_initZDO = null;
        if (m_zdo.Type != m_type && m_zdo.IsOwner())     m_zdo.SetType(m_type);
        if (m_zdo.Distant != m_distant && m_zdo.IsOwner()) m_zdo.SetDistant(m_distant);
        if (m_syncInitialScale) { /* s_scaleHash / s_scaleScalarHash -> transform.localScale */ }
        if ((bool)m_body) m_body.Sleep();
    }
    else
    {
        // --- PATH B: fresh Instantiate -> NEW ZDO IS CREATED HERE ---
        string prefabName = GetPrefabName();
        m_zdo = ZDOMan.instance.CreateNewZDO(base.transform.position, prefabName.GetStableHashCode());
        m_zdo.Persistent = m_persistent;
        m_zdo.Type       = m_type;
        m_zdo.Distant    = m_distant;
        m_zdo.SetPrefab(prefabName.GetStableHashCode());
        m_zdo.SetRotation(base.transform.rotation);
        if (m_syncInitialScale) SyncScale(skipOne: true);
        if (m_ghostInit) { m_ghost = true; return; }
    }
    LoadFields();
    ZNetScene.instance.AddInstance(m_zdo, this);
}
```

Notice: in Path A the ZDO's prefab hash is **never re-validated or rewritten** — it comes straight off the wire/save. Only `Type` and `Distant` are reconciled, and only if `IsOwner()`.

### How a cloned prefab's ZNetView gets a valid hash

```csharp
// :70414 — PRIVATE, needs the publicizer to call directly
private string GetPrefabName()
{
    return Utils.GetPrefabName(base.gameObject);
}
```

`Utils.GetPrefabName` — **NOT FOUND in assembly_valheim** (lives in `assembly_utils.dll`). The nearest in-assembly implementation of the same idea is `ItemDrop.GetPrefabName(string)` (:59236):

```csharp
private string GetPrefabName(string name)
{
    char[] anyOf = new char[2] { '(', ' ' };
    int num = name.IndexOfAny(anyOf);
    if (num >= 0) return name.Substring(0, num);
    return name;
}
```

i.e. the runtime name is truncated at the first `(` or space, stripping Unity's `" (Clone)"`. Do **not** rely on that behaviour to fix a bad name — vanilla explicitly renames its clone instead (`Player.SetupPlacementGhost`, :18504: `m_placementGhost.name = selectedPrefab.name;`).

**Rules for your cloned template:**
1. `clone.name` must contain **no `(` and no space** — otherwise `Utils.GetPrefabName` truncates it and the ZDO hash will not match the key you registered under.
2. Register under **exactly** `clone.name` : `m_namedPrefabs.Add(clone.name.GetStableHashCode(), clone)`. Never register under the source prefab's name.
3. `Object.Instantiate` always appends `" (Clone)"` — **assign `.name` immediately after instantiating**, before the object could ever spawn.

`ZDO` side (accessors are public):
```csharp
public void SetPrefab(int prefab)  // :62611  (bumps DataRevision if changed)
public int  GetPrefab()            // :62620
public ZDO CreateNewZDO(Vector3 position, int prefabHash)   // ZDOMan :65109
public bool Persistent { }         // :62163
public bool Distant    { }         // :62182
public ObjectType Type { }         // :62277
```

---

## 7. Making an `Instantiate`d clone inert at Awake time

At `ZNetScene.Awake` time `ZDOMan.instance` is **non-null** (proved by :69615 dereferencing it in the same method). Therefore a naive `Object.Instantiate(sourcePrefab)` in an Awake pre/postfix takes **Path B** above: it creates a real, replicated ZDO at the clone's transform position (world origin) and calls `ZNetScene.instance.AddInstance` — a ghost object spawned into the live world and synced to every peer.

### Vanilla mechanisms found

**(a) `ZNetView.m_forceDisableInit`** — the only vanilla "instantiate without networking" guard.
Used at :13357 (`Humanoid.PickupPrefab`), :18493 (`Player.SetupPlacementGhost`), :57755 & :57794 (`Inventory.AddItem`), :85261 (`FejdStartup.SetupCharacterPreview`). Pattern:
```csharp
ZNetView.m_forceDisableInit = true;
GameObject gameObject = UnityEngine.Object.Instantiate(itemPrefab);
ZNetView.m_forceDisableInit = false;
```
**Do NOT use this for a template.** :70237 executes `UnityEngine.Object.Destroy(this)` — it destroys the `ZNetView` **component** off the clone. Your template would be permanently ZNetView-less and could never spawn networked.

**(b) `ZNetView.StartGhostInit()` / `FinishGhostInit()`** (:70531 / :70536, both `public static`, backing field `private static bool m_ghostInit` :70229). Used by ZoneSystem at :98835/:98842, :99013/:99030, :99747/:99763, :99832/:99841 (e.g. `CreateLocationProxy` :99828). **Also NOT inert:** Path B still runs `ZDOMan.instance.CreateNewZDO(...)` at :70281 — a ZDO *is* created; only `LoadFields()` and `AddInstance()` are skipped. The `m_ghost` field (:70225) is written at :70293 and **never read anywhere** — dead. ZoneSystem destroys these ghosts afterwards.

**(c) A vanilla disabled-parent / template-container pattern: NOT FOUND.** No `SetActive(false)` container object exists in assembly_valheim for holding prefabs. Vanilla keeps location prefabs in separate additively-loaded scenes instead (`public List<string> m_locationScenes` :98106, `public List<GameObject> m_locationLists` :98108). `DontDestroyOnLoad` appears only at :50876, :76573, :79636, :79748, :80019, :83230, :88067 — all manager singletons, never prefab templates.

### The pattern you must use (Unity semantics, not a Valheim API)

Unity does not invoke `Awake` on components of a GameObject that is inside an inactive hierarchy. That, not any Valheim call, is what keeps a clone inert:

```csharp
// once, before/at ZNetScene.Awake
var container = new GameObject("YourMod_PrefabContainer");
container.SetActive(false);                       // MUST be before any Instantiate into it
UnityEngine.Object.DontDestroyOnLoad(container);  // survives scene changes

// per clone
var clone = UnityEngine.Object.Instantiate(sourcePrefab, container.transform);
clone.name = "YourThing";        // no '(' and no space; strips "(Clone)"

// then register (BOTH lists)
var zns = ZNetScene.instance;
int hash = clone.name.GetStableHashCode();
if (!zns.m_namedPrefabs.ContainsKey(hash)) {     // m_namedPrefabs needs the publicizer
    zns.m_prefabs.Add(clone);
    zns.m_namedPrefabs.Add(hash, clone);
}
```

`Instantiate(source, parent)` keeps local transform; the container being inactive means `ZNetView.Awake` never fires, so no ZDO, no `AddInstance`, no replication. When `ZNetScene.CreateObject` later instantiates it via :69675 `Instantiate(prefab2, position, rotation)` **without a parent**, the result is root-level and active, and Awake runs normally through Path A.

---

## 8. Related members that exist (verified)

| Signature | Line |
|---|---|
| `public void AddInstance(ZDO zdo, ZNetView nview)` | 69643 |
| `public void Destroy(GameObject go)` | 69686 |
| `public ZNetView FindInstance(ZDO zdo)` | 69890 |
| `public GameObject FindInstance(ZDOID id)` | 69904 |
| `public bool HaveInstance(ZDO zdo)` | 69899 |
| `public int NrOfInstances()` | 70002 |
| `public void SpawnObject(Vector3 pos, Quaternion rot, GameObject prefab)` | 70007 |
| `private void RPC_SpawnObject(long spawner, Vector3 pos, Quaternion rot, int prefabHash)` | 70023 |
| `public void Shutdown()` | 69629 |
| `public bool IsAreaReady(Vector3 point)` | 69726 |
| `public bool OutsideActiveArea(Vector3 point)` | 69964 |
| `ZNetView.StartGhostInit()` / `FinishGhostInit()` (public static) | 70531 / 70536 |
| `ZNetView.SetLocalScale(Vector3 scale)` | 70301 |
| `ZNetView.LoadFields()` (public) | 70341 |
| `ObjectDB.UpdateRegisters()` (**private**) | 90528 |
| `ObjectDB.CopyOtherDB(ObjectDB other)` (public) | 90520 |
| `Game.PortalPrefabHash { get; private set; }` → `List<int>`, filled in `Game.Awake` from `m_portalPrefabs` names | 85544 / 85553 |


## GOTCHAS
- m_namedPrefabs.Add at :69609 uses Dictionary.Add, not the indexer. A duplicate prefab name (your clone colliding with vanilla or another mod) throws ArgumentException INSIDE the foreach, aborting ZNetScene.Awake entirely -- so the ZDOMan.m_onZDODestroyed hook (:69616) and the 'SpawnObject' routed RPC (:69617) are never registered, and every later prefab in m_prefabs is unregistered. Always guard with HasPrefab(hash) / m_namedPrefabs.ContainsKey(hash) before adding.
- Adding to ZNetScene.m_prefabs AFTER Awake has run does nothing. m_prefabs is referenced at exactly two lines in the whole assembly (:69584 decl, :69607 Awake loop). m_namedPrefabs is the only runtime lookup. A postfix hook must write to m_namedPrefabs directly (private readonly, requires the publicizer).
- MULTIPLAYER KILLER: if the SERVER lacks a prefab a client created a ZDO for, ZNetScene.CreateObjectsSorted (:69798-69804) / CreateDistantObjects (:69837-69843) run SetOwner(ZDOMan.GetSessionID()) then ZDOMan.instance.DestroyZDO(zdo) with log 'Destroyed invalid predab ZDO:'. The object is PERMANENTLY deleted from the world for everyone. Client-side-only registration of a ZNetView-bearing prefab is unsafe on a dedicated server. Vanilla performs no prefab-table handshake at connect -- enforce mod presence via ServerSync version-check.
- Object.Instantiate at ZNetScene.Awake time is NOT inert. ZDOMan.instance is already non-null (ZNet.Awake :67075-67080 constructs it, and ZNetScene.Awake itself dereferences it at :69615), so ZNetView.Awake takes the else-branch at :70280-70285 and calls ZDOMan.instance.CreateNewZDO -- a real replicated object spawned at the clone's position (world origin). You must instantiate into an already-SetActive(false) parent so Awake never fires.
- ZNetView.m_forceDisableInit = true is NOT a template-safe guard: :70237 does UnityEngine.Object.Destroy(this), destroying the ZNetView COMPONENT off the clone. Fine for vanilla's throwaway item/ghost instances (:13357, :18493, :57755, :57794, :85261), fatal for a prefab you intend to register.
- ZNetView.StartGhostInit()/FinishGhostInit() (:70531/:70536) do NOT prevent ZDO creation. CreateNewZDO at :70281 still runs; only LoadFields() and ZNetScene.AddInstance() are skipped (:70291-70295). The m_ghost field (:70225) is written at :70293 and never read -- it is dead code. Do not use ghost-init to make a template.
- Unity appends ' (Clone)' to Instantiate results. ZNetView.GetPrefabName() (:70414) -> Utils.GetPrefabName(gameObject) truncates at the first '(' or space (cf. the identical ItemDrop.GetPrefabName at :59236-59245). If your registered key contains a '(' or a space, the runtime hash will silently differ from the registration key and every spawn will be a missing-prefab ZDO. Set clone.name explicitly right after Instantiate, exactly as vanilla does at :18504 (m_placementGhost.name = selectedPrefab.name).
- The container GameObject holding your clones needs UnityEngine.Object.DontDestroyOnLoad, otherwise a scene change destroys the clones while m_namedPrefabs (if you also survive) keeps fake-null Unity references -- GetPrefab returns a destroyed object, and Instantiate on it throws. Note ZNetScene.Awake runs fresh on every game-scene load, so your registration hook must be re-entrant.
- ZNetView.Awake Path A (:70245-70277) never re-validates or rewrites the ZDO's prefab hash from the wire -- only Type and Distant are reconciled, and only when IsOwner(). A hash mismatch between peers is therefore indistinguishable from a missing prefab; there is no self-healing.
- On a CLIENT the missing-prefab case is completely silent: CreateObject returns null (:69667-69670), the non-server branch does nothing, and CreateDestroyObjects retries every 1/30f second forever (Update :69918-69927). No log, no error -- just an invisible object. Do not expect a console message when debugging.
- ZNetView.LoadFields (:70386-70407) resolves GameObject and ItemDrop fields via ZNetScene.instance.GetPrefab(name); a null result silently leaves the field at its prefab default. Custom-field references to mod prefabs degrade with zero diagnostics.
- If the clone is an item, ZNetScene registration is not enough: ObjectDB.m_items must also contain it and ObjectDB.UpdateRegisters() (:90528, PRIVATE -- needs the publicizer or reflection) must rerun. It also uses Dictionary.Add at :90534, so the same duplicate-key throw applies. ObjectDB is rebuilt by CopyOtherDB (:90520) from FejdStartup.SetupObjectDB (:83621-83626), so re-register after that too.
- ZNetScene.GetPrefabName() (:70414) on ZNetView is private -- calling it directly relies on the publicized assembly. ZNetScene.GetPrefabHash(GameObject) (:69721) is public and does the same go.name.GetStableHashCode(); prefer it.
- GetStableHashCode is an extension method in assembly_utils.dll, not assembly_valheim. Reference assembly_utils.dll and call the game's implementation; a hand-rolled hash will not match and every ZDO will be orphaned.

## NOT FOUND
- ZNetScene.AddPrefab — NOT FOUND. Grep for 'AddPrefab|RegisterPrefab' over the entire decompile returns zero matches. There is no vanilla helper for adding a prefab at runtime.
- ZNetScene.RemovePrefab / UnregisterPrefab — NOT FOUND.
- ZNetScene.UpdateRegisters / RebuildNamedPrefabs / SyncPrefabs — NOT FOUND. No method re-derives m_namedPrefabs from m_prefabs. The only population site is the ZNetScene.Awake loop (:69607-69614).
- Any read of ZNetScene.m_prefabs outside Awake — NOT FOUND. Only :69584 (declaration) and :69607 (Awake). The m_prefabs at :95565+ is SpawnSystem.m_prefabs, List<SpawnData>, an unrelated class.
- Utils.GetPrefabName(GameObject) and Utils.GetPrefabName(string) — NOT FOUND in assembly_valheim.decompiled.cs (only call sites, e.g. :70416, :13744, :60347, :100585). Defined in assembly_utils.dll (Managed folder, 198144 bytes). Its exact body is UNVERIFIED here; ItemDrop.GetPrefabName(string) at :59236 is a separate, identically-shaped implementation.
- StringExtensionMethods.GetStableHashCode(string) body — NOT FOUND in assembly_valheim. Also in assembly_utils.dll. Do not reimplement.
- A vanilla disabled-parent / inactive template-container pattern for prefabs — NOT FOUND. No SetActive(false) GameObject is used anywhere in assembly_valheim to hold prefab templates. Vanilla uses separate additively-loaded scenes instead (ZoneSystem.m_locationScenes :98106, m_locationLists :98108). DontDestroyOnLoad appears only at :50876, :76573, :79636, :79748, :80019, :83230, :88067, all manager singletons.
- Any prefab-list or prefab-hash validation during the client/server handshake — NOT FOUND. No RPC compares prefab tables between peers; mismatches are only discovered lazily at CreateObject time.
- ZoneSystem.SetupLocations / PrepareNetViews / PrepareRandomSpawns — NOT FOUND (these are Jotunn-era or older-version names; they do not exist in this build).
- Callers of ZoneSystem.SetLoadingInZone (:100227) / UnsetLoadingInZone (:100240) — NOT FOUND anywhere in the assembly, so m_loadingObjectsInZones stays empty and IsZoneReadyForType (:100207) always returns true in practice. Missing prefabs therefore cannot stall zone readiness.
- ZNetView.m_ghost read site — NOT FOUND. Written at :70293, never read. Dead field.