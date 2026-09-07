# BarrkBOT Exports — getting mod data onto Discord

**Overview:** BarrkBOT answers questions in Discord from files that Valheim mods write. This is a
cross-project contract, not a per-mod integration. On the live box: **5 mods, 7 files** —
BlightedHeart, Fatty, TortalPortal, TheRavensCall ×2 (`BarrkBOT_data1`, `BarrkBOT_data2`) and
Njord ×2 (`barrkbot_boats.json`, `barrkbot_sailors.json`). MistsOfAvalor is packed but not
deployed; WingsoftheValkyrie 2.0.1 is staged. **ShadowsOfMidgard publishes nothing** despite
appearing in older notes — its deployed 1.0.0 writes no export, and the version that would is
built but unreleased.

It is written down here because getting it wrong fails **silently** — a file with the wrong name is
picked up by nothing and errors nowhere, and the mod author has no signal at all that their export
is dead.

> ### Three states, not two
>
> ```
> written by the mod  ->  swept into BarrkBOT's cache  ->  read to answer a question
> ```
>
> Only the third answers anybody, and it is the furthest from the truth. A file can be current at
> step one and absent from step two. It can also be **present at step two having been deleted at
> step one** — `barrkbot_players.json` and `steve_export.json` were removed from the server on
> 2026-08-20 and their contents are still sitting in cache directories now. That one is harmless
> today only because `listCachedExports()` iterates the **manifest** rather than the directory, so
> an orphan is never listed and never served — a property of one function, not a guarantee. A
> cached copy of a deleted export is exactly the shape that once served 2.4 days of stale numbers
> with every field plausible and no error anywhere.
>
> **Do not read `WindowsDEV/Discord-BarrkBOT/data/` to answer "what is published".** That checkout's
> *source* tracks the deployed version, but its `data/` directory is a leftover cache from the
> pre-2026-08-22 G-Portal host and shows 7 files across 4 mods with no Njord at all. The live cache
> is on the box, under BarrkBOT's own home. Reading the dev checkout's cache and reporting it as the
> roster is a mistake already made once while writing this page.

**The single most important thing on this page:** if the numbers are measured client-side, the
server cannot see them, and BarrkBOT can only read the server. See "The client-side trap" below
before designing anything.

---

## 1. You do not write any bot-side code

`get_valheim_mod_data` already answers per-mod and per-player questions from **any** export,
shape-agnostically; `playerAcrossExports` cross-cuts one player over every mod. A new export is
answerable the moment the sweep picks it up.

Do not add a slash command for it. Do not ask BarrkBOT to store your data — it has a JSON store
and a SQLite corpus and **neither should hold mod data**. The contract is: the mod owns
persistence, BarrkBOT reads an export.

For lore, recipes and controls there is nothing to publish at all: `explain_valheim_mod` reads
the mod's own README off the server live, and that outranks anything BarrkBOT holds internally.
Keep the shipped README current and it is already the answer.

## 2. Where the file goes, exactly

From `Discord-BarrkBOT/src/actions/valheimModData.js`:

| | |
|---|---|
| Root | `/BepInEx/config` |
| Max depth | **2** below that root |
| Filename | `/^(?:barrkbot[_-][\w.-]*\|steve_export)\.json$/i` |
| Size cap | 4,000,000 bytes |
| Cadence | swept every **20 min**, scheduler ticks every 5 min (6.0.63; was 2h/30m before 2026-08-22) |

So `BepInEx/config/<ModName>/barrkbot_<thing>.json` is the shape to use. Put a **member-facing
word** in the filename — it is the cheapest way for a question to route to your export. MistsOfAvalor
renamed a file to say "labyrinth" and lost every question phrased as "maze"; that particular loss was
recovered the same day, because since BarrkBOT 6.0.60 an export's **own field names** are indexed
alongside its folder, filename, path and `source`, so "maze" now reaches it through `current_maze`
and `mazes_cleared`. The filename is still the cheapest hit and the advice stands — it is just no
longer the only one.

Since 2026-08-22 BarrkBOT and the Valheim server are the **same machine**
(`/home/wubarrk/WindowsShare/ValheimServer`), so reads are local. Do not put an HTTP listener in
a game mod; nobody wants one and it is not needed.

Write **temp-then-rename** (`File.Replace`, falling back to `File.Move` when the target does not
exist yet). The sweep runs on its own schedule and a half-written file is a parse error at best.

**Four filenames are already owned by dedicated readers** and are redirected rather than served by
`get_valheim_mod_data`: `BarrkBOT_data1.json`, `barrkbot_players.json`, `BarrkBOT_data2.json` and
`steve_export.json`. Only a concern if you pick a colliding name.

If you ever rename an export, **delete the old file on first write**. BarrkBOT serves a mod's
whole export tier and structurally cannot tell a rename corpse from a second, different export —
it will present the dead file as live.

## 3. The document shape

```json
{
  "generated_at": "2026-08-23T00:03:52Z",
  "source": "Wings of the Valkyrie 2.0.1",
  "schema_version": 1,
  "intervals": { "write_seconds": 60 },
  "session_started_at": "...",
  "<field>_notes": "guidance, not data",
  "players": { "<stable player id>": { "name": "Display Name", "...": 0 } }
}
```

- `generated_at` — ISO-8601 UTC **with a Z**. BarrkBOT trusts this over file mtime for staleness.
- `source` — mod name + version. Do **not** pad it with search terms.
- `intervals` — declare your real write cadence, but know that **nothing reads it today**. It sits
  in `META_KEYS` and is skipped as metadata; staleness is judged solely on `generated_at` against
  now. Keep sending it (it is harmless and may be used later); do not design around it being read.
- `session_started_at` — **only** if your counters reset on restart. Omit it for lifetime totals.
- `players` — keyed by a **stable id**, with the display `name` inside the row. This is what makes
  a row addressable by the only identifier anyone in Discord has, *and* what marks the map as
  per-player so BarrkBOT excludes those names from mod-matching vocabulary. Get it wrong and a
  player called "Bronzebeard" becomes an alias for a mod.

**Put units in field names.** `flight_time_seconds`, `distance_flown_meters`, `max_altitude_meters`.
A unitless number is the most reliable way to get a wrong sentence out of a right value.

Accumulated durations rank normally, but a field matching
`/(?:^|_)(?:write|census|interval|poll|sync|tick|export|refresh|update)_seconds$/i` is deliberately
excluded as configuration — "the leader by `write_seconds`" is a ranking of a settings value. So
`poll_seconds` stays out of leaderboards and `flight_time_seconds` ranks. Worth knowing before
naming a real counter something that pattern would eat: that exact exclusion silently swallowed
Njord's `helm_seconds` for the reader's whole life, making "who has spent the most time at the helm"
quietly unanswerable until it was fixed on 2026-08-22.

**Create rows on first activity; never pre-seed.** An empty `players` map means "not recorded
yet". It never means "nobody", and it is not a zero.

## 4. `_notes` keys — how to ship a caveat safely

Any key matching `/(?:^|_)(?:notes?|comment|remarks?|caveats?|warning)$/i` is lifted out of the
data and treated as attributed guidance (BarrkBOT 6.0.61+). This exists because a directive left
in an ordinary *value* becomes content: the model has been watched reading an instruction out to
a channel word for word.

Declare at minimum, wherever they apply:

- counters within your own mod that must never be summed into one total
- counters that share a name with another mod's but not a base — the classic is
  `distance_flown_meters` (WingsoftheValkyrie) against `distance_sailed_meters` (Njord); say
  explicitly that they must never be ranked or summed together
- any counter that will be mistaken for playtime
- that an empty map is "not recorded yet" rather than zero

```csharp
Str(sb, 1, "distance_flown_meters_notes",
    "Horizontal distance covered under this mod's wings only. It shares no base with any "
  + "sailing, riding or walking distance counter from another mod on this server: never sum, "
  + "rank or compare them together.");
```

## 5. Two constraints on the reader's side, verified in source

Neither is a reason to design your export differently. Both are things an author is better off
knowing than discovering from a member's wrong answer.

**A rendered tool result is capped at 6,000 characters** (`valheimModExports.js`). Width therefore
has a cost: WingsoftheValkyrie's 20-fields-per-player export rendered **9,951 characters for two
players** and was cut mid-ranking — the same shape of truncation that once answered "who has eaten
the most" with the *first* row rather than the largest, because the correct row sat past the cut.
Since 6.0.62 rows are fitted to a character budget rather than a row count, and above 8 ranked
fields the `leaders` table is dropped in favour of the prose sentence, which is what makes a wide
export fit. **Do not narrow an export to suit this** — that was BarrkBOT's explicit position, and
the reader is what gets to change. But an author shipping forty fields should know the shape of it.

**The stale threshold sits deliberately above the sweep interval, and a write cadence does buy
fresh answers.** As of BarrkBOT 6.0.63: `SWEEP_EVERY_MS = 20 * 60_000` and `SWEEP_TICK_MS = 5 * 60_000`
in `valheimModData.js`, against `STALE_AFTER_MINUTES = 60` in `valheimModExports.js`. So a file the
mod rewrites every 60 seconds reaches an answer within **about 20 minutes**, with room for one
missed sweep before anything is called stale, and `probes/sweep-freshness-check.mjs` asserts that
ordering holds.

This is worth knowing as history, because it was the reverse until 2026-08-22 and the reversal was
invisible: the threshold (60 min) was *shorter* than the sweep that refreshed it (2 h), inherited
from when the sweep was an FTP round trip to G-Portal and never revisited after Valheim moved to
local disk. BarrkBOT hedged about files written seconds earlier for roughly half of every cycle, and
Njord's sailors file was once 17 minutes old on disk and 250 minutes old in cache purely because one
sweep missed a write and then held the miss for two hours.

The rule that came out of it, now written into `valheimModData.js`: **the sweep interval must stay
below the reader's staleness threshold.** When it does, crossing 60 minutes means something is
genuinely wrong rather than that the clock ticked — which is the only way a staleness warning is
worth anything.

---

## 6. The client-side trap

**This decides the architecture, so settle it before writing a serializer.**

Anything measured by client-side physics — flight, glide, altitude, speed, stealth, input — is
simulated only on the peer that owns the ZDO, which for a player is normally that player's own
client. A dedicated server never sees it. And BarrkBOT can read the **server box and nothing
else**, so a mod that simply writes a file locally writes it on the wrong machine.

This is the same family as two findings already paid for elsewhere: `Character.OnDeath` does not
fire server-side (BlightedHeart, which had to move to `ZDOMan.m_onZDODestroyed`), and
TheRavensCall has a whole findings document on it.

When the data is client-side, the shape is: **clients report totals up over a routed RPC, the
server keeps the latest row per player, and the server writes the export.**
`WingsoftheValkyrie/FlightReport.cs` is the worked example. Points worth copying:

- Register keyed off the **`ZRoutedRpc` instance**, not a bool. The router object is rebuilt for
  every network session, so comparing instances makes joining a second world re-register by
  itself with no teardown patch to forget.
- `ZRoutedRpc.GetServerPeerID()` is **private**. `ZNet.instance.GetServerPeer().m_uid` is public
  and is the same number — reach through the public door rather than adding a reflection
  dependency on a name that can move.
- Short-circuit the RPC when `ZNet.instance.IsServer()` — solo play and a listen server are both
  "the server is right here", and on a world with no peers yet there may be nothing to route to.
- Register **before** consulting your send throttle, and before accepting a row locally.
  Registration is what notices a new session and clears the last one's state, so running it
  afterwards either resets the throttle it just consumed or discards the row it was just given.
- Persist the server's own row set to a **non-`barrkbot_*` file** beside the export
  (`flight_registry.dat`). Without it a restart blanks the export until every player happens to
  log in again; with a matching name, the sweep would see two files claiming the same facts.
- **The reported numbers are whatever the client says they are.** Fine for a logbook among people
  who know each other. Say so in the README, and never build anything on it that needs to be true.
- A client on the **previous mod version never reports and never appears**, silently. That makes
  any such feature a client-side update for everyone, not just a server update. Put it in the
  changelog.

## 7. Before shipping an export

Run the real serializer in a standalone harness and hand BarrkBOT the **actual JSON** for two
cases: an empty/fresh server, and a populated one. Its session feeds both through the live reader
and returns the sentences BarrkBOT would actually produce. That routine has found defects on the
reader side every single time it has been run — a field list finds none of them, because the
failures are in phrasing and routing rather than in structure.

`WingsoftheValkyrie/tests/FlightLogTests` is a working harness of this kind: it compiles the
mod's real `.cs` files against stubs for `Player`/`ZNet`/`ZRoutedRpc` and emits both samples.

## 8. Checklist

1. Is the data client-side? If yes, RPC to the server first — the server writes the file.
2. `BepInEx/config/<ModName>/barrkbot_<member-facing-word>.json`, depth ≤ 2.
3. `generated_at` (Z), `source` (name + version), `schema_version`, `intervals`.
4. `players` keyed by stable id, `name` inside the row, created on first activity.
5. Units in every field name — and check no real counter of yours matches the cadence pattern
   `(write|census|interval|poll|sync|tick|export|refresh|update)_seconds`, which is excluded from
   rankings as configuration.
6. `_notes` keys for every never-sum, never-rank and not-what-it-looks-like caveat.
7. Temp-then-rename; delete the old file if you renamed the export.
8. Generate both sample JSONs from the real serializer and send them to BarrkBOT for a read-back.
   Ask for the **rendered sentences**, not a schema opinion — the failures live in phrasing,
   routing and width, and none of them are visible in the JSON.

Related: [SharedInfrastructure.md](SharedInfrastructure.md), [HexiumPublishing.md](HexiumPublishing.md),
[WingsoftheValkyrie.md](WingsoftheValkyrie.md).
