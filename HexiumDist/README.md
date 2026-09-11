# Valkyrie's Cargo

**A Valheim mod by [Raven Iron](https://ravenirongames.com/).**

> *A Valkyrie descends from the stormy heavens, air-dropping Ingvar the Far-Travelled—a stout, red-bearded dwarven merchant clad in plate mail—beside your hearth. Trade from a live stock of wares at dynamic supply-and-demand prices for five minutes before he vanishes like Odin into the mist.*

**Requires Valheim 1.0.12.** Valheim 1.0.12 moved the network version, so a build made for 1.0.7 or 0.221.12 cannot connect at all. Install this build on the server **and on every client**, all on the same version: the mod refuses a client whose copy does not match.

---

## What is Valkyrie's Cargo?

**Valkyrie's Cargo** introduces an atmospheric wandering merchant encounter to Valheim, powered by a fully server-authoritative, persistent world economy.

- **The Visit**: When you are rested with comfort of 4 or more within a sheltered base, the server periodically rolls for a visit. A winged Valkyrie swoops in through the clouds carrying Ingvar the Far-Travelled in her grip, dropping him directly beside your hearth fire.
- **Ingvar the Far-Travelled**: A short, fiery red-haired, red-bearded dwarven merchant clad in heavy plate mail, carrying a massive ironbound cargo chest.
- **Dynamic Trade Terminal**: Press `E` to open the custom Cargo Terminal. Ingvar carries a live stock of 72 catalogue items (18 wares he sells and buys back, 54 raw goods he only purchases).
- **Supply & Demand Economy**: Buy out his stock and prices rise; flood him with resources and he pays less. Prices naturally drift back toward equilibrium over in-game days.
- **Barter & Purse Protection**: Ingvar arrives with a purse of 1,500 coins and accepts barter at his live buy rates—preventing players from dumping whole warehouses on him in one visit.
- **The Departure**: After five minutes (or pressing `Shift+E` twice to dismiss him early), Ingvar gives a respectful farewell and vanishes in an ethereal burst of mist and ravens, true to Odin's legacy.

---

## ServerSync & Multiplayer

Built from the ground up for dedicated servers and multiplayer worlds:
- **Server-Authoritative**: The market state, persistent world stock, roll schedule, and purse are owned strictly by the server. No client can inject or forge trade transactions.
- **ServerSync Powered**: Configuration is automatically synced and locked from the dedicated server to connecting clients.
- **Shared Experience**: Every nearby player witnesses the same Valkyrie descent, visits Ingvar at the same base hearth, and interacts with the shared persistent world economy.

---

## Console Commands (Admin)

Admins can manage and test visits directly from the in-game console:
- `cargo visit [player]` — Triggers an immediate Valkyrie delivery visit for the specified player (or the local player).
- `cargo dismiss` — Immediately ends an active merchant visit.
- `cargo status` — Displays the current market state, active visit timer, and director details.

---

## Installation

**The server and every client need this mod, on the same version.** A client whose copy does not match is refused at the handshake with a message naming the mod and both versions.

### With a Mod Manager (Recommended)
1. Install via **Hexium** or your preferred Thunderstore-compatible mod manager.
2. Ensure **BepInExPack Valheim** is installed.

### Manual Installation
1. Extract the package zip.
2. Copy `plugins/ValkyriesCargo.dll` into your `BepInEx/plugins/` directory.
3. Do the same on the dedicated server, and on every client that will connect to it.

---

## Links & Community

- **Official Website**: [https://ravenirongames.com/](https://ravenirongames.com/)
- **Developer**: Raven Iron
