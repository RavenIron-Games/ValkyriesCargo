# Work split — Valkyrie's Cargo, from 2026-09-06

> **Status: PROPOSAL, not agreed.** Don and Wu'barrk have not divided the duties yet. Section 0 is the
> neutral list of work packages to divide; sections 1 and 3 are one possible way to cut them, written
> by Don's side as a starting point. Section 2, the contract, is code now (PR #1) and holds whichever
> way the packages are dealt: it is the seam, not an assignment.

## 0. The work packages, with what each needs

| # | Package | Size | Needs first | Natural pull |
|---|---|---|---|---|
| P1 | Contract files + demo (`Core/`, `Net/CargoRpc`) | done, PR #1 | — | — |
| P2 | Market core: pricing, drift, purse, scheduler, visit clock, packet encoders; tests | 1–2 days | P1 | anyone; pure C#, no game |
| P3 | Comfort report + event registration + `cargo visit`; headless proof | 1 day | P2 | needs a dedicated server (Don has CairnTest) |
| P4 | Authored flight: server creates the bird, pilot flies it; two-client proof | 1–2 days | P3 | needs two clients on one machine (Don has two accounts) |
| P5 | Merchant: carry pin, `InIntro`, follow, callout, immortal, dismissal, Odin vanish, restart sweep | 2 days | P4 | as P4 |
| P6 | Deal wire server side: direct ZRpc, owed ledger, persistence (Cairn pattern) | 1–2 days | P1, P2 | anyone |
| P7 | Cargo Terminal: IMGUI window on the gilt theme, panes, tray, deal builder, `cargo terminal demo` | 2–3 days | P1, SharedUI files | Wu'barrk: it is his theme and his focus helper |
| P8 | Body: retopo, rig, clips, animator contract, bundle on Unity 6000.0.61f1, loader | open-ended, after 0.1 | model | Wu'barrk: his model, his Editor version |
| P9 | Release: README truth pass, package, Hexium name check, store upload | half a day | all | Don: the RavenIronStudios store account |

P2, P6 and P7 can start today in parallel; P3–P5 are a chain; P8 is off the 0.1 path. Divide as you like;
the only hard constraints are the tools each package needs (a server, two clients, the Unity version).

## 1. One possible split (proposal)

| | **Track A — the world side** (Don, Windows, CairnTest) | **Track B — the terminal and the body** (Wu'barrk, Linux, Unity 6000.0.61f1) |
|---|---|---|
| Owns | scheduler, event, flight, merchant AI, market, persistence, deal wire, ServerSync channels, all Harmony patches, release packaging, dedicated-server verification | the Cargo Terminal (IMGUI on VikingOS's theme), the client-side deal builder and staging tray, the body pipeline (retopo, rig, clips, bundle, loader) |
| Folders | `Core/` `Server/` `Net/` `Patches/` `Client/ComfortReporter.cs` `Client/CargoFlight.cs` `Client/CargoMerchant.cs` `tests/` `tools/` | `Client/Terminal/**` `Client/BodyLoader.cs` `Libs/SharedUI/**` the Unity project (a sibling folder, never inside the repo) |
| Shared, change only by PR that both read | `Core/Deal.cs` `Core/MarketSnapshot.cs` `Core/VisitSnapshot.cs` `Net/CargoRpc.cs` (the contract, §2) | same |
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
- The client touches its inventory **only** inside `onAnswer` when `Ok`, using `ItemsToAdd/Remove` and `CoinsDelta`.
- `UnitPriceSeen` is what the player saw; a mismatch comes back as `price_changed` with `NewMarketState`
  (policy Reconfirm: keep the tray, redraw, ask once more).
- Nonce: 64-bit random per deal; the client keeps a bounded inbox of applied `DeliveryId`s and acks.
- Console: `cargo terminal demo` (Track B) opens the terminal on `MarketSnapshot.Demo` with `CargoRpc.UseDemo(true)`,
  so the UI is developable and reviewable with no server and no merchant.

## 3. Order of work

**Track A**
1. Contract files above, with tests and the demo constants. *(first PR, unblocks B)*
2. `Market`, `Scheduler`, `VisitClock`, packet encoders; tests, mutation-proven.
3. Comfort report, the event registration, `cargo visit`; headless proof.
4. Authored flight; two-client proof.
5. Carried merchant, callout, immortality, dismissal, Odin vanish, restart sweep.
6. Deal wire server side, persistence, owed ledger; the simultaneous last-unit test.
7. Release 0.1.0 packaging; Hexium name check first.

**Track B**
1. Vendor `Libs/SharedUI/GiltFrameTheme.cs` + `UIFocus.cs` with origin headers; build once.
2. `cargo terminal demo`: the window opens and closes on the demo snapshot; cursor release on close and on logout.
3. Panes, rows, stock and trend glyphs, staging tray, payment mode, countdown, dismiss; all against the demo.
4. The deal builder: stage → `Deal` → `CargoRpc.Send` → apply on `Ok`; `price_changed` turns the line amber.
5. Body pipeline in parallel: retopo, rig, clips, animator contract, bundle on 6000.0.61f1; `BodyLoader`.
6. Theme option (`Client.Theme = BlackGold`).

Where they meet: A6 + B4 = the first real deal on CairnTest. Plan for it as a shared session.

## 4. Process

- Branches `a/<topic>` and `b/<topic>`; PRs into `main`; `dotnet run --project tests/CoreTests/CoreTests.csproj`
  green before every PR; small commits with a one-line why.
- `main` always builds and boots headless. Whoever breaks it fixes it.
- Contract changes: a PR touching a §2 file needs a comment from the other track before merge.
- No binaries in the repo (the bundle ships beside the DLL in the package; the GLB lives in the Unity folder).
- Verified facts go into `CLAUDE.md` "Status" with the date and the exact log line, by whoever saw it.
- "Not ours" files (`Libs/**`) are replaced from upstream, never edited.

## 5. Needs between the tracks

| Track B needs from A | when |
|---|---|
| the contract files + demo constants | A1, first |
| `CargoRpc` real transport | A6 |
| a merchant to open the terminal on | A5 |

| Track A needs from B | when |
|---|---|
| the two SharedUI files (or a submodule) | before B1 |
| `ICargoTerminal` implementation | before A5's interaction step |
| the rigged body + bundle | after 0.1 |
| tear-down vs reconfirm opinion | anytime |
