# StormTest session, 2026-09-07 10:39–11:38 — Don's Windows client, six visits

The first session with the owner's own client on a dedicated server, on PR #46's build (main `af10fcf` plus the
probe rows). Every line the mod wrote is in `2026-09-07-stormtest-session.log.txt` beside this file (server first,
then the client's second boot; the client's first boot, 10:56–11:12, was overwritten by a game restart and its lines
are quoted here from the live watch). **No exception from the mod on either side, all session.**

| | |
|---|---|
| Server | StormTest, dedicated, stock Valheim 0.221.12, our DLL alone, world `Dedicated`, port 2476 |
| Client | Gale profile `Default`, the same 4,221,440-byte DLL, Steam id `7656…5778` (on the admin list) |
| Visits | 6 begun, 6 ended (3 timer, 3 dismissed: two by `cargo dismiss`, one by "Send him off") |
| Deals | 20 settled over the wire, 3 refusals logged (`purse_empty`), more counted |
| Purse | 800 → 644 → 3299 → 2128 (carry) → 260 → 0 → 924 → 1262 (carry) |

## What was proven, by item (`CLAUDE.md` "What to verify in-game")

Each item's line is now pasted into CLAUDE.md itself; this is the index. "Server half" means the server-side line
was seen and the client console's answer was not read (a client's console does not log; item 5's dumps and every
`cargo status` need a screenshot or the server window).

| item | state | the line |
|---|---|---|
| 2 | DONE (Windows) | `renderer=True, patches 18/18 applied, catalogue=72 entries, engine: same build … probes 18/18 ok, 7 not probeable` |
| 4 | sync half | `Received 31 configs and 2 custom values from the server for mod Valkyrie's Cargo`; a catalogue reset pushed `Received 1 configs` |
| 7 | server half | the roll reasons name the report: `1 not rested`, `1 comfort < 4`, `1 on cooldown` |
| 8 | DONE | natural: `roll: visit: Nomadtest at (-51.9, 57.3); 1 eligible, 1 ticket(s)` → `visit #1 begins`; forced: `admin Nomadtest (860278520): cargo visit` → `roll: forced visit: …` → `visit #3 begins`, and the client's `server answered: cargo visit Nomadtest: forced visit: …`. Banner not reported |
| 9 | pause half | `visit #3 ended … 8 clock republish(es)` (a relog mid-visit), `visit #5 … 11 clock republish(es)`; the sleep half not run |
| 10 | server half | `visit #1 ended: timer; takings 0 coins, purse 800, 0 clock republish(es), 0 owed deliveries`; banner not reported |
| 11 | admin half | `visit #2 ended: admin Nomadtest`, `visit #4 ended: admin Nomadtest`, client `server answered: cargo: visit #4 dismissed (admin Nomadtest)`; the non-admin refusal needs a second account |
| 12 | DONE | `roll: forced: Nomadtest not eligible: not rested; online: 1 not rested` and `… comfort < 4; online: 1 comfort < 4`, both echoed to the client |
| 13 | DONE | `deal w4790ce-3-1 with Nomadtest: bought 126 Wood at 1, coins +126 to the player; purse 674` / client `terminal deal on visit #3: ok w4790ce-3-1 -126 Wood, +126 coins`; buys `sold 1 FlametalNew at 110` then `sold 5 FlametalNew at 117` — the curve moved 110 → 117 = 110 × (6/5)^0.35, and black metal 60 → 76, flametal 162 with two left, all to the coin |
| 15 | rows half | sidecar 11:11:14: `stock Wood 326`, `purse 644`, `visit 3`, `seq 3`, `session 3 860278520 Nomadtest … Dropped`; `cool` rows after every visit. The restart-mid-visit half was Wu'barrk's run |
| 16 | purse half | `deal refused for Nomadtest: purse_empty` twice, then `further refusals are counted, not logged`; `sold_out` and `stale_visit` not seen |
| 17 | log half | `terminal opened: visit #1 (demo)`, `ok demo-1-1 +1 Honey, -2 coins`, `ok demo-1-2 +49 Honey, -98 coins`, `terminal closed: inventory`; the window itself not described yet |
| 18 | DONE | `terminal opened: visit #3 on Dverger(Clone)`, `terminal closed: use`, sells, buys, barter (`ok w4790ce-5-3 +50 Honey, -12 Silver, +236 coins`; a four-line one that drained the purse to exactly 0 and was accepted), `terminal closed: escape`, `terminal closed: sent him off` with the server's `VCargo_dismiss from Nomadtest: visit #5 dismissed (dismissed by Nomadtest)` |
| 21 | log half | `cargo flight #1: flying from … 75.186 m out at 8 m/s, turning 45 deg/s (radius 10.1859159 m)`; `dropped at (…) after 16.60019 s` — six flights at 16.60 / 16.96 / 16.02 / 16.02 / 16.72 s against a simulated ~16.8; visits 4 and 5 took the shrink path (`straight in 78 m out`, `49.5 m short`). The glide on screen and a second client not reported |
| 23 | deal + visit rows | traders on the NEXT cycle: `{"76561198392625778":{"name":"Nomadtest","coins_spent":0,"coins_earned":156,"deals_settled":3,…}}`; visits: `"1":{"pilot":"Nomadtest","started_at":"2026-09-07T18:00:15.244Z","ended_at":"2026-09-07T18:05:15.244Z","duration_seconds":300,"takings_coins":0,"ended_reason":"timer"}`. The switch and BarrkBOT reading still open |
| 24 | DONE, both sides | `probes 18/18 ok, 7 not probeable` on the server and the client boot lines; the moved-version direction still open |
| 26 | all but "waits" | `cargo catalogue add Ruby:40:15:45:Ware` → `catalogue applied: 72 entries; 72 kept, 0 added, 0 dropped; purse 800, next visit #3`, the cfg rewritten with `Ruby:40:15:45:Ware`; again → `the line is already exactly that, nothing changed`; `Nonsense` → `this game has no prefab named 'Nonsense' (names are exact, and case matters)`; `Boar` → `'Boar' is a prefab but not an item (no ItemDrop), so it could never be delivered`; `cargo catalogue reset` → `catalogue reset to the shipped catalogue (docs/CATALOGUE.md); catalogue applied: 72 entries; … purse 924, next visit #6`. The add-during-a-visit answer not tried |

Beyond the list:

- **The Fair Market Act, live.** With bronze and iron shelves EMPTY he paid 10 and 17 — 70% of base, the clamp — not
  the tripled buy-back the curve would give.
- **Both drift knobs, live.** Bronze, iron, silver, black metal and flametal at 0 at the end of visit 3 were still 0
  at the start of visit 4 (Wares never drift); Wood went 326 → 322 in the same gap, which is exactly what a
  three-game-day half-life gives for the world time that passed.
- **The carry on gross coins.** Visit 4 opened at 2128 = 800 + ½ × 2655 (the coins IN during visit 3, not the net
  2499); visit 6 at 1262 = 800 + ½ × 924.
- **A relog mid-visit.** The pilot quit at 11:12:05 and rejoined at 11:14:33 with visit 3 running: the persistent
  merchant survived, the new client adopted him `awake as trading, ours, body=Ingvar`, the leash walked him back
  (`reached the player`), the terminal reopened on him, and the clock had paused with nobody near.
- **Ingvar's body on a Windows client**, every visit: `body: dressed Ingvar in a copy of the Dverger's own material`,
  `Ingvar attached to 'Dverger(Clone)' at local y 0.05`.
- **The walk-up finished on every visit** — the first times ever — but never on the first attempt (below).

## Defects found

**D1 — the first approach after the drop never starts cleanly (every visit, 6/6).** The client logs
`trading (entered: gave up walking after 20 s)` with `the walk-up did not finish. moved N m in 20 s (budget scaled
from 0 m at entry; stuck 0 s of the last window) and stopped M m away; … grounded no/yes`, then the trading leash
fires (`the player left: … for 5.02 s`), and the second approach reaches the player every time. The `0 m at entry`
is the signature: `CargoMerchant.ResolveCarrier` copies `VCargo_state` off the ZDO straight into `_state` every
physics step, and that path skips the bookkeeping `Decide` does on a plan-driven change (`_timeInState`,
`_approachMoved`, `_distanceAtApproachEntry`, `_progress`). WHEN it fires varies — at the drop on visits 1–3
(`_timeInState` still holding the flight's 17 s), never on visit 4 (dismissed after 64 s), 55 s after the drop on
visit 5 (598 m moved), 107 s after on visit 6 (144 m moved) — which says the ZDO write itself lands late or not at
all on some visits, i.e. the bird's handoff (`CargoFlight.Drop` → `FindMerchantByCarrier`) is part of it, not only
the timer. Not answered from the logs: whether she lets go over the drop point or carries him off first. Proposed:
route the ZDO-driven state change through the same entry reset; do not count the approach budget while airborne;
one log line at the ZDO-driven transition so the next log answers the rest.

**D2 — the per-player cooldown is keyed on the session uid.** After two relogs the sidecar holds three `cool` rows
for one player (`-794915846`, `860278520`, `73796573`): `Scheduler` keys on `zdo.GetOwner()`, the peer's
connection id, which `PLAYER-IDENTITY-FACTS` §1–2 says is exactly the wrong long. A relog clears a player's cooldown.
Key on the platform id (what the owed ledger and the export already use) or `s_playerID`.

**D3 — the deferred reclaim is not reclaiming; the sweep is.** Every visit end logs `restart sweep: 1 stranded
merchant(s) destroyed` 6 s later, so `Spawner.ClearIfStillOurs` found nothing to reclaim and F4's belt-and-braces
sweep did the work. Candidate: `Reclaim`'s take-then-destroy on a pilot-owned ZDO (`DestroyZDO` is a no-op for a
non-owner, `zdo_bodies`). The result is right; the path is not the one intended, and the line says "restart" at
every end.

**Wording, no behaviour:** `admin wire registered for ? (0)` on every connect (the peer's name and uid are not known
at socket time; the identity is the socket anyway); the CLIENT logs `session ended: sidecar flushed; director …
dropped` on leaving a world (a listen-host phrase); the server's `visit #N: dropped` trails the client's by ~6 s
(the ZDO sync cadence, not a bug).

## Not yet reported from the screen

The glide and the release (does she let go over the drop point?), how far he falls, the Odin vanish on the five
dismissals, the callout bubble and the hover prompt, the terminal's look, the walk and one-shot clips on a
merchant, daylight, and whether the base sits on a slope. The item-5 dumps (`cargo prefab …`) can be typed into the
StormTest server window, whose console output is logged.
