# Handoff for Wu'barrk — Valkyrie's Cargo, 2026-09-06; section 0 re-cut 2026-09-08 morning: #55 merged, 1.0 held, the rotating shelf waiting on you

The repo is scaffolded, builds clean, tests pass, and boots headless on a dedicated server. This is
what it is, what of yours is already in it, what we need from you, and exactly where each thing goes.

## 0. Where things stand, 2026-09-08 morning: #55 merged, 1.0 held, the rotating shelf waiting on you

**Your list is `docs/TODO.md` section 2**; when this file and that one disagree, TODO wins. This morning's
state is kept below as history.

`main` is at a08e8c4 (your #55 merged the morning of 2026-09-08; the close-out docs commit on top): 0 warnings,
**1722 checks**, `probes 19/19 ok`, the day's PRs #24 to #53 in `CHANGELOG.md` "0.1.0-rc2" and #54 and #55 under
"Since 0.1.0-rc2". **`v0.1.0-rc2` is cut** at e4ee83c (2026-09-07 evening, at the owner's word): the store zip and
the bundle attached, a pre-release, uploaded to no store — **it carries neither D5 nor your save-path resolver**.
**The rc1 tag is marked superseded and still must not reach a tester**: it carries F1, and the ServerSync gate does
not tell the cuts apart (all are 0.1.0), so every copy is replaced by hand. Your rc1 note (issue #23) is closed.
`Assets/valkyriescargo_kit` is attached to both releases, so Don's builds carry Ingvar. **The owner's conditions
for the next cut (2026-09-08 00:40) are met but for the vanish being watched**; the cut is on his word, rc3 or
0.1.0 proper.

**Valheim 1.0: your sweep verified from Don's machine, and HELD by the owner.** The `public-test` branch is
password-protected and hidden from the branch list — that is why it read as absent last night, not because it
was; it is fetched here now (build 23105022, 0.221.13, server only). Our probe tool on it: the version line
unreadable (renamed constants), `zone_maths` FAILED on the `Vector2s` overloads, three probes THREW on the
one-argument `GetStableHashCode`, your `save_path` PASSED; the decompile shows the `GetAllCharacterZDOS` early
return word for word. Fix designs exist on Don's side (a pure hash rebinding all 13 of our sites through the
namespace, a peer-list gather, both version names, the zone type at runtime; a ServerSync-namespace shim for its
three sites, flagged as the owner's call). **The owner said "dont change anything for 1.0": nothing is built, and
neither side starts without his word.** The client axis is unswept everywhere.

**Later the same morning: your playtest, and what is yours from it.** The shelf rolled on StormTest as designed
(PR #57 still waits for the word), you joined by join code with Yggdrasil's Reckoning 0.1.1 on both sides, and
your seven-item report is split on **issue #59**. Built on our side already, on `a/terminal-ux`: the count box and
"all" on every staged line, the Coins/Barter switch gone (YOU GET / YOU GIVE, one balance line, "Cover it with
my goods" whenever a ware is staged), and the 40 % black backdrop with brighter text. Yours: the flight's feel
(say which screen and which part; try `Server.FlightSpeed` 12 / `Server.FlightTurnRate` 30 first, no build),
the walk-off (attach your client log's `cargo merchant #N: … (entered: …)` lines; the fix we propose is "he never
walks while a terminal is open on him" — `VisitState.terminalsOpen` from our wire, a `busy` input to
`MerchantPlan` from you), the one-line `Localization.instance.Localize(...)` in `CargoMerchant.GetHoverText`, and
the Smoothbrain Backpacks test with the expected behaviour written out.

**The rotating shelf, issue #56, is BUILT on `a/rotating-shelf`; the PR waits for Don's word.** Your read came the
same morning (two game days by default; the backpack add-on) and the owner said build. Ingvar's selling side stops
being a fixed list — 20 of the 72 catalogue entries on the shelf at a time, re-rolled every two game days, seeded
from the world's salt and the period (nothing persisted, nothing sent: a restart shows the same shelf), the fixed
Ware list gone; on the shelf an entry behaves as a Ware, off it as a Want, stock persists across rolls, a due roll
waits for no visit and logs `shelf rolled: …`. `Server.ShelfSize` 20 (0 = the old fixed shelf) and
`Server.ShelfRotationGameDays` 2. Nothing of yours is touched; the client is untouched. The four economy questions
in the issue are still yours: the Haldor anchors sold as well as bought, the round trip across a changing twenty,
the purse against a shelf of wood and hide, buying-side bases becoming selling prices — and two the build raised:
a flooded Want lands on the shelf with all of it for sale at the flooded price (cap a landing entry at its target?),
and whether a roll should be announced to players. The backpack add-on needs the plugin GUID(s) to detect and your
bake for the body; the owner's buy-anything ask is designed in the PR and not built.

**Fifteen visits flew today on Don's Windows client against StormTest**: six in the morning, twenty deals, no
exception; three in the evening on the audit's fixes; six at night on rc2 and then on your D5 (item 1 below and
the record). Ingvar landed in his own body every time, within a second
of the simulation; the terminal opened on him; the prices, the Fair Market Act, the drift and the purse carry all
matched the design to the coin; a relog mid-visit handed him to the new client still trading; the export wrote its
trader and visit rows. The record is `docs/proofs/2026-09-07-stormtest-session.md`, the evening's log beside it.
The evening found two things: **he walked backward on every visit** — a half-turn at the attach, fixed as the
client knob `Client.BodyYawDegrees` (default 180, PR #53), the walk forward not yet seen — and **visit 9, on the
build with all four fixes, was a no-show** (item 1 below). Not yet watched: whether she lets go over the drop
point, the vanish, the callout bubble, the hover prompt, and every fix merged since the audit (its six, then D1 to
D4 and the half-turn; `docs/AUDIT-STORMTEST-2026-09-07.md` §5 says what would exercise each, one line each).

**The walk-up, and what the audit found (`docs/AUDIT-STORMTEST-2026-09-07.md`, read §0):**
1. **He reached Don on every visit — but never on the first attempt.** The first approach after the drop gives
   up with `budget scaled from 0 m at entry`, 6/6: the drop writes his state onto the ZDO, `ResolveCarrier` copies
   it straight into `_state`, and that path skips the reset `Decide` does, so F5's scaled budget has never run on
   a machine. On visits 4–6 the give-up came a minute or two late with him far from the pilot, which the code
   alone does not explain; the diff in §1.4 adds the one log line that settles it. **Built by you as PR #50 and
   merged the same evening.** Your line has printed once, on visit 9, and it settled the late give-ups: **the
   pilot's client loses ownership of the merchant during the carry.** At the drop it read `carried -> approaching
   via the ZDO, 16.96 s after waking; carrier none, 135.7 m from the player, watching, grounded yes; walk-up
   budget 90 s` — the entry reset works (90 s, not 20), but the client was `watching`, so no Decide ran and he
   never woke. **The fix is yours and is not in rc2:** keep the pilot as owner through the carry
   (`ClaimOwnership` in `Reassert` while `Pinned`, or refuse the release until owned) and print the owner uid in
   the line. **Built by you as PR #54, merged and deployed the same night, and SEEN: visits 13, 14 and 15 read
   `ours (owner -677746031), 2 / 2 / 0 reclaim(s)` at the drop and the first approach reached Don every time —
   the first times in fifteen visits.** Who takes ownership, and when, is still not known: all of tonight's starts
   are inside the pilot's 3x3 by your own `ZoneOwnership` arithmetic, so the reclaim counts by direction do not
   fit the sector strip around a standing pilot. The ask on #54 is one line, once per carry, at the first reclaim.
2. **The visit-end sweep reports a stranded merchant at every end.** The reclaim works; `DestroyZDO` only queues,
   and the sweep runs in the same call. §2: sweep one tick later. **Built by you as PR #51 and merged**; a clean
   end now prints `merchant and bird reclaimed …` and no sweep line at all.
3. **The cooldown key and two wording lines** were Don's and are built (PR #48).
4. **Your merged fixes against the logs (§5):** F7 and N1 confirmed on a machine; F5 and F4 contradicted (items 1
   and 2); F1, F2, F6, F8, F9, F11 never exercised. `docs/TODO.md` §2 still says F5 fixed the walk-up cause.

**Valheim 1.0 on 2026-09-09.** Steam already has a `default_pre1_0` branch on both apps ("Last stable build
before 1.0", today's builds), so the 0.221.12 baseline stays fetchable after 1.0 lands and a server can pin
itself there if the mod needs a day. No 1.0 build is downloadable yet. The probe registry was run tonight
against the previous stable build (0.221.4) and reported every version number as moved with all 18 probes
still resolving (`docs/ENGINE-PROBES.md` §8 item 4). **When 1.0 lands, Don's side fetches it and runs the
probe tool against it within the hour** and tells you which probes moved; the comparative decompile against the
244-row surface is still yours.

**Decided today by the owner:** no JSON library after all (`Core/Json.cs`; decision 3 reversed), the DLL
untracked (release assets), the catalogue verbs, the two drift knobs (Wares never, Wants three days), ghost mode,
the load-bearing set. **Still his, not ours:** the client-asserted comfort numbers, reconfirm versus teardown.

**From you, in order:** **your read of the shelf PR** (built on `a/rotating-shelf`; the two new economy questions,
and the backpack mod's plugin GUID); the first-reclaim line (item 1) and a look at F3's grace — tonight's ends reclaimed him
6 / 0 / 0 / 0 s after `ended` across a dismiss, two timers and a dismiss, where `VanishGraceSeconds` says 2 (both
on #54); ~~the ownership fix~~ done (#54); ~~the walk-up, the sweep, your TODO §2~~ done (#50, #51, #52); the
animator parameter names and item 23 on your server; ~~the three rc1 things in issue #23~~ closed with rc2; and
one author name on your commits (today's came as `t`, `trial` and one merge authored as the model).

**Don's rig at this close-out:** StormTest is down (stopped cleanly 06:50, 2026-09-08) with **visit 15 still open
in the sidecar** and `purse` 100000, a test value he set. The morning boot on main a08e8c4 resumed that visit and
rebound the merchant (`visit #15 RESUMED after a restart … merchant ZDO 1:60469 rebound`: item 15's server half and
F4's rebind, seen), and the next boot does it again unless the row is removed first. StormTest and his Gale
`Default` profile carry main a08e8c4: D5 and your save-path resolver, `probes 19/19 ok` on 0.221.12. The record of
the night's six visits is `docs/proofs/2026-09-07-stormtest-night.md`.

### The morning of 2026-09-07, after the rc1 merge (history)

**Your list now lives in `docs/TODO.md` section 2.** That file is the tracker — three tracks, one per owner,
each editing only its own section — and it is cut from `main` at 8453b65. Everything below in this section is
the history of how we got here; when the two disagree, TODO wins.

`main` is at 2cf0f1c: 0 warnings, **1301 checks**, `v0.1.0-rc1` tagged with the store zip on the release and
**uploaded to no store**. Your PR #22 merged the whole 0.1.0 integration (P5 the merchant, P4's drop bound,
the `VCargo_` rename with `Core/Keys.cs`, your P10a continuation, and P12 the BarrkBOT export) with Thorium's
economy decisions in it — the Fair Market Act, purse 1500, four Wants to base 3, `PriceChangePolicy` gone —
and PR #17 completed the knowledge-base snapshot at 67 files.

**One live visit has been run, on your client**, and `CLAUDE.md`'s INTEGRATED IN-GAME RUN is its record.
Ingvar landed in his own baked body, the state machine and the leash worked, a visit resumed off the sidecar
and another ended on its timer — and he **gave up walking after 20 s** and called out from the drop point,
which is the designed fallback and not a success. The glide, the drop, the walk-up completing, the terminal
on a real visit, a trade and the vanish have never been watched, and no two-client item has run.

**Three things TODO §2 needs from you first**, and the first one blocks Don's whole screen-proof track:
1. **Attach `Assets/valkyriescargo_kit` to the v0.1.0-rc1 release**, and to every release after a re-bake.
   The bake is your machine's (the owner decided that on 2026-09-07: Don installs no Unity), so a bake that
   never leaves your box means every build on his side loses the body — today the only copy in git is inside
   the tracked `HexiumDist/plugins/ValkyriesCargo.dll`.
2. **The walk-up**, the 20-second timeout above. Your client, your P5, and cheaper for you to find than for
   Don to burn screen time on the same wall.
3. **P10a against Valheim 1.0 on 2026-09-09.** Any change in the 244-row surface is a stop-ship.

Also on your list there: the client-only proofs (the animator parameter names cannot be read on a dedicated
build — see `CLAUDE.md`'s engine facts), item 23 (the export live on your dedicated server), the truth pass on
your own files (`README.md`, `CHANGELOG.md`, `models/README.md`, and the reason four Wants went to base 3,
which is only in the release note), and closing issue #16.

Two of the four owner decisions in TODO §1 land on your work if they go the other way: the Newtonsoft
dependency P12 added, and the tracked DLL. Neither is yours to act on before the word.

---

### The evening of 2026-09-07: your list as it stood then (history)

Where main was: P1, P2, P3, P6 and P7 merged; the P8 loader and the P9 release pass being reviewed
and landing that night; three more branches following them (a client proof runbook with deploy scripts, an
economy simulation, a decompile audit of the never-run client paths). PR #8 had a review on it.
Don merged everything; nothing below asked you to merge.

1. ~~**PR #8: answer the review and push the fix.**~~ DONE 2026-09-07: answered 05:09, verified on Don's side
   (1078 checks with main merged; the flight table reproduced), merged as 1884fcd. The rest of this item is history.
   Originally: Four findings, all on the flight, all from simulating
   `Fly` with the prefab's real speed 20 and turn rate 20 (a 57 m turning circle):
   - the turn-in point sits inside that circle, so the bird never reaches it and orbits until
     `MaxFlightSeconds`; straight in works (drop at 3.8 s), or a turn rate of our own (60 deg/s works);
   - `CargoFlight.Awake` rebuilds a different turn-in from the one `FlightPlan` planned (other distance,
     other side, no block clamp): write `vc_turn` beside `vc_target` (renamed `VCargo_turn`/`VCargo_target`
     on 2026-09-07), or delete the swing on both sides;
   - `DescentY = StartY`, so the drop fires about 105 m up; give the turn-in a descent altitude and/or
     the bird a speed of our own (8 m/s from 120 m reaches 14 m in 20 s, which is also the 15-20 s
     design 3.2 wanted the sky to hold);
   - every visit authors a persistent vanilla Dverger until P5 exists: gate the merchant in `Author`
     behind P5, or have `Clear()` destroy him. Until P5 lands, the merged DLL must not go on a live server.
   Your two questions are answered on the PR: the three `VisitDirector` seams stay as they are, and
   `FlightPlan` stays in `Core/`. Say on the PR when the branch is ready.

2. **Before you bake: the bundle contract moved under you tonight (P8 loader), in your favour.**
   - The bake needs **no AnimatorController and no ZSyncAnimation entries**. The loader plays the six
     clips by name (`Walk`, `Idle`, `Talk`, `Hello`, `Shrug`, `Nod`) through a `PlayableGraph`; design
     11.4 is rewritten to say so. `tools/unity/IngvarBundleBuilder.cs` as it stands produces exactly
     what is needed.
   - Two one-line corrections to `models/README.md`, yours to make: section 6's
     `using (Stream s) { LoadFromStream(s); }` disposes a stream that `AssetBundle.LoadFromStream` reads
     lazily (the loader holds it open in a static); section 2's "set `BodyPrefab` to Ingvar" is wrong for
     the code as built: `BodyPrefab` stays the engine prefab your `Spawner` writes into the ZDO, and the
     new `Server.CustomBody` (synced and locked, default true) is the switch.
   - The clip sub-assets must come through under the six take names. On a client `cargo body` prints
     each clip found or MISSING with its length, `SkinnedMeshRenderer`, bones (expect 24), triangles
     (expect 31112) and the ground offset (expect about 0). That is the gate for a first bake, beside
     the 64 KB rule in `models/SETUP-FOR-CLAUDE.md`.
   Then `tools/setup-ingvar-unity.ps1`, the Editor menu or the CLI line in SETUP section 5, and `-Embed`
   copies it to `Assets\valkyriescargo_kit` (gitignored; the csproj embeds it when the file exists). Get
   the file to Don outside git: a release asset on the repo, or a direct transfer. `cargo body preview`
   on any client then stands him up with no merchant and no server.

3. **P5: the seams exist now.**
   - `CargoTerminalHost.Instance.Open(merchant, visitId)` opens the Cargo Terminal (`ICargoTerminal`).
   - `BodyLoader.Attach(Character)` returns an `IngvarBody`, or null when the stand-in stays (no bundle,
     `CustomBody` false, a dedicated server). Call it from `CargoMerchant.Awake`, on every machine.
     `IngvarBody.Greet() / Talk() / Shrug() / Nod()` fire the one-shots and return false when refused
     (the same gesture already playing). Idle and Walk need nothing from you: they follow the body's
     own movement.
   - `vc_dismiss` is on the wire; `VisitSession`'s phases and `SetDrop` are the director's; `vc_state`,
     `vc_carrier` and `vc_seed` are your `Spawner`'s keys. (All four renamed to the `VCargo_` prefix on
     2026-09-07.)
   The rest is design 3.3: the carry pin and `InIntro`, the drop handoff, follow and callout, immortal,
   dismissal, the Odin vanish, the restart sweep.

4. **One screen proof that takes two minutes and needs no server: item 17.** Build main, put the DLL in
   your client's `BepInEx\plugins\`, and from the main menu run `cargo terminal demo`. Nobody has seen
   the window yet and you have the client. Paste `terminal opened: visit #1 (demo)`,
   `terminal closed: escape` and what you saw into `CLAUDE.md` Status. If it draws wrong, that is the
   first bug of the evening and it is Don's. In a world, `cargo prefab odin` settles `m_ttl` (your 60
   against the compiled 300); paste that line too.

5. **Incoming tonight, to read and not act on:** the P8+P9 PR (the loader's `Attach` and `IngvarBody`
   are the seam you code against; README is rewritten as a truth pass), then the runbook
   (`docs/PROOF-CLIENT.md` with `tools/deploy-test.ps1`, `tail-log.ps1`, `set-test-config.ps1`), the
   economy simulation (`docs/ECONOMY-SIM.md`) and the client audit (`docs/CLIENT-AUDIT.md`). The runbook
   is the checklist for your two-client evening once P5 stands. (All four landed: PRs #9 to #12, main at
   1039 checks.)

6. **One decision for the two of you, from the simulation** (`docs/ECONOMY-SIM.md`, verdict item 1): buying a
   shelf out and selling it straight back is profitable, because `MaxPriceMultiplier` 3.0 times `SpreadBuy` 0.7 is
   2.1, and it drains his whole purse on the first visit with the shelf left where it started. The cheap fix is one
   clause in `Market.Pays`: a Ware he sells is bought back at par at most. The config fix is the multiplier down to
   1.4, which flattens the scarcity signal. Say which; it goes into DESIGN section 8 and one of us builds it.

7. **Night close, 2026-09-07 (Don asleep; his Claude carries the merges):** PR #15 is under an Opus
   adversarial review and merges when its findings are answered on the PR; issue #16's rename is Don's,
   after #15 and P11 land; P10a is yours after P5; the knowledge base is at `docs/knowledge-base/` (add
   `VALHEIM-API-REFERENCE`); three PRs of Don's (P10a tooling, P10b, P11) arrive overnight, read them in the
   morning. `docs/HANDOFF-CLAUDE.md` section 0 is the full state.
   **What actually happened:** the agents were stopped at the owner's word before any of those three PRs was
   opened, and PR #15 was never merged as itself — you carried it, and everything else, into **PR #22**. The
   three branches (`a/p10a-sweep`, `a/p10b-probes`, `a/p11-shakedown`) are still unmerged; `a/p10a-sweep` holds
   nothing main lacks, and the other two are being rebased on Don's side now.

## 1. Where things are

| Read first | What it is |
|---|---|
| `docs/TLDR.md` | one screen for the design, one for the catalogue |
| `docs/DESIGN.md` | v3, the design of record: your v5 merged with our v2 after the review |
| `docs/REVIEW-v5-2026-09-06.md` | what changed from your v5 and the engine fact behind each change |
| `docs/CATALOGUE.md` | the 72 catalogue entries with every number's reason |
| `CLAUDE.md` | build commands, layout, house rules, the "not ours" list, what is verified |

Layout: `ValkyriesCargo/` is the plugin (net472), `tests/CoreTests/` the off-game harness,
`tools/` the scripts, `libs/` the game DLLs (gitignored; see §2), `docs/` everything above.

## 2. Building on your machine

The csproj expects the game and BepInEx assemblies in `libs\` at the repo root. On Windows,
`tools\fetch-libs.ps1` copies them from the Steam install. On Linux, point it at your `libs-Tools`:
the files it needs are `assembly_valheim_publicized.dll`, `assembly_utils_publicized.dll`,
`assembly_guiutils_publicized.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`,
`UnityEngine.IMGUIModule.dll`, `UnityEngine.TextRenderingModule.dll`, `UnityEngine.InputLegacyModule.dll`,
`UnityEngine.UIModule.dll`, `UnityEngine.UI.dll`, `Unity.TextMeshPro.dll`, `UnityEngine.PhysicsModule.dll`,
`UnityEngine.AnimationModule.dll`, `BepInEx.dll`, `0Harmony.dll`. A symlink `libs -> ../libs-Tools`
works if the names match; the publicized ones must carry the `_publicized` suffix.

```
dotnet build ValkyriesCargo/ValkyriesCargo.csproj          # the mod
dotnet run --project tests/CoreTests/CoreTests.csproj       # 27 tests, no game needed
```

ServerSync is already in as `ValkyriesCargo/Libs/ServerSync.cs` (your shared-source pattern, not
ILRepack). Its header says where it came from; do not edit it, replace it from upstream.

## 3. What of yours is already in

- **The design.** v5's escrow ideas (nonce ring, the price the player saw in every deal, no inventory
  mutation before the server answers), the asset-pipeline lessons (§6.3–6.6), the `AttachPoint` anchor.
- **The item data.** `docs/data/items-valheim-2026-07-31.tsv` was extracted from your TheEye
  `Values_Dump.json` (2026-07-31): prefab, display name, type, stack, weight, vanilla value,
  teleportable, token. The catalogue and its tests were checked against it.
- **Your JOTUNN/headless facts** §7–§8 are cited for the Unity pipeline. There is no §9 in the copy
  here; the "windows-mono not needed" claim is marked unverified.

## 4. What we need from you, and where it goes

### 4.1 The two shared-source files (blocks the terminal)

From your `libs-Tools/SharedUI/`:

```
ValkyriesCargo/Libs/SharedUI/GiltFrameTheme.cs
ValkyriesCargo/Libs/SharedUI/UIFocus.cs
```

Add a header to each, same shape as `Libs/ServerSync.cs`:

```csharp
// NOT OURS. VikingOS SharedUI by Wubarrk - libs-Tools/SharedUI/GiltFrameTheme.cs at VikingOS 0.9.8,
// 2026-09-06. License MIT. Vendored as shared source; update from libs-Tools, never edit here.
```

Keep the namespaces as they are (`SharedUI`). The csproj globs every `.cs` under `ValkyriesCargo/`,
so dropping the files in is the whole integration; build once to confirm no missing reference.
`UIFocus` carries two Harmony patches of its own (`GameCamera.UpdateMouseCapture`, `Chat.HasFocus`);
they are expected and listed in the design as shared-source patches.

We will **not** reference `VikingOS.dll` at runtime: every class in it is internal, it is a beta,
and Cargo players on other servers may not run it. The terminal is drawn with your theme so it
looks native beside VikingOS when both are installed.

If you would rather publish `SharedUI` as a git submodule or a tagged tarball, say so and we will
consume it that way instead of vendoring.

### 4.2 The trade code, as reference only

`BarrkUI/Trading/TradeEscrow.cs`, `TradeInbox.cs`, `TradeRpc.cs`, `TradeItem.cs`, `TradeWindow.cs`
would help as **reading material** (the delivery-id inbox and redelivery rule are being ported into
our merchant deals, without Newtonsoft). Put them under `docs/reference/vikingos-trading/` if you
share them; nothing compiles from there.

### 4.3 The body (not on the 0.1 path)

The resized GLB (1.37 m) is the source of truth. Before it can be an NPC:
1. retopology or decimation to ~30k triangles, maps re-baked at 2048²;
2. a Humanoid rig with skin weights, plus an `AttachPoint` empty between the shoulder blades;
3. clips: idle, walk, talk gesture, hang;
4. an Animator Controller that declares what vanilla drives: floats `forward_speed`, `sideway_speed`,
   `turn_speed`, `tilt`, `statef`; bools `onGround`, `falling`, `inWater`, `encumbered`, `flying`,
   `sleeping`, `sitting`, `freeze`, `blocking`; int `statei`; triggers `attack`, `stagger`, `alert`,
   `interact`, `consume`, `eat`, `jump`, `equip_hip`, `fly_takeoff`, `fly_land`. At minimum the
   locomotion set, or he slides;
5. the bundle built with **Unity 6000.0.61f1** (the installed game's runtime; a newer Editor will not
   load), `SkinnedMeshRenderer` + `CapsuleCollider`, material baked at build time, target 5–10 MB.
Full plan: `docs/DESIGN.md` §11 and `docs/REVIEW-v5-2026-09-06.md` §2. Deliver the bundle plus the
prefab name it contains; the loader swaps it under the same `Humanoid`/`MonsterAI` prefab clone.

### 4.4 Data refreshes after a game update

Re-run TheEye's `dump_eye`, then regenerate the table (Node, from the dump folder):

```
node -e "const fs=require('fs');let s=fs.readFileSync('Values_Dump.json','utf8');if(s.charCodeAt(0)===0xFEFF)s=s.slice(1);const a=JSON.parse(s).filter(r=>r._Source==='Items');const g=(r,k)=>r[k]==null?'':String(r[k]);fs.writeFileSync('items.tsv','prefab\tdisplay\ttype\tstack\tweight\tvalue\tteleportable\ttoken\n'+a.map(r=>[r._Key,g(r,'_displayName'),g(r,'m_shared.m_itemType'),+g(r,'m_shared.m_maxStackSize')||0,+g(r,'m_shared.m_weight')||0,+g(r,'m_shared.m_value')||0,g(r,'m_shared.m_teleportable'),g(r,'m_shared.m_name')].join('\t')).join('\n'))"
```

Drop the result in `docs/data/items-valheim-<date>.tsv`, point the test harness at it
(`tests/CoreTests/Program.cs`, one constant), run the tests: a renamed prefab fails on the desk.

## 5. Decisions you should know were made

- **Custom terminal, locked** (owner, 2026-09-06), drawn in IMGUI with your theme, opened from our
  own `Interactable`; the vanilla store is never patched.
- **Price change on a staged deal: reconfirm, provisional.** The line turns amber and the player
  confirms once more; `Teardown` (dissolve every open tray) sits behind config. The owner is not sure
  yet; your view is wanted.
- **Odin vanish is the departure**, as the original brief said; v5's mist-and-horn is gone.
- **ServerSync as source, not ILRepack.** Same result, one fewer tool.
- **Dverger is the 0.1 body.** Yours replaces it behind the same contract when it is rigged.
- **The flight starts ~90 m out, not 500 m**: the game only instantiates objects inside a player's
  active zone block.

## 6. Do not

- Edit `Libs/ServerSync.cs` or, later, `Libs/SharedUI/*` in place; replace from upstream.
- Add a runtime dependency on VikingOS, Jotunn or any other plugin.
- Put the GLB, a bundle or any binary over a few hundred KB in the repo; bundles go beside the
  DLL in the package, sources live in the Unity project directory, which is a sibling folder, not
  inside the repo.
- Commit without `dotnet run --project tests/CoreTests/CoreTests.csproj` green.

## 7. Open questions for you

1. Shared-source files: vendored copies with headers, or a submodule?
2. Tear-down versus reconfirm on a price tick (§5).
3. Where bundles get built: your Linux box (has 6000.0.61f1) or Don's (has 6000.5.4f1 only)?
4. Will the rigged body come with its own clips, or should we plan on Mixamo?
