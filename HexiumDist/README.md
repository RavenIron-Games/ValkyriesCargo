<div align="center">

# 🪶 Valkyrie's Cargo 🪙

![Valheim Mod](https://img.shields.io/badge/Valheim-Merchant_Encounter-orange.svg)
[![Multiplayer Compatible](https://img.shields.io/badge/Multiplayer-Server--Synced-blue.svg)]()
[![Valheim](https://img.shields.io/badge/Valheim-1.0.12-critical.svg)]()
[![Framework](https://img.shields.io/badge/Requires-BepInEx-red.svg)]()
[![Economy](https://img.shields.io/badge/Economy-Supply_%26_Demand-green.svg)]()
[![Custom Character](https://img.shields.io/badge/Ingvar-Custom_Character-purple.svg)]()
[![Version](https://img.shields.io/badge/Version-0.1.0-lightgrey.svg)]()

> *"You bank the fire, set down your axe, and the light goes strange. Wings beat in the upper skies —*
> *and she comes down out of the cloud with a red-bearded dwarf hanging from her talons, and sets him*
> *on his feet beside your hearth like a parcel she was paid to deliver."*

*A Valkyrie air-drops a merchant at your door. You have five minutes.*

**Requires Valheim 1.0.12**, on the server **and on every client**. Valheim 1.0.12 moved the network
version, so a build made for 1.0.7 or 0.221.12 cannot connect at all — that wall is the game's, not ours.

</div>

**Valkyrie's Cargo** adds a wandering merchant encounter that nobody summons. When you have earned a
quiet moment — rested, comfortable, at your own fire — the **server** decides the sky is right, and a
Valkyrie carries **Ingvar the Far-Travelled** down to your hearth. He walks up, calls out, and opens
a trade window backed by a **live, persistent, server-owned economy**: prices that move with what the
world actually buys and sells, a purse that can run dry, and a shelf that changes between visits.
Then he vanishes into the mist the way Odin does.

---

<details>
<summary>📜 <b>Contents</b></summary>

- [🪽 The Visit](#-the-visit)
- [🪙 A Market That Remembers](#-a-market-that-remembers)
- [🧔 Ingvar Himself](#-ingvar-himself)
- [🎒 Backpacks Add-On](#-backpacks-add-on)
- [🌐 Multiplayer & Server Authority](#-multiplayer--server-authority)
- [🖥️ Console](#️-console)
- [⚙️ Configuration](#️-configuration)
- [🛠️ Compatibility](#️-compatibility)
- [📦 Dependencies](#-dependencies)
- [📥 Installation](#-installation)
- [📖 Status — 0.1.0](#-status--010)
- [🐦‍⬛ Credits](#-credits)

</details>

---

## 🪽 The Visit

| | |
| :--- | :--- |
| 🔥 **You earn it** | No horn, no command, no altar. The server rolls for a visit while you are **rested** and **comfort 4+** at your own base. |
| 🕊️ **A real approach** | The Valkyrie's flight is *planned* — a genuine line through the sky, flown the same way on every screen near enough to see it (Valheim loads what is within about 96 m of you, and the approach is laid out to fit inside that). She comes in low enough that you can see what she is carrying. |
| 🧳 **The drop** | She releases Ingvar beside your fire and turns for the horizon. |
| 🚶 **He walks up** | Ingvar crosses the ground to you himself and greets you in his own words. He is unkillable, unhostile, and entirely uninterested in your Greydwarf problem. |
| 💬 **He trades** | Press **`E`** for the Cargo Terminal — our own window, not a reskin of the vanilla store. |
| 🌫️ **He leaves** | After five minutes — or when you send him off early — he gives you a parting word and vanishes in a burst of mist. |

---

## 🪙 A Market That Remembers

This is the part that is not decoration. Ingvar's stock is one **shared, persistent economy**, the
same on every visit and on every player's screen.

| Feature | What it means at the table |
| :--- | :--- |
| 📈 **Supply & demand** | Buy him out and the price climbs. Flood him with iron and he pays less for the next load. Every number you see was computed from stock that real players moved. |
| 🌊 **Price drift** | Prices ease back toward their resting value over in-game days — a market you wrecked on Tuesday is worth visiting again by Friday. |
| 💰 **A purse, not a printer** | He arrives with **1500 coins** and no more. When it is dry he is done buying; barter, or come back next visit. |
| ⚖️ **The Fair Market Act** | He will **never** pay you more for a thing than he charges for it. No buy-low-sell-high loop against your own merchant. |
| 🔄 **A rotating shelf** | He carries **20 of a 72-entry catalogue**, re-rolled every **2 in-game days**. What he has this week is not what he had last week. |
| 🤝 **Barter is first-class** | Pay with what you are carrying. The terminal covers the balance from your goods at his live rates — one button. |
| 💾 **It survives a restart** | Stock, purse and prices are written beside your world save. A visit interrupted by a server restart picks up where it stopped. |
| 📬 **Nothing is lost** | A deal settled but not delivered — you crashed, you dropped — is held on the server and redelivered when you come back. Built and tested off-game against the ledger; it is the one path in this mod we have never managed to catch happening in a live session, so we would especially like to hear if it ever bites you. |

> 💡 **Admins:** the catalogue is editable **live**, between visits, with `cargo catalogue add/remove/reset`.
> No restart, and the change reaches every client within a second.

---

## 🧔 Ingvar Himself

Ingvar is **not** a re-textured Dverger. He is his own character, with his own **idle, walk, greeting,
talk, shrug and nod**, and he ships **inside the plugin**. There is no second download, no asset pack,
nothing extra to install.

He stands a head shorter than you, red-bearded, in heavy plate, and he watches you while you shop.

---

## 🎒 Backpacks Add-On

Running [**Smoothbrain's Backpacks**](https://thunderstore.io/c/valheim/p/Smoothbrain/Backpacks/)?
Ingvar notices.

> ⏳ **Waiting on Valheim 1.0.** Backpacks has no 1.0 build yet. Until it does, nothing here engages and
> the shelf runs at its single size — the detection is live and will pick it up the day it lands.

| | |
| :--- | :--- |
| 🎒 **He wears one** | The real pack, on his back — the same one your players wear. |
| 📦 **He carries more** | The shelf **doubles** (`20 → 40` of the catalogue). A world that hauls more gets a bigger caravan. |

Entirely optional and auto-detected. Without it, he simply travels light and nothing changes.

---

## 🌐 Multiplayer & Server Authority

Built for dedicated servers first, not adapted to them afterwards.

- 🔒 **Server-authoritative.** The market, the schedule, the purse and every settled deal live on the
  server. A client *proposes*; the server decides, at the prices **it** holds, and only then does an
  inventory change. There is no client-side path to forging a trade.
- 🔗 **ServerSync.** Configuration is pushed and locked from the host, so everyone plays the same
  economy whether they edited their config file or not.
- 👥 **One shared visit.** The same descent, the same merchant at the same hearth, the same shelf —
  and the same prices moving under you as other people trade.
- 🛡️ **Version gate.** A client without the mod, or on the wrong version, is refused at the handshake
  and told exactly why, by name and number.

---

## 🖥️ Console

Typed into the in-game console. Admin verbs are checked **on the server**, so a client can run them
and a non-admin cannot.

| Command | What it does |
| :--- | :--- |
| `cargo status` | Everything at a glance: the visit, the market, the schedule, the engine check |
| `cargo stock [item]` | His shelf and its live prices |
| `cargo catalogue list` | The full 72-entry catalogue |
| `cargo body` | Ingvar's model, clips and bones — the first thing to ask for if he looks wrong |
| `cargo engine` | The Valheim build this was written against, versus the one you are running |
| `cargo visit [player]` | *(admin)* Send a visit now |
| `cargo dismiss` | *(admin)* End the visit now |
| `cargo catalogue add/remove/reset` | *(admin)* Edit the shelf live, between visits |

> 💡 `cargo status` **names its own sources.** It is the first thing to paste into a bug report.

---

## ⚙️ Configuration

Settings live in `com.raveniron.valkyriescargo.cfg`. **Every gameplay-affecting setting is
server-synced and admin-controlled**; only cosmetics are local.

### 🔒 Server-Synced (Admin Controlled)

| Setting | Default | What it does |
| :--- | :--- | :--- |
| `MinComfortLevel` | `4` | Comfort you need at your fire to qualify |
| `MerchantLifespanSeconds` | `300` | How long the visit lasts |
| `PurseCoins` | `1500` | What Ingvar arrives with |
| `PurseCarryPercent` | `50` | How much of his takings carries to the next visit |
| `ShelfSize` | `20` | How many of the 72 he carries |
| `ShelfRotationGameDays` | `2` | How often the shelf re-rolls (`0` = never) |
| `BackpackShelfMultiplier` | `2` | Shelf multiplier when Backpacks is loaded |
| `FlightSpeed` / `FlightTurnRate` | `8 m/s` / `45 °/s` | How the Valkyrie flies |
| `FairMarketAct` | `on` | He never pays more than he charges |
| `CustomBody` | `on` | Ingvar's own body; off keeps the stand-in |
| `CarryOffset` | `0, 0, 0` | Where he hangs from the Valkyrie's talon, `x, y, z` in the talon's own space. The default puts his feet on it. Leave it empty to follow the Valkyrie prefab instead |

### 🎨 Local to Your Game

| Setting | Default | What it does |
| :--- | :--- | :--- |
| `ShowArrivalMessage` | `on` | The banner when he lands |
| `ShowPriceTrend` | `on` | The amber mark on a price that moved |
| `Theme` | `Vanilla` | The terminal's frame |
| `TerminalScale` | `1.0` | How large the terminal draws |
| `TerminalBackdropAlpha` | `0.4` | How dark the terminal's backdrop sits |
| `BodyYawDegrees` | `180` | Which way Ingvar faces on the chassis |

---

## 🛠️ Compatibility

Built against **Valheim 1.0.12**. At boot the mod checks the game it is actually running on,
prints what it found, and any feature depending on something that has **moved** switches *itself*
off and says so — rather than taking your session down with it. `cargo engine` shows you that check.

- ✅ Dedicated servers, listen hosts and single-player
- ⚠️ **Valheim 1.0.12 only.** The game refuses a peer on another network version before this mod is
  consulted, so every copy has to be replaced by hand when the engine moves
- ✅ Run alongside a **117-plugin** modpack — on 0.221.12, where that pack existed. The 1.0 testing so
  far has been on a clean server, because most of the family has not moved to 1.0 yet
- ✅ Built to sit beside the family — Cairn, Undertow, FireFront, Ragnarok's Wrath, RavenEye and
  Yggdrasil's Reckoning — and proven beside them on 0.221.12
- ❌ Does **not** patch `StoreGui`, `EnvMan`, or any vanilla trader

---

## 📦 Dependencies

> ⚠️ **Requires:** BepInEx

| Dependency | Why |
| :--- | :--- |
| **BepInExPack Valheim** (denikson) | The mod loader (also provides HarmonyX) |

*Optional:* **Backpacks** (Smoothbrain) — unlocks the pack on his back and the doubled shelf.

## 📥 Installation

**With a mod manager (recommended):** install through Hexium, Gale, r2modman or any
Thunderstore-compatible manager — the dependency above is pulled in for you.

**Manual install:**
1. Install **BepInExPack Valheim**.
2. Drop `ValkyriesCargo.dll` into `BepInEx/plugins`.
3. On a dedicated server, install it on the **server and every client**.
4. Go and get comfortable.

---

## 📖 Status — 0.1.0

**A first playable, and honest about it.** The loop has been run end to end on a dedicated server
with real players — twenty-seven visits so far: the roll, the flight, the drop, the walk-up, the
terminal, deals across the wire, the price curve, the persistence and the departure have all been
watched on a screen. Some corners are proven only in the log, and a few only off-game.

**The one we will name outright:** redelivery after a lost connection is built, and tested off-game,
and has **never** been caught happening in a live session — the window between a deal being answered
and acknowledged is too small to stand in front of. Two-player trading at one merchant is also thinly
tested. Everything else on this page has been watched.

If something reads wrong, `cargo status` and your `BepInEx/LogOutput.log` will usually say why —
and we would genuinely like to hear about it.

---

## 🐦‍⬛ Credits

**Raven Iron is NomadicWar & Wu'barrk.** Both founders, both designers of this mod, and between them
everything in it.

| | |
| :--- | :--- |
| ⚒️ **NomadicWar** | Co-founder, Raven Iron. Design; the market and the economy, the visit director, persistence and the server side. |
| 🎨 **Wu'barrk** | Co-founder, Raven Iron. Design; Ingvar himself — model, rig, texture and animation — the Cargo Terminal, and the VikingOS interface work beneath it. |

**ServerSync** is blaxxun's `ConfigSync.cs` (MIT-0), compiled in as shared source and never edited.
The terminal's frame and focus handling are Wu'barrk's VikingOS shared source (MIT).

> 🪶 Nothing in this package is ripped, re-uploaded, or derived from another creator's assets.

<div align="center">
  <i>Created by Raven Iron — NomadicWar &amp; Wu'barrk</i>
  <br>
  <a href="https://ravenirongames.com/">ravenirongames.com</a>
</div>

---
<br>
<small>

**Vendored, and not ours:** ServerSync is blaxxun's `ConfigSync.cs` (MIT-0). The terminal's frame and
focus handling are Wu'barrk's VikingOS shared source (MIT). Both carry their origin and version in
their own file headers and are compiled in unedited.

</small>
