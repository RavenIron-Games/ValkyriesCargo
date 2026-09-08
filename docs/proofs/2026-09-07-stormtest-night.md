# StormTest session, 2026-09-07 20:18–21:10 — Don's Windows client, visits 10 to 15: rc2 on a screen, then D5

The night session after the rc2 cut. Visits 10 to 12 ran on rc2's code (main `84de90a`); the server was then
stopped, Wu'barrk's PR #54 (D5) reviewed, merged (main `2694d3b`) and deployed to both sides, and visits 13 to 15
ran on it. Every line the mod wrote on both sides is in `2026-09-07-stormtest-night.log.txt` beside this file.
**No exception from the mod on either side, all night** (the `ArgumentNullException` at each server boot is
vanilla's `ShieldDomeImageEffect` making a material with no shader on a headless server).

| | |
|---|---|
| Server | StormTest, dedicated, Valheim 0.221.12, our DLL alone, world `Dedicated`, port 2476; booted 20:18 on rc2's code, 20:44 on `2694d3b` |
| Client | Gale profile `Default`, the same DLL each time; session uid `-765188066` on the first join, `-677746031` on the second |
| Visits | 6 begun (all forced), 4 ended clean (2 timer, 2 dismissed), 1 left open at a disconnect (12, row cleared before the next boot), 1 open when the server was stopped (15) |
| Deals | none; the terminal opened on him on visits 13, 14 and 15 |
| Boots | both on a cleared session row; both logged `boot sweep: 1 stranded merchant(s) destroyed`, as predicted |

## The six visits

| visit | build | from | start | at the drop (the client's transition line) | first approach | end |
|---|---|---|---|---|---|---|
| 10 | rc2 | west | 90 m | `ours`, 47.5 m from the player, budget 31.7 s | gave up at 31.7 s: moved 242 m, stopped 10.1 m away; the leash's second approach reached +29 s | dismiss 20:23:55; reclaim +6 s; no sweep line |
| 11 | rc2 | east | 78 m | `watching`, 95.7 m (frozen mid-descent) | never — nobody owned him | timer 20:31:46; reclaim +0 s; a `leaving -> approaching via the ZDO` line on the watcher +1 s |
| 12 | rc2 | south | 78 m | `watching`, 116.8 m (frozen); **Don standing still** | never | open at the disconnect 20:37:06; row cleared before the 20:44 boot (backup beside the file) |
| 13 | D5 | south | 78 m | `ours (owner -677746031), 2 reclaim(s) during the carry`, 44.2 m, budget 29.4 s | **reached in 11 s** | timer 20:57:16; reclaim +0 s; no sweep line |
| 14 | D5 | east | 78 m | `ours (owner -677746031), 2 reclaim(s)`, 48.5 m, budget 32.3 s | **reached in 12 s** | dismiss 21:04:21; reclaim +0 s; no sweep line |
| 15 | D5 | north-east | 90 m | `ours (owner -677746031), 0 reclaim(s)`, 46.3 m, budget 30.9 s | **reached in 13 s** | open when the server was stopped from the desktop (~21:10); its row is in the sidecar with `purse` 100000, a test value |

The "N m from the player" at the drop is a 3D distance: the release is about 47 m above the ground, 13 m across.

## Proven on a screen tonight

- **D5 (PR #54): three for three.** The pilot's client is the owner at every drop on the fixed build, from three
  directions, two of which lost him on rc2 an hour earlier.
- **D1 and F5, at last.** The entry reset ran on every drop (budgets scaled from the release distance, 29–32 s,
  never the old flat 20 s), and **the first approach reached the player on 13, 14 and 15** — the first three
  times in fifteen visits.
- **D3 (PR #51).** Every clean end printed `merchant and bird reclaimed and their destroy queued` and no sweep
  line (10, 11, 13, 14). Before it, every end printed `restart sweep: 1 stranded`.
- **D4a (PR #48).** Both wires registered for Nomadtest once the identity had arrived (20:20:05, 20:51:25), and
  the client's own registration lines followed.
- **The half-turn (PR #53).** `turned 180 deg (Client.BodyYawDegrees)` printed on every attach. Whether he walks
  facing forward is Don's to say; it is not in the log.
- **The boot sweep on a cleared row**, twice, exactly one stranded merchant each time.
- **Clean boot lines** on both sides on both builds: `patches 18/18 applied`, `probes 18/18 ok, 7 not probeable`,
  the version handshake `0.1.0` both ways.

## Found tonight

1. **The ownership loss reproduces on rc2 with the pilot standing still** (visit 12), so movement is ruled out and
   the direction is the variable. On D5 the reclaim counts run **2 / 2 / 0** from the south, the east and the
   north-east. By `Core/ZoneOwnership.cs`'s own arithmetic every one of tonight's starts is inside the pilot's 3x3
   (pilot zone (-1, 1); starts in (-2, 0), (0, 0), (-1, 0), (0, 2)), and on rc2 the frozen distances put the
   loss 8 to 11 s into the flight, on the descent leg, well inside the block. So the trigger is not the plain
   sector strip around where the player stands; PR #54's story ("stripped at the flight start, 90 m out") does
   not fit these numbers. The fix holds regardless. The ask on #54: one line, once per carry, at the first
   reclaim, with his position, the player's and the seconds since waking.
2. **F3's vanish grace does not show.** End to reclaim: 6 s (10, dismiss), 0 s (11, timer), 0 s (13, timer), 0 s
   (14, dismiss). The director ticks once a second and both kinds of end go through `End` → `SendVanish` +
   `VanishGraceSeconds` (2 s) → `FinishDeparture`, so every gap should read 2 or 3 s. The vanish RPC does reach
   the client (visit 11 logged `leaving` a second after the end), so he does not blink out unannounced, but the
   beat between the farewell and Odin's effect is not there. Track B's file; noted on #54.
3. **Visit 10's first approach on rc2**: ownership held, the budget scaled, and he still gave up — 242 m moved in
   31.7 s (running the whole budget) and 10 m short of the 3.5 m arrival radius. Don may have been moving. Not
   seen again: on D5 every first approach reached.
4. **A cosmetic line on a watcher**: visit 11's `leaving -> approaching via the ZDO` at the end — the local state
   went `Leaving` on the RPC while the ZDO still said `approaching`, because nobody owned him to write it.

## Not seen tonight

The walk direction and the vanish (Don was outside the base at both timer ends; the roll after each said
`comfort < 4`), the release over the drop point, any deal, and two clients.

## What the rc2 cut is missing

`v0.1.0-rc2` (e4ee83c) does not carry D5. Anyone on it gets tonight's visits 11 and 12. The next cut is on the
owner's word; the condition he set for it (the ownership fix in, a first approach that reaches) is met but for
the vanish being watched.
