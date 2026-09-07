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

Not yet built: the flight, the merchant, the terminal, the persistence layer, the deal wire, the
admin commands; nothing on the game side calls the market core yet. See `docs\DESIGN.md` section 9 for the order.
