# SnapToGround, and what remote zone loading does to it

Verified against `DECOMPILED ASSEMBLY VALHEIM/assembly_valheim.decompiled.cs` and
`WubarrksEye_Dumps/2026-07-31_21-06-43/Prefabs_Dump.json` on 2026-08-26, while root-causing
"the Bog Witch is buried under her hut" in TortalPortal.

Read this before shipping anything that loads, pokes, ghost-generates or instantiates world
away from a player. It is the reason a portal camera 5 km away could bury Haldor in front of
somebody standing next to him.

---

## 1. The component, and who has it

`SnapToGround` (decompile line 123663) is how a handful of objects find the ground. It is a
**global static list that is drained on call**:

```csharp
public class SnapToGround : MonoBehaviour
{
    public float m_offset;
    private static List<SnapToGround> m_allSnappers = new List<SnapToGround>();
    private bool m_inList;

    private void Awake()     { m_allSnappers.Add(this); m_inList = true; }
    private void OnDestroy() { if (m_inList) { m_allSnappers.Remove(this); m_inList = false; } }

    public void Snap()
    {
        if (ZoneSystem.instance == null) return;
        float groundHeight = ZoneSystem.instance.GetGroundHeight(transform.position);
        Vector3 position = transform.position;
        position.y = groundHeight + m_offset;
        transform.position = position;
        ZNetView c = GetComponent<ZNetView>();
        if (c != null && c.IsOwner()) c.GetZDO().SetPosition(position);   // persists AND replicates
    }

    public static void SnappAll()
    {
        if (m_allSnappers.Count == 0) return;
        Heightmap.ForceGenerateAll();
        foreach (SnapToGround s in m_allSnappers) { s.Snap(); s.m_inList = false; }
        m_allSnappers.Clear();
    }
}
```

**Exactly four prefabs in the game carry it**, and three are the traders:

| Prefab | Components |
|---|---|
| `Haldor` | Transform, CapsuleCollider, **Trader**, ZNetView, SnapToGround |
| `Hildir` | Transform, CapsuleCollider, **Trader**, ZNetView, SnapToGround |
| `BogWitch` | Transform, CapsuleCollider, **Trader**, ZNetView, SnapToGround |
| `dvergrprops_wood_stakewall` | ZNetView, SnapToGround |

**CORRECTED 2026-08-26.** An earlier revision of this sheet attributed the second `Snap()`
call site to `NpcTalk.Start()`. It is **`Trader.Start()`** (class opens at 125839, `Start()` at
125948, the `Snap()` at 125955) — `Trader` carries the same `m_randomGreets`/`m_standRange`
dialog fields as `NpcTalk`, which is what made them easy to confuse. The difference matters,
because it means a trader snaps **herself**:

```csharp
private void Start()          // Trader.Start
{
    m_animator = GetComponentInChildren<Animator>();
    m_lookAt = GetComponentInChildren<LookAt>();
    SnapToGround component = GetComponent<SnapToGround>();
    if ((bool)component) component.Snap();       // DIRECT — no ForceGenerateAll
    InvokeRepeating("RandomTalk", m_randomTalkInterval, m_randomTalkInterval);
}
```

So a trader is snapped **twice**:

1. **`Trader.Start()`**, one frame after instantiation, calling `Snap()` directly and bypassing
   `Heightmap.ForceGenerateAll()` entirely. Her zone is almost always still streaming at that
   moment, so **this first snap is very often wrong**.
2. **The next `SnappAll()` from anywhere in the world.** `Snap()` does not deregister her, so
   she is still in the list — and that pass *does* run `ForceGenerateAll()` first. **This is her
   correction, and she gets exactly one**, because the pass then clears the list.

> The failure is not one bad snap. It is a bad first snap whose single correction is spent on a
> drain that fires while the ground is still not right.

Get that one wrong and three things happen at once, and the third is the expensive one:

1. she is set to whatever the downward raycast hit — the raw world-gen surface, not the
   levelled pad her hut stands on;
2. she is removed from the list, so **nothing will ever snap her again**;
3. `Snap()` writes the wrong height **into her ZDO** if this peer owns the `ZNetView`, so it
   persists in the world file and replicates to every other player.

Symptom: trader standing inside the ground under her own hut, for everyone, until a zone
reload gives her another turn — which may or may not go better.

## 2. Who calls `SnappAll()`

Only two places in the whole assembly:

```
ZoneSystem.SpawnLocation   line 99298-99299, last two lines, in EVERY SpawnMode:
                               location.m_prefab.Release();
                               SnapToGround.SnappAll();
DungeonGenerator.Generate  lines 105344 and 105401
```

In an unmodded game the call that catches a trader is the tail of **her own camp's**
`SpawnLocation`, so the ground beneath her had just been built and the answer is right. That
is why this is a rare curiosity in vanilla rather than a constant.

## 3. The two doors a remote-loading mod opens

### Door A — first-time zone generation

```csharp
private bool PokeLocalZone(Vector2i zoneID)
{
    if (m_zones.TryGetValue(zoneID, out var value)) { value.m_ttl = 0f; return false; }
    SpawnMode mode = ((!ZNet.instance.IsServer() || IsZoneGenerated(zoneID)) ? SpawnMode.Client : SpawnMode.Full);
    if (SpawnZone(zoneID, mode, out var root)) { ...; return true; }
    return false;
}

private bool SpawnZone(Vector2i zoneID, SpawnMode mode, out GameObject root)
{
    ...
    if ((mode == SpawnMode.Ghost || mode == SpawnMode.Full) && !IsZoneGenerated(zoneID))
    {
        PlaceLocations(...);   // -> SpawnLocation -> SnappAll()
        PlaceVegetation(...);
        PlaceZoneCtrl(...);
        ...
        SetZoneGenerated(zoneID);
    }
    return true;
}
```

Reached by `PokeLocalZone` **only on a server, only for a never-generated zone**, and by any
direct `SpawnZone(z, SpawnMode.Ghost, ...)` call. `IsZoneGenerated` latches, so this door
closes permanently after a zone's first visit.

On a **client**, or on a server for an already-generated zone, the mode is `SpawnMode.Client`
and the whole block is skipped — `PokeLocalZone` itself never reaches `SnappAll`.

Guard: skip zones where `!ZoneSystem.instance.IsZoneGenerated(zone)` before poking, if your
feature does not need first-time generation.

### Door B — LocationProxy, and this one never closes

```
LocationProxy.Awake -> ZoneSystem.SpawnProxyLocation -> SpawnLocation(..., SpawnMode.Client, ...) -> SnappAll()
```

`LocationProxy` is an ordinary networked object created by `ZNetScene.CreateObjects` from a
ZDO that already exists. **`IsZoneGenerated` has no bearing on it** — and a `LocationProxy`
ZDO only exists in a zone that *has* been generated, so a Door-A guard points you straight at
Door B.

This door reopens on **every visit**: the proxies are destroyed when the area unloads and
re-created when you come back. Any mod that injects distant sectors into `ZNetScene.CreateObjects`
(a `CreateObjects` prefix appending `FindSectorObjects` results is the usual shape) fires one
`SnappAll` per far-off camp, ruin and dungeon it pulls in.

**This was the actual cause of the TortalPortal bug**, not Door A.

## 4. Why `Heightmap.ForceGenerateAll()` does not save you

```csharp
public static void ForceGenerateAll()
{
    foreach (Heightmap h in s_heightmaps)
        if (h.HaveQueuedRebuild()) { ZLog.Log("Force generating hmap " + ...); h.Regenerate(); }
}
```

It only completes **queued** rebuilds on heightmaps that **already exist**. A zone still
streaming in has neither, so `SnappAll` proceeds and `GetGroundHeight` raycasts whatever
surface happens to be there — typically the unmodified world-gen terrain, below the pad the
location's terrain edits will shortly raise.

Note also: it walks *every* heightmap in the scene and rebuilds mesh **and collider**
synchronously. An unintended `SnappAll` is a global frame hitch as well as a correctness risk.

`GetGroundHeight` on a total miss returns the point's own `y` unchanged (line 99434) — so a
missing heightmap leaves an object where it was rather than at zero. The damage comes from
hitting the *wrong* surface, not from hitting nothing.

## 5. Practical rules

1. If your feature loads world away from a player, assume you fire `SnappAll` unless you have
   traced both doors. Grepping your own code for `SnappAll` finds neither of them.
2. Door A: gate on `IsZoneGenerated` when you do not need first-time generation. Cheap
   (`m_generatedZones.Contains`) and it removes the need for a guard rather than adding one.
3. Door B: if you instantiate objects remotely, you need an actual guard — but **do not scope it
   to your own loading**. An earlier revision of this sheet recommended that, and it is the
   weaker idea: *"is this snapper inside the block I just built"* is only ever a proxy for
   *"is the ground under this snapper finished"*, and the second question can be asked directly.
   Asking it directly is simpler, needs no scope plumbing, and fixes the vanilla race and every
   other mod's version of it rather than only your own. A Harmony prefix on
   `SnapToGround.SnappAll` that snaps the snappers whose ground is there and leaves the rest
   **registered** rather than clearing the list. Reflection for `m_allSnappers` and `m_inList`
   (both private; publicized assemblies expose them but `AccessTools.Field` works either way).
   Bound the deferral — a snapper whose zone never loads must still be snapped eventually, or
   vanilla's "everything is placed by the next drain" guarantee becomes "maybe never".

3a. Also prefix `SnapToGround.Snap` to finish any **queued** heightmap rebuild before the raycast.
   A queued rebuild means the collider is still the surface from *before* that zone's terrain
   edits, which is exactly the wrong answer — and it is the only way to reach `Trader.Start()`'s
   direct call.

3b. Neither of those can see terrain that is present and settled when she is placed and is then
   **edited by a `TerrainComp` that had not streamed in yet**. Nothing detects that
   prospectively. It needs a watchdog after the fact: re-check for a while and correct if the
   object ends up under the ground. This is also the only thing that can reach a trader **already
   buried in a live world file** — that position is authoritative until something writes a better
   one. Claim the `ZNetView` before correcting, or `Snap()` will not persist it.

3c. The raycast mask is `LayerMask.GetMask("terrain")` — **terrain only**, never buildings or
   pieces. Two consequences: "buried" always means "snapped to a terrain surface lower than the
   finished one", never "snapped onto a roof"; and with no heightmap at all the raycast simply
   misses, `GetGroundHeight` returns the point's own `y`, and the object is left exactly where it
   was. **Not-ready is never worse than not-snapped**, which is what makes deferring safe.
4. **Fail open.** Any exception or renamed field should stand the guard down and hand `SnappAll`
   back to vanilla. An occasional sunken trader beats an object never placed on the ground.
5. A guard is subtractive, so two mods prefixing `SnappAll` is not a correctness problem —
   whichever runs first skips the drain, and one that is out of scope returns true and defers.
   The argument for sharing one is maintenance (both reflect into the same two private fields),
   not safety.
6. **A guard does not repair what is already buried.** The bad position is in the world file.
   Reloading the zone is the fix, and only once every client on the table is guarded — one
   unguarded client can write it straight back.

## 6. Reference implementation

`TortalPortal/SnapGuard.cs` and `TortalPortal/TraderRepair.cs` (v1.4.5) — the three parts above:
`Snap` prefix that finishes queued rebuilds, `SnappAll` prefix that defers rather than drains
(with a 10s ceiling), and a 30s post-spawn trader watchdog that repairs and persists. No scope
plumbing; fail-open stand-down throughout.

**Note for anyone holding a copy of the v1.4.4 file**: that version was scoped to the mod's own
remote loading and had a `BeginRemoteZoneWork`/`EndRemoteZoneWork` API. It is superseded by the
above, which is both simpler and strictly more effective. A verbatim copy of it is a fork, not a
consumer.

---

## 7. Who may PLACE a snapping object on a dedicated server (verified 2026-09-01, Njord 2.0.0)

Siting an object that carries `SnapToGround` is a **physical** act: it needs colliders to raycast
against and a heightmap that has finished rebuilding. Section 5 covers doing it correctly. This
covers **which machine is allowed to try**, which is a different question and the one that cost
Njord a day.

**A dedicated server is not that machine.** Per `VALHEIM-DEDICATED-SERVER-FACTS.md`, it has real
zones only around (0,0); everywhere else is ghost-generated ZDOs with no physics, no heightmaps,
and no `Start()`. So:

- **`ZNetScene.IsAreaReady(pos)` never returns true on a headless server** for a position outside
  the origin zones — not after an hour, and **not while a player is standing on the exact spot**.
  It asks whether every ZDO in that sector has a live instance *in this process*, and a headless
  server instantiates almost none. Njord polled it every 5s for nine minutes and logged
  "nobody has loaded the zone" while the player was on it. The player had loaded it. The server
  had not, and never would.
- `JOTUNN-AND-HEADLESS-AUTOMATION-FACTS.md` already notes `IsAreaReady` is necessary but not
  sufficient. Add to that: on a dedicated server it is not even *achievable*.

**The shape that works — the authority DECIDES, a machine with a BODY executes:**

1. Client detects the condition locally (see the trigger note below) and asks the server for a claim.
2. Server checks existence **by ZDO scan, never by looking for objects** — it has hardly any
   objects, so "I cannot see one" would mean "build another" — plus config and a claim timeout,
   and grants to exactly one machine.
3. Client sites the object and reports where it went.
4. Server records it, and on refusal tells the client to **remove what it made** — refusing the
   ledger write is not enough, because the object already exists by then.

Roles worth naming explicitly, because `IsServer()` conflates them:
`DedicatedServer` = authority, no body. `PlayerHost` (also plain singleplayer) = both.
`Client` = body, no authority.

### The trigger: do not hang it on a location prefab

The obvious move — hang a component on `StartTemple` so it wakes wherever the temple loads — is
what Mists of Avalor does, via Jotunn's `PrefabManager.GetPrefab` on
`ZoneManager.OnVanillaLocationsAvailable`. **The vanilla equivalent does not exist.** Location
prefabs live in `ZoneSystem.m_locations` behind a `SoftReference<GameObject>` loaded on demand;
they are **not** in `ZNetScene.m_prefabs`. `ZNetScene.GetPrefab("StartTemple")` returns null, the
component is never attached, and the feature silently does nothing — the same failure class as the
bug it was written to fix. Njord shipped that for one build.

Without Jotunn, state the condition directly instead: poll, on each client, for
`ZoneSystem.instance.IsZoneLoaded(pos)` plus `!Heightmap.FindHeightmap(pos).HaveQueuedRebuild()`.
That is exactly what siting needs, it is true precisely when somebody is standing there, and it
depends on no prefab table. It is also **observable** — a "still watching" line appearing in the
*client* log and not the server's is positive evidence the work moved, where a null prefab lookup
is invisible.
