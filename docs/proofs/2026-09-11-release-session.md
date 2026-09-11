# The release session, 2026-09-11 — what is left, in order

**The bar (`docs/RELEASE.md` §4):** every item in CLAUDE.md's "What to verify in-game" done on a screen,
with the exact line pasted, a date and the server. Not a summary: the line.

**The server.** Storm10, `C:\Users\donfr\ValheimServers\Storm10`, port 2477, world Storm10, password
`stormhold`, crossplay. Valheim **1.0.12**, our DLL and ServerDevcommands 1.112 only. Join code on the
boot; ask for it, it changes on some boots. Don's client runs the Gale **testing** profile (our DLL,
ServerDevcommands, Configuration Manager).

**The build.** `main` at the `Server.CarryOffset` merge: 0 warnings, 1987 off-game checks, boot line
`built against Valheim 1.0.12 (network 40, player 46, world 41; Steam build 25253764 client / 25253791
server; bodies read 2026-09-11); running same build 1.0.12 … probes 19/19 ok, 8 not probeable`.

---

## Already closed today, from this side, no client needed

- **Item 1, and item 24's boot half, RE-PROVED ON 1.0.12** (Storm10, 09:15). The line above, plus
  `Valkyrie's Cargo v0.1.0 loaded - renderer=False, patches 18/18 applied, catalogue=72 entries, engine:
  same build 1.0.12 (net 40, player 46, world 41); probes 19/19 ok, 8 not probeable, ServerSync version
  gate armed`. No `FAILED`, no `registry:` line.
- **Item 23's switch half, PROVEN 09:29–09:33.** `Server.BarrkBotExport = false`, restart, 90 s of a 60 s
  cadence: all seven files' `generated_at` unmoved at `2026-09-11T16:28:54.690Z` and **all seven still on
  disk, none deleted**. Set back to `true`, restart: `2026-09-11T16:33:12.406Z`, moving again. The switch
  stops the export and leaves what is there, exactly as the item is written.

Still open on item 23: **BarrkBOT itself reading the files.** That is Wu'barrk's Discord bot pointed at this
server's `BepInEx/config`, and nothing on this machine can stand in for it.

---

## The trick that unblocks two items without a second account

Items **4 (the local-edit half)** and **11b (a non-admin's `cargo visit`)** both need a client that is NOT
an admin. ServerSync exempts an admin from the config lock, and `AdminGate` lets an admin through, so
neither refuses Don while he is on the list.

`SyncedList` **reloads on file change, with no restart** (CLAUDE.md engine facts, seen on 1.0.7). So the
session can make Don a non-admin for two minutes: take the four lines out of
`Storm10\saves\adminlist.txt`, prove both items, put them back. Say the word and it happens from this side
while you stay connected. If the lock does not flip live, a reconnect settles it.

---

## The order

Each row: who types it, what to type, and the line that proves it. A line that does not appear is the
finding, not a reason to move on.

### Before joining

**17 — `cargo terminal demo`, from the main menu.** The log half was done 2026-09-07; what is missing is
the owner describing the window. Open it, then say whether the gilt window reads, the cursor frees, the
tray adds up and Escape closes it. `terminal opened: visit #1 (demo)` / `terminal closed: escape`.

### On joining (no visit needed)

**2 — the client boot line.** The same line as the server's with `renderer=True`. Read it on screen;
a client's console does not write to the log.

**6 — `cargo status` numbers.** The simulation-distance line (1.0 replaced `m_activeArea`), `catalogue: 72
entries (18 wares, 54 wants), 0 problem(s)`, `N events registered, ours=yes`, `config: following the
server`, `my report: rested=…, comfort=…, written N s ago`, and the new `carry:` line naming the offset.

**5 — the prefab dumps.** `cargo prefab Valkyrie`, `cargo prefab Dverger`, `cargo prefab odin`,
`cargo prefab Haldor`. Paste all four. These settle the animator parameter names, which a headless dump
can never give (a dedicated build strips controllers). The Valkyrie dump now also prints `attachOffset`.

### With a visit (`cargo visit`)

**22a — the flight line.** `cargo status` during the flight:
`flight: visit #<N>, bird <id> (flying), merchant <id>` — and `(dropped)` after the drop.

**22c — dismiss mid-flight.** `cargo visit`, then `cargo dismiss` BEFORE the bird reaches the drop.
Server: `visit #<N> ended: admin <name>` and `merchant and bird reclaimed and their destroy queued (it
lands on the next ZDOMan.Update)`. No orphan afterwards.

**22b — walking out of the block.** Start a visit and walk more than about 96 m from where you stood, so
the bird leaves your active area. Within 5 s the server says:
`the bird's ZDO is gone and the merchant was never dropped; the visit continues on the ground`
(`Server/Spawner.cs:265`). Server-side only.

**25 — ghost mode.** With a visit on the ground, get a Greydwarf pack or a raid near Ingvar. **This patch
writes no line of its own** — it silently answers "not enemies". So the proof is the screen: nothing
targets him, no health bar over him, he never swings, and you are fought exactly as before. Then
`cargo status` must list `Patch_BaseAI_IsEnemy` among the applied patches. Note it is live only while a
visit is running.

**16 — the two unseen refusals.** `purse_empty` is already proven.
- `sold_out`: `cargo catalogue add Iron:60:10:10:Ware` as admin (a ware whose max is its target), then buy
  it out. Client: `terminal deal on visit #<N>: sold_out`. Server: `deal refused for <name>: sold_out`.
  Watch for the tray refusing to stage first — the client validates too, and then the server never sees it.
- `stale_visit`: send a deal against a visit that has ended.

**14 — the ledger and redelivery.** Confirm one deal, and the instant the answer lands, **hard-quit**
(Alt+F4) before the ack goes back. Rejoin. Server: `VCargo_claim from <name>: redelivered 1 owed deal(s)`
(`Net/DealWire.cs:157`). The window is small; it may take two tries.

### Needing the adminlist trick

**4 — the local edit.** While connected, edit a `Server.*` value in the client's own
`com.raveniron.valkyriescargo.cfg`. `cargo status` must still show the server's value and
`config: following the server` (`Patches/Patch_Terminal.cs:295`).

**11b — the non-admin refusal.** `cargo visit` answers `not an admin (the server's adminlist.txt decides)`
and the server logs `refused VCargo_admin visit from <name>`.

### Last, because it swaps the client DLL

**3 — the version wall.** Bump the csproj version, build, install on the CLIENT only, join. Vanilla's
connection dialog shows ServerSync's own text (`Libs/ServerSync.cs:1228-1231`):
`Valkyrie's Cargo may not be higher than version <server>. You have version <client>.`
and the server logs `Disconnect: The client (<platform id>) doesn't have the correct Valkyrie's Cargo
version <version>` (line 1236). Then put the right DLL back.

**22d — the intro Valkyrie.** A new character's intro flight must behave exactly as vanilla, with no line
of ours. The patch only skips vanilla `Awake` for a bird carrying `VCargo_cargo`.

---

## What cannot close today

- **Item 23's BarrkBOT reading** — needs Wu'barrk's bot pointed at this server.
- **#66, the walk-off inside Trading** — needs two clients on one visit, so it needs Wu'barrk.
- **Item 13/18's "on every machine" halves** — the same.

Everything else on the list is reachable from one client and this server.
