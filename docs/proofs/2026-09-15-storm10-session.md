# Storm10, 2026-09-15 — issue #79's gates, the food rows and one row per token, on a screen

**What this is.** The lines behind the `v0.1.1` cut's live claims, copied out of the two `BepInEx\LogOutput.log`
files on the day — rc5's session wrote the rule that a proof living only in that file is not preserved. The
extract is `2026-09-15-storm10-session.log.txt` beside this file: the server's lines first, numbered as in
Storm10's log (which is appended across boots, `AppendLog=true`), then the client's. The comments on PR #82,
PR #84 and PR #86 quoted the same lines at the time.

**The server.** Storm10, `C:\Users\donfr\ValheimServers\Storm10`, port 2477, world Storm10, crossplay,
Valheim **1.0.12**. Don's client on the Gale **testing** profile, in-game `Nomadtest`, an admin. The cfg's
stored `LocationClearance` was 25 (not the shipped 8) for the first refusals — found at the 09:36 deploy and
corrected while the server was down (PR #84's correction comment); both refusals hold at 8 (Hildir at 30.9 m
is inside 24 + 8, the stones at 32.2 m inside 25 + 8).

**The builds.** Three builds ran during the day and every one logs `Valkyrie's Cargo v0.1.0 loaded`, because
the version bump came after: PR #82's (08:07–08:22), PR #84's (08:41–08:47) and merged `main` at `ad6c334`
(#84, #85, #86 in; 09:35–09:50; md5 `BA6B2F5C98AC083797A7FDA5A5ABD268`). No `.cs` file changed between
`ad6c334` and the `v0.1.1` cut — the version number, `manifest.json` and documents did. A 0.1.1 DLL built from `4ff22fc`
(md5 `31545E9778C6BEAE6AB5A8BCFE675745`) booted on Storm10 at 10:33 with no client: `Loading [Valkyrie's
Cargo 0.1.1]`, `v0.1.1 loaded - renderer=False, patches 18/18 applied, catalogue=101 entries, engine: same
build 1.0.12 (net 40, player 46, world 41); probes 19/19 ok, 8 not probeable, ServerSync version gate armed`,
`director up: … catalogue 101 entries, purse 100725, next visit #14 … sidecar … (108 rows loaded)`, no
`FAILED`, no `registry:`; stopped gracefully at 10:35. The store zip's DLL is the same code rebuilt from the
commit tagged `v0.1.1`. A build embeds its commit id (`0.1.1+<sha>` in the informational version) and the PE
checksum, MVID and PDB id follow from it, so the two DLLs differ in 145 bytes across 7 runs — that string and
the header ids — and nowhere else (a byte compare at the cut). The shipped hash is on the release page.

## Issue #79's gates (PR #82, PR #84)

Forced visits by the admin (`cargo visit`); the answer is the server's `roll:` line.

| time | where | server log | outcome |
|---|---|---|---|
| 08:13:55 | beside Hildir's camp | `forced: Nomadtest not eligible: inside Hildir_camp, a merchant's camp` (4539) | refused |
| 08:16:21 | the Sacrificial Stones, under #82's two-kind rule | `forced visit: Nomadtest at (-31.124, 8.922502); 1 eligible` → visit #10 authored, `dropped at (-17.44, 80.74, 8.09)`, dismissed (4563–4574) | granted — the gap the third switch closes |
| 08:17:40 | a base on a Meadows ruin (`WoodHouse3`) | `forced visit … 1 eligible` → visit #11 `flight authored … straight in 90 m out`, `dropped at (-122.17, 43.86, 18.49)`, `cargo dismiss` at 08:18:50 (4609–4623) | granted, flown in, set down |
| 08:22:07 | a crypt door | `forced: Nomadtest not eligible: inside Crypt3, a dungeon entrance` (4888) | refused |
| 08:46:12 | a second ruin base (`WoodHouse9`), under #84's three-kind rule | `forced visit … 1 eligible` → visit #12 `flight authored … straight in 90 m out`; `cargo dismiss` eight seconds later, **no `dropped at` line** (6013–6025) | granted, flown, dismissed before the drop |
| 08:46:39 | the Sacrificial Stones, under #84's rule | `forced: Nomadtest not eligible: inside StartTemple, one of the game's landmarks` (6044) | refused |

So: one ruin flight set down (visit #11), a second authored and dismissed before the drop (visit #12) — not
"twice", which the CHANGELOG and README said until the `v0.1.1` docs pass.

Once per boot from #84's build on, the server names what its world pins: `landmarks in this world
(Server.AvoidLandmarks refuses these): StartTemple x1; pinned on the map but a merchant's camp
(Server.AvoidMerchantCamps): Hildir_camp x1; pinned but a dungeon's door (Server.AvoidDungeonEntrances): none;
pinned once their zone generates, not yet placed in this world: AncientUpgradeStation x10, BogWitch_Camp x10,
Hildir_cave x3, Hildir_crypt x3, Vendor_BlackForest x10, bosslocation x3` (6803, 7176, 7629).

**The stored catalogue line.** The first boots of the `ad6c334` build came up with `director up: … catalogue
72 entries` (6406, 6785) although the build ships 101: BepInEx's stored `Catalogue` line in the cfg wins over
the shipped default. After that line was taken out of the cfg while the server was down: `catalogue 101
entries` (7158). Hence the "run `cargo catalogue reset`" advice for servers that already run the mod.

## Seven deals on visit #13 (PR #85, PR #86) — `ad6c334`, 09:44–09:50

`cargo visit` at 09:44:33 (7231–7240): `forced visit: Nomadtest at (-171.06, 50.78); 1 eligible`, flight
authored `straight in 90 m out`, `dropped at (-169.94, 30, 64.95)`. Then, server settled (7247–7261) against
client applied (client 1000–1120):

| deal | server | client |
|---|---|---|
| 13-1 | `sold 20 CookedLoxMeat at 7, coins -140 to the player; purse 100140` | `ok wffffffffa3c7dd22-13-1 +20 CookedLoxMeat, -140 coins` |
| 13-2 | `sold 60 Blackwood at 4, coins -240; purse 100380` | `+60 Blackwood, -240 coins` |
| 13-3 | `sold 1 BjornHide at 10, coins -10; purse 100390` | `+1 BjornHide, -10 coins` (`Adding item BjornHide x1`) |
| 13-4 | `sold 39 BjornHide at 10, coins -390; purse 100780` | `+39 BjornHide, -390 coins` (`Adding item BjornHide x39`) |
| 13-5 | `sold 1 SerpentMeat at 5, bought 1 BjornHide at 7, coins +2; purse 100778` | `+1 SerpentMeat, -1 BjornHide, +2 coins` |
| 13-6 | `sold 10 TrophyGoblin at 15, sold 7 Carapace at 10, bought 32 BjornHide at 7, coins +4; purse 100774` | `+10 TrophyGoblin, +7 Carapace, -32 BjornHide, +4 coins` |
| 13-7 | after `one minute left`: `bought 7 BjornHide at 7, coins +49; purse 100725` | `-7 BjornHide, +49 coins` |

`visit #13 ended: timer; takings 725 coins, purse 100725` (7262) — 140 + 240 + 10 + 390 − 2 − 4 − 49 = 725, so
the closing line balances only with the seventh deal, which PR #86's comment and the first cut of the release
documents left out. The hides: 40 bought in two deals (13-3, 13-4) and all 40 sold back in three (13-5, 13-6,
13-7) through the prefab-keyed removal. Neither log records stack counts (`BjornHide` stacks to 50), so
nothing here says how the 40 sat in the pack; "32 hides across two stacks" was a guess and is withdrawn. Visit #14 below
is the proof that run could not give.

Two of the new food rows traded: `CookedLoxMeat` (a Ware, 13-1) and `SerpentMeat` (a Want, 13-5).

**One row per token.** `admin Nomadtest (167492677): cargo catalogue add FishAnglerRaw:16:40:120:Want` at
09:47:33 (7257); the client's `server answered: cargo: catalogue add refused: 'FishAnglerRaw' carries the same
item token as 'FishRaw', already in the catalogue ($item_fish_raw); the inventory counts goods by that token,
so only one of the two can be traded - remove FishRaw first if this is the one you mean` (client 1113).

**The console mirror (PR #81).** `console: cargo: asked the server to visit; its answer prints here` (client
955) and `server answered: cargo visit Nomadtest: forced visit: …` (960): the console line as `console: …`, the
server's answer as `server answered: …`, both in the client's log.

## Not seen on the day

- **The version wall in this cut's direction** (a 0.1.0 client at a 0.1.1 server). No client joined the 0.1.1
  boot. The 2026-09-11 proof (`2026-09-11-release-session.log.txt` lines 376–381) is the mirror pair: a 0.1.1
  client refused by a 0.1.0 server. Same gate, other branch.
- **The 0.1.1 client boot line** — seen at 11:04, see visit #14 below.
- **Redelivery**, and every two-client item, as at rc5.

## Visit #14 — the stack walk, 11:05–11:12, the zip's own DLL on both sides

The server rebooted at 11:00 on the DLL that ships in `RavenIronStudios-ValkyriesCargo-0.1.1.zip`
(`0.1.1+a5e4e4f`, md5 `17CB3DCD025921CF33958077B77F91DC`; `Loading [Valkyrie's Cargo 0.1.1]` at line 7685,
`director up: … catalogue 101 entries, purse 100725, next visit #14`), and the `testing` client launched at
11:04 on the same bytes: `Valkyrie's Cargo v0.1.1 loaded - renderer=True, patches 18/18 applied,
catalogue=101 entries … probes 19/19 ok` (client line 19). The handshake both ways: `Sending Valkyrie's Cargo
version 0.1.1 and minimum version 0.1.1 to the client` / `Received … 0.1.1 … from the client` on the server,
the mirror on the client, `Network version check, their:40, mine:40`.

`cargo visit` at the same base as #13: the first refused `not eligible: comfort < 4`; the second `forced
visit: Nomadtest at (-170.37, 53.19); 1 eligible` → `visit #14 begins … purse 100000`, flight authored
`straight in 90 m out`, `dropped at (-161.42, 30.98, 63.08)`.

| deal | server | client |
|---|---|---|
| 14-1 | `bought 60 Blackwood at 3, coins +180 to the player; purse 99820` | `ok wffffffffa3c7dd22-14-1 -60 Blackwood, +180 coins` |
| 14-2 | `bought 25 Resin at 1, coins +25 to the player; purse 99795` | `ok wffffffffa3c7dd22-14-2 -25 Resin, +25 coins` |

**The stack walk, proven.** `Blackwood` stacks to 50 (`docs/data/items-valheim-2026-07-31.tsv`), so the 60
bought on visit #13 (deal 13-2) sat in two stacks, and removing 60 in one deal had to cross both — the path
`Client/DealApplier.Remove` walks and nothing off-game exercises. The player-side dismiss ended it:
`visit #14 ended: dismissed by Nomadtest; takings 0 coins, purse 99795`.

**The walk-up ran out its budget once.** Client: `the walk-up did not finish. walked 20.000267 s of a 20 s
budget (scaled from 25.6692371 m at entry), moved 40.6875229 m; stuck 0 s of the last window, and stopped
17.57369 m away` (a Warning), then `trading (entered: gave up walking after 20 s)`, `approaching (entered: the
player left: 17.56 m for 5.02 s)`, `trading (entered: reached the player)`, `terminal opened`. On a plain base
he walked further than the straight line and ended further away than he started; the re-approach when the
player moved recovered it. Wu'barrk's merchant logic behaving as written, noted for him, not a fault of the cut.

**Found: the purse carry does not survive a restart.** Visit #13 took in 780 coins gross (13-1 to 13-4), and
the sidecar restored that as `coined` at both later boots, but the number `Market.StartVisit` receives is the
director's `_lastCoined`, which is set only when a visit ends (`VisitDirector.End`). So visit #14 opened at
`purse 100000` — `PurseCoins` with a carry of 0 — where an unbroken server would have opened it at 100390 (50 %
of 780, cap 3×). On a shipped server (`PurseCoins` 1500) every visit that follows a restart starts without the
carry the 2026-09-07 decision gave it. One line at the end of `LoadSidecar` (`_lastCoined = _market.Coined`)
would seed it from the sidecar; not changed here. **Fixed as PR #91 (main `5c58e81`) and proven at 13:09, below.**

## Visits #15 and #16 — the purse carry across a restart, proven, 13:03–13:09

The server on the carry-fix build (`0.1.1+5c58e81`, PR #91 merged; md5 `8A273C6108F9BFD7B42DDFBA7CA08085`, on the
`testing` client too), booted 13:01. `cargo visit` at (-63.5, -18.1): `visit #15 begins … purse 100000` — right,
visit #14 had taken in nothing. One deal, 15-1: `sold 2 BlackCore at 300, sold 20 AmberPearl at 14, sold 9 Ruby at 35, sold 40 FishRaw at 2, sold 33 Carapace at 11, coins -1638 to the player` — 1638 coins gross into the purse. `visit #15 ended:
dismissed by Nomadtest; takings 1638 coins, purse 101638`.

Graceful stop at 13:08:17 (`Game - OnApplicationQuit`). The sidecar on disk between the two processes:
`purse 101638`, `purseStart 100000`, `coined 1638`, `visit 15`. Relaunched 13:08; `director up: … purse 101638,
next visit #16`.

`cargo visit` at the same spot: **`visit #16 begins … purse 100819`** — `PurseCoins` 100000 plus half of the 1638
the previous visit took in, through a full stop and relaunch. Before PR #91 this line read `purse 100000`
(visit #14 above, after visit #13's 780). The one assignment no off-game check reaches is now the one line a
machine has shown.

