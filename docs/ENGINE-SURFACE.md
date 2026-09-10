# The engine surface

Every Valheim type and member Valkyrie's Cargo depends on, in one machine-readable list. This is
the filter `tools/diff-engine.js` applies to a decompile-versus-decompile diff, which is what makes
a comparison of two builds a page instead of a hundred thousand lines. P10a; the brief is
`docs/P10-P11-FOR-DON.md`.

**The dependency is not the API surface.** A rename fails the build and announces itself. What this
list exists for is the other kind: **a method body that changes without its signature moving**, which
neither the build nor the 1078 off-game checks would say a word about. Roughly half the rows below
are members this mod never calls — they are bodies a recorded decision rests on
(`CLAUDE.md` "Engine facts the code relies on today", `docs/DESIGN.md` §0 and §3).

## Regenerating the raw list

```bash
grep -ohE '\b(ZDOMan|ZNetScene|ZNetView|ZDO|ZDOID|ZDOVars|ZNet|ZRpc|ZRoutedRpc|ZPackage|ZoneSystem|ZSyncTransform|EnvMan|RandEventSystem|RandomEvent|Valkyrie|Character|Humanoid|MonsterAI|BaseAI|Player|Inventory|ItemDrop|ObjectDB|Localization|MessageHud|Chat|NpcTalk|Odin|Tameable|SEMan|Terminal|Game|GameCamera|Minimap)\.[A-Za-z_][A-Za-z0-9_]*' \
     --include=*.cs -r ValkyriesCargo/ | sort -u
```

**The curation below is by hand, and the raw list is a starting point, not the answer.** That grep
sees only `Type.member`, so on 2026-09-07 it returned 81 lines of which five were false hits
(`Character.cs`, `NpcTalk.cs`, `RandEventSystem.cs`, `ZNet.cs` from prose; `Terminal.CargoTerminal`,
which is ours), and it could not see any of:

- **instance calls** — `zdo.GetInt(...)`, `p.GetInventory()`, `nview.IsOwner()`, `inv.CountItems(...)`,
  `peer.m_socket.GetHostName()`. Most of what this mod actually does is here.
- **Harmony patch targets** — three of ours plus five in the two vendored files, named in an
  attribute rather than in an expression.
- **members reached by reflection from `Libs/ServerSync.cs`** — `ZNet.m_adminList`,
  `ZNet.ListContainsId`, `ZRoutedRpc.m_peers`, `ZRpc.m_socket`, `ZRpc.m_functions`,
  `ZNetPeer.m_socket`, `ZNet.GetPeer(ZRpc)`, `ZNet.m_connectionStatus`. Blaxxun's file, blaxxun's
  reflection, and a rename there is a silent failure inside somebody else's code.
- **types the regex never listed** — `ZInput`, `InventoryGui`, `Menu`, `World`, `FileHelpers`,
  `Heightmap`, `ZNetPeer`, `ISocket`, `PlayerProfile`, `Trader`, `EffectList`, `FejdStartup`,
  `ZPlayFabSocket`, `StringExtensionMethods`, `Version`, `SE_Rested`, `ZSyncAnimation`,
  `VisEquipment`, `FootStep`, `RandomAnimation`, `Projectile`, `Console`.

There are no `___field` injections anywhere this mod reaches, P5 included: `grep -rnoE
'___[A-Za-z_][A-Za-z0-9_]*'` over `ValkyriesCargo/` on `origin/b/p5-merchant` returns nothing, same
as main. All six patches - the three already merged plus P5's `Patch_Humanoid_Awake`,
`Patch_Character_InIntro` and `Patch_Character_Damage` (which patches `Character.RPC_Damage`, not
`Damage` - see the row below) - take `__instance`, `__runOriginal` and `__result` only. This
paragraph used to predict P5 would need one; checked directly against P5's own tree, it does not.

Four types are named only as a `GetComponent<T>()` type argument in `cargo prefab`'s dump —
`MonsterAI`, `NpcTalk`, `Tameable`, `ZSyncAnimation` — so only their existence matters, and a build
failure catches that. They have rows below only where a BODY of theirs is also load bearing.

## Format

One member per line: `Type.Member | assembly | kind | note`. Nested types and their members carry
the full path (`ZDO.ObjectType`, `ItemDrop.ItemData.SharedData.m_name`). A member is named on the
type that DECLARES it, not the type the code calls it through — `Humanoid.GetInventory`, though
every call site says `player.GetInventory()`. Overloads are one row: the diff tool folds every
overload of a name together, so a change in any of them is reported and an overload appearing or
disappearing reads as a signature change.

Two things to know when reading a report. **For a field the initialiser is part of the
declaration**, so `m_dayLengthSec = 1200` becoming `= 1500` arrives as "signature changed" rather
than "body changed" — right, because a field has no body, but not what the word suggests. And a
row that resolves in NEITHER tree is reported separately as a *manifest problem*: it is this file
being wrong, not the engine moving. Three rows were wrong on the first run and the tool found all
three (`ZDOID.IsNone` — a real member the extractor was losing behind an `operator ==`;
`RandEventSystem.Update`, which is `RandomEvent.Update` plus `SendCurrentRandomEvent` plus
`SetActiveEvent`; and `Player.GetInventory`, declared on `Humanoid`).

| kind | meaning |
|---|---|
| `call` | this mod calls or reads it |
| `patch` | a Harmony target of ours (`ValkyriesCargo/Patches/`) |
| `patch-vendored` | a Harmony target inside `Libs/ServerSync.cs` or `Libs/SharedUI/UIFocus.cs` — NOT OURS |
| `reflect-vendored` | reached by reflection from a vendored file — NOT OURS, and invisible to the compiler |
| `type` | a nested type named in code |
| `fact` | never called: a body a recorded decision rests on. **The dangerous rows.** |

## The manifest

```
ZDO.ObjectType | assembly_valheim | type | Spawner writes Type = Prioritized on the bird
ZDO.Type | assembly_valheim | call | Spawner.Author; a property, not a setter
ZDO.Persistent | assembly_valheim | call | bird false, merchant true; DESIGN 3.2
ZDO.Distant | assembly_valheim | call | Spawner.Author, both ZDOs false
ZDO.m_uid | assembly_valheim | call | Spawner, CargoFlight.FindMerchantByCarrier
ZDO.SetPrefab | assembly_valheim | call | Spawner.Author - CreateNewZDO does NOT do this
ZDO.SetPosition | assembly_valheim | call | Spawner.Author
ZDO.SetRotation | assembly_valheim | call | Spawner.Author
ZDO.SetOwner | assembly_valheim | call | Spawner.Author (last, to the pilot) and Reclaim (to the server)
ZDO.GetOwner | assembly_valheim | call | VisitDirector.Gather; the owner id is the peer uid
ZDO.GetPosition | assembly_valheim | call | VisitDirector.Gather
ZDO.IsValid | assembly_valheim | call | Spawner.Tick, Reclaim, CargoFlight.Drop
ZDO.Set | assembly_valheim | call | all overloads: int, bool, Vector3, ZDOID; ComfortReporter, Spawner, CargoFlight
ZDO.GetInt | assembly_valheim | call | VCargo_cargo, VCargo_comfort, baseValue
ZDO.GetBool | assembly_valheim | call | VCargo_dropped, VCargo_rested, dead
ZDO.GetVec3 | assembly_valheim | call | VCargo_target, VCargo_turn
ZDO.GetString | assembly_valheim | call | playerName
ZDO.GetZDOID | assembly_valheim | call | VCargo_carrier, in CargoFlight.FindMerchantByCarrier
ZDO.GetHashZDOID | assembly_valheim | call | Spawner.CarrierKey: the two-int key pair a ZDOID is stored as
ZDOID.None | assembly_valheim | call | Spawner, CargoFlight
ZDOID.IsNone | assembly_valheim | call | Spawner.Tick, Reclaim
ZDOMan.instance | assembly_valheim | call | Spawner, CargoFlight
ZDOMan.CreateNewZDO | assembly_valheim | call | Spawner.Author; the hash is used ONLY for the portal list
ZDOMan.GetZDO | assembly_valheim | call | Spawner.Tick and Reclaim
ZDOMan.DestroyZDO | assembly_valheim | call | Spawner.Reclaim; a NO-OP for a non-owner, hence SetOwner first
ZDOMan.GetSessionID | assembly_valheim | call | Spawner.Reclaim; == ZNet.GetUID()
ZDOMan.RemoveOrphanNonPersistentZDOS | assembly_valheim | fact | the bird's other lifetime rule: a disconnected owner is swept
ZDOMan.ReleaseZDOS | assembly_valheim | fact | every 2 s on the server: one ReleaseNearbyZDOS per peer
ZDOMan.ReleaseNearbyZDOS | assembly_valheim | fact | D5: a persistent claim outside the owner's area is stripped, an unowned one inside it is taken; Core/ZoneOwnership.cs
ZDOMan.IsInPeerActiveArea | assembly_valheim | fact | the grant branch's "is its owner still near it"
ZDOMan.FindSectorObjects | assembly_valheim | fact | the sweep's candidate set: the near zones, all of them on classic
ZDOVars.s_playerName | assembly_valheim | call | VisitDirector.Gather
ZDOVars.s_baseValue | assembly_valheim | call | VisitDirector.Gather; the eligibility gate
ZDOVars.s_dead | assembly_valheim | call | VisitDirector.Gather
ZDOVars.s_velHash | assembly_valheim | call | CargoFlight.Fly writes it so every other screen dead-reckons a glide
ZNet.instance | assembly_valheim | call | everywhere
ZNet.IsServer | assembly_valheim | call | CargoTick.Role, DealWire, AdminRpc
ZNet.IsDedicated | assembly_valheim | fact | NOT used: HasRenderer is graphicsDeviceType, because the client reference assembly hardcodes this false
ZNet.IsAdmin | assembly_valheim | call | AdminGate.Check - the ONE method naming the game's admin API; fail closed
ZNet.GetUID | assembly_valheim | call | Patch_Terminal.Admin, CargoTick.PilotLine
ZNet.GetWorldUID | assembly_valheim | call | MarketStore.Resolve and the delivery-id salt
ZNet.GetTimeSeconds | assembly_valheim | call | the world clock the visit and the market drift count
ZNet.GetPeers | assembly_valheim | call | DealWire.Tick and PeerFor
ZNet.GetPeer | assembly_valheim | call | AdminRpc.OnRequest (long overload); ServerSync reflects the ZRpc one
ZNet.GetSyncedSimulationDistance | assembly_valheim | call | ActiveAreaLive.Read (1.0): the server's own setting, or the value it validated for this client
ZNet.m_simulationDistance | assembly_valheim | fact | set by RPC_ValidatedSimulationDistance; what a client's area is sized by
ZNet.GetServerPeer | assembly_valheim | call | AdminRpc.Send
ZNet.GetServerRPC | assembly_valheim | call | CargoTransport.EnsureRegistered; one per connection
ZNet.GetAllCharacterZDOS | assembly_valheim | call | VisitDirector.Gather: the whole eligibility view
ZNet.Awake | assembly_valheim | patch-vendored | ServerSync RegisterRPCPatch
ZNet.OnNewConnection | assembly_valheim | patch-vendored | ServerSync
ZNet.Shutdown | assembly_valheim | patch-vendored | ServerSync
ZNet.RPC_PeerInfo | assembly_valheim | patch-vendored | ServerSync's buffering socket around the handshake
ZNet.Disconnect | assembly_valheim | patch-vendored | ServerSync VersionCheck
ZNet.m_adminList | assembly_valheim | reflect-vendored | ServerSync reads it to exempt admins from the config lock
ZNet.ListContainsId | assembly_valheim | reflect-vendored | ServerSync; private
ZNet.m_connectionStatus | assembly_valheim | reflect-vendored | ServerSync sets ErrorVersion on a version mismatch
ZNet.GetConnectionStatus | assembly_valheim | call | ServerSync's version wall
ZNet.ConnectionStatus | assembly_valheim | type | ServerSync
ZNet.m_onlineBackend | assembly_valheim | call | ServerSync skips the socket wrap for Steamworks
ZNetPeer.m_rpc | assembly_valheim | call | DealWire registers on each peer's own socket
ZNetPeer.m_uid | assembly_valheim | call | DealWire, AdminRpc
ZNetPeer.m_socket | assembly_valheim | call | AdminGate and DealWire.KeyFor
ZNetPeer.m_playerName | assembly_valheim | call | DealWire.Who, AdminRpc.OnRequest
ISocket.GetHostName | assembly_valheim | call | the platform id: the owed ledger's key, and the admin check's
ZRpc.Register | assembly_valheim | call | DealWire and CargoTransport; replaces by name, so repeating is safe
ZRpc.Invoke | assembly_valheim | call | VCargo_deal, VCargo_ack, VCargo_claim, VCargo_dismiss, VCargo_dealt
ZRpc.IsConnected | assembly_valheim | call | CargoTransport.Ready
ZRpc.HandlePackage | assembly_valheim | patch-vendored | ServerSync
ZRpc.Serialize | assembly_valheim | call | ServerSync
ZRpc.Deserialize | assembly_valheim | call | ServerSync
ZRpc.m_socket | assembly_valheim | reflect-vendored | ServerSync swaps in a buffering socket
ZRpc.m_functions | assembly_valheim | reflect-vendored | ServerSync replays buffered calls
ZRpc.GetSocket | assembly_valheim | call | ServerSync
ZRoutedRpc.instance | assembly_valheim | call | AdminRpc; NULL for the whole of plugin Awake and re-created per world
ZRoutedRpc.Register | assembly_valheim | call | AdminRpc.EnsureRegistered: VCargo_admin, VCargo_reply
ZRoutedRpc.InvokeRoutedRPC | assembly_valheim | call | AdminRpc.Send and the reply
ZRoutedRpc.Everybody | assembly_valheim | call | ServerSync
ZRoutedRpc.m_peers | assembly_valheim | reflect-vendored | ServerSync broadcasts config to every peer
ZPackage.Write | assembly_valheim | call | ServerSync
ZPackage.ReadInt | assembly_valheim | call | ServerSync
ZPackage.ReadLong | assembly_valheim | call | ServerSync
ZPackage.ReadString | assembly_valheim | call | ServerSync
ZPackage.ReadByte | assembly_valheim | call | ServerSync
ZPackage.ReadByteArray | assembly_valheim | call | ServerSync
ZPackage.GetArray | assembly_valheim | call | ServerSync
ZPackage.GetPos | assembly_valheim | call | ServerSync
ZPackage.SetPos | assembly_valheim | call | ServerSync
ZNetView.GetZDO | assembly_valheim | call | ComfortReporter, CargoFlight, Patch_Valkyrie_Awake
ZNetView.IsValid | assembly_valheim | call | ComfortReporter, CargoFlight
ZNetView.IsOwner | assembly_valheim | call | CargoFlight: only the owner flies
ZNetView.Destroy | assembly_valheim | call | CargoFlight, when the bird has left
ZNetView.m_persistent | assembly_valheim | call | cargo prefab dump
ZNetView.m_distant | assembly_valheim | call | cargo prefab dump
ZNetView.Awake | assembly_valheim | fact | THE authored-ZDO fact: the init-ZDO branch re-applies Type and Distant but NOT Persistent
ZNetView.m_initZDO | assembly_valheim | fact | where CreateObject parks the ZDO so our keys are there before any Awake
ZNetScene.instance | assembly_valheim | call | Spawner, Patch_Terminal
ZNetScene.HasPrefab | assembly_valheim | call | Spawner.Author refuses to author what clients cannot resolve
ZNetScene.GetPrefab | assembly_valheim | call | cargo prefab
ZNetScene.CreateObject | assembly_valheim | fact | parks the ZDO in ZNetView.m_initZDO before Awake
ZNetScene.InActiveArea | assembly_valheim | fact | 1.0: a point against a Vector2s zone or a Vector3 centre, in metres; Core/ActiveArea.cs
ZNetScene.PointInsideActiveArea | assembly_valheim | fact | 1.0, private: within 1.5 zones of the zone centre on both axes (1 at near 1; a strict 1.75-zone circle at near 2 off classic); Core/ActiveArea.cs
ZNetScene.OutsideActiveArea | assembly_valheim | fact | the destroy test of CreateDestroyObjects, the same rule negated
ZNetScene.RemoveObjects | assembly_valheim | fact | destroys the instance and a non-persistent owned ZDO with it
Hoverable.GetHoverText | assembly_valheim | call | CargoMerchant implements it; the interfaces probe counts the members
Hoverable.GetHoverName | assembly_valheim | call | CargoMerchant implements it
Hoverable.GetHoverOffset | assembly_valheim | call | 1.0's third member; CargoMerchant answers his character's (Character.GetHoverOffset)
Character.GetHoverOffset | assembly_valheim | call | CargoMerchant.GetHoverOffset: m_hoverOffset, the way vanilla reads every Character's
Interactable.Interact | assembly_valheim | call | CargoMerchant implements it
Interactable.UseItem | assembly_valheim | call | CargoMerchant implements it
ZNetScene.CreateObjectsSorted | assembly_valheim | fact | the destroy-unresolvable-prefab branch that the server's pinned position keeps away from us
ZoneSystem.instance | assembly_valheim | call | Spawner, CargoFlight, Patch_Terminal
ZoneSystem.m_waterLevel | assembly_valheim | call | CargoFlight.Floor: never carried below the sea
ZoneSystem.m_zoneSize | assembly_valheim | fact | 64; Core/ActiveArea.cs carries it as a constant
ZoneSystem.GetZone | assembly_valheim | fact | floor((v + zoneSize/2) / zoneSize), a Vector2s since 1.0; reimplemented in Core/ActiveArea.cs
ZoneSystem.GetZonePos | assembly_valheim | fact | zone * 64: the centre the 1.0 area is measured from; Core/ActiveArea.ZoneCentre
SimulationDistance.NearSimulationDistance | assembly_valheim | call | ActiveAreaLive.Read (1.0): the size of the active area
SimulationDistance.IsClassic | assembly_valheim | call | ActiveAreaLive.Read (1.0): the shape of it
SimulationDistance.OriginalDistance | assembly_valheim | fact | near 2 classic: a stock server's area is 0.221.12's 3x3 block
SimulationDistance.GetSimulationDistance | assembly_valheim | fact | the level table, near 1 to 5
ZoneSystem.GetGroundHeight | assembly_valheim | call | CargoFlight.Floor and cargo body preview
ZSyncTransform.OwnerSync | assembly_valheim | fact | writes s_velHash only when the velocity changes from its cache, so our write survives
ZSyncTransform.m_velocityCached | assembly_valheim | fact | starts at Vector3.negativeInfinity, which is why the first write always lands
ZSyncTransform.GetVelocity | assembly_valheim | fact | vanilla's Valkyrie reports zero: it reads a Rigidbody the prefab does not have
ZSyncAnimation.Awake | assembly_valheim | fact | root-scoped GetComponentInChildren<Animator>; BodyLoader's appended-last defence
EnvMan.instance | assembly_valheim | call | the day length; NEVER patched (house rule 4)
EnvMan.IsDay | assembly_valheim | call | static; the DaytimeOnly gate
EnvMan.m_dayLengthSec | assembly_valheim | call | the market's drift half-life; compiled 1200, the live scene says 1800
RandEventSystem.instance | assembly_valheim | call | VisitDirector, CargoEvent, Patch_Terminal
RandEventSystem.Awake | assembly_valheim | patch | our prefix registers valkyries_cargo on EVERY machine
RandEventSystem.m_events | assembly_valheim | call | CargoEvent.Register appends to it
RandEventSystem.HaveEvent | assembly_valheim | call | CargoEvent.IsRegistered
RandEventSystem.GetCurrentRandomEvent | assembly_valheim | call | the hold, the mirror and the end of every visit
RandEventSystem.SetRandomEventByName | assembly_valheim | call | CargoEvent.Start; also how vanilla restores a saved event
RandEventSystem.ResetRandomEvent | assembly_valheim | call | cargo dismiss and the orphan sweep
RandEventSystem.FixedUpdate | assembly_valheim | fact | THE clock rule, half of it: computes playerInArea from m_eventRange, then calls RandomEvent.Update
RandEventSystem.SendCurrentRandomEvent | assembly_valheim | fact | the 2 s broadcast of name, time and position to Everybody
RandEventSystem.SetActiveEvent | assembly_valheim | fact | the swap that fires OnActivate / OnDeactivate on a client inside the range
RandEventSystem.RPC_SetEvent | assembly_valheim | fact | a client resolves the name from ITS OWN m_events
RandEventSystem.SetRandomEvent | assembly_valheim | fact | private; the only thing that starts or ends an event
RandEventSystem.GetEvent | assembly_valheim | fact | private; the name lookup SetRandomEventByName and HaveEvent share
RandEventSystem.GetPossibleRandomEvents | assembly_valheim | fact | m_random = false keeps ours out of the random pool
RandEventSystem.PrepareSave | assembly_valheim | fact | vanilla SAVES the running event with the world
RandEventSystem.Save | assembly_valheim | fact | name, time and position (SaveAsync until 1.0)
RandEventSystem.Load | assembly_valheim | fact | restores through SetRandomEventByName; the director adopts it within 15 s
RandomEvent.m_name | assembly_valheim | call | CargoEvent
RandomEvent.m_enabled | assembly_valheim | call | CargoEvent.Register
RandomEvent.m_random | assembly_valheim | call | false: out of the random pool
RandomEvent.m_duration | assembly_valheim | call | refreshed from config before every start
RandomEvent.m_nearBaseOnly | assembly_valheim | call | CargoEvent.Register
RandomEvent.m_pauseIfNoPlayerInArea | assembly_valheim | call | true: the clock pauses when nobody is within 96 m
RandomEvent.m_eventRange | assembly_valheim | call | 96
RandomEvent.m_standaloneInterval | assembly_valheim | call | 0: out of the standalone loop
RandomEvent.m_standaloneChance | assembly_valheim | call | 0
RandomEvent.m_spawnerDelay | assembly_valheim | call | 0
RandomEvent.m_cameraShakeCurve | assembly_valheim | call | MUST be empty: with keys, Update calls GameCamera.instance.AddShake, null on a dedicated server
RandomEvent.m_biome | assembly_valheim | call | Heightmap.Biome.All
RandomEvent.m_startMessage | assembly_valheim | call | the centre banner
RandomEvent.m_endMessage | assembly_valheim | call | the centre banner
RandomEvent.m_forceMusic | assembly_valheim | call | empty: no raid music
RandomEvent.m_forceEnvironment | assembly_valheim | call | empty: no weather; house rule 4 never touches EnvMan
RandomEvent.m_time | assembly_valheim | call | CargoEvent.Remaining and cargo status
RandomEvent.m_pos | assembly_valheim | fact | where the pause radius is measured from
RandomEvent.Update | assembly_valheim | fact | THE clock rule, the other half: m_time += dt ONLY with a player in the area; ends past m_duration; AddShake when the curve has keys
RandomEvent.OnActivate | assembly_valheim | fact | shows m_startMessage once, on a client inside the range
RandomEvent.OnDeactivate | assembly_valheim | fact | shows m_endMessage when the event ended while active
Heightmap.Biome | assembly_valheim | type | RandomEvent.m_biome = Heightmap.Biome.All
Valkyrie.Awake | assembly_valheim | patch | our prefix skips vanilla for a bird carrying VCargo_cargo
Valkyrie.m_instance | assembly_valheim | fact | assigned on line 1 of Awake, BEFORE the owner guard: the reason the patch is a skip
Valkyrie.DropPlayer | assembly_valheim | fact | Game.SkipIntro calls it on m_instance and it un-intros Player.m_localPlayer unguarded
Valkyrie.UpdateValkyrie | assembly_valheim | fact | the flight maths Client/CargoFlight.cs keeps: 25 m look-ahead, banked turn, 0.5 m arrival
Valkyrie.m_attachPoint | assembly_valheim | call | where the merchant hangs (CargoFlight.AttachPoint)
Valkyrie.m_attachOffset | assembly_valheim | call | CargoFlight.AttachOffset
Valkyrie.m_dropHeight | assembly_valheim | call | the ONE flight number still taken from the prefab
Valkyrie.m_speed | assembly_valheim | call | cargo prefab dump only; ours flies at the synced FlightSpeed
Valkyrie.m_turnRate | assembly_valheim | fact | 20 on the prefab is a 57 m turning circle, wider than the whole run
Valkyrie.m_startDistance | assembly_valheim | call | cargo prefab dump
Valkyrie.m_startAltitude | assembly_valheim | call | cargo prefab dump
Game.instance | assembly_valheim | call | CargoTick.HostKey
Game.GetPlayerProfile | assembly_valheim | call | the listen host's ledger key
Game.SkipIntro | assembly_valheim | fact | calls Valkyrie.m_instance.DropPlayer(destroy: true)
Game.FixedUpdate | assembly_valheim | fact | THE one asserted client/server difference: the server pins its reference position to (1000000, 0, 1000000)
PlayerProfile.GetPlayerID | assembly_valheim | call | CargoTick.HostKey
Player.m_localPlayer | assembly_valheim | call | everywhere on the client side
Humanoid.GetInventory | assembly_valheim | call | DealApplier, CargoTerminal.RefreshCounts; declared on Humanoid, called through Player
Player.GetPlayerName | assembly_valheim | call | the admin verbs and the local transport
Player.GetComfortLevel | assembly_valheim | call | ComfortReporter; public where m_comfortLevel is not
Player.IsDead | assembly_valheim | call | a terminal panel rule
Player.m_comfortLevel | assembly_valheim | fact | private and computed locally: the whole reason the client reports comfort itself
Player.TakeInput | assembly_valheim | fact | consults Chat.HasFocus; the choke point UIFocus works through
Player.Update | assembly_valheim | fact | reads TakeInput() before any OnGUI runs
Character.GetAllCharacters | assembly_valheim | call | CargoFlight.FindMerchantByCarrier, the only path on a pure client
Character.GetSEMan | assembly_valheim | call | ComfortReporter
Character.m_name | assembly_valheim | call | cargo prefab dump
Character.m_faction | assembly_valheim | call | cargo prefab dump; CargoMerchant.Reassert sets it to Character.Faction.Players (P5)
Character.Faction | assembly_valheim | type | nested public enum, Players first; what CargoMerchant.Reassert sets so vanilla treats him as a player ally for aggro and targeting
Character.SetTamed | assembly_valheim | call | CargoMerchant.Reassert; public, but fires RPC_SetTamed and does NOT set m_tamed synchronously - only the RPC handler (m_nview.IsOwner()-gated) does, so IsTamed() right after MakeTame() is not a usable check
Character.InIntro | assembly_valheim | patch | our postfix (Patch_Character_InIntro): __result = true while CargoMerchant.Pinned; the one caller (UpdateMotion, off CustomFixedUpdate) zeroes the Rigidbody's velocity every step while true - not immunity, just no fall accumulating under the carry pin
Character.Damage | assembly_valheim | fact | NOT where damage happens: public, runs on the ATTACKER's machine, FindWeakSpotIndex + InvokeRPC("RPC_Damage", hit) and nothing else; see Character.RPC_Damage and CLAUDE.md's "Two corrections"
Character.RPC_Damage | assembly_valheim | patch | private; our prefix (Patch_Character_Damage, Priority.Low, __runOriginal) cancels it for the merchant HERE, not on Damage - the victim-side choke point every hit passes through
Humanoid.Awake | assembly_valheim | patch | our postfix (Patch_Humanoid_Awake) adds CargoMerchant when the ZDO carries VCargo_ingvar; runs after m_visEquipment and m_seed are set up here - NOT after GiveDefaultItems, which this method does not call (see Humanoid.Start)
Humanoid.Start | assembly_valheim | fact | calls GiveDefaultItems() for non-players only - the Dverger's crossbow is equipped HERE, not in Awake; CargoMerchant.Reassert's staggered re-apply (0.5s/1s/3s, then every 5s), not patch ordering, is what strips it back off
Humanoid.UnequipAllItems | assembly_valheim | call | CargoMerchant.Reassert: strips every equip slot (incl. the crossbow Start just gave him), triggerEquipEffects false
MonsterAI.MakeTame | assembly_valheim | call | CargoMerchant.Reassert; calls Character.SetTamed(true) (async, see above), SetAlerted(false), clears both target fields
MonsterAI.m_alertRange | assembly_valheim | call | declared here, not BaseAI; default 9999f, CargoMerchant.Reassert sets it 0
BaseAI.m_aggravatable | assembly_valheim | call | declared on BaseAI, not MonsterAI (MonsterAI : BaseAI); CargoMerchant.Reassert sets it false
BaseAI.m_passiveAggresive | assembly_valheim | call | declared on BaseAI, not MonsterAI; CargoMerchant.Reassert sets it false
BaseAI.m_randomMoveRange | assembly_valheim | call | declared on BaseAI, not MonsterAI; default 4f, CargoMerchant.Reassert sets it 1.5
Character.Awake | assembly_valheim | fact | caches m_animator = GetComponentInChildren<Animator>(); BodyLoader's appended-last defence
Character.m_animator | assembly_valheim | fact | must keep pointing at the VANILLA animator after the body swap
Character.SetVisible | assembly_valheim | fact | throws m_lodGroup.localReferencePoint out and back on every ownership change
Character.UpdateLodgroup | assembly_valheim | fact | scoped to m_visual, so a body hung off the ROOT is outside it
Character.m_collider | assembly_valheim | fact | a CapsuleCollider; the body swap must not disturb it
VisEquipment.UpdateLodgroup | assembly_valheim | fact | refills LOD[0].renderers from every renderer under Visual on any equipment change
CharacterAnimEvent.Awake | assembly_valheim | fact | one of the six root-scoped animator lookups BodyLoader must survive
NpcTalk.Start | assembly_valheim | fact | root-scoped animator lookup that runs AFTER Awake
FootStep.Start | assembly_valheim | fact | root-scoped animator lookup
RandomAnimation.Start | assembly_valheim | fact | root-scoped animator lookup
Projectile.RPC_Attach | assembly_valheim | fact | root-scoped animator lookup that can run at any time
SEMan.s_statusEffectRested | assembly_valheim | call | ComfortReporter
SEMan.HaveStatusEffect | assembly_valheim | call | ComfortReporter; the int overload
SE_Rested.CalculateComfortLevel | assembly_valheim | fact | runs locally and never leaves the client: DESIGN 3.1
Inventory.AddItem | assembly_valheim | call | DealApplier.AddStacks; caps ONE call at a stack
Inventory.CanAddItem | assembly_valheim | call | DealApplier.CanApply, the pre-check that keeps a deal atomic
Inventory.RemoveItem | assembly_valheim | call | DealApplier.Apply, BY SHARED NAME
Inventory.CountItems | assembly_valheim | call | DealApplier.Count, BY SHARED NAME
ItemDrop.m_itemData | assembly_valheim | call | DealApplier
ItemDrop.ItemData.m_shared | assembly_valheim | call | DealApplier
ItemDrop.ItemData.SharedData.m_name | assembly_valheim | call | the "$item_..." token vanilla's inventory keys on
ItemDrop.ItemData.SharedData.m_maxStackSize | assembly_valheim | call | DealApplier.AddStacks
ObjectDB.instance | assembly_valheim | call | DealApplier, cargo prefab
ObjectDB.GetItemPrefab | assembly_valheim | call | resolves a prefab name to the GameObject
MessageHud.instance | assembly_valheim | call | the pilot's line and the delivery banner
MessageHud.ShowMessage | assembly_valheim | call | CargoTick.PilotLine, Deliveries.Announce
MessageHud.MessageType | assembly_valheim | type | Center and TopLeft
Minimap.IsOpen | assembly_valheim | call | a terminal panel rule
Minimap.Update | assembly_valheim | fact | opens on !Chat.HasFocus() plus a still-pressed ZInput button
InventoryGui.IsVisible | assembly_valheim | call | a terminal panel rule
InventoryGui.Update | assembly_valheim | fact | opens on !Chat.instance.HasFocus()
Menu.IsVisible | assembly_valheim | call | a terminal panel rule
Chat.HasFocus | assembly_valheim | patch-vendored | UIFocus postfix; the choke point every input gate consults
Chat.instance | assembly_valheim | fact | UIFocus
Chat.Update | assembly_valheim | fact | UIFocus: the press that opens the chat window is the one that removes the cover
GameCamera.instance | assembly_valheim | fact | NULL on a dedicated server: why m_cameraShakeCurve must be empty
GameCamera.UpdateMouseCapture | assembly_valheim | patch-vendored | UIFocus prefix AND a Priority.First postfix
GameCamera.AddShake | assembly_valheim | fact | what a non-empty m_cameraShakeCurve would reach through a null instance
Terminal.InitTerminal | assembly_valheim | patch | our postfix registers the cargo console
Terminal.ConsoleCommand | assembly_valheim | type | the constructor assigns into Terminal's map by lowered name, so re-registration is harmless
Terminal.ConsoleEventArgs | assembly_valheim | type | Args and Context
Terminal.AddString | assembly_valheim | call | every line the console prints
Console.instance | assembly_valheim | call | AdminRpc.OnReply prints the server's answer
Odin.m_despawn | assembly_valheim | call | the Odin vanish; dumped by cargo prefab odin
Odin.m_ttl | assembly_valheim | fact | 300 is the FIELD INITIALISER; the prefab says 60 (PR #8). cargo prefab odin decides it
Trader.m_name | assembly_valheim | call | cargo prefab Haldor
Trader.m_items | assembly_valheim | call | cargo prefab Haldor
Trader.m_standRange | assembly_valheim | call | cargo prefab Haldor
EffectList.m_effectPrefabs | assembly_valheim | call | Patch_Terminal.DumpEffects
EffectList.EffectData | assembly_valheim | type | Patch_Terminal.DumpEffects
EffectList.EffectData.m_prefab | assembly_valheim | call | decides the effect rule branch: networked means the owner creates it
EffectList.EffectData.m_enabled | assembly_valheim | call | Patch_Terminal.DumpEffects
SaveSystem.GetWorldsSaveRootPath | assembly_valheim | call | WorldSavePath.FindCandidate (1.0; World.GetWorldSavePath on 0.221.12); explicitly Local, because Auto/Cloud return "" under Steam Cloud
FejdStartup.ShowConnectError | assembly_valheim | patch-vendored | ServerSync's version-mismatch message
ZPlayFabSocket.m_remotePlayerId | assembly_valheim | reflect-vendored | ServerSync copies it onto the buffering socket
Version.CurrentVersion | assembly_valheim | fact | 1.0.7; the identity of the build, and P10b's boot check
Version.c_networkVersion | assembly_valheim | fact | 39 (m_networkVersion until 1.0)
Version.c_PlayerVersion | assembly_valheim | fact | Version.Player.DeepNorth = 46 (m_playerVersion until 1.0)
Version.c_WorldVersion | assembly_valheim | fact | Version.World.DeepNorth = 41 (m_worldVersion until 1.0)
FileHelpers.FileSource | assembly_utils | type | the Local member, by name: its value moved on 1.0 (bit flags)
StringExtensionMethods.GetStableHashCode | assembly_utils | call | every ZDO key and every prefab hash in this mod
ZInput.GetKeyDown | assembly_utils | call | the terminal's Escape; NOT UnityEngine.Input, which this build ignores
ZInput.GetButtonDown | assembly_utils | call | Use, Inventory, Map
ZInput.ResetButtonStatus | assembly_utils | call | so Tab does not close the terminal AND open the inventory
Localization.instance | assembly_guiutils | call | the terminal's item names; a real dependency since P7
Localization.Localize | assembly_guiutils | call | turns a "$item_..." token into what the player reads
```
