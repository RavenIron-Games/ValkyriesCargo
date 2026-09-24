# Valkyrie's Cargo

A Valheim mod by [Raven Iron](https://github.com/RavenIron).
**Raven Iron is NomadicWar & Wu'barrk** - both founders, both designers of this mod.

**A Valkyrie drops a wandering merchant beside your base when you are rested. He buys and sells for
five minutes at prices that move with what the world sells him, then vanishes like Odin.**

> **Status: `v0.1.5`, a first playable; a pre-release on GitHub; the store upload is a separate step.** Cut
> 2026-09-24 from `main`. **For Valheim 1.0.12 and 1.0.15** (both network version 40; built against 1.0.12,
> tested on 1.0.15): 1.0.12 moved the network version to 40, so `v0.1.0-rc4` (1.0.7) and `v0.1.0-rc3` (0.221.12)
> cannot connect to it at all. The store carries whichever
> cut was last uploaded (`v0.1.0-rc5`, 2026-09-11, was the first; 0.1.4 since 2026-09-16), and
> **a 0.1.4 client is refused by a 0.1.5 server** by the version gate — on purpose: the store takes
> one upload per number, and both sides move together. **Updating a server: nothing to delete** —
> from 0.1.4 the mod migrates its own config file, and 0.1.5 does not change its layout (Installing, below).
> The store zip and the body bundle are attached to the GitHub release, which stays flagged a pre-release
> because 0.1.x is a first playable and says so.
> What 0.1.5 adds: a deal lands in your pack whole or not at all; a deal made more than 96 m from the visit is
> refused; the Fair Market Act covers every row while the shelf rotates, and within a visit he never pays more for
> an item than the lowest price he sold it at (two rule changes, set out in `docs/DECISIONS-WUBARRK.md` §2, where
> they are still marked PROPOSED for Wu'barrk's confirmation); a `ShelfSize`
> change waits for the visit to end; a visit resumed during the flight moves on to the ground phase; and the DLL no
> longer carries the build machine's folder path. Checked in game on 2026-09-24 (Storm10, Valheim 1.0.15,
> dedicated, visits #23 to #30, on the same code as this release): each of the six gameplay fixes behaved as
> described (`docs/proofs/2026-09-24-storm10-session.md`); not tried in game: the distance check on a listen
> host, a trade between two players, and a sale at the capped price after a shelf roll (the price was read, not
> sold at).
> What 0.1.4 added: the mod migrates its own config file (a value still at an old default moves, a value you set
> stays, a backup lands beside the file, the boot line says what it did) and the catalogue becomes the shipped rows
> plus your `CatalogueOverrides`, so a stored line never hides a new default again.
> What 0.1.3 added: the visit clock runs whoever is near him, so a visit ends on time with nobody online
> (before, it never ended without a player within 96 m of him, and held every raid with it); the new server
> option `PauseVisitWhenEmpty` holds it on an empty server instead (off by default).
> What 0.1.2 added: the merchant guard — Ingvar trades and hovers as himself on a server whose taming mod made
> the Dvergr a pet (DvergrAllies; found on Wonderland, proven on Storm10 with the mod on both sides).
> What 0.1.1 added: Ingvar insists on ground somebody built and refuses a drop at another merchant's camp, a
> dungeon door or one of the game's landmarks (issue #79; three switches, proven live); food and drink in
> the catalogue, 72 → 101 rows (an existing server takes them with `cargo catalogue reset`); one row per
> item token and deals keyed by prefab; every console line in the client's log; the BarrkBOT export per
> contract v4.
> Twenty-seven visits across five sessions stood behind rc5; the 2026-09-15 session on Storm10 added
> visits #10 to #13 — three refusals named by place, a flight to a base on a ruin set down beside the
> house (a second was authored and dismissed before the drop), and seven deals on the new catalogue settled
> line for line. Proven off-game across 2589 checks at 0.1.5 and an eleven-scenario economy
> simulation. **Never seen in a game: redelivery** — a player paying and the goods arriving after a lost
> connection — which is proven off-game only; nor the two-client items.
> `docs/PROOF-CLIENT.md` is the runbook and CLAUDE.md lists what remains. This file is the developer's
> README; the store page is `HexiumDist/README.md`. **`v0.1.0-rc1` carries a blocker; do not install it.**

---

## What it will do

**The visit.** When you are rested, your comfort is 4 or more and you stand where the game counts a
base, the server may send Ingvar the Far-Travelled your way: a Valkyrie will fly in with him in her
talons, drop him beside your hearth, and he will walk up and call out. Everyone nearby will see all
of it. Never on command: the visit is a roll every 25 minutes at 25%, and a summon horn is a later,
earned feature. The roll, the eligibility gates, the event, the flight and the merchant are all built
now, and have flown nine live visits on 2026-09-07: Ingvar arrives in his own body and walks up to
you, though never yet on his first attempt (see Status).

**The trade.** Press E on him for the Cargo Terminal: his wares on one side, the goods he wants on
the other, a staging tray in between. He carries a live stock of 101 catalogue entries — 27 he sells
and buys back, 74 he only buys — that persists per world. Buy him out and the price climbs; flood
him and he pays less; a game day later it has drifted back. He pays coins, or takes your goods in
barter at his live buy price, and he arrives with a purse of 1500 coins, so nobody can dump a
warehouse on him. If a price moves while you are staging, the line turns amber and you confirm once
more; nothing leaves your inventory until the server has answered. The market, the wire and the
window are all built; the window has been opened on him and twenty deals have settled through it.

**The departure.** Five minutes, or Shift+E twice to send him off. He speaks a farewell and vanishes
in Odin's own effect. Built (`Client/CargoMerchant.cs`); the timer and the dismissal have ended
visits on a screen, the farewell and the vanish effect have not been watched.

## What it will not do

No horn item in 0.1. No patch on the vanilla trader or store — the terminal is our own window, opened
from our own interact handler. Nothing happens on command except an admin's `cargo visit`.

---

## Status

**Truth pass against `main` at the `v0.1.5` cut, 2026-09-24.** This mod runs on **Valheim 1.0.12 and 1.0.15**
(network version 40; tested on 1.0.15 at this cut) and on nothing older: 1.0.12 moved the network version to 40,
so a build for 1.0.7 or 0.221.12 cannot connect at all. Before this cut, on PR #104's head `253f732` (only
documents changed after it), visits #23 to #30 on Storm10 (Valheim 1.0.15, dedicated) checked each of the six
gameplay fixes in game (`docs/proofs/2026-09-24-storm10-session.md`); not run in game: the distance check on a
listen host, a two-player trade, and a sale at the capped price after a shelf roll (the price was read, not sold
at). `v0.1.0-rc4` is a 1.0.7 build and is superseded; `v0.1.0-rc3` remains the last 0.221.12
build.

**What has been seen on a machine.** Twenty-seven visits across five sessions on dedicated servers with a
real client: the flight and the drop, Ingvar in his own baked body, the walk-up, the terminal opened on
the merchant, deals with the price curve and the Fair Market Act correct to the coin, the arrival banner,
the vanish, dismissals, a relog mid-visit, a visit resumed across a restart, the rotating shelf, the
backpack multiplier, and on 2026-09-11 the carry offset tuned live from Configuration Manager while the
bird was in the air. On 2026-09-15 (Storm10): issue #79's gates — refused beside Hildir's camp, at a crypt
door and at the Sacrificial Stones, flown in to a base on a ruin and set down (a second ruin base's flight
was authored and dismissed before the drop) — and seven deals on the 101-row catalogue, including 40 hides
bought in two deals and all 40 sold back in three through the new prefab-keyed removal. That work ran on
`main` at `ad6c334`, before the version bump; no `.cs` file changed between it and the cut, and a 0.1.1
build (from `4ff22fc`, the zip's code but for the commit id a build embeds) booted on Storm10 at the cut
(`v0.1.1 loaded … catalogue=101 entries … probes 19/19 ok`). Off-game: 2093 checks, 0 warnings.
**0.1.2 adds the merchant guard on top (PR #94)** — proven live on Storm10 with DvergrAllies 1.0.7 on both
sides (visit #18: the guard named all three of its components on Ingvar's wake, the terminal opened on him,
two deals, the owner's hover clean; `docs/proofs/2026-09-15-storm10-session.md`, the last section); no other
`.cs` change since 0.1.1's zip; a 0.1.2 build booted on Storm10 at the cut (`v0.1.2 loaded … patches 19/19
applied … probes 19/19 ok`); 2093 checks, 0 warnings.
**0.1.3 adds the visit clock on top (PR #97)** — the event registers with vanilla's pause-out-of-range OFF, so a
visit ends `MerchantLifespanSeconds` after it began whoever is near him, with nobody online too; the new synced
`Server.PauseVisitWhenEmpty` (off) holds it on an empty server. Proven live on Storm10 2026-09-16 (visit #21 ended by
its timer with the server empty; visit #22 paused on the tick the engine dropped the lost peer, held six empty
minutes, resumed on the pilot's return and ended by its timer, 0 clock republishes;
`docs/proofs/2026-09-16-storm10-session.md`). The recorded visit duration now comes off the event's own clock; the
ZNet probe names `GetNrOfPlayers`; 2100 checks, 0 warnings. A 0.1.3 build booted on Storm10 at the cut: `Loading [Valkyrie's Cargo 0.1.3]`, `v0.1.3 loaded - renderer=False, patches 19/19 applied, catalogue=101 entries, engine: same build 1.0.12 (net 40, player 46, world 41); probes 19/19 ok, 8 not probeable` (08:08, no client, the sidecar's 110 rows loaded, next visit #23).
**0.1.4 adds the config migration on top (PR #99)** — the file is read before any bind, a `[Meta] ConfigVersion` stamp
says its layout, a value still at an old default moves to the new one, a value the admin set stays and is named in the
log, a `.vN.bak` lands beside the file first, and the catalogue becomes the shipped rows plus `Server.CatalogueOverrides`
(`Prefab:Base:Target:Max:Kind` to add or change a row, `-Prefab` to remove one). Proven on Storm10 2026-09-16 with three
headless boots (a Wonderland-style 0.1.0 file kept its admin's four changes and gained all 29 food rows; a second boot was
a no-op; Storm10's own 0.1.3 file migrated clean; `docs/proofs/2026-09-16-storm10-session.md`). 2540 checks, 0
warnings. A 0.1.4 build booted on Storm10 at the cut: `Loading [Valkyrie's Cargo 0.1.4]`, `v0.1.4 loaded - renderer=False, patches 19/19 applied, catalogue=101 entries, engine: same build 1.0.12 (net 40, player 46, world 41); probes 19/19 ok, 8 not probeable` (10:58, no client, no migration line because the file was already at version 2, director up at visit #23).
**0.1.5 adds the deal and market fixes on top (PR #104)** — a deal lands whole or not at all (`Core/PackTransaction.cs`),
a deal made more than 96 m from the visit is refused and a far `cargo terminal open` no longer holds him in place
for the players who are with him (`DealWire.Near`),
the Fair Market Act covers every row while the shelf rotates and caps the in-visit buy-back (both PROPOSED in
`docs/DECISIONS-WUBARRK.md` §2), a `ShelfSize` change waits for the visit, and a visit resumed mid-flight moves on
to the ground; PR #103 keeps the build machine's folders out of the DLL. Checked on Storm10 2026-09-24 (Valheim
1.0.15, crossplay, visits #23 to #30) on PR #104's head `253f732`: `v0.1.5 loaded - renderer=False, patches 19/19
applied, catalogue=101 entries, engine: newer game version (1.0.15 vs 1.0.12); probes 19/19 ok, 8 not probeable` on
the server (the client's line reads `renderer=True`); `refused VCargo_deal from TestNomad: 218.628662 m from the visit…`; after a shelf roll `Silver (Want):
1/12 max 36, he pays 28`; `visit #30 RESUMED after a restart: pilot TestNomad, 04:57 left by the saved clock`; a
deal owed to a full pack kept across a server restart and applied once (`delivery …24-1 applied: +1 Resin, -1
coins`); `docs/proofs/2026-09-24-storm10-session.md`. Not run in game: the listen-host range check, a two-player
trade, a sale at the capped price after the roll (visit #29 read the price and sold nothing), and the undo inside
the whole-deal apply (off-game only). 2589 checks, 0 failed. The cut changes only documents after `253f732`; no build of the cut commit was booted
at the cut.

**What is proven and what is not, at this cut** (`docs/proofs/2026-09-11-release-session.md`,
`docs/proofs/2026-09-15-storm10-session.md`, `docs/proofs/2026-09-24-storm10-session.md`). The
release bar is the three items `docs/RELEASE.md` §4 names. The **version wall** and the **non-admin
refusal** are proven with their lines. **Redelivery — a player paying and the goods arriving after a lost
connection — is proven off-game only and has never been seen in a game**, because the window between a
deal's answer and its acknowledgement cannot be hit from outside the client process. Also never watched
with two clients: the merchant walking off while two players trade, and the "on every machine" halves of
the deal items. **Not re-run since 0.1.1:** the version wall and the non-admin refusal — the gate and the admin
path are untouched since rc5. The wall proof was a 0.1.1 client refused by a 0.1.0 server, the other branch of
the same gate, so the direction this cut creates (a 0.1.4 client at a 0.1.5 server) is a code reading, not an
observation; the 2026-09-24 test saw only the 0.1.5/0.1.5 handshake. 0.1.5 rewrote the apply step redelivery
runs through (`Core/PackTransaction.cs`); on 2026-09-24 a deal owed to a full pack survived a server restart
and `cargo claim` delivered it once (`VCargo_claim from TestNomad: redelivered 1 owed deal(s)`), which exercises
the owed-delivery store but not the lost-acknowledgement window.

**Still a first playable, not a settled one.** The log lines below are the ones recorded in `CLAUDE.md`
"Status" by whoever saw them.

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

The registration line is the 2026-09-06 wording; since 2026-09-16 (PR #97) it reads `duration 300 s, runs
whoever is near him (Server.PauseVisitWhenEmpty holds it on an empty server), no spawns, …`.

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

### Seen on a screen, 2026-09-07, and what has not been

Nine visits on StormTest (dedicated, port 2476) from the owner's Windows client: six in the morning on
PR #46's build, three in the evening on the audit's fixes. The record is
`docs/proofs/2026-09-07-stormtest-session.md`, with every line the mod wrote in the `.log.txt` files
beside it. No exception from the mod on either side, all day.

- **The flight**: the Valkyrie flies straight in and drops him within a second of the off-game
  simulation every time (16.0 to 17.0 s), and Ingvar wears his own body every time. Not yet watched
  from two screens at once.
- **The walk-up finished on every visit** that had one, and never on the first approach: the first
  attempt gave up (D1, fixed by PR #50, unseen since), the trading leash walked him back and the
  second reached the player. He walked backward all day — a half-turn at the attach, fixed by PR #53
  and not yet seen. On the last visit he never woke: the pilot's client lost ownership of him during
  the carry. That is the open defect at this cut.
- **The Cargo Terminal** opened on him with E and closed on use and on the inventory: twenty deals
  settled over the wire — sells, buys, a four-line barter — at the price curve, with the Fair Market
  Act clamp, both drift knobs and the purse carry correct to the coin, and `purse_empty` refused twice
  then counted. `cargo terminal demo` drew the window on an in-process market first.
- **Visits begin and end**: natural rolls and forced ones; of the morning's six, three ended by the
  timer and three by dismissal (`cargo dismiss` and "Send him off"); the eligibility refusals
  (`not rested`, `comfort < 4`, `on cooldown`) echoed to the client; the clock paused with nobody near
  (a behaviour removed 2026-09-16, PR #97: the clock runs whoever is near him);
  and a relog mid-visit that the persistent merchant survived. Not yet seen: the centre banner and a
  non-admin refused.
- **The sidecar and the export**: stock, purse, visit, cooldown and session rows written on every
  save, and the BarrkBOT trader and visit rows on the next cycle. The restart-mid-visit half is still
  to run.
- **The boot lines** on both sides: `renderer=True, patches 18/18 applied, catalogue=72 entries,
  engine: same build … probes 18/18 ok, 7 not probeable`; the moved-version direction proven offline
  against 0.221.4.

Never yet seen on a screen: the release over the drop point and the vanish in Odin's effect, the
callout bubble, the hover prompt, the ServerSync version wall, the two-client items, and every fix
merged since the audit (its six, then D1 to D4 and the half-turn; `docs/AUDIT-STORMTEST-2026-09-07.md`
§5 says what would exercise each).

The full numbered list is `CLAUDE.md`, "What to verify in-game"; `docs/PROOF-CLIENT.md` is the
runbook, and each proof gets pasted back into `CLAUDE.md` as it happens.

So today a visit puts Ingvar in the yard, walks him up and trades through the terminal, proven on a
screen against a dedicated server across twenty-seven visits behind rc5 and more since, the last eight
(#23 to #30) on 2026-09-24. The first approach reaching the player,
the vanish and the carry holding its owner were all open questions at the rc2 cut and have since been
watched and fixed. What has still never been seen is **redelivery after a lost connection**, and no
two-client item has run. The tag is `v0.1.5`; the store carries whichever cut was last uploaded;
`docs/RELEASE.md` §4 records the three-item bar that was met and the one item skipped, with the reason.
---

## If a client is refused with a version message

**Read the server log before believing it.** ServerSync fails closed when the version exchange does not
complete, and a transport failure looks exactly like a version mismatch from the client's side. Both were
seen on 2026-09-11 within an hour of each other, and the server log tells them apart in one line:

- **A real mismatch** — the server received a version different from its own, and says so:
  `Received Valkyrie's Cargo version 0.1.1 and minimum version 0.1.1 from the client.` followed by
  `Disconnect: The client (...) doesn't have the correct Valkyrie's Cargo version 0.1.0`. Install the same
  build on both sides.
- **A transport failure wearing the same message** — the server received the SAME version, accepted the
  peer (`Network version check, their:40, mine:40`, `Server: New peer connected,sending global keys`) and
  then lost the socket in the same second: `Failed to send, suspend TX on playfab/... while trying to
  reconnect`. Nothing is wrong with the mod. On crossplay this clears when the server is restarted, which
  releases the PlayFab party network.

**And on a crossplay server, read the join code off the right line.** The log prints two:
`Session "<name>" registered with join code <n>` is not a code anyone can join with and is the same number
on every boot; `Session "<name>" with join code <n> ... is active with N player(s)` is the live one.

## Installing

**Server:** required. **Every client:** required, on exactly the same version. The mod refuses a
client that does not have it, or runs any other build, at the handshake, with ServerSync's message
naming the mod and both versions. It has to: the visuals, the terminal and the trade all live in
client code.

On a Gale-managed client the plugin folder is
`%APPDATA%\com.kesomannen.gale\valheim\profiles\<profile>\BepInEx\plugins\`, not the Steam folder.

**Before 0.1.4 (history): updating meant deleting the old config file first.** The server was taken down,
`BepInEx\config\com.raveniron.valkyriescargo.cfg` was removed, the new DLL went in, and on start the
file was written fresh with the shipped defaults (0.1.3 added `Server.PauseVisitWhenEmpty`; 0.1.1 grew the
catalogue to 101 rows). A stored line beat the shipped default, so an old file hid new rows and new knobs
— on Wonderland the 72-row catalogue from 0.1.0 outlived two updates. Anything set on purpose (`PurseCoins`,
the roll, the flight) needed setting again afterwards; `cargo catalogue reset` as an admin took the shipped
catalogue alone. A client's copy could stay: every `Server.*` value on it is overruled by the server.

**Updating a server to 0.1.4 or later, from any earlier version: nothing to do.** The mod migrates the file
itself, before it binds anything: a value still at an old default moves to the new one, a value you set on
purpose stays exactly as you set it, and a backup of the old file lands beside it (`<file>.v<N>.bak`) before
anything is touched - never overwritten: a second migration of the same file writes a timestamped `.bak`
instead, so a half-finished earlier run cannot destroy the only clean copy. The catalogue in particular moves
to `Server.CatalogueOverrides` - the shipped catalogue plus your changes, so a new default (the next set of
rows, whatever they are) can never be shadowed by an old stored line again. The boot log names what it did,
and `cargo config` says it again on request.

Requires [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
and nothing else: no Jotunn, no JSON library (the BarrkBOT export writes its files through the mod's
own `Core/Json.cs`). `manifest.json`'s dependency list is that one entry.

Built against the assemblies of Valheim **1.0.12** (the install of 2026-09-11); that install's
`UnityPlayer.dll` reports **Unity 6000.0.75**, which is the Editor version any asset bundle for
this mod must be built with. 0.221.12 is no longer a build target (Steam's `default_pre1_0` branch
keeps it; `v0.1.0-rc3` was the last build for it).

---

## Configuration

`BepInEx\config\com.raveniron.valkyriescargo.cfg`. **Up to 0.1.3, an update meant deleting the server's
copy** so it was written fresh with the shipped defaults — a stored line beat a new default (Installing,
above, now history). **From 0.1.4 the mod migrates the file itself**, from any earlier version, on boot (a stamped `Meta.ConfigVersion`, a backup
beside the file, a boot line saying what moved and what was kept) — nothing to delete any more.

**Every `Server.*` value is synced from the server and locked**: on a connected client the local
value is shown read-only, and a client's write is rejected unless that client is on the server's
`adminlist.txt`. `Client.*` values are local and never leave the machine. `LockConfiguration`
itself is what arms the lock.

### `[Server]`

| Key | Default | Range | Meaning |
|---|---|---|---|
| `LockConfiguration` | `true` | | Server enforces every `Server.*` value on every client. Admins on `adminlist.txt` may still change them. On Valheim **1.0.12** the bare number, `Steam_<id>` and the display id `V_<steamid>` all admit, so a list from any era works. On **1.0.7 only**, the display id was the one that matched and a list holding the older forms admitted nobody: 1.0.7 ended `ZNet.ListContainsId` with `flag =`, which overwrote the earlier match, and 1.0.12 made it `flag \|=`. The list reloads on file change either way, with no restart. |
| `Enabled` | `true` | | Roll visits at all. |
| `RequireRested` | `true` | | A player must carry the Rested effect to be eligible. |
| `MinComfortLevel` | `4` | 0-20 | Minimum comfort level (the number in the Rested tooltip) for eligibility. A bed, a fire and a roof give 3; 4 needs a chair or a banner. |
| `MinBaseValue` | `1` | 0-10 | Vanilla base value at the player (workbench/forge coverage); vanilla raids use 3. |
| `DaytimeOnly` | `true` | | Only roll visits by day; the flight is the show. |
| `EventCheckIntervalMinutes` | `25` | 1-240 | Real minutes between rolls. |
| `EventChancePercent` | `25` | 0-100 | Chance per roll when at least one player is eligible. |
| `PlayerCooldownMinutes` | `60` | 0-1440 | Real minutes before the same player can be chosen again; stamped at dispatch. |
| `CooldownRadius` | `60` | 0-500 | Metres: a base on cooldown blocks its neighbours within this radius. |
| `RequireBuiltBase` | `true` | | **Issue #79 (2026-09-14).** A visit needs ground somebody actually BUILT on: at least one player-placed piece within `BuiltBaseRadius` of the player, reported by the client the way comfort is. Vanilla's base value counts the game's own camps (the Bog Witch's, Haldor's) as a base; this is what stops that. |
| `BuiltBaseRadius` | `20` | 4-64 | Metres around the player searched for a player-placed piece; 20 is the radius vanilla measures base value over. |
| `AvoidMerchantCamps` | `true` | | Refuse a visit at another merchant's camp - Haldor, Hildir, the Bog Witch, any modded trader: any location holding a `Trader`, by its own exterior radius plus `LocationClearance`. A location has one kind and one switch, so a camp is never also "a landmark" and `false` really does open the camps. Read on the server. |
| `AvoidDungeonEntrances` | `true` | | Refuse a visit at the door of a crypt, cave, mine or fortress: any location with an interior. Being inside one is refused regardless. Read on the server. |
| `AvoidLandmarks` | `true` | | Refuse a visit at a place the game pins on the map that is neither a camp nor a dungeon door: the Sacrificial Stones, every boss altar. The game's own map-icon flag, no name list; the server log names what this world has at the first roll. Read on the server; a client's `cargo status` cannot see it. |
| `LocationClearance` | `8` | 0-64 | Metres around the player added to a location's own radius for the three checks above; the margin that keeps him off the boundary fence. |
| `MerchantLifespanSeconds` | `300` | 30-1800 | How long Ingvar stays, as the vanilla random event's duration. The clock runs from the moment the visit begins whoever is near him or not, on the server's own time. |
| `PauseVisitWhenEmpty` | `false` | | Hold a running visit's clock while nobody is online, so a visit started at bedtime is still there in the morning. Off: he leaves when the lifespan runs out, empty server or not. A listen host counts as a player online, so it never engages there. A lost connection keeps its peer counted until the engine's ZRpc timeout drops it, 90 s later; a clean logout drops it at once. Read on the server. |
| `ApproachDistance` | `3.5` | 1-10 | Metres from the player at which he stops walking up. Read on the **client that owns the merchant**, synced from the server. |
| `BodyPrefab` | `Dverger` | | The engine creature prefab the merchant is **cloned from, for good** — `Character`, `MonsterAI` and the collider all come from it, whatever body is drawn on top. Must have a `Humanoid`, a `MonsterAI` and an `Animator`. This is not the custom-body switch; that is `CustomBody`. |
| `CustomBody` | `true` | | Put Ingvar's own body on the `BodyPrefab` clone from the AssetBundle embedded in this DLL. `false` keeps the Dverger stand-in visible, and so does a build with no bundle embedded (`cargo body` says which). This is the switch, not `BodyPrefab`. Read on the **client**, synced from the server; a dedicated server never reads it. |
| `FlightStartDistance` | `90` | 30-200 | Metres from the pilot where the Valkyrie appears; shrunk at runtime, 12 m at a time, until the start fits inside the pilot's active zone block. The floor is `FlightPlan.MinimumStartDistance` (30 m) — below that the bird would appear on top of the player. |
| `FlightStartAltitude` | `120` | 30-400 | Altitude of the Valkyrie's start point, metres above the drop. |
| `FlightDescentDistance` | `50` | 10-200 | Metres out at which the descent leg begins. |
| `FlightSpeed` | `8` | 2-40 | Metres a second the Valkyrie flies, overriding the prefab's own speed. 8 gives design 3.2's 15-20 s of sky over a 90 m approach. Read on the **client that owns the bird**, synced from the server; the server never reads it. |
| `FlightTurnRate` | `45` | 5-360 | Degrees a second the Valkyrie may turn, overriding the prefab's own. Read on the **client that owns the bird**, synced from the server. |
| `CarryOffset` | `0, 0, 0` | +/-5 an axis | Where Ingvar hangs while the Valkyrie carries him: `x, y, z` in the **talon's own space**, SUBTRACTED from the talon. The default holds his feet **on** the talon, tuned on a machine for his height; bigger numbers hang him further off it (`y` below, `z` behind) and negative ones carry him past it. Leave it **empty** to follow the Valkyrie prefab's own offset instead, `0, 0.3, 0.4` on the shipped bird, which is vanilla's framing for a full-height player in the intro. Anything that is not three numbers, or past 5 m on an axis, is refused with one log line and the prefab's used. Read on **every machine that has him instanced** (the pin runs on each, and every screen must agree), synced from the server; a change lands on the next physics step, so it can be tuned while he is in the air. |
| `CatalogueOverrides` | (empty) | | **0.1.4.** The shipped catalogue plus your changes: `Prefab:BasePrice:TargetStock:MaxStock:Kind` to add or change a shipped row, `-Prefab` to remove one, comma-separated. Empty (the default) is the shipped catalogue exactly as it ships - 101 entries, every number's reason in `docs/CATALOGUE.md`. Editable on a running server with `cargo catalogue add|remove|reset` (admin) or Configuration Manager as an admin; a change applies as soon as no visit is running. Replaces the old `Catalogue` key, which stored the whole line and let a stale value shadow a new shipped default forever (`docs/CATALOGUE.md` "Overrides"). |
| `PriceElasticity` | `0.35` | 0.05-1.5 | Exponent of (target / stock) in the price; higher is steeper. |
| `MinPriceMultiplier` | `0.4` | 0.05-1 | Floor on the price multiplier when he is flooded. |
| `MaxPriceMultiplier` | `3` | 1-10 | Ceiling on the price multiplier when he is out. |
| `SpreadBuy` | `0.7` | 0.1-1 | What he pays as a fraction of what he charges for the same item. |
| `FairMarketAct` | `true` | | Caps what he pays to buy back a Ware at par (base × `SpreadBuy`), so a high `MaxPriceMultiplier` can never turn buying him out and selling straight back into free coins. Since 0.1.5 it also covers every row while the shelf rotates, and within a visit he never pays more for an item than the lowest price he sold it at that visit. |
| `WareHalfLifeGameDays` | `0` | 0-365 | Between visits a **Ware**'s stock drifts back toward its target with this half-life, in game days (30 real minutes of server uptime each). `0` is never: what he has to sell is what players sold him and what an admin's target says (`cargo catalogue add`). The owner's call, 2026-09-07; the sweep is `docs/ECONOMY-SIM.md` §10. |
| `WantHalfLifeGameDays` | `3` | 0-365 | The same for a **Want**: he passes on what he was sold, so a flooded row half-clears in three game days and he never fills up for good. At `0` every Want fills to its max and he stops buying it. |
| `ShelfSize` | `20` | 0-200 | **The rotating shelf (2026-09-08).** How many catalogue entries he sells at a time, drawn from the whole catalogue by a seeded roll every `ShelfRotationGameDays`: on the shelf an entry is sold at the curve, bought back under the Fair Market Act and drifts on the Ware half-life; off it, bought only (still under the Fair Market Act since 0.1.5: every row is on the shelf in some period). `0` is the fixed shelf: the `Ware`/`Want` kinds in the catalogue line decide, as before. `docs/CATALOGUE.md` §7. |
| `ShelfRotationGameDays` | `2` | 0.1-365 | How many game days one shelf lasts. The roll happens on the first idle tick of a new period, never under a running visit, and a restart mid-period shows the same shelf. |
| `BackpackShelfMultiplier` | `2` | 1-4 | **The backpack add-on (2026-09-08).** When the backpack mod named by `BackpackModGuid` is loaded on the server, the shelf is `ShelfSize` times this (capped at 200 and at the catalogue): players who can carry more get more to buy. Nothing changes without the mod. `0` in `ShelfSize` stays the fixed shelf. |
| `BackpackModGuid` | `org.bepinex.plugins.backpacks` | | The BepInEx GUID of the backpack mod to look for (Smoothbrain's Backpacks by default). Looked up on the server at director up and once a second after; empty = never look. |
| `PurseCoins` | `1500` | 0-100000 | Coins he arrives with. |
| `PurseCarryPercent` | `50` | 0-100 | Percent of last visit's takings added to the next purse, capped at three purses. |
| `EnableBarter` | `true` | | Allow paying with goods he wants, valued at his live buy price. `false` makes the terminal refuse goods staged beside a ware ("Coins for my wares on this shore") and hides "Cover it with my goods"; the server settles a barter deal either way. Read on the **client**, synced from the server. |
| `BarrkBotExport` | `true` | | Write `barrkbot_cargo_market.json`, `barrkbot_cargo_traders.json` and `barrkbot_cargo_visits.json` under `BepInEx/config/ValkyriesCargo/` for BarrkBOT to read, refreshed about once a minute; the world sidecar is still the source of truth. |

### `[Meta]`

| Key | Default | Meaning |
|---|---|---|
| `ConfigVersion` | `0` | **0.1.4.** The config file's layout version, stamped by the mod. Do not edit it: the mod migrates the file itself on boot (backing it up beside itself first) and stamps this when it finishes. Local, never synced. `cargo config` reads it against the current version. |

### `[Client]`

| Key | Default | Range | Meaning |
|---|---|---|---|
| `ShowArrivalMessage` | `true` | | Show the private "wings beat in the upper skies" line when you are the chosen player. |
| `ShowPriceTrend` | `true` | | Show the up/down glyph against base price in the terminal. |
| `Theme` | `Vanilla` | `Vanilla` \| `BlackGold` | Terminal metal colour. |
| `TerminalScale` | `1.0` | 0.5-2 | Terminal size multiplier. |
| `TerminalBackdropAlpha` | `0.4` | 0-1 | Opacity of the black backdrop behind the terminal's text: `0.4` is a 40 % translucent black (the playtest's ask, 2026-09-08), `1` the solid panel of before, `0` the frame alone over the world. |

---

## Console

Prefix `cargo`. Console commands are not config: `LockConfiguration` does not touch them.

| Command | What it does |
|---|---|
| `cargo status` | Role, which side owns the config, the catalogue, the state channels, the transport, and — where the world runs — the director, the visit, the wire, the sidecar and every candidate. The instrument to reach for first. |
| `cargo version` | This build, and that every client must run exactly it. |
| `cargo config` | **0.1.4.** The config file's layout version against the current one, the catalogue overrides in words, and the last migration's boot line. Anyone, local. |
| `cargo prefab <name>` | A game prefab's components, children, effect lists (with the networked/local branch each entry takes) and animator parameters. Try `Valkyrie`, `Dverger`, `odin`, `Haldor`. |
| `cargo stock [prefab]` | His shelf as this machine last heard it: stock against target, what you pay, what he pays, the trend. Names one row, or the first 24. |
| `cargo deal buy\|sell <prefab> [count]` | A deal without the terminal: builds the same `Deal`, sends it over the same wire, applies the same answer. The reference path. Since 0.1.5 it answers `too_far_to_trade` from more than 96 m from the visit, like the terminal. |
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
| `cargo catalogue reset` | **Admin.** Back to the shipped 101 entries. Same path. |
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
- `docs/CATALOGUE.md` — the 101 default entries with the reason for every number, checked against
  `docs/data/items-valheim-2026-07-31.tsv`.
- `docs/WORKSPLIT.md` — who owns what, and the frozen contract between the two tracks.
- `docs/RELEASE.md` — how a release is cut.
- `docs/PROOF-CLIENT.md` — the in-game verification runbook: the order, the exact command for each
  item, and the log line the code writes.
- `CLAUDE.md` — the engineering notes: engine facts read from the decompiled assembly, house style,
  the status lines quoted above, and the in-game verification list.
- `PLAN.md` — the original plan; `docs/DESIGN.md` is authoritative where the two differ.

## Credits

**Raven Iron is NomadicWar & Wu'barrk.** Both founders, both designers of this mod.

- **NomadicWar** - co-founder, Raven Iron. Design; the market and the economy, the visit director,
  persistence and the server side.
- **Wu'barrk** (Thorium Wu'barrk) - co-founder, Raven Iron. Design; Ingvar himself
  (`models/ingvar.fbx`: model, rig, texture and animation), the Cargo Terminal, and the interface
  work beneath it: `Libs/SharedUI/GiltFrameTheme.cs` and `Libs/SharedUI/UIFocus.cs` are his VikingOS
  0.9.8 shared source, MIT, vendored here and never edited.

**ServerSync** is blaxxun's `ConfigSync.cs`, MIT-0, compiled in as shared source.

Item data checked against Wu'barrk's TheEye dump of 2026-07-31.

---

## Support Raven Iron

Every Raven Iron mod is free, and stays free — all of it, always. Nothing is held
back for patrons, and nothing ever will be.

If you'd like to help cover server hosting and test hardware:

- **Website** — <https://ravenirongames.com>
- **Patreon** — <https://www.patreon.com/cw/RavenIronGames>
- **Discord** — <https://discord.gg/AGKDEurAVa> — a channel per mod, and where the
  testing happens
