# Valkyrie's Cargo — Design (v3: merged design of record)

> One-screen summary of this document and the catalogue: `TLDR.md`.

**Mod:** Valkyrie's Cargo · **Publisher:** Raven Iron (`com.raveniron.valkyriescargo`, namespace `RavenIron.ValkyriesCargo`)
**Runtime:** Valheim 0.221.x on **Unity 6000.0.61f1** (read from the installed `UnityPlayer.dll`), BepInEx 5.4.x, HarmonyX,
**ServerSync** (blaxxun, MIT-0) merged into the DLL by ILRepack. Vanilla assemblies only. No Jotunn.
**Every client must run the mod** (ServerSync `ModRequired`).
**Lineage:** v2 (2026-09-06, from the decompiled engine and ServerSync source) merged with Wu'barrk's v5 of the same day
(`docs/WUBARRK-DRAFT-*.md`, the v5 zip) after the verified review in `docs/REVIEW-v5-2026-09-06.md`. Where v3 cites an
engine fact it names the class and member; the bodies were read with `ilspycmd` on the real `assembly_valheim.dll`.

Sibling of Cairn, Undertow, FireFront, Ragnarok's Wrath and RavenEye; same house style. Three firsts for the family,
each a recorded decision (section 8): ServerSync, a trade terminal of our own, and, later, an asset bundle.

---

## 0. Scope

**0.1 ships:** random visits gated on rested + comfort at a base; the intro Valkyrie carrying Ingvar to the drop; Ingvar
walking to the pilot and calling out; five minutes or dismissal; the Cargo Terminal where he **buys and sells** from a
live, persistent stock with **coins and barter** at **real-time supply-and-demand prices**; shared visuals on every
client; ServerSync-locked config; server authority over the market; the Odin vanish; the Dverger placeholder body.

**Later (not 0.1):** the earned summon horn (with the purse as its cap); Thorium's custom body via the family's first
asset bundle (section 11 says what the model needs first); an explicit barter basket; rare rotating stock;
localisation; reputation lines.

---

## 1. Authority, in one table

| Concern | Authority | How it reaches the others |
|---|---|---|
| Configuration | Server (`ConfigSync.IsSourceOfTruth`) | ServerSync, locked; admins exempt |
| Who is eligible, when to roll, whom to pick | Server | reads character ZDOs |
| The event (banner, 300 s clock, pause, restore) | Server via vanilla `RandEventSystem` | vanilla `SetEvent` broadcast |
| Existence of the bird and the merchant | Server authors both ZDOs | ZDO replication; owner = pilot |
| Flight path, merchant walking | The owner client (pilot first; vanilla may hand the merchant to a nearer client) | `ZSyncTransform`, `ZSyncAnimation`, ZDO state keys written by the owner |
| Visit phase, clock, purse, drop point | Server | `VisitState` (ServerSync custom value) |
| Stock and prices | Server | `MarketState` (ServerSync custom value) |
| A deal | Server validates and applies | direct `ZRpc` request/response, then `MarketState` |
| Player inventory | The player's client (vanilla) | vanilla |
| Speech, effects, terminal contents | Every client, from shared state | derived, never sent as text |
| Destruction at the end | Server (`SetOwner` + `DestroyZDO`) | ZDO removal |

The one thing this design cannot make server-authoritative is a player's own inventory: vanilla keeps it on the
client-owned player ZDO. What the server guarantees is that **the merchant's side of every deal is exact**: two players
cannot both buy the last Black Core, prices move once per deal in one place, and no client can move them. The client
never touches its inventory until the server has answered (section 3.4).

---

## 2. ServerSync

**Embedding.** `ConfigSync.cs` compiled into `ValkyriesCargo.dll` as shared source (`Libs/ServerSync.cs`, origin
header, MIT-0), the way Wu'barrk's mods and most ServerSync mods do it. Chosen over ILRepack in Phase 0 because it
needs no NuGet task and no merge step, and the house has no build tooling beyond `dotnet build`. It is third-party
code inside our assembly: it patches `ZNet.RPC_PeerInfo` (wrapping the socket in a buffering socket, except Steam
sockets) and reads `ZRoutedRpc.m_peers` and `ZNet.m_adminList` by reflection. Blaxxun's reflection, in blaxxun's file,
updated from upstream; ours stays at zero.

```csharp
internal static readonly ConfigSync Sync = new("com.raveniron.valkyriescargo")
{
    DisplayName = "Valkyrie's Cargo",
    CurrentVersion = BuildVersion.Value,          // generated from the csproj Version
    MinimumRequiredVersion = BuildVersion.Value,  // every client on exactly this version
    ModRequired = true,
};
LockConfiguration = Config.Bind("Server", "LockConfiguration", true, "...");
Sync.AddLockingConfigEntry(LockConfiguration);
// Server.* : Sync.AddConfigEntry(entry);                      SynchronizedConfig = true
// Client.* : Sync.AddConfigEntry(entry).SynchronizedConfig = false;
internal static readonly CustomSyncedValue<string> VisitState  = new(Sync, "visit",  "");
internal static readonly CustomSyncedValue<string> MarketState = new(Sync, "market", "");
```

**What ServerSync does (read in `ConfigSync.cs`).** After `RPC_PeerInfo` the server sends all synced configs and custom
values to the new peer; later changes broadcast **on change only, there is no heartbeat**. Server → client goes over each
peer's own `ZRpc`; client → server is routed and, while locked, rejected unless the client is on `adminlist.txt`. Payloads
over 10 000 bytes are Deflate-compressed, over 250 000 sliced; ours are hundreds of bytes and go uncompressed.
A version mismatch disconnects at handshake with a message naming the mod and both versions.

```
VisitState : v1;visitId;phase;pilotUid;birdZdo;merchantZdo;dropX;dropY;dropZ;endWorldTime;purse;seed
MarketState: v1;visitId;purse;prefab:kind:stock:target:max:buy:sell:trend|prefab:...
```
`phase` ∈ `none, flying, dropped, approaching, trading, leaving`. `ValueChanged` refreshes whatever is open.

---

## 3. Systems

### 3.1 Eligibility and the roll — `Client/ComfortReporter.cs`, `Core/Scheduler.cs` (pure), `Server/VisitDirector.cs`

**Client report** (2 s, on change, inside the mod's one tick): `zdo.Set("VCargo_rested", seman.HaveStatusEffect(SEMan.s_statusEffectRested))`
and `zdo.Set("VCargo_comfort", player.GetComfortLevel())` on the local player's own ZDO. Client-owned, so it replicates;
same trust class as vanilla's `baseValue`.

**Server view** from `ZNet.instance.GetAllCharacterZDOS()` (host and every ready peer): `ownerUid = zdo.GetOwner()`,
position, `baseValue = zdo.GetInt(ZDOVars.s_baseValue)`, `rested`, `comfort`, `y`.

**Eligible** = ready ∧ alive ∧ `rested` (if `RequireRested`) ∧ `comfort ≥ MinComfortLevel` ∧ `baseValue ≥ MinBaseValue` ∧ `y < 3000`
∧ (`DaytimeOnly` → `EnvMan.instance.IsDay()`, read only) ∧ not on this player's cooldown ∧ no base within
`CooldownRadius` on cooldown ∧ the owner peer is ready.

**Roll** every `EventCheckIntervalMinutes` real minutes: hold if `RandEventSystem.instance.GetCurrentRandomEvent() != null`;
else with any eligible candidate and `Random < EventChancePercent`, pick one uniformly (candidates within 40 m collapse to
one ticket). That player's client is the **pilot**. Cooldown stamped at dispatch, so a failed flight cannot be farmed.
The roll is `UnityEngine.Random.Range(0f, 1f)` or a `System.Random`, never `Random.value` (inclusive of 1.0); the core
clamps a 1.0 anyway so chance 100 always visits.

**Admin.** `cargo visit` forces a roll for the caller at chance 100, ignoring the cooldowns (the admin asked) but not
rested, comfort, baseValue or the dungeon bound, gated by `ZNet.IsAdmin(hostName)` through RavenEye's
`AdminGate` shape (public API, fail closed). `cargo dismiss`, `cargo reset`, `cargo save` and `cargo catalogue
add|remove|reset` are admin too; `cargo status`, `cargo stock`, `cargo catalogue list` and `cargo prefab <name>` are not. Console commands are not config: `LockConfiguration` does not touch them.

### 3.2 Authoring the flight — `Server/Spawner.cs`, `Client/CargoFlight.cs`, `Patches/Patch_Valkyrie_Awake.cs`

> **Checked against both real builds, 2026-09-07** (`docs/engine-sweeps/2026-09-07-baseline-client-vs-server.md`,
> and its Linux twin beside it). The reference-position pin this whole section is built on — a dedicated server
> pins itself to (1000000, 0, 1000000) every fixed frame, so it instantiates nothing of ours and every object
> must be authored as a ZDO owned by a client — is confirmed **word for word**, and every consequence drawn from
> it here holds. One softening: it is the difference **that matters**, not the only one. Six surface members
> differ between the client and dedicated-server assemblies (`Game.FixedUpdate`, which is the pin;
> `ZNet.IsDedicated`, `Terminal.AddString`,
> `GameCamera.UpdateMouseCapture`, `ZNet.Awake`, `ZNet.RPC_PeerInfo`); the report names each and why the other
> five change nothing here.

**The active-block constraint.** A client instantiates a ZDO only while `ZNetScene.InActiveArea(zone(zdo), zone(client))`
holds (`|zone − centre| ≤ m_activeArea − 1`, 64 m zones). Outside it `RemoveObjects` destroys the instance, and a
non-persistent owned ZDO with it. Vanilla's intro survives 500 m only because the passenger's reference position rides the
bird. So the server picks a start point **inside the pilot's block**: bearing `seed` at `FlightStartDistance` (90 m),
shrunk by 12 m until `InActiveArea` holds; altitude `FlightStartAltitude` (45 m since 2026-09-08; it was 120, which put the
approach 53 deg above the horizon and made the carry unwatchable), descent at `FlightDescentDistance`
(50 m). At `m_speed` 10 m/s that is a 15–20 s flight, visible from the first frame. `cargo status` prints the runtime
`ZoneSystem.instance.m_activeArea` (decompiled default 1; the prefab may raise it) and the clamp it implies.

**Drop point** (server, XZ, no terrain): 12–15 m from the pilot on the seeded bearing, inside the same block, beside
something built (`Homestead.IsNearPlayerBuilt`, Ragnarok's Wrath). The pilot refines Y on arrival.

**Authoring** (server, public API):
```
bird = ZDOMan.instance.CreateNewZDO(start, 0); bird.SetPrefab("Valkyrie".GetStableHashCode());
bird.SetPosition(start); bird.SetRotation(look); bird.Persistent = false; bird.Distant = false;
bird.Set("VCargo_cargo", visitId); bird.Set("VCargo_target", drop); bird.SetOwner(pilotUid);
npc = CreateNewZDO(start − attachOffset, 0); npc.SetPrefab(BodyPrefab.GetStableHashCode());
npc.SetPosition(...); npc.Persistent = true; npc.Set("VCargo_ingvar", visitId); npc.Set("VCargo_seed", seed);
npc.Set("VCargo_carrier", bird.m_uid); npc.Set("VCargo_state", 0); npc.SetOwner(pilotUid);
```
`CreateNewZDO(pos, hash)` uses the hash only for the portal check, so `SetPrefab` is explicit. The pilot's
`ZNetScene.CreateObject` instantiates both with `m_initZDO`, so the keys exist **before** any `Awake` runs.

`Patch_Valkyrie_Awake` (prefix, `Priority.Low`): read the ZDO through public `GetComponent<ZNetView>().GetZDO()`. If it
carries `VCargo_cargo`: `enabled = false`, `AddComponent<CargoFlight>()`, `__runOriginal = false`; never touch
`Valkyrie.m_instance` (a real intro may be running for someone else). Otherwise `return true`. The narrow, named
exception to "never replace", on our own object only (RavenEye's shape).

`CargoFlight` (every machine that has the bird): the owner flies vanilla `UpdateValkyrie` maths (**three waypoints,
linear steps, a banked turn; no spline**) with the target from `VCargo_target` and the floor
`max(GetGroundHeight, ZoneSystem.instance.m_waterLevel) + m_dropHeight`; `m_dropHeight = 10` is ground clearance the whole
way, and the drop fires on 0.5 m XZ proximity. Everyone watches `VCargo_dropped` and sets `animator.SetBool("dropped", true)`.
`Drop()` (owner): `bird.Set("VCargo_dropped", true)`; refine drop Y; clear the merchant's carrier link (`npc.Set("VCargo_carrier",
ZDOID.None)`, the pilot owns it now); send `VCargo_placed(visitId, npcZdo, dropPoint)` over the direct socket; fly the
fly-away waypoint (lateral, at altitude, not vertical); `m_nview.Destroy()`.

Server on `VCargo_placed`: `SetRandomEventByName("valkyries_cargo", dropPoint)` (banner and 300 s clock for everyone within
96 m), `VisitState.phase = dropped`, `endWorldTime = now + MerchantLifespanSeconds`.

**Fallback** if `cargo prefab Valkyrie` shows the prefab unregistered with `ZNetScene` (the server would `DestroyZDO` an
unresolvable prefab in `CreateObjectsSorted`): the server sends `VCargo_visit` and the pilot instantiates locally with a
pending flag, the v1 design. Documented, not coded, until the check says it is needed.

### 3.3 The merchant — `Client/CargoMerchant.cs`, `Patches/Patch_Humanoid_Awake.cs`, `Patch_Character_InIntro.cs`, `Patch_Character_Damage.cs`

**Body contract.** `ZNetView`, `ZSyncTransform`, `ZSyncAnimation`, `Humanoid`, `MonsterAI`, a `SkinnedMeshRenderer`
under an `Animator` that declares the vanilla parameter set (section 11), a `CapsuleCollider` (`Character.m_collider`),
and optionally a `Visual` child. **0.1 body: `Dverger`** (config `Server.BodyPrefab`), checked at boot by `cargo prefab
Dverger`. Every part of that contract stays the `BodyPrefab` clone's for good: as BUILT (P8, section 11.6) Ingvar's body
does **not** replace it. It is ADDED as a child of the same clone and the clone's renderers are switched off, so the
`Animator` that declares the vanilla parameter set, the `CapsuleCollider` and the whole component set are still the
prefab's. Ingvar's own animator declares nothing of vanilla's (section 11.4) and never has to.

`Patch_Humanoid_Awake` (postfix, default priority, try/catch): if the ZDO carries `VCargo_ingvar`, `AddComponent<CargoMerchant>()`.
Every machine.

`CargoMerchant.Awake` (every machine): `character.m_name = "Ingvar the Far-Travelled"`; `NpcTalk` disabled if present;
`m_nview.Register("VCargo_say", RPC_Say)` and `Register("VCargo_vanish", RPC_Vanish)`; subscribe to `VisitState.ValueChanged`.
Owner only: `monsterAI.MakeTame()`, `m_aggravatable = false`, `m_randomMoveRange = 1.5f`, `humanoid.UnequipAllItems()`.

**Carried.** While `zdo.GetZDOID("VCargo_carrier")` is not `None`, every machine's `LateUpdate` finds the bird with
`ZNetScene.instance.FindInstance(carrierId)` and pins `transform` and `m_body.position` to `m_attachPoint − TransformVector(m_attachOffset)`
(vanilla `SyncPlayer` sets the rigidbody too; there is no IK anywhere in this, on either side).
`Patch_Character_InIntro` (postfix, default priority, `___m_nview`): `__result = true` while the carrier link is set, so
the owner's physics zeroes velocity each step instead of accumulating a 120 m fall (`Character.CustomFixedUpdate`, the
`InIntro()` branch). The falling animation plays while he hangs; that is what hanging looks like. When the link clears he
falls the last 10 m; non-players take no fall damage (`UpdateGroundContact`: `IsPlayer() && num > 4f`).

**On the ground** (owner writes `VCargo_state`; every client reacts):
- `0 carried` → link cleared and `IsOnGround()`: landing effect (rule in 3.6), `SetFollowTarget(pilot's GameObject if
  instantiated here, else nearest Player)`, state `1`.
- `1 approaching` → within `ApproachDistance` (3.5 m) or 20 s: `SetFollowTarget(null)`, `SetPatrolPoint()`, state `2`;
  the **callout** (section 7, `large: true`, line = `VCargo_seed % lines.Count`, identical on every screen).
- `2 trading` → if the nearest player is beyond 12 m for 5 s, state `1` toward them; never beyond `m_eventRange`.
- `3 leaving` → set by `RPC_Vanish`.
Ownership may pass to a nearer client mid-visit (vanilla); the state is in the ZDO, so the new owner continues.

**Immortal.** `Patch_Character_Damage` (prefix, `Priority.Low`, `___m_nview`): `VCargo_ingvar` → `__runOriginal = false`. One
ZDO int read inside the hit pipeline; its try/catch returns `true` so vanilla runs if anything throws.

**Interaction.** `CargoMerchant : Hoverable, Interactable` (the body has no `Tameable`): hover shows the name, `[E] Trade`,
`[Shift+E] Send him on his way`, and the countdown from `VisitState.endWorldTime`. `alt` twice within 3 s → `VCargo_dismiss`.
Plain → **open the Cargo Terminal directly** (section 3.4) and send `VCargo_open`. Vanilla `StoreGui` is never involved and
never patched.

### 3.4 The Cargo Terminal — `Client/Terminal/*.cs`, `Net/CargoRpc.cs`, `Core/Deal.cs` (pure)

**Locked decision: a terminal of our own** (owner's choice 2026-09-06), **drawn with VikingOS's own toolkit.** VikingOS
(`Wubarrk-VikingOS_BETA`, MIT, installed on Ravenrest and both raveniron profiles) draws every panel procedurally in
IMGUI from `SharedUI.GiltFrameTheme`, with `SharedUI.UIFocus` owning the cursor and input focus; its trade window is built
the same way. Those two files are Wu'barrk's shared source (`libs-Tools/SharedUI/`, pulled into his mods by
`<Compile Include>`; see his `SharedInfrastructure.md`). We take them the same way: vendored into `Libs/SharedUI/` with an
origin header (VikingOS 0.9.8) and re-synced when he updates them. **No runtime dependency on VikingOS**: every one of its
classes is `internal`, it is a beta, and a Cargo player on another server may not run it. With it installed, the terminal
simply looks native to it; its layout editor walks `RectTransform`s and leaves an IMGUI window alone. Two ServerSync copies
(its internalised one, ours) coexist as every ServerSync mod pair does.

The terminal is drawn from the mod's single `OnGUI` (house rule 2 applies to `Update`; one `OnGUI` per mod is the same
discipline), sized in resolution-independent units, black-gold by construction; `Client.Theme` picks the metal colour.
Item icons are drawn with `GUI.DrawTextureWithTexCoords` and the sprite's `textureRect`, because `Sprite.texture` is the
whole atlas page (Wu'barrk's chat-RPC facts §8). `UIFocus.SetWantsCursor(id, true)` while open, released on close **and on
session end** (`Game.Logout`, `ZNet.Shutdown`), the cursor-leak lesson from BarrkUI §6.

```
┌───────────────────────────────────────────────────────────────────────┐
│ Ingvar the Far-Travelled — Valkyrie's Cargo          03:42 left   [X] │
│ His purse 812c        Your coins 840c                                 │
├──────────────────────────────┬────────────────────────────────────────┤
│ HIS WARES        stock  price│ YOUR GOODS HE WANTS       has   pays   │
│ [i] Iron           8/20  38c▲│ [i] Linen thread x14    31/90   9c     │
│ [i] Ruby          12/45  19c▼│ [i] Deer hide x6        40/120  3c     │
│ [i] Black Core     0/6 SOLD  │ [i] Flax x40            60/180  4c     │
├──────────────────────────────┴────────────────────────────────────────┤
│ YOU GET            count  all  x  value │ YOU GIVE        count  all  x  value │
│ [i] Ruby @ 19c     [ 1 ] [all][x]  19c  │ [i] Linen @ 9c  [ 2 ] [all][x]  18c  │
│ [Confirm deal] [Clear] [Cover it with my goods]   you pay 1c   [Send him off] │
└───────────────────────────────────────────────────────────────────────┘
```

(The sketch is the 2026-09-08 shape. Until the first playtest the header carried a `Pay with: Coins | Barter`
switch and the tray was one line of text; the playtest found the switch unreadable and the click-per-unit staging
unusable, so the switch went, the tray became YOU GET beside YOU GIVE with a count box and an "all" button on
every line, and the one balance line says who pays whom. `EnableBarter=false` now means the client refuses goods
staged beside a ware, since there is no mode to hide.)

**Display terminal.** The panel renders `MarketState` and never computes a price. Clicking a ware adds it to the staging
tray (Shift five, Ctrl twenty, a typed count or "all" for the rest); clicking one of your goods offers it. The tray shows the server's current numbers; `MarketState.ValueChanged`
redraws it while open, so **B watches the price tick while A buys**.

**The Deal** (one primitive, pure, tested):
```
Deal { v, visitId, nonce, wanted: [(prefab, n)], offered: [(prefab, n)], coinsOffered, expected: {wantedUnits[], offeredUnits[]} }
net = price(wanted) − value(offered)      // at the SERVER's current prices
net > 0 : player pays net coins;  net < 0 : merchant pays −net from his purse
```
Buy = wanted lines + coins (one line per ware, more than one ware per deal since 2026-09-08). Sell = offered only. Barter = wanted + offered (the tray is the basket; auto-fill picks highest value
first until value ≥ price; change in coins). `expected` carries the unit prices the player saw (v5's `ExpectedPrice`).

**Guarantees, stated honestly** (v5's five, corrected by the review):
1. **Replay immunity**: 64-bit `nonce` per deal; the server keeps a 500-entry ring of settled nonces per visit and refuses
   repeats (a direct socket does not stop a replayed packet by itself).
2. **Server-side atomicity**: one main thread, one visit: validate in `Core/Market.Settle`'s order (empty, stale visit, duplicate nonce; the
   each wanted line: unknown, not on shelf, bad count, the same ware twice, sold out, price changed; each offered line: unknown, bad count, over max, price
   changed; coins short; purse empty), then commit stock, purse, `MarketState`, then answer. Price is checked before
   the purse so Reconfirm gets the price answer, not `purse_empty`. Two deals for the last unit arrive
   in order; the second gets `sold out`.
3. **Price-change handling (provisional: reject-and-reconfirm)**: if `expected` no longer matches, the server answers
   `price_changed` with the new numbers; the tray keeps its contents, the changed line turns amber, and `[Confirm deal]`
   accepts the new price. The alternative, dissolving every open tray on any tick, is one paragraph away and was declined
   for now because at a busy base it empties other players' trays on every deal and hands a griefer a lever.
4. **Peer isolation**: trays are local; the server has no per-player staging state, only settled deals.
5. **No local inventory mutation before the server's answer**: on `VCargo_dealt(ok)` the client removes and adds
   (`Inventory.RemoveItem(item, amount)` / `AddItem`; check before remove; prefab-name keys, per
   `VANILLA-PIECE-INTEROP-FACTS` §1–2) and plays `m_buyEffects` / `m_sellEffects`; on refusal it changes nothing.

**Wire.** `VCargo_open` / `VCargo_close` (subscription), `VCargo_deal` → `VCargo_dealt(deliveryId, ok, reason, coinsDelta, items[], newPrices?)`
on the **direct peer `ZRpc`** (client side `ZNet.GetServerRPC()`, server side each `peer.m_rpc`, keyed on the `ZRpc`
instance as `RosterSync` does; registered per session, never in plugin `Awake`, where `ZRoutedRpc.instance` is still
null). Refusals name their reason: sold out, over his max, purse empty, coins short, inventory full, visit over, unknown
item, bad count, stale visit, duplicate nonce, price changed. The server sends `VCargo_say(index)` after a deal so every screen
sees the same reaction.

**Delivery, taken from VikingOS's escrow ("Trading, Without Trusting Anyone").** Its rule: the server keeps offering a
delivery until the client confirms it has it, and the client remembers what it has already been paid, so a redelivery is
recognised rather than applied twice. Ported, not depended on:
- Every accepted deal gets a `deliveryId` = `{salt}-{visitId}-{seq}`: the salt is the world's (`demo` for the demo),
  visit ids are monotonic and persisted, the sequence is persisted, so an id never repeats across restarts, worlds or
  the demo, and the inbox can be one file for every server. The client applies it once and records the id in a bounded local inbox (500
  ids, sidecar file in the config dir, the `TradeInbox` shape without Newtonsoft), then sends `VCargo_ack(deliveryId)`.
- The server keeps an **owed ledger** per player id in the world sidecar (`owed` rows): a deal it committed but never
  saw acked. On `VCargo_ack` the row clears. At session start the client sends `VCargo_claim`; the server redelivers every owed
  row over the direct socket. A player who dropped between the merchant's commit and their own inventory write gets their
  goods next login, and never twice.
- No heartbeat: the merchant is the server, and the socket's own connection state is the heartbeat. VikingOS needs one
  because both parties are clients.

**Panel rules** (in the mod's single tick, mirroring `StoreGui.Update`): close beyond 5 m of the merchant, on Escape or
Use, when the inventory or map opens, when the local player dies, and when `VisitState.phase` becomes `leaving`.

### 3.5 Economy — `Core/Market.cs` (pure), `Server/MarketStore.cs`

Catalogue lines `Prefab:BasePrice:TargetStock:MaxStock:Kind` (`Ware` = he sells and buys, `Want` = buys only); the defaults,
their reasons and the item data they were checked against are in `docs/CATALOGUE.md` and `docs/data/`. Unknown prefab
names at boot are dropped with one log line, never a crash; the off-game tests validate the defaults against the item
table so a misspelling fails on the desk. The line is editable on a running server (2026-09-07: `cargo catalogue
add|remove|reset`, or Configuration Manager as an admin): the director rebuilds the market as soon as no visit is
running, through `Market.WithCatalogue`, carrying stock and drift stamps by prefab, the purse, this visit's baseline,
the visit number and the delivery sequence; a new row starts at target, a dropped row goes, a lowered max clamps. The
nonce ring does not carry, which is why a running visit makes the change wait. A prefab the game has no item for
(`ZNetScene.GetPrefab`, `ItemDrop`) is dropped with one log line at boot and on every edit, and refused by `add` in words. Price `= base × clamp((target / max(1, stock))^α, MinMult, MaxMult)`, `α = 0.35`, clamps 0.4 / 3.0; `sell = base ×
multiplier × SpreadBuy` (0.7) rounded once, never the rounded price times 0.7. **The Fair Market Act** (2026-09-07,
§8, `docs/DECISIONS-WUBARRK.md` §2): for a `Ware`, the multiplier on the sell side only is capped at 1.0 before
`SpreadBuy` is applied — `MaxMultiplier` (3.0) × `SpreadBuy` (0.7) = 2.1 > 1 otherwise, so an empty shelf paid more
to buy back than a full one charged to sell, and buying it out then selling it straight back pumped the purse for
free (`docs/ECONOMY-SIM.md` §9). The buy price and every `Want` are untouched; `MarketRules.FairMarketAct`
(`Server.FairMarketAct`), synced+locked, defaults on. A deal is priced as a whole at the moment
of settlement, `count × unit` at the current price, and stock moves after (§8: a bulk deal beats a drip-feed, by
design). Bought units decrement, sold units increment, refused above `max`, `0` is SOLD OUT. Drift between visits:
`stock += (target − stock) × (1 − 0.5^(days / halfLife))` in world time, one half-life per kind (`WareHalfLifeGameDays` 0 = never, `WantHalfLifeGameDays` 3: the
owner, 2026-09-07, from the sweep in `docs/ECONOMY-SIM.md` §10 — a Ware keeps what trading left, a Want half-clears in three
days so he never fills up for good), a day being
`EnvMan.instance.m_dayLengthSec` read once at boot (the compiled default is 1200; the scene is expected to say 1800 and
`cargo status` prints what it found; 1800 is assumed only with no `EnvMan`). Purse: `PurseCoins` + `PurseCarryPercent`
of last takings, capped at 3×. Integer coins, min 1, never negative, never NaN; the rules are sanitized into their
ranges when the market is built.

Persistence: Cairn's `Persistence.cs` clone, `valkyriescargo_{worldUid}.dat` beside the world; first line `format\t1`;
tagged rows `stock`, `purse`, `purseStart`, `visit`, `seq`, `cool` and `coolbase` (cooldowns saved as remaining
seconds and rebased to the clock at load, so a restart never mixes clocks); on a format mismatch, upgrade or reset with a `.corrupt` copy; write-behind,
cadence save, `OnDestroy` flush, `.tmp/.bak` discipline.

Tests (off-game, shipping source against stubs): price monotonic in stock; clamps; sold-out and over-max; the three deal
shapes; `expected` mismatch → `price_changed`; nonce replay rejected; purse floor; drift converges; catalogue parser
tolerates junk; `VisitState`/`MarketState`/Deal encoders round-trip; format-version mismatch path. Each proven to fail
without its fix.

### 3.6 Effects, speech and departure

**Effect rule** (decided at boot, per prefab, logged once): a prefab with a `ZNetView` is created by the **owner** only
(vanilla replicates it); one without is created by **every client** locally. Applied to `Odin.m_despawn`'s entries and the
landing effect. No effect is ever an RPC to everybody.

**Speech.** Scripted moments derive from `VCargo_state` + `VCargo_seed` on every client; reactions come as `VCargo_say(index)` from the
server (routed, object-targeted; an index into our table, never text). `Chat.instance.SetNpcText` draws the bubble.

**Departure (locked: the Odin vanish).** Triggers: the event's 300 s timer (server tick sees `GetCurrentRandomEvent()` no
longer ours; polled, never edge-triggered), `VCargo_dismiss` (server calls `ResetRandomEvent()`), or `cargo dismiss`. Then:
`VisitState.phase = leaving` (terminals close); server → routed object RPC `VCargo_vanish`: farewell line, then
**`Odin.m_despawn` by the effect rule** (the public `EffectList` on the `odin` prefab, borrowed; the `Odin`
COMPONENT is never added, and its `m_ttl` on the shipped prefab is 60, not the 300 the field initialiser says --
our 300 is the event's `m_duration` and never was his); after 1.5 s the server reclaims `SetOwner(session) + DestroyZDO` (Undertow's pattern); persist;
`VisitState = none`; `cargo status` says `ended: timer | dismissed by <name> | admin`. A horn may sound too, as departure
audio; it is not the summon horn.

### 3.7 Restart, orphans, edges

- **Boot sweep**: the sidecar's `session` row is adopted when vanilla brings the event back (it saves the running
  event: name, time, position) and dropped after 15 s otherwise; P5 adds `GetAllZDOsWithPrefabIterative(BodyPrefab)`
  for `VCargo_ingvar`: rebuild or reclaim. Never an orphan.
- **Pilot disconnects mid-flight**: the bird's ZDO is non-persistent and owner-less → vanilla drops it; the merchant's ZDO
  is persistent and gets adopted by whichever client's block holds it; none within 30 s → server reclaims, visit ends.
- **Pilot disconnects mid-visit**: the merchant is adopted by a nearer client; the visit continues.
- A raid while he is there: impossible, one random event at a time. Nobody within 96 m: the clock pauses (vanilla).
  The event's `m_time` (real seconds, paused out of range) is the authority; the server republishes
  `VisitState.endWorldTime` whenever `now + (duration − m_time)` drifts more than a second from what it published (a
  pause, a resume, a sleep skip), and every `VisitClock` retargets without re-arming its one-minute warning.
- Two terminals open: both render `MarketState`; a deal from either updates both. Listen host: works as pilot and server.

### 3.8 Messages

| Name | Direction | Transport | Payload | Trust |
|---|---|---|---|---|
| `VCargo_rested`, `VCargo_comfort` | client → server | own character ZDO | int, int | client-reported, like vanilla `baseValue` |
| bird + merchant ZDOs | server → all | ZDO authoring, owner = pilot | prefab, pos, keys | server-authored |
| `VCargo_placed` | pilot → server | direct `ZRpc` | visitId, npc ZDOID, drop | pilot trusted to place (co-op carve-out) |
| `VisitState`, `MarketState` | server → all | ServerSync custom values | strings, versioned | server-only writes; ServerSync rejects others |
| `VCargo_open` / `VCargo_close` | client → server | direct `ZRpc` | visitId | subscription only |
| `VCargo_deal` / `VCargo_dealt` | client ↔ server | direct `ZRpc` | Deal / result | validated at server prices; nonce ring |
| `VCargo_ack` | client → server | direct `ZRpc` | deliveryId | clears an owed row |
| `VCargo_claim` | client → server, at session start | direct `ZRpc` | none | server redelivers owed rows |
| `VCargo_dismiss` | client → server | direct `ZRpc` | visitId | any visitor |
| `VCargo_say` | server → all in range | routed, object-targeted | line index | cosmetic |
| `VCargo_vanish` | server → all in range | routed, object-targeted | none | cosmetic + owner effect |
| `SetEvent` | server → all | vanilla routed | name, time, pos | vanilla |
| `VCargo_admin` | client → server | routed | verb, arg (`visit <name>`, `dismiss`, `reset`, `save`, `catalogue add|remove|reset …`) | the SERVER checks `ZNet.IsAdmin(hostName)`, fail closed; the request is never trusted |
| `VCargo_reply` | server → client | routed | answer text | cosmetic (printed in the caller's console) |
| config | server → all | ServerSync | entries | locked; admins exempt |

Every payload starts with a format version; RPC names stay stable (a mismatch is a log line naming the side to update).

---

## 4. Patches

| Target | Kind | Priority | Injected | Why |
|---|---|---|---|---|
| `RandEventSystem.Awake` | prefix, `return true` | Low | — | register `valkyries_cargo` (Ragnarok's Wrath precedent) |
| `Valkyrie.Awake` | prefix, skip for `VCargo_cargo` only | Low | — (public `GetZDO`) | vanilla would teleport the local player and take `m_instance` |
| `Humanoid.Awake` | postfix, try/catch | default | — | add `CargoMerchant` on `VCargo_ingvar` objects, every machine |
| `Character.InIntro` | postfix, `__result=true` while `VCargo_carrier` set | default | `___m_nview` | owner physics holds still in the talons |
| `Character.Damage` | prefix, skip for `VCargo_ingvar` | Low | `___m_nview` | immortal merchant; catch returns true |

| `GameCamera.UpdateMouseCapture`, `Chat.HasFocus` | shared-source patches inside `UIFocus.cs` (VikingOS) | theirs | — | cursor and input focus while the terminal is open |

**No `StoreGui` or `Trader` patch exists in v3**: the terminal opens from our own `Interactable`. ServerSync's patches
(`ZNet.RPC_PeerInfo` and friends) and `UIFocus`'s two are third-party code we compile in, listed in CLAUDE.md as
"not ours" with their origin versions. No transpilers, no max or high
priority, every body its own try/catch logging at most three times. Our code names no private member; private fields
reach patches only by `___injection`; the one reflection seam is Cairn's `FieldRefAccess<World,long>("m_uid")` in the
persistence clone, resolved in its own method, retried, never latched.

---

## 5. Build

`ValkyriesCargo.csproj` = RavenEye's (net472, version-const generator, `libs\` check via `tools\fetch-libs.ps1`,
`AllowUnsafeBlocks` false) plus the references ServerSync and an IMGUI terminal need: `assembly_utils` (`SyncedList`,
`GetStableHashCode`), `UnityEngine.UI`, `Unity.TextMeshPro`, `UnityEngine.UIModule`, `UnityEngine.IMGUIModule`,
`UnityEngine.TextRenderingModule`, `UnityEngine.InputLegacyModule`, `UnityEngine.AnimationModule`,
`UnityEngine.PhysicsModule`. ServerSync is `Libs\ServerSync.cs`, compiled in; no ILRepack, no NuGet (Phase 0 decision).
`tools\package.ps1` ships the one DLL. `tests\CoreTests` compiles the shipping `Core\` and `Net\` sources against stubs
(Ragnarok's Wrath ∪ RavenEye) and never sees ServerSync or the terminal.

**VikingOS shared source.** `Libs\SharedUI\GiltFrameTheme.cs` and `Libs\SharedUI\UIFocus.cs`, vendored from Wu'barrk's
`libs-Tools\SharedUI\` (MIT, origin and version in the file header), compiled in like any other source. They need
`UnityEngine.IMGUIModule.dll`, `UnityEngine.TextRenderingModule.dll` and `UnityEngine.InputLegacyModule.dll`, which
`tools\fetch-libs.ps1` does not copy today: add them. `UIFocus` carries two Harmony patches of its own
(`GameCamera.UpdateMouseCapture`, `Chat.HasFocus`); they are listed in section 4 as shared-source patches. The Unity project (when it exists) is a **sibling directory**, not
`<mod>\Unity\`, so no `Compile Remove` block is needed and the test harness glob stays clean.

**Unity for bundles.** The game runs on 6000.0.61f1, so bundles are built with that Editor (never newer). This machine
has 6000.5.4f1 only: install 6000.0.61f1 through the Hub, or build on the Linux box that has it. Windows build line:
```
& "C:\Program Files\Unity\Hub\Editor\6000.0.61f1\Editor\Unity.exe" -batchmode -quit -projectPath <proj> -executeMethod MerchantBundleBuilder.BuildKit -logFile <log>
```
No `-nographics` (texture bakes need a graphics device); on Linux, `unity run <proj> -- -executeMethod ...` manages the
flags itself. Lessons from `RagnaroksWrath/docs/reference/JOTUNN-AND-HEADLESS-AUTOMATION-FACTS.md` §7–§8 (measured in this
household) and AwayFromHome: `ENABLE_UNITY_COLLECTIONS_CHECKS` during `BuildAssetBundles`, check the bundle file size,
tangents regenerated, DXT5nm swizzle for normals, DXT5 never crunched for normals, the copy step from the Unity output to
the embedded resource. There is no §9 in that document; the "windows-mono not needed" claim is unverified.

---

## 6. Configuration (`com.raveniron.valkyriescargo.cfg`)

`Server.*` synced and locked; `Client.*` local.

```
[Server]
LockConfiguration            true
Enabled                      true
RequireRested                true
MinComfortLevel              4
MinBaseValue                 1
DaytimeOnly                  true
EventCheckIntervalMinutes    25
EventChancePercent           25
PlayerCooldownMinutes        60
CooldownRadius               60
MerchantLifespanSeconds      300       = RandomEvent.m_duration (NOT Odin.m_ttl: the prefab says 60)
ApproachDistance             3.5       read on the CLIENT that owns the merchant (P5, CargoMerchant)
BodyPrefab                   Dverger   the ENGINE prefab the merchant is cloned from (Character, MonsterAI, collider)
CustomBody                   true      put Ingvar's own body on that clone, from the bundle embedded in the DLL;
                                       false keeps the stand-in visible, and so does a build with no bundle (P8)
FlightStartDistance          90        30-200; clamped into the pilot's active block at runtime, shrunk in 12 m
                                       steps, never below FlightPlan.MinimumStartDistance (30)
FlightStartAltitude          45        30-400 (the code clamps at 400, so the config says 400; was 120 until 2026-09-08)
FlightDescentDistance        50        10-200; also capped at MaxDescentFraction (0.75) of the run
FlightSpeed                  8         2-40; ours, not the prefab's 20; read on the CLIENT that owns the bird (P4)
FlightTurnRate               45        5-360; ours, not the prefab's 20; read on the CLIENT that owns the bird (P4)
Catalogue                    (72 entries; the authoritative list with every number's reason is docs/CATALOGUE.md,
                             built from docs/data/items-valheim-2026-07-31.tsv: 18 Wares he sells and buys back,
                             54 Wants he only buys. Bases anchored so he pays Haldor's rate for the four vanilla
                             valuables at target stock; every prefab name verified against the dump.
                             Editable live: cargo catalogue add|remove|reset (admin); applied as soon as
                             no visit is running (2026-09-07).)
PriceElasticity              0.35
MinPriceMultiplier           0.4
MaxPriceMultiplier           3.0
SpreadBuy                    0.7
FairMarketAct                true      caps a Ware's buy-back at base x SpreadBuy (the Fair Market Act, §8);
                                       off restores the pre-2026-09-07 number, MaxPriceMultiplier x SpreadBuy
WareHalfLifeGameDays         0         0-365; 0 = never (the owner, 2026-09-07): a Ware keeps what trading left
WantHalfLifeGameDays         3         0-365; a flooded Want half-clears in three game days, so he keeps buying
PurseCoins                   1500      was 800 until 2026-09-07 (docs/DECISIONS-WUBARRK.md); the carry is
                                       measured on the GROSS coins a visit took in, not the net
PurseCarryPercent            50
EnableBarter                 true      read on the CLIENT: the terminal refuses goods staged beside a ware and
                                       hides "Cover it with my goods"; the server settles a barter deal either
                                       way (docs/CONFIG-SHAKEDOWN.md)
BarrkBotExport               true      the three barrkbot_cargo_*.json mirrors under BepInEx/config/ValkyriesCargo,
                                       written after the sidecar saves, never the source of truth (P12)
                                       (PriceChangePolicy was deleted 2026-09-07: nothing read it; only Reconfirm
                                       exists. Section 3.4 guarantee 3 and section 8 still carry the open choice.)

[Client]
ShowArrivalMessage           true
ShowPriceTrend               true
Theme                        Vanilla     Vanilla | BlackGold
TerminalScale                1.0
TerminalBackdropAlpha        0.4         0-1; the black backdrop behind the text (2026-09-08); 1 = the solid panel
```

---

## 7. Ingvar's lines (plain text; indexes cross the wire, never text)

Arrival (by seed): "Hail, hearth-keeper! Ingvar the Far-Travelled, down from the Bifrost with cargo from nine realms!" ·
"By Odin's ravens, that Valkyrie has never heard of a soft landing! Well met, warrior. Ingvar, at your service." · "The
wandering trader is come! Five minutes of the Allfather's patience, and then I am mist again." · "From Yggdrasil's high
branches to your front gate. Make it quick, my escort circles overhead."
Open: "Ah, the sweet smoke of a well-earned hearth. What do you bring, and what do you need?"
Buy: "Sold, and may it serve you." · "A fair price, for today." · "Scarce goods, dear goods."
Sell: "I'll take those." · "Enough of these and I'll stop paying, mind." · "The Plains will want that."
Refuse: "I've all the linen a man can carry." · "My purse is bare, friend." · "That I do not deal in."
Price changed: "The wind shifted while you counted. Say yes again and it's done."
One minute: "Hurry your bargaining, friend! The Valkyrie's horn sounds in the wind."
Dismiss, first press: "Send me off, then? Ask once more and I'll go." · Farewell: "The Allfather calls me back to the mist!"
Banner start "Valkyrie's Cargo has landed", end "Ingvar has gone back to the mist".
Pilot's private line at dispatch: "Wings beat in the upper skies... an emissary from Asgard descends."

---

## 8. Decisions

| Decision | Answer | Status |
|---|---|---|
| Authority split | Server: config, schedule, event, existence, market, clock, destruction. Owner client: motion. Every client: rendering. Player inventory: vanilla, mutated only after the server answers | locked |
| ServerSync | Compiled in as shared source (`Libs/ServerSync.cs`), `ModRequired`, all `Server.*` locked, `VisitState` + `MarketState` custom values; first in the family | locked by the brief |
| Rested and comfort | On the player's own ZDO, read by `GetAllCharacterZDOS()` | locked |
| Client-reported eligibility | `VCargo_rested`, `VCargo_comfort`, and vanilla's own `baseValue`, `playerName`, `dead` and position are all written by the client on its own character ZDO and read by the server's scheduler. **Accept.** Worst case is an undeserved visit: a merchant, on a cooldown the liar spends, at a place the liar named. Server-side comfort would mean reimplementing `SE_Rested.CalculateComfortLevel` (static, local, walks the pieces around the player's own transform) against the server's piece list — a large new surface to stop somebody giving themselves a shop (`docs/TRUST-BOUNDARY.md` §3) | proposed: accept (P11, 2026-09-07; owner to confirm) |
| The merchant's state key | `VCargo_state` is a client-to-client rendering hint (which pose on each screen). The server writes it at authoring and on adoption and never reads it; the visit's phase lives in `VisitState`, written by `VisitSession.SetPhase`. A server decision must never consult it, because whichever client's block holds the persistent merchant owns the ZDO and writes the key (`docs/TRUST-BOUNDARY.md` §3) | locked (P11, 2026-09-07; verified against P5 as merged) |
| Event tie-in | Real `RandomEvent`, `m_random=false`, scheduled by us, ended by vanilla or `ResetRandomEvent` | locked |
| Object creation | Server authors both ZDOs with owner = pilot; pilot's `ZNetScene` instantiates them | proposed; v1 pending-flag spawn is the fallback |
| Flight start | Inside the pilot's active block, ~90 m out, ~45 m up (was ~120: unwatchable); never 500 m | locked by the engine |
| Carry | Real merchant pinned to the talons on every machine; `InIntro` postfix on the owner; no IK | locked |
| Where market state lives | ServerSync custom values + sidecar save; never on the merchant ZDO | locked by the engine |
| **Trade UI** | **A terminal of our own**, opened from our `Interactable`; `StoreGui` untouched | **locked (owner, 2026-09-06); built (P7)** |
| Terminal toolkit | IMGUI on VikingOS's `GiltFrameTheme` + `UIFocus`, vendored shared source (MIT); no runtime dependency on VikingOS | locked (owner: "we have VikingOS to use") |
| Delivery semantics | At-least-once `VCargo_dealt` with a client inbox of applied delivery ids; server owed ledger by platform id, claimed at login (VikingOS's escrow rule, ported) | built (P6) |
| Price-change policy | Reconfirm; Teardown behind `PriceChangePolicy` | provisional (owner unsure) |
| Deals | Direct `ZRpc`, server-validated, nonce ring, `expected` prices, `MarketState` after | locked |
| Departure | The Odin vanish, `Odin.m_despawn` by the effect rule; horn as extra audio only | locked by the brief |
| Effects | Owner-only if the prefab is networked, everyone if not; decided at boot | locked |
| Speech | State + seed for scripted lines; server `VCargo_say(index)` for reactions | proposed |
| 0.1 body | `Dverger`, tamed, following, immortal; custom body later behind the contract | locked for 0.1 |
| Ghost mode (F11) | Hostiles neither target nor fear Ingvar: any pair with the merchant in it answers "not enemies" at the static `BaseAI.IsEnemy(a, b)`, the one gate every targeting path, hit filter and the enemy HUD go through (`Patches/Patch_BaseAI_IsEnemy.cs`; the decision pure in `Core/Ghost.cs`). Neither faction-only nor the aggro magnet the audit's F11 described | **locked (owner, 2026-09-07: "ghost mode")** |
| Runtime material edits | Not in 0.1 (no custom body). When the body comes: bake the finished material into the bundle; no runtime `SetTexture` on a creature material | proposed |
| **Body animation** | **Ingvar has his OWN Animator**: the bundle's controller and clips (Walk, Idle, Talk, Hello, Shrug, Nod), driven by `CargoMerchant` from the agent's velocity and the visit phase. No mapping onto the Dverger or any vanilla rig; vanilla's animator parameters (section 11.4) are not his contract | **locked (owner, 2026-09-06: "give Ingvar his own animator")**; **built (P8 loader): PlayableGraph over the six clips, no controller in the bundle** |
| Source art in the repo | `models/ingvar.fbx` + `models/ingvar_albedo.png` (11 MB) are the one named exception to "no binaries" (WORKSPLIT §4, PR #4); the bundle still ships in the package, Meshy's raw output stays out | locked (Wu'barrk decided, Don agreed 2026-09-06) |
| Lifespan / dismissal | 300 s event clock; Shift+E twice; any visitor | locked / proposed |
| Restart mid-visit | Resume: vanilla saves the running event with the world; the director adopts it from the sidecar's `session` row within 15 s of boot, else the visit is over | built (P6) |
| Deal pricing | The whole quantity at the price on screen when confirmed; stock moves after. A bulk deal beats a drip-feed, bounded by his purse and his stock | proposed (review 2026-09-06) |
| The round trip (Fair Market Act) | A Ware's buy-back multiplier clamped at 1.0 in `Market.PaysFor`, not a lower `MaxPriceMultiplier`: he never pays more than `base × SpreadBuy` for something he sells, but still charges the full 3.0× and still pays a `Want` unclamped. `MarketRules.FairMarketAct`, synced+locked, default on | locked (owner, 2026-09-07); built |
| Visit and delivery ids | Visit ids monotonic and persisted; `deliveryId = salt-visit-seq`, the world's salt (`demo` for the demo), seq persisted | proposed (review 2026-09-06) |
| Cooldown persistence | Saved as remaining seconds, rebased at load | proposed (review 2026-09-06) |
| Day length | `EnvMan.instance.m_dayLengthSec` read when the director starts and printed by `cargo status`; 1800 assumed only without an EnvMan | **verified 1800 s on StormTest 2026-09-06** |
| Forced visits | `cargo visit` ignores cooldowns, keeps every other gate | proposed (review 2026-09-06) |
| Build | net472, `libs\` via fetch-libs, `ILRepack.targets`, `AllowUnsafeBlocks` false; Unity project as a sibling directory; Editor 6000.0.61f1 for bundles | proposed |
| Where bundles get built | Wu'barrk bakes it: he is the Unity side and holds 6000.0.61f1. The bake is reproducible from the repo's source art on any machine with that Editor (`tools/setup-ingvar-unity.ps1`, then the Editor menu or the `unity` CLI, then `-Embed`), so nothing depends on one box | locked (owner, 2026-09-06: "wubarrk is also the unity guy") |
| Console prefix | `cargo` — `status`, `version`, `engine`, `prefab <name>`, `body`, `stock`, `deal`, `claim`, `terminal`, `catalogue list`; admin: `visit [player]`, `dismiss`, `reset`, `save`, `catalogue add|remove|reset`. From a client the admin verbs ride `VCargo_admin`; a dedicated console names the player | built (P3, P7; catalogue 2026-09-07) |
| Catalogue edits on a running server | `cargo catalogue add|remove|reset` (admin) edit the synced `Server.Catalogue` entry itself, so the sync, the lock, the cfg file and the export follow; the director rebuilds the market as soon as no visit is running through `Market.WithCatalogue` (stock, drift stamps, purse, visit number and delivery sequence carried by the sidecar's own rows; the nonce ring is not, which is why a running visit makes the change wait, once in the log and always in `cargo status`); a prefab the game has no item for is dropped, with one log line, at boot and on every edit. No sell-only kind | locked (owner, 2026-09-07: "no sell kind"); built |
| Dependencies | BepInExPack only. Re-affirmed by the owner 2026-09-07: P12's `ValheimModding-JsonDotNET` dependency (two serializer calls) was replaced the same day by the pure `Core/Json.cs`, a writer and never a reader | locked |

---

## 9. Milestones (each with its proof)

1. **Repo, boot, ServerSync.** Merged DLL boots on client and dedicated server; a client on another version is refused;
   `LockConfiguration` makes a client's edit read-only; `cargo prefab Valkyrie|Dverger|odin` dumps components and effect
   prefabs. Proof: logs and dumps in CLAUDE.md.
2. **Pure core.** `Market`, `Scheduler`, `Deal`, `VisitClock`, the encoders, the persistence rows: tests green and
   mutation-proven. Needs none of the open decisions. Proof: `tools\run-tests.ps1`.
3. **Eligibility and event.** `VCargo_rested`/`VCargo_comfort` in `cargo status`; `cargo visit` starts the event with banner and
   300 s clock, and it ends itself. Proof: server log, one screen.
4. **Authored flight.** Server creates the bird ZDO; the pilot flies it; a second client 60 m away sees it; no player moved.
   Proof: two screens, `m_instance` untouched (log).
5. **Carried merchant.** Both clients see him in the talons, dropped, landing, walking, calling out; immortal; dismissal;
   one Odin puff on both; restart mid-visit resumes or cleans. Proof: section 10, items 1–8.
6. **Terminal and market.** Terminal renders `MarketState`; buy, sell, barter deals round-trip; the simultaneous last-unit
   test; B's tray ticks while A buys; price-change reconfirm. Proof: items 9–15.
7. **Release 0.1.0.** README with the authority table and the "not yet verified" list; CHANGELOG; package; Hexium (name
   check first), Thunderstore, GitHub tag. Adversarial review before.

---

## 10. What to verify in-game (not yet done)

1. Client on version X joins server on version Y: refused with the ServerSync message.
2. Client edits `MinComfortLevel` locally while connected: read-only; server value wins in `cargo status`.
3. Rested at a lit base: `cargo status` shows `rested=1 comfort=N baseValue≥1` for that player.
4. `cargo visit`: pilot gets the private line; bird appears inside its block, descends with Ingvar in the talons.
5. Second client 60 m away sees the bird, the passenger, the wing change at the drop, the fall, the walk, the callout.
6. Ingvar stops ~3.5 m off, faces the pilot, never attacks, cannot be hurt; greylings hitting him do nothing.
7. Shift+E twice: farewell, **the Odin vanish exactly once on each client**, gone, banner ends, terminals closed.
8. Restart mid-visit: right time left, or gone with a log line saying why.
9. Two terminals open: same rows, same prices; A buys iron; B's tray ticks without reopening.
10. Both confirm the last Black Core in the same second: one gets it, the other reads `sold out`, stock is 0 once.
11. A stages a ruby; B buys rubies; A's line turns amber with the new price; A confirms; deal settles at the new price.
12. Sell linen: coins arrive; his linen price drops; past his max the line refuses.
13. Barter a ruby with iron scrap: change in coins; purse moved by the change only.
14. Purse to zero: selling refused, buying works, coins refill the purse.
15. Next world day: stock drifted toward target; prices relaxed.
16. Listen host as pilot: whole loop with no dedicated server.
17. Pilot disconnects mid-flight, and separately mid-visit: bird gone; merchant adopted or reclaimed within 30 s.
18. Offshore island base: eligible; start point inside the block; bird above water; drop on the island.
19. Randomness: an hour with eligible players and no `cargo visit`: visits arrive on the roll, never on demand.
20. Not shipped in 0.1: no horn item exists; the body is the Dverger.

---

## 11. The custom body (later): what the model needs before section 3.3's contract can hold

Source of truth: the **resized** GLB from the v5 zip (1.37 m tall). Measured: one unrigged mesh, 1,000,905 vertices,
1,854,666 triangles, no clips, 4K JPEG colour and normal, 2K roughness, 76 MB. A statue. In order:

1. **Retopology or decimation** to ~30k triangles; re-bake the maps onto the low mesh at 2048².
2. **Rig**: Humanoid skeleton with skin weights (Mixamo auto-rig fast, Rigify controlled); an `AttachPoint` empty between
   the shoulder blades (v5's `(0, 1.25, −0.35)` as the start).
3. **Clips**: idle, walk, talk gesture, hang. Export FBX or glTF **with** skin and clips.
4. **The animator contract (decided 2026-09-06: his own; BUILT P8).** **The bundle needs no `AnimatorController`,
   and no `ZSyncAnimation` entry.** It needs exactly what `tools/unity/IngvarBundleBuilder.cs` already produces:
   the tagged FBX with its `SkinnedMeshRenderer`, its 24 bones and the six clips as sub-assets (Walk 4.21 s,
   Idle 10.00 s, Talk 5.17 s, Hello 3.79 s, Shrug 2.00 s, Nod 1.25 s; all in place), plus the texture.
   `Client/IngvarBody.cs` plays them through a `PlayableGraph`: one `AnimationPlayableOutput` on the prefab's own
   `Animator`, an `AnimationMixerPlayable`, one `AnimationClipPlayable` per clip resolved BY NAME (a clip the
   bundle does not carry is logged once and stays at weight 0). Lengths are read off the clips at runtime; the
   numbers above are only fallbacks.
   **Speed comes from displacement, not from the network.** The driver measures how far its own transform moved
   this frame, smoothed over 0.15 s, so every machine derives the same Idle/Walk blend from the same replicated
   position: no `m_syncFloats` entry, no trigger, no extra ZDO key, and nothing that can desync. The blend maths
   is `Core/BodyMotion.cs` (pure, 78 off-game checks): idle below 0.05 m/s, walk above 0.06, 0.15 s crossfade;
   one-shots blend in over 0.06 s, own the body, and hand back at 85% of the clip's own length. `Greet()`,
   `Talk()`, `Shrug()` and `Nod()` are the public API P5 calls on every machine from the ZDO state and `VCargo_say`.
   Vanilla's parameter set (`forward_speed`, `onGround`, the triggers `Character` and `MonsterAI` write) is NOT
   his contract; nothing maps him onto the Dverger skeleton.
   The `Armature` node carries scale 0.01 (cm to m): correct at 1.370 m, never "fixed".
5. **Unity 6000.0.61f1**, then the section 5 build lessons. As BUILT (`tools/unity/IngvarBundleBuilder.cs`) the bundle
   carries exactly two assets — the tagged FBX and its 2048² texture — and from the FBX a `SkinnedMeshRenderer`, the
   24-bone rig, the six clips as sub-assets, and the material the importer makes (`ImportStandard`), not a creature
   donor's. It carries **no `AnimatorController`** (item 4) and **no `CapsuleCollider`**: the merchant's collider is
   the `BodyPrefab` clone's own, and a second one on the body would put a second collider on the character layer —
   `Client/BodyLoader.cs` attaches the body as a child and leaves `Character`'s collider alone.
   Target bundle 5–10 MB embedded as a resource; the 76 MB source never ships and never enters the mod repo.
6. **Loader (BUILT, P8: `Client/BodyLoader.cs`)**: swap the body under the same `Humanoid`/`MonsterAI` prefab clone,
   not a `MeshFilter`; keep `Character`'s component set intact. Verified by `cargo prefab` before and after.
   As built the swap is ADDITIVE: the prefab is instantiated as a child named `IngvarBody` on the character's ROOT
   at local `(0, groundOffset, 0)` — the offset derived from `sharedMesh.bounds` unioned over the renderers, never
   hardcoded, expected 0 — and the stand-in is hidden by DISABLING its `Renderer`s and its `LODGroup`. Nothing is
   destroyed and the `Visual` child is never deactivated, because six vanilla members take the animator with a
   ROOT-scoped `GetComponentInChildren<Animator>()`, which is depth-first in child order and skips inactive
   GameObjects: `Character.Awake` (Character.cs:502) and `ZSyncAnimation.Awake` (ZSyncAnimation.cs:45) at Awake,
   and — after it — `NpcTalk.Start`, `FootStep.Start` (which sits on the character root: it takes `m_character`
   with `GetComponent<Character>()` on the same object), `RandomAnimation.Start`, and `Projectile.RPC_Attach`,
   which searches the ZNetScene instance it stuck into and then walks that animator's hierarchy for the nearest
   bone. Our child is appended LAST, so all six find the vanilla animator first — and only while its object stays
   active. (Line numbers for the last four, in a full ilspycmd decompile of assembly_valheim.dll: 6625, 12377,
   23084, 3086.) The renderer sweeps that run after Awake are scoped to `m_visual` (`Character.UpdateLodgroup`,
   Character.cs:3531; `VisEquipment.UpdateLodgroup`, VisEquipment.cs:710), so a body hung off the root is outside
   all of them — but they still reach the STAND-IN through its `LODGroup`, which is why that goes off too: Unity's
   LOD system owns `Renderer.enabled` for the renderers in a LOD level, and `Character.SetVisible`
   (Character.cs:3775) drives a LOD transition on every change of ZDO ownership.
   The switch is the new `Server.CustomBody` (synced+locked, default true), NOT `BodyPrefab`: `BodyPrefab` stays the
   engine prefab the merchant is cloned from, because `Character`, `MonsterAI` and the collider all come from it.

---

## 12. Risks and open questions

- **Runtime `m_activeArea`** (default 1 in code; the prefab may raise it): `cargo status` prints it; the clamp handles it.
- **Valkyrie prefab registration** with `ZNetScene`: existence proof is the intro on dedicated servers; check first; the
  v1 fallback stays documented.
- **ZDO authoring**: `CreateNewZDO` + `SetPrefab` + `SetOwner(peer)` is what vanilla does for portals and spawners; verify the
  pilot instantiates the authored bird and `Valkyrie.Awake` sees `VCargo_cargo` on first run.
- **`InIntro` postfix scope**: `Player` overrides it, `Humanoid` does not (decompile); players unaffected.
- **Dverger specifics**: `Visual` child, `NpcTalk`, crossbow in `m_defaultItems`, `MakeTame` across ownership handoff;
  each a `cargo prefab` line or a ten-second test. Fallback body `Skeleton`.
- **Terminal**: the first IMGUI window in the family, on code that has run in VikingOS 0.9.x but whose *negotiation* half
  is recorded as never exercised with two clients (BarrkUI §7). Carry the four UI traps from BarrkUI §8 and the chat-RPC
  facts: `activeInHierarchy` as well as null when caching anything on screen; `Sprite.texture` is the atlas; `CalcSize`
  on a wrapping style measures one line; release the cursor request on session end. This is where the adversarial review
  should live.
- **Shared source drift**: the two vendored files change when VikingOS does; the header records the version, and a
  `cargo status` line prints it, so a stale copy is a visible fact rather than a silent one.
- **Getting the files**: `libs-Tools\SharedUI\*.cs` live on Wu'barrk's machine, not here; the decompiled DLL is a
  fallback, the real source is what gets vendored.
- **ServerSync's socket wrapper** on `RPC_PeerInfo`: widely shipped; pinned version in CLAUDE.md as "not ours".
- **Effect prefab networking** decided at boot; the dump tells which branch each entry takes.
- **Economy tuning** is blind until players trade; conservative defaults, `cargo stock` on Ravenrest for a week.
- **Performance of the custom body** when it comes: 30k triangles is the budget; 1.85M is not a game asset.
