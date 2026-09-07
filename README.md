# Valkyrie's Cargo

A Valheim mod by [Raven Iron](https://github.com/RavenIron).

**A Valkyrie drops a wandering merchant beside your base when you are rested. He buys and sells for
five minutes at prices that move with what the world sells him, then vanishes like Odin.**

> **Status: 0.1.0, first playable.** The plugin boots on a client and a dedicated server, the
> director runs, the market persists, and Ingvar's own body loads out of the bundle. A full visit is
> proven off-game across 1301 checks and a nine-scenario economy simulation, and is still being proven
> in-game -- `docs/PROOF-CLIENT.md` is the runbook and CLAUDE.md lists what remains. This file is the
> developer's README; the store page is `HexiumDist/README.md`.

---

## What it will do

**The visit.** When you are rested, your comfort is 4 or more and you stand where the game counts a
base, the server may send Ingvar the Far-Travelled your way: a Valkyrie will fly in with him in her
talons, drop him beside your hearth, and he will walk up and call out. Everyone nearby will see all
of it. Never on command: the visit is a roll every 25 minutes at 25%, and a summon horn is a later,
earned feature. The roll, the eligibility gates and the five-minute event are built and run today;
the bird and the man are not (see Status).

**The trade.** Press E on him for the Cargo Terminal: his wares on one side, the goods he wants on
the other, a staging tray in between. He carries a live stock of 72 catalogue entries — 18 he sells
and buys back, 54 he only buys — that persists per world. Buy him out and the price climbs; flood
him and he pays less; a game day later it has drifted back. He pays coins, or takes your goods in
barter at his live buy price, and he arrives with a purse of 800 coins, so nobody can dump a
warehouse on him. If a price moves while you are staging, the line turns amber and you confirm once
more; nothing leaves your inventory until the server has answered. The market, the wire and the
window are all built; the window has never been drawn on a screen.

**The departure.** Five minutes, or Shift+E twice to send him off. He speaks a farewell and vanishes
in Odin's own effect. Not built.

## What it will not do

No horn item in 0.1. No custom body yet: the loader is built, but until the bundle is baked and embedded a Dverger stands in for Ingvar. No
patch on the vanilla trader or store — the terminal is our own window, opened from our own interact
handler. Nothing happens on command except an admin's `cargo visit`.

---

## Status

Truth pass against `main` at commit `7ac0dc0`, 2026-09-06. The log lines below are the ones recorded
in `CLAUDE.md` "Status" by whoever saw them.

### Built and proven headless, on a dedicated server

The plugin, the config surface, the market, the scheduler, the event, the deal wire and the world
save. Proven by running the DLL on a dedicated server and reading `BepInEx\LogOutput.log`. No
client has ever been part of any of these runs.

**Boot and ServerSync.** CairnTest (dedicated, port 2466), 2026-09-06 15:23, alongside two sibling
mods: within 20 s of launch the log showed, in order, `Loading [Valkyrie's Cargo 0.1.0]`, then

```
Valkyrie's Cargo v0.1.0 loaded - renderer=False, patches=10, catalogue=72 entries, ServerSync version gate armed; role is decided when a world loads.
```

then `Registered 'com.raveniron.valkyriescargo ConfigSync' RPC - waiting for incoming connections`,
then `Load world: CairnTest`, `role: dedicated server`, `Game server connected`.

**The event and the director.** StormTest (dedicated, port 2476, a 117-plugin modpack clone),
2026-09-06 18:55, in order:

```
Valkyrie's Cargo v0.1.0 loaded - renderer=False, patches=13, catalogue=72 entries, ServerSync version gate armed; role is decided when a world loads.
event 'valkyries_cargo' registered (20 events now); duration 300 s, pauses with nobody within 96 m, no spawns, no music, no weather.
role: dedicated server
routed RPCs registered for this session: VCargo_admin, VCargo_reply
director up: salt w4790ce, day 1800 s (EnvMan.m_dayLengthSec), catalogue 72 entries, purse 800, roll every 60 s at 25%, first roll one interval from now; market state is NOT persisted yet (P6)
roll: held: a random event is active (a raid, a storm, or a visit)
```

The day length the price drift counts is read from the live `EnvMan` and was 1800 s, not the
compiled default of 1200. The hold was against a real foreign event — another mod had a storm
running. No exception from this mod in that log.

**Persistence, with a restart.** StormTest again, 2026-09-06 19:25, with the plugin folder cleared
to this DLL alone. First boot printed `director up: ... next visit #1, ...; sidecar
valkyriescargo_4690126.dat (fresh world)` and the file appeared in `saves\worlds_local` at once: 78
lines, `format 1`, 72 `stock` rows, `purse 800`, `purseStart 0`, `visit 0`, `seq 0`. On restart:
`director up: ... sidecar valkyriescargo_4690126.dat (76 rows loaded)`, and the first file rotated
to `.bak`. The same boot also printed `roll: no eligible player: nobody online` — the empty-server
path, live.

Off the game entirely: **1034 checks** in `tests\CoreTests`, which compiles the shipping sources
themselves — the whole of `Core\`, plus `Net\CargoRpc.cs`, the terminal's tray model and the body's blend model — against
stubs, never a copy. Mutation-proven (28 mutations on the market core, seven more on the terminal's tray
model, five on the body's blend model; each fails without its fix).

### Built, and never seen on a screen

Every one of these compiles, and none of it has been run with a renderer attached. A clean build
proves nothing about a game member.

- **The Cargo Terminal** — the IMGUI window itself: the two panes, the icons, the staging tray, the
  amber price line, Confirm, Fill from my goods, Send him off. `cargo terminal demo` draws it on an
  in-process market with no server and no merchant; nobody has run that command yet.
- **The client boot line** (`renderer=True`), the ServerSync version wall, the config lock on a
  connected client, and the `cargo prefab` dumps.
- **The comfort report**: `Client\ComfortReporter.cs` writing `VCargo_rested` / `VCargo_comfort` on the local
  player's own ZDO, and those numbers appearing in the server's candidate list.
- **A forced visit**: `cargo visit`, the pilot's private line, the centre banner, the countdown, the
  clock pausing when everyone walks out of range, the timer ending the visit, `cargo dismiss`, and a
  non-admin being refused.
- **A deal over the wire**: `cargo deal buy Iron 2` moving an inventory and a price on every machine,
  a redelivery after a disconnect, a visit resumed after a mid-visit restart, and each refusal
  reason.
- **The body loader**: `cargo body` reporting the embedded bundle, and `cargo body preview` standing
  Ingvar in front of the player. Needs a baked bundle, and none exists yet.
- **The flight** (`Server/Spawner.cs`, `Client/CargoFlight.cs`, `Patches/Patch_Valkyrie_Awake.cs`, Wu'barrk's
  P4): the server authors the Valkyrie for the pilot's client to fly, straight in from about 90 m out and 120 m
  up over about 17 s. Simulated by both owners; never flown. Until the merchant exists it carries nothing.

The full numbered list is `CLAUDE.md`, "What to verify in-game", items 2 to 20. Whoever boots a
client first works that list and pastes the exact lines back into `CLAUDE.md`.

### Not built

- **The merchant** (`Client/CargoMerchant.cs` and its patches): the carry in the talons, the landing,
  the walk, the callout, immortality, Shift+E dismissal, the Odin vanish, the restart sweep.
- **The custom body**: the source art is in `models\` and the loader (`Client/BodyLoader.cs`, with
  `cargo body preview` as its proof) is in this build; the baked asset bundle is not, so the Dverger
  stands in.

So today a visit starts an event and a market with nobody standing in your yard. That is why the
status line says not yet playable.

---

## Installing

**Server:** required. **Every client:** required, on exactly the same version. The mod refuses a
client that does not have it, or runs any other build, at the handshake, with ServerSync's message
naming the mod and both versions. It has to: the visuals, the terminal and the trade all live in
client code.

On a Gale-managed client the plugin folder is
`%APPDATA%\com.kesomannen.gale\valheim\profiles\<profile>\BepInEx\plugins\`, not the Steam folder.

Requires [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
and nothing else. No Jotunn.

Built against the assemblies of the Valheim install of 2026-09-06 (0.221.x); that install's
`UnityPlayer.dll` reports **Unity 6000.0.61f1**, which is the Editor version any asset bundle for
this mod must be built with.

---

## Configuration

`BepInEx\config\com.raveniron.valkyriescargo.cfg`.

**Every `Server.*` value is synced from the server and locked**: on a connected client the local
value is shown read-only, and a client's write is rejected unless that client is on the server's
`adminlist.txt`. `Client.*` values are local and never leave the machine. `LockConfiguration`
itself is what arms the lock.

### `[Server]`

| Key | Default | Range | Meaning |
|---|---|---|---|
| `LockConfiguration` | `true` | | Server enforces every `Server.*` value on every client. Admins on `adminlist.txt` may still change them. |
| `Enabled` | `true` | | Roll visits at all. |
| `RequireRested` | `true` | | A player must carry the Rested effect to be eligible. |
| `MinComfortLevel` | `4` | 0-20 | Minimum comfort level (the number in the Rested tooltip) for eligibility. A bed, a fire and a roof give 3; 4 needs a chair or a banner. |
| `MinBaseValue` | `1` | 0-10 | Vanilla base value at the player (workbench/forge coverage); vanilla raids use 3. |
| `DaytimeOnly` | `true` | | Only roll visits by day; the flight is the show. |
| `EventCheckIntervalMinutes` | `25` | 1-240 | Real minutes between rolls. |
| `EventChancePercent` | `25` | 0-100 | Chance per roll when at least one player is eligible. |
| `PlayerCooldownMinutes` | `60` | 0-1440 | Real minutes before the same player can be chosen again; stamped at dispatch. |
| `CooldownRadius` | `60` | 0-500 | Metres: a base on cooldown blocks its neighbours within this radius. |
| `MerchantLifespanSeconds` | `300` | 30-1800 | How long Ingvar stays, as the vanilla random event's duration. |
| `ApproachDistance` | `3.5` | 1-10 | Metres from the pilot at which he stops walking. |
| `BodyPrefab` | `Dverger` | | The creature prefab that plays Ingvar until the custom body exists. Must have a `Humanoid`, a `MonsterAI` and an `Animator`. |
| `CustomBody` | `true` | | Put Ingvar's own body on the `BodyPrefab` clone from the AssetBundle embedded in this DLL. `false` keeps the Dverger stand-in visible, and so does a build with no bundle embedded (`cargo body` says which). This is the switch, not `BodyPrefab`. |
| `FlightStartDistance` | `90` | 24-200 | Metres from the pilot where the Valkyrie appears; clamped at runtime into the pilot's active zone block. |
| `FlightStartAltitude` | `120` | 30-500 | Altitude of the Valkyrie's start point, metres above the drop. |
| `FlightDescentDistance` | `50` | 10-200 | Metres out at which the descent leg begins. |
| `FlightSpeed` | `8` | 2-40 | Metres a second the Valkyrie flies, overriding the prefab's own speed. 8 gives design 3.2's 15-20 s of sky over a 90 m approach. |
| `FlightTurnRate` | `45` | 5-360 | Degrees a second the Valkyrie may turn, overriding the prefab's own. |
| `Catalogue` | 72 entries | | What Ingvar sells and buys: `Prefab:BasePrice:TargetStock:MaxStock:Kind` entries separated by commas; `Kind` is `Ware` (sells and buys back) or `Want` (buys only). The default is 18 wares and 54 wants; every number's reason is in `docs/CATALOGUE.md`. An unknown prefab name is dropped with one log line and the rest still loads. |
| `PriceElasticity` | `0.35` | 0.05-1.5 | Exponent of (target / stock) in the price; higher is steeper. |
| `MinPriceMultiplier` | `0.4` | 0.05-1 | Floor on the price multiplier when he is flooded. |
| `MaxPriceMultiplier` | `3` | 1-10 | Ceiling on the price multiplier when he is out. |
| `SpreadBuy` | `0.7` | 0.1-1 | What he pays as a fraction of what he charges for the same item. |
| `StockHalfLifeGameDays` | `1` | 0.1-30 | Between visits his stock drifts back toward target with this half-life, in world days. |
| `PurseCoins` | `800` | 0-100000 | Coins he arrives with. |
| `PurseCarryPercent` | `50` | 0-100 | Percent of last visit's takings added to the next purse, capped at three purses. |
| `EnableBarter` | `true` | | Allow paying with goods he wants, valued at his live buy price. |
| `PriceChangePolicy` | `Reconfirm` | `Reconfirm` \| `Teardown` | `Reconfirm`: a staged deal whose price moved turns amber and needs one more click. `Teardown`: every open tray is cleared on any price change. |

### `[Client]`

| Key | Default | Range | Meaning |
|---|---|---|---|
| `ShowArrivalMessage` | `true` | | Show the private "wings beat in the upper skies" line when you are the chosen player. |
| `ShowPriceTrend` | `true` | | Show the up/down glyph against base price in the terminal. |
| `Theme` | `Vanilla` | `Vanilla` \| `BlackGold` | Terminal metal colour. |
| `TerminalScale` | `1.0` | 0.5-2 | Terminal size multiplier. |

---

## Console

Prefix `cargo`. Console commands are not config: `LockConfiguration` does not touch them.

| Command | What it does |
|---|---|
| `cargo status` | Role, which side owns the config, the catalogue, the state channels, the transport, and — where the world runs — the director, the visit, the wire, the sidecar and every candidate. The instrument to reach for first. |
| `cargo version` | This build, and that every client must run exactly it. |
| `cargo prefab <name>` | A game prefab's components, children, effect lists (with the networked/local branch each entry takes) and animator parameters. Try `Valkyrie`, `Dverger`, `odin`, `Haldor`. |
| `cargo stock [prefab]` | His shelf as this machine last heard it: stock against target, what you pay, what he pays, the trend. Names one row, or the first 24. |
| `cargo deal buy\|sell <prefab> [count]` | A deal without the terminal: builds the same `Deal`, sends it over the same wire, applies the same answer. The reference path. |
| `cargo claim` | Ask the server for any delivery it still owes you. |
| `cargo terminal demo` | Open the Cargo Terminal on the in-process demo market: **no server, no world and no merchant needed**. This is the way to see the window. |
| `cargo terminal open` | Open it on a running visit, with no merchant to stand beside (until the merchant exists). `cargo terminal close` closes it. |
| `cargo visit [player]` | **Admin.** Force a visit for yourself, or for the named player. Cooldowns are ignored; every other gate is kept. |
| `cargo dismiss` | **Admin.** End the running visit now. |
| `cargo reset` | **Admin.** Forget every cooldown. |
| `cargo save` | **Admin.** Write the world sidecar now. |
| `cargo body` | The body loader's state: where the bundle came from (embedded, a file beside the DLL, or none), the prefab, the six clips and their lengths, the mesh and the ground offset. Answers on a dedicated server too. |
| `cargo body preview` | Stand Ingvar 2.5 m in front of you, facing you, on the ground, with no merchant and no server: the way to see the body. `cargo body walk` toggles his walk on the spot, `cargo body clip <Hello\|Talk\|Shrug\|Nod>` plays a gesture, `cargo body clear` takes him away. Needs a baked bundle. |

The four admin verbs run in place on a server or a listen host. From a client they ride the
`VCargo_admin` routed RPC to the server, where vanilla's own `ZNet.IsAdmin` decides — fail closed — and
the answer comes back on `VCargo_reply` and prints in the caller's console.

---

## Design and documents

- `docs/DESIGN.md` — the design of record: authority, systems, the wire, the economy, decisions,
  milestones, risks.
- `docs/TLDR.md` — one screen.
- `docs/CATALOGUE.md` — the 72 default entries with the reason for every number, checked against
  `docs/data/items-valheim-2026-07-31.tsv`.
- `docs/WORKSPLIT.md` — who owns what, and the frozen contract between the two tracks.
- `docs/RELEASE.md` — how a release is cut.
- `CLAUDE.md` — the engineering notes: engine facts read from the decompiled assembly, house style,
  the status lines quoted above, and the in-game verification list.
- `PLAN.md` — the original plan; `docs/DESIGN.md` is authoritative where the two differ.

## Credits

Designed with **Thorium Wu'barrk**, who also built Ingvar's model (`models/ingvar.fbx`) and wrote the
terminal's look: `Libs/SharedUI/GiltFrameTheme.cs` and `Libs/SharedUI/UIFocus.cs` are his VikingOS
0.9.8 shared source, MIT, vendored here and never edited.

**ServerSync** is blaxxun's `ConfigSync.cs`, MIT-0, compiled in as shared source.

Item data checked against Wu'barrk's TheEye dump of 2026-07-31.
