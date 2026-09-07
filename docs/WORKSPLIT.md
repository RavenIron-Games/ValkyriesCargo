# Work split — Valkyrie's Cargo, from 2026-09-06

> **Status: DECIDED by the owners, the night of 2026-09-06.** Wu'barrk takes the creature pipeline
> (P4 flight, P5 merchant, the bake half of P8); Don takes the terminal (P7), the loader half of P8 and
> the release (P9). P1, P2, P3 and P6 are merged. Section 2, the contract, is code and is the seam.

## 0. The work packages, with what each needs

| # | Package | Size | Needs first | Natural pull |
|---|---|---|---|---|
| P1 | Contract files + demo (`Core/`, `Net/CargoRpc`) | done, PR #1 | — | — |
| P2 | Market core: pricing, drift, purse, scheduler, visit clock, packet encoders; tests | done, PR #3 | P1 | — |
| P3 | Comfort report + event registration + `cargo visit`; headless proof | code done, headless-proven; client proof pending | P2 | a client on an admin-listed account |
| P4 | Authored flight: server creates the bird, pilot flies it; two-client proof | **Wu'barrk** (owner, 2026-09-06 night) | P3 (merged) | design 3.2; reconcile the event start: P3 starts it at dispatch, 3.2 says at the drop (`vc_placed`); the clock follows whichever is chosen |
| P5 | Merchant: carry pin, `InIntro`, follow, callout, immortal, dismissal, Odin vanish, restart sweep | **Wu'barrk** (with P4: the carry straddles both) | P4 | design 3.3, 3.6, 3.7; the Animator is his own (DESIGN §8) |
| P6 | Deal wire server side: direct ZRpc, owed ledger, persistence (Cairn pattern) | code done, headless-proven (sidecar round trip); client proof pending | P1, P2 | — |
| P7 | Cargo Terminal: IMGUI window on the gilt theme, panes, tray, deal builder, `cargo terminal demo` | **Don**: code done, off-game proven; screen proof pending | P1, P6 (merged), SharedUI (vendored, PR #2) | design 3.4; §2 below is the whole contract |
| P8 | Body: rig, clips, bundle on Unity 6000.0.61f1 (**Wu'barrk**, PR #4 merged; the BAKE is still to do); `Client/BodyLoader.cs` + the clip driver (**Don**: code done, off-game proven; screen proof pending) | split | model (in) | design §11; `models/SETUP-FOR-CLAUDE.md` for the bake. The bundle needs NO AnimatorController: the loader plays the clips by name |
| P9 | Release: README truth pass, package, Hexium name check, store upload | **Don** | all | the RavenIronStudios store account |

P4 → P5 is a chain on Wu'barrk's side; P7 and the loader run in parallel on Don's; the bake is independent.
The client-side proofs of what is merged (CLAUDE.md "What to verify in-game", items 2–16) belong to whoever
boots a client first, and go into CLAUDE.md Status with the exact lines.

## 1. The split (decided 2026-09-06)

| | **Track A — Don** | **Track B — Wu'barrk** |
|---|---|---|
| Owns | the Cargo Terminal (P7: IMGUI on the gilt theme, panes, tray, the deal builder on `CargoRpc`), the body loader and its Animator driver (P8, mod side), the market, persistence, the deal wire, the scheduler and the event (merged), release packaging (P9), the store account | the creature pipeline: the authored flight (P4), the merchant (P5: carry pin, `InIntro`, follow, callout, immortal, dismissal, the Odin vanish, the restart sweep), the body's rig, clips and bundle (P8, Unity side), the two-client proof of all three |
| Folders | `Client/Terminal/**` `Client/BodyLoader.cs` `Client/DealApplier.cs` `Core/` `Server/VisitDirector.cs` `Server/MarketStore.cs` `Net/` `tests/` `tools/*.ps1` | `Client/CargoFlight.cs` `Client/CargoMerchant.cs` `Server/Spawner.cs` `Patches/Patch_Valkyrie_Awake.cs` `Patch_Humanoid_Awake.cs` `Patch_Character_InIntro.cs` `Patch_Character_Damage.cs` `models/` `tools/unity/**` `tools/*_ingvar.py` the Unity project (a sibling folder) |
| Shared, change only by a PR the other side commented on | the contract (§2): `Core/Deal.cs` `Core/MarketSnapshot.cs` `Core/VisitSnapshot.cs` `Net/CargoRpc.cs` `Client/Terminal/ICargoTerminal.cs`; and the seams P4/P5 touch in A's files: `Core/VisitSession.cs` (phases, the drop point), `Server/VisitDirector.cs` (where the event starts) | same |
| Docs | `CLAUDE.md` status lines for what each verified; `docs/DESIGN.md` by PR | same |

Nobody edits the other's folders. A need in the other track is an issue or a PR, not a silent edit.

## 2. The contract (frozen; Track A ships these files first, Track B codes against them)

**Snapshots** are the strings ServerSync carries (design §2). Track A ships parsers with a demo constant so
the terminal can render before any server exists.

```csharp
namespace RavenIron.ValkyriesCargo.Core
{
    public enum EntryKind { Ware, Want }                       // exists (Catalogue.cs)

    public sealed class MarketRow {                              // one line of MarketState
        public string Prefab; public EntryKind Kind;
        public int Stock, Target, Max, Buy, Sell;                // Buy = what the player pays, Sell = what Ingvar pays
        public int Trend;                                        // -1 below base, 0 at, +1 above
    }
    public sealed class MarketSnapshot {
        public int VisitId; public int Purse; public IReadOnlyList<MarketRow> Rows;
        public MarketRow Find(string prefab);
        public static MarketSnapshot Parse(string s, List<string> problems);   // "v1;visitId;purse;row|row|..."
        public static readonly string Demo;                      // 72 rows at target stock, purse 800
    }
    public enum VisitPhase { None, Flying, Dropped, Approaching, Trading, Leaving }
    public sealed class VisitSnapshot {
        public int VisitId; public VisitPhase Phase; public long PilotUid;
        public double EndWorldTime; public int Purse; public int Seed;
        public static VisitSnapshot Parse(string s, List<string> problems);
        public static readonly string Demo;
    }

    public sealed class DealLine { public string Prefab; public int Count; public int UnitPriceSeen; }
    public sealed class Deal {                                   // one primitive: buy, sell, barter
        public int VisitId; public long Nonce;
        public DealLine Wanted;                                  // null for a pure sell
        public List<DealLine> Offered = new List<DealLine>();    // empty for a pure buy
        public int CoinsOffered;
    }
    public sealed class DealResult {
        public long Nonce; public string DeliveryId;
        public bool Ok; public string Reason;                    // DealReason tokens, in the server's checking order:
                                                                 // empty_deal, stale_visit, duplicate, unknown_item,
                                                                 // bad_count, sold_out, price_changed, over_max,
                                                                 // coins_short, purse_empty; plus visit_over (real
                                                                 // server only), not_connected and malformed (client
                                                                 // side). inventory_full is the TERMINAL's own
                                                                 // pre-check: the server cannot see an inventory.
        public int CoinsDelta;                                   // + to the player, - from the player
        public List<DealLine> ItemsToAdd, ItemsToRemove;         // applied by the client only when Ok
        public string NewMarketState;                            // whole MarketState on price_changed, else ""
    }
    public sealed class DealInbox { ... }                        // applied DeliveryIds, bounded 500; CargoRpc owns one
    public sealed class DemoMarket { ... }                       // in-process settlement with the server's refusal order
}

namespace RavenIron.ValkyriesCargo.Net
{
    public interface ICargoTransport { bool Ready {get;} void Open(int); void Close(int); void Dismiss(int); void Send(Deal, Action<DealResult>); }

    public static class CargoRpc                                 // client-side surface the terminal calls
    {
        public static bool Ready { get; }                        // a transport is installed and live
        public static bool IsDemo { get; }
        public static MarketSnapshot Market { get; }             // last published, parsed
        public static VisitSnapshot  Visit  { get; }
        public static void Open(int visitId);                    // vc_open
        public static void Close(int visitId);                   // vc_close
        public static void Dismiss(int visitId);                 // vc_dismiss
        public static void Send(Deal deal, Action<DealResult> onAnswer);   // vc_deal -> vc_dealt; a redelivered Ok is answered duplicate
        public static event Action<MarketSnapshot> MarketChanged;          // fired by PublishMarket
        public static event Action<VisitSnapshot>  VisitChanged;
        public static void UseDemo(bool on);                     // no server: settle against DemoMarket.Default(), publish its snapshots
        public static DemoTransport Demo { get; }                // the demo transport while on (PlayerCoins, Market.Tick for price_changed)
        // game side only: UseTransport(ICargoTransport), PublishMarket(string), PublishVisit(string), LoadInbox(DealInbox), EndSession()
    }
}

namespace RavenIron.ValkyriesCargo.Client.Terminal
{
    public interface ICargoTerminal                              // what the merchant calls; Track B implements
    {
        bool IsOpen { get; }
        void Open(GameObject merchant, int visitId);             // the merchant's GameObject for the 5 m hide rule
        void Close();
    }
    public static class CargoTerminalHost { public static ICargoTerminal Instance; }   // set by Track B at plugin Awake
}
```

Rules the contract carries:
- The terminal **never computes a price**; it renders `MarketSnapshot` and sends `Deal`s.
- **Snapshots are values, not live objects.** A `MarketRow` held from an earlier snapshot never changes;
  after every `MarketChanged` (and after every accepted deal) re-read rows from `CargoRpc.Market`, and
  send the `UnitPriceSeen` from the row the player is looking at NOW. Prices move with every deal.
- The client touches its inventory **only** inside `onAnswer` when `Ok`, using `ItemsToAdd/Remove` and `CoinsDelta`.
- `UnitPriceSeen` is what the player saw; a mismatch comes back as `price_changed` with `NewMarketState`
  (policy Reconfirm: keep the tray, redraw, ask once more).
- Nonce: 64-bit random per deal; the client keeps a bounded inbox of applied `DeliveryId`s and acks.
- Console: `cargo terminal demo` (Track B) opens the terminal on `MarketSnapshot.Demo` with `CargoRpc.UseDemo(true)`,
  so the UI is developable and reviewable with no server and no merchant.

What the market-core review (2026-09-06) says the terminal must know:
- For a `wanted` line send `row.Buy` as `UnitPriceSeen`; for an `offered` line send `row.Sell`. Backwards is a
  permanent `price_changed` loop.
- `Wanted` must be a Ware; offering a Want back to him answers `unknown_item`, not a friendlier code.
- Only `price_changed` carries `NewMarketState`; `sold_out` and `over_max` come back with an empty one, so the tray
  waits for the next `MarketChanged`.
- SOLD is `Stock == 0`, never the price: an empty shelf still quotes a price (as if one were left).
- The "you pay" line is `count × unit`, a flat quote at the price on screen, not a running sum; that is how the
  server prices it too.
- `Trend` is derived from the buy price against base, for Wants as well; the arrow is monotone with `Sell`.
- `CargoRpc.Market` is the cached snapshot. Never read `DemoMarket.Market` in a draw path: it builds 72 rows per call.
- `CargoRpc.Demo.Market.Tick(prefab, scarcer: true|false)` walks that row's stock until the number the terminal
  shows (Buy for a Ware, Sell for a Want) actually moves, and returns false only at the row's bound; use it to see
  `price_changed` on any row.

## 3. Order of work

**Track A (Don)**
1. ~~Contract~~, ~~market core~~, ~~eligibility and event~~, ~~deal wire and persistence~~: merged (PRs #1, #3, #5, #6).
2. ~~P7: vendor check, `cargo terminal demo` opens and closes~~ built (a/p7-terminal); screen proof pending.
3. ~~P7: panes, rows, glyphs, tray, payment mode, countdown, dismiss~~ built.
4. ~~P7: the deal builder~~ built: stage → `Deal` → `CargoRpc.Send` → `DealApplier.Apply` on `Ok`; `price_changed`
   turns the line amber and Confirm accepts the new price. Proof on a real server pending (CLAUDE.md item 18).
5. ~~P8 (mod side): `Client/BodyLoader.cs` loads the embedded bundle, swaps the body under the same prefab clone,
   drives the Animator from velocity and phase~~ **built** (a/p8-loader): `Client/BodyLoader.cs` +
   `Client/IngvarBody.cs` + `Core/BodyMotion.cs` (pure, 78 checks); a `PlayableGraph` over the six clips with no
   `AnimatorController` in the bundle, speed from the transform's own displacement, `Greet/Talk/Shrug/Nod` as the
   API P5 calls. Gated by the NEW `Server.CustomBody`, not by `Server.BodyPrefab` — `BodyPrefab` stays the engine
   prefab the merchant is cloned from. `cargo body [preview|walk|clip <name>|clear]` shows it without a merchant.
   Screen proof pending (CLAUDE.md items 19 and 20): no baked bundle exists yet.
6. P9: README truth pass, CHANGELOG, package, Hexium name check, store upload. Adversarial review before.

**Track B (Wu'barrk)**
1. P4: decide where the clock starts (dispatch, as P3 does, or the drop, as design 3.2 says), then the server
   authors the bird and the merchant with owner = pilot, `Patch_Valkyrie_Awake`, `CargoFlight`; two-client proof.
2. P5: carry pin and `InIntro`, the drop handoff, follow and callout, immortal, dismissal (Shift+E twice,
   `vc_dismiss` is already on the wire), the Odin vanish by the effect rule, the restart sweep
   (`GetAllZDOsWithPrefabIterative`; the director already resumes the session row).
3. P8 (Unity side): bake the bundle with `IngvarBundleBuilder`, size gate, hand it over for embedding.
4. The client proofs of everything merged, as they come naturally with a client in hand.

Where they meet: A4 + B2 = the first real deal from the window with the merchant standing there. Plan for it as
a shared session on a server both can reach.

## 4. Process

- Branches `a/<topic>` and `b/<topic>`; PRs into `main`; `dotnet run --project tests/CoreTests/CoreTests.csproj`
  green before every PR; small commits with a one-line why.
- `main` always builds and boots headless. Whoever breaks it fixes it.
- Contract changes: a PR touching a §2 file needs a comment from the other track before merge.
- No binaries in the repo, with **one named exception**: the body's source art, `models/ingvar.fbx`
  (4.3 MB) and `models/ingvar_albedo.png` (6.6 MB). Everything else stays out — the built bundle ships
  beside the DLL in the package, and Meshy's raw per-clip output (343 MB, six files each carrying a
  duplicate mesh and 22 MB of the same textures) is gitignored.
  **Decided by Wu'barrk, 2026-09-06**, answering the question Don raised on PR #4. The reasoning: the
  body is a hand-made asset with no other home, Don is away, and a build input nobody can fetch is worse
  than 11 MB in git. If the second owner disagrees, the fallback is a GitHub release asset — the two
  files move, `tools/setup-ingvar-unity.ps1` gains a download step, and nothing else changes.
- Verified facts go into `CLAUDE.md` "Status" with the date and the exact log line, by whoever saw it.
- "Not ours" files (`Libs/**`) are replaced from upstream, never edited.

## 5. Needs between the tracks

| Track B needs from A | when |
|---|---|
| the contract, the wire, `VisitSession` phases and `SetDrop`, `vc_dismiss`: all merged | now |
| a decision on where the clock starts, if B wants it moved to the drop (a §2-style PR on `VisitDirector`) | at P4 start |
| `ICargoTerminal` implementation to open on the merchant | before B2's interaction step |
| ~~`BodyLoader` to put the bundle on~~ **built** (a/p8-loader): drop the bake at `Assets\valkyriescargo_kit` (the csproj line is in, conditional) and `cargo body` reports it | now |

| Track A needs from B | when |
|---|---|
| the merchant to open the terminal on (P5) | for A4's real-server proof; the demo covers everything before it |
| the baked bundle and its size | at A5 |
| the drop point and phases written through `VisitSession` (already in the contract) | with P4 |
| tear-down vs reconfirm opinion | anytime |
