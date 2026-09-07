# Paused Projects — Notable/Unique Implementations

These projects are on hold. **Roster corrected 2026-09-03:** `PAUSED/` now holds DvergrWarbandST, IronCohort22, Norns Tale, PortalLock, SkyNet Redux v1, Skynet v2, **DeathBeforeDishonor** (writeup in `DeathBeforeDishonor_TheEye.md`), **TheRavensCall** (1.2.1) and **WhereTheCrowFlies** (1.1.0) — the last two have no writeup. **RuneboundRest is no longer under `PAUSED/`**: it is top-level at `WubarrkCODING/RuneboundRest/` with a released 1.1.0; its section is kept below for the transpiler technique. These but contain real, sometimes genuinely novel implementations worth preserving as reference. This is a lighter-weight survey than the active-project reports.

---

## DvergrWarbandST

**Overview:** "Dvergr Warband: Siege & Tactics" is a full BepInEx/Jotunn Valheim mod that adds RTS-style squad command of Dvergr allies (hard dependency on the base `DvergrAllies` mod — see `DvergrAllies.md`). It layers a Commander HUD, formation control, procedural mission outposts, and scripted multi-faction sieges on top of that base companion AI. The richest and most novel of the seven paused projects.

*(Note: a `DvergrAllies/` subfolder inside this project containing one file plus a prebuilt DLL is a vendored reference copy of the base mod, not part of this project's own source.)*

### System: RTS Squad Formation & Movement Control
- **Purpose:** Group tamed Dvergr/allied units into up to 4 squads of 4, assign formations, and issue move orders that hold formation shape.
- **Key files:** `SquadManager.cs`, `CommanderHUD.cs`
- **Architecture:** `SquadManager.Squad` holds a `List<Character> Members` and a `FormationType` (ShieldWall/Wedge/SkirmishLine). `OrderMove()` computes per-unit world positions via `CalculateFormationPositions()` (simple offset math along a `right = Cross(up, forward)` vector), then for each member spawns a throwaway `GameObject` at the target position and calls the existing `MonsterAI.SetFollowTarget(moveTarget)` — it doesn't touch AI internals at all, it just feeds vanilla `MonsterAI` a fake follow target. Squad selection uses `Physics.SphereCastAll` sorted by distance, filtered to tamed/Dverger/Player-faction characters.
- **How to implement:** 1) Define a `Squad` model with members + formation enum. 2) Write a pure function mapping (center, forward, memberCount) → list of world offsets per formation shape. 3) On move order, spawn one throwaway empty GameObject per member at its computed slot and call `MonsterAI.SetFollowTarget()` on each unit — no custom pathfinding/AI override needed. 4) Reassigning formation while a move is active just re-runs the same order against the last stored target/facing.

### System: RTS Camera Mode (camera hijack)
- **Purpose:** Toggleable top-down tactical camera separate from the normal third-person view.
- **Key files:** `CommanderHUD.cs`
- **Architecture:** On toggle, stores an `rtsPosition`/`rtsRotation` (fixed 60° pitch) and in `LateUpdate()` directly overwrites `GameCamera.instance.transform.position/rotation` every frame while active, overriding vanilla camera control without patching `GameCamera` itself. Selection/assignment raycasts switch between mouse-position rays (RTS mode) and screen-center rays (third-person).
- **How to implement:** Cache the `GameCamera` component, and in `LateUpdate` (after vanilla's own camera update) stomp its transform to your own tracked position/rotation while a mode flag is set; drive pan/zoom by mutating that cached position/rotation in `Update()`.

### System: Procedural Mission Outpost Generation
- **Purpose:** Spawn a fully-built enemy outpost (walls, spikes, torches, fire, multiple loot chests, boss + mob spawns, an optional "rescue a caged Dvergr" event) at a random valid location on demand, with a map marker and auto-detected completion.
- **Key files:** `OutpostGenerator.cs`, `WarTableUI.cs`, `FactionAligner.cs`
- **Architecture:** `FindSuitableOutpostLocation()` samples random directions/distances and uses `WorldGenerator.instance.GetHeight()` — which works even on unloaded zones — to find dry land without forcing zone generation. `SpawnOutpost()` procedurally places walls/spikes in a hollow-square loop, ground-snaps every piece via `ZoneSystem.GetGroundHeight`, and per faction theme (Fuling/Greydwarf/Draugr/Troll) picks wall/enemy/boss prefab names and loot tables. An `AreaClearer` component destroys trees/rocks/destructibles in a radius via `Physics.OverlapSphere`. `MissionObjective` polls every 2s for any living non-player/non-tamed/non-Dverger character within radius, auto-completing after 3 consecutive clear checks (~6s) to avoid spawn-lag false completes. Minimap pins added via reflection against `Minimap.AddPin` (multiple overload fallbacks) to survive API mismatches.
- **How to implement:** 1) Scan candidate positions with `WorldGenerator.GetHeight` (no zone load required) to avoid water/unsuitable terrain. 2) Spawn structural pieces in a loop with `Instantiate` + ground-snap each. 3) Track completion with a lightweight poller requiring N consecutive clear ticks before firing completion. 4) For event markers, resolve `Minimap.AddPin` via reflection with an overload-length fallback loop.

### System: Unified Enemy Faction Alignment (anti-infighting)
- **Purpose:** Force normally-hostile-to-each-other monster types (e.g., Trolls + Draugr) to ignore each other and focus fire on the player during sieges/outposts.
- **Key files:** `FactionAligner.cs`
- **Architecture:** A one-line component that on `Start()` sets `character.m_faction` to a custom out-of-range enum value cast from an int (`(Character.Faction)1001`), exploiting the fact that Valheim's faction check is just an int/enum comparison with no bounds validation — any spawned monster given this component joins a synthetic faction all of them share, so mutual hostility disappears while player hostility remains.
- **How to implement:** Add a tiny component that sets `character.m_faction = (Character.Faction)<unused int>` on spawn for every enemy you want unified. No Harmony patch needed — it's a plain field write exploiting vanilla's permissive enum casting.

### System: Injected Custom Siege/Raid Event
- **Purpose:** A scripted 5-minute, ~35-enemy multi-wave siege triggerable via console command or the War Table UI, independent of vanilla raid events.
- **Key files:** `RaidSystem.cs`
- **Architecture:** Builds a `RandomEvent` object in code (custom spawners for wave types, forced music/weather) and Harmony-postfixes `RandEventSystem.Awake` to append it into `__instance.m_events` if not already present. Triggering calls the vanilla `RandEventSystem.instance.SetRandomEventByName(...)`.
- **How to implement:** Construct a `RandomEvent`/`SpawnSystem.SpawnData` object entirely in C# (no Jotunn custom-event API needed) and inject it into `RandEventSystem.m_events` via a Harmony postfix on `Awake`; trigger with the vanilla `SetRandomEventByName`/`ResetRandomEvent` API.

### System: Custom War Table Piece + Mission Board UI
- **Purpose:** A buildable structure (cloned from the vanilla cartography table) that opens a Jotunn custom GUI mission board for launching procedural missions.
- **Key files:** `WarTableManager.cs`, `WarTableUI.cs`, `WarTableInteractable.cs`
- **Architecture:** `CustomPiece` clones `piece_cartographytable`, strips the vanilla `MapTable` component via `DestroyImmediate`, adds a custom `WarTableInteractable`. UI built through Jotunn's `GUIManager.OnCustomGUIAvailable` callback with a small `HoverAnimator` (`IPointerEnterHandler`/`IPointerExitHandler`) that lerps button scale/text color on hover.
- **How to implement:** Clone an existing piece with `CustomPiece(newName, baseName, PieceConfig)`, remove unwanted vanilla interaction components with `DestroyImmediate`, attach your own MonoBehaviour, build the panel UI inside a `GUIManager.OnCustomGUIAvailable` handler.

---

## IronCohort22

**Overview:** This is **not a Valheim/BepInEx mod** — it's a very early-stage standalone tactical-squad game prototype, built as a hybrid Godot 4.3 project alongside a separate `src/` solution using Unity types and referencing "Fish-Net" (a Unity netcode library) for a planned client/server split. It appears to be an experimental/confused scaffold mixing two different engines rather than a coherent shippable project — almost everything is a stub or TODO placeholder. Unrelated to Valheim entirely, despite the name superficially suggesting a DvergrAllies-style companion AI system.

### System: Boids-style Squad Flocking + Formation Shapes
- **Purpose:** Move a 20-unit squad ("Grunts") toward per-unit formation slot targets while avoiding overlapping each other, with 4 formation shapes.
- **Key files:** `src/IronCohort22.Client/Squad/SquadFormationManager.cs`, `FlockingGrunt.cs`, `LieutenantController.cs`
- **Architecture:** `SquadFormationManager` precomputes a `Vector3[20]` of local offsets per formation type (row/column math, triangular row-fill for Wedge, radial angle placement for Skirmish) and rotates each by the leader's facing on demand. Each `FlockingGrunt` (`CharacterController`-based) independently computes a steering vector each frame: attraction toward its assigned formation slot + a separation vector averaged from nearby peers (classic boids separation, O(n²) — no spatial partitioning). `LieutenantController` spawns the squad and shares the peer list.
- **How to implement:** 1) Precompute local-space offset table per formation enum value using simple geometric loops. 2) Rotate offsets into world space by the leader's forward transform on formation change. 3) Per-unit `Update()`: sum a normalized "seek target" vector and an inverse-distance-weighted "avoid nearby peers" vector, blend by configurable weights, move via `CharacterController.Move`. Fine for squads of ~20; would need spatial partitioning to scale further. Unity-flavored, portable formation/flocking pattern that could be adapted into a Valheim companion mod's movement layer.

---

## Norns Tale

**Overview:** A Jotunn-based Valheim mod that turns player deaths, kills, boss kills, biome discovery, and gear-tier milestones into a persistent, Norse-flavored narrative/achievement "saga" system — broadcasting flavor-text events to all players, awarding titles at thresholds, and optionally posting key moments to a Discord webhook. Client/Server split with Jotunn `CustomRPC` networking and a draggable in-game HUD.

### System: Saga Chronicle — Achievement/Lore Tracking with Discord Webhook
- **Purpose:** Track cumulative per-player stats (deaths, creature kills, boss kills, biomes discovered, gear tiers reached), award narrative titles at configurable thresholds, generate ambient lore snippets, and broadcast/log/webhook these events.
- **Key files:** `Server/ServerSystems.cs`, `Server/SagaStore.cs`, `Server/TitleRules.cs`, `Server/LoreGenerator.cs`, `Server/DiscordIntegration.cs`, `Core/RPC.cs`, `Client/EventHud.cs`, `Utils/Logging/Chronicle.cs`
- **Architecture:** `SagaStore` persists one JSON file per player under `BepInEx/config/NornsTale_Saga/{name}_norn.json` (stats/titles) and a separate `{name}_lore.json` (accumulated narrative fragments), loaded/saved defensively (catch-and-default on corrupt/missing files). `TitleRules` is a static threshold table (e.g., kill-count tiers → title names; boss-name → flavor title dict). Events flow through `ServerEvents` → `ServerSystems` (build message → `Plugin.Broadcast` center-screen chat to all peers → append to a local log file → conditionally push to Discord via `DiscordIntegration.Send` (raw `UnityWebRequest` POST of `{"content": "..."}`, fire-and-forget) → conditionally roll random lore and whisper it privately to the triggering player via a Jotunn `CustomRPC` routed through `RPC.SendToClient`). Player login/logout tracked via Harmony patches on `Game.SpawnPlayer` (postfix) and `Player.OnDestroy` (prefix). Client-side `EventHud` is a hand-rolled draggable/scalable overlay panel (drag via `RectTransformUtility.ScreenPointToLocalPointInRectangle`, scale via mouse scroll clamped, position/scale persisted to `PlayerPrefs`), toggled with backslash.
- **How to implement:** 1) Per-player JSON store keyed by player name under `Paths.ConfigPath`, with try/catch-default load and atomic-ish save. 2) A static threshold-ladder for title rules, checked after each stat increment. 3) Jotunn `NetworkManager.Instance.AddRPC(name, serverHandler, clientHandler)` for a lightweight typed event channel. 4) Discord webhook: POST JSON `{"content": msg}` via `UnityWebRequest`, escaping quotes only — fire-and-forget is acceptable for non-critical notifications. 5) Draggable HUD panel: build a `RectTransform`+`Image`+`Text` at runtime, implement drag/scale directly in `Update()` using `RectTransformUtility` screen-to-local conversion, persist via `PlayerPrefs`.

---

## PortalLock

**Overview:** A small server-side-only Valheim plugin enforcing a configurable max-portals-per-player limit (default 6) across all portal prefab types, with admin bypass and a YAML-backed persistent index. A fairly generic, single-purpose utility mod without deep systems.

### System: Heuristic Portal Detection + YAML Index with Atomic Save
- **Purpose:** Detect portal placement/destruction generically (not tied to a specific prefab whitelist) and persist per-player portal ownership durably.
- **Key files:** `src/PortalLock/PlacementPatches.cs`, `src/PortalLock/PortalTracker.cs`, `src/PortalLock/YamlStorage.cs`
- **Architecture:** Detection is name/component heuristic rather than a hardcoded prefab list: any placed/instantiated/destroyed `GameObject` whose name contains "portal" (case-insensitive) or that has a component literally named `"Portal"` (checked via `GetComponent("Portal")` by string, avoiding a hard type reference) is treated as a portal. A `Prefix_PlacePiece` Harmony patch (server-only, admin-bypassed) blocks placement (`return false`) if the placing player is at/over their limit; a static field bridges the prefix to a later `Postfix_Instantiate` patch on `ZNetScene.Instantiate` (since the actual GameObject/ZDO isn't available until instantiation), which registers the portal by ZDO id and writes an owner key onto the ZDO for persistence across restarts. `Postfix_Destroy` unregisters. `YamlStorage` (YamlDotNet) saves to a temp file, backs up the previous version to `.bak`, copies temp→real, deletes temp — a crude but effective corruption-avoidance pattern.
- **How to implement:** For generic prefab-family detection without a hardcoded list, match by substring on `GameObject.name` and by component name-string lookup (`GetComponent("TypeName")`) rather than requiring a compile-time type reference. For placement→instantiation linkage, stash the acting player in a static field in the placement prefix and consume it in the scene-instantiate postfix. For crash-safe file writes: write to `.tmp`, backup existing file to `.bak`, copy tmp over the real path, delete tmp.

---

## RuneboundRest
*Not paused as of 2026-09-03 — lives at `WubarrkCODING/RuneboundRest/`, released 1.1.0 (`wubarrk.runeboundrest`). Kept here for the technique.*

**Overview:** A comfort/resting-mechanics overhaul mod: configurable rested-buff duration, comfort search radius/update interval, sitting/sleeping comfort bonuses, and conditions required to start resting (fire/shelter/wet/danger). Fully ServerSync-config-synced. The most IL-transpiler-heavy of the seven projects.

### System: Full Takeover of Vanilla Resting Status via IL Transpiler "Neutering"
- **Purpose:** Replace vanilla's hardcoded resting-status add/remove logic (which forcibly clears the Resting status effect under conditions the mod wants to override, e.g. being cold) with fully custom, config-driven logic.
- **Key files:** `Patches/RestingConditionsPatch.cs`
- **Architecture:** Rather than trying to out-race or block vanilla's status changes, a transpiler scans `Player.UpdateEnvStatusEffects` IL for every `ldsfld SEMan.s_statusEffectResting` and replaces it with `ldc.i4.0` (constant 0). Since vanilla's `AddStatusEffect(0)`/`RemoveStatusEffect(0)` become no-ops against status ID 0, this makes every vanilla read/write of the Resting effect harmless, and a Postfix on the same method then evaluates the mod's own condition set and calls `AddStatusEffect`/`RemoveStatusEffect` with the *real* id directly.
- **How to implement:** To fully own a vanilla status-effect toggle without fighting the game's own logic every frame: find the static field reference(s) driving the vanilla add/remove calls via `HarmonyTranspiler`, replace the field load with a constant `0` (or any inert value) so the original calls become no-ops, then apply your own conditional add/remove in a Postfix using the field's real value.

### System: Magic-Number IL Patching for Hardcoded Constants
- **Purpose:** Make vanilla hardcoded constants (comfort search radius = 10f, comfort scan interval = 2f) configurable without a full method rewrite.
- **Key files:** `Patches/ComfortRadiusPatch.cs`, `Patches/ComfortUpdateIntervalPatch.cs`
- **Architecture:** Both transpilers scan for a specific `ldc.r4 <value>` IL instruction matching the known vanilla constant and replace it — one by swapping the opcode to `call` against a small static getter method, the other by emitting `ldsfld` (static config field) + `callvirt` (the `ConfigEntry<float>.Value` getter) directly inline in place of the constant. Both log an error if the expected constant isn't found.
- **How to implement:** To make an inlined vanilla constant configurable: transpile the target method, find the `ldc.r4`/`ldc.i4` instruction bearing the exact known constant value, replace it with a `call` to a static wrapper returning your config value, or inline IL loading the config value directly. Always assert/log when the expected constant pattern isn't found, since future game updates can silently break the match.

---

## SkyNet Redux v1 / Skynet v2

**Overview:** Both are the same concept: an adaptive, self-diagnosing **dedicated-server performance auto-tuner** for Valheim — not a gameplay mod. It reflects into private Valheim networking/AI/zone-gen internals to dynamically throttle ZDO sync traffic, AI update rates, and zone generation based on live server load, switching between "Boost/Balanced/Shield" modes. **Skynet v2 is a leaner successor/refactor of SkyNet Redux v1**: v2 drops the entire client-side subsystem (no HUD/telemetry-to-client), drops the boot-time deep-scan/auto-patcher indirection, and moves from a manual-`Update()`-driven pipeline + reflection-built Harmony patcher, to conventional static `[HarmonyPatch]` classes in a `Patches/` folder — the same brain, server-only, wired more idiomatically.

### System: Reflection-Based "Deep Scan" Self-Diagnosis + Degraded-Mode Fallback (Redux v1)
- **Purpose:** At boot, probe the currently-loaded game assembly for the exact private fields/methods needed (ZDOMan send-queue fields, ZoneSystem generation internals, EnvMan getters, AI update method), cache them via reflection, and gracefully degrade (fixed "Balanced" mode) for any capability whose hook isn't found — resilient to Valheim updates renaming/moving private members.
- **Key files:** `Server/ServerDeepScan.cs`, `Shared/SharedReflectionCache.cs`, `Shared/DeepScanData.cs`
- **Architecture:** Resolves and caches `MethodInfo`/`FieldInfo` handles for the target internals, builds a `DeepScanData` capability record (`CanThrottleZDOs`, `CanThrottleZones`, `CanBalanceAI`, etc.), logs a summary. `ServerAutoPatcher.Apply()` (Redux v1 only) then conditionally applies Harmony patches for only the capabilities the scan found available.
- **How to implement:** At startup, resolve every reflection target you'll need up front into a single cache class, recording success/failure per capability. Gate each optional subsystem's patch application and runtime behavior on its capability flag, and fall back to a safe fixed behavior when a capability is missing.

### System: Adaptive Load-Based Mode Switching ("Trinity" model)
- **Purpose:** Continuously compute a synthetic server-load score from tick time, drift, and jitter, and select one of three operating modes that scale AI/ZDO/zone throttling aggressiveness.
- **Key files:** `Server/ServerLogicEngine.cs`, `Shared/SkyNetStabilizedMetrics.cs`, `Server/ServerEnvironmentEngine.cs`
- **Architecture:** Computes `trinity = jitter*W1 + drift*W2 + tick*W3`, scaled by an environment bias, thresholds into Boost/Balanced/Shield with a 2-second cooldown to prevent mode flapping. Downstream systems read the current mode to scale their own behavior:
  - `ServerZDOThrottle`: per-peer ZDO send-queue budget by mode, evicting oldest-by-sync-time entries (LRU) once a peer's queued ZDO count exceeds budget, at a fixed 20Hz tick.
  - `ServerAILoadBalancer`: scans all `BaseAI` instances 4x/second and multiplies each one's `m_updateInterval` by a mode-based multiplier, reflection-writing the private field directly.
  - `ServerZoneWarmup`: proactively pre-generates zones ahead of players (one zone per tick, spiral search from each ready peer) by reflecting into `ZoneSystem` internals and invoking the private `SpawnZone` method — only when terrain is confirmed ready via `HeightmapBuilder.IsTerrainReady`.
  - `ServerHeartbeat`: broadcasts a compact snapshot to all clients over a custom RPC, but only when something meaningfully changed — event-driven rather than fixed-interval, reducing network chatter.
- **How to implement:** 1) Build a small metrics engine sampling tick time/drift/jitter each frame into a smoothed struct. 2) Combine into one weighted scalar and threshold into a discrete mode enum with hysteresis (cooldown timer) to avoid oscillation. 3) Have each throttling subsystem read the current mode once per its own tick and scale a single per-subsystem knob — keep subsystems decoupled from the mode logic itself. 4) For ZDO throttling: reflect into the per-peer send dictionary, sort by sync timestamp, remove-oldest down to budget. 5) For AI throttling: reflect-write the per-instance update-interval field, clamped to a sane range. 6) For zone warmup: only pre-spawn a zone once its terrain is confirmed ready, one per tick. 7) Only push heartbeat/telemetry packets on meaningful change, not on a fixed timer.

### System: Harmony-Postfix Metrics/Throttle Hooking on Hot Game Methods (v2's cleaner wiring)
- **Purpose:** Same throttling behavior as above, wired as idiomatic static Harmony patch classes instead of a dynamically-built patcher.
- **Key files:** `Skynet v2/Patches/ZDOMan_Patch.cs`, `ZoneSystem_Patch.cs`, `CharacterAI_Patch.cs`, `EnvMan_Patch.cs`, `ZNET_FixedUpdate_patch.cs`
- **Architecture:** e.g. a one-line `[HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.Update))] [HarmonyPostfix]` calling `ServerMetricsEngine.Update()` — the "clean" version of what Redux v1 did via a reflection-built dynamic patcher.
- **How to implement:** If you already have a working reflection-based dynamic patcher and the target methods turn out to be stable across the versions you support, prefer converting them to plain static `[HarmonyPatch(typeof(X), nameof(X.Y))]` classes — simpler to read/maintain, at the cost of needing the type/method to be a real compile-time reference (losing the graceful-degradation-on-missing-member safety net of the reflection-based approach).
