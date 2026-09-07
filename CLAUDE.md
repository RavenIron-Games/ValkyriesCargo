# Valkyrie's Cargo

A Valheim mod by **Raven Iron**. A Valkyrie drops a merchant, Ingvar the Far-Travelled, beside your
base at a random moment when you are rested and comfortable. He walks up, calls out, buys and sells
from a live, persistent stock at supply-and-demand prices for five minutes, and vanishes the way Odin
does. Every player sees the same visit; only the server owns the market.

**Not** on command (the earned summon horn is a later feature), **not** a custom body in 0.1 (the
Dverger stands in until Thorium's model is rigged), **not** a patch on the vanilla store. The one UI
it draws is its own trade terminal, opened from our own interact handler.

Sibling of Cairn, Undertow, FireFront, Ragnarok's Wrath and RavenEye, bound by the same house style.
Three firsts for the family, each a recorded decision: ServerSync, a trade terminal of our own, and
(later) an asset bundle.

Design of record: `docs/DESIGN.md` (v3). One screen: `docs/TLDR.md`. Catalogue with every number's
reason: `docs/CATALOGUE.md`. Plan and sibling-code map: `PLAN.md`. Review of the partner's v5 draft
and the model: `docs/REVIEW-v5-2026-09-06.md`.

---

## Status

**Phase 0 scaffold, 2026-09-06. Builds clean (0 warnings), 27/27 off-game tests, packages
(`dist\RavenIronStudios-ValkyriesCargo-0.1.0.zip`, right layout).** What exists: the plugin entry,
ServerSync vendored and armed, the whole config surface bound and locked, the `cargo` console, the
catalogue parser with 72 data-checked defaults. Nothing rolls a visit, flies, walks, trades or persists.

**HEADLESS VERIFIED 2026-09-06 15:23 on CairnTest (dedicated, port 2466, world CairnTest, alongside
Cairn.dll and RavenEye.dll):** within 20 s of launch the BepInEx log showed, in order,
`Loading [Valkyrie's Cargo 0.1.0]`, then
`Valkyrie's Cargo v0.1.0 loaded - renderer=False, patches=10, catalogue=72 entries, ServerSync version gate armed; role is decided when a world loads.`
(10 = ServerSync's own patches plus our terminal postfix), then
`Registered 'com.raveniron.valkyriescargo ConfigSync' RPC - waiting for incoming connections`, then
`Load world: CairnTest`, `role: dedicated server`, `Game server connected`. Stopped by `Stop-Process`
(a test world; nothing to save). The `ArgumentNullException: Value cannot be null` Unity line at boot
predates us: it is in the 10:32 VantageTest run with only Cairn and RavenEye loaded. Not yet seen: a
client boot, the version wall, the config lock, the prefab dumps. See "What to verify in-game".

**The contract (PR #1, merged) and the market core (branch `a/market-core`, 2026-09-06).** Pure, off-game:
`Core/Market.cs` (rules sanitized on the way in; the price curve, one rounding for the charge and one for
what he pays; purse and carry; drift by the EnvMan day length; settlement in the contract's refusal order;
salted delivery ids; sidecar rows incl. `purseStart`/`visit`/`seq`), `Core/Scheduler.cs` (eligibility,
the roll, tickets, cooldowns saved as remaining seconds), `Core/VisitClock.cs` (a countdown mirror the
server retargets); `DemoMarket` is the real Market with a price-driven `Tick`. **714 off-game checks,
mutation-proven** (20 mutations by an Opus prover, 8 more by hand; each fails without its fix). Reviewed
against DESIGN and CATALOGUE by an Opus reviewer; its blockers are fixed and the documents corrected
(CATALOGUE section 5 numbers; DESIGN section 8 gained five proposed decisions). Nothing on the game side
calls any of it yet. `cargo status` now prints `EnvMan.m_dayLengthSec`, the day the drift counts: its
compiled default is 1200, the scene is expected to say 1800, and that is UNVERIFIED until a client boots.

**P3 eligibility and event, 2026-09-06 (branch `a/p3-eligibility-event`).** `Client/ComfortReporter.cs` writes
`vc_rested`/`vc_comfort` on the local player's own ZDO every 2 s; `Server/CargoEvent.cs` + the
`RandEventSystem.Awake` prefix register the vanilla event `valkyries_cargo` on every machine;
`Server/VisitDirector.cs` (one tick a second where the world runs) reads every character ZDO into the pure
`Scheduler`, starts the event for the pilot it picks, publishes `VisitState`/`MarketState`, mirrors the
event's clock through `Core/VisitSession.cs` (retarget rule, design 3.7) and ends the visit when the engine
ends the event; `Net/AdminRpc.cs` carries `cargo visit [player]` and `cargo dismiss` from a client to the
server, where `Server/AdminGate.cs` (vanilla's `ZNet.IsAdmin`, fail closed) decides. 769 off-game checks.

**HEADLESS VERIFIED 2026-09-06 18:55 on StormTest (dedicated, port 2476, the full Ravenrest modpack clone,
117 plugins)**, in order in `BepInEx\LogOutput.log`:
`Valkyrie's Cargo v0.1.0 loaded - renderer=False, patches=13, catalogue=72 entries, ServerSync version gate armed; role is decided when a world loads.`
(13 = ServerSync's 10, UIFocus's 2, our RandEventSystem.Awake prefix), then
`event 'valkyries_cargo' registered (20 events now); duration 300 s, pauses with nobody within 96 m, no spawns, no music, no weather.`,
then `role: dedicated server`, `routed RPCs registered for this session: vc_admin, vc_reply`, then
`director up: salt w4790ce, day 1800 s (EnvMan.m_dayLengthSec), catalogue 72 entries, purse 800, roll every 60 s at 25%, first roll one interval from now; market state is NOT persisted yet (P6)`
(**the scene's day length IS 1800 s**: read from the live EnvMan, so the drift half-life is right), then one
interval later `roll: held: a random event is active (a raid, a storm, or a visit)` (Ragnarok's Wrath had a storm
running: the hold works against a real foreign event). No exception from us in the log. The roll reasons print
only on change, so a quiet log after that line is the loop holding, not the loop dead. Not yet seen: a player's
report in `cargo status`, a forced visit, the banner, the timer ending a visit; all need a client (see "What to
verify in-game"). CairnTest was in use by the owner for another mod at the time and was not touched.

**P6 deal wire and persistence, 2026-09-06 (branch `a/p6-deal-wire`).** `Net/DealWire.cs` registers `vc_open`,
`vc_close`, `vc_deal`, `vc_ack`, `vc_claim`, `vc_dismiss` on EACH peer's own ZRpc as it connects and answers
`vc_dealt` on the same socket; `Net/CargoTransport.cs` is the client end (the real `ICargoTransport` behind
`CargoRpc`), `LocalTransport` the listen host's in-process one, `Deliveries` the redelivery path;
`Client/DealApplier.cs` is the ONLY code that writes an inventory for a deal (removals first, then additions,
by the item's shared name); `Client/InboxStore.cs` keeps the applied delivery ids in the config folder.
`Core/OwedLedger.cs` (pure) is the server's memory of deliveries not yet acked, keyed by platform id;
`Core/Sidecar.cs` (pure) is the world file's format; `Server/MarketStore.cs` moves it to disk with Cairn's
discipline (.tmp, .bak, .corrupt). The director loads the sidecar when it is built, writes it on a 30 s cadence
while dirty and at visit start, visit end, session end and shutdown, and ADOPTS a saved visit whose event the
engine restored (vanilla saves the running random event with the world). Console: `cargo stock [prefab]`,
`cargo deal buy|sell <prefab> [count]`, `cargo claim`; admin `cargo reset`, `cargo save`. 852 off-game checks.

**HEADLESS VERIFIED 2026-09-06 19:25 on StormTest (plugins cleared to this DLL alone, 1 plugin to load)**:
first boot `director up: ... next visit #1, ...; sidecar valkyriescargo_4690126.dat (fresh world)` and the file
appeared in `saves\worlds_local` at once: 78 lines, `format 1`, 72 `stock` rows, `purse 800`, `purseStart 0`,
`visit 0`, `seq 0`. Restart: `director up: ... sidecar valkyriescargo_4690126.dat (76 rows loaded)` and the
first file rotated to `.bak`. Earlier the same boot printed `roll: no eligible player: nobody online` (the
empty-server path, live). Not yet seen: a deal over the wire, a redelivery, a resumed visit; all need a client
(items 13-16).

**P7 the Cargo Terminal, 2026-09-06 (branch `a/p7-terminal`).** `Client/Terminal/CargoTerminal.cs` is the IMGUI
window on Wu'barrk's vendored gilt theme (`SharedUI.GiltFrameTheme` + `UIFocus`): the title with the countdown,
purse and pay mode, HIS WARES (icon, name, stock/target, price, trend) and YOUR GOODS HE WANTS (icon, name, what
you carry, his shelf, what he pays), the staging tray with the flat "you pay" line, Confirm / Clear / Fill from my
goods / Send him off (twice), and his words in the footer. `Client/Terminal/TrayModel.cs` (pure, 74 checks) is
the tray: staging clamped to stock, room and what you carry, the prices copied from the snapshot and amber where
they moved, Validate in the server's order, Build at the price on screen NOW, AutoFill for barter, the answer
handling (Ok empties, price_changed goes amber against the new market, refusals keep the tray). The one OnGUI is
`CargoTick.OnGUI`; the tokens are raised from `CargoTick.Update`, never from OnGUI (the theme's gotcha 3);
the window id carries the mod's name. Panel rules: Escape, Use, Tab, M, the inventory or map open, the player
dead, more than 5 m from the merchant, the visit over or leaving. `cargo terminal demo` opens it on the in-process
market with no server; `cargo terminal open` on the running visit before P5 gives it a merchant. The inventory is
written only through `DealApplier` inside the answer. Off-game: builds clean (net48), 926 checks, seven tray
mutations caught. **Not yet seen on a screen**: the window itself (item 17).

---

## Commands

```powershell
.\tools\fetch-libs.ps1     # once per machine: copies game/BepInEx DLLs into libs\
.\tools\run-tests.ps1      # off-game logic tests (net10) — run before every commit
.\tools\package.ps1        # Release build + store zip in dist\ (writes manifest version from the csproj)
dotnet build ValkyriesCargo\ValkyriesCargo.csproj
```

To inspect a game member — signature, accessibility, or the actual body — decompile it:

```powershell
$m = "C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed"
ilspycmd -r $m $m\assembly_valheim.dll -t Valkyrie        # the REAL assembly: true accessibility
```

Read the body; do not infer it from the shape. Do not `head`-truncate a member grep.

To test in-game: copy `ValkyriesCargo\bin\Debug\ValkyriesCargo.dll` into `<install>\BepInEx\plugins\`.
The owner's client runs through Gale (`%APPDATA%\com.kesomannen.gale\valheim\profiles\<profile>\BepInEx\plugins\`);
dedicated test servers live under `C:\Users\donfr\ValheimServers\` (CairnTest on port 2466 is the
minimal one; the runbook is `RagnaroksWrath\docs\HANDOFF.md`). Valheim locks the DLL while running.

Console today: `cargo status | version | prefab <name> | stock [prefab] | deal buy|sell <prefab> [count] | claim |
terminal demo|open|close | visit [player] | dismiss | reset | save`. `visit`, `dismiss`, `reset` and `save` are
admin verbs: on a server or listen host they run in place; from a client they ride `vc_admin` to the server,
where the public `ZNet.IsAdmin` (RavenEye's `AdminGate` shape, fail closed) decides and `vc_reply` prints the
answer in the caller's console. `deal` is the terminal's deal without the terminal: it builds the same `Deal`,
sends it through `CargoRpc` and applies the answer through `DealApplier`.

---

## Layout

Built:

```
ValkyriesCargo/
  ValkyriesCargo.cs          plugin entry: config (creates the ConfigSync), Harmony, tick, boot line
  Config/ModConfig.cs        Server.* synced+locked, Client.* local, VisitState/MarketState channels
  Core/CargoTick.cs          the ONLY Update in the mod; role decided at runtime
  Core/Catalogue.cs          PURE: the catalogue line parser and the 72 defaults
  Core/Wire.cs Core/MarketSnapshot.cs Core/VisitSnapshot.cs Core/Deal.cs   PURE: the contract (PR #1)
  Core/Market.cs             PURE: rules, price curve, purse, drift, settlement, sidecar rows
  Core/Scheduler.cs          PURE: eligibility, the roll, tickets, cooldowns and their rows
  Core/VisitClock.cs         PURE: the countdown mirror (world seconds; the server retargets it)
  Core/DemoMarket.cs         PURE: the real Market behind `cargo terminal demo`, plus Tick and Advance
  Core/VisitSession.cs       PURE: the server's visit record; the clock-mirror retarget rule (design 3.7)
  Core/Lines.cs              PURE: Ingvar's words (design 7); indexes cross the wire, never text
  Core/Sidecar.cs            PURE: the world file's format; routes rows to market, scheduler, session, ledger
  Core/OwedLedger.cs         PURE: deliveries the server still owes, by platform id, until acked
  Server/MarketStore.cs      the sidecar on disk: valkyriescargo_{worldUid}.dat, .tmp/.bak/.corrupt
  Net/DealWire.cs            server end: vc_open/close/deal/ack/claim/dismiss on each peer's ZRpc; vc_dealt back
  Net/CargoTransport.cs      client end (the real ICargoTransport), LocalTransport (listen host), Deliveries
  Client/DealApplier.cs      the ONLY inventory writer for a deal: CanApply, Apply, by shared item name
  Client/InboxStore.cs       the applied delivery ids on disk (config folder)
  Client/Terminal/CargoTerminal.cs   the window: ICargoTerminal on the gilt theme, drawn from the one OnGUI
  Client/Terminal/TrayModel.cs       PURE: the staging tray, Validate/Build/AutoFill/Answer
  Server/VisitDirector.cs    where the world runs: gather ZDOs -> Scheduler -> event -> VisitState/MarketState
  Server/CargoEvent.cs       the vanilla RandomEvent `valkyries_cargo`: definition, registration, start, remaining
  Server/AdminGate.cs        vanilla's ZNet.IsAdmin(hostName), fail closed (RavenEye's shape)
  Client/ComfortReporter.cs  vc_rested / vc_comfort on the local player's own ZDO, every 2 s
  Net/AdminRpc.cs            vc_admin (client -> server) and vc_reply (server -> client) on the routed RPC
  Patches/Patch_RandEventSystem_Awake.cs   prefix, Priority.Low, return true: registers the event
  Net/CargoRpc.cs            the client-side surface the terminal calls; the demo transport (PR #1)
  Client/Terminal/ICargoTerminal.cs   what the merchant calls; Track B implements it (PR #1)
  Patches/Patch_Terminal.cs  the `cargo` console: status, version, prefab dump
  Libs/ServerSync.cs         NOT OURS: blaxxun ConfigSync.cs, compiled in as shared source
tests/CoreTests/             net10 harness; compiles the REAL Core sources against stubs
tools/                       fetch-libs, run-tests, package
libs/                        gitignored; populated by fetch-libs.ps1
docs/                        DESIGN, TLDR, CATALOGUE, REVIEW-v5, data/items table, the partner's drafts
```

Planned (design section 3; names are final, files do not exist yet):

```
  Server/Spawner.cs
  Client/CargoFlight.cs Client/CargoMerchant.cs Client/BodyLoader.cs
  Patches/Patch_Valkyrie_Awake.cs Patch_Humanoid_Awake.cs
  Patches/Patch_Character_InIntro.cs Patch_Character_Damage.cs
  Libs/SharedUI/GiltFrameTheme.cs Libs/SharedUI/UIFocus.cs   (Wu'barrk's VikingOS, MIT, not yet received)
```

---

## House style — inherited (each rule came from a measured failure in a sibling; not re-derived here)

1. **Harmony: prefixes for behaviour at `Priority.Low`, honouring `__runOriginal`; result-decorating
   postfixes at default priority where appending is the whole point. Never a max- or high-priority
   replace.** The planned `Patch_Valkyrie_Awake` skips vanilla for our own object only, the narrow
   named exception RavenEye recorded for `UpdateNoMap`.
2. **No long-lived coroutines.** `CargoTick` is the one `Update`. Nothing else owns a timer.
3. **Cosmetics off the gameplay path.** Every patch body is its own try/catch, logging at most three
   times.
4. **Never patch `EnvMan`. Never touch materials, textures or shaders.** Reading `EnvMan.IsDay()` is
   fine; the material work for the custom body happens at build time in the bundle, not at runtime.
5. **Publicized assemblies are COMPILE-TIME ONLY.** Our files name no private member. Private fields
   reach patches only by `___injection`; private methods are patched, never called. `Libs/ServerSync.cs`
   reflects into a few; that is its file.

Three the siblings added later and this mod inherits: **field injection over reflection in patches**
(`___m_nview`: a renamed field fails at patch time, not silently at call time); **reflection resolution
in its own method, never in the method that does the work** (Mono resolves field access when the
CALLER is JIT-compiled, so a try/catch in the same method never runs); **never move what you do not
own** (only the owner's ZDO writes replicate; setting a ZDO position from elsewhere is a suggestion the
owner overwrites next frame).

**Debugging discipline.** A silent success and a silent no-op look the same from outside the game.
`cargo status` names its sources; spend the first round-trip on it, not on a guess.

---

## The workspace knowledge base — read it BEFORE deriving anything

`libs-Tools\` is the family's accumulated engine knowledge, written up from mods that shipped and
broke in production. **This mod did not consult it until 2026-09-07 and paid for that once already**
(see the two corrections below). Consult it before decompiling, and before designing any system whose
shape another mod has already found the hard way.

> **It lives on Wu'barrk's machine only** — `~/WubarrkCODING/libs-Tools/`, a sibling of this repo there,
> and it is not in this repository or on Don's. It is Markdown, not binaries, so it *could* be shared;
> until it is, the table below is a catalogue of what to ask him for, not a path to open. Anything from
> it that this mod actually depends on gets copied into this repo as a quoted fact with its source
> named — as the two corrections below are — so the code here never rests on a document Don cannot read.

| What to ask for | What is in it |
|---|---|
| `IMPLEMENTATIONS\MASTER_IMPLEMENTATIONS.md` | The index: every reusable system in every project, one line each, pointing at a per-project detail file. Its **READ FIRST** section is the moving-an-object rules and the traps that cost real player data. |
| `IMPLEMENTATIONS\DvergrAllies.md` | **P5's ground truth.** A shipped mod that clones Dverger prefabs, tames them, makes them follow and overrides their `MonsterAI`. The exact de-hostility field list, `Character.Faction.Players`, the staggered re-apply, the `Tameable` retrofit. |
| `IMPLEMENTATIONS\WingsoftheValkyrie.md` | Flight movement and a multiplayer VFX state sync over custom ZDO fields — the same shape as `vc_state`. |
| `IMPLEMENTATIONS\AwayFromHome.md`, `MistsofAvalor.md` | The bundle pipeline and the grave-relocation disaster. Already cited by `models\README.md`. |
| `VALHEIM-API-REFERENCE\` | 13 files of decompiled API facts with line numbers. `09-DAMAGE-ZDO-MULTIPLAYER.md` is the ZDO and damage authority: who runs what, and a GOTCHAS list that is worth reading whole. |
| `*-FACTS.md` | Per-topic findings: dedicated server, headless/empty server, ZDO wire limits, console routing, player identity, player attach. |

### Two corrections it forces on this repo

1. **A `ZDOID` is a session handle, not an identity — never persist anything against one.** `ZDO.Load`
   opens with `m_uid.SetID(++ZDOID.m_loadID)`: every ZDO is renumbered every time the world is read
   off disk. The id is stable while the world stays loaded and meaningless the moment it does not,
   so the feature works all session and every key in it is orphaned by the next login. It cost
   TortalPortal its favourites feature. **`Spawner.CarrierKey` (`vc_carrier`) writes the bird's
   `ZDOID` onto the merchant's PERSISTENT ZDO and is exposed to exactly this.** P5 owns the fix:
   the carry link is session-only, so the restart sweep must clear `vc_carrier` (`ZDO.RemoveZDOID`
   exists) and treat `vc_state` as the authority, never a surviving id.

2. **`Character.Damage` is a thin RPC sender, not where damage happens.** It runs on the ATTACKER's
   client, calls `FindWeakSpotIndex` and `InvokeRPC("RPC_Damage", hit)`, and nothing else — reading
   or modifying health there does nothing. The work is in the private `Character.RPC_Damage`, whose
   first lines run on EVERY client that has the victim instanced; the `if (!m_nview.IsOwner()) return;`
   gate is partway down. So the planned `Patch_Character_Damage` for the merchant's immortality is
   named for the wrong method: cancelling belongs at `RPC_Damage`, and anything that mutates state in
   a postfix there must re-gate on `IsOwner()` or it applies once per peer.

Also load-bearing for what this mod already does, and confirmed rather than corrected: `ZDO.Set` has
**no** ownership check on any overload (the `okForNotOwner` parameter is ignored in the body), so a
non-owner write is a silent desync that the owner overwrites on its next sync — which is the house
rule "never move what you do not own", stated as an API fact. `ZDO.GetVec3` has no default argument.
`string.GetStableHashCode()` lives in `assembly_utils.dll`, not `assembly_valheim`.

---

## "Not ours"

- **`Libs/ServerSync.cs`** = blaxxun's `ConfigSync.cs`, master, fetched 2026-09-06 (1415 lines),
  MIT-0. Compiled into this assembly as shared source, the way every ServerSync mod does it. It patches
  `ZNet.RPC_PeerInfo` (a buffering socket around the handshake) and reads `ZRoutedRpc.m_peers` and
  `ZNet.m_adminList` by reflection. Its business; update from upstream, never edit.
- **`Libs/SharedUI/GiltFrameTheme.cs`, `Libs/SharedUI/UIFocus.cs`** (not yet vendored) = Wu'barrk's
  VikingOS 0.9.8 shared source, MIT. `UIFocus` carries two Harmony patches (`GameCamera.UpdateMouseCapture`,
  `Chat.HasFocus`); design section 4 lists them as shared-source patches.

---

## Locked decisions — do not revisit without asking (the full table is `docs/DESIGN.md` section 8)

| Decision | Answer |
|---|---|
| Every client runs the mod | ServerSync `ModRequired`, minimum version = current; a mismatch is refused at handshake |
| Trade UI | A terminal of our own, IMGUI on VikingOS's theme, opened from our `Interactable`; `StoreGui` never patched |
| Market state | ServerSync custom values + sidecar world save; **never on the merchant's ZDO** (owner writes only replicate) |
| Deals | Direct peer `ZRpc`, server-validated, nonce ring, the prices the player saw; inventory touched only after the answer |
| Departure | The Odin vanish (`Odin.m_despawn`), once per screen; 300 s event clock or Shift+E twice |
| Price-change policy | Reconfirm (provisional; `Teardown` behind config) |
| 0.1 body | `Dverger`, tamed, following, immortal |
| Console prefix / GUID / namespace | `cargo` / `com.raveniron.valkyriescargo` / `RavenIron.ValkyriesCargo` |

---

## Engine facts the code relies on today (bodies read 2026-09-06; the full list is `docs/DESIGN.md` section 0 and 3)

- The installed Valheim runs on **Unity 6000.0.61f1** (`UnityPlayer.dll`); bundles must be built with that Editor.
- **ServerSync broadcasts on change only.** No heartbeat. Client writes are rejected while locked unless
  the client is on `adminlist.txt`. Payloads under 10 000 bytes go uncompressed.
- **Comfort never leaves the client** (`SE_Rested.CalculateComfortLevel` is local); the client will
  write `vc_rested` / `vc_comfort` on its own character ZDO, which replicates because the client owns it.
- **Objects are instantiated on a client only inside its active zone block**
  (`ZNetScene.InActiveArea`: `|zone − centre| ≤ m_activeArea − 1`, 64 m zones); outside it
  `RemoveObjects` destroys the instance and a non-persistent owned ZDO with it. The Valkyrie starts
  ~90 m out, never 500.
- **`ZRoutedRpc.instance` is null for the whole of plugin `Awake`** and is re-created on every world
  join; register routed handlers per session, direct `ZRpc` handlers on peer connect.
- `EnvMan.IsDay()` is static. `Character.m_collider` is a `CapsuleCollider`. `Odin.m_despawn` and
  `Odin.m_ttl` (300 s) are public.
- **The game day is 1800 s** (`EnvMan.instance.m_dayLengthSec`, public, read live on StormTest 2026-09-06;
  the COMPILED default is 1200, the scene overrides it). The market's drift counts this number, never a constant.
- **The vanilla random event**: `RandEventSystem.SetRandomEvent` is private; `SetRandomEventByName`,
  `ResetRandomEvent`, `GetCurrentRandomEvent`, `HaveEvent` and `m_events` are public. The server's
  `FixedUpdate` advances `m_time` only while a player is within `m_eventRange` (96 m) of `m_pos` and
  ends the event when `m_time > m_duration`; every 2 s it broadcasts name, time and position, and a client
  resolves the name from ITS OWN `m_events`, which is why the event is registered on every machine. A
  client inside the range makes it the active event and shows `m_startMessage` once. `m_random = false`
  keeps it out of the random pool. `m_cameraShakeCurve` must be EMPTY: with keys, `Update` calls
  `GameCamera.instance.AddShake`, null on a dedicated server.
- **Vanilla SAVES the running random event with the world** (`RandEventSystem.PrepareSave/SaveAsync/Load`:
  name, time, position) and restores it on load through `SetRandomEventByName`, so a restart mid-visit brings
  the event back; the director adopts it from the sidecar's `session` row within 15 s, else lets it go.
- **Direct peer RPC**: `ZRpc.Register<T>(name, Action<ZRpc,T>)` replaces by name (safe to repeat),
  `ZRpc.Invoke(name, params)`, `ZNet.GetServerRPC()` (client, one per connection), `ZNet.GetPeers()` with
  `peer.m_rpc` (server); the peer's platform id is `peer.m_socket.GetHostName()`.
- **Inventory** counts and removes by the item's SHARED name (`ItemDrop.m_itemData.m_shared.m_name`, a
  "$item_..." token), never the prefab name; `Inventory.AddItem(GameObject, amount)` caps one call at a
  stack; `CanAddItem(GameObject, stack)` is the pre-check; `ObjectDB.GetItemPrefab(name)` resolves a prefab.
- `Player.m_comfortLevel` is private and computed locally every 2 s (`SE_Rested.CalculateComfortLevel`);
  `Player.GetComfortLevel()` is public. `Player.GetPlayerName()`, `Character.GetSEMan()`,
  `SEMan.HaveStatusEffect(int)`, `SEMan.s_statusEffectRested` are public. A character ZDO carries
  `playerName`, `baseValue` and `dead` (`ZDOVars`); its owner is the peer's uid.

---

## What to verify in-game (Phase 0)

1. ~~**Boot line, dedicated server**~~ **DONE 2026-09-06** (see Status): the DLL sits in CairnTest's
   `BepInEx\plugins\`; a headless boot shows the loaded line with `patches=10, catalogue=72 entries`,
   ServerSync's RPC registration, and `role: dedicated server`.
2. **Boot line, client:** same line with `renderer=True`; `cargo status` answers in the console.
3. **Version wall:** a client on another version (bump the csproj, rebuild, install on one side only) is
   refused with ServerSync's message naming the mod and both versions.
4. **Locked config:** a client edits `MinComfortLevel` locally while connected; `cargo status` still shows
   the server's value and `following the server`.
5. **`cargo prefab Valkyrie`, `cargo prefab Dverger`, `cargo prefab odin`, `cargo prefab Haldor`:** paste
   the dumps below this list. They decide the effect rule branches, the Valkyrie registration question,
   the Dverger's `NpcTalk`/`Tameable`/animator parameters, and whether `m_attachPoint` exists.
6. **`cargo status` numbers:** the runtime `ZoneSystem.m_activeArea` (the clamp depends on it); `valkyries_cargo`
   is registered (StormTest log: 20 events) but a client-side `cargo status` has not yet said so.

P3, needs a client on a server whose adminlist.txt names it (CairnTest or StormTest):
7. **The report:** `cargo status` on the client shows `my report: rested=..., comfort=..., written N s ago`; the
   SAME numbers appear in the server's `candidates:` line (a listen host shows both at once).
8. **`cargo visit`** from the client: the console prints `asked the server`, then the server's answer
   (`cargo visit <name>: forced visit: <name> at (x, z); ...`); the server log shows `visit #1 begins`; the pilot
   sees "Wings beat in the upper skies..." and, once inside 96 m of where they stood, the centre banner
   "Valkyrie's Cargo has landed"; `cargo status` shows `visit: #1 Flying, pilot <name>, 04:5x left`.
9. **The clock:** walk more than 96 m away for a minute, come back: the countdown resumed where it paused and
   the server log counted a clock republish; sleep through a night mid-visit: the countdown did not jump.
10. **The end:** after 300 s the server log shows `visit #1 ended: timer; takings 0 coins`, the banner
    "Ingvar has gone back to the mist" shows, `cargo status` shows `visit: none; last #1 ended: timer`.
11. **`cargo dismiss`** ends it early with `ended: admin <name>`; a non-admin's `cargo visit` is answered
    `not an admin` and the server log says `refused vc_admin visit from <name>`.
12. **The gates:** a client that is not rested is refused `forced: <name> not eligible: not rested; online: ...`.

P6, the wire, with a visit running (`cargo visit` first):
13. **A deal:** `cargo stock Iron` shows his shelf; `cargo deal buy Iron 2` prints `sending`, then
    `DONE w...-1-1: +2 Iron, -N coins`; the inventory changed by exactly that; the server log shows
    `deal w...-1-1 with <name>: sold 2 Iron at N, coins -2N to the player; purse ...`; `cargo stock Iron`
    shows stock 18 and a higher price on EVERY machine. `cargo deal sell Wood 10` the other way.
14. **The ledger:** `cargo status` on the server shows `owed ledger 0 row(s)` after the ack arrives; log out
    the instant after a deal's answer (before the ack), log back in: the server log shows
    `vc_claim from <name>: redelivered 1 owed deal(s)` and the client shows `delivery ... applied` or, if the
    inbox already had it, nothing twice.
15. **The sidecar after deals:** the server's file carries the changed `stock` rows, `purse`, `visit 1`,
    `seq N`, the `session` row while the visit runs, `cool` rows after it; a restart mid-visit prints
    `visit #1 RESUMED after a restart` and the countdown continues.
16. **Refusals:** `cargo deal buy BlackCore 3` answers `sold_out`; a buy with fewer coins than the price is
    stopped on the client before sending; a stale visit id is `stale_visit`.

P7, the terminal (a client, no server needed for the first item):
17. **`cargo terminal demo`** (from the main menu or in a world): the gilt window opens centred with the cursor
    free; 18 wares on the left with icons and prices, 72 rows on the right; clicking a ware stages it (Shift 5,
    Ctrl 20, right-click takes back); "you pay" is count x price; Confirm deal answers with one of his three buy
    lines and the price on that row moves; a second Confirm on the same line comes back "The wind shifted..."
    with the line amber, and Confirm new price goes through; Escape closes it and the cursor locks again; the log
    shows `terminal opened: visit #1 (demo)` and `terminal closed: escape`.
18. **On a real visit** (`cargo visit`, then `cargo terminal open`): the countdown matches the server's; a buy
    changes the inventory by exactly the deal and the server log shows the deal; the same row's price moved on
    every machine; a sell of goods you carry pays coins; Fill from my goods covers a ware with the dearest goods
    first; Send him off twice ends the visit (`ended: dismissed by <name>`); Tab and M close it; walking away
    closes it only once P5 gives it a merchant.

---

## Working agreement

- **Run `.\tools\run-tests.ps1` before every commit.**
- **Prove a new test fails without its fix.**
- **A clean build proves nothing about member access.** Anything reaching a game member needs one
  in-game run before it is called done.
- **Ask before changing anything in the locked-decisions table.**
