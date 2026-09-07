# Away From Home

**Running a farm — taming, breeding, hatching, growing — at a place no player is standing, while
the player is somewhere else entirely.**

The companion to **[ZoneAnchor.md](ZoneAnchor.md)**, which is the prerequisite and is not repeated
here: read it first. That document keeps a patch of world *existing and drawing*; this one makes
the animals in it *live*. Verified against the live Valheim build as of **August 2026**, same
method as the anchor doc: `BepInEx.AssemblyPublicizer.MSBuild` over `assembly_valheim`, every
"private" member below called or read directly. Re-verify against a current decompile before
trusting a new game patch.

**Shipped as of 2026-08-10, networked/menu pass added 2026-08-11** — `C:\WubarrkCODING\AwayFromHome\`
(BepInEx/Harmony + the shared ServerSync library, no other dependency). Implements the client-side keeper
described below in full: the rotation, the settle-before-claim rule, claim-only-unowned arbitration, the
sector subscription for a client of a dedicated server, and a cycle-safety warning when a rotation would
revisit a smelter/kiln site slower than the real 3600s-per-gap cap the decompile confirmed on
`Smelter.UpdateSmelter`/`GetDeltaTime` (not previously verified when this doc was written — see the
updated clock-table note below).

The 2026-08-11 pass replaced the original client-local `afh_add`/`afh_list` file with a
**server-authoritative** site registry (`SiteNetwork.cs`): players' sites live on the server, keyed by a
routed-RPC-authenticated owner id (`ZDOMan.GetSessionID()` as seen by the server, not a client-supplied
field — cannot be spoofed), persisted next to the world save, and published to every client as a
`CustomSyncedValue<string>` ZPackage blob, same shape as TortalPortal's `PortalRegistry`. This is what
makes an admin master list possible at all — a purely client-local list can never show what an offline
player registered. Every gameplay-affecting config setting is likewise wrapped in ServerSync's
`AddConfigEntry`/`AddLockingConfigEntry`, so a server admin's dials are what every connected client
actually runs under, not just a suggestion. A styled IMGUI menu (`UI/KeeperUIManager.cs`, built on a port
of TortalPortal's `TortalUITheme` as `AFHUITheme.cs`) sits alongside the console commands: a player's own
tab for add/remove, and an admin-only tab listing and removing *any* player's site, gated server-side by
resolving the routed RPC's authenticated `sender` to a peer's socket hostname and checking
`ZNet.IsAdmin(hostname)` — the exact chain `ScatterCommand.cs` in TortalPortal already uses, with a
same-process special case so the hosting player's own requests (which never resolve to a `ZNetPeer`) are
still trusted as themselves. **Not shipped**: the dedicated-server-while-nobody-online variant sketched
near the bottom of this doc — still open questions, still needs a testbed session (though see this repo's
README for a note on what already happens as a side effect if the mod runs on the server binary itself).

**Handoff note, 2026-08-11:** versioned to `0.0.1` and packaged — `HexiumDist/AwayFromHome-v0.0.1.zip`
(manifest, README, changelog, icon, plugin DLL, same shape as every other package in this stable). The
mod compiles clean and every mechanism was checked against a decompile line or a working sibling mod, but
**nothing has been loaded into a live Valheim session yet** — no automated test suite exists for a BepInEx
mod in this stable, so the actual verification step is a human running it. `AwayFromHome/README.md`'s
*Handoff* section has the concrete first-things-to-check list. Treat every claim above as "true according
to the decompile and the build," not yet "confirmed true in a running game."

TortalPortal 1.3.1 ships the nearest relative —
`AnchorPresence` + `PumpSpawning`, which make *wild* creatures appear during a live view — but a
ranch is a different animal: it needs **ownership**, not presence, and it deliberately wants LESS
of the anchor than the live feed uses.

---

## The one-sentence mechanism

Every animal behaviour in Valheim — eating, taming progress, love points, births, hatching,
growing up — is an `InvokeRepeating` loop gated on `m_nview.IsOwner()`. Creatures in an anchored
zone are instantiated but **unowned** (vanilla assigns simulation ownership by peer proximity, and
no peer is near), so every one of those loops runs on *nobody*: the pen renders perfectly and is
frozen in amber. **Ownership is the entire feature.** No presence spoofing, no camera, no sky —
the `SpawnSystem` player-gate that made TortalPortal's spawning feature hard does not exist here:

```csharp
// Tameable.TamingUpdate — the whole gate. No Player anywhere.
if (m_nview.IsValid() && m_nview.IsOwner() && !IsTamed() && !IsHungry() && !m_monsterAI.IsAlerted())

// Procreation.Procreate — same shape.
if (!m_nview.IsValid() || !m_nview.IsOwner() || !m_tameable.IsTamed()) return;
```

---

## The clock table — decide the design from this

Valheim's progression clocks come in two kinds, and the difference is what keep-alive is *worth*:

| System | Clock | Needs loaded+owned time? |
|---|---|---|
| Hunger (`Tameable.IsHungry`) | **timestamp** (`s_tameLastFeeding` vs `m_fedDuration`) | No — fed status **decays even while unloaded** |
| Eating (refreshing that timestamp) | **AI tick** (`MonsterAI.UpdateConsumeItem`) | **Yes** — the animal must actually eat a loaded `ItemDrop` |
| Taming progress (`TamingUpdate`) | **tick accrual** — `DecreaseRemainingTime(3f)` per 3 s tick | **Yes** — pure loaded time, plus fed and not alerted |
| Love points (`Procreate`, 10 s interval) | **tick rolls** — chance per tick, partner in range, fed, under `m_maxCreatures` | **Yes** |
| Pregnancy duration (`IsDue`) | **timestamp** (`s_pregnant` ticks vs `m_pregnancyDuration`) | No — gestation elapses unloaded |
| Birth (the pregnant branch of `Procreate`) | **tick** | **Yes** — one owned tick after due |
| Egg hatching (`EggGrow.GrowUpdate`, 5 s interval) | **timestamp** (`s_growStart` vs `m_growTime`) — but `CanGrow()` (fire, roof) is re-checked per tick and **resets the timestamp to 0** when conditions lapse | **Yes** for the hatch itself, and conditions must hold across ticks |
| Growing up (`Growup.GrowUpdate`) | **timestamp** (`GetTimeSinceSpawned`) | Only the final prefab swap needs one owned tick |
| Plants, beehives, fermenters, fireplace fuel | **timestamp** | **No — do not keep these alive; the game already fakes them retroactively** |

Read the chain through the table and the value proposition falls out. Fed status decays in real
time whether the pen is loaded or not — leave boars for half an hour and they are hungry, and
hungry blocks taming, love points and pregnancy rolls alike. The timestamps (gestation, growup
age) accrue for free and merely wait for one owned tick to *land*. So:

- **Without keep-alive:** the chain stalls at the first tick-gated step. Animals go hungry the
  moment their last feeding times out, nothing re-feeds them, no love accrues, births and
  hatchings queue up waiting for your next visit.
- **With keep-alive:** feeding is continuous (fed never lapses while the trough is stocked), the
  tick accruals run at full speed, and the timestamp events land the moment they are due. The pen
  runs exactly as if you were standing in it.
- **The cheap middle** (see *Many pens* below): periodic short visits by a rotating anchor keep
  the fed timestamp topped up and give queued births/hatchings their tick, at a fraction of the
  cost of holding everything loaded.

---

## Ownership: the claim, and why vanilla lets you keep it

`ZDOMan.ReleaseZDOS` runs every 2 seconds and looks like it should destroy this whole idea — it
reassigns ownership by proximity. Read it closely and it does the opposite: it only scans
**sectors within `m_activeArea` of each peer's reference position** (`FindSectorObjects(zone(refPos),
m_activeArea, 0, ...)`), for the local session and once per connected peer. A pen five kilometres
from everybody is in *nobody's* scan, so:

- **Claims on a far pen are never auto-stripped.** The strip branch (`GetOwner() == uid` and not
  in active area → `SetOwner(0)`) only fires for ZDOs *near your own refpos* that you own — the
  pen never enters that sweep.
- **A real player arriving takes over, correctly.** Their sweep covers the pen; your refpos is far
  away, so `IsInPeerActiveArea(penSector, you)` is false and they `SetOwner(them)`. That is the
  right outcome — a human standing there beats a background keeper — and the keeper must expect
  it (see the loop below).
- **When they leave, they drop it.** As the pen exits their active area their own sweep sets
  owner `0`, and your next claim pass picks it back up.

The claim itself is one publicized call per creature, repeated on a slow cadence rather than done
once — repetition is what makes the real-player handover self-healing:

```csharp
// Every ~5s while the pen anchor is up, AFTER settle (see the physics warning):
foreach (ZDO zdo in penSectorZdos)               // FindSectorObjects over the anchor ring
{
    if (!zdo.Persistent || zdo.HasOwner()) continue;   // unowned only: never fight a peer,
    zdo.SetOwner(ZDOMan.GetSessionID());               // never fight a player standing there
}
```

Claiming only the **unowned** is the entire arbitration policy — the same shape as
`GetPlayersInZone`'s "add the local player only when the list came back empty". A ZDO owned by
anyone else is either a real player's (theirs by right) or another keeper's (first come, first
served).

> ### ⚠️ Settle before you own — the physics rule, restated because it bites here hardest
>
> An owned `Rigidbody` on terrain that has not finished building **falls through the world, and
> `ZSyncTransform` writes the owner's transform into the ZDO every frame — it saves itself
> falling.** Farm animals are rigidbodies; this failure deletes the herd, permanently, in the
> world file. Claim nothing until the anchor reports settled (the ZoneAnchor doc's settle test:
> stable ZDO count + `IsAreaReady` + no queued heightmap rebuilds, held for consecutive polls).
> This is a hard rule, not a tuning choice — the same bug class destroyed a player inventory in
> another mod in this stable.

---

## What to deliberately LEAVE OUT

A ranch keeper is mechanisms 1–3 of the zone anchor plus the claim pass. Everything else the live
feed uses is dead weight here — and one omission is a feature:

| Live-feed part | Ranch keeper | Why |
|---|---|---|
| Camera, RenderTexture, readback | **omit** | Nothing renders; nobody is looking |
| `AnchorEnvironment` (sky/weather/grass) | **omit** | Render-time state; no render |
| `TerrainLod` horizon rig | **omit** | Same |
| `AnchorPresence` patches + `PumpSpawning` | **OMIT — this is the feature** | Presence is what makes `SpawnSystem` spawn *wild* creatures. Without it, no player is in the zone as far as spawners are concerned: **no greydwarves harassing the pen, no wild spawns, no raid events** (RandEventSystem is player-gated too). The pen simulates *peacefully* — tames tick, hostiles do not appear. Adding presence to a ranch means monsters spawning 40–80 m from livestock with nobody there to defend it. |

The keeper is therefore invisible and cheap: zones poked, objects instanced, ZDOs subscribed (on
a dedicated server), creatures owned. No pixel is ever drawn.

---

## The keeper loop

The lifecycle rules are the anchor doc's, with the corrected teardown pattern (its Lifecycle §1:
**`StopCoroutine` does not run a coroutine's `finally`** — teardown is an explicit, idempotent
method called by whatever stops the loop AND from the `finally` as backup). What differs from the
live feed is the deadman: there is no album ticking every frame. The keeper is driven by an
explicit want — a config list of pen positions, a ward-like item, a console command — and holds
while that want stands:

```
KeeperRoutine(penPos):
    viaServer = client of a dedicated server
    try:
        while pen still wanted:                      // config entry / item present / command
            if world unloading: yield break          // finally cleans up
            if a HIGHER-PRIORITY anchor consumer is active (a live feed): stand down, wait
            SnapshotAnchor-equivalent.PokeZones()
            if viaServer: SectorSubscription.Renew(penPos) every 5s
            if not settled yet: run the settle test; continue
            every ~5s: claim pass (unowned persistent ZDOs in ring)
            yield WaitForSeconds(0.5)
    finally:
        TearDownKeeper()      // idempotent; also called by whatever stops the routine

TearDownKeeper():
    stop claiming (that is all "unclaiming" is — vanilla reassigns or idles the moment
    a real peer matters, exactly as when a player walks away)
    drop the anchor (zones TTL out, next RemoveObjects tick sweeps instances)
    if viaServer: SectorSubscription.Release()
```

Nothing needs to be un-owned on teardown: an owner that stops renewing its claim simply stops
mattering — `ReleaseZDOS` hands the ZDOs over the next time any peer is near, and unloaded,
still-"owned" ZDOs tick nothing because their owner has no instance to tick. Cleanup is free, in
the same way every other anchor teardown is free.

**Priority:** the single-anchor politeness protocol from the anchor doc gains a third consumer.
Order them explicitly: live feed > background photographer > keeper. The keeper is the one whose
work survives interruption best (timestamps hold; a missed claim pass costs seconds), so it yields
to everything and resumes last.

---

## Many pens: the rotation, and why timestamps make it work

Holding N anchors is N × (one extra player's streaming) — the wrong shape. The clock table gives
the right one: **rotate one keeper through the pens.**

- Fed durations are minutes long (per-creature `m_fedDuration`), and eating is instant once an
  owned, hungry animal stands near a loaded `ItemDrop`. A visit long enough to settle + a few AI
  ticks (~30–60 s) tops every fed timestamp back up.
- Gestation and growup accrue *between* visits for free; the visit gives queued births, hatches
  and prefab swaps their owned tick.
- Only the pure tick accruals — taming progress, love-point rolls — advance *solely* during
  visits. A rotation trades their speed for cost, linearly and honestly.

So a rotation with period shorter than the shortest `m_fedDuration` in the pens keeps every chain
unbroken, at the cost of one anchor. Stock the troughs deep (items are ZDOs; a pile of berries
persists like anything else) and the ranch runs on a duty cycle of a few percent.

---

## Production buildings — why "partially" (decompile-verified, 2026-08-10)

`Smelter.UpdateSmelter()` (`InvokeRepeating` at 1f/1f, owner-gated) reads real elapsed time off a ZDO
timestamp (`s_startTime`) exactly like a fully-timestamp system — but then **clamps it to 3600f before
draining it**, and unconditionally resets the timestamp to now on every call regardless of how much of
that 3600s cap got used. So a smelter/kiln/blast furnace/spinning wheel/windmill left unowned for longer
than an hour **permanently forfeits everything beyond the last hour**, the instant an owner finally ticks
it — this is the precise shape of "partially" catches up from a timestamp: the *elapsed time* is captured
like a timestamp, but *turning it into product* is a real per-second loop with a hard ceiling, not a pure
function of `now - start`. A rotation that revisits every production site more often than that cap loses
nothing; slower than that, it loses the excess every single visit, forever, even though the keeper never
stopped running. `CookingStation.UpdateCooking()` has no such cap (its elapsed-time delta is applied in
one uncapped lump sum), and `Fermenter` needs no owned tick at all, matching this doc's original claim
exactly. No manual pump/reverse-patch is needed for either: the natural `InvokeRepeating` fire, the moment
after ownership is claimed, already drains the full backlog in one call.

---

## The dedicated-server variant (sketch — not yet built)

Everything above runs on a **client**, which means "away" = *elsewhere in the world, logged in*.
The logged-off version belongs on the server, and is structurally *smaller*:

- The server holds every ZDO already — mechanisms 3–4 (subscription, ghost generation) vanish.
  Mechanisms 1–2 (zone poke + `CreateObjects` append) run server-side; the server runs
  `ZoneSystem`/`ZNetScene` like any peer.
- The claim is `zdo.SetOwner(ZDOMan.GetSessionID())` with the *server's* session id; the server
  then ticks the components on its own instances.
- **Open questions to verify before building** (marked here so nobody trusts the sketch): whether
  world time advances on an empty server in the current build (timestamp clocks depend on it);
  whether any animal component assumes a `Player.m_localPlayer` exists in ways the gates above do
  not show (the taming message path uses `Player.GetClosestPlayer`, which is null-safe); and the
  interaction with the server's save cadence. Budget a testbed session on the
  DEDICATED-SERVER-TESTBED before promising logged-off farming.

---

## Cost model

Identical to the anchor doc's, minus rendering: one ring-1 keeper ≈ one extra player's streaming
and instancing while held, gone within ~5 s of release. No RT, no readback, no post-processing —
the keeper's frame cost is instancing and AI for one pen's worth of creatures. Mind the known RAM
ceiling on the test box when running client + server + keeper together. Ring 1 (3×3 zones,
192 m) covers any sane pen; there is no reason a keeper ever needs ring 2.

---

## Hook-point summary (delta over ZoneAnchor.md — everything there still applies)

| Purpose | Hook | Kind |
|---|---|---|
| Creatures simulate | `ZDO.SetOwner(ZDOMan.GetSessionID())` on **unowned** persistent ZDOs in ring, after settle, every ~5 s | direct call (publicized), repeated |
| Never fight a human | claim only `!zdo.HasOwner()`; vanilla's `ReleaseNearbyZDOS` hands over/back by proximity | policy, no code at the handover |
| Peaceful pen | **do not** install the presence patches (`GetPlayersInZone` / `FindBaseSpawnPoint` / spawner ownership) | omission |
| Feeding works | anchor keeps trough `ItemDrop` ZDOs instanced; owned `MonsterAI.UpdateConsumeItem` does the rest | free with mechanisms 1–2 |
| Chain progress while away | timestamps (`s_tameLastFeeding`, `s_pregnant`, spawn age, `s_growStart`) accrue unloaded; ticks land on visit | design around the clock table |
| Lifecycle | explicit idempotent teardown + renewal-driven subscription + stand-down for the live feed | ZoneAnchor.md Lifecycle, as corrected |

Reference implementations to lift from: `SnapshotAnchor.cs` (mechanisms 1–2),
`SectorSubscription.cs` (mechanism 3), `LivePreview.cs` (the corrected Stop/TearDown shape),
`PlacementPatch.SnapshotAnchoredPortal` (settle test + polite-consumer guards) — all TortalPortal
1.3.1. Decompile line references for the gates quoted above: `Tameable.TamingUpdate` /
`IsHungry`, `Procreation.Procreate` / `MakePregnant` / `IsDue`, `EggGrow.GrowUpdate`,
`Growup.GrowUpdate`, `ZDOMan.ReleaseZDOS` / `ReleaseNearbyZDOS` in
`DECOMPILED ASSEMBLY VALHEIM/assembly_valheim.decompiled.cs`.

---

## ADDENDUM 2026-08-11 — first live run, and the v1.0.0 redesign it forced

Everything above still describes the keeper mechanism correctly and is unchanged by this. What follows
corrects two claims this doc and the v0.0.1 code shared, and records what a live dedicated-server run
actually proved. Written as an addendum rather than an edit so the original reasoning stays auditable.

### Verified live (dedicated server, lean rig, VerboseLogging on)

Rig: `libs-Tools/DEDICATED-SERVER-TESTBED/wire-afh-server.ps1` + `start-afh-server.ps1`, port 2496,
this mod + Server Devcommands only.

- Plugin loads, all Harmony patches apply, ConfigSync RPC registers, all console commands register,
  keeper coroutine starts.
- **Settle-then-claim works on real terrain.** A site ~1.9 km from any player settled and claimed **937
  objects**; nothing fell through the world. The `Ring = 1` (3x3 zone) footprint is the reason the count
  is that large - world-gen vegetation and rocks are persistent ZDOs and are all unowned when no peer is
  near, so most of those 937 are scenery, not the pen. That IS the real cost profile of a held site.
- **`Smelter` resolves headlessly.** `ZNetScene.FindInstance(zdo).GetComponent<Smelter>()` returned a live
  component on a dedicated server, confirming mechanism 2 instantiates on a headless box.
- **The fully-offline case works.** This doc listed it as an unbuilt sketch needing a live testbed. A
  20-minute soak with **zero players connected** ran **55 site visits across 3 sites, 0 settle failures,
  0 errors, 0 exceptions**, no memory growth, and the coroutine never wedged. It is no longer a sketch.
- **A claim count of 0 after the first pass is correct, not a fault.** `ReleaseNearbyZDOS` only scans
  sectors near a peer's OWN refpos, so the keeper never loses a claim it holds; every later visit
  therefore reports `claimed 0`. This makes the log line a poor liveness signal after the first pass -
  worth knowing before someone debugs a working system.

### CORRECTION 1 — the session id is NOT durable (this doc's networking section was wrong)

The v0.0.1 design keyed a site's owner to the routed-RPC sender id and described it as "the same
per-installation id the ownership-claim system already relies on being durable". **That is false.**
Verified chain in `assembly_valheim_SERVER.decompiled.cs`:

- `ZRoutedRpc.InvokeRoutedRPC` stamps `m_senderPeerID = m_id`; `m_id` is set from exactly one call,
  `m_routedRpc.SetUID(ZDOMan.GetSessionID())` (`:66759`).
- `ZDOMan.GetSessionID()` returns `m_sessionID`, declared
  `private readonly long m_sessionID = Utils.GenerateUID();` (`:64441`) - never restored from any save.
- `ZNet.Awake` does `m_zdoMan = new ZDOMan(...)` (`:66720`), so **every world join builds a fresh ZDOMan
  and a fresh random id**.

Consequence in v0.0.1: every reconnect orphaned every site a player owned - their own list came back
empty, they could not remove their sites, and only an admin could. It worked perfectly within a single
session, which is why only a live multi-session test would ever have caught it.

**The durable identity to use instead is `Piece.m_creator`**, written from `Player.GetPlayerID()` ->
`PlayerProfile.m_playerID`, which IS saved (`:91177`) and reloaded (`:91335`) with the `.fch` character
file. Caveat: per-CHARACTER, not per-account.

### CORRECTION 2 — a mod that adds a prefab MUST be installed server-side, or it destroys data

`ZNetScene.CreateObjectsSorted` / `CreateDistantObjects` do not skip an object whose prefab hash the
server cannot resolve - they call `ZDOMan.DestroyZDO` and the object is gone **permanently**
(`:69553-69558`, `:69588-69594`). Any Wubarrk mod adding a buildable must say this loudly in its README.

### The v1.0.0 redesign: a site is a placed object

Sites are no longer registered by command or menu. A site is a **Keeper Stone**, a buildable piece
(10 Mushroom / 10 MushroomBlue / 5 SurtlingCore, recoverable, no crafting station - it belongs at a
remote outpost, where a workbench requirement would forbid it). Because the piece's own ZDO is the record:

- the persistence file, the add/remove RPCs, the per-sender rate limiter and the per-player site cap are
  all **deleted** - a build cost and a physical object make each one unnecessary;
- ownership comes free and durable via `m_creator`;
- the server derives the list live with
  `ZDOMan.GetAllZDOsWithPrefabIterative(prefabName, list, ref index)` - **public**, reads the ZDO index so
  unloaded zones are included, budgets ~400 sectors per call and returns `true` only when finished, so it
  must be pumped (vanilla's console `find` spins it; `SiteRegistry.Rescan` yields between pumps instead).

The F7 menu is now **admin-only** (master list + remove-at-distance); players read a stone by looking at
it (`Hoverable`).

**Registered without Jotunn**, following `Runic/Core/PrefabCloner.cs` - see `Runic.md`, which remains the
workspace's reference for Jotunn-free registration. The three traps it already solves and that apply
identically to pieces: `ZNetScene.m_prefabs` is read once in `Awake` (write `m_namedPrefabs` directly);
`Object.Instantiate` at runtime wakes the object and spawns a live networked entity unless instantiated
directly into an already-inactive parent; a duplicate hash throws inside `ZNetScene.Awake` and takes the
`SpawnObject` RPC registration down with it. New beyond Runic: `PieceTable` injection via
`ObjectDB.GetItemPrefab("Hammer").GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces.m_pieces`,
and the `m_knownRecipes` gate in `Player.UpdateKnownRecipesList` (a piece whose materials the player has
never picked up simply will not appear).

### Art pipeline

First use of the Meshy pipeline outside Avalor. The stone is a Meshy `latest`-engine generation
(8324 tris), baked to an AssetBundle by a **dedicated Unity project** at `AwayFromHome/Unity/` and
embedded in the DLL (~372 KB). It has its own project because `AvalorBundleBuilder` sweeps every `.glb`
under `Assets/` into one bundle, and sharing would have folded this mesh into Avalor's shipping bundle.
API findings (no `meshy-7`; use `latest`) are recorded in the dated addendum to
`MistsofAvalor/.agents/skills/meshy/SKILL.md`.

**Still unverified:** everything added in 1.0.0 - whether the stone appears in the hammer, whether it
looks right in-world (scale/offset/tint), whether place/destroy registers correctly, and whether ownership
genuinely survives a reconnect. The keeper mechanism underneath it is the part that is now proven.

---

## ADDENDUM 2026-08-15 — v1.0.0, the first public release

**Version renumbered on the author's call: everything from 0.0.1 through the 1.1.x line was internal
pre-release testing, nothing was ever published, so the first public release is `v1.0.0` and the changelog
was collapsed to a single entry.** Do not reintroduce 1.1.x references into code, logs or docs — several
comments and one user-facing warning string carried them and were stripped. Shipped private at
`github.com/RGlabs84/AwayFromHome` (tag `v1.0.0`, release asset attached). Thunderstore untouched.

The keeper mechanism described above the earlier addenda is unchanged and still correct. What 1.0.0 adds
is that **the Keeper Stone stopped being a marker and became the thing that supplies the site.**

### The two failures that forced it, and why they are the same failure

Both were visible in this mod's own telemetry for two release cycles:

- A pen the keeper holds open **still stalls** at whatever percentage it reached when the food ran out,
  because Valheim only advances taming while an animal is fed.
- A furnace handed back the time a frozen server clock owed it (previous addendum) **still goes cold**,
  because it burns through its load and nobody is there to reload it. A full hopper is ~17 minutes of a
  rotation that can run for days.

Marking a site was never the hard part. *Keeping it supplied* was. And the only object standing in the
middle of every site is the stone.

### What was built

- **`KeeperStore`** — a real vanilla `Container` (3×2) on a colliderless child with `m_rootObjectOverride`
  at the stone's own `ZNetView`. Three silent-failure traps, all documented in
  `../VANILLA-PIECE-INTEROP-FACTS.md` §5.
- **`KeeperFeeder`** — puts one real `ItemDrop` on the ground and replaces it when eaten, because
  `MonsterAI.UpdateConsumeItem` is the game's only eating path and its only sensor is an
  `OverlapSphere` on the item layer. **There is no container path; an animal can stand against a chest
  full of carrots and starve.** Patching `FindClosestConsumableItem` was rejected: it must *return* an
  `ItemDrop` that then gets `MoveTo`'d and `RemoveOne()`'d, so you construct the real item anyway, only
  now behind a hook on the hottest per-creature path in the game.
- **`KeeperSupply`** — reloads `Smelter` pieces (which is smelter, charcoal kiln, blast furnace, windmill,
  spinning wheel and eitr refinery) by walking vanilla's own `OnAddOre` path in its own order. See
  `../VANILLA-PIECE-INTEROP-FACTS.md` §1–§3 — the check-before-remove ordering there is the difference
  between this working and it deleting players' ore.
- **`PenArea`** — the leash became a shape (circle/square/rectangle + rotation) on four ZDO keys.
  `afh_leash` keeps its old name and meaning so pre-shape stones read back as the circle they already
  were with no migration and no version check: the shape key is absent, `GetInt` returns 0, and
  `PenShape.Circle` **is** 0.
- **`PenProjector`** — replaced `CircleProjector`, which draws only circles. See
  `../VANILLA-PIECE-INTEROP-FACTS.md` §6, including why the drawn outline and the enforced test must come
  from one type.

### Two design rules worth stealing

- **Never let a shared resource serve one feature at another's expense silently.** The stone's six slots
  feed animals *and* furnaces, and barley is a lox's food *and* a windmill's input (flax/spinning wheel
  likewise). Anything a tameable near the stone eats is reserved from the supply run. Hunger is
  deliberately *not* part of that test — it flips every few minutes, so a hunger-aware reservation would
  hand the herd's barley to the windmill during the exact window the herd was full.
- **A boundary is not a line when the things it bounds have bodies.** The stray sweep originally corrected
  anything `> 0 m` outside the pen. An accelerated soak logged `furthest was a Boar at 0.0m` — a boar
  leaning on its own fence, teleported to the middle, walking back, teleported again, once per poll for
  the whole visit. Churn that achieves nothing, burying the real escapes under a drip of 0.0 m ones, and
  **every one of those teleports is a physics re-seat, which is the exact event penned animals escape
  through.** Fixed with a 0.5 m tolerance, comfortably inside a boar's own footprint.

### The admin bypass — the "bug" that was a copied-world identity mismatch

`MaxSitesPerPlayer` exemption for admins looked broken for a full session: the 4th stone stayed capped
across laps with an admin connected, and nothing threw. **It was never a bug.** The test world had been
copied off another machine, so every stone carried a `Piece.m_creator` belonging to a character that had
never logged into that server. The lookup was correct and found nothing to match.

**The technique that settled it, reusable anywhere:** `PlayerProfile` writes `SetName(ReadString())` then
`m_playerID = ReadLong()`, so the playerID sits **immediately after the name string** in the `.fch` —
extract it and grep the world `.db` for that int64.

The one diagnostic that made it visible — a per-lap line printing the owner IDs the standing stones
actually carry, which is the single fact not observable from outside the process — was **kept in the
release** rather than stripped, folded behind `VerboseLogging`. Verified on a fresh world:
`matched=4, exempt=4, flag-written-this-pass=3` while connected (the 4th refused because the *player*
owned that stone and the write is owner-guarded), then `flag-written-this-pass=4` two laps after logout
once the keeper reclaimed it.

### Verified live, dedicated Linux server, accelerated soak

Hostile profile on purpose — `DwellSeconds 20`, `MinimumCycleSeconds 0`, `StoneScanIntervalSeconds 10`,
roughly 30× the site churn of shipping defaults, because load/unload churn is what shakes out settle races
and animals walking through fences.

**100 site visits, 0 errors, 0 strays, 0 JUMPEDs, 1 settle failure** — and that one is explained: a player
was spawning items 2 m from the stone, and `IsSiteReady` requires `census.Total == lastTotal`, so each
spawned ZDO reset the stability streak. *A settle failure while somebody is actively spawning things at
the site is the gate working, not failing.*

Memory oscillated in a 1.56–1.62 GB band and **fell** across a later sample (1618 MB @ 64 settles →
1604 MB @ 77) — the decisive observation, since a leak cannot go down. A single mid-window sample had
looked like growth; three points three minutes apart cannot tell sawtooth from a leak.

Everything the mod does is now confirmed working unattended: keeper rotation, settle census, ownership
reclaim on disconnect, offline production credit, autofeeding, producer restocking, the shaped leash, and
the admin bypass persisting across a logout.

## ADDENDUM 2026-09-01 — 1.0.1 → 1.0.6 in one day: one furnace for every ore, a quieter anchor, and the texture that was never the problem

Six point releases, three of them chasing the same appearance bug. `MinimumRequiredVersion` moved once,
to **1.0.2**, and stayed there — everything after it is client rendering or server-only behaviour.

### 1.0.2 — the blast furnace takes every smelter ore (`BlastFurnaceOres.cs`)

No Harmony patch. `Smelter.m_conversion` is the single list behind `IsItemAllowed`, `FindCookableItem`,
`Spawn` and `DropAllItems`, so appending the smelter's `ItemConversion`s to `blastfurnace`'s list (deduped
on `m_from.gameObject.name`, latched because `TryRegister` fires from three hooks) is the whole feature.
Live result: **7 ores** — CopperOre, IronOre, IronScrap, BronzeScrap, TinOre, SilverOre, CopperScrap.
**This is why the floor rose to 1.0.2:** `RPC_AddOre` re-validates on arrival and silently drops what it
rejects (§1 of `VANILLA-PIECE-INTEROP-FACTS.md`), so a 1.0.2 client feeding copper to a 1.0.1 server's
blast furnace watches the copper vanish. `ServerSync.VersionCheck` is bidirectional — `CurrentVersion >=
their minimum AND their current >= our minimum` — so one floor protects both directions.

Same release: `_Color` set to white and `_ValueNoise` zeroed on the donor material (the sign's tint and
Valheim's per-piece mottle were both being applied over the albedo), and `DumpMaterial` learned to print
Color/Vector properties — their absence is what had hidden the tint for two sessions.

### 1.0.3 — `ZoneAnchor` only feeds loaded zones

`FindSectorObjects(anchorZone, Ring, …)` pulls ZDOs from every sector in the ring whether or not the zone
is loaded; `ZNetScene.CreateObjects` then instantiates them into zones with no terrain, and every
`TerrainComp.Load` on the way throws NRE. Replaced with a per-zone loop: `IsZoneLoaded(zone)` →
`ZDOMan.FindObjects(zone, list)` (private; reachable through the publicizer, §7). **Honesty note kept on
purpose:** the 7,137-suppression storm this was written against turned out to be **LetItGrow 0.1.1's**
anchor, which cleared when *they* shipped 0.1.2 — before this fix existed. AwayFromHome's own baseline on
its own world was **1**. The fix is correct; the big number was never ours. See `ZoneAnchor.md`.

Also: the settle-failure log now names its cause — `terrain still has a queued rebuild` vs `the object
count was still moving (N -> M)` — instead of a bare failure, and the icon warning is demoted to Info
on a headless server (it has no build menu to draw one in).

### 1.0.4 / 1.0.5 — two texture releases that treated a symptom

In shade the stone read as a black lump (mean luminance 0.288, 54.7% of texels below 20%), so 1.0.4
lifted shadows; in daylight it then read as a white lump, so 1.0.5 replaced the lift with a luminance
range-remap into `[0.16, 0.70]` — hue-preserving, measured, correctly reasoned about a bimodal histogram,
and **aimed at the wrong thing**. Both are now identity `(0, 1, 1)`; the knobs and the reasoning stay in
`KeeperStoneBundleBuilder.cs` as a caution. Also from this pair: albedo `DXT1/Best` instead of
`DXT1Crunched/Normal` (crunch bought nothing — the texture is embedded in the DLL, not downloaded — and
cost the fine detail), and `KeeperStoneTextureDump.cs`, an editor menu that blits the baked DXT textures
back to PNG so "what actually shipped" can be looked at.

### 1.0.6 — `_MainTex_ST`. The whole bug was one pair of numbers.

Rohan's screenshot showed hard-edged **blue, cream and black patches on cap and stem alike** — the atlas's
own colours in the wrong places, which no tone change could produce or cure. So instead of a fourth
adjustment, every link was measured: shipped DXT1 correlates **0.996** with the source upright (0.055
flipped), the bundled mesh matches the GLB index-for-index (12,222 verts, UV0 exactly `(u, 1-v)` per
glTFast, 8,309 identical triangles), bundle bytes present in the DLL. All faithful. The only unexamined
state was the donor material's texture scale/offset — copied by `new Material(donor)`, untouched by
`SetTexture`, and **invisible to a property-table dump because ST vectors are not properties**. The client
log after the fix stated it: `_MainTex was scale=(0.33, 0.30)`. Reset to identity on every slot, previous
values logged, `[st scale/offset]` printed beside every texture. Rohan, in world: *"look good now."*
Full write-up as **§8 of `VANILLA-PIECE-INTEROP-FACTS.md`**.

### Rig and build facts learned the hard way

- **Hash every DLL and `diff -rq` the whole plugin tree; folder names and byte sizes are not version
  checks.** A "load-outs identical" pass by folder name missed a StackIT 0.1.1/0.2.1 skew that blocked the
  connect; Njord 1.9.2 and 1.9.3 were both exactly 241,152 bytes with different hashes.
- Never leave a backup of a plugin folder inside `BepInEx/plugins` — BepInEx scans recursively and loads
  both as duplicate GUIDs.
- Replace DLLs under a running process with copy-to-temp + `mv` (atomic rename keeps the loaded inode);
  the process still runs the old code until restart, and `MinimumRequiredVersion == PluginVersion` mods
  will refuse the client until you do.
- On a headless server the donor material resolves to `Hidden/InternalErrorShader` with **no**
  properties, so every `HasProperty` misses and every neutralise/bind line reads as a total failure or a
  no-op. Label those lines; only a client log says anything about appearance.
- `.NET` string literals are UTF-16 — `strings -el`, not `strings`, or a freshly built DLL looks like it
  lacks the code you just added.
- The `[BepInPlugin]` version is a separate hard-coded constant from ServerSync's `CurrentVersion`; bump
  both or BepInEx's load line lies about what is running.
