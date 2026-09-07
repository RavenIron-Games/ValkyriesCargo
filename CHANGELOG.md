# Changelog

## 0.1.0 (unreleased)

**Phase 0 — the scaffold. Nothing playable, nothing verified in-game yet.**

- Repo laid out from the RavenEye template: net472 plugin, `libs\` from `tools\fetch-libs.ps1`,
  off-game harness in `tests\CoreTests`, `tools\package.ps1` for the store zip.
- ServerSync (blaxxun, MIT-0) vendored as shared source in `Libs\ServerSync.cs`; `ModRequired`
  with minimum version = current, so a client without the mod or on another version is refused
  at handshake.
- The whole config surface bound (`Server.*` synced and locked, `Client.*` local) and the two
  broadcast channels `VisitState` / `MarketState` declared. Nothing writes them yet.
- The `cargo` console: `status` (role, config authority, catalogue, the engine numbers the design
  depends on, the current random event), `version`, and `prefab <name>` (components, children,
  effect lists with the networked/local branch each entry takes, animator parameters).
- `Core\Catalogue.cs`: the pure parser with 72 data-checked defaults (18 wares, 54 wants), every
  prefab name verified against the item table in `docs\data`; a bad entry is reported and skipped,
  never thrown. 27 off-game tests.
- Design of record: `docs\DESIGN.md` v3; `docs\TLDR.md`; `docs\CATALOGUE.md`.
- The contract between the tracks (PR #1): `Core\Wire`, `MarketSnapshot`, `VisitSnapshot`, `Deal`,
  `Net\CargoRpc` with a demo transport, `Client\Terminal\ICargoTerminal`; 231 off-game checks.
- The market core: `Core\Market` (rules sanitized on the way in; the price curve with one rounding for
  the charge and one for what he pays; purse and carry; drift by the EnvMan day length; settlement in
  the contract's refusal order; salted delivery ids; sidecar rows including `purseStart`, `visit`,
  `seq`), `Core\Scheduler` (eligibility, the roll, tickets, cooldowns saved as remaining seconds),
  `Core\VisitClock` (a retargetable countdown mirror); `DemoMarket` is the real Market with a
  price-driven `Tick`. 714 off-game checks, mutation-proven. `cargo status` prints the EnvMan day
  length. Docs corrected from the review: CATALOGUE section 5, DESIGN sections 3.1/3.4/3.5/3.7/8.
- P7, the Cargo Terminal: an IMGUI window on the vendored VikingOS gilt theme; wares and wants with icons,
  a staging tray at the prices on screen (amber where they moved), Confirm at the price seen now, barter
  auto-fill, Send him off twice; opened by `cargo terminal demo` on the in-process market or
  `cargo terminal open` on a running visit, and by the merchant once P5 exists. The pure tray model has
  74 checks. 926 off-game checks. The window has not been seen on a screen yet.
- P6, the deal wire and persistence: `vc_open/close/deal/ack/claim/dismiss` on each peer's own ZRpc,
  `vc_dealt` back; the client transport behind `CargoRpc` (a listen host trades in-process); the owed
  ledger by platform id, redelivered on `vc_claim`, cleared on `vc_ack`; `DealApplier`, the one inventory
  writer; the applied-delivery inbox on disk. The world sidecar `valkyriescargo_{uid}.dat` (Cairn's
  discipline) carries stock, purse, visit numbers, cooldowns, the running session and the owed rows; a
  restart mid-visit resumes the visit the engine restored. Console: `cargo stock`, `cargo deal buy|sell`,
  `cargo claim`, admin `cargo reset`, `cargo save`. Headless-verified: the file written on first boot,
  76 rows loaded on the next. 852 off-game checks.
- P3, eligibility and the event: the client writes `vc_rested`/`vc_comfort` on its own character ZDO every
  2 s; the vanilla event `valkyries_cargo` is registered on every machine (`RandEventSystem.Awake` prefix);
  the server's director reads every character ZDO into the pure Scheduler once a second, starts the event
  for the pilot it picks, publishes VisitState and MarketState, mirrors the event's clock (design 3.7) and
  ends the visit when the engine ends the event; `cargo visit [player]` and `cargo dismiss`, admin-gated on
  the server by vanilla's own list, ride a routed RPC from a client. `cargo status` shows the report, the
  director, the visit and every candidate. Headless-verified on StormTest: 13 patches, the event registered,
  the day length read from the engine (1800 s), the first roll held by a live storm. 769 off-game checks.

Not yet built: the flight, the merchant, the body loader. Nothing has been seen from a client yet. See `docs\DESIGN.md` section 9 for the order.
