# Handoff to Wu'barrk's Claude — Valkyrie's Cargo, 2026-09-06; section 0 re-cut at the close of 2026-09-07 night

You are the second engineering session on this mod. Don's session (me) built what is here; you and
Wu'barrk take part of what is left. This file tells you everything you need to act, in the order to
read it, and ends with the one job to do first: **divide the work packages with your owner, write the
split down, and say so on PR #1.**

Repo: <https://github.com/RavenIron-Games/ValkyriesCargo> (public, org RavenIron-Games). Everything below is on `main`.

---

## 0. State at the close of 2026-09-07 night — READ THIS FIRST

Don is asleep; his Claude carries the night with the owner's merge authority (review, then merge what is
clean, ours and yours). Sections 1–11 below are older and survive where they do not disagree with this.

**Amended later the same night, at the owner's word ("shut the agents down and take the data"):** the four agents
were stopped. P10a had FINISHED: branch `a/p10a-sweep` is pushed and PR-ready. P10b and P11 were stopped mid-work and
pushed as they stood: `a/p10b-probes` (6 commits, builds clean, 1193 checks) and `a/p11-shakedown` (6 code commits
plus a WIP commit of two partial documents, builds clean, 1078 checks); neither is reviewed. The P5 review was stopped
before it produced anything, so **PR #15 is open, unreviewed by our side, unmerged.** Nothing was merged after the
stop, and no PR was opened. The paragraphs below describe the state as it was before the stop.

**Merged to `main` since the last handover, all by PR, in this order:**

| PR | What | Proof |
|---|---|---|
| #7 | P7 the Cargo Terminal: IMGUI on the vendored gilt theme, the pure tray model, `cargo terminal demo|open|close` | 926 checks; never drawn on a screen |
| #8 | P4 the authored flight (Wu'barrk): straight approach, `vc_turn` (renamed `VCargo_turn` on 2026-09-07), glide altitude on the waypoint, `FlightSpeed` 8 / `FlightTurnRate` 45 synced, the merchant gated behind P5; reviewed with a simulation, answered, both sides reproduced the numbers | 39 checks; never flown |
| #9 | P8 loader (`BodyLoader`, `IngvarBody` PlayableGraph over the six clips, no controller needed in the bake, `Server.CustomBody`, `cargo body preview`) + P9 release pass (README truth, CHANGELOG, manifest, `package.ps1`, `docs/RELEASE.md`; Hexium name free); adversarial review found the LODGroup re-enable and fixed it | 1034 checks; no bundle baked |
| #10 | `docs/PROOF-CLIENT.md`, the client proof runbook for items 2–20, with `tools/deploy-test.ps1`, `tail-log.ps1`, `set-test-config.ps1` | scripts dry-run only |
| #11 | The client-path audit: 57 members verified, six defects fixed (the demo terminal never ticked from the main menu; Tab/M double-open; refused deals acked; a destroyed merchant; leaked focus tokens; legacy Input) plus `DealInbox.Forget` | 1039 checks |
| #12 | The economy simulation (`tests/EconSim`, `docs/ECONOMY-SIM.md`): the buy-out-and-sell-back round trip is PROFITABLE and drains the purse; a decision for the two owners | deterministic, checked |
| #13 | Your brief for P10 and P11, accepted by Don | — |
| #14 | The knowledge base wired into CLAUDE.md and two corrections (a ZDOID is a session handle; immortality belongs on `RPC_Damage`); the base itself now lives at `docs/knowledge-base/` (52 files; the API reference folder is still missing) | — |

Main at the close: 0 warnings, **1078 checks**. Also on main: the flight's docs and verify items 21–22, the
P10a handover (below), and `docs/HANDOFF-WUBARRK.md` section 0, your evening list.

**Open when Don went to bed:**
- **PR #15, P5 the merchant (yours).** Builds clean with main merged, 1095 checks. An Opus adversarial review
  is running on it in the 11d shape (every game call against the real assembly, ownership on every machine,
  the pure plan's gaps, our seams `BodyLoader.Attach` and `CargoTerminalHost.Open`, the four-line
  `VisitDirector` seam). One thing it is told to look hard at: the boot `Spawner.Sweep` runs before the
  sidecar's session is adopted, so `Sweep(0)` may destroy the merchant of the very visit about to resume.
  Findings go on the PR; it merges when they are answered.
- **Issue #16, rename `vc_` to `VCargo_`** (21 names, 5 files): Don's, done after #15 and P11 land, as one
  pass with a `Core/Keys.cs`, a harness check for the prefix and uniqueness, and the collision mechanics in
  the trust-boundary document. (Done 2026-09-07: the rename, found to be 21 names across more call sites
  than the issue predicted, plus `Core/Keys.cs` and its distinctness harness check, landed on
  `b/vcargo-prefix-rename`. The collision mechanics write-up in the trust-boundary document was not part
  of that pass.)
- **In flight on Don's side**, three Opus agents in worktrees, each opening a PR when done: P10a (the sweep
  tooling: `fetch-builds`, `decompile-builds`, `diff-engine.js`, `docs/ENGINE-SURFACE.md`, `ENGINE-BASELINE.md`,
  the client-vs-server sweep, and the dedicated-server live and public-test sweeps via steamcmd); P10b (the
  boot-time version line, per-fact probes each in its own method, degrade-don't-throw, refuse on a newer
  sidecar format, `cargo status`/`cargo engine`); P11's off-game half (the config shakedown with fixes in
  `ModConfig`, `docs/TRUST-BOUNDARY.md` with server-side validation fixes, the 11d checklist).

**Decided tonight by the owner:** P10 and P11 accepted; **P10a is yours after P5** (the tooling and first sweeps
from Don's side are your foundation; the client fetches and every recurring sweep are yours, on your rig);
the knowledge base lives under `docs/knowledge-base/`.

**Decisions still for the two owners:** the profitable round trip (`docs/ECONOMY-SIM.md` verdict 1: pay a Ware
bought back at par at most, or the multiplier down to 1.4); the client asserting its own rested/comfort numbers
(P11 will propose "accept, worst case an undeserved visit"); reconfirm versus teardown on a price tick.

**Proofs still pending on a screen:** CLAUDE.md items 2–22; the runbook is `docs/PROOF-CLIENT.md`. You have
the client and took item 17 (`cargo terminal demo`) and `cargo prefab odin`; the bundle is not baked, so
items 19–20 wait on you; items 21–22 are the flight, two clients.

**Two dates:** Valheim 1.0 lands 2026-09-09, which is why P10a's baseline sweep runs tonight against the
public-test branch; after 1.0, re-run the sweep before trusting any engine fact in CLAUDE.md. And after #15
merges, a merged DLL puts a Dverger on the ground (`MerchantEnabled` is true): StormTest is the test bed,
nothing goes on a live server.

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
| **Market core** (PR #3), **eligibility + event** (PR #5), **deal wire + persistence** (PR #6) | built; **852 checks**; see §0 for the proofs |
| Headless boot on a dedicated server | **seen** (see §0) |
| Client boot, version wall, config lock, prefab dumps, and every client-side proof | not yet seen (needs a screen; CLAUDE.md items 2–16) |
| Flight (P4), merchant (P5), terminal (P7), body (P8), release (P9) | **not built** |

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
