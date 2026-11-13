<!-- README schema v2 -->
# 🪶 Valkyrie's Cargo

![VALKYRIE'S CARGO](https://img.shields.io/badge/VALKYRIE%27S_CARGO-A_MERCHANT_FROM_THE_SKY-c9a227?style=for-the-badge&labelColor=1b1b1b)
![VERSION](https://img.shields.io/badge/VERSION-0.1.7-8b1e1e?style=for-the-badge&labelColor=1b1b1b)
![CLIENT & SERVER](https://img.shields.io/badge/CLIENT_%26_SERVER-SAME_VERSION_ON_BOTH-1f4e79?style=for-the-badge&labelColor=1b1b1b)

> *You bank the fire, set down your axe, and the light goes strange. Wings beat in the upper skies, and she comes down out of the cloud with a red-bearded dwarf hanging from her talons, and sets him on his feet beside your hearth like a parcel she was paid to deliver.*

### A Valkyrie air-drops a merchant at your door. You have five minutes.

Nobody summons him. When you have earned a quiet moment, rested and comfortable at your own fire, the server decides the sky is right, and a Valkyrie carries **Ingvar the Far-Travelled** down to your hearth. He walks up, calls out, and opens a trade window of his own. Then he vanishes into the mist the way Odin does.

Behind the window is a **live, persistent, server-owned economy**: prices that move with what the world actually buys and sells, a purse that can run dry, and a shelf that changes between visits. Every player on the server trades in the same market.

---

![NEW IN 0.1.7](https://img.shields.io/badge/NEW_IN_0.1.7-VALHEIM_1.0.21-8b1e1e?style=for-the-badge&labelColor=1b1b1b)

**This version is the Valheim 1.0.21 build. Nothing else is new.**

| You | It |
|---|---|
| **Play on Valheim 1.0.21** | the mod is rebuilt and checked against it; the gameplay is unchanged |
| **Update the server and every player together** | Valheim 1.0.21 moved the game's network version to 41, and the mod's version gate moves with it: a player on the previous build is refused at the door |
| **Update** | nothing to delete; your config file carries over as it is |

---

![BEFORE YOU INSTALL](https://img.shields.io/badge/BEFORE_YOU_INSTALL-READ_THIS-b8860b?style=for-the-badge&labelColor=1b1b1b)

| | If… | Then… |
|---|---|---|
| 🤝 | you play on a **server** | install it on the server **and every player**, the same version. A player without it, or on another version, is refused at the door and told why, by name and number. |
| 🔄 | **Valheim updates** | the game refuses a player on another network version before this mod is ever asked, so when the game moves, replace every copy of the mod together. |
| 🧪 | you want to know **how finished it is** | it is a first playable, and honest about it. The whole loop has run end to end on a dedicated server with real players, more than thirty visits. Redelivery after a lost connection has never been caught happening in a live session, and two players trading at one merchant is thinly tested. |
| 💾 | you are **updating** | nothing to delete. The mod migrates its own config file: a value still at an old default moves to the new one, a value you set stays, a backup lands beside the file, and the boot log says what it did. |

---

![I](https://img.shields.io/badge/I-THE_VISIT-c9a227?style=for-the-badge&labelColor=1b1b1b)

**No horn, no command, no altar. You earn him.**

| | |
|---|---|
| 🔥 **You earn it** | the server rolls for a visit while you are **rested**, at **comfort 4 or more**, by day, at a base you built yourself |
| 🕊️ **A real approach** | the Valkyrie's flight is planned: a genuine line through the sky, flown the same way on every screen near enough to see it, low enough that you can see what she is carrying |
| 🧳 **The drop** | she releases Ingvar beside your fire and turns for the horizon |
| 🚶 **He walks up** | Ingvar crosses the ground to you himself and greets you in his own words |
| 💬 **He trades** | press **E** for the Cargo Terminal |
| 🌫️ **He leaves** | after five minutes, or when you send him off early, he gives you a parting word and vanishes in a burst of mist |

<details>
<summary>📖 <b>Read the saga</b> — where he will and will not land</summary>

**He insists on ground somebody built.** The game's own idea of a "base" cannot tell your hall from the game's own camps, so Ingvar also wants something player-built near you. That keeps him out of the Bog Witch's camp, where a player once found him.

**He keeps away from other people's doors.** No visit while you stand at another merchant's camp (Haldor, Hildir, the Bog Witch, any modded trader), at the door of a crypt, cave, mine or fortress, or at a place the game pins on the map, such as the Sacrificial Stones or a boss altar. Inside a dungeon, never.

**The approach fits what your game has loaded.** Valheim loads what is within about 96 m of you, and the flight is laid out to fit inside that, so everyone near enough sees the same descent.

**The visit's clock runs whoever is near him.** He leaves on time even with nobody online. A server owner can hold the clock while the server is empty, so a visit that starts at bedtime is still there in the morning.

</details>

---

![II](https://img.shields.io/badge/II-A_MARKET_THAT_REMEMBERS-2e5e3a?style=for-the-badge&labelColor=1b1b1b)

**This is the part that is not decoration. One shared economy, the same on every visit and every screen.**

| | |
|---|---|
| 📈 **Supply and demand** | buy him out and the price climbs; flood him with iron and he pays less for the next load. Every number you see was computed from stock real players moved |
| 🌊 **What he only buys drifts back** | a row you flooded clears over a few in-game days, so he never fills up for good. What is on his shelf keeps exactly what trading left |
| 💰 **A purse, not a printer** | he arrives with **1500 coins**, plus half of what he took in on his last visit, and no more. When it is dry he is done buying: barter, or come back next visit |
| ⚖️ **The Fair Market Act** | no buy-it-out-and-sell-it-back loop: within a visit he never pays more for an item than the lowest price he sold it at, and he never buys a row back above its par price |
| 🔄 **A rotating shelf** | he sells **20 of a 101-entry catalogue**, re-rolled every **2 in-game days**. What he has this week is not what he had last week |
| 🤝 **Barter is first-class** | pay with what you carry: the terminal covers the balance from your goods at his live rates, with one button |
| 💾 **It survives a restart** | stock, purse and prices are written beside your world save. A visit interrupted by a server restart picks up where it stopped |
| 📬 **Nothing is lost** | a deal settled but not delivered is held on the server and redelivered when you come back. A full pack counts too: the server keeps the deal until it fits |

<details>
<summary>📖 <b>Read the saga</b> — how a deal is kept honest</summary>

**The server decides.** Your game proposes a deal; the server settles it at the prices **it** holds, and only then does an inventory change. There is no way for a player's game to forge a trade.

**A deal lands whole or not at all.** Every line of a deal is checked against your pack before it is sent. If the pack cannot take all of it, nothing changes, and the server keeps the delivery until it fits: type `cargo claim`. A delivery owed to a full pack has been watched holding through a server restart and landing exactly once.

**You trade with Ingvar where he is.** A deal from more than 96 m from the visit is refused, in the terminal with his own line.

**Admins can edit the catalogue live,** between visits, with `cargo catalogue add`, `remove` and `reset`. No restart, and the change reaches every player within a second.

</details>

---

![III](https://img.shields.io/badge/III-INGVAR_HIMSELF-5b2c83?style=for-the-badge&labelColor=1b1b1b)

**Not a re-textured Dverger. A character of his own.**

| | |
|---|---|
| 🧔 **His own body** | red-bearded, a head shorter than you, in heavy plate, and he watches you while you shop |
| 🎭 **His own moves** | idle, walk, greeting, talk, shrug and nod |
| 📦 **Inside the plugin** | no second download, no asset pack, nothing extra to install |
| 🛡️ **Here to trade** | unkillable, unhostile, and entirely uninterested in your Greydwarf problem. On a server whose taming mod made the Dvergr a pet, he still trades and hovers as himself |

---

![IV](https://img.shields.io/badge/IV-THE_CARGO_TERMINAL-1f4e79?style=for-the-badge&labelColor=1b1b1b)

**Our own window, not a reskin of the vanilla store.**

| | |
|---|---|
| 🪙 **Live prices** | every row at the price the server holds right now, with an amber mark on a price that moved |
| 🤝 **Barter** | one button pays the balance from your goods |
| 🎨 **Your frame** | Vanilla or BlackGold metal, drawn at the size you like, over a backdrop as dark as you like |

---

![THE TOOLBOX](https://img.shields.io/badge/THE_TOOLBOX-QUICK_START_%C2%B7_KEYS_%C2%B7_HOW--TO-1b1b1b?style=for-the-badge&labelColor=8b1e1e)

**First ten minutes:** install it on the server and every player → build a base with a roof, a fire, a bed and a seat → rest until you are **Rested** at comfort 4 or more, by day → listen for the wings → walk up to Ingvar and press **E**.

| Key | | Command | |
|---|---|---|---|
| **E** | open the Cargo Terminal | `cargo status` | everything at a glance: the visit, the market, the schedule, the engine check |
| | | `cargo stock [item]` | his shelf and its live prices |
| | | `cargo claim` | ask the server for any delivery it still owes you |
| | | `cargo catalogue list` | the full catalogue |
| | | `cargo body` | Ingvar's model, clips and bones: ask for it first if he looks wrong |
| | | `cargo engine` | the Valheim build this was written against, and the one you are running |
| | | `cargo visit [player]` | *(admin)* send a visit now |
| | | `cargo dismiss` | *(admin)* end the visit now |
| | | `cargo catalogue add` · `remove` · `reset` | *(admin)* edit the shelf live, between visits |

Admin commands are checked **on the server**, so an admin can type them from their own game and a non-admin cannot.

<details>
<summary>🛠️ <b>Change what he carries</b></summary>

The setting `CatalogueOverrides` holds only your changes to the shipped catalogue, comma-separated: `Prefab:Base:Target:Max:Kind` adds or changes a row, and `-Prefab` removes one. Empty means the shipped 101 rows as they ship. `cargo catalogue add`, `remove` and `reset` edit it for you on a running server; the change applies as soon as no visit is running. A prefab the game has no item for is dropped with one log line.

</details>

<details>
<summary>🛠️ <b>Report a problem</b></summary>

Type `cargo status` and copy what it prints: it names its own sources. Send it with your `BepInEx/LogOutput.log`. Those two usually say why, and we would genuinely like to hear about it.

</details>

---

![MULTIPLAYER](https://img.shields.io/badge/MULTIPLAYER-ONE_MERCHANT%2C_ONE_MARKET-1f4e79?style=for-the-badge&labelColor=1b1b1b)

| Works with vanilla players and servers | Needs it on the server | Needs it on both ends |
|---|---|---|
| no: a player without the mod is refused at the door, by name and number | the market, the schedule, the purse and every settled deal · the save beside the world · the Backpacks check | the flight · Ingvar's body · the terminal · the version gate |

**Built for dedicated servers first.** Listen hosts and single player work too. Settings are pushed and locked from the server, so everyone plays the same economy whatever is in their own file. Everyone near the hearth sees one shared visit: the same descent, the same merchant, the same shelf, and the same prices moving under you as other people trade.

**Versions must match.** The server refuses a player on any other version of the mod and tells them which. Update the server and its players together.

---

![COMPATIBILITY](https://img.shields.io/badge/COMPATIBILITY-PLAYS_WELL_WITH_OTHERS-2e5e3a?style=for-the-badge&labelColor=1b1b1b)

| Mod | |
|---|---|
| **Vanilla traders** | Untouched. The mod does not patch the vanilla store, the weather or any vanilla trader. |
| **Backpacks** (Smoothbrain) | Optional and found by itself: when it is loaded on the server, Ingvar's shelf doubles, 20 to 40 of the catalogue. It needs a Backpacks build that loads on your Valheim; until then the shelf stays its normal size. |
| **Taming mods** | Ingvar trades and hovers as himself even on a server whose taming mod made the Dvergr a pet. |
| **The Raven Iron family** | Built to sit beside Cairn, Undertow, FireFront, Ragnarok's Wrath, RavenEye and Yggdrasil's Reckoning. |
| **Large modpacks** | Run beside a 117-plugin modpack on an earlier Valheim; not yet with a pack that size on this one. |
| **Hexium · Gale · r2modman** | Install it through any Thunderstore-compatible manager; the dependency comes with it. |

At boot the mod checks the game it is actually running on and prints what it found. A feature that depends on something the game has moved switches itself off and says so, rather than taking your session down. `cargo engine` shows that check.

---

![SETTINGS](https://img.shields.io/badge/SETTINGS-BEPINEX_CONFIG-b8860b?style=for-the-badge&labelColor=1b1b1b)

All in `BepInEx/config/com.raveniron.valkyriescargo.cfg`, and all safe to leave alone. Every setting that changes the game is server-synced and admin-controlled; only the look of your own terminal is local.

<details>
<summary>🔒 <b>Server-synced</b> — set on the server, locked for everyone else</summary>

| Section | Setting | What it does |
|---|---|---|
| Server | `LockConfiguration` | the server's values win on every player; admins may still change them |
| Server | `Enabled` | roll visits at all |
| Server | `RequireRested` · `MinComfortLevel` · `MinBaseValue` · `DaytimeOnly` | who qualifies: rested, how comfortable, how much base around them, by day only |
| Server | `RequireBuiltBase` · `BuiltBaseRadius` | a visit needs something player-built near you, and how far to look |
| Server | `AvoidMerchantCamps` · `AvoidDungeonEntrances` · `AvoidLandmarks` · `LocationClearance` | no visits at other merchants' camps, dungeon doors or map landmarks, and the margin around them |
| Server | `EventCheckIntervalMinutes` · `EventChancePercent` | how often the server rolls for a visit, and the chance per roll |
| Server | `PlayerCooldownMinutes` · `CooldownRadius` | how long before the same player can be chosen again, and how far a base's cooldown reaches |
| Server | `MerchantLifespanSeconds` · `PauseVisitWhenEmpty` | how long a visit lasts, and whether its clock holds while nobody is online |
| Server | `ApproachDistance` | how close Ingvar walks up before he stops |
| Server | `BodyPrefab` · `CustomBody` | the game creature he is built on, and his own body on top of it (off keeps the stand-in) |
| Server | `FlightStartDistance` · `FlightStartAltitude` · `FlightDescentDistance` · `FlightSpeed` · `FlightTurnRate` | the Valkyrie's approach and how she flies |
| Server | `CarryOffset` | where he hangs from the Valkyrie's talon; empty follows the Valkyrie's own |
| Server | `CatalogueOverrides` | your changes to the shipped catalogue |
| Server | `PriceElasticity` · `MinPriceMultiplier` · `MaxPriceMultiplier` · `SpreadBuy` | how steep the price curve is, its floor and ceiling, and what he pays against what he charges |
| Server | `FairMarketAct` | the cap that stops a buy-and-sell-back loop |
| Server | `WareHalfLifeGameDays` · `WantHalfLifeGameDays` | how fast stock drifts back between visits, for what is on his shelf (never, by default) and what he only buys |
| Server | `ShelfSize` · `ShelfRotationGameDays` | how many rows he sells at a time, and how often the shelf re-rolls |
| Server | `BackpackShelfMultiplier` · `BackpackModGuid` | the Backpacks add-on: the shelf multiplier, and which mod to look for |
| Server | `PurseCoins` · `PurseCarryPercent` | what he arrives with, and how much of his takings carries to the next visit |
| Server | `EnableBarter` | show the terminal's Barter button |
| Server | `BarrkBotExport` | write the market, trader and visit files a server bot can read |

</details>

<details>
<summary>🖥️ <b>Local to your game</b> — yours alone</summary>

| Section | Settings |
|---|---|
| **Client** | `ShowArrivalMessage` · `ShowPriceTrend` · `Theme` · `TerminalScale` · `TerminalBackdropAlpha` · `BodyYawDegrees` (the arrival line when you are chosen, the price-trend mark, the terminal's frame, size and backdrop, and which way Ingvar's body faces) |

</details>

---

![CREDITS](https://img.shields.io/badge/CREDITS-THANK_YOU-1b1b1b?style=for-the-badge&labelColor=c9a227)

**Needs:** [BepInExPack Valheim](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/) (denikson), the mod loader. **Optional:** [Backpacks](https://thunderstore.io/c/valheim/p/Smoothbrain/Backpacks/) (Smoothbrain), for the doubled shelf.

**Raven Iron is NomadicWar and Wu'barrk.** Both founders, both designers of this mod, and between them everything in it.

- ⚒️ **NomadicWar**, co-founder, Raven Iron: design; the market and the economy, the visit director, persistence and the server side.
- 🎨 **Wu'barrk**, co-founder, Raven Iron: design; Ingvar himself (model, rig, texture and animation), the Cargo Terminal, and the VikingOS interface work beneath it.

🪶 Nothing in this package is ripped, re-uploaded, or derived from another creator's assets.

*Created by Raven Iron: NomadicWar and Wu'barrk.* · [ravenirongames.com](https://ravenirongames.com/) · **Vendored, and not ours:** ServerSync is blaxxun's `ConfigSync.cs` (MIT-0). The terminal's frame and focus handling are Wu'barrk's VikingOS shared source (MIT). Both carry their origin and version in their own file headers and are compiled in unedited.
