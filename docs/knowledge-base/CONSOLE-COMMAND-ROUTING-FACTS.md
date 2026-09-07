# Console command routing facts

Written after Njord's admin commands turned out to be unreachable on a dedicated server for
their entire life. Nothing here is Njord-specific — any mod in this solution that registers a
console command doing server-side work can hit all of it.

> [!IMPORTANT]
> **Half of this was already written down and I did not read it.** See *"A Jotunn `ConsoleCommand`
> runs where it was TYPED, not on the server"* in `VALHEIM-DEDICATED-SERVER-FACTS.md`, verified
> 2026-08-11 against Avalor — including the exact log signature I spent an afternoon
> rediscovering: **the server logs the raw command text from a remote admin and then none of the
> command's own log lines follow.** Njord's server log showed precisely that and it took a second
> pass to recognise. Read the existing FACTS files before debugging; this one covers the
> vanilla-`Terminal` specifics and the admin-check material that the older note does not, and the
> two should be read together rather than either alone.

Line numbers refer to `DECOMPILED ASSEMBLY VALHEIM/assembly_valheim.decompiled.cs`.

## The one that costs an afternoon

**A console command runs on the machine it was typed into.** `Terminal.ConsoleCommand`'s
`onlyServer` flag, and any `if (!ZNet.instance.IsServer()) return;` guard you write yourself,
decide **who may run a command — never where it runs**.

On a dedicated server this makes a naive server-guarded command unrunnable by anybody:

- the admin is a **client**, so the guard rejects them;
- the headless host has no `Player.m_localPlayer` to stand in for anyone, and nobody is typing
  at its console.

The command is not broken in the sense of throwing. It is unreachable, which looks the same
from outside and debugs very differently.

## The pipe vanilla already provides

`ZNet.RemoteCommand(string)` (≈67980) ships the text to the server. Server side,
`RPC_RemoteCommand` checks the sender against **`adminlist.txt`** before running anything, then
calls `Console.instance.TryRunCommand(command)`.

Two consequences worth having:

- The admin check is the **server's**, not a client-side opinion you have to trust. Do not
  reimplement it.
- `Console` exists in the dedicated-server assembly (`assembly_valheim_SERVER.decompiled.cs`,
  ≈35277, 44 references), so this works headless.

Working pattern — the position must travel with the command, because the machine doing the
work has no player standing anywhere:

```csharp
// Client reads its own position, then hands off. Returns true when the caller should stop.
if (RouteToServer($"mymod.placeat {Inv(p.x)} {Inv(p.y)} {Inv(p.z)}")) return;
```

**Format and parse coordinates with `CultureInfo.InvariantCulture`.** A client with a comma
decimal separator otherwise sends `mymod.placeat 12,5 30,0 -8,25`, which splits into six
arguments.

The cost of routing is that output prints in the **server's** console, not the admin's. Say so
in the client's console rather than letting the command look silent. (MoA's `AvalorCommands`
reached the same conclusion independently and routes through its own RPC — either is fine; the
vanilla pipe is less code and gets the adminlist check free.)

`Terminal.TryRunCommand` (37632) also auto-routes for you if the command is registered with
`onlyServer: true, remoteCommand: true` — vanilla uses exactly that pairing (35889 onward). Use
it when the command needs no client-side input. Use the manual `RouteToServer` shape above when
it does, or when you want to tell the admin where the answer went.

## Command validity gates

`ConsoleCommand.IsValid` (35623):

```
(!IsCheat || context.IsCheatsEnabled())
  && (context.isAllowedCommand(this) || skipAllowedCheck)
  && (!IsNetwork || ZNet.instance)
  && (!OnlyServer || ZNet.instance.IsServer())
```

- `Terminal.isAllowedCommand` (37822) returns **true** — the F5 console allows anything. The
  `Chat` override (34524) refuses cheat commands, so `/mycommand` in chat behaves differently
  from the console.
- **`IsCheatsEnabled()` (37619) returns `ZNet.instance.IsServer()`.** A client on a dedicated
  server can therefore *never* have cheats on in vanilla — which is what Server devcommands
  exists to fix. **Do not register a mod command with `isCheat: true` if admins on a dedicated
  server need it.**
- `onlyAdmin: true` implies `OnlyServer` (35523). It does not add a check of its own in
  `IsValid`; the real admin gate is `RPC_RemoteCommand`'s adminlist test.

## The visibility trap

`Log.LogWarning` / `LogInfo` go to the BepInEx log, which is **invisible in game**. A command
that refuses via the log alone is indistinguishable from a command that does not exist — the
player types it and nothing whatsoever happens.

Print refusals and results with `Console.instance?.Print(text)` (35379). Log as well if you
like, but the console is the one the person typing can see.

Same trap one level down: if your mod's `Warn` helper is gated behind a `Debug_Enable` config
that defaults to false, then **failures are silent by default while successes are not**, and the
one boot you most want to observe tells you nothing. Njord shipped exactly that. Check which of
your log levels are gated, and by what.

## Registration

`new Terminal.ConsoleCommand(name, description, action)` only writes into a static dictionary
(`commands[command.ToLower()] = this`, 35516), so **immediate registration works everywhere**,
including headless and before any Terminal instance exists. Elaborate wait-for-Terminal
coroutines are unnecessary.

Pass a real `description`: it is what `help` prints, and a command with `""` is a command nobody
finds. Command names may contain dots — `TryRunCommand` splits on spaces only.

## Admin checks: never let the client decide

There are **two** admin matchers in vanilla and they do not agree.

- **Server side**, `ZNet.ListContainsId` (67818) parses the id and falls back from the qualified
  form to the bare one, so a `adminlist.txt` line of `76561198224105156` matches a peer whose
  host name is `Steam_76561198224105156`. This is what `RPC_RemoteCommand` uses, and it is why
  devcommands work.
- **Client side**, `ZNet.PlayerIsAdmin` does a plain `adminList.Contains(networkUserId.ToString())`
  against the **qualified** form only. A bare-id adminlist — which is how nearly everyone writes
  it — never matches, so `LocalPlayerIsAdminOrHost()` tells a genuine admin they are not one.

Njord shipped a client-side gate on `LocalPlayerIsAdminOrHost()` and refused a listed admin.
Normalising both forms did **not** fix it. **The cause, confirmed by a diagnostic command on a
live client, is that `ZNet.GetAdminList()` returns an EMPTY list on a client** — a full minute
after joining, with the id resolving correctly to `Steam_76561198224105156` and the server's
`adminlist.txt` holding the matching bare id.

Do not be talked out of this by reading `SendAdminList()`, as I was. It does push the raw file
lines to every peer where `IsReady()` (which is only `m_uid != 0`, set before the call in
`RPC_PeerInfo`), so the send *looks* correct — but what the client ends up holding is empty
regardless. What the server sends is not evidence of what the client has. Measure the client.

Jotunn sidesteps the whole problem: `SynchronizationManager` asks the server and receives a
boolean (`Received admin status from server: Admin`). If a mod genuinely needs a client-side
answer, that is the shape to copy — one RPC, not a synced list.

**The rule that came out of it, which is worth following regardless of that cause:**

> `adminlist.txt` lives on the server. A client-side admin check can only guess, and a client
> that refuses on its guess stops the command before the server ever hears it — so the server
> log is empty and there is nothing to diagnose. Make the local check **advisory**: warn, then
> send. Let the authority refuse, because it is the machine with the data.

Corollaries:

- If a client-executed action writes to the world before the server authorises it, the server
  must be able to **undo** it on refusal, not merely decline to record it.
- When a server-side check cannot resolve who the sender is, that is **Unknown**, not "not
  authorised". Collapsing the two makes the authority destroy legitimate work — for Njord that
  would have been an NPC appearing and then vanishing, which reads as the feature being broken.
- Ship a `whoami`-style command that prints the resolved id, the list as that machine sees it,
  and each entry's normalised form. Identity bugs are unfalsifiable from a log that only records
  the verdict.
