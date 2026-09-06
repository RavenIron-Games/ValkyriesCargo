# Handoff to Wu'barrk's Claude — Valkyrie's Cargo, 2026-09-06

You are the second engineering session on this mod. Don's session (me) built what is here; you and
Wu'barrk take part of what is left. This file tells you everything you need to act, in the order to
read it, and ends with the one job to do first: **divide the work packages with your owner, write the
split down, and say so on PR #1.**

Repo: <https://github.com/RavenIron/ValkyriesCargo> (public). Branch `a/contract` = PR #1; `main` = Phase 0.

---

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
- **Comfort never leaves the client**; the client writes `vc_rested`/`vc_comfort` on its own ZDO.
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
| Catalogue parser, 72 defaults checked against the item table | 27 tests |
| **Contract** (PR #1): `Wire`, `MarketSnapshot`, `VisitSnapshot`, `Deal`/`DealResult`/`DealInbox`, `DemoMarket`, `CargoRpc` + demo transport, `ICargoTerminal` | built; **231 tests**; reviewed, no blockers |
| Headless boot on the CairnTest dedicated server | **seen** 2026-09-06 15:23: loaded line, ServerSync RPC registered, `role: dedicated server` |
| Client boot, version wall, config lock, prefab dumps | not yet seen (needs a screen) |
| Scheduler, event, flight, merchant, terminal, market pricing, persistence, deal wire | **not built** |

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

## 8. Your first job: divide the packages

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
3. Whether the rigged body arrives with clips or we plan on Mixamo.
4. Vendored copies of SharedUI with headers, or a submodule.

## 11. Commands

```
tools\fetch-libs.ps1                                   # Windows: libs\ from the Steam install
dotnet build ValkyriesCargo\ValkyriesCargo.csproj
dotnet run --project tests\CoreTests\CoreTests.csproj --nologo    # 231 tests, no game
tools\package.ps1                                      # store zip in dist\
```
On Linux, `libs/` is a symlink to `libs-Tools` with the names listed in `HANDOFF-WUBARRK.md` §2.
In game: `cargo status`, `cargo version`, `cargo prefab Valkyrie|Dverger|odin|Haldor`.
