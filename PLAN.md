# Valkyrie's Cargo — Plan

> **Revised 2026-09-06, later the same day.** Wu'barrk's brief changed the shape: rested + comfort as the gate, the
> visit rides a vanilla `RandomEvent`, five minutes or dismissal, the merchant walks to you and calls out, he buys
> AND sells from a live stock with barter, prices move in real time. **`docs/DESIGN.md` is now authoritative where
> the two differ**; this file keeps the sibling-code map (section 9) and the phase skeleton.

A Valheim mod by **Raven Iron**. A wandering trader, **Ingvar the Far-Travelled**, is dropped at a
player's base by a Valkyrie at a random moment, buys raw materials for coins at prices that move
with what the world sells him, stays ten to fifteen minutes, and vanishes the way Odin does.

Sibling of Cairn, Undertow, FireFront, Ragnarok's Wrath and RavenEye, bound by the same house
style (see RavenEye/CLAUDE.md "House style — non-negotiable"). RavenEye's "no UI of ours" rule is
RavenEye's, not the house's: this mod has one panel, and that panel is the whole product.

Origin: Discord, 2026-09-06, Thorium Wu'barrk and Nomadicwar. Name candidates were Valkyrie's
Cargo, Bifrost Wares, The Allfather's Courier, Freyja's Flight Merchant; Valkyrie's Cargo chosen
because the man is airdropped by the Valkyrie. Checked free by web search on Thunderstore and
Nexus on 2026-09-06; **Hexium not yet checked** (search "cargo", "valkyrie", "ingvar").

---

## 1. The pitch, in the words that were agreed

| Agreed on 2026-09-06 | Source line |
|---|---|
| Sell materials to an NPC for coins, so the economy does not depend on player-to-player trade | Nomadicwar #2 |
| One trader who buys mats, comes to your base for 10–15 minutes | Nomadicwar #5 |
| Arrival is a Valkyrie drop; departure is the Odin vanish | Thorium #19–22, Nomadicwar #44–45 |
| Not on command; random, so nobody plans a resource dump | Nomadicwar #23 |
| A back end economy that changes price by how much you sell and when | Nomadicwar #24 |
| A summon horn is allowed later, earned, with a long cooldown; until then random | Thorium #25, Nomadicwar #26 |
| "Base" = anywhere a player has comfort; workbench and bed both count; offshore works | Thorium #30–41, Nomadicwar #33–39 |
| Trader: Ingvar the Far-Travelled. Model: Thorium's Meshy build, later | Nomadicwar #55, Thorium #60 |

---

## 2. Engine facts — Valheim as installed, decompiled 2026-09-06 (0.221.12 per RavenEye's notes)

Read the bodies; every claim below came from `ilspycmd` on the REAL `assembly_valheim.dll`.

**Base and comfort**
- `Player.UpdateBaseValue` runs every 2 s on the client: `m_baseValue = EffectArea.GetBaseValue(pos, 20f)`
  (count of PlayerBase effect areas: workbench, forge, etc.), and on change writes it to BOTH
  `ZNet.instance.m_serverSyncedPlayerData["baseValue"]` (public dictionary, sent to the server)
  and the player ZDO (`ZDOVars.s_baseValue`). **The server already knows who is at a base.**
- `RandEventSystem.CheckBase` is vanilla's own gate for base-only raids: `player.baseValue >= 3`.
  It reads `peer.m_serverSyncedPlayerData` and `peer.m_refPos` from the peer list (public
  fields on `ZNetPeer`), plus `Player.m_localPlayer` for a listen host.
- Comfort (`SE_Rested.CalculateComfortLevel`) is **client-only**: shelter plus comfort pieces
  within 10 m (`Piece.GetAllComfortPiecesInRadius`, static, loaded pieces only). It is stored in
  `Player.m_comfortLevel` (private) and **never synced**. The server cannot see it.
- Vanilla excludes `position.y > 3000` (dungeons) from base events. Copy that.

**Trader and store**
- `Trader` (Haldor's component) is a plain client-local MonoBehaviour: no ZNetView use at all.
  Public: `m_name`, greet/bye/stand ranges, `m_items`, every dialogue list, every EffectList,
  `Interact` (opens `StoreGui.instance.Show(this)`), `OnBought`, `OnSold`, `GetHoverName`.
  `Start()` uses `InvokeRepeating("RandomTalk")` (vanilla's, not ours).
- `StoreGui.SellItem` (private): price = `m_shared.m_value * m_stack`, sells the WHOLE stack,
  adds coins by `m_coinPrefab.gameObject.name`. `GetSellableItem` returns only the FIRST valuable
  item from `Inventory.GetValuableItems`. `m_trader` is private; `Show(Trader)` and `Hide()` are public.
- `ItemDrop.ItemData.SharedData.m_value` is shared per item TYPE. Setting it on linen makes
  linen sellable to Haldor too. **Dynamic per-trader prices cannot ride on `m_value`.**
- `StoreGui.m_listElement`, `m_sellButton`, `m_coinText`, `m_sellEffects`, `m_coinPrefab` are
  public: the vanilla look can be cloned for our own panel without an asset bundle.

**Valkyrie**
- `Valkyrie.Awake` on the OWNER computes the whole path from `Player.m_localPlayer.transform.position`
  and calls `SyncPlayer`, which teleports the LOCAL PLAYER to the attach point every frame.
  Non-owners get `enabled = false`. `m_instance` is a public static set in Awake. The intro
  spawns it with `UnityEngine.Object.Instantiate` (not ZNetScene) with a ZNetView on it.
- On a dedicated server `Player.m_localPlayer` is null: vanilla `Awake` would throw. **Our
  flagged Valkyrie must skip vanilla Awake entirely** (prefix, `__runOriginal=false`, set
  `enabled=false` on the vanilla component, never touch `m_instance`) and run our own flyer.
- Path geometry (start 500 m out at 500 m altitude, descent leg at 100 m, drop at 10 m over the
  target, fly-away, then `m_nview.Destroy()`) is 185 lines and reusable as-is. `m_animator`
  has a `dropped` bool. `ZoneSystem.GetGroundHeight` keeps it above terrain; over water it uses
  ground height, so offshore drops need a water-level floor. VERIFY: the Valkyrie prefab is in
  `ZNetScene.m_prefabs` (other players must see the flight).

**Odin**
- `Odin.m_despawn` (public EffectList) is the vanish. `ZNetScene.instance.GetPrefab("odin")
  .GetComponent<Odin>().m_despawn.Create(pos, rot)`. vfx prefabs carry ZNetViews and replicate.

**Random events**
- `RandEventSystem` runs one event at a time, server-side, rolled every `m_eventIntervalMin`
  with `m_eventChance`, saves the active event, pauses when no player is in the area, and
  broadcasts `SetEvent` to everybody. Hosting the visit there would make a visit and a raid
  mutually exclusive. We run our own scheduler on the same peer data and borrow nothing but
  the idea. `RandEventSystem.HaveActiveEvent()` (public static) tells us to hold off during a raid.

**Networking and objects (from the siblings' recorded engine facts, still true)**
- `ZRoutedRpc` forwards whatever target and sender the client wrote (RavenEye/docs/DESIGN.md
  F9). A routed "I sold you 50 iron" is forgeable, so **client-to-server sells go over the
  direct peer `ZRpc`** (RavenEye/Net/RosterSync.cs pattern: one socket, one possible origin).
  `ZNetView.InvokeRPC` is routed and stays out of the sell path.
- A dedicated server **cannot compute ground height on terrain it has not loaded**
  (`GetGroundData` returns its input; RagnaroksWrath/Net/RelicSync.cs). Anything that must
  stand on the ground near a base is placed by a client that is there, who confirms back.
  The Valkyrie flight also needs terrain, so the same client flies it.
- `ZNetScene.GetPrefab(string)` is public; `ZDOMan.GetAllZDOsWithPrefabIterative("Haldor", …)`
  public (orphan sweep on boot). TTL reclaim of a server-tracked object:
  `if (!zdo.IsOwner()) zdo.SetOwner(ZDOMan.GetSessionID()); DestroyZDO(zdo)`
  (Undertow/Systems/FlotsamSystem.cs). Track by ZDOID, never by GameObject: a dedicated
  server unloads the instance while the ZDO lives.
- Custom ZDO keys survive the world save, but **only the owner's writes replicate**
  (RagnaroksWrath/docs/reference/CREATURE-PERSISTENCE-AND-NEMESIS-FACTS.md §2). ZDOIDs are
  re-minted at every world load: never key cross-session state on one (§1).
- `ZDO` string/int/long setters replicate to every client in range for free: **prices, purse
  and the visit clock live on the trader's ZDO**, written by its owner (the server, after it
  takes ownership on confirmation).

---

## 3. Architecture

```
SERVER (owns the economy, the clock and the trader ZDO once it exists)
  CargoTick (ONE Update)  -- every CheckInterval: Scheduler.Roll(peers, clock, cooldowns)
       |  picks one eligible base (baseValue >= MinBaseValue, alive, y < 3000, no raid, day)
       |  and its player as the PILOT (RelicSync placement-delegation pattern)
       v
  Visit.Start -- direct ZRpc "vc_visit"(target, seed) to the pilot; retry until confirmed
       |         pilot confirms "vc_placed"(traderZdoId, valkyrieZdoId) after the drop
       |         server takes ownership of the trader ZDO, writes vc_ingvar=1, vc_end=worldTime,
       |         vc_purse=coins, vc_prices="linen:12;flax:4;..."; banner to everybody in range
       v
  Visit.Run -- direct ZRpc "vc_sell"(item, count) from a client -> Economy.Quote -> validate
       |        purse/list/clock -> update saturation, purse, prices on the ZDO
       |        -> direct ZRpc "vc_sold"(ok, coins) back on the same socket
       v
  Visit.End -- clock elapsed OR purse empty + grace -> "vc_vanish" to clients in range (Odin
              effect, cosmetic) -> SetOwner(self) + DestroyZDO (FlotsamSystem TTL pattern)
              cooldown stamped for that base area; economy persisted

PILOT CLIENT (the player whose base was chosen; has terrain, has a local player)
  on "vc_visit": set CargoFlight.s_pending, Object.Instantiate "Valkyrie" (as the intro does),
                 our CargoFlight flies the vanilla path with a water floor, drops at target,
                 Object.Instantiate "Haldor" on the ground, sends "vc_placed", flies away
  Patch Valkyrie.Awake (prefix, Priority.Low): if s_pending -> consume it, vanilla component
                 enabled=false, __runOriginal=false (vanilla would teleport the local player)

EVERY CLIENT
  Patch Trader.Interact (prefix, Priority.Low, ___m_nview injected): if ZDO vc_ingvar ->
                 open CargoPanel, __runOriginal=false
  Patch Trader.Start/GetHoverName: name "Ingvar the Far-Travelled", our dialogue lists
  CargoPanel: cloned StoreGui elements; rows = inventory items on the buy list x unit price;
              countdown from vc_end; purse from ZDO; Sell -> "vc_sell"; on "vc_sold" remove
              items and add coins (vanilla's own trust model: the client mutates its inventory)
```

Trust model, stated once: the server owns prices, purse and the clock; the client owns its
inventory, exactly as vanilla trading does. A cheating client can already give itself coins;
it cannot move Ingvar's prices or refill his purse, and it cannot make the server believe
another player sold anything (direct socket, one origin). The pilot is trusted to place the
trader where asked, the same co-op drift carve-out RagnaroksWrath records for HealthSync.

Ownership, stated once: the server takes the trader ZDO on confirmation and is the only
writer of its keys; nobody moves the trader after placement (Undertow rule 6: never move
what you do not own). The Valkyrie stays pilot-owned and destroys itself at the fly-away point.

**Economy model (Core/Economy.cs, PURE)**
- Per item: `base` price (config), `saturation S` (persisted), `price = base x max(floor, 1 - k*S)`.
- A sale of `n` units adds `n x SaturationPerUnit` to S. S decays with half-life
  `RecoveryHalfLifeGameDays` measured in world time (`ZNet.GetTimeSeconds`), so a server that
  is empty overnight does not recover.
- **Purse**: Ingvar arrives with `PurseCoins`; every sale drains it; empty purse ends the visit
  after a short goodbye. The purse is the hard cap that makes "no planned dump" true even once
  the horn exists.
- Rounding: integer coins, minimum price 1, never negative, never NaN (test these).

**Scheduler (Core/Scheduler.cs, PURE)**
- Input: list of (peerId, position, baseValue, alive, y), world time, active-raid flag,
  base cooldown ledger (position + expiry), config. Output: chosen drop position or none.
- Eligible = alive, baseValue >= MinBaseValue, y < 3000, no raid, daytime if configured, not
  within `CooldownRadius` of a base whose cooldown has not expired.
- One roll per interval; `VisitChancePercent`; uniform pick among eligible bases (players
  within 40 m of each other collapse to one base so a town is one target, not five).

**Persistence (Server/EconomyStore.cs) — the house pattern, not a new one**
- Clone `Cairn/Cairn/Core/Persistence.cs` (FormatVersion 2): a sidecar `.dat` at
  `World.GetWorldSavePath(FileSource.Local)` named `valkyriescargo_{uid}.dat`, uid from
  `AccessTools.FieldRefAccess<World, long>("m_uid")` (a `long`, not `ulong`), atomic
  `.tmp` then `.bak` then move, `.corrupt` quarantine, per-line fail-safe parse, invariant
  culture, UTF8 without BOM, `OverrideDirectory`/`OverrideWorldUid` test seams.
- Row kinds, one TSV, tagged like `RagnaroksWrath/Core/RelicLedger.cs`: `sat` (item,
  saturation, last-update world time), `cool` (base x, z, expiry), `visit` (last visit time).
  Write-behind dirty flag, cadence save, `OnDestroy` flush.

**Restart mid-visit**
- Haldor's prefab is persistent: an Ingvar ZDO survives a restart. On boot the tick sweeps
  `GetAllZDOsWithPrefabIterative("Haldor")` for `vc_ingvar` and either resumes the clock or
  vanishes him if `vc_end` has passed. Never leave an orphan Ingvar standing in someone's base.

---

## 4. Decisions to lock (proposed; ask before changing once locked)

| Decision | Proposal | Status |
|---|---|---|
| Trigger | Random only in 0.1. Horn is a later, earned item with a cooldown; purse still caps it | agreed in chat |
| "Base" for the server | `baseValue >= MinBaseValue` (default 1: one workbench radius). Comfort is client-only; a client-reported comfort bonus weight is phase 2, never the sole gate | open: Thorium wanted comfort; the engine offers baseValue |
| Offshore | Works by construction (baseValue is location-free); flight needs a water-level floor | agreed |
| UI | Our own sell panel, cloned from StoreGui elements. Vanilla store cannot show a list or per-trader prices | open |
| What Ingvar buys | Config list `item:basePrice`, default a raw-materials set (wood, resin, leather scraps, deer hide, flax, linen thread, ...). He sells nothing in 0.1 | open |
| Visit length | 10–15 min real time, uniform, config; clock stored as world time on the ZDO | agreed |
| Arrival | The chosen player's client pilots the Valkyrie and places Ingvar on the ground, then confirms; the server cannot see terrain it has not loaded. A visibly carried passenger is phase 2 (needs a synced transform on the Haldor prefab: verify) | proposed |
| Ownership | Server takes the trader ZDO on confirmation and is its only writer; the Valkyrie stays pilot-owned; nothing moves what it does not own | proposed |
| Persistence | Cairn's `Persistence.cs` sidecar `.dat` keyed by world uid; RelicLedger-style tagged rows | proposed |
| Departure | Odin's `m_despawn` effect, then destroy | agreed |
| Fairness | One base per roll, cooldown per base area (`BaseCooldownGameDays`), towns count once | proposed |
| Daytime only | Yes by default (the flight is the show) | proposed |
| During raids/boss | Never spawn while `RandEventSystem.HaveActiveEvent()` | proposed |
| Transport | ZDO fields for replicated state; direct peer `ZRpc` for `vc_visit`/`vc_placed`/`vc_sell`/`vc_sold` with a versioned payload (RosterPacket pattern); routed RPC only for the cosmetic `vc_vanish` broadcast | proposed |
| Base gate | Vanilla `baseValue` from `m_serverSyncedPlayerData` for eligibility; `RagnaroksWrath/Core/Homestead.IsNearPlayerBuilt` (creator-tagged ZDOs, 3x3 sectors) to confirm the drop point is beside something built | proposed |
| Model | Haldor's body renamed in 0.1; Thorium's Meshy model via asset bundle later | agreed |
| Console prefix | `cargo` (`cargo status`, `cargo prices`, `cargo visit` admin-only force, `cargo reset`) | proposed |
| GUID / namespace | `com.raveniron.valkyriescargo` / `RavenIron.ValkyriesCargo` | proposed |
| Dependencies | BepInExPack only. No Jotunn (siblings are raw Harmony) | proposed |

---

## 5. Config (same Server.*/Client.* split as RavenEye)

```
Server.Enabled                  true
Server.CheckIntervalMinutes     10        real minutes between rolls
Server.VisitChancePercent       15        per roll, per server (about one visit an hour with people at bases)
Server.MinBaseValue             1
Server.DaytimeOnly              true
Server.VisitMinutesMin/Max      10 / 15
Server.PurseCoins               600
Server.BaseCooldownGameDays     2
Server.CooldownRadius           60
Server.BuyList                  "Wood:1,Resin:1,LeatherScraps:2,DeerHide:3,Flax:4,LinenThread:12,..."
Server.SaturationPerUnit        0.01
Server.PriceFloorPercent        25
Server.RecoveryHalfLifeGameDays 1.5
Client.ShowArrivalBanner        true
```

---

## 6. Phases

**Phase 0 — Repo (half a day)**
- Scaffold from RavenEye: csproj (net472, version-const generator, libs check), tools/
  (fetch-libs, run-tests, package), tests/CoreTests harness, .gitignore, manifest.json,
  CLAUDE.md, docs/DESIGN.md (this plan becomes its first section), CHANGELOG.md, icon.
- Stubs = the union of `RagnaroksWrath/tests/CoreTests/Stubs.cs` (Vector3, ZoneSystem
  maths, World, FileHelpers, AccessTools, config quartet) and `RavenEye/tests/CoreTests/Stubs.cs`
  (ZDOID, a working ZPackage). The harness compiles the SHIPPING source, never a copy.
- CLAUDE.md inherits the house rules by reference (Undertow's provenance rule: do not
  re-derive them or write them up as if this project measured them), and adds the ones the
  sweep surfaced that bind here: field injection (`___m_nview`) over reflection in patches;
  reflection resolution in a separate method from the work; never move what you do not own.
- Check the name on Hexium. Record the naming history in CLAUDE.md as the siblings do.
- Plugin boots on client and dedicated server with a one-line boot log.

**Phase 1 — Pure core, tested off-game (one day)**
- `Core/Economy.cs`: quote, apply sale, decay, purse. Tests: floor, rounding, decay over a
  gap, purse exhaustion, unknown item refused, zero/negative count refused.
- `Core/Scheduler.cs`: eligibility, town collapse, cooldown ledger, single pick with an
  injected RNG. Tests: dungeon exclusion, raid hold, cooldown honoured, empty world gives none.
- `Core/VisitClock.cs`: start/end in world seconds, remaining, expired (RavenEye/Core/Grant.cs
  is the tested clock shape to copy: clamped grace, explicit end versus silence).
- `Core/PriceTable.cs`: the ZDO string encoding/decoding (round-trips, tolerates junk).
- `Net/CargoPackets.cs`: engine-free writers/readers for visit, placed, sell, sold, with the
  format version inside the payload (RavenEye/Net/RosterPacket.cs).
- Every test proven to fail without its fix (house working agreement).

**Phase 2 — Server side (one to two days)**
- `Core/CargoTick.cs` (the ONE Update): roll, delegate, confirm, run, end, boot sweep.
  Peer walk as in `RagnaroksWrath/Systems/ZoneSyncSystem.cs` (`GetPeers` then the character
  ZDO, `m_refPos` fallback) plus RavenEye's listen-host case (the host is not a peer).
- `Server/VisitDirector.cs`: pick pilot, send `vc_visit`, retry until `vc_placed`, take
  ownership, write keys; TTL reclaim as `Undertow/Systems/FlotsamSystem.cs` does it.
- `Server/EconomyStore.cs`: Cairn's Persistence clone. `Server/VersionSync`: copy
  `RagnaroksWrath/Net/VersionSync.cs` (server broadcasts its version, skewed client warns once).
- `Client/CargoFlight.cs` (runs on the pilot): the vanilla path, water floor, drop, place
  Haldor, confirm. `Patches/Patch_Valkyrie_Awake.cs`: pending-flag instances skip vanilla.
  `Patches/Patch_Trader_Start.cs`: name and dialogue for flagged instances.
- `cargo status` and `cargo where` exist BEFORE any coin moves (Undertow's "measure before
  you push"): status names the scheduler's last decision in words.
- Headless verification on CairnTest (port 2466, minimal) with one Gale client as pilot:
  `cargo visit` forces a visit at the admin's base; server log shows pick, delegation,
  confirmation, clock, vanish. Restart mid-visit: sweep resumes or vanishes correctly.
  Runbook for the servers: `RagnaroksWrath/docs/HANDOFF.md` "Runbook" (start line, join
  code, `BepInEx\LogOutput.log` recreated each boot). Record a `CargoTest` server's own
  launch line in this repo's CLAUDE.md; the siblings never wrote theirs down.

**Phase 3 — Client side (two days)**
- `Patches/Patch_Trader_Interact.cs`: open `CargoPanel` for Ingvar, `__runOriginal=false`.
- `Client/CargoPanel.cs`: cloned vanilla elements, rows from inventory on the buy list, unit
  price, stack total, purse, countdown, Sell per row; `Net/CargoSync.cs`: registration on
  the client's server socket (`GetServerRPC()`) and on each peer's `m_rpc` server-side,
  keyed on the `ZRpc` instance as RosterSync does.
- Inventory discipline from `RagnaroksWrath/docs/reference/VANILLA-PIECE-INTEROP-FACTS.md`
  §1–§2: check before you remove; `m_shared.m_name` and `m_dropPrefab.name` are different
  strings, and the buy list must say which one it is keyed on (prefab name).
- Arrival banner via `MessageHud` (Client.ShowArrivalBanner), Ingvar's greet/goodbye lines.
- Odin vanish observed on a client that is NOT the owner.

**Phase 4 — Two-client verification (half a day)**
- Owner on Gale client + second account (TesTylass) on the same laptop, dedicated test world.
- Checklist in section 7. Fix, then adversarial review pass (four reviewers, as RavenEye had).

**Phase 5 — Release 0.1.0**
- README (what it is, what it is not, config table, "not yet verified" list), CHANGELOG,
  `package.ps1`, Hexium under RavenIronStudios + Thunderstore, GitHub RavenIron/ValkyriesCargo tag v0.1.0.

**Later (not 0.1)**
- The earned horn: item, cooldown in world days, purse still applies.
- Comfort bonus weight (client-reported, validated against baseValue).
- Meshy model for Ingvar via asset bundle; carried-passenger flight if the prefab syncs.
- Ingvar sells a small rotating stock of far-travelled goods.
- Per-item "when" pricing: seasonal or day-count drift on top of saturation.

---

## 7. What to verify in-game (not yet done)

1. `cargo visit` on a dedicated server: Valkyrie appears at altitude, descends, Ingvar stands
   at the drop point, banner shows, `cargo status` reads the clock and purse.
2. Second client sees the flight and Ingvar without being the owner.
3. Sell linen: price shown = server quote; coins arrive; a second sale of the same item shows
   a lower price; purse on both clients drops by the same amount.
4. Let the clock run out: Odin vanish on both clients, Ingvar gone, cooldown stamped.
5. Drain the purse: goodbye line, vanish after grace.
6. Restart the server mid-visit: Ingvar resumes with the right remaining time, or vanishes
   if the time passed while down.
7. Offshore base (island with a workbench): eligible, flight lands above water level.
8. Start a raid, then wait an interval: no visit during the raid.
9. Two bases online: over ten forced rolls, both get visits, neither twice inside the cooldown.
10. Listen host (host is not a peer): the host's own base is eligible.

---

## 8. Risks and open questions

- **Valkyrie prefab registration.** If it is not in `ZNetScene.m_prefabs`, non-owners log
  "Missing prefab" and see nothing, and worse: `Undertow/Systems/FlotsamSystem.cs` records
  that an unresolvable prefab hash sends `CreateObjectsSorted` into `DestroyZDO`, silent
  loss. Check with a `FloatScan`-style prefab enumeration first (Undertow/Core/FloatScan.cs).
  Fallback: register at boot the way `FireFront/Patches/DousingBombPatches.cs` inserts a
  clone (idempotent across `ObjectDB.Awake` and `CopyOtherDB`, guarded by `DbIsFullyLoaded`).
- **Awake ordering on the pilot.** The pending flag must be set immediately before
  `Object.Instantiate` and consumed by the first `Valkyrie.Awake` that sees it; if anything
  else instantiates a Valkyrie in between (a second player's intro), the flag lands on the
  wrong bird. Guard: also stamp the ZDO with `vc_cargo` in the prefix and check both.
- **The pilot leaves.** If the chosen player disconnects mid-flight, no confirmation ever
  comes: the director retries with the next eligible base and forgets the visit after N tries.
- **First asset bundle in the family.** Thorium's Meshy model would be the family's first
  bundle; `RagnaroksWrath/docs/reference/JOTUNN-AND-HEADLESS-AUTOMATION-FACTS.md` §7–§8
  records a 0-byte bundle that "succeeded" silently. Not 0.1; cloning Haldor is the
  sanctioned route until then.
- **Haldor prefab on a dedicated server.** `Trader.Update` calls `Player.GetClosestPlayer`,
  which returns null there; the animator gets `Stand=false`. Harmless; verify no NRE in Start.
- **Water floor for the flight.** `GetGroundHeight` returns seabed under water; the path must
  clamp to `ZoneSystem.instance.m_waterLevel + dropHeight`.
- **Comfort vs baseValue.** Thorium's "anywhere a player can get comfort" is the intent;
  the engine offers baseValue for free and comfort only with a client report. Decide before
  Phase 1 so the Scheduler's input shape is final.
- **Economy tuning is blind until players sell.** Ship with conservative defaults and the
  `cargo prices` command so a server owner can watch saturation move.
- **Competitors:** TravelingHaldor (OdinPlus) roams by biome; TradersExtended (shudnal) has
  per-trader sell lists; Better Trader Remake has a resetting coin pool. None deliver to a
  base on a timer with a moving buy price. Say so in the README without naming them as lesser.
- **New ground.** Nothing in the seven sibling repos touches `Trader`, `StoreGui`, Haldor,
  coins or `m_value`. The store panel and the sell transaction have no house precedent;
  expect the adversarial review to spend its time there.

---

## 9. Reusable pieces in the family (sweep of 2026-09-06)

| Need | Copy from |
|---|---|
| Sidecar world save | `Cairn/Cairn/Core/Persistence.cs`; multi-row shape `RagnaroksWrath/RagnaroksWrath/Core/RelicLedger.cs` |
| Direct peer ZRpc, anti-forgery | `RavenEye/RavenEye/Net/RosterSync.cs`; reasoning `RavenEye/docs/DESIGN.md` |
| Versioned packet in payload | `RavenEye/RavenEye/Net/RosterPacket.cs` |
| Server spawn, ZDOID tracking, TTL reclaim | `Undertow/Undertow/Systems/FlotsamSystem.cs` |
| Client-placed object with confirm handshake | `RagnaroksWrath/RagnaroksWrath/Net/RelicSync.cs` (`RPC_RelicPlace`) |
| "Near something a player built" (server) | `RagnaroksWrath/RagnaroksWrath/Core/Homestead.cs` |
| Random online player as event centre | `RagnaroksWrath/RagnaroksWrath/Systems/World/WeatherSystem.cs` (`TryPickStormCentre`) |
| Event-area maths, dungeon exclusion | `RagnaroksWrath/RagnaroksWrath/Core/StormArea.cs` |
| Peer walk with listen-host case | `RagnaroksWrath/RagnaroksWrath/Systems/ZoneSyncSystem.cs`, `RavenEye/RavenEye/Net/RosterSync.cs` `BuildRoster` |
| Tested clock with grace | `RavenEye/RavenEye/Core/Grant.cs` |
| Admin check, fail closed | `RavenEye/RavenEye/Server/AdminGate.cs` |
| Server version broadcast | `RagnaroksWrath/RagnaroksWrath/Net/VersionSync.cs` |
| Prefab enumeration console command | `Undertow/Undertow/Core/FloatScan.cs` |
| Vanilla prefab clone registration | `FireFront/Patches/DousingBombPatches.cs` |
| NPC says a line (vanilla Hugin) | `Cairn/Cairn/Voice/HuginVoice.cs` |
| Test stubs | `RagnaroksWrath/tests/CoreTests/Stubs.cs` + `RavenEye/tests/CoreTests/Stubs.cs` |
| Server runbook | `RagnaroksWrath/docs/HANDOFF.md` "Runbook"; CairnTest port 2466 minimal, StormTest port 2476 full modpack |

Gap the sweep found: nothing in the family despawns WITH an `EffectList`. The Odin vanish is
written here for the first time.
