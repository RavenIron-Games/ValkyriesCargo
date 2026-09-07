# The BarrkBOT contract — read before renaming or reshaping anything

**BarrkBOT reads these files off this server's local filesystem via its generic multi-mod scanner.
The filenames and the field names below are an interface, not internal state.** Changing either
breaks a program running in another repo (`WindowsDEV/Discord-BarrkBOT`) that has no way to find
out. The authoritative contract is `WindowsDEV/Discord-BarrkBOT/docs/MOD_EXPORT_CONTRACT.md` (v4);
this file follows its template (`BlightedHeart/BARRKBOT_CONTRACT.md`) and Let It Grow's
(`LetItGrow/BARRKBOT_CONTRACT.md`). **If this file and the source (`Server/BarrkBotExport.cs`,
`Core/BarrkExport.cs`, `Core/BarrkRollover.cs`) disagree, the source is right and this file is a
bug.**

**Shape-verified, not yet live-verified** (the authoritative contract's own two-tier distinction,
section "Two kinds of verified"). `dotnet run --project tests/CoreTests` proves the shaping, the
rollover and the ordering guarantee (98 checks, `TraderLedgerTests`, `VisitHistoryTests`,
`BarrkRolloverTests`, `BarrkExportTests`, `SidecarThenMirrorTests`), and a standalone run of the real
`Core/BarrkExport.cs` + `Core/BarrkRollover.cs` against Newtonsoft.Json produced real, well-formed
output — the 72-entry shipped catalogue renders to exactly **5 parts** (measured, not estimated: part
1 alone is 4,913 bytes and holds the first 15 rows before the 2,600-character row budget forces
`barrkbot_cargo_market_2.json`). What is **not yet proven**: that `Server/BarrkBotExport.cs` itself
ever executes on a running dedicated server, that `VisitDirector.Tick` actually calls it every
`ExportCadenceSeconds`, or that BarrkBOT's live scanner picks the files up. Nobody has installed this
DLL on a dedicated server and watched files land in `BepInEx/config/ValkyriesCargo/` yet — that is
the next thing to prove, the same way every item in CLAUDE.md's "What to verify in-game" is proven,
by pasting the log line here once it exists.

| Path | Written by | BarrkBOT reads it as |
| :--- | :--- | :--- |
| `BepInEx/config/ValkyriesCargo/barrkbot_cargo_market.json` (+ `_2` … `_5` at the shipped catalogue) | `Server/BarrkBotExport.cs` | the live catalogue: what Ingvar sells and buys, stock, price, trend |
| `BepInEx/config/ValkyriesCargo/barrkbot_cargo_traders.json` | `Server/BarrkBotExport.cs` | per-player trade totals this session |
| `BepInEx/config/ValkyriesCargo/barrkbot_cargo_visits.json` | `Server/BarrkBotExport.cs` | one row per visit that has ended this session |

`BepInEx/config/ValkyriesCargo.cfg` (the BepInEx config file, `Server.BarrkBotExport` among its
entries) is not part of this contract. Neither is the world sidecar
(`saves/worlds_local/valkyriescargo_{worldUid}.dat`) — see "Why there are three files, not one" below.

## The pipeline, in one paragraph

Valkyrie's Cargo is server-authoritative for all three collections (design section 2: only the
server owns the market), so the server is the one machine on which the market, the trader ledger and
the visit history are ever complete — there is no client-side counting and no merge step.
`VisitDirector.Tick` accumulates a `_sinceExport` timer alongside its existing `_sinceSave` one
(`Server/VisitDirector.cs`; no new timer, no coroutine — CLAUDE.md house rule 2), and every
`ExportCadenceSeconds` (60 s) it calls `Core/SidecarThenMirror.Run`, which runs `Flush` (the world
sidecar's own save, `.dat`, `.tmp`/`.bak` rotation) to completion first and only starts the JSON
export if that succeeded — decision 4 (`docs/DECISIONS-WUBARRK.md`), enforced in code, proven off-game
by `SidecarThenMirrorTests`. `Server/BarrkBotExport.cs` then shapes each collection through the pure
`Core/BarrkExport.cs` (what the fields are), paginates through `Core/BarrkRollover.cs` (the v4
rollover: real JSON widths in, `_2`/`_3`… parts out) and writes each file temp-then-rename
(`File.WriteAllText` to `.tmp`, then `File.Replace`/`File.Move`), so a sweep never reads a
half-written file. The `Server.BarrkBotExport` config entry (synced+locked, default on) gates the
whole thing; a throw anywhere in the writer is caught inside `BarrkBotExport.Write` itself (at most
three logged) and a second time by `SidecarThenMirror` around the call site — the sidecar's own save
is never at risk either way.

### Why there are three files, not one

Contract section 3: "publish as many files as the subject matter wants; split by subject." The
market, the traders and the visits measure different things at different scopes (see "The never-compare
rules" below) and would not fit one file's row budget together even before rollover — 72 market rows
alone need 5 parts.

## Envelope

Top level, every file: `schema_version` (4), `part`/`part_of`, `generated_at` (ISO 8601 UTC, the
staleness stamp — this is what BarrkBOT ages, not `intervals`), `source` (`"Valkyrie's Cargo
<version>"`), `intervals.write_seconds` (60). `barrkbot_cargo_traders.json` and
`barrkbot_cargo_visits.json` also carry `session_started_at` (when this `VisitDirector` came up); 
`barrkbot_cargo_market.json` does not — see "Market is not session-scoped" below. Each carries
`<collection>_notes` (guidance, lifted out of the data by the contract's own `*_notes` pattern) and a
`totals` block. `<collection>_leaders` appears **only when `part_of` > 1** (the v4 "hard rule": ranked
over the whole roster before the split, so a superlative is answerable from any one part) — for the
shipped catalogue that means `barrkbot_cargo_market.json` always carries `market_leaders`, and
`barrkbot_cargo_traders.json`/`barrkbot_cargo_visits.json` will once a server's roster or visit count
grows enough to split.

## `market` — a keyed record collection, keyed by prefab

One row per catalogue entry, always — unlike the other two, nothing has to happen first (`Core/Market.cs`
holds every catalogue entry from construction). Ordering follows the catalogue's own order (Wares
first, then Wants), which is why part 1 today is Wares only (measured: 15 of the catalogue's 18) —
part 2 crosses the boundary, ending its own first three rows on the last Wares before the Wants begin.

| Field | Meaning |
| :--- | :--- |
| `kind` | `"Ware"` (Ingvar sells it and buys it back) or `"Want"` (he only buys). |
| `purchasable` | `true` for a Ware. Emitted rather than left for the reader to infer from `kind` — a field both sides calculate is a field that eventually disagrees (the TortalPortal `at_cap`/`over_cap` precedent). |
| `stock`, `target_stock`, `max_stock` | Live stock and the catalogue's own target/ceiling. |
| `buy_price` | What he charges right now for one unit — shown for every row, even a Want (it drives `trend`), but only a `purchasable` row can actually be bought. |
| `sell_price` | What he pays right now for one unit. |
| `trend` | `-1` below its base price, `0` at it, `1` above it (`Market.Trend`). |
| `updated_at` | This export's own `generated_at` — see "Known imprecision" below; not the moment the stock last actually moved. |

`totals`: `catalogue_entries`, `wares`, `wants`, `purse_coins`, `active_visit_id` (`null` when no
visit is running — never `0`, which is a real visit id's neighbour, not "none").

### Market is not session-scoped

Stock and purse persist across a server restart in the world's own sidecar (`Core/Sidecar.cs`,
`docs/DECISIONS-WUBARRK.md` #4); this file is a live mirror of that, refreshed on the export cadence,
carrying no `session_started_at` because there is no session boundary for it to report against.

## `traders` — a per-player collection, keyed by platform id

New: nothing accumulated these totals before this file existed (`Core/TraderLedger.cs`). A row exists
only once that player has had a deal settle this session — an empty map means nobody has traded yet,
never that nobody is online (the same convention Let It Grow's `farms` uses for an untouched
Scarecrow). Key is the platform id `Net/DealWire.KeyFor` already uses for the owed ledger (stable
across reconnects; the peer uid is not).

| Field | Meaning |
| :--- | :--- |
| `name` | Repeated inside the row per the contract's per-player rule — the leaderboard prints raw ids without it. Updated on every deal; an empty incoming name never overwrites a real one. |
| `coins_spent` | Sum of `-CoinsDelta` over every accepted deal where the player paid. |
| `coins_earned` | Sum of `CoinsDelta` over every accepted deal where the player was paid. |
| `deals_settled` | Count of accepted deals (a barter that nets exactly 0 still counts — goods still moved). |
| `items_bought` | Units received from Ingvar, summed across every accepted deal's `ItemsToAdd`. |
| `items_sold` | Units given to Ingvar, summed across every accepted deal's `ItemsToRemove`. |

`totals`: `distinct_traders` and the five per-row numeric fields, each summed across every trader
(`total_coins_spent`, `total_coins_earned`, `total_deals_settled`, `total_items_bought`, `total_items_sold`).

## `visits` — a keyed record collection, keyed by visit id, newest first

One row per visit that has **ended** this session (`Core/VisitHistory.cs`); a visit still in progress
has no row (see `market`'s `active_visit_id`). Rows are emitted newest-first — a member asking "how
did the last visit go" is the common case, and rollover still preserves it (part 1 is always the most
recent slice).

| Field | Meaning |
| :--- | :--- |
| `pilot` | The chosen player's name at dispatch. |
| `started_at`, `ended_at` | ISO 8601 UTC. `started_at` is derived, not stamped independently — see "Known imprecision". |
| `duration_seconds` | Exact: `worldTime` at end minus `VisitClock.StartWorldTime`, and world seconds are real seconds here (the vanilla event's own `m_time`, paused only while nobody is near — `VisitSession`'s own doc comment). |
| `takings_coins` | `Market.Takings` at the moment the visit ended: coins gained this visit, never negative. |
| `ended_reason` | Free text from `VisitSession.End`: `"timer"`, `"dismissed by <name>"`, `"admin <name>"`, `"displaced by event '<name>'"`. |

`totals`: `visits_this_session`, `total_takings_coins`.

## The never-compare rules

1. **Coins are money; items are unit counts of different goods at different prices.** Never sum or
   compare `coins_spent`/`coins_earned` against `items_bought`/`items_sold` as if they measured the
   same thing.
2. **Deals are transactions; items are units.** One settled deal can move many units (`cargo deal buy
   Iron 20` is one deal, twenty items), so `deals_settled` is normally far smaller than either items
   total. They must never be compared as if they measured the same thing — the Let It Grow precedent
   (`crops_harvested` vs `items_swept_to_silo`) for this exact shape of mistake.
3. **`coins_spent` minus `coins_earned` is the one meaningful combination**, unlike 1 and 2: it is a
   player's net coins paid to Ingvar this session (negative means they are net ahead). Worth saying
   explicitly since it is the exception to "never subtract two counters," not another instance of it.
4. **A Want's `buy_price` is not an offer.** Every market row carries `buy_price` (it drives `trend`
   for both kinds identically), but only `purchasable` rows can actually be bought — quoting a Want's
   `buy_price` as a price a member could pay is wrong; it is priced against a fictional demand
   Ingvar never actually sells into.
5. **`market` and (`traders`, `visits`) answer different questions about time.** The market is live,
   continuous, sidecar-backed state with no session boundary; traders and visits are session counters
   that reset on restart. Comparing a `market` number's freshness claim against a `traders`/`visits`
   one is comparing a live mirror to a session tally — see `session_started_at`'s presence/absence
   above before treating the three files as one time base.

## Known imprecision, accepted, not bugs

- **`market`'s `updated_at` is the export's own `generated_at` for every row**, not a per-item
  last-changed stamp — `Core/Market.cs` tracks `UpdatedWorldTime` in world seconds, not wall clock, and
  every row is read at the same instant this export runs, so this is the honest value, not an
  approximation standing in for a more precise one.
- **`visits`' `started_at` is derived** (`ended_at` minus `duration_seconds`), not tracked
  independently. `duration_seconds` itself is exact (the visit's own world clock); only the derived
  wall-clock `started_at` can drift, and only across something that moves world time faster than real
  time relative to the visit's own span (a sleep skipped through mid-visit). `ended_at` is always exact.
- **If the world sidecar cannot be saved, the export is skipped that cycle too, on purpose.** Decision
  4 makes the JSON strictly subordinate to a successful `.dat` save, never a substitute for one
  (`Core/SidecarThenMirror.cs`); the previous export files simply stand, same as the previous `.dat`.
- **Up to `ExportCadenceSeconds` (60 s) of lag** between a real change (a deal, a visit ending) and it
  reaching disk, plus BarrkBOT's own 60 s sweep — the contract's own worst case, "the interval plus one
  30 s scheduler tick" (≈90 s), applies on top of that.
- **`traders` and `visits` reset to empty on every server restart.** They are not in the world sidecar
  and are not meant to be (docs/DECISIONS-WUBARRK.md #4 protects the sidecar's own zero-dependency save
  path; carrying these totals across a restart would mean parsing this mod's own JSON back in, and a
  parse defect then corrupts history silently forever — the same reasoning Let It Grow's
  `session_started_at` rests on).

## If you change any of this

1. Say so here, in `CLAUDE.md`'s Status section, and tell whoever is working on BarrkBOT.
2. Prefer adding a field over renaming one. A new field costs BarrkBOT nothing; a renamed one silently
   zeroes a feature.
3. If you must rename, write both names for one release, then delete the old one — a dead file that
   still parses is more dangerous than a missing one (TheRavensCall's contract, cited by BlightedHeart's).
4. Anything new that's pure belongs in `Core/` (`Core/BarrkExport.cs`, `Core/BarrkRollover.cs`,
   `Core/TraderLedger.cs`, `Core/VisitHistory.cs`) so `tests/CoreTests` can prove it before it ships,
   and prove it fails without its fix.
