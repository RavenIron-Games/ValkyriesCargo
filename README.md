# Valkyrie's Cargo

A Valheim mod by [Raven Iron](https://github.com/RavenIron).

**A Valkyrie drops a wandering merchant beside your base when you are rested. He buys and sells for
five minutes at prices that move with what the world sells him, then vanishes like Odin.**

> **Status: `v0.1.0-rc1`, first playable.** PR #22 merged the flight, the merchant, the Cargo Terminal,
> Ingvar's own body and the BarrkBOT export; the tag is cut, the store zip is built, and the upload is
> deliberately held back (see Status). The plugin boots on a client and a dedicated server, the
> director runs, the market persists, and Ingvar's own body loads out of the bundle. A full visit is
> proven off-game across 1301 checks and a nine-scenario economy simulation, and is still being proven
> in-game: one live visit has landed Ingvar in his own body, but no run has yet watched the walk-up
> finish, the terminal open, a trade settle or the vanish. `docs/PROOF-CLIENT.md` is the runbook and
> CLAUDE.md lists what remains. This file is the developer's README; the store page is
> `HexiumDist/README.md`.

---

## What it will do

**The visit.** When you are rested, your comfort is 4 or more and you stand where the game counts a
base, the server may send Ingvar the Far-Travelled your way: a Valkyrie will fly in with him in her
talons, drop him beside your hearth, and he will walk up and call out. Everyone nearby will see all
of it. Never on command: the visit is a roll every 25 minutes at 25%, and a summon horn is a later,
earned feature. The roll, the eligibility gates, the event, the flight and the merchant are all built
now, and have flown and landed together once, live: Ingvar arrived in his own body, stopped short of
your hearth and called out from where he stood (see Status).

**The trade.** Press E on him for the Cargo Terminal: his wares on one side, the goods he wants on
the other, a staging tray in between. He carries a live stock of 72 catalogue entries — 18 he sells
and buys back, 54 he only buys — that persists per world. Buy him out and the price climbs; flood
him and he pays less; a game day later it has drifted back. He pays coins, or takes your goods in
barter at his live buy price, and he arrives with a purse of 1500 coins, so nobody can dump a
warehouse on him. If a price moves while you are staging, the line turns amber and you confirm once
more; nothing leaves your inventory until the server has answered. The market, the wire and the
window are all built; the window has never been drawn on a screen.

**The departure.** Five minutes, or Shift+E twice to send him off. He speaks a farewell and vanishes
in Odin's own effect. Built (`Client/CargoMerchant.cs`); not yet watched on a screen.

## What it will not do

No horn item in 0.1. No patch on the vanilla trader or store — the terminal is our own window, opened
from our own interact handler. Nothing happens on command except an admin's `cargo visit`.

---

## Status

Truth pass against `main` at commit `8453b65` (PR #22 merged, `v0.1.0-rc1` tagged), 2026-09-07. The
log lines below are the ones recorded in `CLAUDE.md` "Status" by whoever saw them.

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

Off the game entirely: **1301 checks** in `tests\CoreTests`, which compiles the shipping sources
themselves — the whole of `Core\` (the market, the scheduler, the flight plan, the merchant's state
machine, the body's blend model and the BarrkBOT export among them), plus `Net\CargoRpc.cs` and the
terminal's tray model — against stubs, never a copy. Mutation-proven throughout: every fix in this
history has a test recorded to fail without it.

### Built, and mostly still unseen

Most of this compiles and has never been run with a renderer attached. A few items below have now
been run once, live, on a listen host; one run proves far less than a repeatable one, and none of
these is repeatable yet. A clean build proves nothing about a game member either way.

- **The Cargo Terminal** — the IMGUI window itself: the two panes, the icons, the staging tray, the
  amber price line, Confirm, Fill from my goods, Send him off. `cargo terminal demo` draws it on an
  in-process market with no server and no merchant; nobody has run that command yet, and the one live
  visit below ended before anyone pressed E on Ingvar to open it the real way.
- **The client boot line** (`renderer=True`) and `cargo status` answering on a client have both been
  seen once, on a listen host, 2026-09-07. Still not seen: the ServerSync version wall, the config
  lock on a client that is not also the host, and the `cargo prefab` dumps.
- **The comfort report**: `Client\ComfortReporter.cs` writing `VCargo_rested` / `VCargo_comfort` has
  been seen once in a client's own `cargo status`. Not yet confirmed: those same numbers appearing
  side by side in the server's candidate list.
- **A forced visit**: `cargo visit`, the pilot's private line, the centre banner, the countdown, the
  clock pausing when everyone walks out of range, the timer ending the visit, `cargo dismiss`, and a
  non-admin being refused.
- **A deal over the wire**: `cargo deal buy Iron 2` moving an inventory and a price on every machine,
  a redelivery after a disconnect, a visit resumed after a mid-visit restart, and each refusal
  reason.
- **The body loader**: `cargo body` and `cargo body preview` have both been run and watched. Five
  real bake defects were found this way and are fixed at the source (a stray sphere, an unlinked
  albedo, a bind-pose bounding box that lied about the up-axis, Valheim's refusal to light Unity's
  `Standard` shader, and a donor material's emission glow left behind) — the full account is
  `docs/knowledge-base/SKINNED-CHARACTER-BUNDLE-FACTS.md`. Ingvar now stands textured, upright and
  correctly lit in preview. Not yet seen: how he reads in daylight (the only preview run was at
  night) and the walk and one-shot clips playing.
- **The flight** (`Server/Spawner.cs`, `Client/CargoFlight.cs`, `Patches/Patch_Valkyrie_Awake.cs`,
  Wu'barrk's P4): the server authors the Valkyrie for the pilot's client to fly, straight in from
  about 90 m out and 120 m up over about 17 s, and now carries the merchant (P5). Flown once, live —
  Ingvar landed in his own body — and not yet watched from two screens at once.

The full numbered list is `CLAUDE.md`, "What to verify in-game", items 2 to 23 (P4's flight and the
BarrkBOT export each added their own beyond the original 20). A client has since booted and run one
visit; `docs/PROOF-CLIENT.md` is the runbook for the rest of the list, and each proof gets pasted back
into `CLAUDE.md` as it happens.

So today a visit puts Ingvar himself in the yard, flown in and landed in his own body — proven once,
live, on a listen host. He stopped short of the walk-up and called out instead, and nobody has yet
watched one visit's whole loop, glide to vanish, in a single sitting. That is why the tag is
`v0.1.0-rc1` and not yet a store upload: `docs/RELEASE.md` step 5 holds the upload back until that
loop is seen.

---

## Installing

**Server:** required. **Every client:** required, on exactly the same version. The mod refuses a
client that does not have it, or runs any other build, at the handshake, with ServerSync's message
naming the mod and both versions. It has to: the visuals, the terminal and the trade all live in
client code.

On a Gale-managed client the plugin folder is
`%APPDATA%\com.kesomannen.gale\valheim\profiles\<profile>\BepInEx\plugins\`, not the Steam folder.

Requires [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
and nothing else: no Jotunn, no JSON library (the BarrkBOT export writes its files through the mod's
own `Core/Json.cs`). `manifest.json`'s dependency list is that one entry.

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
| `ApproachDistance` | `3.5` | 1-10 | Metres from the player at which he stops walking up. Read on the **client that owns the merchant**, synced from the server. |
| `BodyPrefab` | `Dverger` | | The engine creature prefab the merchant is **cloned from, for good** — `Character`, `MonsterAI` and the collider all come from it, whatever body is drawn on top. Must have a `Humanoid`, a `MonsterAI` and an `Animator`. This is not the custom-body switch; that is `CustomBody`. |
| `CustomBody` | `true` | | Put Ingvar's own body on the `BodyPrefab` clone from the AssetBundle embedded in this DLL. `false` keeps the Dverger stand-in visible, and so does a build with no bundle embedded (`cargo body` says which). This is the switch, not `BodyPrefab`. Read on the **client**, synced from the server; a dedicated server never reads it. |
| `FlightStartDistance` | `90` | 30-200 | Metres from the pilot where the Valkyrie appears; shrunk at runtime, 12 m at a time, until the start fits inside the pilot's active zone block. The floor is `FlightPlan.MinimumStartDistance` (30 m) — below that the bird would appear on top of the player. |
| `FlightStartAltitude` | `120` | 30-400 | Altitude of the Valkyrie's start point, metres above the drop. |
| `FlightDescentDistance` | `50` | 10-200 | Metres out at which the descent leg begins. |
| `FlightSpeed` | `8` | 2-40 | Metres a second the Valkyrie flies, overriding the prefab's own speed. 8 gives design 3.2's 15-20 s of sky over a 90 m approach. Read on the **client that owns the bird**, synced from the server; the server never reads it. |
| `FlightTurnRate` | `45` | 5-360 | Degrees a second the Valkyrie may turn, overriding the prefab's own. Read on the **client that owns the bird**, synced from the server. |
| `Catalogue` | 72 entries | | What Ingvar sells and buys: `Prefab:BasePrice:TargetStock:MaxStock:Kind` entries separated by commas; `Kind` is `Ware` (sells and buys back) or `Want` (buys only). The default is 18 wares and 54 wants; every number's reason is in `docs/CATALOGUE.md`. A prefab the game has no item for is dropped with one log line and the rest still loads. Editable on a running server with `cargo catalogue add|remove|reset` (admin) or Configuration Manager as an admin; a change applies as soon as no visit is running (`docs/CATALOGUE.md` §4). |
| `PriceElasticity` | `0.35` | 0.05-1.5 | Exponent of (target / stock) in the price; higher is steeper. |
| `MinPriceMultiplier` | `0.4` | 0.05-1 | Floor on the price multiplier when he is flooded. |
| `MaxPriceMultiplier` | `3` | 1-10 | Ceiling on the price multiplier when he is out. |
| `SpreadBuy` | `0.7` | 0.1-1 | What he pays as a fraction of what he charges for the same item. |
| `FairMarketAct` | `true` | | Caps what he pays to buy back a Ware at par (base × `SpreadBuy`), so a high `MaxPriceMultiplier` can never turn buying him out and selling straight back into free coins. |
| `WareHalfLifeGameDays` | `0` | 0-365 | Between visits a **Ware**'s stock drifts back toward its target with this half-life, in game days (30 real minutes of server uptime each). `0` is never: what he has to sell is what players sold him and what an admin's target says (`cargo catalogue add`). The owner's call, 2026-09-07; the sweep is `docs/ECONOMY-SIM.md` §10. |
| `WantHalfLifeGameDays` | `3` | 0-365 | The same for a **Want**: he passes on what he was sold, so a flooded row half-clears in three game days and he never fills up for good. At `0` every Want fills to its max and he stops buying it. |
| `PurseCoins` | `1500` | 0-100000 | Coins he arrives with. |
| `PurseCarryPercent` | `50` | 0-100 | Percent of last visit's takings added to the next purse, capped at three purses. |
| `EnableBarter` | `true` | | Allow paying with goods he wants, valued at his live buy price. `false` hides the terminal's Barter button; the server settles a barter deal either way. Read on the **client**, synced from the server. |
| `BarrkBotExport` | `true` | | Write `barrkbot_cargo_market.json`, `barrkbot_cargo_traders.json` and `barrkbot_cargo_visits.json` under `BepInEx/config/ValkyriesCargo/` for BarrkBOT to read, refreshed about once a minute; the world sidecar is still the source of truth. |

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
| `cargo terminal open` | Open it on the running visit as a console shortcut; it does not attach the merchant object even though the merchant now exists — pressing E on Ingvar himself is the real path. `cargo terminal close` closes it. |
| `cargo visit [player]` | **Admin.** Force a visit for yourself, or for the named player. Cooldowns are ignored; every other gate is kept. |
| `cargo dismiss` | **Admin.** End the running visit now. |
| `cargo reset` | **Admin.** Forget every cooldown. |
| `cargo save` | **Admin.** Write the world sidecar now. |
| `cargo catalogue list` | What he sells and buys, as this machine last heard it: `Prefab base target/max`, wares then wants. |
| `cargo catalogue add <Prefab:Base:Target:Max:Kind>` | **Admin.** Add an item, or change one already there (same prefab, in place). The server checks the prefab exists and is an item before anything changes. The edit lands in the cfg file, reaches every client, and applies as soon as no visit is running. |
| `cargo catalogue remove <Prefab>` | **Admin.** Take an item off the shelf. Same path. |
| `cargo catalogue reset` | **Admin.** Back to the shipped 72 entries. Same path. |
| `cargo body` | The body loader's state: where the bundle came from (embedded, a file beside the DLL, or none), the prefab, the six clips and their lengths, the mesh and the ground offset. Answers on a dedicated server too. |
| `cargo body preview` | Stand Ingvar 2.5 m in front of you, facing you, on the ground, with no merchant and no server: the way to see the body. `cargo body walk` toggles his walk on the spot, `cargo body clip <Hello\|Talk\|Shrug\|Nod>` plays a gesture, `cargo body clear` takes him away. Needs a baked bundle. |

The admin verbs run in place on a server or a listen host. From a client they ride the
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
- `docs/PROOF-CLIENT.md` — the in-game verification runbook: the order, the exact command for each
  item, and the log line the code writes.
- `CLAUDE.md` — the engineering notes: engine facts read from the decompiled assembly, house style,
  the status lines quoted above, and the in-game verification list.
- `PLAN.md` — the original plan; `docs/DESIGN.md` is authoritative where the two differ.

## Credits

Designed with **Thorium Wu'barrk**, who also built Ingvar's model (`models/ingvar.fbx`) and wrote the
terminal's look: `Libs/SharedUI/GiltFrameTheme.cs` and `Libs/SharedUI/UIFocus.cs` are his VikingOS
0.9.8 shared source, MIT, vendored here and never edited.

**ServerSync** is blaxxun's `ConfigSync.cs`, MIT-0, compiled in as shared source.

Item data checked against Wu'barrk's TheEye dump of 2026-07-31.
