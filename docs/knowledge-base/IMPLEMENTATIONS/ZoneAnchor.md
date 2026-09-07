# The Zone Anchor

**Keeping any patch of Valheim's world alive, loaded, and (optionally) visible — with no player anywhere near it.**

From TortalPortal v1.2.0, where it lets a camera photograph and live-stream portals nobody has ever
visited. The technique is general: anything you want loaded, simulated, watched, or rendered while
every player is elsewhere sits on top of these five mechanisms. Written to be lifted whole into any
BepInEx/Harmony mod. Redistribute freely — attribution to Wubarrk appreciated, not required.

Verified against the live Valheim build as of **August 2026**, using
`BepInEx.AssemblyPublicizer.MSBuild` on `assembly_valheim` (every "private" member named below is
called directly through publicizing, not reflection). Method names can drift across game patches —
re-verify against a current decompile before trusting a new game version.

---

## Why this is hard at all

Valheim's world only exists near players. Three separate systems enforce that independently, and an
anchor has to answer all of them:

| System | What it does | What happens elsewhere |
|---|---|---|
| `ZoneSystem` | Builds terrain (heightmap, water, locations) for zones near each reference position | No ground. Zones expire on a 4-second TTL |
| `ZNetScene` | Instantiates GameObjects from ZDOs in sectors near the reference position; **destroys everything else every tick** | No objects — and anything you spawn by hand is swept within 1/30 s |
| `ZDOMan` (server) | Sends each peer only the ZDOs around that peer's reported position | A client literally does not *have* the data for distant areas |

And two quieter ones:

| System | The trap |
|---|---|
| Zone generation | Vegetation and location ZDOs **only come into existence when a zone is first generated**. An unvisited zone has no trees to load, no matter how long you wait |
| `TerrainLod` | Distant mountains/coastlines are a low-LOD mesh carpet wrapped around the *player's* camera. Any other viewpoint sees black void past the loaded ring |

A "keep this area loaded" mod must therefore: build terrain (1), protect objects (2), obtain the
data (3), cause the content to exist (4), and — if anything renders the area — bring a horizon (5).

> ### ⚠️ And then a sixth thing that is not on this list, because it is not in the zone
>
> Do all five correctly and an anchored camera still photographs the place under **somebody else's
> sky**: no weather, no correct sun, no grass, no billboards. Valheim's sky, sun, cloud domes,
> ash/rain/snow particles and grass are **not zone contents** — they are one global kit welded to
> the local player's camera and teleported there every `LateUpdate`. No amount of extra anchoring
> produces them, which is exactly why it wastes a day.
>
> **[EnvironmentalControl.md](EnvironmentalControl.md)** is the companion document: which systems
> are per-zone and which are not, every main-camera read with line numbers, and the three ways to
> give an anchor its own presence layer — including the fact that `ClutterSystem` has a **poke
> primitive** in the same shape as `PokeLocalZone`.

---

## Mechanism 1 — Terrain: poke, don't patch

`ZoneSystem.PokeLocalZone(Vector2i)` is vanilla's own primitive and does exactly the right thing:
if the zone exists it resets its 4-second TTL and returns false; if not it builds it synchronously
and returns true. So the terrain half of an anchor needs **no Harmony patch at all** — a coroutine
that pokes the ring while the anchor is wanted:

```csharp
// Called every ~0.25s while the anchor is up. At most ONE new zone is built per call:
// SpawnZone is synchronous and a fresh zone can cost tens of ms, so the ring is spread
// across ticks instead of landing on a single frame. Pokes to existing zones are a
// dictionary lookup and a TTL reset.
public static void PokeZones(Vector3 anchorPos, int ring)
{
    Vector2i c = ZoneSystem.GetZone(anchorPos);
    for (int y = c.y - ring; y <= c.y + ring; y++)
        for (int x = c.x - ring; x <= c.x + ring; x++)
            if (ZoneSystem.instance.PokeLocalZone(new Vector2i(x, y)))
                return;   // one build per call — timeslice
}
```

**Cleanup is free.** Stop poking and the zones TTL out ~4 seconds later through
`ZoneSystem.UpdateTTL`, exactly as if a player had walked away. There is no teardown path to get
wrong — which is the design principle this whole technique keeps returning to.

On the **hosting player** (or singleplayer), `PokeLocalZone` on an ungenerated zone runs
`SpawnMode.Full` and generates it outright — vegetation ZDOs included. On a **pure client** it runs
`SpawnMode.Client`: terrain only, which is why mechanisms 3–4 exist.

## ⚠️ First-time generation can drain a global, unrelated static list

`SpawnZone`'s `SpawnMode.Full`/`Ghost` path also calls `PlaceLocations`, and placing a Location calls
`ZoneSystem.SpawnLocation`, whose last line is `SnapToGround.SnappAll()` — a **static, global** method
that drains and ground-snaps *every* `SnapToGround` component currently waiting anywhere in the loaded
world, not just anything in the zone you just poked. Exactly four vanilla prefabs carry
`SnapToGround`: the three traders (`Haldor`, `Hildir`, `BogWitch`) and `dvergrprops_wood_stakewall`.
None has its own re-snap path, so a trader gets exactly one chance — whichever `SnappAll()` fires first
after she's instantiated. If your anchor's poke fires `SnappAll()` while a trader elsewhere in the
world is mid-instantiation and hasn't been snapped yet, she gets ground-snapped against whatever height
happens to be under her at that arbitrary moment (possibly a zone still mid-generation) instead of her
hut's levelled pad — permanently, since she's removed from the snap list the instant it fires.
(Root-caused by `tortalportal-30`/TortalPortal, cross-session report 2026-08-26; confirmed live in
LetItGrow's away-tending anchor the same day.)

**The anchor never needs to trigger this.** Its whole job is re-loading ground that a player has
*already* visited — which is, by definition, already generated — not terraforming new ground nobody's
seen. So the fix is to simply never let the poke reach the first-time-generation path at all:

```csharp
for (int y = c.y - ring; y <= c.y + ring; y++)
    for (int x = c.x - ring; x <= c.x + ring; x++)
    {
        Vector2i candidate = new Vector2i(x, y);
        if (!ZoneSystem.instance.IsZoneGenerated(candidate)) continue;   // never force first-time gen
        if (ZoneSystem.instance.PokeLocalZone(candidate)) return;
    }
```

`IsZoneGenerated` is a plain `HashSet.Contains` check — free. This costs nothing for the anchor's
actual use case (an anchored piece's own zone, and any neighbor a player has ever walked through, are
already generated) and makes it structurally impossible for this mod's own code to reach
`SpawnLocation`/`SnappAll` at all — no prefix on `SnappAll` itself needed. If some *other* reason exists
to force-generate genuinely new ground from mod code, that code still needs its own guard (a Harmony
prefix limiting `SnappAll` to the caller's own pending snappers, or accepting the race) — this fix only
covers the "re-load already-explored ground" use case this document is about.

## Mechanism 2 — Objects: one prefix, both jobs

`ZNetScene.CreateDestroyObjects` builds a list of the sectors' ZDOs each tick, instantiates from it
(`CreateObjects`) and then destroys every instance *not* on it (`RemoveObjects`). The killer detail:
**both calls receive the same `List<ZDO>` instance.** So a single prefix that appends the anchor's
sectors both spawns the anchored objects *and* shields them from the sweeper:

```csharp
[HarmonyPatch(typeof(ZNetScene), "CreateObjects")]
[HarmonyPrefix]
static void Prefix(List<ZDO> currentNearObjects)   // parameter name must match vanilla
{
    if (!AnchorActive) return;
    Vector2i anchorZone = ZoneSystem.GetZone(AnchorPos);

    // If the anchor sits inside the player's own active area, vanilla is already carrying
    // these sectors — appending would double-list the same ZDOs.
    Vector2i refZone = ZoneSystem.GetZone(ZNet.instance.GetReferencePosition());
    int overlap = ZoneSystem.instance.m_activeArea + Ring;
    if (Mathf.Abs(anchorZone.x - refZone.x) <= overlap &&
        Mathf.Abs(anchorZone.y - refZone.y) <= overlap) return;

    // FindSectorObjects APPENDS (it never clears), which is exactly what we want here.
    ZDOMan.instance.FindSectorObjects(anchorZone, Ring, ZoneSystem.instance.m_activeDistantArea, currentNearObjects);
}
```

**Cleanup is free here too:** drop the anchor flag and the very next tick's `RemoveObjects` sweeps
the anchored instances through the ordinary vanilla path. Nothing to undo, no state to restore.
(`UpdateTTL` also refuses to expire a zone that still has instances in it — `HaveInstanceInSector` —
so the two mechanisms sequence themselves correctly: objects go first, zones follow.)

### ⚠️ 2026-09-01 correction — feed only zones whose terrain already exists

> ⚠️ **2026-09-05 correction to the correction:** the "loaded" test this section and the 2026-09-01
> addendum prescribe must be **"the zone's terrain exists"** (`ZoneSystem.m_zones.ContainsKey(zone)`),
> **never `ZoneSystem.IsZoneLoaded`**. `IsZoneLoaded` also goes false while a dungeon or delayed
> location is streaming its assets, which made the anchor destroy and re-create the whole zone every
> other tick and leaked the Wonderland server to an OOM kill three times. See the 2026-09-05 addendum.

The prefix above has a race that the LetItGrow 0.1.2 live-log review finally pinned. It appends the
WHOLE ring the first tick the anchor is active, while Mechanism 1 builds terrain at most one zone
per poke tick — so for a couple of seconds every visit, every object in the ring is instantiated
over **no ground**. Consequences seen in one day of the Wonderland server log: `TerrainComp.Awake`
finds no `Heightmap`, early-returns and stays inert for the visit ("Terrain compiler could not find
hmap" — AwayFromHome's guard suppressed **1,436,632** follow-up `TerrainComp.Load` null-derefs),
levelled/cultivated ground never applies server-side, HearthBelow's voxel loader waits for a
heightmap that never comes ("Giving up applying voxel data … heightmap never became ready", exactly
on the anchored zones), and any repair path that re-wakes the compiler is itself hazardous (fact 25
in `JOTUNN-AND-HEADLESS-AUTOMATION-FACTS.md`). Vanilla never produces this ordering around a
player: the zone spawns first, its ZDOs stream in after. Restore that order:

```csharp
// Zone by zone, only zones Mechanism 1 has already built. area 0 + distant 0 = exactly that
// sector. FindSectorObjects still appends, so the shielding half of the trick is unchanged.
for (int y = anchorZone.y - Ring; y <= anchorZone.y + Ring; y++)
    for (int x = anchorZone.x - Ring; x <= anchorZone.x + Ring; x++)
    {
        Vector2i zone = new Vector2i(x, y);
        if (!ZoneSystem.instance.IsZoneLoaded(zone)) continue;
        ZDOMan.instance.FindSectorObjects(zone, 0, 0, currentNearObjects);
    }
```

`IsZoneLoaded` is `m_zones.ContainsKey` and `PokeLocalZone` adds the key synchronously after
`SpawnZone` (heightmap component included), so an object is never instantiated ahead of its ground.
A zone the poke will never build (ungenerated — see the first-time-generation warning above) simply
contributes nothing, which beats a zone full of floating objects. This also drops the distant band:
it was instantiating far rocks and trees as *near* objects, in zones with no terrain, for nothing an
anchor's job needs. The "distant ring" section below still applies to anyone who *wants* distant
objects — but the anchor-for-automation case doesn't. (`LetItGrow/Farming/ZoneAnchor.cs`;
AwayFromHome's copy still has the whole-ring shape as of 1.0.2 — flagged to that session.)

## Mechanism 3 — Data: the sector subscription (dedicated servers)

A client of a dedicated server is only ever *sent* the ZDOs around its own reported position — the
far sectors it wants to anchor are simply absent from its `ZDOMan`. The server-side half is a
renewal-driven subscription:

**The RPC.** A routed RPC (`ZRoutedRpc.instance.Register<Vector3>(...)` at `ZNet.Start` time;
clients invoke the targetless `InvokeRoutedRPC(name, pos)` overload, which routes to the server).
One `Vector3` argument: the anchor position, or `Vector3.zero` to release. The server stores
`sender → (pos, expires)` where `expires` is ~15 s out; the client re-sends every ~5 s while it
wants the world. **A client that crashes, disconnects, or just stops asking costs the server
nothing within seconds, and there is no cleanup handshake to get wrong.**

**The send-list patch.** The server builds each peer's outgoing ZDO list in
`ZDOMan.CreateSyncList(ZDOPeer peer, List<ZDO> toSync)`. A postfix appends the subscribed sectors:

```csharp
[HarmonyPatch(typeof(ZDOMan), "CreateSyncList")]
[HarmonyPostfix]
static void Postfix(ZDOMan __instance, ZDOMan.ZDOPeer peer, List<ZDO> toSync)
{
    if (!Subs.TryGetValue(peer.m_peer.m_uid, out var sub)) return;
    if (Time.realtimeSinceStartup > sub.Expires) { Subs.Remove(peer.m_peer.m_uid); return; }
    // (same overlap guard as mechanism 2, vs the peer's own refpos)

    temp.Clear();
    __instance.FindSectorObjects(ZoneSystem.GetZone(sub.Pos), Ring, ZoneSystem.instance.m_activeDistantArea, temp);
    foreach (ZDO zdo in temp)
        if (peer.ShouldSend(zdo)) toSync.Add(zdo);
}
```

`ZDOPeer.ShouldSend` is per-peer revision tracking and it is what makes this cheap: **each object
crosses the wire once, then only when it changes** — bandwidth-wise the subscription is
indistinguishable from the peer standing there. (`ZDOMan.ZDOPeer` is a private nested class;
publicizing makes it a legal parameter type.)

**Authority lives here, not in the client.** The handler refuses subscriptions when the feature is
disabled server-side and refuses NaN/Infinity positions outright — a client with a doctored config
gets silence, not sectors.

## Mechanism 4 — Existence: ghost-generate on demand

If nobody has ever stood in a zone, the server has never *generated* it: there are no vegetation or
location ZDOs to send. The subscription faithfully delivers bald terrain, and no amount of waiting
fixes it — the content doesn't exist. The answer is vanilla's own: **ghost generation**, the mode
the server already runs around every connected peer. While a subscription is live, the same
`CreateSyncList` postfix (before the `FindSectorObjects` sweep) runs:

```csharp
// One zone per send tick — SpawnZone is synchronous. Newly created ZDOs join the very
// same FindSectorObjects sweep below, so they flow out on the subscription immediately.
for each zone z in ring:
    if (!ZoneSystem.instance.IsZoneGenerated(z) &&
        ZoneSystem.instance.SpawnZone(z, ZoneSystem.SpawnMode.Ghost, out _))
        break;
```

Ghost mode creates the zone's ZDOs without keeping instances. Note the one permanent footprint of
the whole technique: **generation is a one-time act, saved to the world file** — anchoring a virgin
zone "explores" it forever, exactly as a sailing player would have.

## Mechanism 5 — The horizon (only if you render)

Anything that *draws* the anchored area — a snapshot camera, a live feed — sees black void past the
loaded ring, because distant terrain is a `TerrainLod` mesh carpet that follows the player's camera.
Give the anchor its own:

- **Build it fresh; do not `Instantiate` the scene's.** The LOD meshes are *children* of the
  component's GameObject, and its `m_heightmaps` list is non-serialized — a clone brings nine
  orphaned mesh copies and then builds nine more.
- Create an **inactive** GameObject, `AddComponent<TerrainLod>()`, copy the five serialized fields
  (`m_terrainSize`, `m_regionsPerAxis`, `m_vertexDistance`, `m_material`, `m_updateStepDistance`)
  from the scene's instance, **then** activate — `OnEnable` is what builds the meshes, and it must
  run with the real material or you get nine invisible ones.
- Pin it: `m_updateStepDistance = float.MaxValue` (never re-follow the player's camera),
  `m_lastPoint = <anchor, grid-snapped to m_vertexDistance>`, every `HeightmapWithOffset.m_state =
  NeedsRebuild`, `m_heightmapState = NeedsRebuild`. Its own `Update` then queues the bakes on the
  `HeightmapBuilder` worker thread and flips `m_heightmapState` to `Done` when the carpet is laid.
- Destroy the GameObject on teardown; `OnDisable → ResetMeshes` cleans up its children.

The player's own horizon is never touched — the two rigs coexist (heights come from the same
`WorldGenerator`, so overlap renders the same way vanilla's own LOD-under-real-terrain overlap does).

---

## ⚠️ The distant ring — a bug this document shipped with

Both sweeps above pass a **distant ring** as `FindSectorObjects`' third argument. TortalPortal
passed **`0`** there for four releases, and the earlier revision of this file recorded that as
correct. It is not, and the failure is silent.

```csharp
// ZDOMan.FindSectorObjects, abridged
for (int l = area + 1; l <= area + distantArea; l++)   // distantArea = 0  ->  loop body never runs
    FindDistantObjects(...);
```

`0` does not mean "a modest distance". The loop runs from `area + 1` to `area + 0`, so **not one
distant object is ever requested.** Everything within the near ring arrives perfectly, which is
what makes it so hard to spot: the anchor looks like it works. What you lose is the far scenery —
and on a dedicated server you lose it *twice*, because the client holds nothing it was not sent and
the server-side sweep was passing `0` as well.

Symptom to recognise: **a destination that is fully loaded, correctly lit, correctly weathered, and
still reads as an empty stage set.** Pass `ZoneSystem.instance.m_activeDistantArea` — the same ring
vanilla gives the player's own camera — in both places.

---

## Mechanism 6 — Presence: convincing the game somebody is there

Mechanisms 1–5 make an area *exist and draw*. They do not make it **inhabited**, because Valheim
gates creature spawning on a `Player`, not on a loaded zone. `SpawnSystem` is the whole story, and
the gate is only four lines deep:

```csharp
UpdateSpawning()        // requires m_nview.IsValid() && m_nview.IsOwner() && Player.m_localPlayer != null
GetPlayersInZone(list)  // Player.GetAllPlayers() filtered by InsideZone(pos)  -> +-32m of the zone centre
if (list.Count == 0) return;                    // <- the actual blocker at an anchor
FindBaseSpawnPoint(spawn, list, out centre, ..) // dart 40-80m around one of those players
```

Everything downstream is player-free: `IsSpawnPointGood` tests biome, biome area, blocked-ness,
altitude and tilt, and nothing else. So **a presence in the zone is the entire feature.**

Two ways to supply one, and only one of them is sane:

- **Do not fabricate a `Player`.** It needs a `ZNetView` to survive its own `Awake`, and a
  networked `Player` object is replicated — every other client gets a ghost standing in the
  Ashlands. This is a trap, not a shortcut.
- **Patch the two methods that ask.** A postfix on `GetPlayersInZone` that adds the local player
  *only when the list came back empty* (a real player standing there is already the right answer),
  and a prefix on `FindBaseSpawnPoint` that runs vanilla's own twenty-dart loop with the anchor as
  the centre. Without the second, the first is useless: vanilla would throw darts around the
  viewer's real position on the far side of the world and `IsSpawnPointGood` would reject all
  twenty for being the wrong biome.

**Ownership is the other half.** `UpdateSpawning` runs only for the owner of the zone-control
object, which out at an anchor is the server or nobody. A prefix that calls `nview.ClaimOwnership()`
for anchored zones is enough — and note what it means: the watching client becomes the simulator
for a place it is not standing in. See the physics warning under *Adapting it* before claiming
anything with a `Rigidbody`.

**And the warm-up is the third half nobody budgets for.** `SpawnSystem.Awake` starts its clock with
`InvokeRepeating("UpdateSpawning", 10f, 1f)` — ten seconds before the FIRST attempt — and the
anchored zone's control object is created fresh every time the anchor raises the zone, so that
clock restarts on every glance. A view held open under ten seconds (most views) never runs one
spawn attempt, however correct the patches. Drive the (patched) method directly at ~1s intervals
while the view is live: that skips only the warm-up, never the rules — the per-spawner timestamps
on the zone ZDO already cap spawns per elapsed second, so extra calls can never spawn faster than
vanilla would.

> **This is where a rendering feature becomes a gameplay feature.** The creatures are real. They
> are written to the world, they outlive the view that created them, and one player browsing an
> album can seed a dozen neighbourhoods. TortalPortal ships it **off by default**, server-synced,
> and refuses to do it for the *unattended* background photographer — only for a view a human
> actually has open. Restrict the spawn ring too: the anchor's ring dial goes to 2 (25 zones), and
> stocking all of those from a glance at a photograph is not a defensible default. Ring 1 matches
> vanilla's own `m_activeArea` and is already wider than a camera sees.

---

## Knowing when the area is actually ready

`ZNetScene.IsAreaReady(pos)` is vanilla's readiness test (zone loaded + every near ZDO instanced)
— **and it lies on a dedicated client**, because it only judges the ZDOs that have *arrived so
far*. Mid-stream, a half-empty area reads as complete. The settle test that actually works:

```
settled ⇔ for N consecutive polls (6 × 0.25s worked well):
    sector ZDO count > 0 and UNCHANGED since last poll     // nothing still in flight
    && ZNetScene.IsAreaReady(pos)                          // everything known is standing
    && !Heightmap.HaveQueuedRebuild(pos, ~100m)            // terrain meshes done (TerrainComp
                                                           //   edits arrive late and queue rebuilds)
    && terrainLodRig == null || rig.m_heightmapState == Done
```

For a **permanent record** (a stored photograph), never act on an unsettled scene — skip and retry,
because a bad artifact stands forever while a missing one heals. For a **live view**, going ahead
at budget exhaustion is fine — the scene finishes loading in front of the viewer.

---

## Lifecycle: the parts that make it failsafe

These patterns are why the anchor can't leak, and they transfer to any use of the technique:

1. **`try/finally` is the BACKUP, not the teardown.** The coroutine that raises the anchor drops
   it in `finally` — but that covers only the paths where the iterator genuinely finishes: the
   deadman firing, the world unloading, a `yield break`. **Unity's `StopCoroutine` does NOT
   dispose the iterator — it only unschedules it, and a `finally` that was never entered never
   runs.** An earlier revision of this document claimed the opposite, and TortalPortal shipped a
   release on that claim; the cost was its flagship bug — every feed stopped explicitly leaked
   the anchor standing, and every later feed waited on the corpse forever. One live view per
   visit, silently. The rule that survives: teardown is an ordinary method, **idempotent**,
   reading statics rather than coroutine locals, called explicitly by whatever stops the routine
   *and* from the `finally` — whichever fires second finds nothing left to do. And a consumer
   that finds the single anchor already held should treat a hold that outlives its owner's
   stand-down window (counted in FRAMES — see below) as a leak to clear, not a queue to wait in.
2. **Renewals, not open/close.** Anything held across a network (the subscription) expires on its
   own unless re-asked-for. Crash-safety without a cleanup protocol.
3. **Deadman switches count FRAMES, not seconds.** The anchor's own zone loading hitches the
   client. A wall-clock deadman reads its own hitch as abandonment and tears the feed down —
   unload, restart, hitch, repeat, forever. During a hitch neither ticks *nor the frame counter*
   advance, so `Time.frameCount - lastTickFrame > 3` survives any stall while still ending an
   abandoned hold within ~50 ms.
4. **One anchor, polite consumers.** Background work checks a `LiveThing.Active` flag before
   starting; the live thing waits out an in-flight background hold. No queue, no lock — two
   booleans.
5. **Every dial is clamped and NaN-proofed at the read.** `AcceptableValueRange` guards the UI, not
   a hand-edited file; `Mathf.Clamp(NaN)` is still NaN and a NaN deadline is an infinite loop. Read
   through accessors that reject NaN/Infinity and clamp to a hard range (ring capped at 2 — 5×5
   zones; a wider ring is a guaranteed hitch on any hardware).
6. **Authority is server-side.** The server refuses subscriptions when the feature is off and
   refuses poisoned positions. Synced config is a convenience; the refusal is the guarantee.
7. **Narrate at Info.** The whole feature is invisible work far from any player; a silent success
   is indistinguishable from a pass that never ran, and that ambiguity costs debugging sessions.

---

## Adapting it: "run things while I'm away"

> **This section now has a full companion document: [AwayFromHome.md](AwayFromHome.md)** — the
> farming/breeding/taming design in detail, including the decompile-verified clock table
> (which progressions are timestamps that accrue unloaded vs tick-accruals that need loaded+owned
> time), the `ReleaseNearbyZDOS` analysis that proves far claims are never auto-stripped, the
> claim-only-unowned arbitration with real players, and why a ranch keeper deliberately OMITS the
> presence patches. What follows here is the short general form.

The anchor as shipped loads and *renders*; making the area **tick** (smelters smelting, kilns
burning, crops visible-growing, spawners spawning) needs one more ingredient: **ownership.**

> **Update:** spawners specifically are now solved and shipping — see *Mechanism 6*. Ownership was
> only half of it; the other half was that `SpawnSystem` asks for a **`Player`**, not for a loaded
> zone, and no amount of claiming makes an empty `GetPlayersInZone` non-empty. Read that section
> before building a keep-alive mod on this: the gate list there is the general shape of the problem
> ("what does the system actually ask for, and is it a player or a position?"), and other systems
> — `SpawnArea`, `RandEventSystem`, `MonsterAI` wake-up ranges — are gated the same way and are
> *not* yet done.

- Objects instantiated in an anchored zone are **unowned** — vanilla assigns simulation ownership
  by peer proximity (`ZDOMan.ReleaseNearbyZDOS`), and no peer is near. Unowned = rendered but
  frozen: no AI, no machine ticks.
- A keep-alive mod claims ownership for the anchoring client:
  `zdo.SetOwner(ZDOMan.GetSessionID())` across the anchored sectors once **settled** (see below),
  and simply stops claiming on teardown — vanilla reassigns or idles them exactly as it does when a
  player leaves an area.
- **Claim only after settle, and beware physics.** An owned `Rigidbody` on terrain that has not
  finished building falls through the world and — because `ZSyncTransform` writes the owner's
  transform into the ZDO every frame — *saves itself falling*. (This exact class of bug destroyed a
  player inventory elsewhere; treat "settled before owned" as a hard rule.)
- **Claim pieces, plants, pickables and drops — never creatures, unless simulating them IS the
  job.** `OutsideActiveArea` gates only `WearNTear`, `SpawnArea` and `StaticPhysics`; a creature you
  own runs its full `MonsterAI` for the whole hold, with nobody there, and will happily chew on the
  site's pieces (a chest is 100 HP). Skip any prefab with a `Character` component; unowned
  creatures stay frozen, exactly as vanilla leaves them. (Fact 26; LetItGrow 0.1.2, 2026-09-01.)
- Know what actually needs it: much of Valheim catches up from timestamps on load (plant growth,
  fermenter, partially smelters). Keep-alive buys you the things that genuinely only run while
  loaded — active spawners, windmill/production in real time, wards, live observation — not the
  things the game already fakes retroactively.
- A **rendering client must exist for pictures only.** Pure keep-alive needs no camera, no horizon
  rig, and would even run on a modded *host* with no players in the area at all (mechanisms 1–2
  alone, since the host holds every ZDO).

## Cost model

One ring-1 anchor ≈ one extra player's worth of streaming and instancing, held only while wanted,
gone within ~5 seconds of release (one `RemoveObjects` tick + the 4 s zone TTL). Ghost generation
adds a one-time world-file growth per virgin zone. The subscription adds one full-sector send per
subscribe, then deltas. Budget accordingly: the shipped mod holds **one** anchor at a time, ever.

---

## Real ports, and what shrinks

Two consumers so far, at genuinely different scales — useful data for whoever ports this next.

- **AwayFromHome** (`AwayFromHome/ZoneAnchor.cs` + siblings) — the full shape: mechanisms 1-3 for a
  dedicated-server-reachable ranch, plus livestock pinning, per-player site caps, and
  `ProductionCatchUp` for the world-clock-freeze problem (see that file's own header — a SEPARATE
  issue from zone-loading, specific to anything keyed to `ZNet.m_netTime`, e.g. `Smelter`).
- **LetItGrow** (`Let It Grow/Farming/ZoneAnchor.cs` + `OwnershipClaim.cs`/`SiteSettle.cs`/
  `ScarecrowKeeper.cs`) — mechanisms 1-2 only, ported 2026-08-24 to let a Scarecrow's own
  `InvokeRepeating` harvest/replant tick fire on farms nobody is standing near. Deliberately dropped
  relative to AwayFromHome:
  - **No SectorSubscription (mechanism 3).** Only the server ever runs the rotation (clients stand
    down, exactly matching AwayFromHome's own final "only the server tends" conclusion after its
    v1.0.0 dual-tending bug) — and the server already holds every ZDO in the world regardless of
    which zones are loaded, so there is nothing a subscription would add here.
  - **No ProductionCatchUp equivalent.** `ScarecrowController.Tick` runs on Unity's real frame time
    via `InvokeRepeating`, not Valheim's freezable world clock — it just needs somewhere to run and
    long enough to do it once, not a credited-seconds correction.
    **Correction (2026-08-26): dodging the CREDIT problem did not dodge the CLOCK problem.** The
    tick fired flawlessly on every visit and nothing ever grew: plant maturity is measured against
    `ZNet.m_netTime`, which **does not advance while the server has zero players** — an empty
    server's crops are frozen at seconds old forever, no matter how many visits the anchor makes.
    See **[WorldClock.md](WorldClock.md)** for the decompile-verified mechanism and the ten-line
    `UpdateNetTime` postfix ("Time Flows While Empty") that fixes it. This applies to ANY anchor
    consumer whose payload is keyed to world time — including AwayFromHome's own away-production,
    whose `ProductionCatchUp` can only credit time the clock actually recorded.
  - **Keeper PARKING (2026-08-26), because a dedicated server never instantiates the world around
    its peers.** The rotation's original assumption — "a Scarecrow near a connected player ticks on
    its own via the normal loaded-zone path" — is listen-server-only thinking. On a dedicated
    server the nearby CLIENT simulates the zone, and a server-authoritative mod's logic stands down
    there by design, so the server-side component only *exists* while the anchor holds its zone:
    a watched farm ran ~15s out of every ~2min, observed live as "the field never fills while I
    stand here." Fix: `ScarecrowKeeper.HoldSite` now holds the anchor for as long as any peer is
    within 64m (capped per stay so a multi-farm rotation still laps). AwayFromHome does NOT need
    this — livestock are vanilla components the nearby client simulates natively; parking is only
    for logic that runs *exclusively server-side*.
  - **Ownership near players is a 2-second treadmill (2026-08-26) — a nuance to AwayFromHome.md's
    "far claims are never auto-stripped."** True far away; near a player it inverts: ownership
    assignment is fully centralized (`ZDOMan.Update` runs `ReleaseZDOS` only when `IsServer()`),
    and every 2s the server hands each ZDO near a player to that player unless the current owner's
    active area covers the sector — the server's own reference position never does, so a server
    claim on a watched object is stolen back within 2s of every reclaim, and claim-then-act-next-
    tick loops never catch an owning tick. Fixes, all field-verified in LetItGrow: pin the prefab
    server-owned with a scoped `ZDO.SetOwner` prefix (`Patches/ZdoOwnershipPatches.cs`) for objects
    whose ZDO the server must write; claim-and-act in the SAME tick for transient objects (drops);
    and **never pin a Container** — vanilla's chest-open grant hands ownership to the opener
    (`Container.RPC_RequestOpen` ends in `SetOwner(uid)`), so a pinned chest simply refuses to open
    (live-verified in 0.0.10, reverted in 0.0.11). Containers get claim-at-write-time instead,
    skipping any silo whose ZDO `s_inUse` flag says a player is browsing it.
    **Addendum (2026-08-27): claim-at-write-time is only HALF the container discipline — reload
    first or you destroy deposits.** A `Container`'s `Inventory` is a cache of the ZDO's "items"
    payload that `GetInventory()` never refreshes, and only the owner's saves replicate. So
    claiming and writing through the stale cache saved PRE-deposit contents over a player's fresh
    deposit (field report: 70 Barley erased, no error anywhere), and the unclaimed withdrawal
    path had the mirror bug (debits that never replicated, items resurrecting on reload). The
    full ritual for every shared-container touch, reads included: reload iff `m_lastRevision !=
    zdo.DataRevision` (vanilla's own `CheckForChanges` gate) → skip if `s_inUse` → claim →
    mutate. This also RESOLVES the "open question" noted two bullets down about whether
    non-owner Container writes persist: they do NOT — see fact 24 and
    **`ContainerInventoryGuard.md`** (fixed in LetItGrow 0.1.1, conservation soak-verified).
  - **No livestock pinning, no per-player site caps, no piece-count "mark" history in the settle
    check** (`SiteSettle.cs` vs. `SiteCensus.cs`) — a Scarecrow's "site" is one piece plus an optional
    linked container, not an arbitrary player-built pen, so "is the Scarecrow's own instance built"
    is a sufficient presence test on its own.
  - **`OwnershipClaim.ClaimPass` kept generic** (claim every unowned/abandoned persistent ZDO in the
    ring) rather than naming the Scarecrow/Silo specifically — cheaper to write, and it also derisks
    an unrelated open question (whether writing to a Container's inventory the calling peer doesn't
    own actually persists/replicates correctly) for free, by simply never leaving it unowned during a
    visit.
  - **Added `Farming/SnapGuard.cs` (2026-08-26)**, ported directly from `TortalPortal/SnapGuard.cs` —
    see `libs-Tools/SNAPTOGROUND-AND-REMOTE-ZONE-LOADING-FACTS.md`. `ZoneAnchor.PokeZones` already
    skips zones that were never generated (closes "Door A"), but its `CreateObjects` prefix still
    injects any `LocationProxy` ZDO already sitting in the ring, and `LocationProxy.Awake` reaches
    `SnapToGround.SnappAll()` regardless of `IsZoneGenerated` ("Door B" — cross-session root-cause from
    `tortalportal-30`). A farm sitting in explored territory has real odds of a camp/ruin landing in a
    5x5 ring, unlike a portal dropped in wilderness, so this one mattered here too, not just for
    TortalPortal.
    **Correction, same day**: the first port above only patched `SnappAll`, scoped to `ZoneAnchor`'s
    ring. `tortalportal-30` caught that `Trader.Start()` calls `Snap()` **directly**, bypassing
    `SnappAll` (and `ForceGenerateAll`) entirely — verified against the decompile — so the scoped guard
    only protected a trader's *second* chance, not her first (and worse) one. Replaced with the
    complete, unscoped port from `TortalPortal/SnapGuard.cs` v1.4.5 (a `Snap` prefix + ground-readiness
    check on every snapper anywhere, deferred rather than dropped) plus new `Farming/TraderRepair.cs`
    (30s post-spawn watchdog, ported from `TortalPortal/TraderRepair.cs` v1.4.5 — the only mechanism
    that can also repair a trader already buried in an existing save).

    **Fork warning, not a shared consumer**: `LetItGrow/Farming/SnapGuard.cs`/`TraderRepair.cs` are
    verbatim-derived *copies* of TortalPortal's v1.4.5 files — NOT wired to any shared file. A
    single-canonical-file-in-`libs-Tools` plan was in flight when the first port happened, then paused
    (2026-08-26, both `TortalPortal` and `AwayFromHome` held on the same basis) with a scope-API shape
    that does **not** match what got ported here. If that plan resumes, treat LetItGrow's copies as
    forks needing reconciliation, not as an existing consumer of the eventual shared file.

  The takeaway for a third port: mechanisms 1-2 (poke + the `CreateObjects` prefix) are the load-
  bearing minimum for "make an owned MonoBehaviour's own tick fire somewhere nobody is standing."
  Everything else in AwayFromHome's version is there to solve a *specific* problem (dedicated-server
  client reach, a frozen world clock, wandering livestock) that doesn't automatically apply just
  because a mod wants "something to keep working while the player is elsewhere" — check which of
  those problems your own consumer actually has before copying the parts that solve them.

---

## Hook-point summary (for porting)

| Purpose | Hook | Kind |
|---|---|---|
| Terrain alive | `ZoneSystem.PokeLocalZone(Vector2i)` | direct call (publicized), coroutine-driven |
| Objects alive | `ZNetScene.CreateObjects(List<ZDO>, List<ZDO>)` | Harmony **prefix**, append to first list |
| Data to client | `ZDOMan.CreateSyncList(ZDOPeer, List<ZDO>)` | Harmony **postfix** on server, append via `ShouldSend` |
| Content exists | `ZoneSystem.SpawnZone(z, SpawnMode.Ghost, out _)` | direct call, server, one per send tick |
| Distant scenery | third arg of `FindSectorObjects` = `m_activeDistantArea`, **never 0** | see the warning above |
| Horizon | `TerrainLod` built fresh + pinned | scene object, no patch |
| Somebody is here | `SpawnSystem.GetPlayersInZone` | Harmony **postfix**, add local player only if empty |
| Where they are | `SpawnSystem.FindBaseSpawnPoint` | Harmony **prefix**, re-run vanilla's loop around the anchor |
| Zone simulates | `SpawnSystem.UpdateSpawning` | Harmony **prefix**, `nview.ClaimOwnership()` |
| Readiness | `ZNetScene.IsAreaReady` + `Heightmap.HaveQueuedRebuild` + count stability | direct calls |
| Transport | `ZRoutedRpc.Register<Vector3>` / targetless `InvokeRoutedRPC` | vanilla RPC layer |

Reference implementation: `SnapshotAnchor.cs`, `SectorSubscription.cs`, `LivePreview.cs`, and the
anchor pass in `PlacementPatch.cs` of TortalPortal v1.2.0.

## ADDENDUM 2026-09-01 — only feed `CreateObjects` from zones that are actually loaded (AwayFromHome 1.0.3)

> ⚠️ **2026-09-05 correction to the correction:** the "loaded" test this section and the 2026-09-01
> addendum prescribe must be **"the zone's terrain exists"** (`ZoneSystem.m_zones.ContainsKey(zone)`),
> **never `ZoneSystem.IsZoneLoaded`**. `IsZoneLoaded` also goes false while a dungeon or delayed
> location is streaming its assets, which made the anchor destroy and re-create the whole zone every
> other tick and leaked the Wonderland server to an OOM kill three times. See the 2026-09-05 addendum.

The port in AwayFromHome gathered its near set with
`ZDOMan.FindSectorObjects(anchorZone, Ring, m_activeDistantArea, list)`. That returns every ZDO in every
sector of the ring **regardless of whether the zone is loaded**, and `ZNetScene.CreateObjects` then
instantiates them into zones that have no terrain yet. Each `TerrainComp.Load` on the way throws an NRE
that a mod-agnostic guard in the same codebase suppresses and counts — so the failure is a quietly
climbing counter, not a crash. The fix is a per-zone loop over the ring:

```csharp
for (int y = anchorZone.y - ring; y <= anchorZone.y + ring; y++)
    for (int x = anchorZone.x - ring; x <= anchorZone.x + ring; x++)
    {
        var zone = new Vector2i(x, y);
        if (!ZoneSystem.instance.IsZoneLoaded(zone)) continue;
        ZDOMan.instance.FindObjects(zone, currentNearObjects);   // private; publicizer
    }
```

**Read the counter honestly before crediting a fix to it.** The 7,137-suppression storm this was written
against belonged to **LetItGrow 0.1.1's** anchor on the same shared server, and cleared when LetItGrow
shipped 0.1.2 — *before* this change existed. AwayFromHome's own baseline on its own world was **1**. A
suppression counter that is mod-agnostic counts everyone; attribute the number before you claim it.

## ADDENDUM 2026-09-04 — never list a ZDO twice (LetItGrow 0.1.3, AwayFromHome 1.0.7)

**What happened.** A Scarecrow farm (LetItGrow) and a Keeper Stone (AwayFromHome) shared zone (60,24) on
the Wonderland server. Both anchors are prefixes on `ZNetScene.CreateObjects`; both appended that zone's
ZDOs to `currentNearObjects`. `CreateObjectsSorted` copies every `!Created` ZDO out of that list and then
instantiates each entry **without re-checking `Created` between entries** — a ZDO listed twice becomes two
GameObjects around one ZDO. For most prefabs that is a leaked double-ticking ghost. For `TerrainComp` it
is the end of the zone's terrain: the second copy's `Awake` finds the first in `s_instances`, logs
vanilla's `Found another terrain compiler in this area, removing it`, and calls `ZNetScene.Destroy` on
it — which, because `OwnershipClaim` had made the server that ZDO's owner, runs `ZDOMan.DestroyZDO`
on the zone's only terrain record. Three zones of one base lost every levelled floor, raised path and
cultivated bed in one tick (02:34); the pieces standing on that ground collapsed when the owner logged in.
The world saves never contained a duplicate compiler — the "other compiler" was the same ZDO.

**Rules, now enforced in both ports:**

1. **Append only what is not already in the list, whoever put it there.** Build a `HashSet<ZDO>` from
   `currentNearObjects`, collect each zone's objects into a scratch list, add only those the set did not
   already contain.
2. **Sweep once more at `Priority.Last`.** A second prefix on `CreateObjects` with `[HarmonyPriority(
   Priority.Last)]` de-duplicates the whole list in place after every other mod has appended. Gated on
   the anchor being active, so clients and idle servers pay nothing.
3. **Never claim ownership of `_TerrainCompiler`.** Nothing an unattended site does writes terrain;
   reading it (`CheckLoad`, `IsCultivated`) needs no owner; an owned compiler is the one thing that turns
   vanilla's reconciliation from a GameObject swap into data loss.
4. **Guard `TerrainComp.Awake`** (Priority.First prefix): if `FindTerrainCompiler(pos)` returns an
   instance whose ZDO **is this ZDO**, detach it by writing `ZNetView.m_zdo = null` — not `ResetZDO()`,
   which also flips `zdo.Created = false` and makes `CreateObjectsSorted` instantiate it a *third* time
   next frame. Vanilla's `Destroy` then sees a view with no ZDO and only destroys the GameObject. If the
   ZDOs differ, leave vanilla's choice alone but log both ids and which one holds `TCData`.

5. **The seal — two keepers never hold the same ground.** `ZoneAnchor.Set` returns false (and the
   keeper skips that site this round, logging "already there? skipping this round") when the other
   mod's anchor is active on an overlapping ring. Found by reflection on the other anchor's public
   statics `IsActive` / `Position` / `Ring` (`AccessTools.TypeByName("AwayFromHome.ZoneAnchor")` /
   `"LetItGrow.Farming.ZoneAnchor"`), so neither mod references the other and either can be absent.
   Overlap: `|Δzone.x| <= ringA + ringB && |Δzone.y| <= ringA + ringB`. Rules 1–4 are defence in depth
   behind this one. Any THIRD anchoring mod should expose the same three statics and check both.

**Restore procedure that worked:** `systemctl --user stop valheim.service` on archserver (saves on
SIGINT), copy the last pre-incident `*_backup_auto-*.db/.fwl` over `Era03-Unlimited.db/.fwl`, keep the
bad save aside, start. Verify with a scan of the `.db` for the `_TerrainCompiler` prefab hash (each
record: `ushort flags, Vector2s sector, Vector3 pos, int prefab, …, byte[] TCData` under the 0x80 flag)
— counting compilers per zone and `TCData` cells before and after.

## ADDENDUM 2026-09-05 — `IsZoneLoaded` is the wrong gate: the dungeon flip-flop (LetItGrow 0.1.4, AwayFromHome 1.0.8)

**What happened.** The Wonderland server was OOM-killed on 31 Aug, 1 Sep and 5 Sep 2026 (30 GB box;
`journalctl --user -u valheim.service` shows `29G memory peak` after 24.5 h, `26.4G`, `24.6G`). Peak memory
was ~10 GB across multi-day runs until 27 Aug; from then on every session climbed roughly **1 GB/hour from
boot, with zero players connected**, so the growth was server-autonomous — the keepers. The server log
gave the shape: `Loading dungeon` ran ~600/day until 1 Sep, then **12,642 / 14,411 / 17,519 / 11,055 per
day** from 2 Sep (the first full day both anchors carried the 2026-09-01 "only loaded zones" gate); the
session that hit 29 GB logged **19,682 `Loading dungeon` against 315 `Spawning dungeon`**. A dungeon was
being loaded and destroyed before it ever finished, ~98% of the time, in bursts aligned to keeper visits.

**Mechanism, decompile-verified.**

```
ZoneSystem.IsZoneLoaded(zone) == m_zones.ContainsKey(zone) && !m_loadingObjectsInZones.ContainsKey(zone)
```

The second clause is vanilla's "something in this zone is still streaming assets in".
`DungeonGenerator.Awake → Load → LoadRoomPrefabsAsync` calls `ZoneSystem.SetLoadingInZone(zdo)` the
tick the generator is instantiated (and `LocationProxy.SpawnLocation` does the same for any location
whose prefab is not yet resident — `ShouldDelayProxyLocationSpawning`). Both clear it in
`OnRoomLoaded → ReleaseHeldReferences` / `SpawnLocation` success — **or in `OnDestroy`**. Around a player
this is harmless: vanilla keeps listing the zone's ZDOs and `CreateObjectsSorted` merely defers
lower-priority types through `IsZoneReadyForType` until the dungeon is up. Gating **our feed** on it
produced a two-tick oscillation for the whole visit:

| tick | `IsZoneLoaded` | anchor prefix | `ZNetScene` |
|---|---|---|---|
| N | true | lists the zone's ZDOs | `CreateObjectsSorted` instantiates the crypt's `DungeonGenerator` → `SetLoadingInZone` |
| N+1 | **false** | skips the zone | `RemoveObjects` destroys **every** instance in it → generator `OnDestroy` → `UnsetLoadingInZone` |
| N+2 | true | lists the zone again | back to N |

Every zone in a ring with a crypt, cave, mine, troll cave or delayed location in it was rebuilt from
scratch every other tick, for the full dwell, every visit. LetItGrow's live config (dwell 15 s, cycle
120 s) made it ~10 dungeon loads a minute all day. What exactly retained the memory across those
create/destroy cycles was **not** pinned (Unity's `Loaded Objects now` count does not climb
monotonically, so it is not simply leaked GameObjects; managed-heap growth under that churn is the
working assumption) — but the churn itself is the bug, and the 27–31 Aug leak had the same signature
under a different churn (the "objects over no ground" storm this document's 2026-09-01 correction fixed:
4.7 M `Object fell out of world` on 27 Aug alone).

**The fix** (both ports, identical): the prefix gates on **terrain exists** —

```csharp
public static bool HasTerrain(Vector2i zone) => ZoneSystem.instance.m_zones.ContainsKey(zone); // publicized
...
if (!HasTerrain(zone)) continue;   // was: if (!ZoneSystem.instance.IsZoneLoaded(zone)) continue;
```

`m_zones` gains the key only after `SpawnZone` succeeded, which requires `HeightmapBuilder.IsTerrainReady`
— exactly the condition the 2026-09-01 gate was written for and nothing more. Objects in a mid-stream zone
stay listed, as they do around a player, and vanilla's `IsZoneReadyForType` ordering finishes the dungeon
the vanilla way. `SiteSettle`/`SiteCensus` still use `IsZoneLoaded` for the *settle* test, which is right:
a site is not settled until its dungeon has finished loading, and with the flip-flop gone that happens
within seconds.

**Rule for the hook-point summary:** *the object feed is gated on terrain existence, never on
`IsZoneLoaded`; `IsZoneLoaded` is a readiness test, not a listing test.*

**Ships as** LetItGrow 0.1.4 / AwayFromHome 1.0.8, server-side only (`MinimumRequiredVersion` unchanged at
0.1.2 / 1.0.2). Hex-folded 2026-09-05; deployed by Rohan (rule #3: never touch the live server).

**Live result, first minutes after the 13:28 restart on 2026-09-05 (1.0.8 / 0.1.4, new rotation defaults, zero
players):** `Loading dungeon` 0, `Object fell out of world` 0, seal skips 0, mod errors 0, LetItGrow visiting
all four farms and AwayFromHome holding its stones; RSS 8.45 GB at 550 s versus 8.9 GB at 575 s in the
previous session. **74-minute result (read-only samples every 10 min, players joining from 13:46):** RSS
8.45 → 8.64 → 8.71 → 8.66 → 8.18 → 8.08 → 8.22 → 8.60 → 8.77 GB — a GC sawtooth between 8.0 and 8.8 GB, not a
slope; `Loading dungeon` : `Spawning dungeon` = 14 : 14; fell-out 0; errors 0; seal skips 9 (LetItGrow yielding
to a 270 s AwayFromHome hold at the shared farm, tended next lap). The previous session was at ~9.4 GB and
climbing at the same uptime. **Fix confirmed.** Later samples: 9.11 GB at 3 h 49 min (40:40), **9.36 GB at
5 h 50 min (60:60, three players online)** — a steady ~125 MB/hour drift, a week from the kill line rather than a
day, and possibly a second, much smaller retention source (the per-instantiation audit that was never finished).
Post-deploy note: the server's `AzuAntiCheat_Whitelist/` needs a folder for each client version you still want
joining — with only `Wubarrk-AwayFromHome-1.0.8` / `Wubarrk-LetItGrow-0.1.4` present, a 1.0.7 client is rejected
even though ServerSync's floor (1.0.2 / 0.1.2) would accept it; keep the old folders beside the new ones until
Hexium carries the new builds.

**Still open (two audit agents were stopped before reporting):** (1) whether any mod retains state
*per ZNetView instantiation* (static collections keyed by ZDO/ZNetView/GameObject, Awake-side
registrations with no OnDestroy cleanup) — that would explain why churn turned into retention rather
than just CPU; (2) ~~whether TortalPortal 1.4.6's own anchor/snapshot path runs on the dedicated server~~ — audited
2026-09-05: it does not (snapshot pass returns on `IsDedicated`, presence gates on `m_localPlayer`), so its
absence from the seal is moot server-side; see the TortalPortal.md 2026-09-05 addendum for what it does cost; (3) what in StackIT 0.2.1 (27 Aug) or
WingsoftheValkyrie 2.1.5 (2 Sep) coincides with the two churn onsets, if anything.

**How to verify after the restart:** `grep -c "Loading dungeon"` vs `grep -c "Spawning dungeon"` in
`valheim_server.log` for the new session should be near 1:1 (it was 62:1), the daily `Loading dungeon`
count should fall back to the hundreds, and RSS (`ps -o rss= -p $(pgrep -f valheim_server.x86_64)`)
should stop climbing ~1 GB/hour with nobody online. If RSS still climbs with the flip-flop gone, the
retention is somewhere else and the `Loaded Objects now` lines, per-mod `Awake`-side registrations, and
the remaining churn sources (LetItGrow's 15 s dwell / 120 s cycle) are the next places to look.

