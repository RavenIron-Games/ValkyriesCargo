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
      (section 3). Still open from the same discussion, undecided: `cargo stock set` / a drop-in stock file
      for admins with server access, and `StockHalfLifeGameDays` allowing 0 (never drift) with a longer
      default — a number for the economy sim first.
- [ ] **Client-asserted comfort numbers.** The client writes `VCargo_rested` / `VCargo_comfort` on its own
      ZDO and the server believes them (`docs/HANDOFF-CLAUDE.md` "Decisions still for the two owners").
      P11 proposes "accept; worst case an undeserved visit". Yes, or ask for a server-side check.
- [ ] **Reconfirm versus teardown on a price tick.** The `PriceChangePolicy` knob is deleted (nothing read
      it); only Reconfirm exists (`Client/Terminal/TrayModel.cs`). Close as final, or ask for Teardown
      to be built. Update the `docs/DESIGN.md` §8 row and `docs/TLDR.md` either way.
- [x] **PR #17** (the `VALHEIM-API-REFERENCE` snapshot, docs only): merged 2026-09-07.
- [ ] **The store.** No upload until the loop below has been seen (`docs/RELEASE.md` step 5; issue #23).
      **And never the rc1 tag**: it carries F1 (fixed on main by PR #30, not in the tag); the next cut replaces it.
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
      trade, the vanish. This is the item that gates the store.

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
- [ ] **The walk-up.** PR #25 merged (the coin-eating consume list; the timeout logs a diagnosis). **The cause
      is fixed in PR #42 (F5):** a budget scaled from the distance at entry (`Clamp(d/1.5, 20, 90)`), a 1 s
      progress window feeding a 3 s stall detector that is a DIFFERENT outcome (`Stuck`) from the timeout, and
      the leash fires once per visit. Proven against the model, not the world. *What closes it:* one forced
      visit after #42 merges, and the `the walk-up did not finish` / `stuck` line pasted here.
- [ ] **Valheim 1.0 lands 2026-09-09.** P10a is his: fetch the 1.0 client and server
      (`tools/fetch-builds.sh`), decompile, `diff-engine` against the 244-row `docs/ENGINE-SURFACE.md`,
      update `docs/ENGINE-BASELINE.md`, check in the report under `docs/engine-sweeps/`, and report
      anything that moved. Then re-check every CLAUDE.md engine fact the diff touches. Any change in
      the surface is a stop-ship.
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
- [ ] **Record the reversal of decision 3.** The owner's PR #40 drops the JsonDotNET dependency for a pure
      `Core/Json.cs` (byte-identical to Newtonsoft over 4,028 comparisons). Decision 3 in
      `docs/DECISIONS-WUBARRK.md` says the opposite; once #40 merges it must say it was overridden by the
      owner and why, not go quietly stale. Docs only, ours.

Not his: the two-client items (cannot run on his side); Don's three branches (rebased here).

---

## 3. Claude on Don's side — rebases, audits, docs

- [x] **`a/p11-shakedown`.** DONE: PR #24 merged 2026-09-07 (the `VCargo_admin` caller fix, the dismiss
      gate, three bounded paths, both documents finished against main, two DESIGN §8 rows, 1302 checks).
- [x] **`a/p10b-probes`.** DONE: PR #28 merged 2026-09-07 (the `RPC_Damage` probe gap fixed; 19 facts with
      the P5 members and the Awake ordering; `docs/ENGINE-PROBES.md`; 1423 checks). Still open from it:
      **item 24**, the probes resolving on a real machine (Don's client or StormTest), and the ~45 probe
      rows from `docs/AUDIT-P4P5-2026-09-07.md` §2 into the registry.
- [x] **`a/p10a-sweep`.** DONE: everything on it was already on main through PR #20 (byte-identical tools
      and reports; main's two engine docs newer); the four doc corrections applied by PR #26; branch deleted.
- [x] **P11d, the adversarial audit of P4 and P5** against the real assembly. DONE 2026-09-07:
      `docs/AUDIT-P4P5-2026-09-07.md` (1 blocker, 5 bugs, 5 risks, 15 notes, ~45 probe rows for P10b, and
      the list of what was checked and found correct); findings posted to Wu'barrk as an issue. The probe
      rows go into P10b's registry after PR #28 merges.
- [x] **Download the bundle asset** from the v0.1.0-rc1 release into this machine's ignored `Assets/`.
      DONE 2026-09-07 (owner's word): 3,845,930 bytes; a build here is 4,181,504 bytes with Ingvar in it.
- [ ] **The audit's probe rows into P10b's registry** (`docs/AUDIT-P4P5-2026-09-07.md` §2), now that
      PR #28 is in; and item 24 run on a real machine.
- [ ] **The two owner decisions of 2026-09-07, built here (PR open on the owner's word):** the JSON
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
- [ ] **The catalogue verbs and the hot swap** (owner, 2026-09-07; PR open on the word): `Catalogue.Compose /
      Upsert / Remove / Without` and `Market.WithCatalogue`, pure, 55 checks (1605 → 1660), three mutations
      caught; `ModConfig.CatalogueVersion`; the director rebuilds the shelf as soon as no visit is running and
      drops prefabs the game has no item for, at boot and on every edit; `cargo catalogue list|add|remove|reset`,
      the admin half through `VCargo_admin`; `cargo status` says when a change waits. **Not yet seen on a
      machine**: CLAUDE.md verify item 26 (StormTest, from an admin client).
