# Changelog

## 0.1.5

### 0.1.5 — NAME TBD, date TBD; a cleanup release, prepared and not cut yet

**Why a number.** The store takes one upload per version number, and the version gate moves with it: **a 0.1.4
client is refused by a 0.1.5 server**, so the server and every player update together. No gameplay code changed
since 0.1.4, and the config file's layout did not change either (`ConfigVersion` stays `2`), so updating a server
from 0.1.4 needs nothing deleted and migrates nothing.

- **The DLL no longer carries the build machine's folder path (PR #103).** Every DLL through 0.1.4 had the
  absolute path of its debug-symbol file written into it, and that path included the build machine's user name.
  The build now maps the repo's folder to a neutral `/_/` in the DLL and its PDB alike, so neither names a local
  path. The compiled code is the same as 0.1.4's; only that recorded path changed. A build still embeds the commit
  it was built from, so the DLL's md5 follows the commit, no longer the folder it was built in. This DLL was built
  from commit `COMMIT TBD`.
- **The GitHub README has a "Support Raven Iron" section (PRs #101 and #102):** the mods are free and stay free, and the
  section links the Raven Iron website, Patreon and the Discord. The README's line on what the store carries is
  brought up to date.

## 0.1.4

### 0.1.4 — cut 2026-09-16 (the day's second), the config migration; a pre-release on GitHub, the store upload is the owner's

**Why a number.** The store takes one upload per version number, and this changes what the config file holds and
how the catalogue is stored. **A 0.1.3 client is refused by a 0.1.4 server** by the version gate, so both sides move
together. **Updating a server from 0.1.3 or earlier: nothing to delete any more** — the first boot on 0.1.4 migrates
the file itself, as below; 0.1.3's delete-the-file note is history.

### The config migration (PR #99, merged 2026-09-16 at the owner's word)

**From 0.1.4 the mod migrates its own config file; the "delete the old config file first" step retires.**
0.1.3 and earlier told a server owner to delete `com.raveniron.valkyriescargo.cfg` on every update, because
BepInEx keeps a stored value across the update and a stored value beats a new shipped default - that is how
Wonderland kept the 72-row `Catalogue` line from 0.1.0 across two updates and never saw the 29 food rows
(2026-09-16). Deleting the file threw away everything an admin had set on purpose. From 0.1.4 the mod reads
the file itself, before any BepInEx bind, and decides what to do with every value already on it:

- A value still equal to one of the mod's own OLD defaults belongs to the mod and moves to the new default.
- Anything else is real admin work and is kept, unchanged.
- The whole file is backed up beside itself first, `<file>.v<N>.bak` (`N` the version it was migrating FROM) -
  never overwritten: a second migration of the same file writes `<file>.v<N>.<timestamp>.bak` instead, so a
  half-finished earlier run cannot destroy the only clean copy. A backup that could not be written at all
  leaves the retired key in the file and the version unstamped, so the migration retries next boot rather
  than losing the admin's line with no copy anywhere.
- A failed migration never stops the mod loading: every other value binds as it always did; the retired
  `Catalogue` line is not read that boot (the shipped catalogue plus `CatalogueOverrides` is in force, and the
  log says so), and `ConfigVersion` is left unstamped so the next boot retries rather than treating the
  failure as done. A retry never overwrites overrides an admin has set from the console in the meantime.
- One boot line says what happened, e.g. `config: version 0 -> 2: Catalogue was the 0.1.0 default, moved to
  the shipped 101 rows; overrides: none` or `config: version 0 -> 2: Catalogue was customised, kept as 3
  override(s): Ruby changed, FlametalNew added, Honey removed`. `cargo status` carries the short form; the new
  `cargo config` verb (anyone, local) carries the whole thing.

The shape is Wu'barrk's `ConfigMigration.cs` from WingsoftheValkyrie (the family's, alongside TortalPortal and
Fatty), adapted here: `Config/ConfigMigration.cs` (the engine-facing half) and `Core/ConfigLedger.cs` (the pure
decisions - a stamped `Meta.ConfigVersion`, currently `2`, and a rebase table keyed by the version it produces).

**The catalogue moves to an overrides model that never needs migrating again.** `Server.Catalogue` (the whole
101-row line) is retired; `Server.CatalogueOverrides` replaces it and holds only what an admin actually
changed - `Prefab:BasePrice:TargetStock:MaxStock:Kind` to add or change a shipped row, `-Prefab` to remove
one, comma-separated, empty by default. The shipped catalogue itself stays code (`docs/CATALOGUE.md`,
`Core/Catalogue.DefaultLine`) and can grow forever - the next 29 rows, whatever they are - without a stored
line ever shadowing it again. `Core/CatalogueOverrides.cs` (pure) is `Apply` (shipped + overrides -> the
catalogue that actually trades) and `Derive` (an old stored `Catalogue` line -> the overrides that reproduce
it, for the migration). `cargo catalogue add|remove|reset` now edit `CatalogueOverrides`, not the whole line;
`cargo status`'s catalogue line reads `catalogue: 101 shipped + <overrides in words>; N entries (...)`. The
retired `Catalogue` key is consumed from the cfg file once the migration has read it - bound under a
throwaway default and removed again, both public `ConfigFile` API, after the backup exists - so BepInEx
does not keep rewriting it forever (BepInEx's own orphan-tracking, `ConfigFile.OrphanedEntries`, is a
private property; nothing here touches it directly).

Tests: `ConfigLedgerTests` and `CatalogueOverridesTests` in `tests/CoreTests/Program.cs`, including the
round-trip invariant (`Apply(Parse(DefaultLine), Derive(stored, HistoricalDefaults, DefaultLine))` reproduces
every row a stored line actually had, food rows the admin never saw included).

**Seen on Storm10, 2026-09-16 10:42–10:48, three headless boots of the branch head** (the record:
`docs/proofs/2026-09-16-storm10-session.md`, the migration section): a synthesised Wonderland-style file (the 0.1.0
72-row `Catalogue` line with Ruby's numbers changed, Honey and Amber removed, Thistle added, no stamp) booted to
`config: version 0 -> 2: Catalogue was customised, kept as 4 override(s): Ruby changed, Thistle added, Amber removed,
Honey removed`, the file stamped 2 with exactly those overrides and no old line, the purse untouched, a byte-identical
`.v0.bak` beside it, and the market export carrying all 29 food rows next to the admin's changes; the same file booted
again as a no-op; Storm10's own 0.1.3 file migrated with empty overrides.

**Which build ran.** The three boots were on the branch head `0.1.3+10bc018`; the record's commit changed documents
only, and no `.cs` file changed from there to this cut. A 0.1.4 build booted on Storm10 at the cut: `Loading [Valkyrie's Cargo 0.1.4]`, `v0.1.4 loaded - renderer=False, patches 19/19 applied, catalogue=101 entries, engine: same build 1.0.12 (net 40, player 46, world 41); probes 19/19 ok, 8 not probeable` (10:58, no client, no migration line because the file was already at version 2, director up at visit #23).

## 0.1.3

### 0.1.3 — cut 2026-09-16, the visit clock; a pre-release on GitHub, the store upload is the owner's

**Updating a server from 0.1.2 or earlier: delete the mod's old config file first** (the owner, 2026-09-16).
That is `BepInEx\config\com.raveniron.valkyriescargo.cfg` on the SERVER, removed while the server is down, so
the first boot on 0.1.3 writes it fresh with this cut's defaults: the new `Server.PauseVisitWhenEmpty` line and
the 101-row `Catalogue`. A stored config file keeps its own values across an update, and a stored line beats the
shipped default: on Wonderland the 72-row `Catalogue` line from 0.1.0 outlived two updates and hid the 29 food
rows until the owner found them missing (2026-09-16). Anything set on purpose in that file (`PurseCoins`, the
roll, the flight, the shelf) goes with it and wants setting again afterwards; `cargo catalogue reset` as an admin
takes the shipped catalogue alone, if that is all a server needs. A client's copy holds nothing the server does
not overrule, and can stay.

**Why a number.** The store takes one upload per version number, and this changes a behaviour that shipped: when
a visit ends. **A 0.1.2 client is refused by a 0.1.3 server** by the version gate, so both sides move together.

- **The visit clock runs whoever is near him (PR #97; 2100 checks, 7 new).** The owner's finding, 2026-09-16: a
  visit whose pilot walked off or logged out never ended. The vanilla random event a visit rides was registered
  with `m_pauseIfNoPlayerInArea` on, and the engine adds no time to such an event while nobody is within 96 m of
  it; Valheim runs one random event at a time, so an open visit also held every raid and every later visit
  (`held: a random event is active`). Now the event registers with that flag OFF: the clock runs from the moment
  the visit begins, on the server's own real time, and Ingvar leaves `MerchantLifespanSeconds` later whoever is
  there or not — with nobody online too, the merchant reclaimed server-side with no vanish seen. The one
  exception is the **new synced option `Server.PauseVisitWhenEmpty`** (default off): an empty server holds the
  clock, so a visit started at bedtime is still there in the morning. "Empty" is the engine's own player count,
  the one it freezes the world clock on; a listen host counts itself, so the option never engages there, and a
  lost connection keeps its peer counted until the engine's 90 s reconnect timeout drops it. Logged on change:
  `visit #N: clock paused: nobody online (Server.PauseVisitWhenEmpty)` / `clock running: K online`; the
  registration line reads `runs whoever is near him (Server.PauseVisitWhenEmpty holds it on an empty server)`.
  With it: the server no longer republishes the countdown while nobody is online (it had re-sent it every
  second to nobody); a visit's recorded duration (`duration_seconds` in the BarrkBOT export) comes off the
  event's own clock as the server last saw it, so an admin's `stopevent` records the truth; `docs/PROOF-CLIENT.md`
  item 9 tests the new clock instead of the pause; the ZNet probe learns `GetNrOfPlayers`. **Seen on Storm10,
  2026-09-16:** option off, visit #21 — the pilot logged out at 07:33:44 and with nobody online the log carries
  `visit #21 ended: timer` about 300 s after `begins`; option on, visit #22 — `clock paused: nobody online` on
  the tick the engine dropped the lost peer (07:50:46), six empty minutes with no end line, `clock running: 1
  online` on the pilot's return (07:56), then `one minute left` and `visit #22 ended: timer … 0 clock
  republish(es)`. The record: `docs/proofs/2026-09-16-storm10-session.md`, with the log extract beside it.

**Which build ran.** The live runs were on the branch's first and second commits (`0.1.2+19cb8bb`,
`0.1.2+4333134`); its third commit changed the recorded duration and the option's description text
(`Server/VisitDirector.cs`, `Config/ModConfig.cs`) and has not run live; nothing else in `.cs` changed from
there to this cut. A 0.1.3 build booted on Storm10 at the cut: `Loading [Valkyrie's Cargo 0.1.3]`, `v0.1.3 loaded - renderer=False, patches 19/19 applied, catalogue=101 entries, engine: same build 1.0.12 (net 40, player 46, world 41); probes 19/19 ok, 8 not probeable` (08:08, no client, the sidecar's 110 rows loaded, next visit #23).

## 0.1.2

### 0.1.2 — cut 2026-09-15 evening, the merchant guard; a pre-release on GitHub, the store upload is the owner's

**Why a number and not a second 0.1.1 zip.** The store takes one upload per version number (the owner,
2026-09-15: "we can't upload another 0.1.1"), and the gate follows the number: **a 0.1.1 client is refused
by a 0.1.2 server** with the mod's own message naming both. Nothing about the gate changed, and that direction
has still not been watched on a machine (rc5's proof ran the mirror pair, the other branch of the same gate).

- **The merchant guard (PR #94; 2093 checks, nothing off-game reaches it).** On Wonderland the same afternoon
  (Valkyrie's Cargo 0.1.0 on both sides, Wu'barrk's DvergrAllies 1.0.7 in the set) the owner's screen gave
  vanilla's tame-follow on the use key ("Ingvar the Far-Travelled follows you") and no terminal, with
  "(Female)" and "Hungry" on his hover; his client's log carries the visit reaching `trading` and no
  `terminal opened` line under it. DvergrAllies puts a `Tameable` subclass, a `Procreation` and a genetics component on
  every Dvergr prefab, on every process that loads it; Ingvar is a clone of that prefab; `Player.Interact` takes
  the FIRST `Interactable` in component order, and a prefab component always precedes ours, which is added at
  runtime. Two pieces, both in our files. `Client/MerchantGuard.cs` runs on the client the moment our clone
  wakes, before `CargoMerchant` is added: it removes every `Tameable` and `Procreation` of any subclass and the
  three DvergrAllies components by type name (no reference to that mod), with `DestroyImmediate` because our own
  `SetTamed` fires in the same frame and a taming mod's `SetTamed` patches key on its genetics component; and it
  writes Shadows of Midgard's frozen opt-out key (`SoMStealthExempt = 1`) on the owner's machine, inert without
  SoM. `Patches/Patch_Player_Interact.cs` is the general case: a use on anything carrying `CargoMerchant` goes
  to `CargoMerchant` (`Priority.Low`, `__runOriginal` honoured, vanilla's gates and the hold throttle kept), so
  no `Interactable` another mod puts ahead of ours ever takes the key. The two string-named engine members it
  reaches are in the `merchant` probe, and the boot line counts `patches 19/19` (was 18). **One change for every
  player, taming mod or not:** the interact animation and the snap-turn are not played on Ingvar any more, the
  way the game's own traders behave. **Seen on Storm10, 2026-09-15 18:51–18:55, visit #18, with DvergrAllies,
  Jötunn and JsonDotNET on both the server and the client:** both boots printed DvergrAllies' `injected
  Taming/Breeding components into 8 wild Dvergr prefabs`; on Ingvar's wake, `merchant guard: removed
  DvergrAllies.DvergrTameable, DvergrAllies.DvergrProcreation, DvergrAllies.DvergrGenetics from Ingvar's clone;
  SoMStealthExempt stamped`; then `terminal opened: visit #18 on Dverger(Clone)` twice, two deals through it, the
  visit sent off from the terminal, the vanish and the reclaim; nothing from DvergrAllies about him afterwards;
  and the owner's word on the hover: "text is fine". Not covered, and said so in the code: a merchant adopted
  across a server restart gets no SoM stamp (no instantiating client owns him then; SoM tests "not tamed" first,
  and he is tamed by us), and the one-line authoring-time stamp is proposed for `Spawner.cs`. The record is
  `docs/proofs/2026-09-15-storm10-session.md`, the last section, with the excerpt from both logs beside it.

**Which build ran.** The guard's live run was on `0.1.1+3285704`, the branch's own build; no `.cs` file changed
between that commit and this cut (the version number, the manifest and the documents did), so the shipped
build differs from the tested one by its version string and nothing else. A 0.1.2 build of it booted on Storm10 at the cut (`Loading [Valkyrie's Cargo 0.1.2]`, `v0.1.2 loaded …
patches 19/19 applied, catalogue=101 entries … probes 19/19 ok`), the taming mods taken off both sides first; the zip's
DLL is the same code rebuilt from the tagged commit, and the two differ only in the commit id a build embeds
(`0.1.2+<sha>`) and the header ids derived from it.

## 0.1.1

### 0.1.1 — cut 2026-09-15, the first cut with its own version number; a pre-release on GitHub, the store upload is the owner's

**Why the number moved.** Every cut through rc5 was `0.1.0`, so the ServerSync gate
(`MinimumRequiredVersion == CurrentVersion`) could not tell one from another and a client on an older
cut joined a server whose config it did not know. From this cut on the version moves with the cut (the
owner, 2026-09-15: "we need to start updating versions as well"): an rc5 client is refused by a 0.1.1
server with the mod's own message naming both numbers. That direction has not been watched on a
machine: rc5's version-wall proof ran the mirror of it, a 0.1.1 client refused by a 0.1.0 server — the
same two numbers with the builds swapped, the other branch of the same gate. Nothing about the gate
changed.

- **The store page and the release documents caught up with rc5 (PRs #76, #77, #78; 2026-09-11; 1987
  checks).** Wu'barrk's store page became the standard one, every claim on it brought to 1.0.12 (four
  advertised settings that did not exist are gone); the dependency string that ships is
  `denikson-BepInExPack_Valheim-5.4.2350` and the two dead manifests under `HexiumDist/` are deleted; the
  six places that still said "uploaded to no store" say it is published.
- **Issue #79, "Little man shows up at the Bogwitch" (PRs #80, #82, #84; 2026-09-14 to 15; main at 2047
  checks after #84).** Vanilla's base value is a count of PlayerBase-flagged effect areas within 20 m with
  no ownership test, so a comfortable spot in the Bog Witch's camp passed every gate. Three cuts, each
  proven where the previous one broke: **(1) somebody built here** — at least one player-placed piece
  (`Piece.IsPlacedByPlayer`, anyone's build, not only yours) within `Server.BuiltBaseRadius` (20), reported
  by the client on its own ZDO the way comfort is; **(2) not at one of the game's locations** — as first
  written this refused every location the game owns, and the first live test refused the owner's own base
  on a Meadows `WoodHouse3` ruin, so it was re-cut to name its reasons; **(3) one kind per location, in
  order** — a `Trader` anywhere in it makes it a merchant's camp (Haldor, Hildir, the Bog Witch, any modded
  one; no name list), else an interior makes it a dungeon's door, else the game's own map-icon flag
  (`m_iconAlways || (m_iconPlaced && m_placed)`, `ZoneSystem.GetLocationIcons`'s test verbatim) makes it a
  landmark (the Sacrificial Stones, the boss altars). Each kind has its own switch — `Server.AvoidMerchantCamps`,
  `AvoidDungeonEntrances`, `AvoidLandmarks`, all on — and because a camp is a camp first, turning the merchant
  switch off really does open the camps (the rule-2 review caught the first landmark cut refusing them as
  landmarks). The location's own exterior radius plus `Server.LocationClearance` (8 m) is what "at" means; a
  ruin, a runestone, a stone circle never counts. **The engine fact underneath, found live:** a dedicated
  server never instantiates a location root — `Location.GetLocation` answers none there — so the server
  reads the zone registry (`ZoneSystem.m_locationInstances`, 12,204 entries on the test world) and learns
  what is inside a location off the prefab asset (`SoftReference.Load/Asset/Release`, hence the new
  `SoftReferenceableAssets.dll` reference), while a client reads the instances and cannot see the map-icon
  flag at all (`cargo status` says so instead of guessing). A refusal names the place: `inside Hildir_camp,
  a merchant's camp`; the server logs once per boot what its world pins on the map; every forced visit logs
  the raw engine answers beside the verdict. **Seen on Storm10, 2026-09-15:** refused beside Hildir's camp
  (`r=24 d=30.9`), at a crypt door (`Crypt3`, interior) and — once the third switch was in — at the
  Sacrificial Stones (`StartTemple r=25 d=32.2`); flown in from 90 m and set down at a base on a ruin (visit #11); a second ruin base's flight was
  authored and dismissed before the drop (visit #12).
  Every gate is a `[Server]` switch; `RequireBuiltBase=false` alone puts the pre-#79 behaviour back.
- **Every `cargo` console line reaches the client's log (PR #81).** On a dedicated server
  `Terminal.AddString` already logs each line; on a client it did not, so `cargo status` and the server's
  answers to `cargo visit` (`server answered: …`) are now mirrored as `console: …` into
  `BepInEx/LogOutput.log`, where a bug report can quote them.
- **BarrkBOT contract v4: `market_not_achievements` (PR #83; main at 2040 checks after #81 to #83).** The bot
  ranks any keyed-record collection since 6.0.92 and credits "any non-numeric scalar in the row", so a
  market row's `kind` was being posted as "Ware now leads stock on the Sap". Per the contract's Achievements
  section, every part of the market export now declares its six numeric fields as not achievements; a pure
  constant, held to the row's numeric fields by reflection in the harness.
- **Food and drink in the catalogue: 29 rows, 72 → 101 (PR #85; 2069 checks).** Nine he sells (boar jerky,
  bread, minced meat sauce, serpent stew, cooked lox meat; the medium healing, medium stamina, frost- and
  poison-resistance meads) and twenty he buys (berries and mushrooms, carrot, turnip, onion and their seeds,
  boar, deer, neck, wolf, lox, serpent, chicken and hare meat, raw fish). Bases are the value column of
  `docs/data/item-values-2026-09-08.tsv`; targets follow the house pattern; `Max = 3 × Target`. Drafted at
  39 and cut to 29 by an economy review whose findings are `docs/CATALOGUE.md` §8: Haldor's 1,500-coin egg,
  the lox-pie cook-and-sell loop, the yield-1 pricing of cauldron crafts, two dishes with broken bases, the
  cooked twins that pay what their raw pays, four meads that could drain a purse — all out. The harness's
  catalogue-vs-item-list test grew by the rows; the economy sim's counts and shelf-fairness bounds now come
  from the catalogue. **An existing server keeps its stored `Catalogue` line across an update** (BepInEx's
  stored value wins over the shipped default): `cargo catalogue reset` as an admin, or edit the cfg while the
  server is down, to take the rows.
- **One row per item token, and deals keyed by prefab (PR #86; main at 2093 checks).** The game's inventory
  counts and removes goods by the item's shared token (`m_shared.m_name`), and two prefabs can carry one:
  `FishRaw` and `FishAnglerRaw` are both `$item_fish_raw`. Two catalogued prefabs on one token would have let
  a player sell forty of the cheap one at the dear one's price. Now the catalogue carries one row per token
  (the first in catalogue order keeps it; the server drops a later row when it applies the line and says so;
  `cargo catalogue add` refuses one and names the holder), and the client's deal applier matches a stack by
  the prefab it knows it came from (`ItemData.m_dropPrefab`, set on pickup, on load and on every add),
  removing stack by stack through `Inventory.RemoveItem(ItemData, amount)` and putting back exactly what came
  out if a pack is short. **Seen on Storm10, 2026-09-15, visit #13:** seven deals settled line for line on the
  server and applied line for line on the client, including 40 hides bought in two deals and all 40 sold
  back in three through the prefab-keyed removal, and two of the new food rows traded; the add refusal
  answered naming `FishRaw` and the token.
- **Rule 2 of the working agreement (CLAUDE.md):** working agents on Sonnet, sub-agents on Haiku, reviews on
  Opus, never the session model. The Opus reviews of #84 and #85 each found a real thing (the camps refused
  as landmarks; the exploit above).

Off-game at the cut: **2093 checks, 0 failed, 0 warnings**; the eleven-scenario economy simulation passes and
`docs/ECONOMY-SIM.md` is regenerated from it. Not re-run for this cut: the version wall and the non-admin
refusal (the gate and the admin path are untouched since rc5; the wall proof was the same two numbers with
the builds swapped, a 0.1.1 client refused by a 0.1.0 server, so the direction this cut creates is a code
reading, not an observation). **Redelivery is still proven off-game only**; #86 rewrote the apply step it
runs through, and the seven live deals proved that step, not redelivery itself.

**Which build ran.** The live work ran on `main` at `ad6c334`, before the version bump — every Storm10 boot
that day logs `v0.1.0 loaded`. No `.cs` file changed between `ad6c334` and the cut (the version number, the
manifest and documents did), so the shipped build differs from the tested one by its version string and
nothing else. A build of it from `4ff22fc` booted on Storm10 at the cut (`Loading [Valkyrie's Cargo 0.1.1]`,
`v0.1.1 loaded … catalogue=101 entries … probes 19/19 ok`) and was stopped without a client joining; the
zip's DLL is the same code rebuilt from the tagged commit, and the two differ only in the commit id a build
embeds (`0.1.1+<sha>`) and the header ids derived from it. The day's lines are in
`docs/proofs/2026-09-15-storm10-session.md`.

**The zip refreshed 2026-09-15 afternoon, same version number.** After the tag, visit #14 on the released build
showed that the purse carry between visits did not survive a server restart: the sidecar kept the last visit's
gross, but the director only learned it when a visit ended, so the first visit after any restart opened at
`PurseCoins` alone. PR #91 seeds it from the sidecar at boot (one line; reviewed on Opus, merge as-is); proven on
visits #15 and #16 — 1638 coins in, a graceful stop and relaunch, the next visit opening at 100819. The store
zip was rebuilt from `main` with that fix in; the version gate is unchanged, 0.1.1 on both sides. Also in the
day's proof document: visit #14 proved the deal applier's removal across two stacks (60 Blackwood in one deal),
the one path nothing off-game exercises.

## 0.1.0

A Valkyrie drops Ingvar the Far-Travelled beside your base at a random moment when you are rested and
comfortable. He walks up, calls out, buys and sells from a live persistent stock at supply-and-demand
prices for five minutes, and vanishes the way Odin does. Every player sees the same visit; only the
server owns the market.

**What has been seen on a screen, and what has not.** On 2026-09-07 the owner's Windows client ran six
visits against a dedicated server, then three more that evening on the audit's fixes: the flight and
the drop within a second of the simulation every time,
Ingvar in his own body, the walk-up (finishing on every visit, but never on the first attempt -- the
walk-up defect below), the terminal opened on the merchant, twenty deals with the price curve, the
Fair Market Act, both drift knobs and the purse carry correct to the coin, dismissals, a relog mid-visit,
and no exception from the mod on either side; the record is `docs/proofs/2026-09-07-stormtest-session.md`.
Proven off-game across 1701 checks and a ten-scenario economy simulation. Never yet seen: every fix
merged since the audit (its six, then D1 to D4 and the half-turn; `docs/AUDIT-STORMTEST-2026-09-07.md`
§5 says what would exercise each), the
two-client items, and the screen questions (the release over the drop point, the callout bubble, the
hover prompt). On 2026-09-08 the owner watched two of them: **the arrival banner works, and the vanish plays
but is not timed perfectly** (`Spawner.VanishGraceSeconds`, Track B's). The runbook is `docs/PROOF-CLIENT.md`
and what remains is listed in CLAUDE.md's "what to verify in-game". Treat 0.1.0 as a first playable, not as
a settled one; **the owner's word on 2026-09-08 after the day's five merges: no release yet.**

Entries are in build order, except the five sections directly below: 0.1.0's newest work, added
after the rest of this log was written.

### 0.1.0-rc5 — cut 2026-09-11, the Valheim 1.0.12 build; **the first cut to reach a store**

- **Valheim 1.0.12, two days after 1.0.7 (2026-09-11).** Swept both axes with our own diff-engine over
  freshly decompiled trees: 273 of 278 surface rows unchanged, 4 bodies changed, 1 signature changed,
  none gone, client and server agreeing exactly. **No mod code needed changing.** The one signature
  change is the NETWORK VERSION, 39 to 40, and it is the game's own wall: `ZNet.RPC_PeerInfo` refuses
  any peer whose number differs, so **`v0.1.0-rc4` cannot connect to a 1.0.12 server or client at all**
  and this cut replaces it. Player 46 and World 41 did not move, so no save migrates. `EngineBaseline`
  and its tests moved together, as they must.
  **And a fact we had recorded that is now false:** on 1.0.7 `ZNet.ListContainsId` ended with
  `flag = list.Contains(FilterPlatformUserID(id))`, so the filtered `V_` match OVERWROTE an earlier bare
  or `Steam_` hit and an admin list holding the older forms stopped admitting anyone. **1.0.12 made it
  `flag |=`.** The earlier match survives, all three forms admit, and a list that 1.0.7 broke works again
  with no edit. Corrected in `AdminGate`, the surface and CLAUDE.md, because a wrong recorded engine fact
  is worse than none.
  The four changed bodies, from the sweep itself: `ZNet.RPC_PeerInfo` and `ZNet.ListContainsId` are the two
  above; `Terminal.InitTerminal` gained a vanilla command, and `Terminal.ConsoleCommand` moved its
  `HideBehindDevCommands` test out of `IsValid` and into `ShowCommand`, which changes what a non-admin sees
  listed and not what the gate admits. Our console registers with neither `isCheat` nor `HideBehindDevCommands`,
  so none of it reaches us.

- **`Server.CarryOffset`: how close he hangs to the talons (branch `a/carry-offset`, 2026-09-11, 1984 checks).**
  Asked for on the screen: Ingvar hangs further under the Valkyrie than he should. The number was never ours to
  set - the pin takes it from the `Valkyrie` prefab's own `m_attachOffset`, `(0, 0.3, 0.4)` on the shipped bird,
  and vanilla tuned that to carry a full-height player through the intro rather than a merchant a head shorter.
  `Core/CarryOffset.cs` (PURE) is the parse and the sanity bound; `CarryPinLive.cs` is the one read, the twin of
  `ActiveAreaLive`. Empty follows the prefab, which is what every build through rc4 did, so nothing moves until
  the value is set. It is synced and LOCKED because the pin runs on every machine that has him instanced and
  every screen must agree, and `CargoMerchant.ResolveCarrier` re-reads it every physics step, so a change pushed
  from Configuration Manager lands while he is still in the air. Text that is not three numbers, or a component
  past 5 m, is refused with one log line and the prefab's offset used - never a fail-closed `(0, 0, 0)`, which
  would stand him inside the bird's foot on every screen. `cargo status` names the offset and where it came from,
  and `cargo prefab Valkyrie` now prints `attachOffset` beside the attach point, which it never did.
  **Two lines in Track B's files, at Don's word and flagged here**: `CargoFlight.AttachOffset` and the no-flight
  fallback in `CargoMerchant.ResolveCarrier` both call `CarryPinLive.Read` instead of naming the constant.
- **And the number it ships with: `0, 0, 0`, his feet on the talon. SEEN ON A MACHINE 2026-09-11** (Storm10,
  Valheim 1.0.12, visits 2 to 4). The owner joined, forced a visit and walked the offset down from
  Configuration Manager **while the bird was in the air** - sixteen config pushes across three visits, each one
  landing on the merchant within a physics step, none refused: the prefab's `(0, 0.3, 0.4)`, then
  `(0, 0.2, 0.25)`, then this. So the whole designed path is proven on a machine as well as off it: an admin
  client writing through a LOCKED synced config, the server taking it, and `ResolveCarrier` re-reading it every
  physics step. The default is the tuned number rather than empty, so a fresh install gets the framing that was
  actually looked at; empty still follows the prefab for anyone who wants vanilla's.

### 0.1.0-rc4 — cut 2026-09-10, the Valheim 1.0.7 build (PRs #70 and #71, plus #66 from the night before, which rc3 did not carry); a pre-release, uploaded to no store

- **The 1.0.7 sweep's second half, and Splatform.dll in the tools (branch `a/rc4-prep`, PR #71).** The 25 body
  changes the sweep left unread on the server axis and the 2 more on the client axis were read, one verdict each,
  in `docs/engine-sweeps/2026-09-10-1.0.7-bodies-read.md`: **every one holds**. Two are worth knowing beyond
  that: `ZNet.ListContainsId` (the `V_<steamid>` lists, found live the same morning) and `ZNet.RPC_PeerInfo`,
  where an invite secret key now bypasses the server password. `Splatform.dll`, the assembly `PlatformUserID`
  lives in — on 0.221.12 too (92 types), never decompiled until now; 1.0.7 added the filter (104 types) — is the
  fourth assembly in `tools/decompile-builds.*` and `tools/diff-engine.js` (a build without it is skipped with a
  line), with four surface rows on it: on the server axis `TryParse` reads as a body change (it accepts the `V_`
  form now) and the filter and its table as new; the 0.221.12 client tree predates the change and cannot be
  re-cut, so those four read "absent from <from>" on the client axis until the next sweep starts from 1.0.7.
  No code change.

- **Valheim 1.0.7 (branch `a/valheim-1.0`, 2026-09-10, 1956 checks).** Steam moved both installs to the
  release on 2026-09-09 (client build 25185596, server 25185644; network 39, player 46, world 41; Unity
  6000.0.75). Swept from here the same day, both axes, the P10a tools unmodified
  (`docs/engine-sweeps/2026-09-09-{server,client}-0.221.12-vs-1.0.7.md`): the two playtest stop-ships
  did not ship (`GetStableHashCode` is one-argument again, `GetAllCharacterZDOS` has no early return); the
  0.221.12 build of the mod broke in six places on the release, all fixed here at the owner's word (the
  1.0.7 publicized assemblies handed over): `Hoverable` gained `GetHoverOffset()` (CargoMerchant could not
  load at all — the `interfaces` probe's quiet failure, exactly as written; one method in his file, his
  character's value); `ZoneSystem.m_activeArea` / `m_activeDistantArea` are gone and the active area is
  the synced simulation distance with a metre test behind `InActiveArea` (`Core/ActiveArea.cs`,
  `ActiveAreaLive.cs`; `ZoneOwnership` and `FlightPlan` rebuilt on it, the same 3x3 block on a stock
  server; the descent-slide loop removed, its bound proven in the harness instead); `ZRoutedRpc.Everybody`
  a `const`; `MessageHud.ShowMessage`, the `ConsoleCommand` constructor and `EffectList.Create` each with
  a new optional parameter (recompiled, the probe rows re-pinned); `Version.c_*` in place of `m_*`
  (EngineCheck reads the new names; EngineBaseline carries 1.0.7). One new body fact,
  `active_area_rule`; `libs/` is the 1.0.7 set and 0.221.12 is no longer a build target. The offline
  probe tool on both 1.0.7 assemblies: `same build 1.0.7 (net 39, player 46, world 41); probes 19/19 ok,
  8 not probeable`. **Seen on a machine 2026-09-10** on Storm10, a fresh 1.0.7 dedicated server, with the 1.0.7
  client: both boot lines as above with `patches 18/18 applied`, a forced visit, the flight dropped after 14.8 s,
  `2 reclaim(s) during the carry`, the walk-up, the terminal, two deals settled line for line, the admin dismiss
  answered with `0 clock republish(es)`, the vanish with the smoke — nothing thrown on either side. Found on the way:
  1.0's admin, permitted and banned lists want the display id `V_<steamid>` (a filter in the new Splatform.dll
  overrides the old bare and `Steam_` match); the docs carry the table.

### 0.1.0-rc3 — cut 2026-09-08 evening, after the two-client session (PRs #54 to #68); a pre-release, uploaded to no store

- **D5, the ownership loss during the carry (PR #54, Track B, 1718).** `HoldTheCarry` claims the merchant back
  every physics step while he hangs from the talons, on the pilot's client only (gated on owning the bird, which
  the engine's sweep never touches); both log lines say who holds him (`ours|watching (owner N)`) and the
  transition line counts the reclaims. **Seen the same night on StormTest, three visits from three directions:
  `ours`, 2 / 2 / 0 reclaims, and the first approach reached the player every time — the first times in fifteen
  visits, so D1, F5 and D5 are all seen.** The trigger is still open: the reclaim counts by direction do not fit a
  sector strip around a standing pilot, and a first-reclaim line is asked for. Also seen that night: D3's clean
  end with no sweep line on every end, D4's wires registering once the identity arrived, and that F3's two-second
  grace before the reclaim does not show in the log (0 s on three of four ends; Track B's file). The record is
  `docs/proofs/2026-09-07-stormtest-night.md`.
- **The sidecar's save path resolved by name, and the 1.0 sweep (PR #55, Track B, 1722).** `Server/WorldSavePath.cs`
  finds `World.GetWorldSavePath` (0.221.12) or `SaveSystem.GetWorldsSaveRootPath` (1.0) and reads
  `FileSource.Local` by name, because the method moved and the enum's values did; the new `save_path` probe reports
  it (19 probes). The sweep against the 1.0 playtest is `docs/engine-sweeps/2026-09-08-server-0.221.12-vs-0.221.13.md`:
  two stop-ships, neither fixed (the one-argument `GetStableHashCode` is gone; `GetAllCharacterZDOS` returns empty on
  a dedicated server). Verified from Track A's machine against the fetched playtest build. **1.0 work is held by the
  owner.**
- **The rotating shelf (branch `a/rotating-shelf`, Track A, 1818 checks; issue #56, the owner's 2026-09-08 change,
  Wu'barrk's two-day default).** The fixed Ware list goes away: `Server.ShelfSize` (20) entries of the whole 72 are
  on sale at a time, chosen by the pure `Core/Shelf.cs` from the world's salt and a period index that moves every
  `Server.ShelfRotationGameDays` (2) game days, returned in catalogue order. On the shelf an entry trades as a Ware
  did (sold at the curve, bought back at par under the Fair Market Act, the Ware half-life); off it, as a Want
  (bought only, the Want half-life). `Market.KindOf` is the one place that decides, and every snapshot row carries
  the effective kind, so the terminal's panes follow with no client change; a buy of an off-shelf entry is refused
  with the new `not_on_shelf` and a line of Ingvar's for it. The director re-rolls on the first idle tick of a new
  period, never under a visit (`shelf rolled: …` / `shelf roll waits: …`), and republishes the market; nothing is
  persisted, so a restart mid-period shows the same twenty. `0` keeps the old fixed shelf. `docs/CATALOGUE.md` §7,
  `docs/ECONOMY-SIM.md` §11. **Seen on StormTest 2026-09-08**: `shelf roll waits: visit #17 is running`, then
  `shelf rolled: … period 14 …` naming the twenty, `shelf now: …` after a restart, visit 18 resumed across it.
- **The terminal after the first playtest (branch `a/terminal-ux`, Track A, 1841 checks; Wu'barrk's report of
  2026-09-08, items 4, 5 and 7; the owner: "build 4, 5 and 7 on a branch").** Click-per-unit staging was unusable
  and the Coins/Barter switch unreadable, so: every staged line carries a **count box** (digits only; a number
  above his stock, your carry or the room on his shelf is written back clamped), an **all** button (a ware: as many
  as he has and you can pay for; goods: everything you carry that fits) and **x**; Shift 5 / Ctrl 20 stay. The
  switch and `PayMode` are gone: the tray is always **YOU GET** beside **YOU GIVE** with one balance line
  (`you pay N c` / `he pays you N c` / `even`), and "Cover it with my goods" shows whenever a ware is staged;
  `EnableBarter=false` now refuses goods beside a ware on the client (`barter_off`, "Coins for my wares on this
  shore, friend"), since there is no mode to hide. The window sits on the theme's near-black panel at
  `Client.TerminalBackdropAlpha` (0.4 = the 40 % translucent black asked for) with both text tones a step
  brighter, all through `ThemeOptions` — the vendored theme is not edited. While a count box has the keyboard the
  text-focus token is raised (from Tick) and Use/Tab/M do not close the window; Enter or a click elsewhere hands
  it back. `TrayModel.SetCount` / `AllOf` / `Remove` are pure and checked. **Not seen on a screen.**
- **He never walks while a terminal is open on him, and the hover localises (branch `a/merchant-busy`, 1862 checks;
  issue #59 items 2 and 3, taken from Track B at the owner's word 2026-09-08).** The playtest saw him walk off with
  two players trading. The server already counted open terminals on the deal wire; now `VisitState` carries the
  count as an OPTIONAL 13th field (a 12-field string from a side that is behind still parses, the format version
  does not move; never in the sidecar row), the director copies the wire's count every tick and republishes when it
  moves, the wire forgets a peer that drops with its terminal open and clears at the visit's end. On the merchant's
  owner, `busy` = the local terminal or the server's count for this visit; `MerchantPlan.Next(..., busy)` holds the
  leash while it is true and `AccumulateFar(..., busy)` does not bank seconds behind the hold, so a close cannot
  fire it on stale time. One log line each way: `cargo merchant #N: a terminal is open on him (K on the wire): the
  leash holds` / `no terminal open on him: the leash is armed again`. `GetHoverText` now passes through
  `Localization.instance.Localize`, so `$KEY_Use` reads as the bound key. What this does NOT settle: which
  transition walked him — the log lines from the walk-off are still asked for on #59; vanilla's idle shuffle is
  bounded to 1.5 m by `ReassertLocal` and would show no state change at all. **Not seen on a screen.**
- **The backpack add-on, the shelf half (branch `a/backpack-shelf`, Track A, 1831 checks; Wu'barrk's design of
  2026-09-08, the owner's "take the shelf multiplier on a branch").** A server running Smoothbrain's Backpacks
  (`Server.BackpackModGuid`, shipped `org.bepinex.plugins.backpacks`, changeable live) sells from a shelf of
  `ShelfSize × Server.BackpackShelfMultiplier` (shipped 2, range 1–4; capped at 200 and at the catalogue): players
  who can carry more get more to buy. `Server/BackpackMod.cs` reads BepInEx's chainloader on the server at
  director up — before the market is sized, so nothing re-rolls a tick later — and once a second after;
  `Shelf.Scaled` is pure (0 stays the fixed shelf; the scaled shelf for a period is a superset of the unscaled
  one, so the mod arriving swaps nothing out). One log line at director up says what was found and what it does
  to the shelf, and `cargo status` repeats it. The backpack on his body is the other half, and Wu'barrk's.
  **Not seen on a machine.**
- **The body goes with the smoke (branch `a/vanish-hide`; the owner's eyes 2026-09-08: the vanish was LATE, taken
  from Track B at his word).** He stood in his own despawn smoke until the server's `Clear` landed,
  `Spawner.VanishGraceSeconds` (2 s) after the RPC; vanilla `Odin.Update` creates the effect and destroys in the
  same frame. The grace stays (it is what lets the RPC land before the ZDO goes): `CargoMerchant.RPC_Vanish` now
  calls `HideForGood` on every screen it reaches — every renderer under him off, every LOD group off, the collider
  off, the AI stood down on the owner — and logs `cargo merchant #N: into the mist: K renderer(s) off with the
  smoke; the Clear follows in 2 s`. Nothing of ours enables a renderer, so he stays gone. **Seen on visit 20,
  2026-09-08, on the owner's dismiss: `into the mist: 1 renderer(s) off with the smoke`, and his word for the
  screen: "vanish looked great."**
- **The dismiss follows him (branch `a/dismiss-at-merchant`, 1885 checks).** Visit 21 (2026-09-08): after the leash
  walk he stood 134 m from his drop point, and the server measured "Send him off" against the DROP POINT: refused
  three times while the client's terminal closed on "sent him off" and said the farewell on trust. Now the event's
  area follows him once he is down (`CargoEvent.Follow`; `Server/VisitAnchor.cs` reads his live ZDO position, the
  drop point only while none is bound), so the vanilla clock no longer pauses beside a trading player (that pause
  retargeted the deadline every 2 s tick and republished VisitState: the client's `Received 0 configs and 1 custom
  values` line every 2 s); the deal wire measures a dismiss against him (the refusal line says `from the merchant`)
  and ANSWERS it on `VCargo_dismissed` (`ok` / `too_far` / `stale_visit`); the terminal waits on that answer
  ("Sending him off", `So be it. I wait on the Allfather's word.`), closes with the farewell on `ok`, shows his words
  for a refusal, and says `No word came back on that. Ask me again.` after 4 s of silence. Shift+E on him stays
  fire-and-forget (interact range is inside the 96 m by construction). SEEN on visit 22 (2026-09-08): after the leash
  walk a Shift+E dismiss was taken first try, `VCargo_dismissed: ok` on the client, **0 clock republishes** (543 on
  visit 21) and the ServerSync line 8 times over the visit instead of every 2 s. The terminal button path itself is
  not yet seen on a screen.
- **More than one ware per deal, and Confirm lit only when the deal can go (branch `a/multi-wanted`, 1924 checks).**
  The owner's ask after the two-client session (visits 23 and 24). The deal's wanted side is a list on the wire like
  the offered side (a single line encodes as before, so a one-ware deal still parses on a side that is behind; a
  two-ware deal fails that side's parse loudly; the same ware twice is refused at parse); `Deal.Wants` beside
  `Offered`, `Wanted` the first line for the code that had one; `Market.Settle` checks every wanted line in the
  design's order and refuses the deal whole on the first that fails; the tray's GET side is a list (`MaxWantedLines`
  8) with the count box, "all" (which pays for the other lines first) and x on every line, both wells drawn the same
  way. The Confirm button is enabled only when the tray's own Validate passes, run on every draw, and his reason
  for a no shows dim beside the balance line before anything is pressed; its lit face is the theme's bright gold
  (the owner on the first screen: "confirm has to be brighter"). SEEN on visits 25 and 26 (2026-09-08, PR #68
  merged): one deal of five wares taken and three kinds given, `w4790ce-25-1 … coins +572 to the player`,
  settled and applied line for line; the owner: "love it."
- **The walk-off inside Trading (branch `a/follow-assert`, Wu'barrk's diagnosis on issue #59, his file at the
  owner's word).** His visit-16 client log has no leash transition at all: Ingvar entered Trading at 25 s and was
  still in it 185 s later, so #61's busy hold (which holds the LEASH) never covered the walk-off he saw. His read:
  a stale `MonsterAI.m_follow` — set only while Approaching, cleared only on a Trading entry the owner's own
  Decide drove, a plain non-replicated field that beats the patrol point in `UpdateAI` — on a machine that
  re-acquires him mid-Trading. Built: the follow target asserted every tick including the null, the patrol point
  set on EVERY Trading entry in `EnterState` (the ZDO path included; owner-gated, it writes the ZDO; `GetPatrolPoint`
  re-reads it every second so it reaches the next owner), the Decide-path duplicate gone, and a watcher never
  steps back below Leaving on a stale ZDO read (his vanish flap, two lines 0.12 s apart). Caveat kept on the
  issue: his own log shows the pilot's client driving both transitions with a constant owner, which clears the
  follow, so visit 16 is not proven to be this mechanism; a combat target walks him off the same way. Needs two
  clients to see. 1885 checks, no pure change.

### 0.1.0-rc2 — cut 2026-09-07 at the end of the day the first visits flew (PRs #24 to #53)

`v0.1.0-rc1` was cut at 09:37 that morning and **must not reach a tester**: it carries the F1 blocker
fixed three hours later. `v0.1.0-rc2` replaces it, cut from `main` after PR #53 the same evening with
everything below in it; the store zip and the bundle are its release assets, it is a pre-release, and
it is uploaded to no store. Both cuts are version 0.1.0, so the ServerSync gate does not tell them
apart: an rc1 client connects to an rc2 server and brings F1 with it. Replace every copy by hand.

- **F1, the blocker (PR #30, 1427 checks).** The immortality prefix on `Character.RPC_Damage` returned
  `false` whenever no merchant was instanced, so with no visit running nothing in the world could take
  damage. `Immortality.RunOriginal` now splits "somebody cancelled" from "no visit".
- **The authority audit and the config shakedown (PR #24, 1302).** `VCargo_admin`'s caller is the
  socket, never a field in the packet; a dismissal is accepted only from a player at the visit; three
  client-driven paths are bounded. Two documents.
- **Ingvar eats the customer's coins (PR #25).** The consume list; and the walk-up timeout logs a
  diagnosis instead of failing silently.
- **The boot-time engine probes (PR #28, 1423; PR #46, 1697).** `Core/EngineProbes.cs` registers 25
  named engine facts; 18 are probed at boot against the running assembly and 7 method bodies are
  registered as not probeable. The version line says what the DLL was built against and what is
  running; a failed probe turns off the part that depends on it and says so, instead of throwing. The
  `RPC_Damage` probe gap found by Track B is closed; every probe was resolved against the real
  `assembly_valheim` 0.221.12 offline, six wrong-signature mutations each fail their own probe, and
  against the previous stable build (0.221.4) the version line reports all four numbers as moved while
  the 18 probes still resolve. `docs/ENGINE-PROBES.md`.
- **`event valkyries_cargo` from the vanilla console starts a real visit (PR #34)** instead of being
  killed within a second.
- **Never a bare `PatchAll` (PR #35, 1470; issue #31).** Every patch class is applied on its own,
  failures are named and counted (`patches N/M applied` in the boot line, the failures in
  `cargo status`), and a ServerSync failure refuses the mod. Decision 9 (PR #36): the load-bearing set
  stays ServerSync only; the flight gets a middle tier through `PatchLedger.IsApplied`.
- **F11, ghost mode (PR #37, 1481).** Hostiles neither target nor fear Ingvar: one prefix on the static
  `BaseAI.IsEnemy`, the decision pure.
- **F2 and F6 (PR #38, 1488).** The hover prompt draws (`GetHoverText`/`GetHoverName` postfixes); the
  immortality covers damage-over-time (a second prefix on the four-argument `ApplyDamage`).
- **F4 and F3's server half (PR #39).** An adopted merchant is rebound after a restart; the server
  sends the vanish and waits two seconds before reclaiming.
- **No JSON library, and the built DLL untracked (PR #40, 1522).** `Core/Json.cs` writes the BarrkBOT
  files byte-identical to Newtonsoft's output (4028 comparisons); the DLL and the bundle ship as release
  assets, not in git. Decision 3 records its own reversal (PR #43).
- **F7 and F8 (PR #41).** Unknown ground leaves the flight altitude alone instead of climbing; the
  `Awake`-ordering hole is closed with the `m_initZDO` fallback.
- **F5, F3's merchant half, F9, F10 (PR #42; the three fix PRs trial-merged together at 1571).** The
  approach budget scales from the distance at entry with a 3 s stall detector and a leash that fires
  once; the arrival line is broadcast, not drawn locally; `LiveCount` is right when `Awake` throws;
  `Reassert` splits into a local half and an owned half.
- **The catalogue verbs and the hot swap (PR #44, 1660).** `cargo catalogue list|add|remove|reset`
  from an admin client; the shelf is rebuilt as soon as no visit is running, and a prefab the game has
  no item for is dropped with its reason.
- **Two drift knobs (PR #45).** One half-life per kind: Wares never drift, Wants relax over three game
  days; the over-max hole it uncovered is closed.
- **The first session on a Windows client, and its audit (PRs #46, #47).** Six visits on StormTest;
  the proofs record, and the audit of what it found: the walk-up's first approach never starts cleanly
  (D1, the ZDO-driven state change skips the entry reset -- so F5's scaled budget has never run on a
  machine; a proposed diff, Track B's files), the visit-end sweep double-counts a reclaim that works
  (D3), the eleven merged fixes against the logs.
- **D2 and D4 (PR #48, 1701).** The per-player cooldown is keyed on the character's `s_playerID`, not
  the per-join session uid, with the two probe rows that go with it; the admin and deal wires register
  a peer once its identity has arrived; a client's session-end line no longer claims a sidecar.

- **D1's entry reset (PR #50, Track B).** The ZDO-driven change to `approaching` runs the same
  `EnterState` as Decide's own, so F5's scaled budget runs on the first approach at last; the
  transition is logged with the carrier's state, the distance to the player, whose client owns him and
  the budget, and the drop's silent miss is a warning.
- **D3's deferred sweep (PR #51, Track B).** The belt-and-braces sweep runs one director tick after
  the reclaim, so a reclaim that works is no longer counted as a stranded ZDO; a clean end logs the
  reclaim and no sweep line.
- **The half-turn at the attach (PR #53, 1701).** Ingvar walked backward on every live visit: his body
  is a half-turn off the carry frame. `Client.BodyYawDegrees` (local, default 180) turns him at the
  attach and in the preview.

Known and open at this cut: **the pilot's client loses ownership of the merchant during the carry.**
On the last visit of the day (visit 9, on this build) the drop happened with the merchant no longer
owned by the pilot's client, so no Decide ran and Ingvar never woke; every late give-up of the session
reads the same way (`docs/AUDIT-STORMTEST-2026-09-07.md` §1.3). The fix is in Track B's file and is not
in this cut. D1's entry reset, D3's deferred sweep and the half-turn are built and have not yet been
watched on a screen.

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
- **The merchant used to fail its own setup, silently.** `MonsterAI.MakeTame()` opens with
  `m_character.SetTamed(true)`, and `BaseAI.m_character` is not assigned until `BaseAI.Awake` -- which
  has not run yet when our `Humanoid.Awake` postfix adds the component and calls it. Vanilla threw, and
  the catch already in `Awake` turned that into `enabled = false`: every merchant this mod ever
  authored, before the fix, stood inert with nothing driving it. Found on the first in-game visit,
  2026-09-07; the fix skips `MakeTame()` on that first, Awake-time call and lets the next `Reassert`
  (0.5 s later, once `BaseAI.Awake` has actually run) make it instead.

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
  `VCargo_dropped`); `MerchantEnabled` held the merchant back when this PR merged, until P5 landed -- it
  authors both now, and both are reclaimed on any visit end. `Patches\Patch_Valkyrie_Awake.cs`: vanilla
  `Awake` skipped for our bird only. `Client\CargoFlight.cs`: the owner flies vanilla's own maths and
  writes the velocity key so every other screen sees a glide.
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

**A client has since run.** 2026-09-07, listen host (client and server on one machine), across two
sessions: the client boot line and `cargo status`'s numbers (the runtime `m_activeArea`, the
catalogue, the comfort report, the day length off a client's own `EnvMan`) are no longer headless
claims, and a saved visit resumed after a restart and later ended on its timer. Ingvar's own body
loaded on a real merchant, and the five bake defects noted in "Phase 8" above were all seen fixed.
The same run found a real defect, now fixed: the merchant's own `MakeTame()` call threw on `Awake`
(see "Phase 4-5" above), so every merchant before the fix stood inert. Ingvar himself gave up walking
after 20 s and called out from where he stood rather than finishing the walk-up -- the designed
fallback, not the outcome anyone wants.

**Still not seen on a screen.** The rest of `CLAUDE.md`'s "What to verify in-game": the version wall,
the config lock, the `cargo prefab` dumps, `cargo visit`'s banner and the pilot's line, the clock
pausing and resuming across the 96 m rule, `cargo dismiss`, a non-admin refused, an ineligible player
refused, a deal over the wire with the price moving on every machine, the owed ledger and a
redelivery after a disconnect, the sidecar after deals, the refusal reasons, `cargo terminal demo`,
the terminal on a real visit, the flight's glide and drop, Ingvar in daylight and his walk and
one-shot clips, and the BarrkBOT files landing on a dedicated server.

**Everything is in this release now.** The authored flight (P4), the merchant (P5) and the baked,
embedded bundle merged together (PR #22); see "Phase 4-5" and "Phase 8" above. `v0.1.0-rc1` is
tagged as a prerelease -- the store upload has deliberately not happened. See `docs/DESIGN.md`
section 9 for the order and `docs/WORKSPLIT.md` for who owns what.
