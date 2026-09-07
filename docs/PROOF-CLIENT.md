# Valkyrie's Cargo — the client proof runbook

Everything in this mod that a renderer has to draw is **unproven**. The server side is proven headless
(CLAUDE.md "Status"); item 1 is done; items 2 to 18 have never run, and 19 and 20 need a bundle that does
not exist yet. This file is the whole list in one sitting: what to type, where, the exact line to look
for, and what the common failure means.

---

## A. What this proves, and the one rule

Each entry below closes one line of CLAUDE.md **"What to verify in-game"**. The numbering is CLAUDE.md's,
not the running order; section D opens with the running order.

**The rule (WORKSPLIT §4, CLAUDE.md "Working agreement"):**

> Verified facts go into `CLAUDE.md` "Status" with the date and the **exact log line**, by whoever saw them.

So: copy the line out of the console or the log, do not retype it, and paste it with the date and which
server it came from. A paraphrase is not a proof. If a line comes back different from what this file
predicts, **the line wins** — paste what you saw and say the runbook was wrong.

Every predicted string below is quoted from the code that writes it; where a value varies it is written
`<like this>`. Two lines are quoted from CLAUDE.md's own wording instead, and say so.

---

## B. Prerequisites

1. **Build.**
   ```powershell
   dotnet build ValkyriesCargo\ValkyriesCargo.csproj -c Release
   ```

2. **Deploy the same build to both sides.** One command:
   ```powershell
   .\tools\deploy-test.ps1 -Build -Client                # server StormTest + the Steam client
   .\tools\deploy-test.ps1 -Build -GaleProfile raveniron  # ...or that Gale profile instead
   ```
   It refuses while `valheim_server.exe` or `valheim.exe` is running (Valheim holds the DLL open), backs
   the old file up as `ValkyriesCargo.dll.prev`, and prints every destination's size and **assembly
   version**. `-WhatIf` shows the plan and writes nothing. It never starts anything.

   Where things are:
   - server: `C:\Users\donfr\ValheimServers\StormTest\BepInEx\plugins\RavenIronStudios-ValkyriesCargo\`
   - Steam client: `C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\plugins\...` (its
     `plugins` folder is **empty** as of 2026-09-06: our DLL will be the only mod there)
   - Gale: `%APPDATA%\com.kesomannen.gale\valheim\profiles\<profile>\BepInEx\plugins\...`
     (profiles present: `bluehills`, `Default`, `raveniron`, `ravenironserver`, `Ravenrest`,
     `ravenrestprofile`, `Wonderland`, `Wonderland ADMINing`)
   - `Valheim - Clean` beside the Steam install is **never** written to; the script refuses that path.

3. **The version gate.** ServerSync is `ModRequired` with `MinimumRequiredVersion == CurrentVersion`
   (`Config/ModConfig.cs`), so the server and the client must carry the **same build**. The two "after"
   lines `deploy-test.ps1` prints must show the same assembly version. On a successful join both logs
   carry ServerSync's own handshake lines (`Libs/ServerSync.cs`, through Unity's log):
   ```
   Sending Valkyrie's Cargo version <v> and minimum version <v> to the server.
   Received Valkyrie's Cargo version <v> and minimum version <v> from the server.
   ```

4. **Become admin.** `cargo visit`, `dismiss`, `reset` and `save` are gated by the SERVER's own list
   (`Server/AdminGate.cs`, vanilla `ZNet.IsAdmin`, fail closed). StormTest's list is
   `C:\Users\donfr\ValheimServers\StormTest\saves\adminlist.txt` and already holds **two Steam ids**
   (the owner's). Add an id on its own line and restart the server if a new account needs it.
   `cargo status` prints `admin here=<True|False>` for the machine you type on.

5. **The server.** Start it yourself; no script here starts anything.
   ```
   C:\Users\donfr\ValheimServers\StormTest\start_stormtest.bat
   ```
   whose launch line is
   ```
   valheim_server -logfile "%~dp0stormtest-server.log" -nographics -batchmode -name "StormTest" -port 2476 -world Dedicated -password "stormhold" -public 0 -savedir "%~dp0saves"
   ```
   Join from the client with **`127.0.0.1:2476`**, password **`stormhold`**. Stop it with **CTRL-BREAK**
   in its window (CTRL-C is ignored).

6. **The client console.** F5 opens it. `cargo` is registered as a plain console command (not a cheat),
   so `devcommands` is not needed. If F5 does nothing, add `-console` to the Steam launch options.

7. **A dedicated server has no console.** Everything you TYPE goes into the client's console; admin verbs
   ride `VCargo_admin` to the server and its answer prints back in your console (and into the client's log as
   `server answered: <the same text>`). Everything the SERVER says is read from its log (section E). The one thing a dedicated server cannot show is its own `cargo status`
   director block (`candidates:`, `owed ledger`, `sidecar`, `wire`); where an item wants that, this file
   says which log line carries the same fact, and a **listen host** (host a world from the client:
   `role: listen host (server + client)`) shows both halves at once.

8. **The second machine** (items 3b, 11b and the "on every machine" halves of 13 and 18): Wu'barrk on
   another box, or a second Steam account on this one. The second account must **not** be in
   `adminlist.txt` — that is what makes items 4 and 11b provable.

---

## C. The fast-test server config

A visit is rolled once per `EventCheckIntervalMinutes` and only if the roll beats `EventChancePercent`,
so at defaults you would wait 25 minutes for a 25% chance. `Server.*` is synced and **locked**, so the
server's own file is the only place to set it:
`C:\Users\donfr\ValheimServers\StormTest\BepInEx\config\com.raveniron.valkyriescargo.cfg`.

```powershell
.\tools\set-test-config.ps1 -Show      # what it says now
.\tools\set-test-config.ps1            # apply (backs up to .cfg.bak first)
.\tools\set-test-config.ps1 -Relax     # ...and drop the eligibility gates too
.\tools\set-test-config.ps1 -Restore   # put the .cfg.bak back
```
It refuses while the server is running (BepInEx rewrites its config on exit and would lose the edit), and
it only touches keys already in the file — if BepInEx has not written this build's config yet it says so
and stops. Restart the server for a change to take effect; `cargo status` prints the values back.

| Key | Default | Fast test | Which gate it relaxes | What the roll says when the gate holds |
|---|---|---|---|---|
| `EventCheckIntervalMinutes` | 25 | **1** | `Scheduler.Tick` only rolls once an interval | (nothing: a roll that has not happened prints nothing) |
| `EventChancePercent` | 25 | **100** | the chance itself | `roll: rolled <0.xx> >= <0.25>: no visit (<n> eligible, <n> ticket(s))` |
| `DaytimeOnly` | true | **false** | the night hold | `roll: held: night, and DaytimeOnly is on` |
| `RequireRested` | true | *kept* (`-Relax`: false) | the Rested effect | `... not eligible: not rested` |
| `MinComfortLevel` | 4 | *kept* (`-Relax`: 0) | comfort from the Rested tooltip | `... not eligible: comfort < 4` |
| `MinBaseValue` | 1 | *kept* (`-Relax`: 0) | vanilla base value (workbench cover) | `... not eligible: baseValue < 1` |

Those three eligibility strings come from `Core/Scheduler.cs` `BucketName`, and appear inside two lines:

- `roll: no eligible player: <n> not rested, <n> comfort < 4, ...` (the summary, per bucket), or
  `roll: no eligible player: nobody online` when the server is empty
- `cargo visit <name>: forced: <name> not eligible: <bucket>; online: <summary>`

**Keep `RequireRested`, `MinComfortLevel` and `MinBaseValue` at their real values** unless there is no
base to stand in: they are what make items 7 and 12 mean anything. The other gates are `Enabled`
(`roll: held: Server.Enabled is false`) and a foreign event
(`roll: held: a random event is active (a raid, a storm, or a visit)`).

**As of 2026-09-06 StormTest's file already carries** `EventCheckIntervalMinutes = 1` and
`DaytimeOnly = false` from the headless run; everything else is at the default, so the only change an
apply makes is `EventChancePercent : 25 -> 100`.

Two more things `roll:` will not repeat: the reason is logged **only when it changes**
(`VisitDirector.LogDecision`), so a quiet log after one `roll:` line is the loop holding, not the loop
dead; and `cargo visit` (`Scheduler.Force`) skips the interval, the chance and the cooldowns but keeps
every other gate, so most items below do not wait for a roll at all.

---

## D. The checklist

**Running order** — one server boot, one client session, two-machine work at the end:

| Order | Items | Where |
|---|---|---|
| 1 | **17** | client, main menu, no server |
| 2 | **2, 5, 6, 4, 7, 12** | client joined to StormTest |
| 3 | visit A: **8, 9, 10** | one forced visit, let the timer end it |
| 4 | visit B: **13, 16, 14, 11a** | one forced visit, `cargo dismiss` ends it |
| 5 | visit C: **18** | one forced visit, "Send him off" ends it |
| 6 | visit D: **15** | one forced visit, stop and restart the server mid-visit |
| 7 | **3** | client-only reinstall of a different version, then put it back |
| 8 | **11b, 13b, 18b** | second machine or second (non-admin) account |
| 9 | **19, 20** | when the bundle exists |

Read the log after each block: `.\tools\tail-log.ps1 -Last 20` (server) and `-Client -Last 20`.

---

### 17 — `cargo terminal demo` (do this first, from the main menu)

**Where:** client, main menu, no server, no world.
**Type:** `cargo terminal demo`

**Console** (`Patches/Patch_Terminal.cs`, `TerminalCommand`):
```
cargo: terminal opened on the demo market (visit #1, purse 800); Escape closes it
```
(visit 1 and purse 800 are fixed: `VisitSnapshot.Demo`, `MarketSnapshot.Demo`.)

**Client log** (`Client/Terminal/CargoTerminal.cs`, `OpenInternal` then `Close`):
```
terminal opened: visit #1 (demo)
terminal closed: escape
```

**On the screen**, from CLAUDE.md item 17's own wording: the gilt window opens centred with the cursor
free; **18 wares** on the left with icons and prices, **72 rows** on the right; clicking a ware stages it
(Shift 5, Ctrl 20, right-click takes back); "you pay" is count x price; Confirm deal answers with one of
his three buy lines and the price on that row moves; a second Confirm on the same line comes back
"The wind shifted..." with the line amber, and **Confirm new price** goes through; Escape closes it and
the cursor locks again.

Details the code fixes: the header reads `Purse 800c` and `Coins 840` (the demo player's purse,
`DemoTransport.PlayerCoins`); the title is `Ingvar the Far-Travelled  -  Valkyrie's Cargo`; the footer
opens with `Ah, the sweet smoke of a well-earned hearth. What do you bring, and what do you need?` and,
after an accepted buy, one of `Sold, and may it serve you.` / `A fair price, for today.` /
`Scarce goods, dear goods.` (`Core/Lines.cs`). The amber line's words are
`The wind shifted while you counted. Say yes again and it's done.`

**Paste back:** the two console lines, the two log lines, and **a screenshot** — this is the item where
the picture is the proof.

**Common failures**
- `cargo: no terminal on this machine (no renderer)` — the terminal is only installed when
  `HasRenderer` is true (`ValkyriesCargo.Awake`). You are on a headless process.
- The window opens but one price never moves on a second Confirm: one unit of a cheap ware may not move
  an integer price. Stage **Ctrl (20)** of something with a small target stock (`BlackCore`, target 2)
  and it will.
- The cursor stays locked, or does not come back on Escape: that is `Libs/SharedUI/UIFocus.cs`, and it is
  a finding — paste it.

---

### 2 — Boot line, client

**Where:** client. **Type:** `cargo status` (in a world or at the main menu).

**Client log**, the line `ValkyriesCargo.Awake` writes:
```
Valkyrie's Cargo v<version> loaded - renderer=True, patches <n>/<n> applied, catalogue=72 entries, ServerSync version gate armed; role is decided when a world loads.
```
Since issue #31 the number is patch CLASSES applied over patch classes found, not patched methods (the
older records' `patches=13` counted methods: ServerSync 9 + `Terminal.InitTerminal` + UIFocus 2 +
`RandEventSystem.Awake`). Both halves equal is the pass. **`patches 15/16 applied; FAILED: <name>; running
degraded` is the mod telling you a patch did not take — a fact, not an error; write the name down** and
`cargo status` prints the reason under it. `REFUSED to run` means ServerSync's version gate or config lock
did not apply, and nothing else was started. If the catalogue had a bad entry the line gains
`(<n> problem(s), see `cargo status`)`.

**Console:** `cargo status` answers at all, first line
`Valkyrie's Cargo v<version> - role=client, renderer=yes`.

**Paste back:** the loaded line and the first line of `cargo status`.

**Common failures**
- No line at all: the DLL is not in that plugins folder, or you read the wrong boot (section E).
- `renderer=False` on a client: you are reading the server's log.

---

### 5 — The prefab dumps

**Where:** client, **in a world** (`ZNetScene` must exist).
**Type:** `cargo prefab Valkyrie` · `cargo prefab Dverger` · `cargo prefab odin` · `cargo prefab Haldor`

Each dump starts
```
prefab '<name>' (ZNetScene): <n> child(ren)
  components: <Type Type Type ...>
```
then children, `ZNetView: yes, persistent=<b>, distant=<b>`, and per prefab:
`Odin.m_despawn[<i>]: <prefab> enabled=<b> networked=<yes -> owner creates it|no -> every client creates it>`;
`Valkyrie: attachPoint=<name|NULL>, speed=..., dropHeight=..., startDistance=..., startAltitude=...`;
`Character: name=..., faction=..., collider=<CapsuleCollider|none on root>, skinned renderers=<n>, MonsterAI=<b>, NpcTalk=<b>, Tameable=<b>, ZSyncAnimation=<b>` and `animator '<controller>' parameters: <names>`;
`Trader: name=..., items=<n>, standRange=...`.

**Paste back:** all four dumps, whole, into CLAUDE.md under this item. They decide the effect-rule
branches (design 3.6), whether `Valkyrie` is registered with `ZNetScene` at all (design 3.2's fallback),
and whether `m_attachPoint` exists.

**Common failure:** `cargo: no prefab named '<name>' in ZNetScene or ObjectDB.` — for `Valkyrie` that is
itself the answer design 3.2 asks for; write it down and say so.

---

### 6 — `cargo status` numbers

**Where:** client, in a world on StormTest. **Type:** `cargo status`

```
  ZoneSystem.m_activeArea=<n> -> objects exist within <n-1> zone(s) (<(n-1)*64> m) of a player's zone; distant area=<n>; water level=<x>
  random event: <none|name (t of d s)>; <n> events registered, ours=yes ('valkyries_cargo')
  time: world <n> s, <day|night>, day length <n> s (EnvMan.m_dayLengthSec, public; the drift half-life counts these; compiled default 1200, scene expected 1800)
```

**Paste back:** those three lines. `m_activeArea` decides the flight's start-point clamp (design 3.2);
`day length` should read **1800** (already read live on StormTest, headless).

**Common failure:** `ours=NO` — the `RandEventSystem.Awake` prefix did not run on this machine, and no
client would ever see the banner. That is a blocker, not a note.

---

### 4 — Locked config

**Where:** client, connected to StormTest.
**Do:** edit `MinComfortLevel` in the CLIENT's own
`BepInEx\config\com.raveniron.valkyriescargo.cfg` (or through a config manager) while connected, then
`cargo status`.

```
  config: following the server, locked=True, admin here=<b>, Enabled=True, roll every 1 min at 100%, rested=True, comfort>=4, baseValue>=1, daytimeOnly=False, lifespan 300 s
```
`following the server` (not `this side is the source of truth`) and the SERVER's `comfort>=4` are the
proof.

**Read this before you conclude anything:** ServerSync exempts admins — "Client writes are rejected while
locked **unless the client is on `adminlist.txt`**" (CLAUDE.md, Engine facts). The owner's ids are on
StormTest's list, so on the owner's account `admin here=True` and the local edit **is** accepted and
pushed. Prove the refusal from the second, non-admin account (item 11b's account) and paste both halves:
`admin here=True` keeping the edit, `admin here=False` losing it.

---

### 7 — The report

**Where:** client, in a world on StormTest, standing at a base.
**Type:** `cargo status`

```
  my report: rested=<yes|no>, comfort=<n>, written <x> s ago (<n> writes to my character ZDO as VCargo_rested/VCargo_comfort)
```
(`ComfortReporter` writes every 2 s; `Reported` false gives
`my report: nothing written yet (no local player, or not its owner)`.)

The server's half of the same fact is the roll summary in the server log, which counts what it read from
your character ZDO:
```
roll: no eligible player: 1 not rested
```
Stand at your base until you are rested with comfort 4, and the next roll (a minute later, at the
fast-test chance of 100%) turns into `visit #<n> begins:` instead — which is item 8 arriving on its own,
without `cargo visit`. Either line proves the server read your report; the change from one to the other
proves it read it TWICE and saw it change.

**Paste back:** the client's `my report:` line and the server's `roll:` line from the same minute, so the
two numbers can be compared. On a listen host `cargo status` prints both at once, and the server's side is
the `candidates (<n>):` block: `<name> (uid <n>): rested=<yes|no> comfort=<n> base=<n> y=<n>`.

**Common failure:** `my report: nothing written yet` while a player is clearly standing there — the
`ZNetView` is not owned yet; wait 2 s and ask again.

---

### 12 — The gates

**Where:** client, connected, **before** you are rested (step outside, or do this the moment you spawn).
**Type:** `cargo visit`

**Console** (the local echo, then the server's answer):
```
cargo: asked the server to visit; its answer prints here
cargo visit <name>: forced: <name> not eligible: not rested; online: 1 not rested
```
The bucket name is one of `not rested`, `comfort < <n>`, `baseValue < <n>`, `in a dungeon`,
`on cooldown`, `near a base on cooldown`, `dead`, `not ready` (`Core/Scheduler.cs`, `BucketName`).

**Server log:**
```
admin <name> (<uid>): cargo visit 
roll: forced: <name> not eligible: not rested; online: 1 not rested
```

**Paste back:** the console answer and the server's `roll: forced:` line.

**Common failures**
- `cargo: not an admin (the server's adminlist.txt decides)` — your id is not on the list (prereq 4).
- `cargo: routed RPC not registered yet; try again in a moment` — `ZRoutedRpc` is re-created per world
  join; wait a second.
- `cargo: this console has no player; use `cargo visit <name>`` — no local player yet.

---

### 8 — `cargo visit` (visit A)

**Where:** client, rested, comfort >= 4, at a base. **Type:** `cargo visit`

**Console:**
```
cargo: asked the server to visit; its answer prints here
cargo visit <name>: forced visit: <name> at (<x>, <z>); 1 eligible, 1 ticket(s)
```

**Server log** (`VisitDirector.Begin`):
```
visit #<n> begins: pilot <name> (uid <uid>) at (<x>, <z>), 300 s, purse <n>, seed <n>
```

**On the screen:** the pilot's private line, centre, once
(`Wings beat in the upper skies... an emissary from Asgard descends.`, `Core/Lines.cs`, shown only to the
chosen player and only while `Client.ShowArrivalMessage` is true), and — **once you are within 96 m of
the point the visit started at** — the vanilla event banner `Valkyrie's Cargo has landed`
(`Server/CargoEvent.cs` sets it as `m_startMessage`).

**Then:** `cargo status` on the client shows
`channels: VisitState=<n> chars, MarketState=<n> chars -> parsed: visit Flying #<n>, market 72 rows, purse <n>`.
The `visit: #1 Flying, pilot <name>, 04:5x left` form CLAUDE.md quotes is the SERVER's status line and
needs a listen host; on a dedicated server the countdown is the terminal's header (item 18) instead.

**Paste back:** the console answer, the server's `visit #<n> begins:` line, and a screenshot of the banner.

**Common failures**
- `a visit is already running (#<n>, <mm:ss> left)` — dismiss it first.
- `held: a random event is active (a raid, a storm, or a visit)` — another mod's event is running.
- `visit #<n>: the event did not start; is 'valkyries_cargo' registered? (`cargo status` says)` — item 6
  failed on the server; nothing else will work.
- No banner: you walked more than 96 m from where the visit began before it drew, or the event is not
  registered on the CLIENT (item 6 again, client side).

---

### 9 — The clock (during visit A)

**Where:** client. **Do:** note the countdown in the terminal (`cargo terminal open`, header
`<mm:ss> left`), close it, walk **more than 96 m** away for a full minute, come back, reopen.

The event's `m_time` only advances while a player is within `m_eventRange` (96 m) of the drop, so the
visit outlives 300 s of wall clock by roughly the time you were away, and the server republishes the
clock whenever it drifts a second from what it published (design 3.7). The republish count is carried by
the end line (item 10): `<n> clock republish(es)`, and it must be **more than 0**.

Second half: sleep through a night mid-visit; the countdown must not jump.

**Paste back:** the two countdown readings with the wall-clock times, and item 10's end line showing the
republish count.

**Common failure:** the countdown runs down while you are away — the pause did not happen; note the
distance you actually reached (96 m is not far).

---

### 10 — The end (visit A)

**Do:** nothing. Let it run out.

**Server log** (`VisitDirector.End`):
```
visit #<n> ended: timer; takings 0 coins, purse <n>, <n> clock republish(es), 0 owed deliveries
```
One minute before, the same log carries `visit #<n>: one minute left`.

**On the screen:** the banner `Ingvar has gone back to the mist` (the event's `m_endMessage`), for anyone
inside 96 m when it ended.

**Client:** `cargo status` shows `parsed: visit None #<n>`. The
`visit: none; last #1 ended: timer` form is again the SERVER's line (listen host).

**Paste back:** the `ended: timer` line and the one-minute line.

**Common failure:** `ended: displaced by event '<name>'` — another mod started an event on top of ours;
that is a real finding about coexistence, paste it.

---

### 13 — A deal (visit B)

**Do:** `cargo visit` again, then:

```
cargo stock Iron
cargo deal buy Iron 2
cargo stock Iron
cargo deal sell Wood 10
```

**`cargo stock Iron`** (`Patch_Terminal.cs`, `Stock`):
```
market for visit #<n>, purse <n> coins; 72 rows
  Iron (Ware): 20/20 max 60, you pay <n>, he pays <n>, trend <up|down|flat>
```
(bare `cargo stock` adds `(first 24; name one for its line)`.)

**`cargo deal buy Iron 2`**:
```
cargo: sending buy 2 Iron at <n> each (nonce <n>)
cargo: DONE <salt>-<visit>-<seq>: +2 Iron, -<n> coins
```
The delivery id's salt on StormTest's world is **`w4790ce`**, so it reads like `w4790ce-2-1`.

**Server log** (`VisitDirector.Settle`):
```
deal <salt>-<visit>-<seq> with <name>: sold 2 Iron at <n>, coins -<n> to the player; purse <n>
```
and, on a sell, `bought 10 Wood at <n>, coins +<n> to the player`.

**Then:** the inventory changed by exactly that, and `cargo stock Iron` shows **stock 18** and a higher
`you pay` than before.

**Paste back:** both `cargo stock Iron` lines (before and after), the `DONE` line, and the server's
`deal ...` line.

**Common failures**
- `cargo: no transport (join a world; the wire registers on connect)` — no world, or the wire never
  registered; the server log should carry `deal wire registered for <name> (<uid>)` and the client
  `deal wire: registered VCargo_dealt on the server socket`.
- `cargo: no visit is running (cargo status)` — the visit ended under you.
- `cargo: that is <n> coins and you carry <n>` — a client-side pre-check, before anything is sent.
- `cargo: he has no row named '<x>' (cargo stock)` / `cargo: he only buys <x>, he does not sell it`.

---

### 16 — Refusals (visit B)

```
cargo deal buy BlackCore 3
```
`BlackCore` has target stock 2, so a 3 is short. Expect
```
cargo: refused: sold_out
```
A buy you cannot pay for is stopped on the client before it is sent:
`cargo: that is <n> coins and you carry <n>`.

A **stale visit** is a deal built against a visit id that has ended: buy something, and while the answer
is out let the visit end (or `cargo dismiss` first, then re-send). Expect `cargo: refused: stale_visit`,
or `visit_over` when no visit is running at all. The server log carries
`deal refused for <name>: <reason>` for every refusal except `price_changed`.

The full token list is `Core/Deal.cs`: `sold_out over_max purse_empty coins_short inventory_full
visit_over unknown_item bad_count stale_visit duplicate price_changed empty_deal not_connected malformed`.

**Paste back:** each `cargo: refused: <token>` line with the command that produced it.

---

### 14 — The ledger (visit B)

**The easy half:** after any accepted deal the client acks at once, so the server's owed ledger is empty.
On a dedicated server, read it from the **visit-end line** (item 10's format), which counts it:
`..., <n> owed deliver(y|ies)` — it must say `0 owed deliveries`. On a listen host, `cargo status` says
`wire: <n> peer socket(s), <n> terminal(s) open, <n> deal(s), <n> redeliveries; owed ledger 0 row(s)`.

**The hard half (a redelivery) is a race and may not be provable in one sitting.** The client acks inside
the same handler that applies the deal (`Net/CargoTransport.cs`, `OnDealt`), so an owed row only survives
if the client dies between the server's answer and the ack. Try: `cargo deal buy Iron 2`, then Alt+F4
within a second of `cargo: sending`. Log back in and look for

- server: `VCargo_claim from <name>: redelivered 1 owed deal(s)`
- client: `delivery <id> applied: +2 Iron, -<n> coins` and the top-left HUD line
  `Ingvar's delivery: +2 Iron, -<n> coins`

`cargo claim` re-asks at any time (`cargo: asked the server for anything it still owes you; deliveries
print in the log and the HUD`). If the ack got out first there is nothing owed and nothing to redeliver:
**say so** rather than claiming the item. If the pack is full the client answers
`delivery <id> deferred: inventory_full (the server keeps it until it fits)` — that is the same path,
proven from the other end, and counts.

---

### 11a — `cargo dismiss` (ends visit B)

**Type:** `cargo dismiss`

**Console:** `cargo: asked the server to dismiss; its answer prints here`, then
```
cargo: visit #<n> dismissed (admin <name>)
```
**Server log:**
```
admin <name> (<uid>): cargo dismiss 
visit #<n> ended: admin <name>; takings <n> coins, purse <n>, <n> clock republish(es), 0 owed deliveries
```

**Paste back:** the console answer and the `ended: admin <name>` line.

(The other end reason, `dismissed by <name>`, comes from the terminal's "Send him off" — item 18.)

---

### 18 — The terminal on a real visit (visit C)

**Do:** `cargo visit`, then `cargo terminal open`.

**Console:** `cargo: terminal opened on visit #<n> with no merchant to stand by`
(before P5 there is no merchant to open it from; that is what this verb is for).
**Client log:** `terminal opened: visit #<n> with no merchant`.

Then, from CLAUDE.md item 18's own wording: the countdown matches the server's; a buy changes the
inventory by exactly the deal and the server log shows the deal; the same row's price moved on every
machine (that half is item 18b, second machine); a sell of goods you carry pays coins; **Fill from my
goods** covers a ware with the dearest goods first; **Send him off** twice ends the visit; Tab and M close
it; walking away closes it only once P5 gives it a merchant.

What the code fixes:
- every confirmed deal logs `terminal deal on visit #<n>: ok <delivery id> <+2 Iron, -50 coins>`, or
  `terminal deal on visit #<n>: <refusal token>`
- **Fill from my goods** appears only with `Pay with: Barter` selected and a ware staged; it answers
  `Offered <n> kind(s) of your goods against it.` or `Nothing of yours covers it.`
- **Send him off** arms for 5 s and reads `Ask once more`; the footer says
  `Send me off, then? Ask once more and I'll go.`, then `The Allfather calls me back to the mist!`
- close reasons in `terminal closed: <reason>`: `escape`, `use`, `inventory`, `map`,
  `inventory or map open`, `player gone`, `too far`, `visit over`, `he is leaving`, `close button`,
  `sent him off`, `console`, `session ended`, `error`
- the dismiss ends the visit through the wire: server log
  `VCargo_dismiss from <name>: visit #<n> dismissed (dismissed by <name>)` then
  `visit #<n> ended: dismissed by <name>; takings <n> coins, ...`

**Paste back:** the open line, one `terminal deal on visit #<n>: ok ...` line with the server's matching
`deal ...` line, the `ended: dismissed by <name>` line, and a screenshot of the window on a real market.

**Common failures**
- `cargo: no visit is running (cargo visit first)`.
- The window closes the instant it opens with `terminal closed: inventory or map open` — the inventory or
  the map was open underneath.
- `terminal closed: error` with `terminal draw threw:` or `terminal tick threw:` above it: paste the
  whole stack, it is the first real finding of the session.

---

### 15 — The sidecar and a restart mid-visit (visit D)

**Do:** `cargo visit`, one deal, then stop the server with **CTRL-BREAK** while the visit is running, and
start it again.

**The file:** `C:\Users\donfr\ValheimServers\StormTest\saves\worlds_local\valkyriescargo_4690126.dat`
(78 lines on 2026-09-06). Its own header names the rows:
```
format	1
# Valkyrie's Cargo world sidecar. Tabs; invariant culture. Rows: stock purse purseStart visit seq cool coolbase session owed.
```
After the deals it must carry the changed `stock` rows, the moved `purse`, `visit <n>`, `seq <n>`, a
`session` row while the visit runs, and `cool` rows after it. The previous file rotates to `.bak`.

**Server log on the restart** (`VisitDirector.Create` then `Adopt`):
```
director up: salt w4790ce, day 1800 s (EnvMan.m_dayLengthSec), catalogue 72 entries, purse <n>, next visit #<n>, roll every 60 s at 100%, first roll one interval from now; sidecar valkyriescargo_4690126.dat (<n> rows loaded, a saved visit waits for its event)
visit #<n> RESUMED after a restart: pilot <name>, <mm:ss> left by the saved clock (the event's own timer corrects it next tick)
```

**Paste back:** the `director up:` line from both boots, the `RESUMED` line, and the row counts from the
file before and after.

**Common failures**
- `saved session row not adopted: the engine did not restore event 'valkyries_cargo' within 15 s; that
  visit ended with the restart` — vanilla did not bring the event back (it saves the running event with
  the world); a real finding either way, paste it.
- `sidecar: format <n> is not <n>; keeping the file as .corrupt and starting fresh` or
  `sidecar: content but not one readable row; ...` — the file is quarantined as `.corrupt`; keep it.
- `event 'valkyries_cargo' is running with no visit session and no saved session row; ending it` — an
  orphan was swept, which is the designed behaviour, not a failure.

---

### 3 — The version wall

**Do:** edit `<Version>` in `ValkyriesCargo\ValkyriesCargo.csproj` to `0.1.1`, build, deploy to the
**client only**, and try to join StormTest (which still runs 0.1.0):
```powershell
.\tools\deploy-test.ps1 -Build -Client -ClientOnly     # the server's DLL is not touched
```

**On screen:** vanilla's connect failure (ServerSync sets `ZNet.ConnectionStatus.ErrorVersion`).
**Client log** (`Libs/ServerSync.cs`, `ErrorClient`, a Warning through Unity's log), one of:
```
Valkyrie's Cargo may not be higher than version <server's version>. You have version <yours>.
Valkyrie's Cargo needs to be at least version <server's minimum>. You have version <yours>.
Valkyrie's Cargo is not installed on the server.
```
**Server log** (`ErrorServer`):
```
Disconnect: The client (<platform id>) doesn't have the correct Valkyrie's Cargo version <version>
```

**Paste back:** the client line and the server line.
**Then put it back:** `<Version>` to `0.1.0`, rebuild, `.\tools\deploy-test.ps1 -Build -Client` (no
`-ClientOnly` this time), and check both "after" lines show the same assembly version again.

---

### 11b, 13b, 18b — the second machine (or a second, non-admin account)

**11b — the admin gate.** From an account **not** in `adminlist.txt`, `cargo visit`:
```
cargo: asked the server to visit; its answer prints here
cargo: not an admin (the server's adminlist.txt decides)
```
**Server log** (a Warning):
```
refused VCargo_admin visit from <name> (<uid>): not an admin
```

**13b / 18b — the same market on every machine.** With both clients connected and a visit running, one
buys 2 Iron; on the OTHER machine `cargo stock Iron` must show the same lower stock and higher price
without anything being typed there (`MarketState` is broadcast by ServerSync after every accepted deal).
With both terminals open, the other tray ticks over without reopening.

**4b — the config lock**, from the same non-admin account: `cargo status` shows `admin here=False`, and a
local edit of `MinComfortLevel` does not survive; the line still reads `following the server, locked=True`
with the server's `comfort>=4`.

**Paste back:** the refusal pair, and the two machines' `cargo stock Iron` lines side by side with the
`deal ...` line from the server that sits between them.

---

### 19, 20 — the body (when the bundle exists)

The loader is on `main` (`Client/BodyLoader.cs`, `Client/IngvarBody.cs`, P8 mod side). **Both items need a
baked bundle, which does not exist yet**: Wu'barrk bakes it on Unity 6000.0.61f1 (`models/SETUP-FOR-CLAUDE.md`),
it goes to `Assets\valkyriescargo_kit` at the repo root and the csproj embeds it on the next build (the DLL grows
by about the bundle's size; `tools/package.ps1` prints both sizes). For trying a bake without a rebuild, the same
file beside the DLL in the plugin folder is accepted, and `cargo body` labels it as a LOCAL file. Until a bundle
exists the verbs still answer, honestly, and that answer is worth one line in the log.

- **19 — `cargo body`** (client console, any time; on a dedicated server console too, if one is attached).
  Format strings from `Patches/Patch_Terminal.cs`, `BodyReport`:
  - `body: source embedded - <detail>` then `  resource: 'ValkyriesCargo.valkyriescargo_kit' inside this DLL, which is what makes every player's Ingvar the same one`. With no bake: `body: source none - <detail>`. With a loose file: `body: source file - <detail>` and `  file: <path> - a LOCAL file, NOT the copy other players have; embed it before it ships`.
  - `  bundle open, prefab 'ingvar' found, CustomBody=True (false keeps the Dverger stand-in), renderer=yes` — on a dedicated server `renderer=no` and nothing after it says anything about appearance.
  - `  clips (6 of 6 wanted): <names with lengths>`; any absent take prints `    MISSING '<name>': it plays at weight 0 and the rest carry on`. Expect `Walk 4.17s, Idle 10.00s, Talk 5.13s, Hello 3.75s, Shrug 1.96s, Nod 1.25s` -- the lengths Unity reported at the bake on 2026-09-07 (`models/README.md` section 1; its earlier row was one 24 fps frame longer on four of them).
  - `  rig: SkinnedMeshRenderer=yes, bones=24 (24 expected), tris=31112 (31112 expected); <bounds>; ground offset 0.000 m, derived from the meshes (0 expected: his origin is at his feet)`. An offset that is NOT near 0 prints ` - NOT near 0, and his origin is meant to be at his feet`: that is the bake, not the loader.
  - `  preview: none (cargo body preview)`.
  Paste the whole block into `CLAUDE.md` Status. The log carries the same facts at load, prefixed `body:`.
- **20 — `cargo body preview`** (client console, in a world, standing on open ground):
  - `cargo: Ingvar is standing 2.5 m in front of you at y <n> (ground offset <n> m), 6 clip(s) bound, graph live. Nothing about him is networked. ...`. Refusals: `cargo: nothing to draw here (no renderer)`, `cargo: no body to show - <detail>`, `cargo: no local player to stand in front of`.
  - Look: about 1.37 m tall (a head shorter than you), feet ON the ground, facing you, the idle moving rather than frozen.
  - `cargo body walk` → `cargo: walking on the spot at a simulated 1.0 m/s; the blend crosses over 0.15 s`; again → `cargo: back to his real speed, which for a body that does not move is zero`.
  - `cargo body clip Hello` → `cargo: Hello - blends in over 0.06 s, hands back at 85% of its length`; a second one while it plays → `cargo: 'Hello' is already playing, or the bundle does not carry it`. Then `Talk`, `Shrug`, `Nod` (the nod is subtle by design).
  - `cargo body` while he stands there: `  preview: up, graph live, 6 clip(s) bound, speed 0.00 m/s, blend 0.00 toward Idle, one-shot none` (with `(SIMULATED 1.0)` and `toward Walk` after `walk`).
  - `cargo body clear` → `cargo: the preview is gone`; again → `cargo: there was no preview`.
  Paste the preview line and what you saw into `CLAUDE.md` Status, and a screenshot into the PR that lands the bundle.

Then on a merchant (P5, Wu'barrk's): the Dverger and his crossbow gone, Ingvar walking when the agent walks,
`cargo prefab` showing the same component set as before the swap, and nothing in the log about a vanilla system
losing its animator.

---

## E. Reading the log

```powershell
.\tools\tail-log.ps1                       # StormTest, this boot, our lines
.\tools\tail-log.ps1 -Last 20              # ...the last 20 of them
.\tools\tail-log.ps1 -Client -Last 20      # the Steam client's log
.\tools\tail-log.ps1 -GaleProfile raveniron -Follow
.\tools\tail-log.ps1 -All                  # the whole boot, unfiltered
```

**The rule that matters: THIS BOOT is everything after the LAST `Chainloader started`.** StormTest's
`BepInEx\config\BepInEx.cfg` has `[Logging.Disk] AppendLog = true`, so
`C:\Users\donfr\ValheimServers\StormTest\BepInEx\LogOutput.log` accumulates across every boot (531 KB,
4406 lines on 2026-09-06). Believing the first `director up:` you scroll to is how you prove yesterday's
run. The script prints only the current boot and says which line it started at. (The Steam client's
`AppendLog` is **false**, so its file is one boot already; the rule is applied anyway.)

By default it keeps any line naming the mod (its own `[Info   :Valkyrie's Cargo]` tag, ServerSync's
version lines through Unity's log, and the `com.raveniron.valkyriescargo ConfigSync` registration), plus
`Chainloader started`, `Load world` and anything containing `Exception`. `-All` drops the filter.

The server's own file is `stormtest-server.log` (the `-logfile` in the start line); the BepInEx log is the
one with our lines in it.

**One known-benign line.** Every boot carries
```
[Error  : Unity Log] ArgumentNullException: Value cannot be null.
```
right after the plugins load. It **predates this mod** — CLAUDE.md records it in the 10:32 VantageTest run
with only Cairn and RavenEye loaded. Do not spend the session on it. Anything else with `Exception` in it
is ours until proven otherwise.

A healthy dedicated boot, in order (StormTest, 2026-09-06 19:25):
```
[Message:   BepInEx] Chainloader started
[Info   :   BepInEx] Loading [Valkyrie's Cargo 0.1.0]
[Info   :Valkyrie's Cargo] Valkyrie's Cargo v0.1.0 loaded - renderer=False, patches=13, catalogue=72 entries, ServerSync version gate armed; role is decided when a world loads.
[Info   :Valkyrie's Cargo] event 'valkyries_cargo' registered (<n> events now); duration 300 s, pauses with nobody within 96 m, no spawns, no music, no weather.
[Info   : Unity Log] Registered 'com.raveniron.valkyriescargo ConfigSync' RPC - waiting for incoming connections
[Info   : Unity Log] <time>: Load world: Dedicated (Dedicated)
[Info   :Valkyrie's Cargo] role: dedicated server
[Info   :Valkyrie's Cargo] admin wire up for this session: VCargo_admin is registered on each peer's OWN socket as it connects and ...
[Info   :Valkyrie's Cargo] director up: salt w4790ce, day 1800 s (EnvMan.m_dayLengthSec), ...
[Info   :Valkyrie's Cargo] roll: no eligible player: nobody online
```
On a client the same boot reads `renderer=True`, then `role: client`, then
`deal wire: registered VCargo_dealt on the server socket`; at logout,
`session ended: sidecar flushed; director, wire, reporter, routed RPCs and the terminal surface dropped`.
