# Valkyrie's Cargo

A Valheim mod by **Raven Iron**. A Valkyrie drops a merchant, Ingvar the Far-Travelled, beside your
base at a random moment when you are rested and comfortable. He walks up, calls out, buys and sells
from a live, persistent stock at supply-and-demand prices for five minutes, and vanishes the way Odin
does. Every player sees the same visit; only the server owns the market.

**Not** on command (the earned summon horn is a later feature), **not** a patch on the vanilla store. The one UI
it draws is its own trade terminal, opened from our own interact handler. 0.1 **does** carry his own body: the
bundle was baked on 2026-09-07 and is embedded in the shipped DLL (PR #22). The Dverger clone stays the chassis
under him — his mesh is ADDED to it and the stand-in's renderers are switched off — and `Server.CustomBody`
(default true) puts the stand-in back.

Sibling of Cairn, Undertow, FireFront, Ragnarok's Wrath and RavenEye, bound by the same house style.
Three firsts for the family, each a recorded decision: ServerSync, a trade terminal of our own, and
(later) an asset bundle.

Design of record: `docs/DESIGN.md` (v3). One screen: `docs/TLDR.md`. Catalogue with every number's
reason: `docs/CATALOGUE.md`. Plan and sibling-code map: `PLAN.md`. Review of the partner's v5 draft
and the model: `docs/REVIEW-v5-2026-09-06.md`. Track B's decision record, with the reasoning behind every
row it changed in the locked table: `docs/DECISIONS-WUBARRK.md`.

---

## Status

**Main after the 0.1.0 integration (PR #22) and the API-reference snapshot (PR #17), 2026-09-07. Builds clean
(0 warnings), 1301/1301 off-game checks, packages (`dist\RavenIronStudios-ValkyriesCargo-0.1.0.zip`, right
layout); `v0.1.0-rc1` is tagged with the store zip attached to the release and uploaded to NO store.** Every
package is now code: the plugin entry, ServerSync vendored and armed, the config surface bound and locked, the
`cargo` console, the catalogue with 72 defaults, the market and the scheduler, the event and the director, the
deal wire, the world sidecar, the Cargo Terminal, the body loader with Ingvar's baked bundle embedded, the
authored flight (P4), the merchant (P5) and the BarrkBOT export (P12). Every ZDO key and RPC name is typed once,
in `Core/Keys.cs`, under the `VCargo_` prefix (issue #16).

**What that does NOT mean.** Exactly ONE live visit has been run, on Wu'barrk's client — see "INTEGRATED IN-GAME
RUN" below for what it did and did not prove. The glide, the drop, the walk-up completing, the terminal on a real
visit, a trade and the vanish have never been watched; no two-client item has run at all. **A build on any
machine carries the body only if `Assets/valkyriescargo_kit` is on it**: `Assets/` is gitignored, the bake is
Wu'barrk's machine's, and every bake reaches the other side as a release asset (attached to `v0.1.0-rc1` on
2026-09-07, and on Don's machine since). The built DLL is NOT tracked in git (owner, 2026-09-07):
`HexiumDist/plugins/` is ignored and a payload is a release asset, never a commit.

The paragraphs below are the history, each with the lines seen.

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
`VCargo_rested`/`VCargo_comfort` on the local player's own ZDO every 2 s; `Server/CargoEvent.cs` + the
`RandEventSystem.Awake` prefix register the vanilla event `valkyries_cargo` on every machine;
`Server/VisitDirector.cs` (one tick a second where the world runs) reads every character ZDO into the pure
`Scheduler`, starts the event for the pilot it picks, publishes `VisitState`/`MarketState`, mirrors the
event's clock through `Core/VisitSession.cs` (retarget rule, design 3.7) and ends the visit when the engine
ends the event; `Net/AdminRpc.cs` carries `cargo visit [player]` and `cargo dismiss` from a client to the
server, where `Server/AdminGate.cs` (vanilla's `ZNet.IsAdmin`, fail closed) decides. 769 off-game checks.

**HEADLESS VERIFIED 2026-09-06 18:55 on StormTest (dedicated, port 2476, the full Ravenrest modpack clone,
117 plugins)**, in order in `BepInEx\LogOutput.log`:
`Valkyrie's Cargo v0.1.0 loaded - renderer=False, patches=13, catalogue=72 entries, ServerSync version gate armed; role is decided when a world loads.`
(13 = the 10 above, UIFocus's 2, our RandEventSystem.Awake prefix; the breakdown of the 10 was never checked
against `Harmony.GetAllPatchedMethods()`), then
`event 'valkyries_cargo' registered (20 events now); duration 300 s, pauses with nobody within 96 m, no spawns, no music, no weather.`,
then `role: dedicated server`, `routed RPCs registered for this session: VCargo_admin, VCargo_reply`, then
`director up: salt w4790ce, day 1800 s (EnvMan.m_dayLengthSec), catalogue 72 entries, purse 800, roll every 60 s at 25%, first roll one interval from now; market state is NOT persisted yet (P6)`
(**the scene's day length IS 1800 s**: read from the live EnvMan, so the drift half-life is right), then one
interval later `roll: held: a random event is active (a raid, a storm, or a visit)` (Ragnarok's Wrath had a storm
running: the hold works against a real foreign event). No exception from us in the log. The roll reasons print
only on change, so a quiet log after that line is the loop holding, not the loop dead. Not yet seen ON THAT DAY: a
player's report in `cargo status`, a forced visit, the banner, the timer ending a visit; all need a client (see
"What to verify in-game"). Since: the report and a forced visit on 2026-09-07 (the first client run and the
integrated run below); **the banner has still never been seen.** CairnTest was in use by the owner for another mod
at the time and was not touched.

**P6 deal wire and persistence, 2026-09-06 (branch `a/p6-deal-wire`).** `Net/DealWire.cs` registers `VCargo_open`,
`VCargo_close`, `VCargo_deal`, `VCargo_ack`, `VCargo_claim`, `VCargo_dismiss` on EACH peer's own ZRpc as it connects and answers
`VCargo_dealt` on the same socket; `Net/CargoTransport.cs` is the client end (the real `ICargoTransport` behind
`CargoRpc`), `LocalTransport` the listen host's in-process one, `Deliveries` the redelivery path;
`Client/DealApplier.cs` is the ONLY code that writes an inventory for a deal (removals first, then additions,
by the item's shared name); `Client/InboxStore.cs` keeps the applied delivery ids in the config folder.
`Core/OwedLedger.cs` (pure) is the server's memory of deliveries not yet acked, keyed by platform id;
`Core/Sidecar.cs` (pure) is the world file's format; `Server/MarketStore.cs` moves it to disk with Cairn's
discipline (.tmp, .bak, .corrupt). The director loads the sidecar when it is built, writes it on a 30 s cadence
while dirty and at visit start, visit end, session end and shutdown, and ADOPTS a saved visit whose event the
engine restored (vanilla saves the running random event with the world). Console: `cargo stock [prefab]`,
`cargo deal buy|sell <prefab> [count]`, `cargo claim`, `cargo catalogue list`; admin `cargo reset`, `cargo save`,
`cargo catalogue add|remove|reset` (2026-09-07: the shelf changes without a restart, between visits). 852 off-game checks.

**HEADLESS VERIFIED 2026-09-06 19:25 on StormTest (plugins cleared to this DLL alone, 1 plugin to load)**:
first boot `director up: ... next visit #1, ...; sidecar valkyriescargo_4690126.dat (fresh world)` and the file
appeared in `saves\worlds_local` at once: 78 lines, `format 1`, 72 `stock` rows, `purse 800`, `purseStart 0`,
`visit 0`, `seq 0`. Restart: `director up: ... sidecar valkyriescargo_4690126.dat (76 rows loaded)` and the
first file rotated to `.bak`. Earlier the same boot printed `roll: no eligible player: nobody online` (the
empty-server path, live). Not yet seen ON THAT DAY: a deal over the wire, a redelivery, a resumed visit; all need
a client (items 13-16). Since: a **resumed visit** off the sidecar, in the integrated run below. **A deal has still
never crossed the wire in a game, and no redelivery has ever run.**

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

**P8 the body loader, mod side, 2026-09-06 (branch `a/p8-loader`).** `Client/BodyLoader.cs` opens the AssetBundle
`valkyriescargo_kit` — the resource embedded in this DLL, resolved BY SUFFIX, or, loudly labelled, a loose file beside
the DLL for trying a bake without a rebuild — once, and never unloads it. `Attach(Character)` hangs the prefab as a
child named `IngvarBody` on the merchant's ROOT at local `(0, groundOffset, 0)`, derived from `sharedMesh.bounds`
unioned over the renderers and expected to be 0, and hides the stand-in by DISABLING every other `Renderer` under the
character AND its `LODGroup` (Unity's LOD system owns `Renderer.enabled` for the renderers it lists, and
`Character.SetVisible` on every ownership change plus `VisEquipment.UpdateLodgroup` on every equipment change would
switch them back on; found by the review) — never destroying one, never deactivating `Visual` — so `Character.m_animator`, `VisEquipment`,
`CharacterAnimEvent`, `ZSyncAnimation` and the `CapsuleCollider` all keep working and every vanilla
`GetComponentInChildren<Animator>` still finds the vanilla animator first (ours is appended last; the search is
depth-first in child order). The whole stock assembly has six root-scoped lookups that can run after Awake:
`Character.Awake`, `ZSyncAnimation.Awake`, `NpcTalk.Start`, `FootStep.Start`, `RandomAnimation.Start` and
`Projectile.RPC_Attach`; the appended-last defence covers all six, and P5 attaching from the `Humanoid.Awake`
postfix means the three Start-time ones WILL run. It re-hides every 2 s, because `VisEquipment` rebuilds a crossbow on any equipment change.
`Client/IngvarBody.cs` plays the six clips through a `PlayableGraph` — one `AnimationPlayableOutput` on the prefab's own
`Animator`, an `AnimationMixerPlayable`, one `AnimationClipPlayable` per clip found BY NAME, a missing clip logged once
and left at weight 0 — with **no AnimatorController anywhere**, `applyRootMotion=false` and
`cullingMode=CullUpdateTransforms`; speed comes from this transform's own displacement, not from the network, so every
machine derives the same walk from the same replicated position with no `ZSyncAnimation` float and no extra ZDO key.
`Greet/Talk/Shrug/Nod` are the public one-shots P5 calls. `Core/BodyMotion.cs` (pure, 78 checks) is the blend: the
0.05/0.06 m/s hysteresis on a 0.15 s smoothed speed, the 0.15 s crossfade, the one-shot envelope (0.06 s in, hand back
at 85% of the clip's OWN length, no self-interrupt, a different one replaces without a gap), and the six weights
summing to 1 across a 40,000-step random walk. New config `Server.CustomBody` (synced+locked, default true) is the
switch; `Server.BodyPrefab` stays the engine prefab the merchant is cloned from. Console: `cargo body`, `cargo body
preview | walk | clip <Hello|Talk|Shrug|Nod> | clear`, and one line in `cargo status`. Off-game: builds clean (net48,
0 warnings), 1034 checks (78 with the model, 30 more from the adversarial review: a one-shot over a moving crossfade,
a replacement during the hand-back, a hitch through the blend-out, a clip shorter than the blend-in), nine model
mutations caught between the two; a 131,072-byte stand-in dropped at `Assets\valkyriescargo_kit`
embedded as `ValkyriesCargo.valkyriescargo_kit` and grew the DLL by exactly that much. **Seen on a screen
2026-09-07** (Wu'barrk's client): item 19 whole, item 20 as far as a standing, textured, upright Ingvar with his
feet on the ground, and `body=Ingvar` on a live merchant in the integrated run. Still not seen: how he reads in
daylight, and the walk and one-shot clips on a merchant (item 20 stays open on those). **The bundle exists as of
2026-09-07**: baked
from `models/ingvar.fbx` in Unity 6000.0.61f1 on Wu'barrk's Linux box (`tools/setup-ingvar-unity.sh`, the twin of
the .ps1), 3,826,415 bytes, 2 assets, `SkinnedMeshRenderer=True, bones=24, tris=31112`, six clips named `Hello,
Idle, Nod, Shrug, Talk, Walk` with `Idle/Talk/Walk` looping; embedding it takes the Debug DLL from 273,408 to
4,100,096 bytes, which is the bundle plus the resource header and is the check that catches a stale copy.

**P4 the authored flight, Wu'barrk, 2026-09-07 (PR #8, merged 1884fcd).** `Core/FlightPlan.cs` (pure, 39 checks) plans
the flight inside the pilot's active block: a STRAIGHT approach along the seeded bearing, shrunk by 12 m steps until the
start fits with an 8 m margin, turned a quarter at a time when a bearing has no room; the descent waypoint ON the
line carrying the glide altitude, capped at three quarters of the run; `TurningRadius(v, w)` and `Reachable(...)`
assert every waypoint is flyable at the shipped speed and turn rate (a pure pursuer cannot reach a point inside its own
turning circle; the review's finding, kept as code). `Server/Spawner.cs` authors the bird's ZDO (`Valkyrie` prefab,
`SetPrefab` explicitly because `CreateNewZDO` does not, non-persistent, owned by the PILOT, keys `VCargo_cargo`,
`VCargo_target`, `VCargo_turn`, `VCargo_dropped`), watches `VCargo_dropped` once a second for the director, and reclaims bird and
merchant on `Clear`; `MerchantEnabled = false` until P5, so nothing but a bird is authored and the merged DLL is safe
on a live server. `Patches/Patch_Valkyrie_Awake.cs` (prefix, `Priority.Low`, `__runOriginal`) skips vanilla `Awake`
for a bird carrying `VCargo_cargo` (vanilla takes `m_instance` before its owner guard and teleports `Player.m_localPlayer`)
and adds `Client/CargoFlight.cs`, which flies vanilla's own `UpdateValkyrie` maths on the owner only, writes
`ZDOVars.s_velHash` so every other screen dead-reckons a glide, and reads speed and turn rate from the new synced
`Server.FlightSpeed` (8) and `Server.FlightTurnRate` (45), never the prefab's. Off-game: the geometry simulated by
both sides (a 90 m start at 8 m/s drops at 11.7 m after 16.8 s; a 30 m start at 16.8 m after 13.6 s). `MerchantEnabled`
is true since P5 landed, so a visit now authors both. **Still not flown on a screen**: items 21 and 22. A bird HAS
been authored in a live visit (the integrated run's last line, with the plan's numbers in the log), but nobody has
watched the glide, the drop, or a second client's dead-reckoning.

**The BarrkBOT export, Wu'barrk, 2026-09-07 (branch `b/barrkbot-export`).** `BARRKBOT_CONTRACT.md` (repo root) is
the per-mod contract, on the BlightedHeart/Let It Grow template, against the authoritative
`WindowsDEV/Discord-BarrkBOT/docs/MOD_EXPORT_CONTRACT.md` v4. Three files under
`BepInEx/config/ValkyriesCargo/`: `barrkbot_cargo_market.json` (the live catalogue, keyed by prefab; the
72-entry default catalogue measures to exactly 5 parts under the contract's 2,600-char row budget, `_2`
through `_5`), `barrkbot_cargo_traders.json` (per player, keyed by platform id: coins spent/earned, deals
settled, items bought/sold — new counters, `Core/TraderLedger.cs`, nothing accumulated these before),
`barrkbot_cargo_visits.json` (one row per visit that has ended this session, newest first, `Core/VisitHistory.cs`).
Both new ledgers are in-memory, reset per session, fed from `Server/VisitDirector.cs`'s existing `Settle`
(a trader's row) and `End` (a visit's row) — no new hook into the deal wire or the event system. `Core/BarrkExport.cs`
(PURE) shapes the rows; `Core/BarrkRollover.cs` (PURE) paginates by each row's REAL rendered JSON width
(measured with the real serializer, never guessed) and ranks `<collection>_leaders` over the whole roster
before a split, the v4 contract's "hard rule". `Server/BarrkBotExport.cs` renders through the pure
`Core/Json.cs` — a writer, never a reader, shaped like Newtonsoft's compact and indented output so the
widths and the files do not move — and writes each file `.tmp`-then-rename. (It rendered through
Newtonsoft.Json for one day: decision 3 in `docs/DECISIONS-WUBARRK.md` brought in a `<Reference>` and the
`ValheimModding-JsonDotNET-13.0.4` manifest dependency for those two calls; **the owner had it removed on
2026-09-07** — BepInExPack only, the locked row stands.) Decision 4 (the
sidecar stays authoritative, the JSON is a mirror) is enforced, not just documented: `VisitDirector.Tick`
runs a new `ExportCadenceSeconds` (60 s) timer alongside the existing save cadence — no new coroutine, house
rule 2 — that calls `Core/SidecarThenMirror.Run`, which runs the sidecar's own `Flush` to completion first and
starts the JSON export only if that succeeded; a throw inside the export is caught there AND a second time
inside `BarrkBotExport.Write` itself (logged at most three times each). New config `Server.BarrkBotExport`
(synced+locked, default true). Off-game: builds clean (net48, 0 warnings), 1190 checks (98 new: the rollover's
exact-budget boundary, the ranking exclusions, the coins/items accounting, oldest-first eviction, newest-first
ordering, and the ordering guarantee itself — each proven to fail without its fix, real mutation output kept
in the PR). A standalone run of the real `Core/BarrkExport.cs` + `Core/BarrkRollover.cs` against Newtonsoft.Json
(outside CoreTests, which stays dependency-free) produced real, well-formed sample output for both a fresh
server and a populated one. **Seen on a dedicated server 2026-09-07 10:41 (StormTest, PR #46's build)**: all seven
files landed 60 s after `director up`, valid JSON, 72 market rows across the five parts, `generated_at` moving
every cycle with nobody online — item 23's server half. BarrkBOT itself has still never read one of these files,
and no deal row or visit row has been seen in them: shape-verified and file-verified, not yet consumer-verified
(the authoritative contract's own distinction). See "What to verify in-game" item 23.

**P5 the merchant and the 0.1.0 integration, Wu'barrk, 2026-09-07 (PR #22, merged; `v0.1.0-rc1` tagged).**
`Client/CargoMerchant.cs` on `Core/MerchantPlan.cs` (pure): the carry pin, the drop handoff, follow, the callout,
the leash, dismissal and the Odin vanish; `Patches/Patch_Humanoid_Awake.cs` adds the component, `Patch_Character_InIntro.cs`
holds the carry, and `Patch_Character_Damage.cs` — which patches **`Character.RPC_Damage`, not `Character.Damage`**,
the correction the knowledge base forced — makes him immortal. `Spawner.Sweep` reclaims orphans at boot and
`MerchantEnabled` is true. Also in the same merge: the `vc_` → `VCargo_` rename with `Core/Keys.cs` as the one place
that types each name (issue #16), P4's drop-point bound from the P11 trust-boundary pass (NaN-safe), the P12
BarrkBOT export, and Thorium's economy decisions (`docs/DECISIONS-WUBARRK.md`): the **Fair Market Act**, the purse
at 1500, four Wants from base 2 to 3, `PriceChangePolicy` deleted, and house rule 4's written exception for the one
runtime material copy. 1301 off-game checks, 0 warnings. Its one live run is the section below.

**P10b the boot-time engine probes, 2026-09-07 (branch `a/p10b-probes`).** `Core/EngineBaseline.cs` (PURE) is
the build this DLL was compiled against as constants — 0.221.12, network 36, player 43, world 37, the two Steam
build ids — and the comparison against the four numbers actually running; the numbers are copied rather than
referenced because vanilla's `Version` type is `internal` and its three numbers are `const`, so a direct
reference would be inlined at OUR compile time and answer "same build" on every Valheim ever released.
`Core/EngineProbes.cs` (PURE) is the registry: 25 named engine facts (the P11d audit's 53 rows folded in on
2026-09-07, PR #46), ranked worst-first by how much SILENCE a
break would come with, each with what it looks at and what turns itself off; seven are method BODIES and are
registered as **not probeable**, which `cargo engine` says out loud rather than implying a pass. A probe is a
veto and never a permit — a fact that has not run, could not be probed, or was never registered answers YES, so
a bug in the registry can never be the thing that turns the mod off. `EngineCheck.cs` is the one file that
touches a game type: every probe is a PAIR (a catching `Probe*` and a `[MethodImpl(NoInlining)]` `Check*`),
because Mono resolves a member access when the CALLER is JIT-compiled and a try/catch in the same method never
runs. It is the mod's ONE named exception to "our files name no private member": `ZSyncTransform.m_velocityCached`,
`BaseAI.m_character`, `Character.RPC_Damage`, `RandEventSystem.Awake`, `Humanoid.Awake`, `ZNetView.Awake`,
`Terminal.InitTerminal` and `BaseAI.Follow` / `MoveTo` are named in strings, handed to reflection, and never called. It runs from plugin `Awake` AFTER the config binds and BEFORE `PatchAll`. Two
features consult their probe and degrade rather than throw (`Server/CargoEvent.cs` refuses to register or start
the event; `Client/BodyLoader.cs` keeps the stand-in), and `Server/MarketStore.cs` REFUSES a sidecar written by
a newer build — held, never `.corrupt`, nothing saved over it, because that is somebody's market and not a
corrupt file. The gap `docs/P10B-PROBE-GAP.md` found is fixed: the probe asked for the public
`Character.Damage` while the immortality patches the private `Character.RPC_Damage(long, HitData)`. Off-game:
builds clean (0 warnings), 1697 checks after PR #46. **SEEN ON A MACHINE 2026-09-07 10:40 on StormTest** (dedicated,
0.221.12, PR #46's build): the boot log carries
`built against Valheim 0.221.12 (network 36, player 43, world 37; Steam build 21981559 client / 21981590 server; bodies read 2026-09-06); running same build 0.221.12 (net 36, player 43, world 37); probes 18/18 ok, 7 not probeable.`
and then `patches 18/18 applied ... engine: same build 0.221.12 (net 36, player 43, world 37); probes 18/18 ok, 7 not
probeable` in the loaded line — every probe resolved its real member under Mono, no FALSE failure, no `registry:`
line (item 24's boot half; the moved-version direction is still open). The document is
`docs/ENGINE-PROBES.md`; the baseline it is dated against is `docs/ENGINE-BASELINE.md` (P10a).

---

## Commands

```powershell
.\tools\fetch-libs.ps1     # once per machine: copies game/BepInEx DLLs into libs\
.\tools\run-tests.ps1      # off-game logic tests (net8.0) — run before every commit
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

Console today: `cargo status | version | engine | prefab <name> | body [preview|walk|clip <name>|clear] | stock [prefab] |
deal buy|sell <prefab> [count] | claim | terminal demo|open|close | visit [player] | dismiss | reset | save`.
`engine` (P10b) prints the Valheim build this DLL was written on against the one actually running, then every
engine probe with its risk rank, what it looked at and what turns itself off when it fails; `cargo status`
carries the same thing in one line, second from the top. It is the first thing to ask for in a bug report from
a player whose Valheim moved. `visit`, `dismiss`, `reset` and `save` are
admin verbs: on a server or listen host they run in place; from a client they ride `VCargo_admin` to the server,
where the public `ZNet.IsAdmin` (RavenEye's `AdminGate` shape, fail closed) decides and `VCargo_reply` prints the
answer in the caller's console. `deal` is the terminal's deal without the terminal: it builds the same `Deal`,
sends it through `CargoRpc` and applies the answer through `DealApplier`.

---

## Layout

Built:

```
ValkyriesCargo/
  ValkyriesCargo.cs          plugin entry: config (creates the ConfigSync), Harmony, tick, boot line
  Config/ModConfig.cs        Server.* synced+locked, Client.* local, VisitState/MarketState channels
  Core/CargoTick.cs          the ONLY Update and the only OnGUI in the mod; role decided at runtime
  Core/Keys.cs               PURE: every ZDO key and RPC name this mod owns, typed once, under the VCargo_ prefix
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
  Core/TraderLedger.cs       PURE: per-player trade totals THIS SESSION, for BarrkBOT (BARRKBOT_CONTRACT.md)
  Core/VisitHistory.cs       PURE: visits that have ended this session, newest first, for BarrkBOT
  Core/BarrkRollover.cs      PURE: the BarrkBOT export's v4 rollover (real-width pagination) and leaderboard ranking
  Core/BarrkExport.cs        PURE: the BarrkBOT export's payload shaping: Market/TraderLedger/VisitHistory -> rows
  Core/SidecarThenMirror.cs  PURE: decision 4 proved off-game: the sidecar succeeds first, a mirror throw never reaches the caller
  Server/MarketStore.cs      the sidecar on disk: valkyriescargo_{worldUid}.dat, .tmp/.bak/.corrupt
  Net/DealWire.cs            server end: VCargo_open/close/deal/ack/claim/dismiss on each peer's ZRpc; VCargo_dealt back
  Server/BarrkBotExport.cs   writes barrkbot_cargo_market/traders/visits.json under BepInEx/config/ValkyriesCargo/, from VisitDirector.Tick
  Net/CargoTransport.cs      client end (the real ICargoTransport), LocalTransport (listen host), Deliveries
  Client/DealApplier.cs      the ONLY inventory writer for a deal: CanApply, Apply, by shared item name
  Client/InboxStore.cs       the applied delivery ids on disk (config folder)
  Client/Terminal/CargoTerminal.cs   the window: ICargoTerminal on the gilt theme, drawn from the one OnGUI
  Client/Terminal/TrayModel.cs       PURE: the staging tray, Validate/Build/AutoFill/Answer
  Core/BodyMotion.cs         PURE: Ingvar's six clip weights: hysteresis, crossfade, the one-shot envelope
  Client/BodyLoader.cs       the embedded bundle, once; the body swapped onto the merchant, additively
  Client/IngvarBody.cs       the driver: a PlayableGraph over the six clips; speed from displacement
  Core/FlightPlan.cs         PURE: the flight inside the active block; TurningRadius/Reachable (Wu'barrk, P4)
  Core/MerchantPlan.cs       PURE: the merchant's state machine: carried -> approaching -> trading -> leaving (Wu'barrk, P5)
  Server/Spawner.cs          authors the bird and the merchant, owned by the pilot; watches VCargo_dropped; Sweep reclaims orphans (Wu'barrk)
  Client/CargoFlight.cs      the owner flies the bird from VCargo_target/VCargo_turn; writes s_velHash for the watchers (Wu'barrk)
  Client/CargoMerchant.cs    Ingvar on the ground: the plan, the carry pin, follow, callout, dismissal, the vanish (Wu'barrk, P5)
  Patches/Patch_Valkyrie_Awake.cs   prefix, Priority.Low: skips vanilla Awake for our bird only (the named exception)
  Patches/Patch_Humanoid_Awake.cs   postfix: adds CargoMerchant when the ZDO carries VCargo_ingvar (Wu'barrk, P5)
  Patches/Patch_Character_InIntro.cs  postfix: __result true while the carry link is set, so the fall never accumulates
  Patches/Patch_Character_Damage.cs   prefix on Character.RPC_Damage (NOT Damage): the merchant is immortal
  Patches/Patch_BaseAI_IsEnemy.cs     prefix on the static BaseAI.IsEnemy(a, b): any pair with the merchant answers "not enemies" (ghost mode, F11)
  Core/Ghost.cs              PURE: the ghost-mode decision (run vanilla / honour a prior cancel / not enemies), the Immortality shape
  Server/VisitDirector.cs    where the world runs: gather ZDOs -> Scheduler -> event -> VisitState/MarketState
  Server/CargoEvent.cs       the vanilla RandomEvent `valkyries_cargo`: definition, registration, start, remaining
  Server/AdminGate.cs        vanilla's ZNet.IsAdmin(hostName), fail closed (RavenEye's shape)
  Client/ComfortReporter.cs  VCargo_rested / VCargo_comfort on the local player's own ZDO, every 2 s
  Net/AdminRpc.cs            VCargo_admin (client -> server) and VCargo_reply (server -> client) on the routed RPC
  Patches/Patch_RandEventSystem_Awake.cs   prefix, Priority.Low, return true: registers the event
  Net/CargoRpc.cs            the client-side surface the terminal calls; the demo transport (PR #1)
  Client/Terminal/ICargoTerminal.cs   what the merchant calls; Track B implements it (PR #1)
  Patches/Patch_Terminal.cs  the `cargo` console: status, version, prefab, body, stock, deal, claim, terminal; admin visit/dismiss/reset/save
  Libs/ServerSync.cs         NOT OURS: blaxxun ConfigSync.cs, compiled in as shared source
  Libs/SharedUI/GiltFrameTheme.cs, UIFocus.cs   NOT OURS: Wu'barrk's VikingOS 0.9.8 shared source, MIT (PR #2)
tests/CoreTests/             net8.0 harness; compiles the REAL Core sources against stubs
tests/EconSim/               the economy simulation: nine seeded scenarios on the real Core; writes docs/ECONOMY-SIM.md
tools/                       fetch-libs, run-tests, package, deploy-test, tail-log, set-test-config, run-econsim; setup-ingvar-unity.ps1, build_ingvar.py, preview_ingvar.py, unity/IngvarBundleBuilder.cs (the bake, Wu'barrk's)
libs/                        gitignored; populated by fetch-libs.ps1
docs/                        TODO (**the tracker: who owns what, cut after rc1**), DESIGN, TLDR, CATALOGUE, WORKSPLIT, RELEASE, PROOF-CLIENT (the verify runbook), CLIENT-AUDIT, ECONOMY-SIM, DECISIONS-WUBARRK, HANDOFF-CLAUDE, HANDOFF-WUBARRK, REVIEW-v5, ENGINE-BASELINE + ENGINE-SURFACE + engine-sweeps/ (P10a), knowledge-base/ (Wu'barrk's snapshot), data/items table, the partner's drafts
models/                      Ingvar's source art (ingvar.fbx + ingvar_albedo.png, the one binary exception) and the bake docs (Wu'barrk's)
```

Nothing is planned-but-missing any more: every file design section 3 names exists on `main` as of PR #22, and
the four that were listed here (`Client/CargoMerchant.cs`, `Patch_Humanoid_Awake.cs`, `Patch_Character_InIntro.cs`,
`Patch_Character_Damage.cs`) are in the list above.

---

## House style — inherited (each rule came from a measured failure in a sibling; not re-derived here)

1. **Harmony: prefixes for behaviour at `Priority.Low`, honouring `__runOriginal`; result-decorating
   postfixes at default priority where appending is the whole point. Never a max- or high-priority
   replace.** The planned `Patch_Valkyrie_Awake` skips vanilla for our own object only, the narrow
   named exception RavenEye recorded for `UpdateNoMap`.
2. **No long-lived coroutines.** `CargoTick` is the one `Update`. Nothing else owns a timer. Exception, written
   down 2026-09-07: a driver MonoBehaviour that lives and dies with its own GameObject and updates only itself
   (`Client/IngvarBody.cs`; `Client/CargoFlight.cs` when it lands). The rule is about timers that outlive their object.
3. **Cosmetics off the gameplay path.** Every patch body is its own try/catch, logging at most three
   times. And every patch CLASS is applied on its own — **never a bare `PatchAll()`** (issue #31,
   `Patching.cs`): a patch that fails to APPLY is logged by name, counted, printed by the boot line
   (`patches N/M applied`) and by `cargo status`, and the mod runs degraded and says so. The one
   exception is the vendored ServerSync's patch classes (the version gate and the config lock), whose
   failure REFUSES the mod outright: nothing ticks, nothing registers, the console still answers.
   The decision is pure (`Core/PatchLedger.cs`). Widening the load-bearing set was delegated to Track B and
   DECLINED (`docs/DECISIONS-WUBARRK.md` §9): between refuse and degrade there is a third answer — a FEATURE
   switching itself off on `PatchLedger.IsApplied(name)`. The flight is the one case: without
   `Patch_Valkyrie_Awake`, vanilla `Valkyrie.Awake` runs on our bird and teleports the local player into
   the sky, so `Spawner` authors no bird and puts Ingvar on the ground, and says so.
4. **Never patch `EnvMan`. Never touch materials, textures or shaders** — with one exception, written
   down 2026-09-07 the way rule 2's was. Reading `EnvMan.IsDay()` is fine. `Client/BodyLoader.cs` builds
   ONE material at runtime: a COPY of the stand-in's own, with our albedo in it. It has to be at runtime
   and cannot be done in the bundle, because a bundle baked in the Editor carries Unity's `Standard` and
   Valheim does not light `Standard` at all. Nothing vanilla is mutated — the donor material is read and
   never written — and the exception is exactly that one copy. `docs/knowledge-base/SKINNED-CHARACTER-BUNDLE-FACTS.md` §4.
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

`docs/knowledge-base/` (Wu'barrk's `libs-Tools\`) is the family's accumulated engine knowledge, written up from mods that shipped and
broke in production. **This mod did not consult it until 2026-09-07 and paid for that once already**
(see the two corrections below). Consult it before decompiling, and before designing any system whose
shape another mod has already found the hard way.

> **It is in this repository at `docs/knowledge-base/`** (decided by the owner 2026-09-07): a snapshot of
> Wu'barrk's `~/WubarrkCODING/libs-Tools/` as of that day, **67 Markdown files, no binaries; the snapshot is
> complete** — the `VALHEIM-API-REFERENCE\` folder, the last piece missing, landed with PR #17 on 2026-09-07.
> His copy is the source and updates
> arrive as PRs. Anything this mod actually depends on is still copied into CLAUDE.md or DESIGN as a quoted
> fact with its source named — as the two corrections below are — so the code never rests on an unread
> document, and so a fact survives the snapshot going stale.

| Where (under `docs/knowledge-base/`) | What is in it |
|---|---|
| `IMPLEMENTATIONS\MASTER_IMPLEMENTATIONS.md` | The index: every reusable system in every project, one line each, pointing at a per-project detail file. Its **READ FIRST** section is the moving-an-object rules and the traps that cost real player data. |
| `IMPLEMENTATIONS\DvergrAllies.md` | **P5's ground truth.** A shipped mod that clones Dverger prefabs, tames them, makes them follow and overrides their `MonsterAI`. The exact de-hostility field list, `Character.Faction.Players`, the staggered re-apply, the `Tameable` retrofit. |
| `IMPLEMENTATIONS\WingsoftheValkyrie.md` | Flight movement and a multiplayer VFX state sync over custom ZDO fields — the same shape as `VCargo_state`. |
| `IMPLEMENTATIONS\AwayFromHome.md`, `MistsofAvalor.md` | The bundle pipeline and the grave-relocation disaster. Already cited by `models\README.md`. |
| `VALHEIM-API-REFERENCE\` | 13 files of decompiled API facts with line numbers into the 0.221.12 decompile, plus a README. **`09-DAMAGE-ZDO-MULTIPLAYER.md` is the ZDO and damage authority** — who runs what on which machine, and a GOTCHAS list worth reading whole; both corrections below came out of it. `03-ZNETSCENE-AND-PREFABS.md` is the one behind `Spawner`'s authoring. |
| `SKINNED-CHARACTER-BUNDLE-FACTS.md` | **P8's ground truth, written FROM this mod 2026-09-07.** The five ways a custom character out of an AssetBundle is silently wrong, each of which passes every gate a build script can check: the `Armature\|` clip prefix that breaks by-name lookup and the loop table together, stray source geometry that only shows as an 80-triangle discrepancy, `sharedMesh.bounds` being bind-pose data that lies about the up-axis, Unity's `Standard` shader that Valheim does not light, and a donor's emission colour left behind when its mask is cleared. Also the debug order that converges. |
| `*-FACTS.md` | Per-topic findings: dedicated server, headless/empty server, ZDO wire limits, console routing, player identity, player attach. |

### Two corrections it forces on this repo

1. **A `ZDOID` is a session handle, not an identity — never persist anything against one.** `ZDO.Load`
   opens with `m_uid.SetID(++ZDOID.m_loadID)`: every ZDO is renumbered every time the world is read
   off disk. The id is stable while the world stays loaded and meaningless the moment it does not,
   so the feature works all session and every key in it is orphaned by the next login. It cost
   TortalPortal its favourites feature. **`Spawner.CarrierKey` (`VCargo_carrier`) writes the bird's
   `ZDOID` onto the merchant's PERSISTENT ZDO and was exposed to exactly this.** **Fixed in P5** (PR #22):
   `Spawner.Sweep` walks every merchant ZDO at boot, destroys the ones whose visit is not running, and for
   the one that is, sets `VCargo_carrier` back to `ZDOID.None` and `VCargo_state` to `Approaching` — logged
   as `N carry link(s) cleared (a restored ZDOID means nothing)`. `VCargo_state` is the authority; a
   surviving id never is.

2. **`Character.Damage` is a thin RPC sender, not where damage happens.** It runs on the ATTACKER's
   client, calls `FindWeakSpotIndex` and `InvokeRPC("RPC_Damage", hit)`, and nothing else — reading
   or modifying health there does nothing. The work is in the private `Character.RPC_Damage`, whose
   first lines run on EVERY client that has the victim instanced; the `if (!m_nview.IsOwner()) return;`
   gate is partway down. So `Patch_Character_Damage` — which keeps that FILE name — is a prefix on
   `Character.RPC_Damage`, not on `Damage`; anything that mutates state in a postfix there must re-gate
   on `IsOwner()` or it applies once per peer. **Landed as written** in P5 (PR #22):
   `[HarmonyPatch(typeof(Character), "RPC_Damage")]`. `docs/DESIGN.md` §3.3 still names the file, not the
   method, which is why the file name was left alone.

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
- **`Libs/SharedUI/GiltFrameTheme.cs`, `Libs/SharedUI/UIFocus.cs`** = Wu'barrk's VikingOS 0.9.8 shared
  source, MIT, vendored 2026-09-06 (PR #2); each file's header records its origin and version. `UIFocus` carries two Harmony patches (`GameCamera.UpdateMouseCapture`,
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
| The round trip | **The Fair Market Act (owner, 2026-09-07; `docs/DECISIONS-WUBARRK.md` §2).** The code fix, not the config fix: `Market.PaysFor` clamps a Ware's buy-back multiplier at 1.0, so he never pays more than `base × SpreadBuy` for something he sells; `PriceFor` (what he charges) and every `Want` are untouched. `MarketRules.FairMarketAct` / `Server.FairMarketAct`, synced+locked, default on |
| 0.1 body | A `Dverger` clone as the chassis — tamed, following, immortal — and, **since PR #22 (2026-09-07), Ingvar's own baked mesh ADDED onto that clone**, the stand-in's renderers switched off and never destroyed. The decision itself is unchanged; what shipped is recorded here. `Server.CustomBody` (default true) is the switch back |
| Console prefix / GUID / namespace | `cargo` / `com.raveniron.valkyriescargo` / `RavenIron.ValkyriesCargo` |

---

## Engine facts the code relies on today (bodies read 2026-09-06; the full list is `docs/DESIGN.md` section 0 and 3)

- The installed Valheim runs on **Unity 6000.0.61f1** (`UnityPlayer.dll`); bundles must be built with that Editor.
- **ServerSync broadcasts on change only.** No heartbeat. Client writes are rejected while locked unless
  the client is on `adminlist.txt`. Payloads under 10 000 bytes go uncompressed.
- **Comfort never leaves the client** (`SE_Rested.CalculateComfortLevel` is local); the client will
  write `VCargo_rested` / `VCargo_comfort` on its own character ZDO, which replicates because the client owns it.
- **Objects are instantiated on a client only inside its active zone block**
  (`ZNetScene.InActiveArea`: `|zone − centre| ≤ m_activeArea − 1`, 64 m zones); outside it
  `RemoveObjects` destroys the instance and a non-persistent owned ZDO with it. The Valkyrie starts
  ~90 m out, never 800.
- **`ZoneSystem.m_activeArea` is 2, not the compiled default of 1** (read live, 2026-09-07). The block
  is 3x3 zones - 192 m - so `FlightPlan`'s configured 90 m start survives whole and neither the shrink
  nor the bearing turn fires in practice. Both still ship, because the value is an inspector field and
  a scene may say otherwise; `Spawner` reads it at runtime and never assumes.
- **The `Valkyrie` prefab overrides almost every field initialiser** (read live, 2026-09-07):
  `m_speed` 20 (not 10), `m_turnRate` 20 (not 5), `m_startDistance` **800** (not 500), `m_startAltitude`
  190 (not 500), `m_descentAltitude` 180, `m_startDescentDistance` 300, `m_attachOffset` (0, 0.30, 0.40)
  (not (0,0,1)). `m_attachPoint` EXISTS: `'Attach'`, under `valkyrie2/Armature/.../r_foot` - the
  merchant hangs from her right talon. Our flight uses none of vanilla's numbers except `m_dropHeight`;
  speed and turn rate are `Server.FlightSpeed` / `FlightTurnRate`.
- **`Odin.m_ttl` on the shipped prefab is 60, not the 300 the field initialiser says** (read live,
  2026-09-07). Nothing reads it - the departure borrows only the `m_despawn` EffectList - but design 3.6
  cited the 300 as the reason our visit is 300 s, and that reasoning was never true. **Never add the
  `Odin` COMPONENT to the merchant**: he would delete himself a fifth of the way into the visit.
  `m_despawn`'s one entry, `vfx_odin_despawn`, carries a `ZNetView`, so by the effect rule the owner
  creates it and vanilla replicates it - one vanish per screen.
- **The shipped `Dverger` has `NpcTalk`** (`randomTalkInterval` 30) and **spawns holding
  `DvergerArbalest`** + `Dverger_melee` (read live, 2026-09-07). Both are why design 3.3's "NpcTalk
  disabled if present" and `UnequipAllItems()` are load-bearing rather than precautionary. It has **no**
  `Tameable`, so `MonsterAI.m_follow` (private, non-persisted) is re-established by us, never restored.
- **`Character.InIntro()` zeroes velocity; it does NOT grant immunity.** Its caller sets
  `m_maxAirAltitude` to the current height and zeroes the Rigidbody's linear and angular velocity, which
  is exactly what a carried merchant needs and nothing more. Immortality is a separate patch, and it
  belongs on `Character.RPC_Damage`, not `Character.Damage` - see the knowledge-base section.
- **`ZRoutedRpc.instance` is null for the whole of plugin `Awake`** and is re-created on every world
  join; register routed handlers per session, direct `ZRpc` handlers on peer connect.
- `EnvMan.IsDay()` is static. `Character.m_collider` is a `CapsuleCollider`. `Odin.m_despawn` and
  `Odin.m_ttl` are public, and **`m_ttl` on the shipped `odin` prefab is 60, not the 300 the field initialiser
  says** (read off the prefab in the headless sandbox, PR #8; the earlier "UNCHECKED" here was stale). Nothing of
  ours turns on it: the departure borrows the `m_despawn` EffectList and never the `Odin` COMPONENT, and the 300 s
  our clock quotes is the event's `m_duration`, which was never Odin's timer. `cargo prefab odin` confirms it on a
  live client (item 5).
- **The flight** (read from the prefab and the decompile by Wu'barrk, PR #8): the shipped `Valkyrie` says speed 20,
  turn rate 20 and drop height 10 (compiled defaults 10, 5, 10); at 20 m/s and 20 deg/s the turning circle is 57 m,
  wider than the whole 76 m run, so ours flies at the synced `FlightSpeed` 8 / `FlightTurnRate` 45. `ZSyncTransform`'s
  non-owner path dead-reckons along `ZDOVars.s_velHash` (capped at 2 s) and `OwnerSync` writes that key only when the
  velocity changes from its cache, so the owner's own write survives. A dedicated server pins its reference position
  to (1000000, 0, 1000000) every fixed frame and instantiates nothing of ours.
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

### From the knowledge base, quoted with its source (added 2026-09-07)

Each of these was read out of `docs/knowledge-base/` — the family's own decompile-verified findings — and is
copied here because the code turns on it. The file named after each is where the full reasoning lives.

- **"A dedicated server stops its world clock entirely while empty"**: `ZNet.UpdateNetTime` advances `m_netTime`
  only while `GetNrOfPlayers() > 0`, so `ZNet.GetTimeSeconds()` freezes the moment the last player logs out while
  every real-frame-time system keeps running — the disguise is that the server "looks alive and accomplishes
  nothing" (`IMPLEMENTATIONS/WorldClock.md`; `HEADLESS-AND-EMPTY-SERVER-FACTS.md` §3;
  `IMPLEMENTATIONS/MASTER_IMPLEMENTATIONS.md` lesson 14). **This mod is on the right side of it by construction**:
  `VisitDirector.Tick(dt, now, worldTime)` takes `now = UnityEngine.Time.time` and
  `worldTime = ZNet.GetTimeSeconds()`, and the market's drift and the visit clock count `worldTime` (so they stop
  with nobody there, which is what should happen) while the roll interval and the scheduler's cooldowns count
  `now` (so they keep running). Do not "fix" either to match the other.
- **`ZoneSystem.GetGroundHeight` is a physics raycast, not a lookup**: "without a loaded Heightmap the float
  overload silently returns your input Y; prefer the `out bool` overload and treat false as 'no terrain'"
  (`VALHEIM-DEDICATED-SERVER-FACTS.md`; the miss behaviour again in
  `SNAPTOGROUND-AND-REMOTE-ZONE-LOADING-FACTS.md`). It is why the drop's Y is refined on the PILOT's client, which
  has the terrain, and never on the server.
- **Routed-RPC names share ONE global hash namespace with the base game and every mod — "prefix with your plugin
  GUID"** (`VALHEIM-DEDICATED-SERVER-FACTS.md`; `IMPLEMENTATIONS/SoM-v2-Surveys/SURVEY-vanilla-api.md` item 14).
  This is the fact behind issue #16: the old two-letter `vc_` was not this mod's to claim, and `Core/Keys.cs` now
  types every name once under `VCargo_`. Same source, same paragraph: registration is a `Dictionary.Add` on a
  **per-world-session** `ZRoutedRpc`, so a same-name double-register **throws** and a stale "already registered"
  flag from the previous world leaves the next one with NO handlers — key registration on the instance reference,
  which `AdminRpc.EnsureRegistered` does.
- **The routed-RPC parameter types are a closed list**: int, uint, long, float, double, bool, string, `ZPackage`,
  `List<string>`, `Vector3`, `Quaternion`, `ZDOID`, `HitData`, `ISerializableParameter`. **"No arrays or enums —
  unsupported types are silently dropped and the receiver deserialises garbage."** And `Everybody` (0L) **"also
  invokes the handler locally on the caller"**, so a broadcast must not also act by hand or a listen host does it
  twice (`SURVEY-vanilla-api.md` item 14; `VALHEIM-DEDICATED-SERVER-FACTS.md`).
- **A non-persistent ZDO the server creates is never handed to a client by the engine**: `ReleaseNearbyZDOS`
  "SKIPS `!Persistent` ZDOs entirely ... never simulates, never culls, leaks until restart
  (`RemoveOrphanNonPersistentZDOS` only reaps on peer disconnect)" (`VALHEIM-DEDICATED-SERVER-FACTS.md`;
  `IMPLEMENTATIONS/MistsofAvalor.md` calls it "the one absolute constraint"). `Spawner` is safe **because it sets
  the owner explicitly at creation** (`bird.SetOwner(pilotUid)`), which is the only way a non-persistent ZDO ever
  reaches a client — and the bird is reaped when that peer disconnects, which is the behaviour wanted.
- **A PERSISTENT ZDO is never released when its owner disconnects** (`HEADLESS-AND-EMPTY-SERVER-FACTS.md` §3;
  `MASTER_IMPLEMENTATIONS.md` lesson 14); the only handover is the 2 s `ZDOMan.ReleaseZDOS` sweep, which
  "only scans sectors near each peer's refpos" (`IMPLEMENTATIONS/AwayFromHome.md`). The merchant is persistent,
  so if the pilot logs out standing on him nobody adopts him until a peer walks into the sector — which is what
  `Spawner.Sweep` at boot exists to clean up after.
- **`transform.position` does not move a non-kinematic Rigidbody** — "assigning it is reverted on the next physics
  step; use `rb.position` (and zero `velocity`/`angularVelocity` if you don't want it to resume its previous
  motion)" (`HEADLESS-AND-EMPTY-SERVER-FACTS.md` §4; the same trap cost AwayFromHome a debugging day,
  `MASTER_IMPLEMENTATIONS.md` lesson 13). The carry pin sets both, deliberately.
- **Four longs look like a player and only one is an identity** (`PLAYER-IDENTITY-FACTS.md` §1–2):
  `PlayerProfile.GetPlayerID()` / `ZDOVars.s_playerID` is **the** identity, "forever, across worlds and servers";
  `ZNetPeer.m_uid` is a connection; and **`ZDOID.UserID` is the trap** — it is `ZDOMan.m_sessionID`, "this session
  only", and "will never equal `s_creator`, `s_playerID`, or anything you can persist. The name is the whole
  problem." Our owed ledger keys on `peer.m_socket.GetHostName()` (the platform id, `Steam_7656…`), which is
  stable across sessions and is fine.
- **A dedicated server's `Game.instance.GetPlayerProfile()` is a placeholder**: `("Stranger", <random long>)`,
  generated at boot, "and a placeholder written down is worse than a null" — guard with `IsDedicated()` **and**
  reject the literal names `"Stranger"` and `"..."` (`PLAYER-IDENTITY-FACTS.md` §5). `CargoTick.HostKey` reads the
  profile and must therefore run only on a listen host.
- **A client-side admin check is advisory at most; the server decides.** Server-side `ZNet.ListContainsId` "parses
  the id and falls back from the qualified form", matching both `76561…` and `Steam_76561…`; client-side
  `ZNet.PlayerIsAdmin` does a plain `Contains` of only one form, and **`ZNet.GetAdminList()` returns an EMPTY list
  on a client** — Njord shipped a client-side gate on `LocalPlayerIsAdminOrHost()` and refused a listed admin
  (`CONSOLE-COMMAND-ROUTING-FACTS.md`). Ours is right by this rule: `VCargo_admin` rides to the server and
  `AdminGate` decides there.
- **A console command runs on the machine it was typed into.** The `onlyServer` flag gates *permission*, not
  location; `Terminal.IsCheatsEnabled()` returns `ZNet.instance.IsServer()`, so **"do not register a mod command
  with `isCheat: true` if admins on a dedicated server need it"** — a client would be refused
  (`CONSOLE-COMMAND-ROUTING-FACTS.md`). `cargo` is registered with neither flag and routes its own admin verbs.
  Vanilla's own pipe, if ever wanted, is `ZNet.RemoteCommand`, adminlist-checked in `RPC_RemoteCommand`.
- **512 KiB (524,288 bytes) per reliable Steam message**, and an oversized one is retried forever until the peer
  drops ~30 s later; "keep any single ZDO's total payload under ~440 KB" (`ZDO-WIRE-LIMITS-FACTS.md`). Ours are
  hundreds of bytes; a future payload chunks or it takes the server down quietly.
- **`ZNet.GetAllCharacterZDOS()` is THE server-side "where is every player"** — the local character ZDO plus every
  ready peer's, position and rotation kept fresh each physics tick — and it **allocates, so never per-tick**.
  `ZNetPeer.m_refPos` is "always populated, updated every 2 s, **NOT gated** by the map-visibility toggle"
  (~2 s stale), while **`ZNet.GetPlayerList()` positions ARE gated** by `m_publicPosition` and read `(0,0,0)` for
  a player with the map toggle off: "never use for game logic" (`VALHEIM-DEDICATED-SERVER-FACTS.md`).
  `VisitDirector.Gather` uses the first, once a second.

### From the P10a client-vs-server sweep (`docs/engine-sweeps/2026-09-07-baseline-client-vs-server.md`)

- **`ZNet.IsDedicated()` is a per-assembly compile-time constant, not a runtime test**: `return false` in the
  client's `assembly_valheim`, `return true` in the server's (`PLAYER-IDENTITY-FACTS.md` §6, now **confirmed
  against both real builds** by the sweep rather than inferred). Reading the client decompile to reason about a
  dedicated branch says the branch is dead; it is not. This mod already refuses to use it — `HasRenderer` is
  `SystemInfo.graphicsDeviceType` — and that decision is recorded in `ValkyriesCargo.cs`.
- **`Terminal.AddString` logs on a dedicated server and not on a client.** The server build's overload writes
  every console line to the log; the client's does not. That is a fact about how every headless proof in this
  file is read: a console answer that appears in `LogOutput.log` on a server will not appear there on a client,
  and the same command's output has to be read off the screen instead.
- The flight bullet above — "a dedicated server pins its reference position to (1000000, 0, 1000000) every fixed
  frame and instantiates nothing of ours" — was checked word for word against both builds and is correct as
  written. It is the difference **that matters**, not the only one: five more surface members differ between the
  two builds, and that report names each with the reason it changes nothing here.

### From Wu'barrk's headless prefab reads (PR #14 comment, 2026-09-07; a shadow dedicated server with BepInEx)

- **A dedicated build strips animator controllers.** Every `Animator` parameter count reads 0 headless and Odin's
  controller is null, so `SetBool("dropped")` and every animation name P5 drives **must be read on a CLIENT** —
  a headless `cargo prefab` dump can never settle them. This is why the animator parameter names are on
  Wu'barrk's list and not provable from any server run (`docs/TODO.md` §2).

---

## INTEGRATED IN-GAME RUN, 2026-09-07 (all four branches merged, listen host, shadow client)

The first time every branch ran together. What was seen, in the log, in order:

```
Valkyrie's Cargo v0.1.0 loaded - renderer=True, patches=17, catalogue=72 entries
role: listen host (server + client)
director up: ... purse 1500 ... sidecar valkyriescargo_2484912131.dat (82 rows loaded, a saved visit waits for its event)
visit #2 RESUMED after a restart: pilot Wubarrk Dev, 03:16 left by the saved clock
cargo merchant #2: awake as approaching, ours, body=Ingvar
cargo merchant #2: trading (entered: gave up walking after 20 s) [callout]
cargo merchant #2: approaching (entered: the player left: 51.35 m for 5.02 s) [following]
cargo merchant #2: trading (entered: gave up walking after 20 s) [callout]
visit #1 ended: timer; takings 0 coins, purse 1500, 5 clock republish(es), 0 owed deliveries
visit #2: flight authored: start (...) at 154.5, descent (...) (50 m short), drop (...) at 34.5,
          straight in 90 m out; bird ...:8284, Dverger ...:8285, both owned by the pilot
```

**`body=Ingvar`** -- his own body on the merchant, not the stand-in. The `MerchantPlan` state machine runs and its
leash fires exactly as designed (12 m for 5 s; it saw 51.35 m for 5.02 s). A visit RESUMES across a restart off the
sidecar. A visit ENDS on its timer, republishing the clock five times. `patches=17` is P4's plus P5's three.

**It also found the bug that mattered most.** `MonsterAI.MakeTame()` opens with `m_character.SetTamed(true)`, and
`BaseAI.m_character` is assigned in `BaseAI.Awake` -- which has not run when our `Humanoid.Awake` postfix adds the
component. `Reassert` threw out of vanilla, and the catch in `Awake` turned that into `enabled = false`: **every
visit P5 ever authored left a bare Dverger with nothing driving it.** No off-game check could have caught it.

Still not seen end to end: the glide itself, the drop, the walk-up completing (he timed out at 20 s and called out
from where he stood, which is the designed fallback, not a success), the terminal on a real visit, a trade, and the
vanish. The two-client items cannot be run here at all.

## What to verify in-game

**An item is proven by its own pasted log line and a date, and by nothing else.** Done so far: **item 1**
(2026-09-06, headless, in Status); **items 2 and 6** (the first client run, 2026-09-07, below); **item 19**
(the same day, once the bundle existed); **item 20 in part** — he stands textured, upright, feet on the ground,
and `body=Ingvar` on a live merchant, while daylight and the walk and one-shot clips are still open;
**item 23's server half and item 24's boot half** (StormTest, 2026-09-07 10:40–10:42, the lines are in each item).
**Never run: items 3, 4, 5, 7 to 18, 21 and 22**, and item 23's other halves (a deal's row, a visit's row, the
switch, BarrkBOT reading the files). Wu'barrk's one live visit (INTEGRATED IN-GAME RUN, above)
produced the server-log half of item 10 (`visit #1 ended: timer; takings 0 coins`) and of item 15
(`visit #2 RESUMED after a restart`, the countdown continuing) — and is a pass for NEITHER, because neither
item's client half was seen (the banner and `cargo status` for 10; the sidecar's changed `stock` rows after
deals for 15, and no deal has ever been made).
**The runbook is `docs/PROOF-CLIENT.md`**: the order, the exact
command for each, the line the code writes, and `tools/deploy-test.ps1` / `tail-log.ps1` / `set-test-config.ps1`.
Deploy the **rc1 release DLL**: it is the only build that carries Ingvar on a machine without the bundle asset.
The client audit (`docs/CLIENT-AUDIT.md`, PR #11) fixed six defects on these paths before anyone ran them; its
section (c) lists what only a screen can settle.

1. ~~**Boot line, dedicated server**~~ **DONE 2026-09-06** (see Status): the DLL sits in CairnTest's
   `BepInEx\plugins\`; a headless boot shows the loaded line with `patches N/N applied, catalogue=72 entries` (N is the number of patch CLASSES since issue #31; the old `patches=10` counted methods),
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
    `not an admin` and the server log says `refused VCargo_admin visit from <name>`.
12. **The gates:** a client that is not rested is refused `forced: <name> not eligible: not rested; online: ...`.

P6, the wire, with a visit running (`cargo visit` first):
13. **A deal:** `cargo stock Iron` shows his shelf; `cargo deal buy Iron 2` prints `sending`, then
    `DONE w...-1-1: +2 Iron, -N coins`; the inventory changed by exactly that; the server log shows
    `deal w...-1-1 with <name>: sold 2 Iron at N, coins -2N to the player; purse ...`; `cargo stock Iron`
    shows stock 18 and a higher price on EVERY machine. `cargo deal sell Wood 10` the other way.
14. **The ledger:** `cargo status` on the server shows `owed ledger 0 row(s)` after the ack arrives; log out
    the instant after a deal's answer (before the ack), log back in: the server log shows
    `VCargo_claim from <name>: redelivered 1 owed deal(s)` and the client shows `delivery ... applied` or, if the
    inbox already had it, nothing twice.
15. **The sidecar after deals:** the server's file carries the changed `stock` rows, `purse`, `visit 1`,
    `seq N`, the `session` row while the visit runs, `cool` rows after it; a restart mid-visit prints
    `visit #1 RESUMED after a restart` and the countdown continues.
16. **Refusals:** `cargo deal buy BlackCore 3` answers `sold_out`; a buy with fewer coins than the price is
    stopped on the client before sending; a stale visit id is `stale_visit`.

P7, the terminal (a client, no server needed for the first item):
17. **`cargo terminal demo`** (from the main menu or in a world): the gilt window opens centred with the cursor
    free; 18 wares on the left with icons and prices, 72 rows on the right (the 54 wants and the 18 wares he buys back); clicking a ware stages it (Shift 5,
    Ctrl 20, right-click takes back); "you pay" is count x price; Confirm deal answers with one of his three buy
    lines and the price on that row moves; a second Confirm on the same line comes back "The wind shifted..."
    with the line amber, and Confirm new price goes through; Escape closes it and the cursor locks again; the log
    shows `terminal opened: visit #1 (demo)` and `terminal closed: escape`.
18. **On a real visit** (`cargo visit`, then `cargo terminal open`): the countdown matches the server's; a buy
    changes the inventory by exactly the deal and the server log shows the deal; the same row's price moved on
    every machine; a sell of goods you carry pays coins; Fill from my goods covers a ware with the dearest goods
    first; Send him off twice ends the visit (`ended: dismissed by <name>`); Tab and M close it; walking more than
    5 m from the merchant closes it (P5 is in, so this branch is live now).

**FIRST CLIENT RUN, 2026-09-07 03:00, Wu'barrk's Linux box** (a shadow copy of the client, BepInEx from the
shadow server, a throwaway world, listen host). In order: the boot line with `renderer=True, patches N/N applied` (item 2 --
the 14th is P4's `Patch_Valkyrie_Awake`), `event 'valkyries_cargo' registered (19 events now)`, `role: listen host
(server + client)` -- **the listen-host role had never been exercised** -- and `director up: ... day 1800 s
(EnvMan.m_dayLengthSec) ... roll every 1500 s at 25% ... sidecar valkyriescargo_2484912131.dat (fresh world)`.
Then `cargo status` answered with, among the rest: **`ZoneSystem.m_activeArea=2`** (item 6: the runtime value, NOT
the compiled 1, and it is what `FlightPlan` clamps against), `catalogue: 72 entries (18 wares, 54 wants), 0
problem(s)`, `30 events registered, ours=yes`, `config: this side is the source of truth, locked=True`,
`my report: rested=no, comfort=1, written 2 s ago` (ComfortReporter is live), and `day length 1800 s` read off a
CLIENT's EnvMan for the first time. Item 2 and item 6 are DONE.

**Item 19 passed but for the bake; item 20 found three real defects, and all three are now fixed AND SEEN
fixed.** `cargo body` reported `source embedded - loaded`, `bundle open, prefab 'ingvar' found`, `clips (6 of 6
wanted): Hello 3.75s, Idle 10.00s, Nod 1.25s, Shrug 1.96s, Talk 5.13s, Walk 4.17s` (exactly the bake's numbers)
and `bones=24 (24 expected)`. **The `StandaloneWindows64` bundle loads on a LINUX client** -- that question is
settled. `cargo body preview` then stood up a **pure white ellipsoid with Ingvar inside it**. Dumping the FBX from
the Editor named all three:

- **a stray `Icosphere`** -- a 2 m sphere, unparented, no material, 80 triangles, which is exactly the
  31192-against-31112 the runtime reported. The white blob. **Fixed at source**: deleted in a Blender re-export.
- **`mats=[Material_1/Standard/tex=NONE]`**: the albedo shipped in the bundle and nothing referenced it, so every
  surface drew white. **Fixed at source**: the image datablock is renamed `ingvar_albedo`, which is the name
  Unity's material import resolves against the PNG beside the model.
- **`char1` bounds `Extents(0.47, 0.24, 0.68)`**: the height is on **Z**. The BONES are converted so he stands
  upright, but `sharedMesh.bounds` is bind-pose data that axis conversion never touches, so every bounds-derived
  number lies -- the "0.244 m ground offset" was half his WIDTH. **Not fixable in the bake**: both
  `bakeAxisConversion` and a Blender re-export with `axis_up='Y'` were tried and change nothing. `BodyLoader`
  measures the POSED mesh (`BakeMesh`) instead and sets `updateWhenOffscreen`, because Unity culls by that box too.

Then two more that only a screen could find: a bundle baked in the Editor carries Unity's `Standard` and **Valheim
does not light it** (a lit head on a flat black body), and swapping only the shader is not enough because
`material.shader = x` keeps only the properties matching BY NAME. He now wears a COPY of the stand-in's own
material (`DvergerBody`, `Custom/Creature`) with our albedo in it -- which made him glow blue head to foot, because
the Dverger's glow is an emission colour gated by a mask, and clearing the donor's maps without the colour lights
everything the mask held back.

**Seen fixed on a screen, 2026-09-07**: the blob, the white, the black body and the blue glow are all gone and
Ingvar stands textured and upright with his feet on the ground. Not yet seen: how he reads in daylight -- the last
run was at midnight -- and the walk and one-shot clips. Item 20 stays open on those.


P8, the body (a client with the baked bundle embedded; **the bundle exists as of 2026-09-07** -- baked on
Wu'barrk's Linux box in Unity 6000.0.61f1, 3,826,415 bytes, and the Debug DLL grows 273,408 -> 4,100,096
when it is embedded. Note it is a `StandaloneWindows64` bundle, which is right for the ship and means a
LINUX client needs a Linux bake through `BodyLoader`'s loose-file path to run these two items):
19. **`cargo body`** says `source embedded ('ValkyriesCargo.valkyriescargo_kit')`, `bundle open`, `prefab 'ingvar'
    found`, six clips with the lengths Unity reported at the bake (`Walk 4.17s, Idle 10.00s, Talk 5.13s,
    Hello 3.75s, Shrug 1.96s, Nod 1.25s`; models/README.md's earlier row was one 24 fps frame longer on
    four of them, corrected 2026-09-07), `SkinnedMeshRenderer=yes, bones=24, tris=31112`, and a **bind-pose ground offset of about
    0.244 m, which is CORRECT for this asset and is not used to place him** — it is measured off a box whose
    axes are wrong, reported because a bake that comes out sideways shows up there first. The number that must be
    near 0 is `cargo body preview`'s `lifted N m ... measured on the posed mesh` (the console prints it as two
    lines: `body: source embedded - ...` then `resource: 'ValkyriesCargo.valkyriescargo_kit' inside this DLL ...`). On a dedicated server the
    same verb answers `source none - client only; not loaded here` and says nothing about appearance.
20. **`cargo body preview`** stands Ingvar 2.5 m in front of the player, facing them, feet ON the ground (not
    floating, not sunk; test OUTDOORS: the preview stands on `GetGroundHeight`, the terrain, so on a floor he sinks to
    the ground beneath it and that is the raycast mask, not the bake), about **1.37 m** tall — a head shorter than the player — idling, with the idle actually
    moving rather than frozen on frame 0. `cargo body walk` walks him on the spot and the crossfade takes about
    0.15 s in each direction; `cargo body clip Hello` waves and hands back to the idle near the end of the clip,
    and a second `cargo body clip Hello` while it plays is refused in the console; `Talk`, `Shrug` and `Nod` each
    play (the nod is deliberately subtle — if it does not read at 3.5 m, that is `make_nod()`, not this code);
    `cargo body clear` takes him away and `cargo status` goes back to `preview: none`. Then on a merchant (P5):
    the Dverger and his crossbow are gone, Ingvar walks when the agent walks, `cargo prefab` shows the same
    component set as before the swap, and nothing in the log says a vanilla system lost its animator.

P4, the flight (Wu'barrk's two-client proof; a visit on a server, the pilot's client watching the sky):
21. **The bird**: at `cargo visit` the server log shows `visit #N: flight authored: start (...) at ..., descent (...)
    at ... (N m short), drop (...) at ..., straight in 90 m out; bird <id>, Dverger <id>, both owned by the pilot`
    (that line HAS been seen, in the integrated run above; the rest of this item has not);
    the pilot's log shows `cargo flight #N: flying from ... via ... to
    ..., 76.5 m out at 8 m/s, turning 45 deg/s (radius 10.2 m)`; a Valkyrie appears about 90 m out and 120 m up,
    glides straight in over about 17 s, and `dropped at (...) after N s` prints near 12 m above the drop point; then
    it turns and leaves. A second client nearby sees the same glide, not a stutter (`s_velHash`).
22. **The edges**: `cargo status` on a client shows the `flight:` line with the runtime `m_activeArea`; the pilot
    walking out of their own zone block makes the bird vanish and, within 5 s, the server log says `the bird's ZDO is
    gone and the merchant was never dropped; the visit continues on the ground`; `cargo dismiss` mid-flight reclaims
    the bird (`spawner: ...` lines, no orphan in the world); a real intro Valkyrie (a new character) is untouched.

The BarrkBOT export (`BARRKBOT_CONTRACT.md`), on a dedicated server, `Server.BarrkBotExport` at its default on:
23. **The files land** — **DONE 2026-09-07 10:41 on StormTest, the server half** (dedicated, PR #46's build, nobody
    online). `director up` at 10:40:11 local; at 10:41:11, exactly `ExportCadenceSeconds` later, the folder
    `BepInEx\config\ValkyriesCargo\` (which had never existed on that machine) held `barrkbot_cargo_market.json`
    through `_5` (5,928 / 5,937 / 5,935 / 5,937 / 5,158 bytes; `part N/5`; 15+15+15+15+12 = 72 market rows),
    `barrkbot_cargo_traders.json` (1,277 bytes, an empty map) and `barrkbot_cargo_visits.json` (889 bytes), all
    seven parsing as JSON, every `generated_at` reading `2026-09-07T17:41:11.206Z`; at 10:42:11 every file's
    `generated_at` read `2026-09-07T17:42:11.225Z` — moving each cycle with nobody online. Still open: a deal's
    row, a visit's row, `Server.BarrkBotExport = false` stopping the files, and BarrkBOT reading them. The item as
    written: within `VisitDirector.ExportCadenceSeconds` (60 s) of `director up`, `BepInEx/config/ValkyriesCargo/`
    holds `barrkbot_cargo_market.json` through `_5` (the shipped catalogue's measured part count), `barrkbot_cargo_traders.json`
    and `barrkbot_cargo_visits.json`, each valid JSON with a `generated_at` that keeps moving every cycle even with
    nobody online. A deal (`cargo deal buy ...`) makes the buyer's row appear in `barrkbot_cargo_traders.json` on the
    NEXT export cycle, not immediately -- the export is time-driven, not deal-driven. A visit ending (`cargo dismiss`
    or the timer) adds a row to `barrkbot_cargo_visits.json` the same way. `Server.BarrkBotExport` set to false stops
    all three files from updating (existing ones are left as they were, not deleted). BarrkBOT itself, pointed at this
    server's `BepInEx/config`, answers a real question from the live files within its own 60 s sweep.

P10b, the engine probes (any boot, client or server, no visit needed):
24. **The probes resolve** — **DONE 2026-09-07 10:40 on StormTest, the boot half** (dedicated, stock 0.221.12, PR #46's
    build): `BepInEx\LogOutput.log` shows `built against Valheim 0.221.12 (network 36, player 43, world 37; Steam
    build 21981559 client / 21981590 server; bodies read 2026-09-06); running same build 0.221.12 (net 36, player 43,
    world 37); probes 18/18 ok, 7 not probeable.` and the loaded line `patches 18/18 applied, catalogue=72 entries,
    engine: same build 0.221.12 (net 36, player 43, world 37); probes 18/18 ok, 7 not probeable`, with no `FAILED`
    and no `registry:` line. `cargo engine`'s 25-line listing has not been read on a client, and the other direction
    (below) has not been run. The item as written: the boot line carries `built against Valheim 0.221.12 (network 36,
    player 43, world 37; ...); running same build 0.221.12 (net 36, player 43, world 37); probes 18/18 ok, 7 not
    probeable.` on an unmodified install, and `cargo engine` lists all 25 facts worst-rank-first with no `FAILED`
    among them and no `registry:` line. **This is the one thing about P10b a clean build cannot prove**: every probe is a reflection
    lookup against a member this mod has never resolved at runtime, so a typo or a wrong overload shows up as a
    FALSE failure that disables a working feature. Any `FAILED` line on a stock 0.221.12 is a bug in
    `EngineCheck.cs`, not in Valheim. Then the other direction, once: install on a machine whose Valheim has moved
    (or edit `EngineBaseline`'s constants and rebuild) and confirm the boot line says the version moved, the mod
    still loads, and nothing throws.

Ghost mode (F11; the owner's decision 2026-09-07), a visit running, any client:
25. **Nothing parks on him**: a Greydwarf pack or a raid near the visit walks past Ingvar and never targets him,
    no enemy health bar appears over him, and he never swings at anything; the player is fought exactly as
    before. `cargo status` lists `Patch_BaseAI_IsEnemy` among the applied patches. With no visit running,
    hostiles behave exactly as vanilla (the prefix is one int compare there).
26. **The shelf changes without a restart** (2026-09-07): on StormTest with no visit running, `cargo catalogue
    add Ruby:40:15:45:Ware` from an admin client answers `cargo: catalogue updated Ruby: base 40, target 15,
    max 45, Ware (was base 29, target 15, max 45, Ware); catalogue applied: 72 entries; 72 kept, 0 added,
    0 dropped; purse …, next visit #…`, the server log carries the same `catalogue applied:` line, `cargo stock
    Ruby` on a client shows the new price within a second, and `com.raveniron.valkyriescargo.cfg` on the
    server carries the edited line. With a visit running the answer is `catalogue change waits: visit #N is
    running; …`, `cargo status` shows `catalogue: a change waits`, and the change applies when the visit ends
    (the `catalogue applied:` line follows the visit's end line). `cargo catalogue add Nonsense:1:1:1:Ware` is
    refused with `this game has no prefab named 'Nonsense'`; `cargo catalogue add Boar:1:1:1:Ware` with `not an
    item`. `cargo catalogue list` on a client shows the edit; `cargo catalogue reset` puts the 72 back.

---

## Working agreement

- **Run `.\tools\run-tests.ps1` before every commit.**
- **Prove a new test fails without its fix.**
- **A clean build proves nothing about member access.** Anything reaching a game member needs one
  in-game run before it is called done.
- **Ask before changing anything in the locked-decisions table.**
