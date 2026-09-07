# Valheim Dedicated-Server Facts (verified against decompiled source)

All line numbers refer to `libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs` (the CLIENT assembly, `IsDedicated() => false` at L68818). **The SERVER build's decompile now sits beside it** as `assembly_valheim_SERVER.decompiled.cs` (from the test bed's install; `IsDedicated() => true` at its L68414, ownership/character-ZDO APIs identical) — diff against it when a fact needs re-proving on the server build.

**Runtime-measured ground truth** (headless boot, Valheim server + BepInEx, 2026-era build): `ZoneSystem.m_activeArea = 2` (serialized prefab value; the C# initializer's `1` is NOT what ships) → instance/ownership active area = the 5×5 zone block around a position, and the `ReleaseNearbyZDOS` handover ring (`activeArea-1`) = 1 zone. `SystemInfo.graphicsDeviceType == Null` detection and per-instance RPC registration both verified working on the real server binary.

## Instances and zones

- **Instances only exist near `ZNet.GetReferencePosition()`** — ZNetScene.CreateDestroyObjects (L69929) / CreateObjectsSorted (L69766). On a dedicated server the reference position is only ever set by `Game.FindSpawnPoint` fallbacks (L85793-85847) → **~world origin, never near players**. `SetReferencePosition` has 6 call sites, all client-side.
- **RemoveObjects destroys the GameObject and KEEPS a persistent ZDO** (L69878-86). A **non-persistent + owned** ZDO is `DestroyZDO`'d at unload — i.e. non-persistent things despawn when their owner walks away.
- **ZoneSystem.Update** (L98652-68): server runs `CreateLocalZones(own refpos)` → REAL zones (terrain Heightmap + MeshCollider, L98791/110779-89) near origin only; `CreateGhostZones(peer refpos)` → **ZDO-only one-shot world generation**: location objects run `Awake` via ghost-init (ZNetView L70291-98) but the root is destroyed the same frame → **`Start()` never runs in a ghost zone**. A dedicated server therefore has physics/heightmaps ONLY in the few zones around (0,0).
- `ZoneSystem.IsZoneLoaded(Vector3)` (L98760) = clean "is this area REAL on this machine" test. Guard any server-side `Physics.*` / `Resources.FindObjectsOfTypeAll` command logic with it.
- `ZoneSystem.GetGroundHeight` is a **physics raycast** (L99961/99972): without a loaded Heightmap the float overload silently returns your input Y; prefer the `out bool` overload and treat false as "no terrain". `WorldGenerator.instance.GetHeight` (L132281) is pure math and works anywhere server-side (but is meaningless far outside the ~10km world radius).

## Player visibility server-side

- **`ZNet.GetAllCharacterZDOS()` (L68699)** — THE server-side "where is every player" API: local character ZDO + every ready peer's `m_characterID` ZDO. Position AND rotation are first-class ZDO fields (`GetPosition` L62625, `GetRotation` L62630) kept fresh every physics tick by the owner's `ZSyncTransform.OwnerSync` (L75111).
- `ZNetPeer.m_refPos` (L66783) — always populated, updated every 2 s (`SendPeriodicData` L68185), **NOT gated** by the map-visibility toggle. ~2 s stale.
- **`ZNet.GetPlayerList()` positions ARE gated** by `m_publicPosition` (L68952-56) — they read `(0,0,0)` for players with the map toggle off. Never use for game logic.
- `Player.GetAllPlayers()` / `Player.IsPlayerInRange` iterate the **local instance list** — empty on a dedicated server, and on a player-host only players near the host. Never authoritative.

## Ownership (the load-bearing rules)

- The ONLY generic ownership assignment is **server-driven**: `ZDOMan.ReleaseZDOS` every 2 s (L65242/65301) → `ReleaseNearbyZDOS` (L65330-54): for each **PERSISTENT** ZDO near own refpos + each peer refpos — owner left the area → `SetOwner(0)`; unowned/absent-owner in a peer's area → `SetOwner(peerUid)`. Handover ring = `m_activeArea - 1` zones (L65335; `m_activeArea` is a serialized prefab field — code default 1, read it at runtime). Latency ≤ ~2.5 s.
- **`!Persistent` ZDOs are SKIPPED entirely** (L65338): a non-persistent ZDO created by the server is NEVER handed to a client → never simulates, never culls, leaks until restart (`RemoveOrphanNonPersistentZDOS` L65817 only reaps on peer disconnect). **Server-spawned creatures must be persistent** — vanilla monster prefabs are (that's why creatures save with the world).
- `ZNetView.Awake` does NOT claim ownership on instantiate; `ClaimOwnership` is explicit. A client instantiating an unowned ZDO waits for the server's sweep — or you `zdo.SetOwner(targetUid)` at creation time and the target simulates on its next objects update (skip the ~2.5 s statue window).
- `ZDOMan.DestroyZDO` only acts if you OWN the zdo — claim first (`SetOwner(ZDOMan.GetSessionID())`) or it is a silent no-op.
- Sectors beyond ±256 zones (world > ~16 km) live in `m_objectsByOutsideSector` (L65147) — fully functional fallback container; `FindSectorObjects` handles it.

## Death, drops, sync

- Mob death runs on the **OWNER**: `RPC_Damage` (owner-gated L8712) → `CheckDeath` → `Character.OnDeath` (L9251, owner-gated L9276) → `m_onDeath` (**CharacterDrop.OnDeath generates loot HERE**, L11374) → `ZNetScene.Destroy` → `DestroyZDO` broadcast (L65357-91). Consequences: (1) custom drop-table entries must exist on the instance of WHOEVER owns the mob at kill time — attach them on every client, not just the spawner; (2) "the tracked ZDO vanished" is a reliable remote death signal.
- `Character.SetMaxHealth` writes `ZDOVars.s_maxHealth`; **`GetMaxHealth` reads it back live** (L9372-88) → max health is replicated state; multiplying it per-machine COMPOUNDS on the shared value. `SetLevel` → `SetupMaxHealth` = `SetMaxHealth(base*level)` — **resets any custom max health**; `SetMaxHealth` only clamps current health down, never heals up.
- `CharacterDrop.Drop.m_levelMultiplier` **defaults true** (L11342) — multiplies chance AND amount by 2^(level-1). Set false on hand-tuned drops.

## Routed RPCs

- Param types (`ZRpc.Serialize` L71603): int, uint, long, float, double, bool, string, ZPackage, List<string>, Vector3, Quaternion, ZDOID, HitData, ISerializableParameter. **No arrays/enums** — box or use ZPackage. Unsupported types are **silently dropped** → receiver deserializes garbage.
- `InvokeRoutedRPC(name, ...)` auto-targets the server (L71170); `(sender, name, ...)` replies to a peer; **`Everybody` (0L) also invokes the handler LOCALLY on the caller** (L71198-201) — a player-host participates in its own broadcasts.
- `Register` overloads for 0-6 typed params (L71278-311). Registration is `Dictionary.Add` on a **per-world-session** ZRoutedRpc instance: (1) same-name double-register THROWS, (2) a stale "already registered" bool from the previous world leaves you with NO handlers in the next one — key your registration on the instance reference. RPC names share ONE global hash namespace with the base game and every mod — **prefix with your plugin GUID**.

## Console on dedicated

- `Console.instance` exists headless (vanilla `ZNet.InternalCommand` L69554 dereferences it unguarded — remote admin commands route through it), but output may never surface on the headless console — mirror prints to your logger. Handlers touching `Player.m_localPlayer` / `Hud` / `Chat` / `MessageHud` must null-guard.

## Headless detection

- `SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null` — compile-independent (works when built against client reference DLLs, where `ZNet.IsDedicated()` is hardcoded false).

## The pattern that follows from all of this

**The server decides, the nearest machine executes.** Server-side logic operates on ZDO data only (positions from character ZDOs, liveness from ZDO existence, placement from replicated world data instead of physics); anything needing physics/heightmaps/HUD/teleports runs on a client, coordinated by GUID-prefixed routed RPCs. Spawn flow that works headless: server `Instantiate`s (ZDO created + mutations written while server owns it) → `zdo.SetOwner(nearestPlayerUid)` LAST → server instance is culled harmlessly → the player's client instantiates from the ZDO and simulates; component-level mutations are rebuilt on every client from replicated ZDO flags in a `Character.Awake` patch. Reference implementation: Mists of Avalor 0.1.0 (`IMPLEMENTATIONS\MistsofAvalor.md`).

---

## Chat does not reach the server when there is only one player (verified 2026-08-05, VikingOS MP pass)

A shout is not broadcast. `Chat.SendText` goes through `CheckPermissionsAndSendChatMessageRPCsAsync`
(decompile 34582), which sends the message **once per PLAYER** in `ZNet.instance.GetPlayerList()`:

```csharp
sendMessageHandler?.Invoke(ZNet.instance.LocalPlayerCharacterID.UserID, filterText: false);  // yourself
foreach (var p in ZNet.instance.GetPlayerList()) { ... sendMessageHandler(p.m_characterID.UserID, ...); }
```

**A dedicated server is not a player**, so it is never a recipient. And `InvokeRoutedRPC` (70934) is:

```csharp
if (targetPeerID == m_id || targetPeerID == 0L) HandleRoutedRPC(data);   // handled locally
if (targetPeerID != m_id)                       RouteRPC(data);          // sent - SKIPPED when it is you
```

So with one player connected, the only recipient of your own shout is yourself, `targetPeerID == m_id`,
and **RouteRPC is never called - nothing crosses the wire at all**. A server-side hook on
`RPC_RoutedRPC` or `Chat.OnNewChatMessage` logs literally nothing in a solo test, and it is not broken.

Consequences for anything that wants to see chat:

| Where | Hook | Sees |
|---|---|---|
| Any machine | `ZRoutedRpc.HandleRoutedRPC(RoutedRPCData)` | its own messages, and anything addressed to it |
| Any machine | `ZRoutedRpc.RouteRPC(RoutedRPCData)` | what it sends, and on a server what it relays onward |

Use BOTH and deduplicate on `RoutedRPCData.m_msgID` (unique per sender per message). Both take decoded
data, so no raw ZPackage parsing is needed; if you do read `m_parameters`, save and restore `GetPos()`
around it, because vanilla is not necessarily finished with the buffer.

`Talker.Type` chat parameters are untagged and positional (`ZRpc.Serialize`, 71349):
`Vector3 pos, int type, UserInfo (string name, string platformId), string text`.

**Broadcasting FROM a headless server works**, and is the only direction that does with one player:
`ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "ChatMessage", pos, (int)Talker.Type.Shout,
new UserInfo { Name = "SERVER", UserId = PlatformUserID.None }, text)`. Target 0 means it is handled
locally *and* routed to every peer - so do not also log it by hand or it appears twice.

## A clean shutdown can outlast a 60s wait

`GenerateConsoleCtrlEvent` returns immediately; the process may sit for minutes after the world has
already been written. **Check the save, not the process**: `saves-*/worlds_local/<World>.db` updated and
`.db.old` / `.fwl.old` rotated means the save completed and `Stop-Process` is then safe.

## A Jotunn `ConsoleCommand` runs where it was TYPED, not on the server (verified 2026-08-11, Avalor 0.1.11)

**`OnlyServer = true` gates PERMISSION, not LOCATION. `IsNetwork = true` does not change it either.** The
command body executes on the machine the command was typed into, so an admin on a client who runs a
world-mutating command mutates **their own copy of the world** and the dedicated server runs none of it.

**How to spot it in a server log:** Valheim logs the raw command text from a remote admin
(`<steamid>/<name> (<pos>): <cmd>` — that is `ZNet.InternalCommand` routing through `Console.instance`,
see *Console on dedicated* above). If that line appears and **none of the command's own log lines follow**,
it ran on the client. Avalor's server logged `avalor_reset` and then nothing — no command line, no reset-manager
line, no generator line — while the client silently built an entire new labyrinth.

**Fix shape, every time:** the command detects "am I a client", fires a routed RPC, and returns; the
server-side handler re-checks `ZNet.instance.IsServer()` and does the work. `InvokeRoutedRPC(name, args)`
with no peer id auto-targets the server (see *Routed RPCs*).

**The second-order damage is the expensive part** — a misrouted command does not fail, it succeeds in the
wrong place, and every piece of *server* state that should have updated silently does not:

- Avalor's end boss stopped spawning. The mob director is server-side, and the only thing that re-armed a
  beaten boss was a wipe notification raised inside the wipe function — which ran on the client. The server's
  "already beaten" flag stayed latched from the previous maze and the spawn check returned early forever.
- The "is anyone still inside before I delete the floor?" test was answered from a client's **partial** ZDO
  table rather than the server's complete one. A wrong answer there drops every player 500m.

**The durable rule:** never let an in-memory flag be the only record of *which world/instance* a piece of
state describes. Bind it to a ZDO whose lifetime matches the thing (Avalor keys boss state to the exit
portal's ZDOID), so any teardown — including one this machine is never told about, by another mod or a
misrouted command — invalidates it for free. Two independent "first match in the ZDO table" searches for the
same logical object are also a trap: dictionary order is not guaranteed stable, so resolve the id once and
pass it, or the two can disagree and flap.

## Player stats and skills are local-profile-only — same "no remote access" rule as everything else (verified 2026-08-19, TheRavensCall/WhereTheCrowFlies pass)

Valheim's ~105 lifetime `PlayerStatType` counters (kills, hits, deaths, jumps, cheats, world loads,
PvP hits/kills, arrows shot, portals, distance traveled — everything on a player's own in-game Stats
screen) live on `PlayerProfile`, a **public, no-reflection-needed** object:

```csharp
// PlayerProfile.PlayerStats — public nested class, public indexer
public class PlayerStats
{
    public Dictionary<PlayerStatType, float> m_stats = new Dictionary<PlayerStatType, float>();
    public float this[PlayerStatType type] { get => m_stats[type]; set => m_stats[type] = value; }
}
public readonly PlayerStats m_playerStats; // on PlayerProfile

float value = Game.instance.GetPlayerProfile().m_playerStats[PlayerStatType.EnemyKills]; // current absolute value
```

`PlayerProfile.IncrementStat(PlayerStatType stat, float amount = 1f)` just does
`m_playerStats[stat] += amount` on that same object — reading the indexer directly gets live ground
truth, the same object Valheim's own `stats` debug console command reads (`assembly_valheim.decompiled.cs`,
the `stats` command iterates `Game.instance.GetPlayerProfile().m_playerStats.m_stats`). Vanilla has **no
in-game Stats GUI panel** — that iteration in the debug command and a `MasterClient.SendStats` telemetry
ping to Valheim's own analytics server are the only two vanilla readers.

Skill levels (`Skills.SkillType`, e.g. `Swords`, `Bows`, `Jump`, `Sneak`) are the same shape: **public**
`Player.GetSkillLevel(SkillType)` / `Player.GetSkillFactor(SkillType)` (0-100 level, 0-1
progress-to-next-level), or `Player.GetSkills().GetSkillList()` for "every skill actually raised" without
querying each type individually.

**Both are local-profile-only, same rule as everywhere else in this doc**: `Game.m_playerProfile` is
populated from the local client's own save file (`LoadPlayerProfile`) and skills live on the `Player`
instance — there is no RPC, ZDO, or any other path in either the client or server assembly that exposes
another player's stats or skills to anyone but themselves. **A dedicated server cannot read this for any
connected player, full stop** — same as gear/inventory (see `Player.GetAllPlayers()` being empty
headless, noted throughout this doc). The only way to get this data server-side is a client report (a
routed RPC the client sends about itself), same pattern as everything else under *Routed RPCs* above.

**The one non-obvious trap if you build a report pipeline for this:** these are cumulative lifetime
totals, not events. A pure delta-accumulator (hooking `IncrementStat`/skill-gain and summing deltas
server-side) starts every player at 0 and only grows from activity observed *after* your mod goes live —
it can never recover whatever a player had already earned, and a single dropped packet causes permanent,
uncorrectable drift from the true value forever after. Send periodic **absolute snapshots**
(`m_playerStats[stat]` / `GetSkillLevel(type)` read fresh each time, assigned not accumulated
server-side) instead — self-backfills existing history on the first sync, self-heals from any dropped
packet on the next one. A low-frequency delta stream on top of that is still fine for near-real-time
liveness between snapshots (accumulate it into the same field), since the next absolute snapshot always
overwrites and corrects whatever the deltas drifted to — but the deltas alone are never the source of truth.
