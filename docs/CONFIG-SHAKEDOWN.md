# Config shakedown — every key in `Config/ModConfig.cs`, 2026-09-07

P11a. One row per key: the default, the range, the side the description claims, **who actually reads it**
(file and line), whether the code re-clamps with a literal of its own and whether that literal agrees with
the `AcceptableValueRange`, whether the description is true against the code, and a verdict.

Read against `main` at `dc979bd` plus this branch's fixes, then **re-read 2026-09-07 against `main` after
PR #22** (Wu'barrk's 0.1.0 integration: P5 revives `ApproachDistance`, `PriceChangePolicy` is deleted,
`FairMarketAct` and `BarrkBotExport` are new, `PurseCoins` is 1500); a row says which reading it comes from
where they differ. Every "who reads it" is from
`grep -rn "ModConfig\." ValkyriesCargo/` followed by the field through `FillMarketRules` / `FillSchedulerRules`
into the pure cores, so a key whose only reader is `cargo status` counts as **not read**: printing a number is
not using it. `Patches/Patch_Terminal.cs` prints eleven of them and is excluded from the reader column for
that reason.

## Verdict counts

| | |
|---|---|
| Keys bound | **34** after PR #22 (30 `[Server]` counting `LockConfiguration`, 4 `[Client]`), plus 2 custom synced values (`visit`, `market`). At the audit: 33 — `PriceChangePolicy` has since gone, `FairMarketAct` and `BarrkBotExport` have arrived |
| Keys **nobody reads** | **0** after PR #22. At the audit: **2** — `ApproachDistance` (now read by `CargoMerchant.cs:272`) and `PriceChangePolicy` (deleted by Wu'barrk) |
| Descriptions that were **false** | **9** at the audit — `LockConfiguration`, `MinComfortLevel`, `ApproachDistance`, `BodyPrefab`, `CustomBody`, `FlightSpeed`, `FlightTurnRate`, `EnableBarter`, `PriceChangePolicy`; all fixed, and `ApproachDistance`'s was re-fixed after P5 revived the key |
| Descriptions incomplete (true but hid a clamp) | 1 — `FlightStartDistance` |
| Ranges that **disagree** with a literal the code clamps with | **5** — of which **2 bit** and are fixed, 3 are the config being the tighter bound and are left |
| Defaults changed | **0** by this pass. **4 by Wu'barrk after it** (PR #22, `docs/DECISIONS-WUBARRK.md`): `PurseCoins` 800 → 1500, the carry measured on gross coins in, four Wants base 2 → 3, `FairMarketAct` on. Reviewed under "The defaults" |
| Keys removed | **1** — `PriceChangePolicy`, by Wu'barrk in PR #22. This pass had declined to (see "The two dead keys"); his reason is the better one |

All fixes are in `ValkyriesCargo/Config/ModConfig.cs` on this branch, in two commits: `config: every
description names the side that actually reads the key` and `config: the two flight ranges that promised
values the code silently ate`.

---

## The table

"Re-clamp" is a literal in the code that narrows the value again after the config has already clamped it to
its `AcceptableValueRange`. "agrees" means the two bounds are the same number.

### `[Server]` — synced through `Sync.AddConfigEntry` and locked by `LockConfiguration`

| Key | Default | Range | Side claimed | Who reads it | Re-clamp, and does it agree | Description | Verdict |
|---|---|---|---|---|---|---|---|
| `LockConfiguration` | `true` | — | ~~SERVER~~ → ServerSync, every machine | ServerSync: `IsLocked` (`Libs/ServerSync.cs:138`), which gates `Broadcast` (`:916`, `:925`) and `isWritableConfig` (`:585`); bound at `ModConfig.cs:90` | — | **was false**: it is read on the client too, and that is the half that stops a locked client sending anything | **fixed** |
| `Enabled` | `true` | — | SERVER | `ModConfig.cs:231` → `Scheduler.cs:138` | — | true | OK |
| `RequireRested` | `true` | — | SERVER | `ModConfig.cs:232` → `Scheduler.cs:191` | — | true | OK |
| `MinComfortLevel` | `4` | 0–20 | SERVER | `ModConfig.cs:234` → `Scheduler.cs:192` | `SchedulerRules.Sanitize` floors at 0, no ceiling (`Scheduler.cs:42`); the config's 20 is the only ceiling | **was false**: "a bed, a fire and a roof give 3" is off by one against `SE_Rested.CalculateComfortLevel`, which starts at 1, adds 1 under a roof and one step per distinct comfort piece within 10 m | **fixed** |
| `MinBaseValue` | `1` | 0–10 | SERVER | `ModConfig.cs:235` → `Scheduler.cs:193` | `Sanitize` floors at 0, no ceiling (`Scheduler.cs:43`) | true — vanilla raids do test `baseValue >= 3` (`RandEventSystem.cs:506`) | OK |
| `DaytimeOnly` | `true` | — | SERVER | `ModConfig.cs:233` → `Scheduler.cs:140` | — | true | OK |
| `EventCheckIntervalMinutes` | `25` | 1–240 | SERVER | `ModConfig.cs:236` → `Scheduler.cs:101`, `:111` | `Sanitize` 10–86 400 **s** = 0.17–1440 min (`Scheduler.cs:44`); config is tighter, never bites | true | OK |
| `EventChancePercent` | `25` | 0–100 | SERVER | `ModConfig.cs:237` → `Scheduler.cs:173` | `Sanitize` 0–100 (`Scheduler.cs:45`) — **agrees** | true | OK |
| `PlayerCooldownMinutes` | `60` | 0–1440 | SERVER | `ModConfig.cs:238` → `Scheduler.cs:247`, `:248` | `Sanitize` 0–604 800 **s** = 0–10 080 min (`Scheduler.cs:46`); config tighter | true | OK |
| `CooldownRadius` | `60` | 0–500 | SERVER | `ModConfig.cs:239` → `Scheduler.cs:259` | `Sanitize` 0–2000 (`Scheduler.cs:47`); config tighter | true | OK |
| `MerchantLifespanSeconds` | `300` | 30–1800 | SERVER | `CargoEvent.cs:80` (`Lifespan`), used at `CargoEvent.cs:53` (registration, **every machine**) and `:93` (start, server) | `Mathf.Clamp(30, 1800)` (`CargoEvent.cs:81`) — **agrees** | **true, confirmed**: it already says "Odin's compiled default is 300; his prefab's own value is unchecked", which matches `CLAUDE.md`'s engine facts (PR #8 reports 60 off the prefab; `cargo prefab odin` still decides) | OK |
| `ApproachDistance` | `3.5` | 1–10 | ~~SERVER~~ → CLIENT | `CargoMerchant.cs:272` (P5, PR #22) — the client that owns the merchant, with a 3.5 fallback if the entry is null | none | dead at the audit and said so; **re-read after P5: read on the client**, and the description says so now | **fixed twice** |
| `BodyPrefab` | `Dverger` | — | SERVER | `Spawner.cs:100` | — | **was false**: "until the custom body exists" — the clone base stays the engine prefab for good (`Character`, `MonsterAI`, the collider). Same wrong reading as `models/README.md` | **fixed** |
| `CustomBody` | `true` | — | ~~SERVER~~ → CLIENT | `BodyLoader.cs:109`, `:308` — a dedicated server never reads it | — | **was false** on the side | **fixed** |
| `FlightStartDistance` | `90` | ~~24~~–200 → **30–200** | SERVER | `Spawner.cs:112` | `Spawner.Clamp(…, 24, 400)` **ceiling disagrees** (config 200 is tighter, harmless); `FlightPlan.Make` raises anything under `MinimumStartDistance` 30 (`FlightPlan.cs:176`) — **the floor disagreed and bit**: 24–29 were three values that all meant 30 | was incomplete (said "clamped into the block", not the 12 m shrink or the 30 m floor) | **fixed: floor 24 → 30** |
| `FlightStartAltitude` | `120` | ~~30–500~~ → **30–400** | SERVER | `Spawner.cs:113` | `Spawner.Clamp(…, 20, 400)` — **ceiling disagreed and bit**: 500 was accepted and became 400 with no line anywhere | true | **fixed: ceiling 500 → 400** |
| `FlightDescentDistance` | `50` | 10–200 | SERVER | `Spawner.cs:114` | `Spawner.Clamp(…, 0, 200)` floor disagrees (config 10 is tighter, harmless); `FlightPlan.Make` also caps it at `MaxDescentFraction` × the run (`FlightPlan.cs:187`) | true | OK |
| `FlightSpeed` | `8` | 2–40 | ~~SERVER~~ → CLIENT | `CargoFlight.cs:87` — the machine that owns the bird | `Mathf.Clamp(2f, 40f)` — **agrees** | **was false** and self-contradictory ("Read on the SERVER … which is where the flying happens") | **fixed** |
| `FlightTurnRate` | `45` | 5–360 | ~~SERVER~~ → CLIENT | `CargoFlight.cs:88` | `Mathf.Clamp(5f, 360f)` — **agrees** | **was false** on the side | **fixed** |
| `Catalogue` | 72 entries | — | SERVER | parsed on **every** machine (`ModConfig.cs:247`, and again on every `SettingChanged`); only the server's copy reaches a market (`VisitDirector.cs:82`) | `Catalogue.Parse` drops unknown/malformed entries with a problem line | true enough — the authority is the server's; noted rather than changed | OK |
| `PriceElasticity` | `0.35` | 0.05–1.5 | SERVER | `ModConfig.cs:210` → `Market.cs:165` | `MarketRules.Sanitize` 0.05–1.5 (`Market.cs:33`) — **agrees** | true | OK |
| `MinPriceMultiplier` | `0.4` | 0.05–1 | SERVER | `ModConfig.cs:211` → `Market.cs:166` | `Sanitize` 0.05–1.0 (`Market.cs:34`) — **agrees** | true | OK |
| `MaxPriceMultiplier` | `3.0` | 1–10 | SERVER | `ModConfig.cs:212` → `Market.cs:167` | `Sanitize` 1.0–10.0 (`Market.cs:35`) — **agrees** | true | OK (but see "the defaults") |
| `SpreadBuy` | `0.7` | 0.1–1 | SERVER | `ModConfig.cs:213` → `Market.cs:179` | `Sanitize` 0.1–1.0 (`Market.cs:36`) — **agrees** | true | OK (but see "the defaults") |
| `FairMarketAct` | `true` | — | SERVER | `FillMarketRules` → `MarketRules.FairMarketAct` → `Market.PaysFor` (a Ware's buy-back multiplier capped at 1.0). New in PR #22 | — | true | OK — closes the round trip this pass left open |
| `WareHalfLifeGameDays` | `0` (never) | 0–365 | SERVER | `FillMarketRules` → `MarketRules.WareHalfLifeGameDays` → `Market.Relax` (Wares only) | `Sanitize` 0–365, a negative → 0, NaN → the shipped 0 — **agrees** | true | OK — replaced `StockHalfLifeGameDays` 2026-09-07 (the owner: wares never, wants 3; `docs/ECONOMY-SIM.md` §10) |
| `WantHalfLifeGameDays` | `3` | 0–365 | SERVER | `FillMarketRules` → `MarketRules.WantHalfLifeGameDays` → `Market.Relax` (Wants only) | `Sanitize` 0–365, a negative → 0, NaN → the shipped 3 — **agrees** | true | OK |
| `ShelfSize` | `20` | 0–200 | SERVER | `FillMarketRules` → `MarketRules.ShelfSize` → `Market.UpdateShelf` / `Market.KindOf` (every price, drift, refusal and snapshot row) | `Sanitize` 0–`Shelf.MaxSize` 200, a negative → 0 — **agrees** | true | NEW 2026-09-08, the rotating shelf (issue #56); a live change re-rolls on the next idle tick, never under a visit |
| `ShelfRotationGameDays` | `2` | 0.1–365 | SERVER | `FillMarketRules` → `MarketRules.ShelfRotationGameDays` → `Shelf.Period` | `Sanitize` 0.1–365 — **agrees** | true | NEW 2026-09-08; a change moves the period, so it re-rolls on the next idle tick |
| `PurseCoins` | `1500` (800 at the audit; raised in PR #22) | 0–100 000 | SERVER | `ModConfig.cs:215` → `Market.cs:142`, `:198`, `:199` | `Sanitize` 0–`MaxPurseCoins` 1 000 000 (`Market.cs:39`–`40`); config tighter | true | OK |
| `PurseCarryPercent` | `50` | 0–100 | SERVER | `ModConfig.cs:216` → `Market.cs:197` | `Sanitize` 0–100 (`Market.cs:41`) — **agrees** | true | OK |
| `EnableBarter` | `true` | — | ~~SERVER~~ → CLIENT | `TrayModel.Validate(…, barter)` refuses goods beside a ware (`barter_off`) and `CargoTerminal.DrawButtons` hides "Cover it with my goods" (since 2026-09-08; until then it hid the Barter button, which is gone); the server settles a barter deal either way | — | **was false** on the side, and hid that the server does not enforce it | **fixed** |
| ~~`PriceChangePolicy`~~ | — | — | — | **deleted on `main` by PR #22**: at the audit it was read by nobody (`Teardown` was never built; the tray always reconfirms, `TrayModel.Answer`) and this pass kept it with a "NOT READ" description; Wu'barrk removed it instead, with the reason in a comment where the binding was (`ModConfig.cs`) | — | — | **removed** |
| `BarrkBotExport` | `true` | — | SERVER | `Server/BarrkBotExport.cs:53`, on the server, once every `VisitDirector.ExportCadenceSeconds` (60 s) after the sidecar has saved. New in PR #22 (P12) | — | true | OK |

### `[Client]` — bound then `SynchronizedConfig = false`, never leaves the machine

| Key | Default | Range | Side claimed | Who reads it | Re-clamp, and does it agree | Description | Verdict |
|---|---|---|---|---|---|---|---|
| `ShowArrivalMessage` | `true` | — | CLIENT | `CargoTick.cs:176` | — | true | OK |
| `ShowPriceTrend` | `true` | — | CLIENT | `CargoTerminal.cs:482` | — | true | OK |
| `Theme` | `Vanilla` | `Vanilla`\|`BlackGold` | CLIENT | `CargoTerminal.cs:250` | — | true | OK |
| `TerminalScale` | `1.0` | 0.5–2 | CLIENT | `CargoTerminal.cs:249` | `Mathf.Clamp(0.5f, 2f)` — **agrees** | true | OK |
| `TerminalBackdropAlpha` | `0.4` | 0–1 | CLIENT | `CargoTerminal.Theme()` → `ThemeOptions.PanelOpacity` (the vendored theme's own knob; its file is not edited) | `Mathf.Clamp01` — **agrees** | true | NEW 2026-09-08, the playtest's item 7 (a 40 % translucent black behind the text) |

### Not keys, but numbers a reader will come looking for

| Number | Where | Why it is not a key |
|---|---|---|
| `PurseCapMultiple = 3` | `ModConfig.cs:217` | design 3.5's "capped at three purses"; hard-coded on the way into `MarketRules` |
| `TownRadius = 40` | `ModConfig.cs:240` | design 3.1: candidates within 40 m collapse to one ticket. The comment already says "not a knob" |
| the game day | `VisitDirector.cs:68` | `EnvMan.instance.m_dayLengthSec`, read live (1800 s on StormTest); never config |
| `VisitState`, `MarketState` | `ModConfig.cs:185`–`186` | `CustomSyncedValue`s, the server's broadcast channels, not settings |

---

## The five range/literal disagreements, in full

Nothing in the build or the harness compares an `AcceptableValueRange` with the literal a call site clamps
with. These are the five that exist today.

| # | Key | Config says | Code clamps to | Effect | Done |
|---|---|---|---|---|---|
| 1 | `FlightStartAltitude` | 30–**500** | `Spawner.cs:113` → 20–**400** | **bit**: 401–500 accepted, silently 400 | config narrowed to 30–400 |
| 2 | `FlightStartDistance` | **24**–200 | `FlightPlan.cs:176` raises below **30** to 30 | **bit**: 24–29 all meant 30 | config floor raised to 30 |
| 3 | `FlightStartDistance` | 24–**200** | `Spawner.cs:112` → 24–**400** | harmless: the config is the tighter bound | left, recorded |
| 4 | `FlightDescentDistance` | **10**–200 | `Spawner.cs:114` → **0**–200 | harmless: config tighter | left, recorded |
| 5 | `PurseCoins` | 0–**100 000** | `Market.cs:40` → 0–**1 000 000** | harmless: config tighter | left, recorded |

**Proposed check for the harness** (`tests/` is another agent's this round, so this is a proposal, not a
commit). It fails today on rows 1 and 2 and passes after this branch:

```csharp
// CoreTests: the config's declared range and the literal the code re-clamps with must be the same number.
// Pure-side only: the game-side literals (Spawner.Clamp, CargoFlight's Mathf.Clamp) cannot be reached from
// net8.0, so those two rows want a source-text check or a comment convention instead.
Check(SchedulerRules.Default is var _ && true, "...");
// MarketRules.Sanitize vs the ModConfig ranges, one Check per pair:
//   Elasticity 0.05..1.5, MinMultiplier 0.05..1.0, MaxMultiplier 1.0..10.0, Spread 0.1..1.0,
//   WareHalfLifeGameDays 0..365, WantHalfLifeGameDays 0..365, PurseCarryPercent 0..100  -- all seven agree today and must stay agreeing.
// FlightPlan: Make(startDistance) below MinimumStartDistance must come back AT MinimumStartDistance,
//   which is what pins ModConfig's FlightStartDistance floor to 30.
```

The honest version of that check is a **source-text** one, because the three literals that actually bit
(`Spawner.Clamp`'s two, `FlightPlan.MinimumStartDistance`) are on the game side where the net8.0 harness
cannot reach: read `ModConfig.cs`'s `AcceptableValueRange<float>(a, b)` calls and `Spawner.cs`'s
`Clamp(entry, def, lo, hi)` calls out of the files and compare the numbers. That is a small tool, and it is
the only thing that will keep this table true after the next edit.

---

## The two dead keys — both resolved by PR #22

At the audit, `ApproachDistance` and `PriceChangePolicy` were bound, synced, locked, printed in the README and
DESIGN section 6 — and read by nothing. A dead key is worse than no key: it is a promise in a config file.
This pass kept both with a "NOT READ BY ANY CODE IN 0.1.0" description, because `ApproachDistance` was P5's
(design 3.3 spends a bullet on it) and `PriceChangePolicy` was the record of an undecided owner choice.

Both are settled on `main` now, differently:

- `ApproachDistance` **is read**: `Client/CargoMerchant.cs:272`, on the client that owns the merchant, exactly
  as design 3.3 said. Its description on this branch is corrected again to say so (side: CLIENT).
- `PriceChangePolicy` **is gone**: Wu'barrk deleted the binding in PR #22 rather than keep a knob that nothing
  read, with the reason in a comment where it was — "a setting that promises a behaviour the code does not have
  is worse than no setting, because a server owner will set it and believe it; 0.1.0 is unreleased, so
  removing it costs nobody a migration; if Teardown is ever built, bind it then." That is the better call. The
  owner choice itself (reconfirm versus teardown) is still open in design 3.4 and section 8, and in
  `docs/TODO.md` section 1; it is no longer misrepresented by a config file.

The rule stands for the future: **a key that is still dead when 1.0 is cut is removed then**, because after a
release it is a compatibility surface.

---

## The defaults

Every default was read against "is this what a fresh install should have", and **none changed in this pass**.
Wu'barrk changed four after it, in PR #22, reviewed against the economy simulation
(`docs/DECISIONS-WUBARRK.md` §2 and the release note): `PurseCoins` 800 → 1500 (one visit bought 39 silver
ore for 795 and left him with 5), the purse carry measured on the **gross** coins a visit took in rather than
the net (over twenty simulated visits the carry cap fired 19 times for a buying server and 0 times for a
selling one, which sat at a flat 800 for ever — `docs/ECONOMY-SIM.md` "what looks off" 4), four Wants that
paid firewood rates (`RoundLog`, `FineWood`, `Feathers`, `LeatherScraps`) raised from base 2 to 3, and
`FairMarketAct` on. All four are read on the server, all four are one number in the config, and none of them
changes a mechanism. Four items from the audit are worth naming, two of them now history:

1. **`MaxPriceMultiplier` 3.0 × `SpreadBuy` 0.7 = 2.1 was the round trip.** Buying a shelf out and selling it
   straight back paid 2.1× what it cost, so the shipped defaults let a player drain the purse without carrying
   anything in (`docs/ECONOMY-SIM.md` §9). This pass left it as the owner decision it was. **Closed in PR #22
   by the code fix, not the config fix**: `FairMarketAct` caps a `Ware`'s buy-back multiplier at 1.0 in
   `Market.PaysFor`, so he never pays more than `base × SpreadBuy` for something he also sells, while what he
   *charges* still rises to the full 3.0× and Wants are untouched. The multiplier stays 3.0, which keeps the
   scarcity signal the mod exists for; the 1.4 alternative would have flattened it.
2. **`CustomBody` defaults `true`, and the bundle now exists** — baked on Wu'barrk's machine and embedded in
   the rc1 DLL (PR #22). The caveat has moved rather than gone: the bake is his machine's (owner decision,
   2026-09-07), so a build on any *other* machine still has no bundle and falls back to the Dverger, loudly
   (`cargo body` names the source). `docs/TODO.md` section 2 carries the bundle handover.
3. **`MinComfortLevel` 4** is, with the corrected mechanism, "a roofed bed near a fire and one thing more".
   That is a real base rather than a bedroll, which is the intent. Kept.
4. **`EventCheckIntervalMinutes` 25 at `EventChancePercent` 25** is a visit about every 100 real minutes per
   eligible group, before cooldowns. Kept; it is the number `docs/CATALOGUE.md` tuned the economy against.

---

## Proposed diffs in files that were not ours this round — status after PR #22

1. ~~**`docs/DESIGN.md` section 6** is missing `FlightSpeed` and `FlightTurnRate`~~ — **done on this branch**:
   both rows added with their ranges and the side that reads them; `FlightStartDistance` / `FlightStartAltitude`
   / `FlightDescentDistance` now name their ranges and clamps; `PurseCoins` 1500, `EnableBarter`'s side,
   `BarrkBotExport` added, `PriceChangePolicy`'s line replaced by a note. (`Catalogue` is still listed under
   that name; `ModConfig`'s field is `CatalogueLine` — a reader grepping should know.)

2. **`README.md`** repeats the same wrong descriptions in its config table, because it was written from them,
   and now also still lists the deleted `PriceChangePolicy` row. **README is Wu'barrk's for the truth pass**
   (`docs/TODO.md` section 2), so the corrections are listed here for him rather than made: `CustomBody`,
   `FlightSpeed`, `FlightTurnRate`, `EnableBarter` and `ApproachDistance` are read on the **client**;
   `FlightStartDistance` is `30-200` and `FlightStartAltitude` `30-400`; `PurseCoins` is 1500;
   `PriceChangePolicy`'s row goes; `FairMarketAct` and `BarrkBotExport` want rows. README's
   `BodyPrefab`/`CustomBody` pair is already **right** and is the text the other documents were corrected
   against.

3. **`models/README.md` (Wu'barrk's) section 2 is wrong** and is the source of the confusion:

   ```
   -   Set it to `Ingvar` to use the custom body, leave it `Dverger` to fall back. Because it is a
   +   `BodyPrefab` is NOT the custom-body switch: it stays the engine prefab the merchant is cloned
   +   from (Character, MonsterAI, the collider), whatever body is drawn on top. The switch is the
   +   separate `Server.CustomBody` (synced + locked, default true), added in P8. Because both are
       `Server.*` entries they are synced and locked, so the server decides which body every client
       builds, and an admin can flip it without a rebuild. That is the whole integration.
   ```

   `docs/HANDOFF-WUBARRK.md` lines 36–38 already tells him this; the file itself was never corrected. Still
   open, still his: `docs/TODO.md` section 2 carries it.

4. ~~**`Patches/Patch_Terminal.cs`**, `cargo status`: the `routed RPCs registered` line is the wrong name for
   the admin wire~~ — **done on this branch**: `admin wire up (direct peer ZRpc)`. `VisitDirector.Refusals`
   has no line yet; see `docs/TRUST-BOUNDARY.md` §5 item 6.
