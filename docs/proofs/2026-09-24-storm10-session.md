# Storm10, 2026-09-24 — the 0.1.5 fixes in game (PR #104)

**What this is.** The lines behind 0.1.5's in-game claims, copied out of the two BepInEx logs on the day,
because the client's log is overwritten on every game start. The extract is `2026-09-24-storm10-session.log.txt`
beside this file: the server's mod lines for the run with the join and restart lines, then the client's from its
last game start. The client's lines from visits #23 and #24 are not in it: the game was restarted after the 09:44
timeout, and that replaced its log.

**The server.** Storm10, port 2477, world Storm10, crossplay, Valheim **1.0.15**, dedicated. The client is a
Gale test profile, in-game `TestNomad`, an admin. Visits #23 to #30, 09:31 to 10:07.

**The build.** PR #104's head, `253f732`, DLL md5 `a405d7bb9c0ad7e91520e2a711607a9d`, on both sides. Each server
boot (09:31 and the two restarts) logs `Valkyrie's Cargo v0.1.5 loaded - renderer=False, patches 19/19 applied,
catalogue=101 entries, engine: newer game version (1.0.15 vs 1.0.12); probes 19/19 ok, 8 not probeable, ServerSync
version gate armed`, and the client's boot logs the same line with `renderer=True`; the handshake reads
`Received Valkyrie's Cargo version 0.1.5 and minimum version 0.1.5 from the client.` The cut commit changes only
documents after `253f732`.

| Step | Fix | Result | Evidence |
|---|---|---|---|
| 7 | Boot / regression | **PASS** | The boot lines above (`renderer=False` on the server, `renderer=True` on the client); handshake 0.1.5/0.1.5. Terminal buy and sell (deals 23-1, 23-2); barter (27-1: he sold 1 Bronze for 2 Resin and 13 coins; client `+1 Bronze, -2 Resin, -13 coins`). |
| 1 | Partial deal | **PASS** | A pack with one free slot and two wares in YOU GET: the HUD showed "You have no room to carry it.", no deal reached the server, and the pack was unchanged. `cargo claim` delivered nothing. |
| 1b | Owed delivery across a restart (extra) | **PASS** | Console deal 24-1 settled while the pack was full (server `deal wffffffffa3c7dd22-24-1 with TestNomad: sold 1 Resin at 1…`; the client's `deal apply refused: inventory_full` came before its log was replaced and is not in the extract). The server ended the visit with `1 owed delivery` and kept it through a server restart (`VCargo_claim from TestNomad: redelivered 1 owed deal(s)`). First claim: `deferred: inventory_full (the server keeps it until it fits)`. After a slot was freed: `delivery …24-1 applied: +1 Resin, -1 coins`, exactly once. A final claim after step 5 delivered nothing. |
| 2 | Distance | **PASS** | 218.6 m away: server `refused VCargo_deal from TestNomad: 218.628662 m from the visit…`; the far open `not counting VCargo_open from TestNomad…`. The client's answer, `too_far_to_trade`, came before its log was replaced and is not in the extract. Near: deal 25-1 `DONE … +1 Resin, -1 coins`. |
| 6 | In-visit cap (PROPOSED rule) | **PASS** | Silver flooded, then bought out at 27 (25-4); the sell-back paid 27, not 28 (25-5). At 0/12 `he pays 27` (the cap), where the uncapped price is 28. At a later visit (#29) the record is gone (`pays 28`): the documented G1 residual. |
| 4 | `ShelfSize` live | **PASS** | 32 → 0 mid-visit #26 applied only after the visit ended (`shelf rolled: shelf fixed (ShelfSize 0 …)`). 0 → 32 mid-visit #27: `cargo deal buy Bronze 1` still went through (27-2), though Bronze is not on the 32-shelf; the roll waited for the visit to end. |
| 3 | Shelf round trip (PROPOSED rule) | **PASS** | Silver bought out on the period-11 shelf; `ShelfSize` 32 → 8 rolled it off still empty (`shelf rolled: shelf 8 of 101, period 11, rolls every 2 game day(s), next in 1.3 game day(s): SilverNecklace, Honey, …`). Visit #29: `Silver (Want): 1/12 max 36, he pays 28`, read on the client; no Silver was sold at it. The pre-fix code works out to 67 at that stock (computed, not run). |
| 5 | Resumed flight | **PASS** | Visit #30 began; the server was stopped with Ctrl+Break before `dropped`. After the restart: `visit #30 RESUMED after a restart … 04:57 left`, `merchant ZDO 1:38568 rebound`. Client `cargo status`: `parsed: visit Dropped #30`. |

**All seven checklist steps pass.** Not covered: the listen-host path of the distance check (`LocalTransport`),
a two-player trade, a sale at the clamped price after the roll, and the undo inside the whole-deal apply (off-game
only).

**Seen along the way, not caused by these fixes:**
- A second crossplay server on the same machine was started at 09:40. Storm10 lost PlayFab at 09:43 and the
  client timed out at 09:44; with the other server stopped, Storm10 was restarted at 09:47.
- Cosmetic: the distance log lines say "and a visitor is within 96 m"; they mean "must be within 96 m"
  (`Net/DealWire.cs`, `Net/CargoTransport.cs`).
- The walk-up to the player sticks on some visits (`stuck: no ground closed in 3 s`). This predates 0.1.5 and
  is Wu'barrk's walk-up.
