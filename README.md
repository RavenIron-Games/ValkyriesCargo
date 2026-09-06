# Valkyrie's Cargo

A Valheim mod by [Raven Iron](https://github.com/RavenIron).

**A Valkyrie drops a wandering merchant beside your base when you are rested. He buys and sells for
five minutes at prices that move with what the world sells him, then vanishes like Odin.**

> **Status: not yet playable.** This is the scaffold of the mod: it loads, its config is in place and
> its console answers, but no merchant visits yet. It is published here so the design, the catalogue and
> the item data are in one place while the systems are built. Watch the changelog.

---

## What it will do

**The visit.** When you are rested, your comfort is 4 or more and you are at a base, the server may
send Ingvar the Far-Travelled your way: a Valkyrie flies in from the horizon with him in her talons,
drops him beside your hearth, and he walks up and calls out. Everyone nearby sees all of it. Never on
command: the visit is a roll every 25 minutes at 25%, and a summon horn is a later, earned feature.

**The trade.** Press E on him for the Cargo Terminal: his wares on one side, the goods he wants on the
other, a staging tray in between. He carries a live stock that persists per world. Buy him out and the
price climbs; flood him and he pays less; a game day later it has drifted back. He pays coins or takes
your goods in barter at his live buy price, and he arrives with a purse of 800 coins, so nobody can dump
a warehouse on him. If a price moves while you are staging, the line turns amber and you confirm once
more; nothing leaves your inventory until the server has answered.

**The departure.** Five minutes, or Shift+E twice to send him off. He speaks a farewell and vanishes in
Odin's own effect.

## What it will not do

No horn item in 0.1. No custom body yet: a dvergr stands in for Ingvar until the model is rigged. No
patch on the vanilla trader or store. Nothing happens on command except an admin's `cargo visit`.

---

## Installing

**Server:** required. **Every client:** required, on the same version. The mod refuses a client that
does not have it or runs another build, with a message naming the mod and both versions, because the
visuals, the terminal and the trade all live in client code.

On a Gale-managed client the plugin folder is
`%APPDATA%\com.kesomannen.gale\valheim\profiles\<profile>\BepInEx\plugins\`, not the Steam folder.

Requires [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).
Built against Valheim 0.221.x (Unity 6000.0.61f1).

---

## Configuration

`BepInEx\config\com.raveniron.valkyriescargo.cfg`. Every `Server.*` value is enforced by the server on
every client (`LockConfiguration`, default on; admins on `adminlist.txt` may still change them).
`Client.*` values are yours.

| Key | Default | Meaning |
|---|---|---|
| `RequireRested` | true | the Rested effect is needed to be eligible |
| `MinComfortLevel` | 4 | a bed, a fire and a roof give 3; a chair or a banner makes 4 |
| `EventCheckIntervalMinutes` | 25 | real minutes between rolls |
| `EventChancePercent` | 25 | chance per roll when someone is eligible |
| `MerchantLifespanSeconds` | 300 | how long he stays; Odin's own timer is 300 |
| `PurseCoins` | 800 | what he arrives with |
| `PriceElasticity`, `MinPriceMultiplier`, `MaxPriceMultiplier`, `SpreadBuy` | 0.35, 0.4, 3.0, 0.7 | the price curve and what he pays as a fraction of what he charges |
| `StockHalfLifeGameDays` | 1 | how fast his stock drifts back to normal between visits |
| `Catalogue` | 72 entries | what he sells and buys, `Prefab:Base:Target:Max:Kind`; see `docs/CATALOGUE.md` |
| `Client.Theme` | Vanilla | terminal metal colour, `Vanilla` or `BlackGold` |

An unknown prefab name in the catalogue is dropped with one log line; the rest still loads.

## Console

`cargo status` — role, which side owns the config, the catalogue, the engine numbers the mod depends on.
`cargo version` — this build.
`cargo prefab <name>` — a prefab's components, children and effect lists (try `Valkyrie`, `Dverger`, `odin`).

Planned, admin only: `cargo visit`, `cargo dismiss`, `cargo stock`, `cargo reset`.

---

## Credits

Designed with Thorium Wu'barrk. Terminal theme from Wu'barrk's VikingOS (MIT). ServerSync by blaxxun
(MIT-0). Item data checked against Wu'barrk's TheEye dump of 2026-07-31.
