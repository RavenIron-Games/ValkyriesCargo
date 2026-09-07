# Trust boundary — what the server believes, what it verifies, 2026-09-07

P11b. The question is not "is it synced" but **"what can a client change that it should not?"**

There was no document that said what the server believes. This is it. Everything below was read out of the
code on this branch, out of `Libs/ServerSync.cs`, and out of the 0.221.12 decompile
(`ZRoutedRpc`, `ZRpc`, `ZNet`, `ZNetPeer`, `Valkyrie`, `RandEventSystem`, `SE_Rested`, `Player`, decompiled
2026-09-06 and 2026-09-07). Nothing here is inferred from a shape.

Two things it is honest about up front:

- **A dedicated Valheim server has no inventory and no position for anybody.** Vanilla keeps both on the
  client-owned player ZDO and puts neither on the wire in a form the server checks. Every "the server cannot
  verify this" row below traces back to that one fact, and no amount of our code changes it.
- **Every client runs this exact DLL** (`ModRequired`, `MinimumRequiredVersion == CurrentVersion`), which is
  a version gate, **not** an integrity gate. It stops a stale client; it does not stop a patched one.

Three defects were found and fixed on this branch; they are marked **FIXED** below. The rest are named,
bounded, and either proposed as diffs in files that are not ours this round or accepted with the worst case
written down.

**Re-read 2026-09-07 against `main` after PR #22.** Every ZDO key and RPC name is now `VCargo_*`, held once in
`Core/Keys.cs` (issue #16; the collision mechanics are in `docs/DECISIONS-WUBARRK.md` §1: `ZRpc.Register`
replaces a handler by name with no warning, and a ZDO key read through another mod's key returns that mod's
plausibly shaped number). The drop-point bound proposed in §3 was implemented by Wu'barrk in PR #18 before P5
landed. P5 is on `main`, so its state key and its immortality are checked below against the merged code, not
the design.

---

## 1. Configuration

### Every key, and how it is bound

`Config/ModConfig.cs` binds through exactly two private helpers and one special case. There is no third path,
and no key bound directly on `cfg` except the locking one:

| Path | Keys | What it does |
|---|---|---|
| `S(cfg, "Server", …)` (`ModConfig.cs:252`) | the other **28** `Server.*` keys | `cfg.Bind` then `Sync.AddConfigEntry(e)`; `SynchronizedConfig` stays `true` |
| `cfg.Bind` + `Sync.AddLockingConfigEntry` (`ModConfig.cs:88`–`90`) | `LockConfiguration` | `AddLockingConfigEntry` itself calls `AddConfigEntry` (`ServerSync.cs:215`), so this key is **in the sync and locked like the rest**, and is additionally the entry whose value decides the lock |
| `C(cfg, "Client", …)` (`ModConfig.cs:263`) | the **4** `Client.*` keys | `cfg.Bind` then `Sync.AddConfigEntry(e).SynchronizedConfig = false` |

Verified key by key against the binder: all 29 `Server.*` entries are in the ConfigSync and all 4 `Client.*`
are excluded from it. **No `Server.*` key is bound another way**, and no `Client.*` key leaks into a package —
`SynchronizedConfig = false` is checked in three places: the `SettingChanged` broadcast lambda
(`ServerSync.cs:197`), `ReadConfigsFromPackage`'s map (`:482`) and `ConfigsToPackage`'s list (`:992`).

The two `CustomSyncedValue<string>`s, `visit` and `market` (`ModConfig.cs:185`–`186`), are a different
mechanism: they are **not** filtered by `SynchronizedConfig` anywhere. See the finding in section 2.

### What a locked client's local edit actually does

Read out of `Libs/ServerSync.cs`. Five separate mechanisms, in the order a value would have to get past them:

1. **The config file cannot move the live value.** `PreventConfigRereadChangingValues` prefixes
   `ConfigEntryBase.SetSerializedValue` (`ServerSync.cs:968`): once the server has sent a value, the entry
   carries the client's own number in `LocalBaseValue`, and a re-read of the `.cfg` writes there and returns
   false. The value the mod reads never changes.
2. **The config manager shows it read-only.** `serverLockedSettingChanged` sets
   `ConfigurationManagerAttributes.ReadOnly = !isWritableConfig(entry)` (`:585`, `:593`).
3. **An in-memory edit does not leave the machine.** `SettingChanged` reaches `Broadcast`, whose whole body
   is inside `if (!IsLocked || isServer)` (`:916`, `:925`). On a locked non-admin client that is a no-op.
4. **The server would drop it anyway.** `HandleConfigSyncRPC` (`:350`) begins:
   `if (isServer && IsLocked && SnatchCurrentlyHandlingRPC.currentRpc?.GetSocket()?.GetHostName() is { } client)`
   → look `client` up in `ZNet.m_adminList` → `if (!exempt) return false;`. **Note where that identity comes
   from: the SOCKET the packet arrived on.** Not the routed `sender` argument, which is right there in the
   same method signature and is ignored. That is the same choice this branch had to make for `VCargo_admin`, and
   blaxxun made it correctly.
5. **The server's value never reaches the client's own file.** `PreventSavingServerInfo` prefixes
   `GetSerializedValue` (`:952`) so what is written to disk is the client's `LocalBaseValue`. On disconnect
   `ResetConfigsOnShutdown` (a postfix on `ZNet.Shutdown`) puts every `LocalBaseValue` back, so a player's own
   settings return the moment they leave.

**The admin exemption.** `lockExempt` is a static bool set from an `Internal/lockexempt` package the server
sends: at handshake, and again whenever `adminlist.txt` changes, from a coroutine that diffs `ZNet.m_adminList`
every 30 s and re-sends to admins and non-admins separately (`ServerSync.cs:265`–`300`). Membership is decided
by each peer's `m_rpc.GetSocket().GetHostName()` — the socket again. `IsLocked` returns false while
`lockExempt` (`:138`), so an admin's edits both broadcast and are accepted. Two consequences worth knowing:
`lockExempt` is **private static on the class, not per-ConfigSync**, so an admin on this server is
lock-exempt for every ServerSync mod in the process at once; and an admin removed from the list stays exempt
for up to 30 s. Both are blaxxun's, in blaxxun's file, and not ours to change.

**The runtime check is not this document.** `CLAUDE.md`'s verify item 4 — a client edits `MinComfortLevel`
while connected, `cargo status` still shows the server's value and says `following the server` — **has never
been run**, and reading the binding code is not running it. It needs a client and a server, and it is a
**client proof**, listed in `docs/PROOF-CLIENT.md`. Everything in this section says the code is shaped right;
only item 4 says it works.

---

## 2. RPCs

### Who may send, what is checked, what a liar gets

| RPC | Transport | Who may send | Identity the receiver uses | What it validates | What a lying client gets |
|---|---|---|---|---|---|
| `VCargo_admin` | **direct peer `ZRpc`** (was routed) | any connected client | **the socket**: `DealWire.PeerFor(rpc)` by reference, then `ZNet.IsAdmin(peer.m_socket.GetHostName())` through `AdminGate`, fail closed | the verb is dispatched only after the gate; a socket with no resolvable peer is refused | `cargo: not an admin`. **FIXED — was a privilege escalation, see below** |
| `VCargo_reply` | direct peer `ZRpc` | the server only (the client registers it on `ZNet.GetServerRPC()` alone) | — | — | text in the caller's console; cosmetic |
| `VCargo_open` / `VCargo_close` | direct peer `ZRpc` | any connected client | the socket → `peer.m_uid` | **nothing**: the `visitId` argument is read and never used | a wrong number in `cargo status`'s `open terminals`. The set is keyed by peer uid, so repeats cannot grow it. Accepted |
| `VCargo_deal` | direct peer `ZRpc` | any connected client | the socket → the platform id (`m_socket.GetHostName()`), which is the owed ledger's key | `Market.Settle`'s order: malformed, empty, **stale visit id**, **duplicate nonce** (500-ring), the wanted line (unknown / not a `Ware` / bad count / **sold out** / **price changed**), each offered line (unknown / bad count / **over max** / **price changed**), coins short, **purse empty** | refusals for everything the server can see. **Not checked: the player's coins and the player's goods** — see below |
| `VCargo_ack` | direct peer `ZRpc` | any connected client | the socket → platform id | `OwedLedger.Ack`: an unknown id returns false with no work; **another player's row returns false** (`OwedLedger.cs:60`) because the key is the socket's, not the packet's | nothing. Cannot clear anyone's row, including by guessing ids |
| `VCargo_claim` | direct peer `ZRpc` | any connected client | the socket → platform id | now one claim per peer per 5 s (`DealWire.ClaimCooldownSeconds`) | its own owed rows, at most every 5 s. **FIXED — was an amplifier** |
| `VCargo_dismiss` | direct peer `ZRpc` | any connected client | the socket → `peer.GetRefPos()` | the visit is running, the id matches, **and the caller is within 96 m of the drop point** | refused and logged (three times, then silently). **FIXED — was any client, anywhere** |
| `VCargo_dealt` | direct peer `ZRpc` | the server only | — | the client checks the delivery id against its own inbox before applying, and acks only what `DealApplier` actually wrote | out of model: a hostile *server* is a server you chose to join |
| `<name> ConfigSync` | **routed**, registered on every machine (`ServerSync.cs:257`) | anyone | **the socket**, on the server (`SnatchCurrentlyHandlingRPC`) | on the server: dropped whole unless the socket is on `adminlist.txt` while locked. **On a client: no check at all** | see the finding below |
| `SetEvent` | vanilla routed | vanilla's own | vanilla's | vanilla's | vanilla's. Our event is registered on every machine so the name resolves; a client that removed the registration only stops seeing its own banner |
| `UIFocus`'s two patches | not RPCs | — | — | — | `Chat.HasFocus` postfix and `GameCamera.UpdateMouseCapture` prefix+postfix are **purely local**: they read static token sets this process filled, touch no ZDO and send nothing. A modified client can free its own cursor and un-gate its own input with or without them. They carry no authority and need none |

### `VCargo_admin` — the privilege escalation, and why it existed

**FIXED on this branch.** Any connected non-admin could run `cargo visit`, `cargo dismiss`, `cargo reset` and
`cargo save` on the server.

`VCargo_admin` rode `ZRoutedRpc`, and the handler took the caller from its `sender` argument. That argument is
`ZRoutedRpc.RoutedRPCData.m_senderPeerID` — **a field the sender serialises into the packet**
(`ZRoutedRpc.cs:20`–`38`). `RPC_RoutedRPC` deserialises it and passes it to `HandleRoutedRPC`, which hands it
to the registered handler, and **at no point is it compared to the socket the packet arrived on**
(`ZRoutedRpc.cs:175`–`197`). `AdminRpc.OnRequest` then did `znet.GetPeer(sender)` and asked `AdminGate` about
*that* peer — so writing an admin's uid into the field got an admin's socket checked against `adminlist.txt`,
and passed.

The gate was never wrong. `AdminGate` is unchanged and still asks vanilla's own public
`ZNet.IsAdmin(hostName)` and still fails closed. What was wrong was the identity it was asked about. The old
file's header even said "routed RPCs are forgeable, so the SERVER decides who is an admin" — the conclusion
was drawn and then the forgeable field was used anyway.

Fixed by moving both verbs onto the direct peer `ZRpc`, where there is no sender field and the connection is
the identity — the same wire, for the same reason, as the deals (design 3.4). `DealWire.PeerFor` was made
public rather than copied.

**Two boot lines change.** `routed RPCs registered for this session: VCargo_admin, VCargo_reply` is gone; a
server now prints `admin wire up for this session: VCargo_admin is registered on each peer's OWN socket as it
connects …` plus `admin wire registered for <player> (<uid>)` per peer, and a client prints
`admin wire: registered VCargo_reply on the server socket`. `cargo status` says `admin wire up (direct peer
ZRpc)` instead of `routed RPCs registered`. `CLAUDE.md`'s StormTest record of 2026-09-06 quotes the old line
as history; `docs/PROOF-CLIENT.md`'s expected boot line is updated on this branch.

### `VCargo_deal` — the coins and the goods

`VisitDirector.Settle` passes `deal.CoinsOffered` — a client-written field — as `playerCoins`, and
`Market.Settle` never checks that the offered lines are carried. Both were already named
(`docs/ECONOMY-SIM.md`, "Not a number, but worth knowing"; PR #12's finding 7) and neither was settled.

**Settled here: accept, and say so at the call site** (done, `VisitDirector.cs`). There is no server-side coin
count and no server-side item list to compare against, and there never will be while vanilla keeps the
inventory on the client-owned player ZDO. `coins_short` is a courtesy to an honest client, not a guard.

What holds the line is the **market's own numbers, every one of them the server's**:

| A modified client tries | What stops it | The bound |
|---|---|---|
| buy without paying | nothing — the deal settles and the purse is *credited* | it costs him **stock**, and stock cannot go below 0 (`sold_out`). The shelf is where it would have been after an honest deal |
| sell goods it does not carry | nothing — the deal settles | `purse_empty`: not one coin past what the purse holds, so **the whole exposure is one purse per visit** (`PurseCoins` + carry, capped at 3×), and `over_max` keeps the shelf inside the catalogue's `MaxStock` |
| replay a deal | the 500-entry nonce ring, per visit | `duplicate` |
| use last visit's prices | the visit id | `stale_visit` |
| claim a price that moved | `UnitPriceSeen` vs the live `Charge`/`Pays` | `price_changed`, with the new market attached |
| flood the ring with refusals | a refusal calls `NonceRing.Forget`, so it never fills | nothing. And 500 *accepted* deals inside one 300 s visit is not reachable; even if it were, eviction would only let an old nonce settle again **at today's prices**, which is a new deal, not a replay |

That is an honest, bounded, per-visit exposure against numbers the server owns. It is the answer to "the
market's own bounds, not the client's number, protect the purse and the stock" — they already do.

### `VCargo_dismiss` — what a visitor is

**FIXED on this branch.** Design 3.8 says `VCargo_dismiss` may come from "any visitor" and never says what a
visitor is. The handler checked only that a visit was running and that the id matched — and the id is
published to every machine in `VisitState`, so the whole attack was "send back the number you were already
given". Any client, anywhere in the world, ended anyone's visit.

**Picked: a visitor is someone within the event's own `m_eventRange` (96 m) of the visit's drop point.** That
is the radius that already means "at this visit" everywhere else in this mod — inside it a player sees the
banner and keeps the clock running, outside it vanilla pauses the clock (`CargoEvent.Register`'s
`m_eventRange = 96f` and `m_pauseIfNoPlayerInArea`). Being online is not being a visitor.

**Enforced**, with the honest caveat written into the code: the position is `peer.GetRefPos()`, which `ZNet`
attributes to the socket (`RPC_ServerSyncedPlayerData` resolves the peer from the `ZRpc`) but takes on the
client's word — the same trust class as `VCargo_rested`. It closes "anyone, anywhere". It does not close a
modified client that lies about where it stands, and **there is nothing server-side to check that against**.
The terminal's own "Send him off" is unaffected: it needs the player within 5 m of the merchant to be open.

**One path is not gated and should be**: `LocalTransport.Dismiss` (`Net/CargoTransport.cs:141`) calls
`d.Dismiss` in-process on a listen host with no distance check. It is the host's own console or terminal, so
the exposure is "the host can dismiss", which the host can do anyway with `cargo dismiss`. Left alone;
`CargoTransport.cs` is not ours this round.

### The vendored ConfigSync RPC — a client-to-client path the lock does not cover

**Not fixed. `Libs/ServerSync.cs` is not ours ("update from upstream, never edit"), and this is upstream's.**

`ZRoutedRpc.instance.Register<ZPackage>(Name + " ConfigSync", RPC_FromOtherClientConfigSync)` runs on **every**
machine (`ServerSync.cs:257`), and `HandleConfigSyncRPC`'s lock check is guarded by `isServer`
(`:350`). Follow a package sent by a modified client with `targetPeerID == ZRoutedRpc.Everybody` (0):

1. On the **server**: `RPC_RoutedRPC` handles it — and the lock check drops it, because the socket is not on
   `adminlist.txt`. Correct.
2. **And then relays it anyway.** `RPC_RoutedRPC`'s second statement is
   `if (m_server && data.m_targetPeerID != m_id) RouteRPC(data)`, which is unconditional on what the handler
   decided (`ZRoutedRpc.cs:183`–`186`). Every other peer gets the packet.
3. On **each other client**: `isServer` is false, so the lock check is skipped entirely, and the loop at
   `ServerSync.cs:440` writes `configKv.Key.BaseConfig.BoxedValue = configKv.Value` — the live value.

So a modified client can push `Server.*` values, **and both custom synced values**, onto every other client.
`ReadConfigsFromPackage` filters config entries by `SynchronizedConfig` but applies **every** custom value it
can name, so `visit` and `market` are reachable.

**The bound, which is why this is a row and not an alarm.** Of the keys that are read on a client, only
`CustomBody`, `FlightSpeed`, `FlightTurnRate` and `EnableBarter` do anything, and all four are cosmetic. The
two channels are the real target — a poisoned `MarketState` makes another player's terminal show prices and
stock that are not the server's. **It cannot make a bad deal go through**: every deal carries the prices the
player saw and the server re-checks them (`price_changed`), and a poisoned `VisitState` only earns
`stale_visit` or `visit_over` at settlement. **And it is not sticky**: the server's next write to either
channel overwrites it, which for `MarketState` is the next accepted deal and for `VisitState` is the next
clock republish.

**Proposed upstream** (blaxxun's `ServerSync.cs`, to be raised with upstream rather than patched here) — one
line at the top of `HandleConfigSyncRPC`, beside the existing server-side check:

```csharp
// A client is never a source of config for another client: only the server's own socket may write here.
if (!isServer && clientUpdate) return false;
```

Until then, this is the honest statement: **`VisitState` and `MarketState` are server-written and
client-forgeable *on the display side only*.** Every decision that costs anything is re-taken on the server.

---

## 3. ZDO writes the server trusts

Only an owner's writes replicate, so "who owns this ZDO" is the whole question.

### The player's own character ZDO — `VCargo_rested`, `VCargo_comfort`, `baseValue`, position, name, `dead`

`Client/ComfortReporter.cs:60`–`61` writes `VCargo_rested` and `VCargo_comfort` on the local player's own ZDO every
2 s. `Server/VisitDirector.cs:415`–`416` reads them back out of `GetAllCharacterZDOS()`, along with
`ZDOVars.s_baseValue`, `s_playerName`, `s_dead` and the position.

**Every one of these is a client asserting something about itself**, and that includes the two vanilla ones:
`Player` writes its own `baseValue` on its own ZDO (`Player.cs:1828`), which is exactly what our two keys do.
There is no vanilla path that computes comfort anywhere else — `SE_Rested.CalculateComfortLevel` is static,
local, and reads `Piece.GetAllComfortPiecesInRadius` around the player's own transform.

**Decision: accept.** Recorded as a `docs/DESIGN.md` section 8 row on this branch, marked *proposed*:

> **Client-reported eligibility** — `VCargo_rested`, `VCargo_comfort`, and vanilla's own `baseValue`, `playerName`,
> `dead` and position, all written by the client on its own character ZDO and read by the server's scheduler.
> **proposed: accept.** Worst case is an undeserved visit — a merchant, on a cooldown the liar spends, at a
> place the liar named. The alternative is server-side comfort, which vanilla does not compute there
> (`SE_Rested.CalculateComfortLevel` is static, local, and walks the pieces around the player's own
> transform), so it would mean reimplementing the comfort rules on the server against a piece list the server
> does have — a large amount of new surface to stop somebody giving themselves a shop.

The rest of that trust class, for completeness:

- **position** decides the drop location, the cooldown stamp and the flight plan. A liar's flight is planned
  around the position it named, so it strands its own bird; the cooldown is stamped where it claimed to be,
  which blocks a real base only if it claimed to be there.
- **`playerName`** is how `cargo visit <name>` finds a candidate (`VisitDirector.FindByName`). Two clients can
  claim one name; an admin's forced visit would then pick whichever matched first. Cosmetic, noted.
- **`GetOwner()`** is the peer uid, assigned by the engine on the ZDO, not a field in a payload. It is the one
  value in `Gather()` a client does not write.

### The bird's ZDO — `VCargo_target` and `VCargo_dropped` (owner: the pilot)

**The one place a client's ZDO write moves server state. It was unbounded when this was written; FIXED in
PR #18 (Wu'barrk, 2026-09-07), as proposed below.** As audited, `Server/Spawner.cs:216` read
`VCargo_dropped` off the bird; when it flips, `:219`–`:220` reads `VCargo_target` and calls
`session.SetDrop(at.x, at.y, at.z)` — **with no check on the value at all**. The bird is owned by the pilot
(`Spawner.cs:145`, `SetOwner(pilotUid)`), so the pilot may write both keys at any moment.

A hostile pilot can therefore put the visit's drop point **anywhere in the world**, at any time, and skip the
flight while doing it. Today (P5 not in) that means a wrong drop in `VisitState` on every screen; with P5 it
means the merchant is authored, and stands, at a point the pilot chose.

`FlightPlan` bounds the *plan*. Nothing bounds what comes back. And the legitimate value barely moves:
`CargoFlight.Drop` writes `at = _drop` with only `at.y` replaced by the ground height, and `_drop` is the
authored `VCargo_target` read at `Awake`. **The honest bound is therefore very tight — the XZ should not move at
all.**

**Proposed diff in `Server/Spawner.cs`** (Wu'barrk's file). **Landed in PR #18** with two changes of his: the
decision moved into `Core/FlightPlan.DropAccepted` so the off-game harness proves it (three checks under
"FlightPlan: the drop the pilot reports is bounded by the drop the server authored"), and the comparison spelled
`!(d <= tolerance)` rather than `d > tolerance`, because every comparison against NaN is false and the natural
spelling *accepts* a forged NaN into the session row, the wire and the sidecar. `Spawner.Tick` (`:234`) now
calls it against `AuthoredDrop`. The diff as it was proposed:

```csharp
// Remember what was authored, so the value that comes back can be checked against it.
+        /// <summary>The drop the server authored. The pilot owns the bird and may write VCargo_target to
+        /// anything; this is what "anything" is checked against (P11's authority audit).</summary>
+        public static UnityEngine.Vector3 AuthoredDrop { get; private set; }
+        /// <summary>How far the reported drop may sit from the authored one. The legitimate delta is ZERO
+        /// in XZ -- CargoFlight.Drop writes back the authored point with only its y replaced by the ground
+        /// height -- so this is float noise plus a wide margin, not a policy.</summary>
+        public const float DropToleranceXZ = 8f;
+        public const float DropToleranceY = 64f;

 // in Author(), beside `Bird = ...`:
+                AuthoredDrop = drop;

 // in Tick(), replacing the unconditional SetDrop:
-                    Vector3 at = bird.GetVec3(TargetHash, Vector3.zero);
-                    string s = session.SetDrop(at.x, at.y, at.z);
+                    Vector3 at = bird.GetVec3(TargetHash, AuthoredDrop);
+                    float dxz = new Vector2(at.x - AuthoredDrop.x, at.z - AuthoredDrop.z).magnitude;
+                    if (dxz > DropToleranceXZ || Mathf.Abs(at.y - AuthoredDrop.y) > DropToleranceY)
+                    {
+                        ValkyriesCargo.Log.LogWarning("visit #" + session.VisitId + ": the pilot reported a drop at " +
+                                                      Wire.Float(dxz) + " m from the authored one; keeping the authored point");
+                        at = AuthoredDrop;
+                    }
+                    string s = session.SetDrop(at.x, at.y, at.z);
```

`Clear()` should reset `AuthoredDrop` with the rest. The alternative bound is
`FlightPlan.PointInBlockWithMargin(at.x, at.z, pilotX, pilotZ, activeArea)` — the constraint the whole flight
already lives under — but it is looser and needs the pilot's live position, so the authored-drop tolerance is
the one proposed.

**Not proposed, deliberately:** nothing to bound `VCargo_dropped` itself. A pilot flipping it early gets a merchant
on the ground sooner, at the authored point once the diff above lands, and the visit's clock is the event's,
not the flight's. That is a worse show for the pilot and nothing else.

### The bird's other keys — `VCargo_cargo`, `VCargo_turn` (owner: the pilot)

Both are read only on the client side, and rewriting them is **self-harm**:

- **`VCargo_turn`** is the descent waypoint `CargoFlight` reads at `Awake`. Only the owner flies; every other
  machine is moved by `ZSyncTransform`. A pilot who rewrites it flies its own bird badly.
- **`VCargo_cargo`** is what `Patch_Valkyrie_Awake` tests to decide "this is ours" (`Patch_Valkyrie_Awake.cs:43`).
  Zeroing it makes vanilla `Valkyrie.Awake` run on every machine that instantiates the bird — but vanilla's
  body returns at `if (!m_nview.IsOwner()) { enabled = false; return; }` (decompile, `Valkyrie.cs:49`), so the
  teleport of `Player.m_localPlayer` reaches **only the pilot itself**. The residual on other machines is the
  one thing that happens before that guard, `m_instance = this`: a bystander whose `Valkyrie.m_instance` is
  now our bird would be dropped from it by `Game.SkipIntro`, which only a player skipping a new character's
  intro at that exact moment can reach. The patch's own header already records this. Accepted.

### P5's `VCargo_state` (owner: the pilot, then whichever client's block holds the merchant)

`Server/Spawner.cs:299`–`306` defines `Carried / Approaching / Trading / Leaving`. The merchant's ZDO is
**persistent** and vanilla hands a persistent ZDO to a nearer client when the owner walks away, so the writer
of `VCargo_state` is "some client", not even reliably the pilot.

Nothing on the server reads `VCargo_state`, and P5 as merged keeps it so: the server *writes* it
(`Spawner.cs:171` at authoring, `:356` on adoption) and only `CargoMerchant` (`:94`, `:232`, `:284`) and
`CargoFlight` (`:218`) read or write it, on clients. **It must stay that way**: the visit's phase already has a
server-owned home in `VisitState`, written by `VisitSession.SetPhase` from the director. `VCargo_state` is a
*client-to-client* rendering hint — what pose the merchant is in on each screen — and treating it as an input
to a server decision would hand the visit's state machine to whoever happens to own the ZDO.

**Recorded as a `docs/DESIGN.md` section 8 row on this branch** (locked; verified against P5 as merged), so the
rule is not rediscovered.

### P5's immortality is a convention among honest clients — built now, at `RPC_Damage`

Read ahead for 11d, and it belongs here: **`Character.Damage(HitData)` is a bare forwarder**. Its whole body
is `if (m_nview.IsValid()) { hit.m_weakSpot = FindWeakSpotIndex(...); m_nview.InvokeRPC("RPC_Damage", hit); }`
(decompile, `Character.cs:1883`). It applies nothing and checks no ownership; the work is in the private
`RPC_Damage`, which runs on the owner.

P5 as merged patches the private `RPC_Damage` itself (`Patches/Patch_Character_Damage.cs`; PR #14's
correction: immortality belongs where the damage is applied, on the owner), so a modified client that invokes
`RPC_Damage` on his `ZNetView` directly still lands in a patched handler on the machine that owns him. What
remains is the owner: whichever client's block holds the persistent merchant owns his ZDO, and if that client
is the modified one, its own unpatched handler is the one that runs. Ingvar's immortality is therefore still
**a convention among honest clients** (`ModRequired`), not a server-authoritative fact. That is fine for a
merchant who lives 300 s and is destroyed by the server at the end either way, and `Server/Spawner.Clear`
(`SetOwner` + `DestroyZDO`) stays the thing that actually guarantees he goes away. The other paths to death
(`SetHealth`, fall damage, drowning, status effects through `SEMan`) are the P4/P5 adversarial audit's (P11d).

---

## 4. The trust table, one page

| The server believes | Verified how | What it cannot verify | Worst case, and the bound |
|---|---|---|---|
| every client runs this exact DLL | ServerSync `VersionCheck` at handshake, `ModRequired`, min == current | that the DLL is **unmodified** | a patched client. Everything below is written assuming one |
| `Server.*` config is the server's | locked; a client neither sends (`Broadcast` no-ops) nor is heard (socket vs `adminlist.txt`); the value is restored on disconnect | — for the server. **Not** that another client cannot push values *client-to-client* | display-only keys and the two channels; not sticky, overwritten by the server's next write. Upstream one-liner proposed |
| a config packet's sender | **the socket** (`SnatchCurrentlyHandlingRPC` → `GetSocket().GetHostName()`) | — | none |
| an admin is an admin | **the socket**, via `ZNet.IsAdmin(hostName)`, fail closed — after this branch | — | none. Before this branch: any client could forge it |
| a deal's sender | **the socket**; the ledger key is `m_socket.GetHostName()` | — | none. A client cannot act as, or ack for, another |
| a deal is priced right | every price recomputed server-side and compared to `UnitPriceSeen` | — | none |
| a deal is not a replay | 500-nonce ring, per visit, plus the visit id | — | none reachable in 300 s |
| his shelf | `sold_out` and `over_max` against the catalogue | — | stock stays inside 0..`MaxStock` |
| **his purse** | `purse_empty` before the commit | **that the player owns the coins or the goods** | **one purse per visit** (`PurseCoins` + carry, ≤ 3×), and a shelf full of goods nobody carried in |
| a player is rested, comfortable, based and alive | not verified | all of it — comfort is computed on the client and vanilla writes `baseValue` the same way | an undeserved visit, on a cooldown the liar spends. **Accepted, DESIGN §8** |
| where a player is | not verified — the client owns its ZDO and reports its own `m_refPos` | all of it | a visit at a place nobody stands; a `VCargo_dismiss` from someone who claims to be near. **Accepted** |
| the visit is running, and its clock | vanilla `RandEventSystem` on the server; `VisitSession` mirrors it | — | none: a client's copy of the clock is display |
| where the merchant is dropped | the drop the pilot reports is checked against the drop the server authored: 8 m in XZ, 64 m in Y, NaN refused (`FlightPlan.DropAccepted`, `Spawner.Tick`; PR #18) | that the pilot flips `VCargo_dropped` early | a merchant on the ground sooner, at the authored point. **Fixed** (was: anywhere in the world) |
| the bird is ours | `VCargo_cargo` on a ZDO the pilot owns | that the pilot did not clear it | vanilla `Valkyrie.Awake` on the pilot's own machine only; `m_instance` on others. Accepted |
| a visit may be ended | the visit id **and** the caller within 96 m of the drop — after this branch | that the caller is really there | a modified client can still claim to be. **Accepted**; before this branch, anyone anywhere |
| what a client did with its inventory | not verified, ever | all of it | it is vanilla's model, not ours: `DealApplier` is the only writer and it runs on the player's own machine after the server's answer |

---

## 5. What is left, in one list

**Fixed here** (three commits, all in files owned this round):

1. `VCargo_admin`'s caller is the socket, not the uid the client wrote — privilege escalation.
2. `VCargo_dismiss` only from a client at the visit — griefing lever.
3. Three client-driven paths that had no bound: the `VCargo_deal` parse warning, the refused-deal log, and
   `VCargo_claim`'s 1-packet-in / 50-packets-out amplifier.

**Proposed, in files not ours this round** — `docs/CONFIG-SHAKEDOWN.md` carries the config-document diffs;
these are the authority ones:

4. ~~`Server/Spawner.cs` — bound the drop point the pilot reports.~~ **DONE by Wu'barrk in PR #18**
   (`FlightPlan.DropAccepted`, NaN-safe, three harness checks), before P5 landed, as asked.
5. `Libs/ServerSync.cs` — upstream one-liner refusing a client-to-client config package on a client. Raise
   with blaxxun; do not patch the vendored file.
6. `Patches/Patch_Terminal.cs`, `cargo status` — **the admin-wire wording is done on this branch**
   (`admin wire up (direct peer ZRpc)`). The refusal counter (`VisitDirector.Refusals`, because after this
   branch only the first three refusals reach the log) has no `cargo status` line yet: the server's block
   prints no deal counters at all today, and it goes in with the first one.
7. `Net/CargoTransport.cs` — `LocalTransport.Dismiss` has no distance check. Host-only, so it is a
   consistency fix rather than a hole.

**Proposed checks for the harness** (`tests/` is another agent's this round):

8. ~~`OwedLedger.Ack` with another player's key must return false and leave the row~~ — already in the
   harness (`OwedLedgerTests`: "another player cannot ack it").
9. **Added on this branch**: `Market.Settle` on a sell from a client claiming `int.MaxValue` coins is still
   refused `purse_empty` once the purse runs out — the property the whole "coins are advisory" decision rests
   on (`Market.Settle` section, after the 800-coin refusal).
10. ~~`over_max` at exactly `MaxStock` from an oversized offer~~ — already in the harness (the 401st log is
    `over_max`; `int.MaxValue` units of Wood is `over_max`, not a wrapped negative).
11. The range/literal check from `docs/CONFIG-SHAKEDOWN.md`, as a source-text tool rather than a unit test.
    Still open.

**Runtime proofs that only a client can settle**: `docs/PROOF-CLIENT.md` items 4 (the lock), 12 (the gate),
13, 14, 16 (the deal wire and the ledger) and the two-client items; tracked in `docs/TODO.md` section 1.
