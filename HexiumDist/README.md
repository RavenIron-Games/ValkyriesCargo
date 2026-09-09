# Valkyrie's Cargo

**A Valheim mod by [Raven Iron](https://ravenirongames.com/).**

**Raven Iron is NomadicWar & Wu'barrk** — the two of us, founders, and the pair who designed and built this.

> *You bank the fire, set down your axe, and the light goes strange. Wings beat in the upper skies.*
> *A Valkyrie comes down out of the cloud with a red-bearded dwarf hanging from her talons — and she*
> *sets him on his feet beside your hearth like a parcel she was paid to deliver.*
>
> *His name is Ingvar the Far-Travelled. He has five minutes, a purse, and a cart's worth of the*
> *realm's goods. Then he goes back into the mist the way Odin does.*

---

## The visit

Nobody summons Ingvar. He arrives when you have earned a quiet moment — **rested, and comfortable
at your own hearth** — and the server decides the sky is right.

**A Valkyrie flies him in.** Not a spawn, not a puff of smoke: a real approach, planned by the
server so every player on the map watches the same bird take the same line. She comes in low
enough to see what she is carrying, releases him beside your fire, and turns for the horizon.

**He walks up and calls out.** Ingvar crosses the ground to you himself, greets you in his own
words, and waits. He is unkillable, unhostile and entirely uninterested in your Greydwarf problem.

**He trades.** Press **`E`** for the Cargo Terminal — Raven Iron's own window, not a reskin of the
vanilla store.

**Then he leaves.** After five minutes — or when you send him off early — he gives you a parting
word and vanishes in a burst of mist, the same way the Allfather does it.

---

## A market that remembers

This is the part that is not decoration. Ingvar's stock is a **live, persistent, server-owned
economy**, and it is the same economy on every visit and every player's screen.

| | |
|---|---|
| **Supply and demand** | Buy him out and the price climbs. Flood him with iron and he pays less for the next load. Every price you see was computed from stock that other players moved. |
| **Drift** | Prices ease back toward their resting value over in-game days, so a market you wrecked on Tuesday is worth visiting again by Friday. |
| **A purse, not a money printer** | He arrives with **1500 coins** and no more. When the purse is dry, he is done buying — bring goods to barter or come back next visit. |
| **The Fair Market Act** | He will never pay you more for a thing than he charges for it. No buy-low-sell-high loop against your own merchant. |
| **A rotating shelf** | He carries **20 of a 72-entry catalogue**, re-rolled every couple of in-game days. What he has this week is not what he had last week. |
| **It survives a restart** | Stock, purse and prices are written to a sidecar beside your world save. A visit interrupted by a server restart picks up where it stopped. |

Barter is first-class: pay for what you want with what you are carrying, and the terminal will
cover the balance from your goods at his live rates.

---

## Built for a server, not bolted onto one

- **Server-authoritative.** The market, the schedule, the purse and every settled deal live on the
  server. A client proposes; the server decides, at the prices it holds, and only then does an
  inventory change.
- **ServerSync.** Configuration is pushed and locked from the host, so every player is playing the
  same economy whether they edited their config or not.
- **Everyone sees one visit.** The same descent, the same merchant at the same hearth, the same
  shelf, the same prices moving as other people trade.
- **Nothing is lost to a disconnect.** A deal that was settled but not delivered is redelivered
  when you come back.

---

## Ingvar has his own body

Ingvar is not a re-textured Dverger. He is an original character — modelled, rigged and animated
for this mod — with his own idle, walk, greeting, talk, shrug and nod, and he is carried inside the
plugin itself, so there is nothing extra to install.

**Wearing Smoothbrain's Backpacks?** Ingvar notices. If the server runs
[Backpacks](https://thunderstore.io/c/valheim/p/Smoothbrain/Backpacks/), he wears the real pack on
his back and shows up with **twice the shelf** — a bigger caravan for a world that hauls more.
Entirely optional; without it he simply travels light.

---

## Installing

**With a mod manager (recommended)** — install through Hexium, Gale, r2modman or any
Thunderstore-compatible manager. **BepInExPack Valheim** is the only dependency.

**By hand** — copy `plugins/ValkyriesCargo.dll` into `BepInEx/plugins/`.

**On a dedicated server** — install it on the server *and* on every client. The server refuses a
client that does not have it, by design, and tells them so by name and version.

---

## Console

Typed into the in-game console. Admin verbs are checked **on the server**, so a client can run them
and a non-admin cannot.

| Command | What it does |
|---|---|
| `cargo status` | Everything at a glance: the visit, the market, the schedule, the engine check |
| `cargo stock [item]` | His shelf and its current prices |
| `cargo catalogue list` | The full catalogue |
| `cargo visit [player]` | *(admin)* Send a visit now |
| `cargo dismiss` | *(admin)* End the visit now |
| `cargo engine` | What Valheim build this was written against versus the one you are running |

`cargo status` names its own sources — it is the first thing to ask for in a bug report.

---

## Configuration

Everything below is a `[Server]` value: set on the host, synced and locked to clients.

| | Default |
|---|---|
| Minimum comfort to qualify | `4` |
| Visit length | `300 s` |
| Ingvar's purse | `1500 coins` |
| Shelf size / rotation | `20 of 72`, every `2` game days |
| Shelf multiplier with Backpacks | `×2` |
| Valkyrie speed / turn rate | `8 m/s`, `45 °/s` |

`[Client]` values are yours alone — the terminal's backdrop, and how Ingvar is placed and turned.

---

## Compatibility

Built against **Valheim 0.221.12**. The mod checks the running game at boot, prints what it found,
and any feature that depends on something that moved switches *itself* off and says so, rather than
taking the session down with it.

Plays well with the rest of the family — Cairn, Undertow, FireFront, Ragnarok's Wrath, RavenEye and
Yggdrasil's Reckoning — and has been run alongside a 117-plugin modpack.

---

## Status — 0.1.0

**A first playable, and honest about it.** The loop runs end to end on a dedicated server with real
players: the roll, the flight, the drop, the walk-up, the terminal, deals over the wire, the price
curve, the persistence and the departure have all been watched on a screen. Some corners have been
proven only in the log, and a few only off-game. If something reads wrong, `cargo status` and your
`LogOutput.log` will usually say why — and we would like to hear about it.

---

## Credits

**Raven Iron is NomadicWar & Wu'barrk.** Both founders, both designers of this mod, and between
them everything in it.

- **NomadicWar** — co-founder, Raven Iron. Design; the market and the economy, the visit director,
  persistence and the server side.
- **Wu'barrk** — co-founder, Raven Iron. Design; Ingvar himself (model, rig and animation), the
  Cargo Terminal, and the VikingOS interface work the terminal is built on.

**ServerSync** is blaxxun's `ConfigSync.cs` (MIT-0), compiled in as shared source and never edited.
The terminal's frame and focus handling are Wu'barrk's VikingOS shared source (MIT).

Ingvar's model, animations and textures are original work by Raven Iron. Nothing in this package is
ripped, re-uploaded or derived from another creator's assets.

**[ravenirongames.com](https://ravenirongames.com/)**
