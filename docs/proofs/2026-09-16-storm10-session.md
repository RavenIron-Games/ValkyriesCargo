# Storm10, 2026-09-16 — the visit clock runs whoever is near him (PR #97)

**What this is.** The lines behind PR #97's live claims, copied out of Storm10's `BepInEx\LogOutput.log`
on the day — rc5's session wrote the rule that a proof living only in that file is not preserved. The
extract is `2026-09-16-storm10-session.log.txt` beside this file: the mod's own lines, the engine's join,
leave, keep-socket and timeout lines, in order, for the two boots; the `Poll tick`, flight-authoring and
shelf-roll noise is left out. PR #97's body quotes the same lines.

**The server.** Storm10, `C:\Users\donfr\ValheimServers\Storm10`, port 2477, world Storm10, crossplay,
Valheim **1.0.12**, dedicated (so the engine freezes the world clock with nobody online, `ZNet.UpdateNetTime`,
and counts no host of its own). Don's client on the Gale **testing** profile, in-game `Nomadtest`, an admin.
The cfg's `PurseCoins` is the 100000 test value.

**The builds.** Both boots log `Valkyrie's Cargo v0.1.2 loaded`, because the version bump comes with the
cut. The 07:20 boot ran the branch's first commit (`19cb8bb`, md5 `390028001B3C98CC8B61D0AF8177101D`); the
07:44 boot its second (`4333134`, md5 `DF273FF9457CDB6EDD3B2AD5F1F73449`). Both: `patches 19/19 applied,
catalogue=101 entries, engine: same build 1.0.12 (net 40, player 46, world 41); probes 19/19 ok, 8 not
probeable` (the second with `ZNet.GetNrOfPlayers` new in the Rpc probe), and both registered the event with
the new line: `event 'valkyries_cargo' registered (19 events now); duration 300 s, runs whoever is near him
(Server.PauseVisitWhenEmpty holds it on an empty server), no spawns, no music, no weather.` The third
commit (`a2344d3`: the recorded duration read off the event's own clock, the contract row, the reconnect
note) changes what `visits` records, not what the log prints. The 0.1.3 cut's build booted on Storm10 at the
cut, `Loading [Valkyrie's Cargo 0.1.3]`, `v0.1.3 loaded - renderer=False, patches 19/19 applied, catalogue=101 entries, engine: same build 1.0.12 (net 40, player 46, world 41); probes 19/19 ok, 8 not probeable` (08:08, no client, the sidecar's 110 rows loaded, next visit #23).

## Visit #21 — the option off: the visit ends with nobody online, 07:31–07:38

**The bug.** Until this branch the visit's vanilla event carried `m_pauseIfNoPlayerInArea`, and
`RandomEvent.Update` adds no time to such an event with nobody within 96 m of it, so a visit whose pilot
walked off or logged out never ended — and, one random event at a time, held every raid and every later
visit with it. The branch registers the event with the flag off.

**Seen.** Nomadtest joined at 07:31:39; `cargo visit` gave `roll: forced visit: Nomadtest at (-63.80, -18.06);
1 eligible, 1 ticket(s)`, `visit #21 begins: pilot Nomadtest (uid -364342149) at (-63.80, -18.06), 300 s,
purse 100011, seed 2108171057`, `visit #21: dropped at (-57.81, 64.89, -5.62)`. The pilot left at 07:33:44:
`Player connection lost server "Storm10" … now 0 player(s)` — a clean close, the peer gone at once. With
the server empty from then on, the log carries `visit #21: one minute left`, then

```
visit #21 ended: timer; takings 0 coins, purse 100011, 206 clock republish(es), 0 owed deliveries
visit #21: merchant and bird reclaimed and their destroy queued (it lands on the next ZDOMan.Update)
```

about 300 s after `begins`, stamped between the 07:33:44 drop and the 07:43:15 stop, with nobody on. Before
the branch that visit would still be open.

**The 206.** The rule-2 review of the first commit predicted it: with the server empty the engine freezes the
world clock while the event's own clock runs on, so the director's once-a-second `Sync` saw the end time
move and republished `VisitState` to nobody every tick for the whole empty stretch. The second commit
stops the mirror while the server is empty; the first tick with a player back retargets it once.

## Visit #22 — the option on: the pause, 07:45–07:51

**Setup.** Stopped gracefully at 07:43:15 (`Game - OnApplicationQuit`); `PauseVisitWhenEmpty = true` written
into the cfg while the server was down; relaunched 07:44 on the second commit's build, `director up: …
purse 100011, next visit #22 …`, nothing to adopt; `Session "Storm10" with join code 608444 … active with
0 player(s)` at 07:44:30.

**Seen.** Nomadtest joined at 07:45:59 (`ZRpc timeout set to 90s`), `Got character ZDOID from Nomadtest`
07:46:22, `roll: forced visit: Nomadtest at (-63.61, -17.71)`, `visit #22 begins: pilot Nomadtest
(uid -456726670) at (-63.61, -17.71), 300 s, purse 100000, seed 1585188344`, `visit #22: dropped at
(-56.27, 65.26, -6.99)`. At 07:49:17 the connection was **lost, not closed**:

```
07:49:17: Keep socket for playfab/8BF4F5368AF43770, try to reconnect before timeout
07:49:17: Player connection lost server "Storm10" that has join code 608444, now 1 player(s)
07:49:17: Failed to send, suspend TX on playfab/8BF4F5368AF43770 while trying to reconnect
```

The engine kept the peer in its player list for the reconnect attempt (`ZPlayFabSocket.LostConnection`),
so `ZNet.GetNrOfPlayers()` stayed at 1, the director's count with it, and the clock ran on. At 07:50:46:
`ZRpc timeout detected`, the pilot's abandoned non-persistent ZDOs destroyed, and on that tick

```
visit #22: clock paused: nobody online (Server.PauseVisitWhenEmpty)
```

89 s after the loss. The engine fact this fixes into the option's description, the README row, DESIGN 3.7
and the proof checklist: the option engages when the engine has dropped the last player — at once on a
clean logout (visit #21), after its 90 s ZRpc timeout on a lost connection (this one). No `visit #22
ended` line followed while the server stood empty.

**The resume half, 07:56–07:59.** The server stood empty from 07:50:46 with no `visit #22 ended` line.
Nomadtest joined again at 07:56:38 (`Got handshake`, `Network version check, their:40, mine:40`, `admin
wire registered for Nomadtest`, `deal wire registered for Nomadtest`), and on the first tick with him
counted:

```
visit #22: clock running: 1 online
```

then, the clock having had between 60 and 68 s left when it paused (it began after 07:46:22 and the
`one minute left` line, which fires at 60 s while anyone is counted online, had not), `visit #22: one
minute left` and

```
visit #22 ended: timer; takings 0 coins, purse 100000, 0 clock republish(es), 0 owed deliveries
visit #22: merchant and bird reclaimed and their destroy queued (it lands on the next ZDOMan.Update)
```

stamped after 07:57:03 (`Got character ZDOID`, the last timestamped engine line before them) and before
08:00:16 (the next one). A visit that would have ended at about 07:51 with the option off
ended at about 07:58 with it on, the six empty minutes held. **0 clock republishes** across a lost
connection, a 90 s reconnect window, a six-minute pause and a resume: with the option on the engine
freezes the world clock on the same count that holds the event's clock, so the mirror never drifted —
the second commit's expectation for the option-on case (visit #21, option off, on the first commit's
build, republished 206 times to nobody). The extract runs on to the pilot's second session (08:00) and the
stop at 08:04:21.

## Not seen on the day

- The countdown a returning client shows (the pilot's screen was not pasted back); the resume line and the
  end are the server's.
- The third commit's recorded duration (`visits.duration_seconds` off the event's own clock) on a
  machine; a `stopevent` mid-visit.
- The option on a listen host (it never engages there: the host counts itself, `ZNet.UpdatePlayerList`).
- A visit restored across a restart with the option on.
