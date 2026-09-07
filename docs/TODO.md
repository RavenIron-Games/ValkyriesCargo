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

- [ ] **The JSON dependency.** `ValheimModding-JsonDotNET-13.0.4` is in `manifest.json` and `Newtonsoft.Json`
      is a compile-time reference (PR #21, `docs/DECISIONS-WUBARRK.md` §3), overriding the locked
      "BepInExPack only" row. Keep as merged, or have the one `JsonConvert.SerializeObject` call in
      `Server/BarrkBotExport.cs` replaced by a pure writer in Core and the manifest entry dropped.
      *Recommended: the swap.*
- [ ] **The tracked DLL.** `HexiumDist/plugins/ValkyriesCargo.dll` (4.1 MB, rebuilt every package run) is in
      git, against WORKSPLIT §4's two-binaries rule. Keep tracking it, or ignore `HexiumDist/plugins/`,
      delete the tracked copy and publish payloads as release assets. The two blobs already in history
      stay either way. *Recommended: stop tracking.*
- [ ] **Client-asserted comfort numbers.** The client writes `VCargo_rested` / `VCargo_comfort` on its own
      ZDO and the server believes them (`docs/HANDOFF-CLAUDE.md` "Decisions still for the two owners").
      P11 proposes "accept; worst case an undeserved visit". Yes, or ask for a server-side check.
- [ ] **Reconfirm versus teardown on a price tick.** The `PriceChangePolicy` knob is deleted (nothing read
      it); only Reconfirm exists (`Client/Terminal/TrayModel.cs`). Close as final, or ask for Teardown
      to be built. Update the `docs/DESIGN.md` §8 row and `docs/TLDR.md` either way.
- [ ] **PR #17** (the `VALHEIM-API-REFERENCE` snapshot, docs only): merge on the word.
- [ ] **The store.** No upload until the loop below has been seen (`docs/RELEASE.md` step 5; issue #23).

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

- [ ] **The P4/P5 audit's findings (`docs/AUDIT-P4P5-2026-09-07.md`; issue #29).** **F1 DONE**: PR #30 merged
      2026-09-07 (the decision moved into the pure `Core/Immortality.RunOriginal`, seven checks) — but rc1
      still carries it, so **the rc1 tag must not reach a tester**; the next cut replaces it. Still open,
      F2 to F6 and the risks: F2 (the hover text never draws:
      `Character` is itself `Hoverable` and wins), F3 (`VCargo_vanish`/`VCargo_say` registered, never sent:
      no Odin vanish, single-screen speech), F4 (a resumed visit never rebinds `Spawner.Merchant`, so `End`
      leaves an immortal merchant in the world), F5 (the flat 20 s timeout and the 12 m leash loop from
      51 m for ever — the live log), F6 (burning/poison/smoke call `ApplyDamage` directly and bypass the
      patch). F1, F3, F4 and F6 were re-verified on this side against the code and the decompile.
- [x] **Hand over the bundle, every bake.** DONE 2026-09-07: `Assets/valkyriescargo_kit` attached to
      `v0.1.0-rc1`, byte-identical to the embedded copy (PR #27's note). Every re-bake: a new asset.
- [ ] **The walk-up.** In the one live visit he `gave up walking after 20 s` and called out from the drop
      point (issue #23, release note). P5, his client, his to find before Don spends screen time on the
      same wall. *Part done, PR #25 merged 2026-09-07:* the Dverger's AI consume list (Coins on it) emptied
      so he no longer walks to a coin stack and eats it, and the timeout now logs a diagnosis. *Still open:*
      the cause itself — the audit's F5 (a flat 20 s budget and a 12 m leash that loop from 51 m) is the
      standing hypothesis, and one run with the new diagnosis line answers it.
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
- [x] **Truth pass on his files.** DONE: PR #27 (decision 8, CATALOGUE matched to the code) and PR #33
      (README, CHANGELOG, models/README, plus the README config corrections from `docs/CONFIG-SHAKEDOWN.md`),
      both merged 2026-09-07.
- [x] **`event valkyries_cargo` from the vanilla console** starts a real visit instead of being killed as a
      leftover (owner's ask; PR #34 merged 2026-09-07; `Scheduler.NearestTo` pure, NaN-safe). Not yet
      typed on a screen.
- [ ] *(history)* The truth-pass list as it stood: `README.md` still lists the deleted `PriceChangePolicy` row (line ~195)
      and says "No custom body yet" (line ~39); `CHANGELOG.md` says P4 is in review (~228) and P5 and
      the bundle are not started (~230); `models/README.md` §2 names `BodyPrefab` where `CustomBody` is
      the switch and §6's stream-disposal note is wrong (`docs/HANDOFF-WUBARRK.md` §0 item 2); the
      reason four Wants went from base 2 to 3 goes into `docs/DECISIONS-WUBARRK.md` (it is only in the
      release note).
- [ ] **Close issue #16.** The rename shipped in #22.
- [ ] *Pending Don's decision above:* stop tracking the DLL (ignore `HexiumDist/plugins/`, delete the
      tracked copy, payloads on releases).
- [ ] *Pending Don's decision above:* replace the Newtonsoft call with a pure writer and drop the
      manifest dependency.

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
- [ ] **Owner's decision, when convenient:** widen the load-bearing set beyond ServerSync's patches, or leave
      it. Recorded in CLAUDE.md house rule 3.
