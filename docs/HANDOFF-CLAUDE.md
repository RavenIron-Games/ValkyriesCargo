# Handoff to Wu'barrk's Claude — Valkyrie's Cargo, 2026-09-06; section 0 re-cut 2026-09-08 morning: #55 merged, 1.0 verified and held, the rotating shelf waiting on you

You are the second engineering session on this mod. Don's session (me) built what is here; you and
Wu'barrk take part of what is left. This file tells you everything you need to act, in the order to
read it, and ends with the one job to do first: **divide the work packages with your owner, write the
split down, and say so on PR #1.**

Repo: <https://github.com/RavenIron-Games/ValkyriesCargo> (public, org RavenIron-Games). Everything below is on `main`.

---

## 0. State on 2026-09-08 morning: #55 merged, 1.0 verified and held, the rotating shelf waiting on you — READ THIS FIRST

**The tracker is still `docs/TODO.md`**, three tracks, each editing only its own section; when this file and
that one disagree, that one wins. The subsection below this one is this morning's state, kept as history.

**Where main is.** `a08e8c4` at the time of writing (your PR #55 merged the morning of 2026-09-08; the close-out
docs commit sits on top): 0 warnings, **1722/1722 off-game checks**, `probes 19/19 ok, 7 not probeable` on the real
assembly. The day's PRs, **#24 to #53**, are in build order with their check counts in `CHANGELOG.md` "0.1.0-rc2";
#54 and #55 are under "Since 0.1.0-rc2". **`v0.1.0-rc2` is cut** at `e4ee83c` (2026-09-07 evening, at the owner's
word): the store zip and the bundle attached, a pre-release, uploaded to no store; **it carries neither D5 nor the
save-path resolver**. `v0.1.0-rc1` is marked superseded and still must not reach a tester: it carries F1, and the
ServerSync gate does not tell the cuts apart (all are 0.1.0), so every copy is replaced by hand. Issue #23 is
closed. The owner's conditions for the next cut (2026-09-08 00:40: the ownership fix in, a first approach that
reaches) are met but for the vanish being watched; the cut itself is on his word — rc3, or 0.1.0 proper.

**Valheim 1.0, verified from Don's machine and HELD by the owner.** Your #55 sweep holds on every point that could
be checked here: Steam's `public-test` branch is password-protected and hidden from the branch list (that is why
it read as absent last night); fetched to `valheim-shadows/server-public-test` (build 23105022, 0.221.13, server
only). Our probe tool on that assembly: the version line unreadable (the constants are renamed), `zone_maths`
FAILED on the `Vector2s` overloads, `velocity_cache` / `character` / `comfort` THREW on the one-argument
`GetStableHashCode`, `save_path` PASSED. The decompile shows the `GetAllCharacterZDOS` early return word for word.
Fix designs exist (a pure `Core/StableHash.cs` exposed as an extension method inside our namespace, so C# rebinds
all 13 of our sites without an edit; a peer-list gather in `VisitDirector.Gather`; both version-constant names;
the zone type resolved at runtime; for ServerSync's three sites a shim declared in its namespace from our file,
flagged as the owner's call) — **and the owner said "dont change anything for 1.0"**. Nothing is built. Neither
side starts 1.0 work without his word; the client axis is unswept everywhere.

**Later the same morning (2026-09-08): the shelf seen, the first two-client playtest, the terminal reshaped.** The
shelf rolled on StormTest exactly as designed (`shelf roll waits:` under visit 17, `shelf rolled: … period 14 …`
with the twenty names, `shelf now:` after a restart, visit 18 resumed); PR #57 still waits for the word. Your
owner joined by crossplay join code with Yggdrasil's Reckoning 0.1.1 on both sides and reported seven items;
**the three terminal ones (amounts, Coins/Barter, contrast) are built on `a/terminal-ux`** (branched off the
shelf branch; its PR targets that branch until #57 merges): a count box, "all" and x on every staged line, no
pay mode (YOU GET / YOU GIVE and one balance line; `EnableBarter=false` now refuses goods beside a ware on the
client), a 40 % black backdrop with brighter text through `ThemeOptions`. **Your side is issue #59**, less the
two the owner then took back for this side (`a/merchant-busy`, edits in your files with his word): the
`terminalsOpen` count now rides in `VisitState` (optional 13th field, the director republishes on change, the
wire forgets a dropped peer), `MerchantPlan.Next(..., busy)` and `AccumulateFar(..., busy)` hold the leash on the
owner while any terminal is open on him, and `GetHoverText` localises. Still yours: the flight's feel (which
screen, which part; the two synced knobs to try) and the walk-off's own log lines (`cargo merchant #N: …
(entered: …)` from your client), which say whether it was the leash the hold now covers. Backpacks 1.3.8 is
installed on StormTest and Don's client. `docs/TODO.md` §3 carries both builds' unseen lists.

**The rotating shelf (issue #56) is BUILT on `a/rotating-shelf` and its PR waits for the word.** The owner's ask,
2026-09-08: Ingvar's selling side stops being a fixed list — 20 of the 72 catalogue entries are on the shelf at a
time, re-rolled every couple of game days, seeded, the fixed Ware list gone; on the shelf an entry behaves as a
Ware, off it as a Want, stock persists across rolls, a due roll waits for no visit. Wu'barrk's read came the same
morning (two game days by default; a backpack add-on) and the owner said build. What landed: `Core/Shelf.cs` (pure:
the period clock, the seeded roll returned in catalogue order, FNV-1a + xorshift64*, not the engine's hash),
`Market.KindOf` as the ONE place the effective kind is decided, `Server.ShelfSize` 20 / `Server.ShelfRotationGameDays`
2 (0 = the fixed shelf of before), the director's roll on the first idle tick of a new period under the catalogue
swap's busy rule, `not_on_shelf`, a `cargo status` line; the client is untouched because the snapshot carries the
effective kind. 1818 checks, economy scenario 11, `docs/CATALOGUE.md` §7. **Nothing touches your files.** The four
economy questions in the issue are still yours (the Haldor anchors sold as well as bought, the round trip across a
changing twenty, the purse against a shelf of wood and hide, buying-side bases becoming selling prices), and the PR
adds two more the build raised: a flooded Want landing on the shelf sells all of it at the flooded price (cap a
landing entry at its target?), and whether a roll should be announced to players. The owner's second ask — Ingvar
buys ANY item offered, an uncatalogued sale forces a persistent common-or-rare entry — is designed in the PR body
and NOT built; the backpack add-on needs the plugin GUID(s) from you and the body half is your bake.

**Fifteen visits have now flown on the owner's Windows client against the dedicated server StormTest**: six in the
morning (10:39–11:38, PR #46's build), twenty deals over the wire, no exception from the mod on either side; three
in the evening on the audit's fixes; and six at night, on rc2 and then on your D5 (the paragraph below the audit's,
and `docs/proofs/2026-09-07-stormtest-night.md`). The record with every line is `docs/proofs/2026-09-07-stormtest-session.md`
(the evening in `2026-09-07-stormtest-evening.log.txt` beside it); CLAUDE.md's verify list carries each item's
line. Proven on a machine: the flight and the drop within a second of the simulation, Ingvar in his own body every
time, the terminal opened ON the merchant, the price curve, the Fair Market Act, both drift knobs and the purse
carry to the coin, dismissals both ways, a relog mid-visit adopting the merchant in his trading state, the export's
trader and visit rows, the catalogue verbs live. The evening added two things: **he walked backward on every
visit** (a half-turn at the attach — `Client.BodyYawDegrees`, local, default 180, PR #53; the walk forward not yet
seen), and **visit 9 on the fully fixed build was a no-show that settled D1's second regime** (two paragraphs
down). **Never yet seen:** every fix merged since the audit (its six — F1, F2, F6, F8, F9, F11 — then D1 to D4 and
the half-turn; a one-line recipe each in `docs/AUDIT-STORMTEST-2026-09-07.md` §5), the two-client items, and the
screen questions (the release over the drop point, the vanish, the bubble, the hover prompt).

**What the session found, audited the same evening** (`docs/AUDIT-STORMTEST-2026-09-07.md`, three Opus auditors
and one refuter per finding; read §0 first):
- **D1, yours: the walk-up's first approach never starts cleanly (6/6).** `CargoMerchant.ResolveCarrier` copies
  `VCargo_state` off the ZDO straight into `_state`, and that path skips every entry reset `Decide` performs — so
  `budget scaled from 0 m at entry` on every visit and **F5's scaled budget has never run on a machine**. The
  flight's seconds on the clock explain visits 1–3; visits 4–6 (give-up never / +55 s / +105 s, the merchant
  148 m and 43 m from a pilot he was dropped 13 m from) are a second regime the code alone does not explain.
  §1.4 is the diff: one `EnterState` both ways in must use, the counted seconds printed beside the budget, a
  warning on the drop's silent miss, and one log line at the ZDO-driven transition that settles the second
  regime next session. A retry and an airborne clock gate were both refuted; the reasons are there.
- **D3, yours: the reclaim WORKS; the sweep double-counts it.** `ZDOMan.DestroyZDO` only queues, and
  `FinishDeparture` sweeps in the same call, so `restart sweep: 1 stranded merchant(s) destroyed` at every clean
  end. §2: run the sweep one director tick later, name the sweep per call site. A `LastReclaimed` skip list was
  refuted twice (a failed reclaim would be skipped by the very sweep that exists to catch it).
- **D2 and D4, Don's, BUILT (PR #48):** the cooldown keyed on `s_playerID` with two probe rows; the wires register
  a peer once `IsReady()`; the client's session-end line.
- **The eleven merged fixes against the logs (§5):** F7 and N1 confirmed on a machine, F5 and F4 contradicted (the
  two above), the rest not exercised. Your `docs/TODO.md` §2 still says F5 fixed the walk-up cause; it did not.

**D1 and D3 are built, by your side: PRs #50 and #51, merged 16:17 the same day** — the audit's diffs word for
word plus your warning on the drop's silent miss and the elapsed-clock wording; #52 corrected your tracker
section. Main `84de90a` carries them with D2/D4 and the body half-turn (#53). **None of the four has been seen on
a machine yet** — except that #50's log line has now printed once and done its job. **Visit 9 (17:03, main
`84de90a`) settled §1.3's second regime: the pilot's client loses ownership of the merchant during the carry.** At
the drop your line read `carried -> approaching via the ZDO, 16.96 s after waking; carrier none, 135.7 m from the
player, watching, grounded yes; walk-up budget 90 s`: the entry reset works (90 s, not 20), but the client was
`watching`, so no `Decide` ran, he never woke, and the clock paused with nobody in range — the same shape as every
late give-up of the morning (those came ≥38 s and ≥20 s after the drops by the client log's own timestamps, not
the 3–5 s your #50 comment read). **The fix is in your file and is not in rc2:** keep the pilot as owner through
the carry (`ClaimOwnership` in `Reassert` while `Pinned`, or refuse the release until owned) and print the owner
uid in the transition line. Who takes ownership, and when, is still unknown; the evidence and the two asks are on
#50 (00:13), and audit §1.3 carries the settled note.

**The engine work, for your 1.0 track.** The probe registry (`docs/ENGINE-PROBES.md`) holds 25 named facts, 18
probed at boot; both halves of item 24 are done — the boot on StormTest, and, tonight, the moved-version
direction offline: against Steam's `default_old` server build (0.221.4 / net 35 / player 42 / world 36) the
version line reports all four numbers as moved and **all 18 probes still resolve**, so nothing we probe changed
between 0.221.4 and 0.221.12 (§8 item 4, with the steamcmd line). **Steam has a `default_pre1_0` branch on both
apps as of today** ("Last stable build before 1.0", pinned to 21981559 / 21981590): the 0.221.12 baseline stays
fetchable after 1.0 lands, and a server can pin itself there. No 1.0 build is downloadable yet. **When 1.0 lands
on 2026-09-09, Don's side will fetch it and run the probe tool against it within the hour**, no game needed, and
say which probes moved; the comparative decompile against the 244-row surface stays yours.

**Rules and facts that changed today, so you do not re-derive them:** no JSON library (`Core/Json.cs` writes the
BarrkBOT files byte-identical to Newtonsoft's; decision 3 reversed, PR #40/#43); the built DLL and the bundle
are release assets, not in git (the bundle IS attached to rc1 now); never a bare `PatchAll` — every patch class
applied on its own, `patches N/M applied` in the boot line (PR #35); ghost mode is one prefix on the static
`BaseAI.IsEnemy` (PR #37); the load-bearing set stays ServerSync only (decision 9); the catalogue verbs and the
hot swap (PR #44); Wares never drift, Wants relax over three game days (PR #45); the cooldown key is
`s_playerID` (PR #48); a peer is registered once `IsReady()`. Process: commit every code edit BEFORE a
mutate-and-restore cycle (a `git checkout --` wiped an uncommitted edit today), and `gh pr merge
--match-head-commit` wants the full sha from `git rev-parse`.

**Decisions.** Decided today by the owner: the JSON swap, the DLL untracked, catalogue edits by an admin, the two
drift knobs, ghost mode, the load-bearing set. Still open, the owner's and nobody else's (`docs/TODO.md` §1):
the client asserting its own rested/comfort numbers, and reconfirm versus teardown on a price tick.

**D5 is built by your side (PR #54, merged and deployed the same night) and SEEN — three visits, three
directions, `ours (owner -677746031)` at every drop and the first approach reaching the player every time, the
first times in fifteen visits.** Two things came back on #54, both yours: the reclaim counts run 2 / 2 / 0 by
direction (south, east, north-east) from starts that are all inside the pilot's 3x3 by `ZoneOwnership`'s own
arithmetic, so the trigger is not settled by the sector strip around a standing pilot — the ask is one line,
printed once per carry at the FIRST reclaim, with his position, the player's and the seconds since waking; and
F3's two-second vanish grace does not show in the log (end to reclaim 6 / 0 / 0 / 0 s across a dismiss, two timers
and a dismiss), which is `End` → `FinishDeparture` in your `VisitDirector`. The record of the night's six visits is
`docs/proofs/2026-09-07-stormtest-night.md`.

**What Don's side wants from yours, in order:** **your read of the shelf PR on `a/rotating-shelf`** (built; the
two new economy questions in its body, and the backpack mod's plugin GUID for the add-on); the first-reclaim line and a look at the grace (above); ~~the ownership fix~~ done (#54); ~~D1 and
D3; your `docs/TODO.md` §2~~ done (#50, #51, #52); the animator parameter names and item 23 on your server, still
yours; ~~issue #23 (your rc1 note)~~ closed with rc2; and your commits under one author name — today's arrived as
`t <t@l>`, `trial <trial@local>` and one merge authored as the model, which is what blame and the release notes
will show. ~~On #55: the branch could not be seen from here~~ — it is hidden, not absent; verified and merged.

**Don's test rig at this close-out, so the next boot is not misread.** StormTest is down, stopped cleanly at 06:50
on 2026-09-08 with **visit 15 still open in the sidecar** (the clock pauses with nobody near, so the visit the
morning boot resumed never ended; `purse`/`purseStart` 100000 is a test value Don set, kept by the carry until
reset). The next boot resumes it again — item 15's server half and F4's rebind, seen this morning as
`visit #15 RESUMED after a restart … merchant ZDO 1:60469 rebound` — unless the row is removed first (a backup
beside the file each time that was done). StormTest and Don's Gale `Default` profile carry main `a08e8c4`: D5 and
the save-path resolver, `probes 19/19 ok` on 0.221.12. Every session on Don's side is on Windows: builds, deploys
and the log watch run from a shell, the game itself needs him at the screen.

### The morning of 2026-09-07, after the rc1 merge (history)

**The tracker is `docs/TODO.md`**, cut from `main` at 8453b65 and split by owner: your track is its section 2,
Don's the section 1 decisions and screen proofs, this session's the section 3 list. **Each track edits only its
own section.** When this file and that one disagree, that one wins and this one gets fixed. Sections 1–11 below
are the original 2026-09-06 handover and survive where they do not disagree with this.

**Everything is now code, and almost nothing is proven.** `main` is at 2cf0f1c: 0 warnings, **1301/1301 off-game
checks**, `v0.1.0-rc1` tagged with the store zip attached to the release and **uploaded to no store**.

| Merged 2026-09-07 | What |
|---|---|
| **PR #22** (1289713), yours | The 0.1.0 integration, carrying **#15** (P5 the merchant), **#18** (P4's drop-point bound, from Don's P11 trust-boundary pass), **#19** (the `vc_` → `VCargo_` rename with `Core/Keys.cs`, issue #16), **#20** (your P10a continuation) and **#21** (P12, the BarrkBOT JSON export). With it: Thorium's economy decisions in `docs/DECISIONS-WUBARRK.md` — the Fair Market Act, the purse at 1500, four Wants from base 2 to 3, `PriceChangePolicy` deleted — and house rule 4's written exception for the one runtime material copy |
| **PR #17**, yours | `docs/knowledge-base/VALHEIM-API-REFERENCE/`. **The snapshot is complete now**: 67 files, nothing missing |

Earlier the same week, all merged and all described in `CLAUDE.md` Status: #7 the terminal, #8 P4 the flight,
#9 the P8 loader + P9 release pass, #10 the client proof runbook, #11 the client-path audit, #12 the economy
simulation, #13 your P10/P11 brief, #14 the knowledge base wired into CLAUDE.md with its two corrections
(a `ZDOID` is a session handle; immortality belongs on `RPC_Damage` — **both landed in code in #22**).

**ONE live visit has ever been run**, on your client, and `CLAUDE.md`'s "INTEGRATED IN-GAME RUN" is the record
of exactly what it printed. It proved: the listen-host role, a visit RESUMED off the sidecar across a restart,
a visit ended on its timer, the `MerchantPlan` state machine and its leash, **`body=Ingvar`** on a live
merchant — and the `MonsterAI.MakeTame`-before-`BaseAI.Awake` crash, which no off-game check could have found.
It did NOT prove: the glide, the drop, the walk-up completing (he gave up at 20 s and called out from where he
stood — the designed fallback, not a success), the terminal on a real visit, a trade, or the vanish. **No
two-client item has run at all**, and no deal has ever crossed the wire in a game.

**In flight on Don's side right now**, three Opus sessions, each opening its own PR:
- `a/p11-shakedown` — being rebased onto main; carries `docs/CONFIG-SHAKEDOWN.md`, `docs/TRUST-BOUNDARY.md` and
  the `VCargo_admin` caller-identity fix.
- `a/p10b-probes` — being rebased; the boot-time probes, plus the gap `docs/P10B-PROBE-GAP.md` names (it probes
  `Character.Damage` where P5 patches `RPC_Damage`) and probes for the P5 members now on main.
- The **P11d adversarial audit of P4 and P5** against the real assembly, now that P5 has landed. Findings reach
  you as an issue.
- `a/p10a-sweep` was diffed against main on 2026-09-07 and has **nothing main lacks** — your #20 carried it all
  and your copy is newer. The branch stays; nothing needs cherry-picking out of it.

**Decided by the owner, 2026-09-07: the bake is your machine's.** Don installs no Unity. So **every bake has to
reach him as a release asset** (`docs/TODO.md` §2, first item) — until `Assets/valkyriescargo_kit` is attached to
the rc1 release, the only copy of the bundle in git is inside the tracked `HexiumDist/plugins/ValkyriesCargo.dll`
and every rebuild on his machine loses Ingvar's body. Also decided: P10a is yours after P5; the knowledge base
lives at `docs/knowledge-base/`.

**Four decisions are the owner's and nobody else's** (`docs/TODO.md` §1): the Newtonsoft dependency #21 added
against the locked "BepInExPack only" row; the 4.1 MB DLL tracked in git against WORKSPLIT §4; the client
asserting its own rested/comfort numbers; and reconfirm versus teardown on a price tick, now that
`PriceChangePolicy` is deleted. Do not act on any of them ahead of the word.

**Valheim 1.0 lands 2026-09-09**, and P10a is yours: fetch the 1.0 client and server, decompile, diff against
the 244-row `docs/ENGINE-SURFACE.md`, and re-check every CLAUDE.md engine fact the diff touches. Any change in
the surface is a stop-ship. The baseline was deliberately captured before it (`docs/ENGINE-BASELINE.md`).

## 1. What this is, in five lines

A Raven Iron Valheim mod. A Valkyrie drops a merchant, Ingvar the Far-Travelled, beside a player's base
at a random moment when they are rested and comfortable. He walks up, calls out, buys and sells from a
live, persistent stock at supply-and-demand prices for five minutes, then vanishes the way Odin does.
Every client sees the same visit; the server owns config, schedule, event, market, clock and the
objects' existence; a client simulates motion; your inventory stays yours, touched only after the
server answers. The trade window is our own, drawn with Wu'barrk's VikingOS theme compiled in.

## 2. Read these, in this order (30 minutes)

1. `docs/TLDR.md` — one screen for the design, one for the catalogue.
2. `docs/WORKSPLIT.md` — §0 the packages to divide (your first job), §2 the contract (now code).
3. `docs/DESIGN.md` — v3, the design of record. §0 is the table of every place the partner's drafts
   and the engine disagreed, with the decompiled body that decided it; §8 the decisions; §11 the body.
4. `CLAUDE.md` — build commands, layout, the house rules, the "not ours" list, what is verified.
5. `docs/REVIEW-v5-2026-09-06.md` — why v5 changed; read if your owner asks "what happened to X".
6. `docs/CATALOGUE.md` and `docs/data/items-valheim-2026-07-31.tsv` — the market's numbers and the
   item table they were checked against (extracted from Wu'barrk's own TheEye dump).
7. `docs/HANDOFF-WUBARRK.md` — the human-facing version of this file, including the Linux build notes.

## 3. Ground truth, and how it was established

Every engine claim in the design was read from the **decompiled body** of the installed
`assembly_valheim.dll` (`ilspycmd`, 2026-09-06), never inferred from a signature. ServerSync's rules
were read from its source (`ConfigSync.cs`, master). The facts your work will lean on:

- The installed Valheim runs on **Unity 6000.0.61f1**; asset bundles must be built with that Editor.
- **ServerSync broadcasts on change only**; no heartbeat; client writes are rejected while locked
  unless the client is an admin; payloads under 10 000 bytes go uncompressed.
- **Only an owner's ZDO writes replicate.** The merchant is client-owned, so market state never lives
  on his ZDO; it travels as two ServerSync custom values (`VisitState`, `MarketState`).
- **Objects exist on a client only inside its active zone block** (`ZNetScene.InActiveArea`,
  `|zone − centre| ≤ m_activeArea − 1`, 64 m zones). The bird starts ~90 m out, not 500 m.
- **Comfort never leaves the client**; the client writes `vc_rested`/`vc_comfort` (renamed
  `VCargo_rested`/`VCargo_comfort` on 2026-09-07) on its own ZDO.
- **`ZRoutedRpc.instance` is null for all of plugin `Awake`**; routed RPCs are forgeable (the
  packet's own sender/target); money rides the direct peer `ZRpc`.
- **VikingOS**: every class is `internal`; it is a beta; the reuse is **shared source** (the two
  files in §5), never a runtime dependency. Its escrow's delivery-id inbox rule is ported into our deals.
- A vanilla character has a `CapsuleCollider` and a `SkinnedMeshRenderer`; the animator parameters
  vanilla drives are listed in DESIGN §11.

If you need a fact that is not written down, decompile it and write it down (CLAUDE.md "Engine facts"
or DESIGN §0). Do not reason from a member's name.

## 4. What exists, and what is proven

| | State |
|---|---|
| Repo, csproj (net472), tools, tests harness | built; `dotnet build` 0 warnings; `tools\run-tests.ps1` |
| ServerSync compiled in (`Libs/ServerSync.cs`, MIT-0) | armed: `ModRequired`, minimum version = current |
| Config surface (`Server.*` synced+locked, `Client.*` local) | bound; parsed catalogue re-parses on change |
| `cargo status | version | prefab <name>` console | built |
| Catalogue parser, 72 defaults checked against the item table | tests |
| **Contract** (PR #1) | built; reviewed, no blockers |
| **Market core** (PR #3), **eligibility + event** (PR #5), **deal wire + persistence** (PR #6) | built; see §0 for the proofs |
| Headless boot on a dedicated server | **seen** 2026-09-06 |
| Client boot and `cargo status` (items 2, 6) | **seen** 2026-09-07 on your Linux client |
| Version wall, config lock, prefab dumps, and every other client-side proof | not yet seen (needs a screen; CLAUDE.md items 3, 4, 5, 7–18) |
| Flight (P4), merchant (P5), terminal (P7), body (P8), release (P9), exports (P12) | **all built and merged** (PR #22); the terminal has never been drawn on a screen and the flight has never been watched |
| The whole loop end to end — glide, drop, walk-up, callout, terminal, trade, vanish | **never run.** It is the item that gates the store |

## 5. What Wu'barrk's side uniquely has

- `libs-Tools/SharedUI/GiltFrameTheme.cs` and `UIFocus.cs` — the terminal cannot start without them.
  They go to `ValkyriesCargo/Libs/SharedUI/` with the origin header shown in `HANDOFF-WUBARRK.md` §4.1.
- The trade code (`BarrkUI/Trading/*`) as reference only; `docs/reference/vikingos-trading/` if shared.
- The model (`norse_dwarf_merchant-resized.glb`, 1.37 m, the source of truth) and a Unity
  6000.0.61f1 Editor. The GLB is a statue today: 1.85 M triangles, no rig, no clips. DESIGN §11 lists
  what it needs; nothing about it is on the 0.1 path.
- His knowledge base (`Brain of the Wubarrk Clan`): the JOTUNN/headless facts §7–§8 the pipeline
  cites (there is no §9), the API reference, the item dump.

## 6. The rules you inherit (do not re-derive them; they came from measured failures in the siblings)

1. Harmony prefixes at `Priority.Low` honouring `__runOriginal`; result-decorating postfixes at default
   priority; never a max- or high-priority replace. Field injection (`___m_nview`) over reflection.
2. One `Update` per mod (`CargoTick`); one `OnGUI` (the terminal); no long-lived coroutines.
3. Every patch body its own try/catch, logging at most three times; cosmetics off the gameplay path.
4. Never patch `EnvMan`; never touch materials, textures or shaders at runtime.
5. Publicized assemblies are compile-time only: our code names no private member.
6. Never move what you do not own (only the owner's ZDO writes replicate).
7. "Not ours" files (`Libs/**`) are replaced from upstream, never edited.
8. No runtime dependency on VikingOS, Jotunn or any plugin. No binaries in the repo.
9. Prove a new test fails without its fix. Verified facts go into `CLAUDE.md` with the date and the
   exact log line. A clean build proves nothing about member access; an in-game run does.
10. Say what is verified and what is not, in every doc and every PR. Nothing is "done" until seen.

## 7. The contract you code against (PR #1, `docs/WORKSPLIT.md` §2)

- The terminal renders `CargoRpc.Market` / `CargoRpc.Visit` and **never computes a price**.
- It sends a `Deal` (wanted line, offered lines, coins, the unit prices the player saw) through
  `CargoRpc.Send` and mutates the inventory **only** inside `onAnswer` when `Ok`, by `ItemsToAdd`,
  `ItemsToRemove`, `CoinsDelta`. `price_changed` carries the whole new market; policy is reconfirm.
- `CargoRpc.UseDemo(true)` gives a full in-process market so the window is buildable with no world;
  `cargo terminal demo` is the console entry to add.
- The merchant will call `CargoTerminalHost.Instance.Open(merchantGameObject, visitId)`; the terminal
  assigns `Instance` at plugin `Awake` and closes itself beyond 5 m, on Escape/Use, on inventory or map
  open, on death, and when `Visit.Phase` becomes `Leaving`. Release the cursor request on close and on
  logout (`UIFocus`; the cursor-leak lesson in Wu'barrk's BarrkUI §6).

## 8. Your first job: divide the packages (superseded by §0: the split was never written; take P7 and P8, and P4/P5 if you get there first)

`docs/WORKSPLIT.md` §0 is the neutral list (P1 done; P2 market core; P3 comfort+event; P4 flight;
P5 merchant; P6 deal wire+persistence; P7 terminal; P8 body; P9 release). Constraints that are real:
P3–P5 need a dedicated server and two client accounts on one machine (Don has both); P7 needs the
SharedUI files (Wu'barrk has them); P8 needs Unity 6000.0.61f1 (Wu'barrk has it); P9 needs the
RavenIronStudios store account (Don). Everything else is a free choice; P2, P6, P7 can run in parallel
from today.

Do this:
1. Read §0 with your owner. Ask which packages he wants; the natural pulls are P7 and P8, but it is
   his call, and taking P2 or P6 as well is welcome (both are pure C# with the harness already there).
2. Write the agreed division into `docs/WORKSPLIT.md` §1 (replace the proposal table), on a branch
   `b/split`, and open a PR. Keep §0, §2, §4 and §5.
3. Comment on PR #1 with your read of the contract: anything the terminal needs that is missing, any
   name you would change, and the three notes already left there (extra reason tokens; `inventory_full`
   is the terminal's pre-check; `visit_over` is real-server only). That comment is what unblocks the
   merge; the contract does not change without it.
4. Vendor the two SharedUI files (§5) in the same PR or a `b/sharedui` PR; build once.
5. Then start on the packages you took, one branch per package (`b/<topic>`), PRs into `main`, tests
   green before every PR, `main` always building and booting headless.

## 9. How the two sessions talk

GitHub is the channel: PR descriptions, PR comments, and `CLAUDE.md` status lines. Each session's
owner relays anything urgent in Discord. Do not edit the other side's folders (WORKSPLIT §1 lists them
once agreed); a need there is a PR comment or an issue. Contract files (`Core/Wire.cs`,
`MarketSnapshot.cs`, `VisitSnapshot.cs`, `Deal.cs`, `DemoMarket.cs`, `Net/CargoRpc.cs`,
`Client/Terminal/ICargoTerminal.cs`) change only by a PR the other side has commented on.

## 10. Open questions the two owners settle (not the sessions)

1. Tear-down versus reconfirm on a price tick (reconfirm is in, provisional; `PriceChangePolicy` config).
2. Where bundles get built (Wu'barrk's box has the right Editor).
3. Whether the rigged body arrives with clips or we plan on Mixamo (PR #4 says rigged and animated).
4. ~~Vendored copies of SharedUI with headers, or a submodule~~ vendored, PR #2.
5. **Binaries in the repo** (PR #4 versus WORKSPLIT §4).
6. The five PROPOSED rows in DESIGN §8 (see §0).

## 11. Commands

```
tools\fetch-libs.ps1                                   # Windows: libs\ from the Steam install
dotnet build ValkyriesCargo\ValkyriesCargo.csproj
dotnet run --project tests\CoreTests\CoreTests.csproj --nologo    # 852 checks, no game (net8.0)
tools\package.ps1                                      # store zip in dist\
```
On Linux, `libs/` is a symlink to `libs-Tools` with the names listed in `HANDOFF-WUBARRK.md` §2.
In game: `cargo status | version | prefab <name> | stock [prefab] | deal buy|sell <prefab> [count] | claim`;
admin: `cargo visit [player] | dismiss | reset | save`.
