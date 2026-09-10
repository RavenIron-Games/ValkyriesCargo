# TODO — Valkyrie's Cargo, cut 2026-09-07 after the 0.1.0-rc1 merge

Three tracks, one per owner. **Each track edits only its own section.** An item leaves the list by the
PR or the log line that closes it, named in the checkbox. Sources are the documents the items came
from; when this file and a source disagree, fix the source first, then this file.

**Cut from:** `main` at 8453b65 (PR #22 merged by Wu'barrk, v0.1.0-rc1 tagged, the store zip attached
to the release and **not uploaded**). One live visit run, on Wu'barrk's client: Ingvar landed in his
own body, gave up walking after 20 s, called out from where he stood. Nothing watched end to end.
Don's three branches (`a/p10a-sweep`, `a/p10b-probes`, `a/p11-shakedown`) unmerged and conflicting
with main. PR #17 open. Issue #16 done but open. Issue #23 is Wu'barrk's rc1 note.

**Decided 2026-09-07 (owner):** the bake is Wu'barrk's machine's. Don's side does not install Unity;
every bake reaches Don as a release asset (section 2, first item).

---

## 1. Don — the client, the servers, the decisions

### Decisions (the owner's; nobody else's to take)

- [x] **The JSON dependency.** DECIDED 2026-09-07: the swap. The two serializer calls in
      `Server/BarrkBotExport.cs` go through the pure `Core/Json.cs` (a writer, never a reader, shaped like
      Newtonsoft's output); the `<Reference>` and the `ValheimModding-JsonDotNET` manifest entry are gone.
      BepInExPack only, the locked row stands. Built on Track A (section 3).
- [x] **The tracked DLL.** DECIDED 2026-09-07: stop tracking. `HexiumDist/plugins/` is gitignored, the
      tracked copy deleted, payloads are GitHub release assets beside the bundle; the two blobs already
      in history stay. Built on Track A (section 3).
- [x] **Catalogue edits by an admin.** DECIDED 2026-09-07: build the `cargo catalogue list|add|remove|reset`
      verbs and a hot swap of the live market between visits; **no sell-only kind**. Built on Track A
      (section 3).
- [x] **How long the shelf remembers.** DECIDED 2026-09-07, from the half-life sweep (`docs/ECONOMY-SIM.md`
      §10): **two knobs, wares never, wants 3** — `WareHalfLifeGameDays` 0 (a Ware keeps what trading left;
      what he sells is what players sold him and what an admin's target says) and `WantHalfLifeGameDays` 3
      (he keeps buying; a flood half-clears in 1.5 real hours). `StockHalfLifeGameDays` is gone. Built on
      Track A (section 3). With the target as the lever that persists, a `cargo stock set` verb would be an
      event tool, not persistence: not asked for, not built.
- [ ] **Client-asserted comfort numbers.** The client writes `VCargo_rested` / `VCargo_comfort` on its own
      ZDO and the server believes them (`docs/HANDOFF-CLAUDE.md` "Decisions still for the two owners").
      P11 proposes "accept; worst case an undeserved visit". Yes, or ask for a server-side check.
- [ ] **Reconfirm versus teardown on a price tick.** The `PriceChangePolicy` knob is deleted (nothing read
      it); only Reconfirm exists (`Client/Terminal/TrayModel.cs`). Close as final, or ask for Teardown
      to be built. Update the `docs/DESIGN.md` §8 row and `docs/TLDR.md` either way.
- [x] **PR #17** (the `VALHEIM-API-REFERENCE` snapshot, docs only): merged 2026-09-07.
- [ ] **The store.** No upload until the loop below has been seen (`docs/RELEASE.md` step 5; issue #23).
      **And never the rc1 tag**: it carries F1 (fixed on main by PR #30, not in the tag); the next cut replaces it.
      **OWNER 2026-09-08, after the day's five merges (main 43149bb): "no release."** Open before one: a two-client
      session on main's build (the count box, the leash hold with a second player, the hover key, the backpack shelf
      line, the daylight backdrop), the vanish's timing, and the Valheim 1.0 stop-ships, which are held.
- [x] **F11, from the P4/P5 audit:** a tamed Ingvar is a legal target for every hostile and cannot die or
      be staggered, so a raid parks on him. **DECIDED 2026-09-07: ghost mode** — Ingvar is to hostiles what
      a player in vanilla's `ghost` mode is: not a target, not a threat. Neither faction-only nor the aggro
      magnet. The mechanism is Wu'barrk's ("he knows how"); moved to section 2.

### Screen proofs — the Windows client against StormTest

Runbook: `docs/PROOF-CLIENT.md`, in its order. Items are CLAUDE.md "What to verify in-game"; the
lettered addenda are `docs/CLIENT-AUDIT.md`. Deploy the **rc1 DLL** from the release with
`tools/deploy-test.ps1` — it is the only build that carries Ingvar until the bundle asset is on this
machine. Every proven item gets its exact log line and date pasted into CLAUDE.md (`docs/RELEASE.md`
step 4).

- [ ] **Item 17, the demo terminal from the main menu**, with the audit's six checks: coins show 840 not 0
      (17a); all eighteen icons and localised names (17b); Escape closes it and note whether the game
      menu opens behind it (17c); Tab and M close it without opening inventory or map, tested after a
      world reload (17d); cursor and camera return by every close route (17e); `TerminalScale` 0.5 and
      2.0 at 1280x720 and maximised (17f).
- [ ] **Boot and status:** the client boot line with `renderer=True` (2); the config lock, a local
      `MinComfortLevel` edit loses to the server (4); `cargo prefab Valkyrie|Dverger|odin|Haldor` pasted
      back into CLAUDE.md (5); the runtime `m_activeArea` in `cargo status` (6); the comfort report (7).
- [ ] **Visit A:** `cargo visit` with the banner and the pilot line (8); the clock pausing past 96 m and
      resuming (9); the timer end at 300 s with the one-minute warning (10); an unrested player refused
      at the gate (12).
- [ ] **Visit B:** `cargo dismiss` ends it early with `ended: admin <name>` (11).
- [ ] **Visit C:** a deal on the wire with the price moving on every machine (13); the refusals
      `sold_out`, `coins_short`, `stale_visit`, `price_changed` (16); the terminal on a live visit, the
      countdown, deals moving prices, "Send him off" twice (18); the owed ledger at 0 after the ack and
      the crash-between-answer-and-ack redelivery at next login (14); a full pack keeps the owed row and
      `cargo claim` redelivers (18a); admin dismiss closes an open terminal with "he is gone" (18b); a
      full pack with 200 coins buying one stack of iron (18c).
- [ ] **Visit D:** stop and restart the server mid-visit; `RESUMED`, countdown continues (15).
- [ ] **The version wall** (3): bump the csproj, rebuild the client only, join, get refused, restore.
- [ ] **The body** (19, 20): `cargo body` says `source embedded`, six clips with the bake's lengths;
      `cargo body preview` stands him at the right height, feet on the ground, idle moving, the walk and
      the one-shot clips playing, `cargo body clear`.
- [ ] **Two clients** (a second account NOT on StormTest's adminlist): the admin gate and the config lock
      from the non-admin (11b, 4); the market broadcast reaching the second screen (13b, 18b); **the
      flight watched from two screens** (21): the bird about 90 m out and 120 m up, the straight glide,
      `dropped at` near 12 m, a smooth glide on the second client; **the edges** (22): the `flight:` line
      in `cargo status`, the pilot leaving the block, dismiss mid-flight, a real intro Valkyrie untouched.
- [ ] **The loop, once, end to end:** glide, drop, the walk-up *completing*, the callout, the terminal, a
      trade, the vanish. This is the item that gates the store. **Seen so far by the owner's eyes (2026-09-08):
      the arrival banner works; the vanish plays but is "not timed perfectly"** — **LATE** (the owner, the same
      hour): the smoke plays and he stands in it until the server's Clear lands, `Spawner.VanishGraceSeconds` (2 s)
      later. Vanilla `Odin.Update` creates the despawn effect and calls `m_nview.Destroy()` in the SAME frame, so
      his body is never in his own smoke. The fix is in `CargoMerchant.RPC_Vanish` (Wu'barrk's): hide him on
      receive — every `Renderer` under the merchant off, the AI stopped — on EVERY screen the RPC reaches, and let
      the grace go on protecting the RPC's delivery invisibly. Not the grace itself: shortening it races the
      RPC, which is the thing F3 put the grace there for. **BUILT on `a/vanish-hide` at the owner's word ("take it
      on a branch")**: `CargoMerchant.HideForGood` from `RPC_Vanish` (renderers, LOD groups, the collider off;
      the AI stood down on the owner; one log line `into the mist: K renderer(s) off with the smoke`). **SEEN on
      visit 20 (2026-09-08 ~10:33, the owner's dismiss): `into the mist: 1 renderer(s) off with the smoke`, and the
      owner: "vanish looked great."** The walk-up completing on the first approach is in the log (visits 13–15, 19,
      20). **Visit 20 also seen**: the new tray settling a deal on the wire (a Flametal on the 40-item shelf for
      five Eitr, +28 coins, to the coin on both sides), the leash-hold line on your own terminal, and the backpack
      boot line with `shelf 40 of 72`. **The owner on the rest, the same hour: "all that looks fine"** — the count
      box, the hover key and the backdrop, seen. Still unseen: the leash hold with a SECOND player at the terminal
      (needs Wu'barrk in), and the tray at `TerminalScale` 2.

---

## 2. Wu'barrk — flight, merchant, body, sweeps, export

- [x] ~~F11, decided by the owner 2026-09-07: ghost mode.~~ **Taken by Track A** (owner, the same evening:
      Wu'barrk is loaded with F2–F10) — PR #37; see section 3. Nothing of Track B's is touched.
- [ ] **The P4/P5 audit's findings (`docs/AUDIT-P4P5-2026-09-07.md`, issue #29).** End of day 2026-09-07:
      **F1 merged** (#30). **F2 + F6 merged** (#38). **F4 + F3's server half + decision 9's `Spawner` half:
      PR #39, ready.** **F7 + F8 + N1: PR #41, ready, merges after #39** (it carries the resolution of a
      `FlightPlan.cs` conflict with #39; both additions kept). **F3's merchant half + F5 + F9 + F10: PR #42,
      ready.** **F11: Don's, merged** (#37, ghost mode). Each PR was re-verified by the coordinator in a fresh
      worktree — build, harness at the claimed count, one mutation re-run by hand — and the three open ones
      were trial-merged together on `main`: clean, 1571 checks. Ticks when #39/#41/#42 merge.
- [x] **Hand over the bundle, every bake.** DONE 2026-09-07: `Assets/valkyriescargo_kit` attached to
      `v0.1.0-rc1`, byte-identical to the embedded copy (PR #27's note). Every re-bake: a new asset.
- [ ] **The walk-up.** PR #25 merged (the coin-eating consume list; the timeout logs a diagnosis). PR #42
      built F5: a budget scaled from the distance at entry (`Clamp(d/1.5, 20, 90)`), a 1 s progress window
      feeding a 3 s stall detector that is a DIFFERENT outcome (`Stuck`) from the timeout, and a leash that
      fires once per visit. **F5 has never run on a machine, and D1 is why** — see the next item. *What closes
      it:* one forced visit after D1 lands, and the `the walk-up did not finish` / `stuck` line pasted here.
- [ ] **D1 — the ZDO-driven state change skips the entry reset** (`docs/AUDIT-STORMTEST-2026-09-07.md` §1;
      found by the StormTest session of 2026-09-07, 6/6 visits). `ResolveCarrier` copies `VCargo_state` off the
      ZDO into `_state` every physics step, ABOVE the `if (!step.Changed) return;` block in `Decide` that is the
      only place resetting `_timeInState`, `_farSeconds`, `_approachMoved`, `_distanceAtApproachEntry` and
      `_progress`. `CargoFlight.Drop` writes `Approaching` that way, so the drop is a third way in and the one
      every visit uses: the walk-up runs on the flight's clock (16-17 s already on it) and is charged the carry's
      displacement, with `budget scaled from 0 m at entry` printed 6 times out of 6 and no `entered: landed` line
      anywhere in the session. **BUILT — PR #50**: one `EnterState(int)` both paths call, plus
      the transition line the audit asked for (`... -> ... via the ZDO, N s after waking; carrier
      none|still instanced|gone, N m from the player, ours|watching, grounded, walk-up budget N s`) and a warning
      when `Drop` finds no merchant naming this bird. `Core/MerchantPlan.cs` untouched — the pure machine is
      right, the caller fed it stale numbers. The retry in `Drop` and the `IsOnGround()` clock gate were both
      refuted by the audit and are deliberately absent. Build 0/0, harness 1701. *What closes it:* the line on a
      screen, and whether visits 4-6's second regime (§1.3, unexplained by the code) shows up again.
- [ ] **D5 — the server strips the pilot's claim on the merchant during the carry** (found by Don on
      StormTest visit 9, 2026-09-07, off the line PR #50 added; the one defect open at the rc2 cut).
      `ZDOMan.Update` runs `ReleaseZDOS` only `if (ZNet.instance.IsServer())` (`asm:65095`), every 2 s,
      and for each peer `ReleaseNearbyZDOS(peer.m_refPos, peer.m_uid)` (`asm:65164`) does, for a
      PERSISTENT ZDO that peer owns: `if (!InActiveArea(sector, zone, m_activeArea - 1)) SetOwner(0L)`
      (`asm:65193`). `m_activeArea` reads 2 live, so the keep-window is a 3x3 block of 64 m zones —
      and `Spawner` authors the merchant owned by the pilot at the FLIGHT START, ~90 m out, right on
      that edge. It is a one-way door: only an owner writes position, so an unowned merchant's SECTOR
      freezes where the strip caught him and the grant branch (`asm:65199`), which tests that frozen
      sector, can never hand him back to the pilot he is being flown toward. That is `watching` at the
      drop, the 135.7 m, `Decide` never running (`FixedUpdate` gates it on `IsOwner`), the late
      give-ups on visits 4-8, the 150-600 m "moved" as one chord from the frozen point, and visit 9's
      no-show. It also explains what did NOT break: the bird is non-persistent and
      `ReleaseNearbyZDOS` skips `!Persistent` outright (`asm:65189`), so the flight was perfect on all
      nine visits. **BUILT — this branch**: `HoldTheCarry` calls `ZNetView.ClaimOwnership` every
      physics step while `Pinned`, gated on `CargoFlight.Flying` so only the pilot's client claims and
      two watchers never fight. `ClaimOwnership` is a no-op when already owner (`asm:70222`) and
      `ZDO.SetOwner` no-ops on an unchanged owner (`asm:63483`), so the real cost is ~1 write per 2 s
      strip; the reclaim runs at the physics rate against a 0.5 Hz strip, so his position can freeze
      for a step instead of forever, and once the bird carries him inside the pilot's own block the
      strip stops firing. Deliberately NOT on the `Reassert` cadence: 5 s against a 2 s strip is the
      "claim-then-act-next-tick loop that never catches an owning tick"
      (`docs/knowledge-base/IMPLEMENTATIONS/ZoneAnchor.md`, the LetItGrow addendum of 2026-08-26).
      Both log lines now carry `zdo.GetOwner()` as PR #50 asked (`ours|watching (owner N - nobody)` —
      0 distinguishes "the sweep released him" from "another peer took him"), the transition line adds
      `N reclaim(s) during the carry` and `IN THE STRIP BAND` from the new pure `Core/ZoneOwnership.cs`.
      Build 0/0, harness **1718** (17 new: the zone grid, the Chebyshev block, the same 90 m surviving
      or stripped depending only on grid alignment, the 95/97 m cliff edge, and the compiled-default
      trap; two mutations re-run by hand — truncate-instead-of-floor and a forgotten `- 1` — each
      caught). *What closes it:* one visit where the transition line reads `ours (owner <pilot>)` at
      the drop, and the walk-up starting from there — which is also what finally closes the walk-up
      and F5 above.
- [ ] **D3 — the reclaim works; the sweep double-counts it** (`docs/AUDIT-STORMTEST-2026-09-07.md` §2; reporting
      only). `FinishDeparture` ran `ClearIfStillOurs` and `Sweep` in one synchronous call, and `ZDOMan.DestroyZDO`
      in 0.221.12 only queues into `m_destroySendList` — removal happens in `HandleDestroyedZDO` on the next
      `ZDOMan.Update`. So the sweep found the merchant the reclaim had just destroyed and called it stranded:
      six visit ends, six phantom `restart sweep: 1 stranded merchant(s) destroyed` lines, none of them a restart.
      **BUILT — PR #51**: `_pendingSweepFor` drained at the top of `Tick` (above every
      `FinishDeparture` call site, so it can never drain in the call that set it), `Sweep(int, string when)` with
      the three call sites labelled `boot` / `boot, after giving up on the carry` / `after visit #N`, and the
      reclaim logging its own outcome. The `LastReclaimed` skip list was refuted twice (`Reclaim` swallows its own
      exceptions, so a reclaim that FAILED would be skipped by the sweep that exists to catch it) and is absent.
      Build 0/0, harness 1701 (`Spawner` is outside the harness). **Known and accepted:** if the server stops
      within one director tick of a departure finishing, the deferred sweep is lost — `CargoTick` calls
      `Flush("shutdown")`, not `End`, so nothing drains it; the boot sweep catches the orphan at next start.
      *What closes it:* a visit end on a server showing `merchant and bird reclaimed ...` and NO sweep line.
      Note for Track A: every quoted `restart sweep:` in CLAUDE.md (line 726) and the proofs record becomes
      historical once this lands — theirs to reword, not ours.
- [ ] **Valheim 1.0 lands 2026-09-09.** P10a is his: fetch the 1.0 client and server
      (`tools/fetch-builds.sh`), decompile, `diff-engine` against the 244-row `docs/ENGINE-SURFACE.md`,
      update `docs/ENGINE-BASELINE.md`, check in the report under `docs/engine-sweeps/`, and report
      anything that moved. Then re-check every CLAUDE.md engine fact the diff touches. Any change in
      the surface is a stop-ship.
      **A head start landed 2026-09-07, from the MigrationStation session on this machine.** A 1.0
      build is already local: Steam public-test build 23105022, **0.221.13**, network version 37, at
      `libs-Tools/GAME-SNAPSHOT-playtest-build23105022/` (dedicated SERVER only; no 1.0 client
      snapshot exists, so the client axis is unverified), decompiled at
      `libs-Tools/DECOMPILED-PLAYTEST-build23105022/`. Its same-axis metadata diff (0.221.12 server
      vs 0.221.13 server) is checked in verbatim at
      `docs/engine-sweeps/2026-09-07-migrationstation-server-vs-server-0.221.12-vs-0.221.13.txt`
      with a header saying what it is and what a signature diff cannot see. **Do NOT install their
      compat layer on a 1.0 testbed** — their own audit found its same-name bridges make
      `Type.GetMethod(name)` / `AccessTools.Method(type, name)` throw `AmbiguousMatchException`, which
      would break our whole name-based probe registry on contact; their advice to us is to recompile
      against 1.0 references instead. What is already known, so the sweep need not re-derive it:
      - **D5 and `Core/ZoneOwnership.cs` survive untouched**, checked at BODY level (a metadata diff
        cannot): `ReleaseNearbyZDOS` is unchanged statement for statement and `GetZone` still computes
        `FloorToInt((v + 32.0) / 64.0)`. Only `Vector2i` -> `Vector2s` moved.
      - **Unchanged by signature:** `ZNetView.ClaimOwnership`, `Character.RPC_Damage` (the
        immortality patch), `RandEventSystem`'s runtime API, `ZoneSystem.instance.m_activeArea`.
      - **Our two real break points, both by design:** `EngineCheck.cs`'s three `InActiveArea` probe
        rows pin `typeof(Vector2i)` and will report FAILED on 1.0 — a probe is a veto, so this is the
        stop-ship signal firing correctly, not a bug; and `CheckVersion` reflects `m_networkVersion` /
        `m_playerVersion` / `m_worldVersion` by name, all renamed on 1.0 (`c_networkVersion`; world
        version becomes the enum `Version.World`), so it answers "is gone or is not a number" rather
        than silently claiming "same build". Both want the rename, neither is a silent failure.
      - Type-pinning our reflection is what keeps us on the right side of the ambiguity above; keep it.
      **THE SWEEP RAN EARLY, 2026-09-08**, on the server axis, with `tools/decompile-builds.sh` +
      `tools/diff-engine.js` - the P10a tools, unmodified; the two snapshots are symlinked into
      `~/valheim-shadows` so `--build` finds them. Report:
      `docs/engine-sweeps/2026-09-08-server-0.221.12-vs-0.221.13.md`. Of 257 surface members: 231
      unchanged, 15 body changed, 6 signature changed, 5 gone. **There is a stop-ship, and it is worse
      than the FileSource one:** `StringExtensionMethods.GetStableHashCode(this string)` gained an
      optional `bool addToNameHash = true`. The ALGORITHM is byte-identical, so no key value and no
      saved world moves - but C# resolves an optional parameter at the CALL SITE at our compile time, so
      our DLL calls a one-argument method that does not exist on 1.0. All 16 call sites throw
      `MissingMethodException`, and most are `static readonly` initialisers, which makes it a
      `TypeInitializationException` that poisons the type for the rest of the session: `Server/Spawner.cs`
      (10 - the whole spawner), `Libs/ServerSync.cs` (3 - the version gate, **and that file is "not ours"
      and may not be edited in place; it wants an upstream ServerSync built for 1.0**),
      `Client/ComfortReporter.cs` (2 - nobody is ever eligible), `EngineCheck.cs` (1). Verified against
      the real assemblies, not the decompile: arity 1 on 0.221.12, arity 2 with no 1-arg overload on
      0.221.13. *Not yet decided:* how we call it from one DLL that must run on both - a cached
      reflected delegate behind a `Keys.Hash(string)` helper covers our 13, but ServerSync's 3 are
      upstream's. **The client axis is still unswept** - no 1.0 client build exists yet; a second run is
      owed when one appears. The 15 body changes are listed in the report and none has been read yet;
      each is a recorded engine fact that may now be false.
      **The 15 were read the same day, and there is a SECOND stop-ship, quieter than the first:**
      `ZNet.GetAllCharacterZDOS` gained `if (m_characterID == ZDOID.None) return empty;` ABOVE the loop
      over `m_peers`. `m_characterID` is set only by `ZNet.SetCharacterID`, called only from
      `Game.SpawnPlayer` (the LOCAL player), and a dedicated server never spawns one - so on a 1.0
      dedicated server the method returns an empty list forever. `VisitDirector.Gather` calls it once a
      second and CLAUDE.md calls it "THE server-side 'where is every player'"; that fact is now false.
      No candidate is ever found, no visit can ever happen, nothing throws, and the roll reports
      `no eligible player: nobody online` on a full server. A listen host is unaffected. Everything else
      holds and the report says why for each: `ZSyncTransform.OwnerSync`'s velocity path is
      byte-identical (only the scale branch moved), `ZNetView.Awake` still never re-applies Persistent,
      `CreateNewZDO` still does not set the prefab, and `Character.Awake` / `ZSyncAnimation.Awake` still
      cache a root-scoped animator so BodyLoader's appended-last defence stands. One WATCH: `ZDO.IsValid`
      is now `m_prefab != -1` rather than a `DataFlags` bit, so a ZDO is invalid between `CreateNewZDO`
      and `SetPrefab` - a window `Spawner.Author` already closes on the next line and must keep closing.
- [ ] **Client-only proofs he took:** the animator parameter names (a dedicated build strips controllers;
      P5's `SetBool` names must be read on a client); the rest of item 20 — Ingvar in daylight, the
      walk, the one-shot clips (CLAUDE.md "Seen fixed on a screen"); `cargo prefab odin` on a client.
- [ ] **Item 23, the export, live:** a visit on his dedicated server, `barrkbot_cargo_*.json` landing under
      `BepInEx/config/ValkyriesCargo/` once a minute, the log line pasted into `BARRKBOT_CONTRACT.md`
      ("shape-verified, not yet live-verified"), and BarrkBOT's scanner picking the files up.
- [x] **Truth pass on his files.** PR #27 (decision 8, CATALOGUE checked entry by entry) and PR #33 (README,
      CHANGELOG, models/README, plus #24's five client-read keys and two ranges) both merged 2026-09-07.
- [x] **Close issue #16.** Closed 2026-09-07 on the rename shipped in #22, verified on `main`.
- [x] **The load-bearing set (issue #31), delegated by the owner.** Decision 9, PR #36 merged: the refuse set
      stays ServerSync only; `PatchLedger.IsApplied(name)` is the middle tier a feature gates itself on. The
      flight's half — no bird without `Patch_Valkyrie_Awake`, Ingvar on the ground, one loud line — is in
      PR #39.
- [x] **`event valkyries_cargo` from the vanilla console** (owner request). PR #34 merged 2026-09-07: the
      director adopts a console-started event onto the nearest player instead of killing it. Left as-is on
      purpose: `event` is `onlyServer` in vanilla (`cargo visit` stays the client route), and `stopevent` ends
      the visit reporting `timer` rather than the true reason — small, separate.
- [x] ~~Pending Don's decision: stop tracking the DLL.~~ Decided and done on Track A 2026-09-07 (section 3).
      Yours after it: `docs/DECISIONS-WUBARRK.md` §7's "the bake runs on both machines" premise and §3 want a
      one-line "overridden by the owner 2026-09-07" each, and `BARRKBOT_CONTRACT.md` no longer needs to name
      Newtonsoft as a requirement (its "shape-verified against Newtonsoft" history can stay).
- [x] ~~Pending Don's decision: replace the Newtonsoft call with a pure writer.~~ Decided and done on Track A
      2026-09-07 (section 3): `Core/Json.cs`. Item 23 (the files landing live) is unchanged and still yours.
- [x] **Record the reversal of decision 3.** DONE: PR #43 merged 2026-09-07. The owner's PR #40 dropped the
      JsonDotNET dependency for a pure `Core/Json.cs` (byte-identical to Newtonsoft over 4,028 comparisons);
      `docs/DECISIONS-WUBARRK.md` §3 now records that it was overridden, when, and why the original argument
      was right about the problem and wrong about the size of the answer. §7's amendment was already in.

Not his: the two-client items (cannot run on his side); Don's three branches (rebased here).

---

## 3. Claude on Don's side — rebases, audits, docs

- [x] **`a/p11-shakedown`.** DONE: PR #24 merged 2026-09-07 (the `VCargo_admin` caller fix, the dismiss
      gate, three bounded paths, both documents finished against main, two DESIGN §8 rows, 1302 checks).
- [x] **`a/p10b-probes`.** DONE: PR #28 merged 2026-09-07 (the `RPC_Damage` probe gap fixed; 19 facts with
      the P5 members and the Awake ordering; `docs/ENGINE-PROBES.md`; 1423 checks). Still open from it:
      **item 24**, the probes resolving on a real machine (Don's client or StormTest). The audit's probe
      rows landed in PR #46 (below).
- [x] **`a/p10a-sweep`.** DONE: everything on it was already on main through PR #20 (byte-identical tools
      and reports; main's two engine docs newer); the four doc corrections applied by PR #26; branch deleted.
- [x] **P11d, the adversarial audit of P4 and P5** against the real assembly. DONE 2026-09-07:
      `docs/AUDIT-P4P5-2026-09-07.md` (1 blocker, 5 bugs, 5 risks, 15 notes, ~45 probe rows for P10b, and
      the list of what was checked and found correct); findings posted to Wu'barrk as an issue. The probe
      rows go into P10b's registry after PR #28 merges.
- [x] **Download the bundle asset** from the v0.1.0-rc1 release into this machine's ignored `Assets/`.
      DONE 2026-09-07 (owner's word): 3,845,930 bytes; a build here is 4,181,504 bytes with Ingvar in it.
- [x] **The audit's probe rows into P10b's registry** (`docs/AUDIT-P4P5-2026-09-07.md` §2). BUILT, PR #46
      MERGED 2026-09-07 (b8f3f78): 19 → 25 facts, 15 → 18 probed at boot, 4 → 7 bodies registered as
      not probeable; three new probes (`znetview`, `interfaces`, `console`), the rest folded into the existing
      ones; 1697 checks; **every probe resolved against the REAL `assembly_valheim.dll` 0.221.12 offline**
      (a scratchpad tool loads the built DLL and calls `EngineCheck.Run()`: `probes 18/18 ok, 7 not probeable`),
      six wrong-signature mutations each a FAILED line from it. `docs/ENGINE-PROBES.md` §7–§9. **Item 24's boot
      half DONE 2026-09-07 10:40 on StormTest**: `probes 18/18 ok, 7 not probeable` and `patches 18/18 applied` in the
      real boot log, under Mono. **The moved-version direction, offline, 2026-09-07 evening**: against Steam's
      `default_old` server build (0.221.4 / net 35 / player 42 / world 36) the tool answered `older game version
      (0.221.4 vs 0.221.12); network version moved (35 vs 36); …; probes 18/18 ok` (ENGINE-PROBES §8 item 4). Still
      open: `cargo engine` read on a client, and a moved-version BOOT on a machine.
- [x] **The StormTest session, 2026-09-07 10:39–11:38** (Don's Windows client, PR #46's build; record:
      `docs/proofs/2026-09-07-stormtest-session.md` + the log excerpt). Six visits, 20 deals over the wire, no
      exception from the mod. DONE: items 8, 12, 13, 18, 24 (both sides), 26 (all but "waits"); the log halves of
      7, 9, 10, 11, 15, 16, 17, 21, 23; plus the Fair Market Act, both drift knobs, the carry on gross coins and a
      relog mid-visit, all to the coin. **Found: D1** the first approach after the drop never starts cleanly (6/6;
      `ResolveCarrier`'s ZDO-driven state change skips `Decide`'s entry reset, and the drop's ZDO write lands at the
      drop, late, or never); **D2** the per-player cooldown keyed on the session uid (three `cool` rows for one player
      after two relogs); **D3** the deferred reclaim at visit end is not reclaiming (the sweep is). Fixes on the
      owner's word; D1/D3 are Track B's files. Still to run: 3, 5, 14, 22, 25; the client-console halves need a
      screenshot or the server window.
- [x] **The session audit for Track B** (owner: "run it on opus agents", 2026-09-07 evening):
      `docs/AUDIT-STORMTEST-2026-09-07.md` — three Opus auditors, one refuter per finding (11 agents). D1 upheld in
      its code half (the ZDO-driven entry skips the reset; F5's scaled budget has never run on a machine) and
      corrected in its story (visits 4–6 are a second regime the code alone does not explain; the fix adds the log
      line that settles it); D3 flipped (the reclaim works, `DestroyZDO` only queues, the same-call sweep
      double-counts — defer it one tick); D2 upheld and narrowed (the base cooldown masked it; key on `s_playerID`
      with two probe rows); D4a/b wording; the eleven merged fixes tabled against the logs (2 confirmed, 2
      contradicted, 6 never exercised); Steam's new `default_pre1_0` branch for P10a. Proposed diffs only, his
      files untouched; **D1 and D3 then BUILT by Track B himself the same evening (#50, #51, merged 16:17; #52 his
      tracker)**, D2/D4 by Track A (#48). **Corrections applied to the proofs record**: the sweep trails the
      end by ~2 s not 6, five flight times not six, `moved 597.9 m in 20 s` is not a speed.
- [x] **D2 + D4 built** (owner: "do ours and merge", 2026-09-07 evening; PR #48): `Candidate.PlayerId` /
      `CooldownKey` on `ZDOVars.s_playerID` with `Uid` as the fallback, keyed through `StampCooldown`, the bucket and
      `Describe` (`Force` stays on `Uid`); `CheckComfort` 13 → 15 members with the `"playerID"` hash, the surface
      string names it, `docs/ENGINE-PROBES.md` §9 row; the admin and deal wires gate on `ZNetPeer.IsReady()` above
      the live count; the client's session-end line. Harness 1697 → 1701; `probes 18/18 ok` against the real
      assembly; the hash mutation fails `comfort` and only `comfort`. **Not yet seen on a machine**: a relog from a
      second base still on cooldown; ~~the wire line naming the player on connect~~ SEEN 15:35:51 the same evening
      (`admin wire registered for Nomadtest (-618124001)`). D1 and D3 stay Track B's.
- [x] **Ingvar walks backward** (owner, on screen, 2026-09-07 15:50, visit 8; "fix the backwards walking"):
      the bundle's forward axis is the Dverger's back and the loader attached him with identity rotation.
      `Client.BodyYawDegrees` (local, default 180, range ±180) applied at `BodyLoader.Attach` and in the preview;
      the attach line logs `turned N deg`. Harness untouched (Unity-side). **Not yet seen after the fix**: visit 9
      (17:03, main 84de90a) printed `turned 180 deg` but was a no-show — see the next item — so the walk itself is
      still unseen; a bake that comes out facing forward sets it to 0.
- [ ] **Visit 9 settled D1's second regime: the pilot loses OWNERSHIP of the merchant during the carry** (CLAUDE.md
      "VISIT 9"; audit §1.3's note). PR #50's line at the drop read `… carrier none, 135.7 m from the player,
      watching, …; walk-up budget 90 s`: the entry reset works (90 s, not the floor), but the pilot's client no
      longer owns him and his body is back along the flight where his networked position froze. No owner, no
      `Decide`, no walk-up; the clock pauses with nobody within 96 m; the pilot left with the visit open. **Track
      B's file** (`CargoMerchant` / `Spawner`): keep the pilot as owner for the whole carry, and log the owner uid at
      the transition so the next log says who took it. Not built; the owner is telling Wu'barrk.
      The evening session's other lines are in CLAUDE.md's "THE EVENING SESSION" paragraph and
      `docs/proofs/2026-09-07-stormtest-evening.log.txt` (D4a seen, D2's base bucket, item 19 DONE, D1's second
      regime on both visits, the Fair Market Act on Eitr).
- [x] **The two owner decisions of 2026-09-07, built here (PR #40 merged):** the JSON
      dependency swapped for `Core/Json.cs` (writer only; compact and indented output shaped like
      Newtonsoft's so the rollover's part boundaries and BarrkBOT's files do not move; the `<Reference>`,
      the `libs` check, the `fetch-libs` copy and the manifest entry removed), and the built DLL untracked
      (`HexiumDist/plugins/` ignored, the tracked copy deleted, payloads as release assets). Docs: RELEASE,
      WORKSPLIT §4, CLAUDE.md, DESIGN §8, README's dependency line.
- [x] **F11 ghost mode — PR #37, merged 2026-09-07.** The owner's decision, built here because
      Track B is loaded: a `Priority.Low` prefix on the static `BaseAI.IsEnemy(a, b)`, any pair with the
      merchant in it answers "not enemies" while a visit runs; `Core/Ghost.Decide` pure, 11 checks, three
      mutations caught; the `character_ai` probe resolves the static overload; DESIGN §8 row; CLAUDE.md
      verify item 25 is the screen proof (a raid walks past him; no enemy bar; he never swings).
- [ ] **After the proofs, if the screen shows it** (`docs/CLIENT-AUDIT.md` report-only findings): the game
      menu opening behind the terminal (finding 7, `Patch_Menu_Update`), the negative icon cache
      (finding 9), the full-pack deal check (finding 10, `CanApply`), `HasRenderer` as a cached field
      (finding 8).
- [x] **CLAUDE.md is this track's; his files are his.** DONE: PR #26 merged 2026-09-07 (the status
      paragraphs true to the one live visit, 17 engine facts with their sources, the P10a corrections, both
      handoffs and WORKSPLIT and TLDR re-cut). Still open: **the proof lines as Don sends them.**
- [x] **Housekeeping:** PR #17 merged; issue #23 answered; #16 closed by him. Still open: the release-time
      README status and CHANGELOG entry (`docs/RELEASE.md` step 4) when the proofs are in.
- [x] **Issue #31, the bare `PatchAll()`** (raised by Thorium; Don's file). DONE: PR #35 merged 2026-09-07 —
      every patch class applied on its own (`Patching.cs`), failures named and counted (`Core/PatchLedger.cs`,
      pure), the boot line `patches N/M applied`, `cargo status` prints the failures, ServerSync's rows refuse
      the mod; 1462 → 1470 with #34. **Not yet seen on a machine**: the new boot line (item 2) and a
      deliberately failing patch in a scratch build printing its name rather than killing the mod.
- [x] ~~Owner's decision: widen the load-bearing set or leave it.~~ **Delegated to Wu'barrk 2026-09-07**
      (owner: "more his alley"); moved to section 2.
- [x] **The catalogue verbs and the hot swap** (owner, 2026-09-07; PR #44 merged): `Catalogue.Compose /
      Upsert / Remove / Without` and `Market.WithCatalogue`, pure, 55 checks (1605 → 1660), three mutations
      caught; `ModConfig.CatalogueVersion`; the director rebuilds the shelf as soon as no visit is running and
      drops prefabs the game has no item for, at boot and on every edit; `cargo catalogue list|add|remove|reset`,
      the admin half through `VCargo_admin`; `cargo status` says when a change waits. **SEEN 2026-09-07 on
      StormTest** from Don's admin client: item 26 all but the add-during-a-visit answer (the session item above).
- [x] **The two drift knobs** (owner, 2026-09-07; PR #45 merged): `MarketRules.WareHalfLifeGameDays`
      0 / `WantHalfLifeGameDays` 3 replace `HalfLifeGameDays`; `Relax` per kind, 0 = never; `Sanitize` 0–365 for
      both; `Server.WareHalfLifeGameDays` / `Server.WantHalfLifeGameDays` replace `Server.StockHalfLifeGameDays`;
      the demo market keeps a one-day half-life on both kinds so its walk still moves; EconSim scenario 10 is
      the sweep and `docs/ECONOMY-SIM.md` is regenerated; harness checks for the shipped knobs and the clamps.
      **SEEN 2026-09-07 on StormTest**: five Wares bought out in visit 3 still at 0 when visit 4 opened; Wood 326 → 322 in the gap.
- [x] **rc2 cut** 2026-09-07 evening at the owner's word: `v0.1.0-rc2` on main e4ee83c (code 84de90a), 1701
      checks, the store zip and `valkyriescargo_kit` attached, pre-release, no store; rc1's page marked
      superseded, issue #23 closed. The gate does not tell rc1 from rc2 (both 0.1.0): replace copies by hand.
      The rc2 changelog entry and the README status rewrite (`docs/RELEASE.md` step 4) went in with it.
- [ ] **The next cut** (owner, 2026-09-08 00:40): waits on Wu'barrk's ownership-during-the-carry PR and one screen
      visit on that build where the first approach reaches Don; not a date. rc3, or 0.1.0 proper if that visit and
      the vanish are clean; if Valheim 1.0 lands first, cut once after the probe run. Review his PR on arrival,
      merge only on the word.
- [x] **D5 reviewed and merged** (owner, "post it and merge", 2026-09-07 ~20:43): Wu'barrk's PR #54, main 2694d3b,
      1718 checks; deployed both sides; **SEEN on visits 13, 14, 15**: `ours (owner …), 2 / 2 / 0 reclaim(s)` and
      the first approach reached every time. The review's table (all starts inside the 3x3; freezes mid-flight)
      and the two notes (the reclaim pattern by direction; F3's grace 0 s on three of four ends) are on #54.
- [ ] **PR #55 (his docs: the 1.0 head start)** — OPEN, NOT merged at the owner's word ("do not merge 55").
      Caveat not yet posted: Steam shows no branch with build 23105022 / 0.221.13 on either app from this machine
      (public, previous stable, the pre-1.0 pins, all at 21981590 or older). Builds, 1718, docs only.
- [ ] **StormTest is down with visit 15 OPEN in the sidecar** (session row; `purse`/`purseStart` 100000 = Don's
      test value; the merchant ZDO in the 21:05:20 save near the drop). The next boot adopts it (item 15 and F4's
      rebind for free) unless the row is cleared first, as was done before the 20:18 and 20:44 boots (a backup
      beside the file each time). The proofs record is `docs/proofs/2026-09-07-stormtest-night.md`.
- [x] **#55 merged** (owner, "merge 55", 2026-09-08 morning; main a08e8c4, 1722): the save-path resolver + the
      `save_path` probe; his 1.0 sweep report. Verified from this machine (the hidden `public-test` branch fetched,
      build 23105022 / 0.221.13; ProbeCheck 15/19 on it; both stop-ships confirmed in the decompile). Deployed both
      sides; StormTest booted 19/19 on 0.221.12 and RESUMED visit 15 (item 15's server half + F4's rebind SEEN).
- [x] **1.0: HELD** (owner, "dont change anything for 1.0"), then **BUILT on `a/valheim-1.0` 2026-09-10** at the word
      (the 1.0.7 publicized assemblies handed over). The two playtest stop-ships never shipped; what the release broke
      is the item at the end of this section.
- [x] **The rotating shelf** (owner, 2026-09-08): 20 of the 72, re-rolled every couple of game days, the fixed Ware
      list goes away; issue #56 carries the design and the four economy questions. Wu'barrk's read came back the
      same morning (two-day default; a backpack add-on). **BUILT on `a/rotating-shelf` 2026-09-08** (the owner: "build
      the shelf on a branch"): `Core/Shelf.cs` (pure), `Market.KindOf` as the one place the effective kind is decided,
      `Server.ShelfSize` 20 / `Server.ShelfRotationGameDays` 2, the director's roll-when-idle with its two log lines,
      `not_on_shelf`, the `cargo status` line; 1818 checks, 0 warnings, economy scenario 11, `docs/CATALOGUE.md` §7.
      PR #57 is open and waits for the word; **SEEN on StormTest 2026-09-08 morning**: `director up: … shelf 20 of
      72, period 13/14`, `shelf roll waits: visit #17 is running`, `shelf rolled: … period 14 …` with the twenty
      names, `shelf now: …` after a restart, and visit 18 resumed across it.
- [x] **The playtest's terminal items 4, 5 and 7** (Wu'barrk's report, 2026-09-08; the owner: "build 4, 5 and 7 on
      a branch"). **BUILT on `a/terminal-ux` the same morning**, branched off `a/rotating-shelf`: (4) a count box on
      every staged line, digits only, clamped to his stock / your carry / the room on his shelf and written back
      clamped, an **all** button (a ware: as many as he has and you can pay for; goods: everything you carry that
      fits), **x** to clear a line, Shift 5 / Ctrl 20 kept, `TrayModel.SetCount` / `AllOf` / `Remove` pure; (5) the
      Coins/Barter switch and `PayMode` removed — the tray is always YOU GET beside YOU GIVE, one balance line
      ("you pay N c" / "he pays you N c" / "even"), "Cover it with my goods" whenever a ware is staged, and
      `EnableBarter=false` refuses goods beside a ware on the client (`barter_off`, a line of Ingvar's); (7) the
      window on the theme's near-black panel at `Client.TerminalBackdropAlpha` (0.4 = the 40 % translucent black
      asked for) with both text tones a step brighter, through `ThemeOptions` (the vendored theme untouched).
      While a count box has the keyboard, Use/Tab/M do not close the window (`UIFocus.SetHasTextFocus` raised
      from Tick); Enter or a click elsewhere hands it back. 1841 checks, 0 warnings. **SEEN on visit 20 (2026-09-08,
      main): the tray settled a deal on the wire, and the owner on the box, the backdrop and the rest: "all that
      looks fine."** Unseen: the tray at `TerminalScale` 2.
      Items 1, 2 (needs Wu'barrk's client log) and 3 (a one-line `Localize` in his `GetHoverText`) and the backpack
      test are his: issue #59.
- [x] **Items 2 and 3 taken from Track B** (owner, 2026-09-08: "take the first two on a branch"). **BUILT on
      `a/merchant-busy`** (off `a/terminal-ux`): `VisitSnapshot.TerminalsOpen` as an optional 13th field (12-field
      strings still parse; never in the sidecar row), `VisitSession.SetTerminalsOpen` publishing on change, the
      director copying `DealWire.OpenTerminals` every tick, the wire forgetting a dropped peer's open terminal and
      clearing at End; his side: `MerchantPlan.Next(..., busy)` holds the leash, `AccumulateFar(..., busy)` banks
      nothing, `CargoMerchant.Busy()` = the local terminal or the server's count for this visit, one log line per
      flip; `GetHoverText` through `Localization.instance.Localize`. 1862 checks, 0 warnings. **Unseen on a
      machine**: `cargo merchant #N: a terminal is open on him (K on the wire): the leash holds` on the owner while a
      SECOND player trades, and whether the walk-off was the leash at all (his log). **SEEN with a second player,
      2026-09-08 evening, visits 23 and 24 (Wu'barrk on as "Wubarrk Dev", both on main's build): the owner's
      client printed `cargo merchant #23: a terminal is open on him (1 on the wire): the leash holds` with his own
      terminal closed, then `no terminal open on him: the leash is armed again` when Wu'barrk's closed; the same
      pair on visit 24; the owner: "2 client worked."** The walk-off was NOT the leash (his visit-16 log; PR #66).
      The hover key: **seen, the owner 2026-09-08 ("all that looks fine")**; the hold line on his own terminal:
      seen on visits 19 and 20.
      **Backpacks 1.3.8 is installed on StormTest and Don's client** (GUID `org.bepinex.plugins.backpacks`,
      confirmed off the DLL; Thunderstore, the owner's download OK) — the backpack test can run from here.
- [ ] **The buy-anything extension** (owner, 2026-09-08, the same message): Ingvar buys ANY item a player offers,
      on the shelf or not; a sale of an uncatalogued item forces a new persistent entry, classed common or rare by
      value; common rows rotate as normal inventory with a timed persistence (say 2 visits), rare rows stay until
      sold or on an extended timer. DESIGNED in the shelf PR's body (six decisions: the price source for an
      uncatalogued item, the common/rare classifier, the sidecar row and its expiry, whether rares count inside
      `ShelfSize`, `cargo catalogue reset`, a cap on walk-in rows for the synced payload); NOT built, by the word.
      **DECIDED 2026-09-08 (owner), two of the six:** a common walk-in row lives **2 visits without a sale**, then
      rotates off the shelf; and the price source is a **full item value table** built against the Valheim wiki —
      every player item assessed for rarity and complexity, valued 2–600, with 800–1200 reserved for the ultra
      complex or rare (`docs/ITEM-VALUES.md` + `docs/data/item-values-2026-09-08.tsv`, Track A, in progress).
- [ ] **The backpack add-on** (Wu'barrk, 2026-09-08): shelf ×2–4 when a backpack mod is detected at server load,
      and a backpack on his body. **DECIDED 2026-09-08 (owner): Smoothbrain's Backpacks** (BepInEx GUID
      `org.bepinex.plugins.backpacks`, **confirmed off the 1.3.8 DLL** the same day, installed on StormTest and
      Don's client); ×4 is the whole shipped catalogue, ×3 is 60 of 72; the body half is his bake.
      **The shelf half is BUILT on `a/backpack-shelf`** (the owner: "take the shelf multiplier on a branch"):
      `Server.BackpackShelfMultiplier` 2 (1–4) and `Server.BackpackModGuid`, `Server/BackpackMod.cs` reading the
      chainloader at director up (before the market is sized) and every tick, `Shelf.Scaled` pure (0 stays fixed,
      cap 200, superset of the unscaled shelf), `FillMarketRules` applying it, one log line and a `cargo status`
      line. 1831 checks. **SEEN on StormTest 2026-09-08 10:26** (the first boot on main): `backpack mod:
      org.bepinex.plugins.backpacks 1.3.8 loaded; shelf x2 (Server.BackpackShelfMultiplier)`, then `director up: …;
      shelf 40 of 72, period 14 …` with forty names on `shelf now:` and no re-roll after; visit 20 bought a Flametal
      that is on the shelf only by the multiplier. The body half stays his.
- [x] **`a/dismiss-at-merchant` — the dismiss measured against him and answered (PR #64 MERGED 2026-09-08, main
      55508d6, 1885 checks, deployed both sides).** SEEN on visit 22 (2026-09-08 ~11:15): leash walk (31 m / 5 s,
      followed, reached again), then Shift+E on him → server `VCargo_dismiss from Nomadtest: visit #22 dismissed`
      first try, client `VCargo_dismissed: ok`, the vanish; the end line says **0 clock republish(es)** against 543
      on visit 21, and the client's ServerSync line came 8 times over the whole visit instead of every 2 s. Still
      unseen: the terminal BUTTON path (the "Sending him off" wait, the farewell, `terminal closed: sent him off`)
      and a refusal on a screen (needs a modded client or a stale visit).
- [x] **`a/multi-wanted` — more than one ware per deal, Confirm lit only when the deal can go (PR #68 MERGED
      2026-09-08 evening, 1924 checks).** SEEN on visit 25: `deal w4790ce-25-1 with Nomadtest: sold 1 Ruby at 35,
      sold 2 ArrowFrost at 3, sold 1 ArrowIron at 2, sold 3 FlametalNew at 140, sold 1 BlackMetal at 62, bought 4
      BlackCore at 210, bought 5 Eitr at 36, bought 1 FlametalNew at 77, coins +572 to the player`, applied line
      for line on the client; the lit Confirm in the bright gold on visit 26 (the owner: "love it"). Open note: the
      same ware may sit on both sides of a deal (three Flametal bought at 140, one sold back at 77) — costs the
      player, never the purse; refuse it only at the owner's word.
- [ ] **`a/follow-assert` — the walk-off inside Trading, Wu'barrk's two diffs plus the vanish-flap guard
      (2026-09-08 evening, his file at the owner's word).** PR #66 MERGED at the owner's word ("github issues
      need solved"), UNSEEN on a screen. Proof needs TWO clients: a leash-free visit
      where ownership moves off the pilot (watch `ours|watching (owner N)` on both logs) and Ingvar stays at his
      patrol point; and no `leaving -> trading via the ZDO` line on the watcher at the vanish. Open question for
      Wu'barrk on the issue: was Ingvar armed, was anything hostile near the base on visit 16 (a combat target
      beats the follow and the idle walk alike, state unchanged).
- [ ] **`a/valheim-1.0` — Valheim 1.0.7 (2026-09-10, 1956 checks).** Steam moved both installs to 1.0.7 on 2026-09-09;
      swept from here the same day (`docs/engine-sweeps/2026-09-09-{server,client}-0.221.12-vs-1.0.7.md`; the
      playtest's two stop-ships did not ship). Six breaks fixed: `Hoverable.GetHoverOffset()` (CargoMerchant could
      not load); `m_activeArea` gone → the synced simulation distance and `Core/ActiveArea.cs` (D5 and the flight
      clamp rebuilt on it; the descent slide removed, its bound proven); `ZRoutedRpc.Everybody` a const; three
      signatures grown by an optional parameter; `Version.c_*`. The offline probe tool reads `same build 1.0.7 …
      probes 19/19 ok, 8 not probeable` on both assemblies. Two edits in CargoMerchant.cs (his file) at the word.
      PR #70 open. **SEEN 2026-09-10 07:11 to 07:48** on Storm10 (a fresh 1.0.7 dedicated server, port 2477, a new world,
      our DLL + ServerDevcommands 1.110 only) with the 1.0.7 client: both boot lines `running same build 1.0.7 (net 39,
      player 46, world 41); probes 19/19 ok, 8 not probeable`, `patches 18/18 applied`; a forced visit, the flight
      dropped after 14.8 s, `2 reclaim(s) during the carry`, the walk-up, the terminal and the leash, two deals settled
      line for line, the admin dismiss answered (`0 clock republish(es)`), the vanish, the reclaim; nothing thrown.
      Merge on the word. Then the 25 unread body changes, one by one, and `Splatform.dll` into the decompile set
      (`tools/decompile-builds.*` name three assemblies; `PlatformUserID` lives in the fourth now).
      **1.0 finding:** `adminlist.txt` / `permittedlist.txt` / `bannedlist.txt` want `V_<steamid>` (Steam V, Xbox X,
      PlayStation S, Nintendo N, Game Center A; `ZNet.ListContainsId`'s filtered match overrides the old forms). Don's
      other 1.0 servers need their lists rewritten; in CLAUDE.md's engine facts and the README.
      Open beside it: BepInExPack 5.4.2350 (the manifest now names it), Backpacks / ServerDevcommands / Infinity
      Hammer / World Edit Commands have no 1.0 builds yet; ServerSync upstream has no 1.0 commit (the vendored
      copy compiled clean and its three `Everybody` sites inline).
