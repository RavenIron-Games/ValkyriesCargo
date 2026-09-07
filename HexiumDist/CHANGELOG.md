# Changelog

## 0.1.0

A Valkyrie drops Ingvar the Far-Travelled beside your base at a random moment when you are rested and
comfortable. He walks up, calls out, buys and sells from a live persistent stock at supply-and-demand
prices for five minutes, and vanishes the way Odin does. Every player sees the same visit; only the
server owns the market.

**What has been seen on a screen, and what has not.** The plugin boots on a client and a dedicated
server, the director runs, the market persists across restarts, and Ingvar's own body loads out of the
bundle and stands correctly lit. A full visit -- the flight, the drop, the walk-up, the terminal and a
trade -- has been proven off-game across 1301 checks and in a nine-scenario economy simulation, and is
still being proven in-game; the runbook is `docs/PROOF-CLIENT.md` and what remains is listed in
CLAUDE.md's "what to verify in-game". Treat 0.1.0 as a first playable, not as a settled one.

Entries are in build order.

### Phase 4-5 — the flight and the merchant

- `Core/FlightPlan.cs` plans a straight approach inside the pilot's active block, shrinking the start
  by 12 m steps and turning a quarter at a time when a bearing has no room; `TurningRadius`/`Reachable`
  assert every waypoint is flyable at the shipped speed, because a pure pursuer whose target sits inside
  its own turning circle never closes and orbits for ever.
- `Server/Spawner.cs` authors the bird and the merchant as ZDOs owned by the pilot; `Client/CargoFlight.cs`
  flies vanilla's own maths on the owner alone and writes `s_velHash` so every other screen dead-reckons
  a glide instead of a stutter.
- **The drop point the pilot reports is bounded against the one the server authored.** The bird is owned
  by the pilot, so its target key is a value a client writes, and it used to be handed to the visit
  unexamined -- a modified client could put Ingvar anywhere in the world. The bound refuses a forged NaN
  as well, which the natural spelling of the check would have accepted.
- `Client/CargoMerchant.cs` carries Ingvar under the talons, walks him up, calls out, keeps him peaceful
  and immortal for the visit, and vanishes him with Odin's own effect.

### Phase 8 — Ingvar's body

- The bundle is baked and embedded in the DLL, so every player sees the same merchant with no download.
- Five bake defects were found and fixed, every one of which produced a bundle that passed every gate a
  build script can check. Written up in full at `docs/knowledge-base/SKINNED-CHARACTER-BUNDLE-FACTS.md`.

### Phase 12 — BarrkBOT, and the economy

- The market, the traders and the visits are published as JSON under `BepInEx/config/ValkyriesCargo/`
  for BarrkBOT to read off the server filesystem. The world sidecar always saves first; a failure in the
  export can never take down a save or a visit.
- **The Fair Market Act.** Ingvar used to buy his own wares back for more than he sold them --
  `MaxPriceMultiplier` 3.0 x `SpreadBuy` 0.7 = 2.1 -- so a player could empty a shelf, sell it straight
  back, and walk off with his whole purse while the shelves ended exactly where they started. A Ware's
  buy-back is now capped at par. What he charges still rises to the full 3.0x, and goods he only buys
  are untouched.
- His purse is 1500 rather than 800, and the carry between visits is measured on the coins that came in
  rather than on the net, so a server that sells to him refills him as well as one that buys from him.
- Four goods that paid firewood rates -- round logs, fine wood, feathers and leather scraps -- now pay
  what they are worth.

### Phase 0 — the scaffold

- Repo laid out from the RavenEye template: a `net48` plugin, `libs\` populated by
  `tools\fetch-libs.ps1`, the off-game harness in `tests\CoreTests` (`net8.0`, compiling the
  shipping sources against stubs), `tools\package.ps1` for the store zip. The csproj `<Version>` is
  the one source of the version number: the C# constant is generated from it and `package.ps1`
  writes `manifest.json` from it.
- ServerSync (blaxxun's `ConfigSync.cs`, MIT-0) vendored as shared source in `Libs\ServerSync.cs`;
  `ModRequired` with minimum version = current, so a client without the mod or on another build is
  refused at handshake.
- The whole config surface bound — 26 `Server.*` entries synced and locked, 4 `Client.*` local — and
  the two broadcast channels `VisitState` / `MarketState` declared.
- The `cargo` console: `status`, `version`, `prefab <name>`.
- `Core\Catalogue.cs`: the pure parser and 72 data-checked defaults (18 wares, 54 wants), every
  prefab name verified against `docs\data\items-valheim-2026-07-31.tsv`; a bad entry is reported and
  skipped, never thrown. **27 off-game checks.**
- Design of record: `docs\DESIGN.md` v3, `docs\TLDR.md`, `docs\CATALOGUE.md`.
- Vendoring pass (PR #2): `Libs\SharedUI\GiltFrameTheme.cs` and `Libs\SharedUI\UIFocus.cs` brought in
  from Wu'barrk's VikingOS 0.9.8 (MIT) as shared source; the `.gitignore` rule that had been hiding
  `Libs\` fixed; the plugin moved to `net48` and the harness to `net8.0`.

### P1 — the contract between the two tracks (PR #1)

- `Core\Wire.cs`, `Core\MarketSnapshot.cs`, `Core\VisitSnapshot.cs`, `Core\Deal.cs` (Deal,
  DealResult, DealInbox), `Net\CargoRpc.cs` with a demo transport, and
  `Client\Terminal\ICargoTerminal.cs`. Frozen, so the terminal could be written against a market
  that did not exist yet. **231 off-game checks.**

### P2 — the market core (PR #3)

- `Core\Market.cs`: rules sanitized on the way in; the price curve with one rounding for what he
  charges and one for what he pays; purse and carry; drift by the `EnvMan` day length; settlement in
  the contract's refusal order; salted delivery ids; sidecar rows including `purseStart`, `visit`
  and `seq`.
- `Core\Scheduler.cs`: eligibility, the roll, town tickets, cooldowns saved as remaining seconds.
  `Core\VisitClock.cs`: a retargetable countdown mirror. `Core\DemoMarket.cs`: the real Market with a
  price-driven `Tick`.
- **714 off-game checks, mutation-proven** (20 mutations by an Opus prover, 8 by hand; each fails
  without its fix). Reviewed against DESIGN and CATALOGUE; the blockers were fixed and the documents
  corrected (CATALOGUE section 5; DESIGN sections 3.1, 3.4, 3.5, 3.7 and 8).
- `cargo status` gained the `EnvMan.m_dayLengthSec` line, the day the drift counts.

### P3 — eligibility and the event (PR #5)

- `Client\ComfortReporter.cs` writes `VCargo_rested` / `VCargo_comfort` on the local player's own ZDO every
  2 s (comfort never leaves the client in vanilla; this is the same trust class as `baseValue`).
- `Server\CargoEvent.cs` and the `RandEventSystem.Awake` prefix register the vanilla random event
  `valkyries_cargo` on every machine.
- `Server\VisitDirector.cs`, one tick a second where the world runs: every character ZDO into the
  pure `Scheduler`, the event started for the pilot it picks, `VisitState` and `MarketState`
  published, the event's clock mirrored through `Core\VisitSession.cs` (the retarget rule, design
  3.7), and the visit ended when the engine ends the event.
- `Net\AdminRpc.cs` carries `cargo visit [player]` and `cargo dismiss` from a client to the server,
  where `Server\AdminGate.cs` (vanilla's `ZNet.IsAdmin`, fail closed) decides.
- **769 off-game checks.**

### P6 — the deal wire and persistence (PR #6)

- `Net\DealWire.cs` registers `VCargo_open`, `VCargo_close`, `VCargo_deal`, `VCargo_ack`, `VCargo_claim` and
  `VCargo_dismiss` on each peer's own `ZRpc` as it connects, and answers `VCargo_dealt` on the same socket.
  `Net\CargoTransport.cs` is the client end, `LocalTransport` the listen host's in-process one,
  `Deliveries` the redelivery path.
- `Client\DealApplier.cs` is the only code in the mod that writes an inventory for a deal (removals
  first, then additions, by the item's shared name). `Client\InboxStore.cs` keeps applied delivery
  ids in the config folder, so a redelivery is recognised instead of applied twice.
- `Core\OwedLedger.cs` (pure) is the server's memory of deliveries not yet acked, keyed by platform
  id. `Core\Sidecar.cs` (pure) is the world file's format. `Server\MarketStore.cs` moves it to disk
  with Cairn's discipline (`.tmp`, `.bak`, `.corrupt`).
- The director loads the sidecar when it is built, writes it on a 30 s cadence while dirty and at
  visit start, visit end, session end and shutdown, and adopts a saved visit whose event the engine
  restored — vanilla saves the running random event with the world.
- Console: `cargo stock [prefab]`, `cargo deal buy|sell <prefab> [count]`, `cargo claim`, and the
  admin verbs `cargo reset` and `cargo save`.
- **852 off-game checks.**

### P7 — the Cargo Terminal (PR #7)

- `Client\Terminal\CargoTerminal.cs`: the IMGUI window on the vendored VikingOS gilt theme
  (`SharedUI.GiltFrameTheme` + `SharedUI.UIFocus`). The title with the countdown, purse and pay
  mode; HIS WARES (icon, name, stock against target, price, trend) and YOUR GOODS HE WANTS (icon,
  name, what you carry, his shelf, what he pays); the staging tray with the flat "you pay" line;
  Confirm, Clear, Fill from my goods, Send him off (twice); and his words in the footer.
- `Client\Terminal\TrayModel.cs` (pure, 74 checks): staging clamped to stock, room and what you
  carry; the prices copied from the snapshot and amber where they moved; Validate in the server's
  order; Build at the price on screen now; AutoFill for barter; the answer handling (Ok empties,
  `price_changed` goes amber against the new market, refusals keep the tray).
- The one `OnGUI` is `CargoTick.OnGUI`, and the focus tokens are raised from `CargoTick.Update`,
  never from `OnGUI`. Panel rules: Escape, Use, Tab, M, the inventory or map open, the player dead,
  more than 5 m from the merchant, the visit over or leaving.
- `cargo terminal demo` opens it on the in-process market with no server; `cargo terminal open`
  opens it on a running visit. The inventory is written only through `DealApplier`, inside the
  answer.
- **926 off-game checks**, with seven tray mutations caught.

### P8 — the body loader, mod side (branch `a/p8-loader`)

- `Client\BodyLoader.cs`: the AssetBundle `valkyriescargo_kit`, embedded in this DLL when a bake exists at
  `Assets\valkyriescargo_kit` (a loose file beside the DLL is accepted for trying a bake, loudly labelled), is
  loaded once and never unloaded. `Attach` hangs Ingvar's prefab on the merchant's root and switches the
  stand-in's renderers off, never destroying one, so every vanilla system keeps its animator.
- `Client\IngvarBody.cs`: the six clips (Walk, Idle, Talk, Hello, Shrug, Nod) played through a PlayableGraph
  with no AnimatorController; Idle and Walk blended from the body's own displacement, the four gestures as
  one-shots for the merchant to call. `Core\BodyMotion.cs` (pure, 78 checks) is the blend.
- New `Server.CustomBody` (synced and locked, default true): the switch. `BodyPrefab` stays the engine prefab.
- Console: `cargo body`, `cargo body preview | walk | clip <name> | clear`; a `body:` line in `cargo status`.
- The bake needs nothing beyond what `tools\unity\IngvarBundleBuilder.cs` already produces.
- **1034 off-game checks** (30 of them from the adversarial review), with nine blend-model mutations caught between
  the builder and the reviewer. The review also found and fixed the one runtime defect: Unity's LOD system would have
  switched the hidden stand-in back on at every ownership or equipment change, so the loader disables the `LODGroup` too. The embed proven off-game: a stand-in file
  at `Assets\valkyriescargo_kit` grew the DLL by exactly its size and appeared as the resource
  `ValkyriesCargo.valkyriescargo_kit`.

### P4 — the authored flight (PR #8, Wu'barrk)

- `Core\FlightPlan.cs` (pure, 39 checks): the flight inside the pilot's active block, a straight approach with the
  descent waypoint on the line carrying the glide altitude, and `TurningRadius` / `Reachable` so the harness refuses
  any waypoint a pursuer at the shipped speed and turn rate cannot reach.
- `Server\Spawner.cs`: the bird's ZDO authored whole and owned by the pilot (`VCargo_cargo`, `VCargo_target`, `VCargo_turn`,
  `VCargo_dropped`); the merchant not authored until P5 (`MerchantEnabled`); both reclaimed on any visit end.
  `Patches\Patch_Valkyrie_Awake.cs`: vanilla `Awake` skipped for our bird only. `Client\CargoFlight.cs`: the owner
  flies vanilla's own maths and writes the velocity key so every other screen sees a glide.
- New `Server.FlightSpeed` (8) and `Server.FlightTurnRate` (45): ours, synced, not the prefab's 20 and 20, whose
  57 m turning circle is wider than the whole approach.
- Reviewed on the PR with a simulation of the flight; all four findings taken and reproduced by both sides. Not
  flown. **1078 off-game checks.**

### After P8: the audit, the runbook and the simulation (PRs #10, #11, #12)

- **The client-path audit** (`docs\CLIENT-AUDIT.md`): 57 engine members on the never-run client paths checked
  against the real assemblies; six defects fixed. `cargo terminal demo` from the main menu drew but never ticked
  (Escape dead, every buy refused `coins_short`); Tab and M closed the terminal AND opened the inventory or map;
  a deal the pack refused was acked anyway (now the transports ack only what `DealApplier` applied, and the
  inbox mark is taken back with `DealInbox.Forget`); a destroyed merchant switched the 5 m rule off instead of
  closing; the focus tokens leaked on plugin destroy; Escape was read through legacy `Input`. **1039 checks.**
- **The proof runbook** (`docs\PROOF-CLIENT.md`): CLAUDE.md items 2 to 20 in one sitting, each with the line the
  code writes; `tools\deploy-test.ps1`, `tools\tail-log.ps1`, `tools\set-test-config.ps1`.
- **The economy simulation** (`tests\EconSim`, `docs\ECONOMY-SIM.md`, `tools\run-econsim.ps1`): nine seeded
  scenarios against the real market. Finding for the owners: the buy-out-and-sell-back round trip is profitable
  (`MaxPriceMultiplier` 3.0 x `SpreadBuy` 0.7 = 2.1) and drains the purse on the first visit; a decision for DESIGN
  section 8 before anyone trades. No default changed.

### Verification

**Headless-proven on a dedicated server.** Server-side only; there was no client in any of these
runs, and `renderer=False` in every boot line.

- CairnTest (port 2466), 2026-09-06 15:23, with two sibling mods loaded: `Loading [Valkyrie's Cargo
  0.1.0]`, then `Valkyrie's Cargo v0.1.0 loaded - renderer=False, patches=10, catalogue=72 entries,
  ServerSync version gate armed; role is decided when a world loads.`, then `Registered
  'com.raveniron.valkyriescargo ConfigSync' RPC - waiting for incoming connections`, `Load world:
  CairnTest`, `role: dedicated server`, `Game server connected`.
- StormTest (port 2476), 2026-09-06 18:55, in a 117-plugin modpack clone: the same loaded line with
  `patches=13`; `event 'valkyries_cargo' registered (20 events now); duration 300 s, pauses with
  nobody within 96 m, no spawns, no music, no weather.`; `routed RPCs registered for this session:
  VCargo_admin, VCargo_reply`; `director up: salt w4790ce, day 1800 s (EnvMan.m_dayLengthSec), catalogue 72
  entries, purse 800, roll every 60 s at 25%, first roll one interval from now`; then `roll: held: a
  random event is active (a raid, a storm, or a visit)` against a live foreign event. The day length
  the drift counts is 1800 s from the live `EnvMan`, not the compiled default of 1200.
- StormTest, 2026-09-06 19:25, cleared to this DLL alone: `sidecar valkyriescargo_4690126.dat (fresh
  world)` and the file on disk at once — 78 lines, `format 1`, 72 `stock` rows, `purse 800`,
  `purseStart 0`, `visit 0`, `seq 0`; after a restart, `sidecar valkyriescargo_4690126.dat (76 rows
  loaded)` with the first file rotated to `.bak`. Also `roll: no eligible player: nobody online`.

**Not yet seen on a screen.** `CLAUDE.md` "What to verify in-game" items 2 to 20, none of them done:
the client boot line, the version wall, the config lock, the `cargo prefab` dumps, the runtime
`m_activeArea`, the comfort report in `cargo status`, `cargo visit` with the banner and the pilot's
line, the clock pausing and resuming, the timer ending a visit, `cargo dismiss`, a non-admin
refused, an ineligible player refused, a deal over the wire with the price moving on every machine,
the owed ledger and a redelivery after a disconnect, the sidecar after deals, a visit resumed after
a mid-visit restart, the refusal reasons, `cargo terminal demo`, the terminal on a real visit, and the body (`cargo body`, `cargo body preview`) once a
bundle is baked.

**Not in this release yet.** The authored flight (P4) is in review on another branch and has no entry
here; the merchant (P5) and the baked bundle are not started. See
`docs/DESIGN.md` section 9 for the order and `docs/WORKSPLIT.md` for who owns what.
