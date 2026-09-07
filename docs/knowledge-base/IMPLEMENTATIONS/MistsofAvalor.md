# Mists of Avalor — Technical Systems Report

## Overview

**Mists of Avalor** is a large **Valheim game mod** built with **BepInEx** (mod loader) and **Jotunn** (the Valheim modding library), written in C# targeting **.NET Framework 4.8**, using Harmony for runtime patching. It adds a procedurally-generated **3D multi-floor labyrinth dimension** (a recursive-backtracker maze with vertical floors, combat arenas, traps, locked vaults, and a dynamic mob-spawning "director") that lives in an unused pocket of Valheim's existing world space (`X:30000, Y:500, Z:30000`), reached through a custom portal spawned near the player's starting Sacrificial Stones. Scope: ~11,000 lines of C# across Core/Prefabs/Maze/Atmosphere/Mobs/Portal/Gear/Compat/Commands, plus a companion in-Unity-Editor asset-bundle build tool and a Meshy-AI-driven authored-art pipeline. The "procedural generation" this project implements is a maze/dungeon generator plus procedural runtime terrain edits to the *overworld* around the portal (not a heightmap/biome terrain generator in the classic sense — Valheim already owns overworld terrain, so this project's terrain work is about *editing* Valheim's existing per-zone heightmap safely).

Author-written docs worth citing directly: `Geadme.md` (player-facing feature/config README), `AVALOR_WEAPONS_SPEC.md` (design-spec for the two custom weapons and their "donor transform" balancing philosophy), `_Attic/README.md` (superseded files kept for audit trail), and `.agents/skills/meshy/SKILL.md` (a Claude-agent skill documenting the Meshy text-to-3D API workflow).

### System index
1. 3D Maze Procedural Generation (recursive backtracker)
2. Maze Mesh Streaming / Chunked Combine-Mesh Renderer
3. Authored-Panel Orientation Solver (mesh-dressing pattern)
4. Live Atmosphere/Fog/Lighting System
5. Per-Prop Live Density Culling (torches/mist)
6. Procedural Rune Texture + Material System
7. Layered Portal Rune VFX (particle-system gateway effect)
8. Mob Director (dynamic proximity spawner + end boss)
9. Sub-Boss Mutation System
10. Custom Boss-Bar HUD
11. Cross-Mod AI Compatibility Layer (reflection + patch-ordering interop)
12. Portal / Teleportation Mechanics
13. Overworld Portal Site Generator (placement + procedural decoration + path grading)
14. Cross-Zone Terrain Editing System
15. Jotunn Prefab-Cloning Pipeline ("clone, strip, re-skin" pattern)
16. Weapon "Donor Transform" Stat System
17. Banded/Depth-Scaled Loot Table System
18. Persistent Diagnostic Logger
19. Config System (BepInEx + Jotunn admin-sync + live-reactive settings)
20. World-State Reset / ZDO-Wipe System
21. Grave Relocation System
22. Console Command Framework
23. UnityBundleBuilder Asset Pipeline (Editor tool)
24. Meshy AI 3D-Generation Skill/Workflow
25. Draggable / Resizable / Persisted HUD Widget (no EventSystem, no sprites)
26. Mod-Placed Building Pieces: the two components that delete your object
27. ZDO-Driven Mob Director (the dedicated-server pattern)
28. Chunk-Data Decode: replicated world geometry as a physics substitute
29. Routed-RPC Net Layer: roles, claims, queries
30. Persistent Terrain Edits: `TerrainOp`/`TerrainComp` vs `TerrainModifier`
31. Sky-Region Presence: the airspace is a COLUMN, not a sphere
32. ⭐ Relocating a Physics Object Safely (and the Grave Watchdog) — **promoted to MASTER_IMPLEMENTATIONS.md**

Followed by **Dedicated-Server Field Findings** — what the first live headless deployment broke, two recorded corrections, and the testbed operating rules.

---

### 1. 3D Maze Procedural Generation (Recursive Backtracker)

- **Purpose:** Generates a unique, fully-connected, multi-floor 3D labyrinth every time the maze resets, sized 20×20×1 to 60×60×3 cells depending on config.
- **Key files:** `Maze/AvalorMazeGenerator.cs` (1209 lines, the whole pipeline lives in `GenerateMaze()`), `Core/MistsofAvalorPlugin.cs` (config enums).
- **Architecture:**
  - A 3D grid `MazeCell[width, floors, depth]` where each cell stores 6 booleans (`wallTop/Bottom/Left/Right/Ceiling/Floor`, i.e. ±Z, ±X, ±Y) plus a `distance` field for later flood-fill.
  - **Algorithm 1 — Recursive backtracker (iterative, stack-based):** push the start cell (0,0,0), loop: pop `current`, gather unvisited neighbours (4 horizontal always; vertical neighbours only offered with 15% probability, biasing the maze toward wide flat floors instead of stairwells), if any exist re-push `current`, pick one at random, `RemoveWall` between them, push the chosen cell, mark visited. Classic guaranteed-fully-connected, perfect-maze algorithm.
  - **Algorithm 2 — Braid pass:** for every interior cell with ≥3 walls (a dead end), 35% chance to knock down one more wall toward a valid neighbour, turning a subset of dead ends into loops.
  - **Algorithm 3 — Arena carving:** picks N random cells, carves out a 2×2 to 4×4 open room by clearing internal walls, then decorates it (chests at distinct corners, a random mob spawner).
  - **Guaranteed entrance mouth:** forces the (0,0,0) cell's two interior-facing walls open regardless of RNG.
  - **Algorithm 4 — Dijkstra/BFS flood fill:** re-run visited/distance tracking from the entrance across all 6 neighbour directions to find the single cell of *maximum graph distance* from the start — that becomes the guaranteed exit location (deepest point, not just farthest in a straight line).
  - **Loot/prize placement:** a unique "prize chest" is placed in the open neighbour cell of the exit (queried from the grid's wall bits — not a hardcoded offset, since the exit is by definition a dead end with only one open side).
  - **Ordering lesson (v0.0.9):** the exit portal, shrine, recall rune and prize chest are now emitted **immediately after the flood-fill**, before the decoration pass. They used to come last, after a triple-nested walk over every cell on every floor that yields every 30 instantiations — hundreds of frames on a 60×60×3 maze — so anything that interrupted the coroutine produced a fully furnished labyrinth with **no ending**. Nothing about them depended on the decoration pass; they were last only by accident of how the method grew. **Emit the objects that make a level completable before the objects that make it pretty.** The same pass also had to exclude the exit cell *and its one open neighbour* from the key-locked vault-gate roll, which could otherwise seal the player out of the way home.
  - **Chunking for streaming:** the maze is split into 4×4-cell "mega-chunks"; each chunk's wall/floor/ceiling/stair state is packed into a `byte[]` (one byte per cell, bit flags for the 6 wall directions + a "stair" bit) and stored on the chunk's ZDO, deferring actual mesh geometry to a separate renderer component (System 2); chunks spawned sorted by distance from the entrance (outward-first) with a `yield return null` per chunk to spread instantiation across frames.
  - **World-space mapping:** `cellSize = 6f` (corridor width), `floorHeight = 4f` (storey height).
  - Loot chests are graded by a `Depth01` metric = straight-line distance from entrance / maze diagonal span, feeding into `AvalorLootManager` (System 17).
- **How to implement (step-by-step):**
  1. Define a 3D array of cell structs with 6 wall booleans, all `true` initially (fully walled).
  2. Implement iterative recursive-backtracker maze carving over the 3D grid using a `Stack<Vector3Int>`; bias vertical-neighbour selection to a low probability (~15%) for flat floors connected by occasional stairs.
  3. Run a second pass: for any interior cell with ≥3 walls still standing, roll a chance (e.g. 35%) to remove one more wall — this "braids" dead ends into loops.
  4. Run an "arena" pass: pick random NxN cell blocks and clear all internal walls to create open rooms; populate with chests/spawners.
  5. Force the entrance cell open on its outward sides.
  6. Run BFS/Dijkstra from the entrance across the final wall graph; the cell with the largest visited distance is the exit — guaranteed reachable and maximally far by pathing.
  7. Pack cell wall-state into compact byte flags per chunk and hand off to a separate mesh-building system.
  8. Instantiate networked entities a few at a time per frame (coroutine `yield return null` every N spawns) to avoid a multi-second hitch.
- **Reusable pattern/snippet:**
```csharp
Stack<Vector3Int> stack = new Stack<Vector3Int>();
var current = new Vector3Int(0,0,0);
grid[current.x,current.y,current.z].visited = true;
stack.Push(current);
while (stack.Count > 0) {
    current = stack.Pop();
    var unvisited = GetUnvisitedNeighbors(current, grid, width, floors, depth, rnd);
    if (unvisited.Count > 0) {
        stack.Push(current);
        var chosen = unvisited[rnd.Next(unvisited.Count)];
        RemoveWall(current, chosen, grid);
        grid[chosen.x, chosen.y, chosen.z].visited = true;
        stack.Push(chosen);
    }
}
```

---

### 2. Maze Mesh Streaming / Chunked Combine-Mesh Renderer

- **Purpose:** Converts a chunk's packed byte-array cell data into actual walkable/renderable geometry at streaming time, off the main thread, without per-object colliders per wall tile.
- **Key files:** `Maze/AvalorMazeChunkRenderer.cs`, `Prefabs/AvalorAssetManager.cs` (`CombineTileMeshesThreaded`, `BuildCombined`, `FindLOD0`).
- **Architecture:**
  - On `Start()`, decodes byte flags per cell, builds one `List<Matrix4x4>` bucket per **tile variant** (3 wall variants, 3 floor variants, 1 ceiling, 1 stair) — deterministic variant selection is a hash of the tile's quantized world position (prime-multiplier XOR hash + a 64-bit mix constant) so every client renders identical stonework without networking the choice.
  - **Wall de-duplication:** interior walls sit on a boundary shared by two cells; a `HashSet<long>` keyed on quantized position+yaw ensures each boundary is only emitted once.
  - **Threaded mesh combine (`CombineTileMeshesThreaded`):** reads the *source* tile's small mesh on the main thread once, then does the per-instance matrix-transform math on a `ThreadPool` worker thread using raw `Matrix4x4.MultiplyPoint3x4`/`MultiplyVector` (no Unity API calls off the main thread), then uploads the finished buffer back on the main thread via `Mesh.SetVertices/SetNormals/...`. Removes the classic `Mesh.CombineMeshes` main-thread spike.
  - **Off-thread collider baking:** `Physics.BakeMesh(meshId, false)` runs on a background `ThreadPool` work item; the collider's `sharedMesh` is only assigned once baked (or after a 5s timeout fallback).
  - **Bounded concurrency:** a static counter caps simultaneous chunk builds at 4, with leak-proof release in both `finally` and `OnDestroy()` (Unity does not call a coroutine's `finally` if the GameObject is destroyed mid-coroutine while streamed out) plus a 5-second timeout so a stuck slot can never deadlock streaming forever.
  - **Stairs:** an authored "stair tile" mesh is used when available, oriented by sampling the *lower* cell's open wall bit so you always walk off the bottom into open corridor.
- **How to implement:**
  1. Store per-chunk cell data as compact byte flags.
  2. At stream-in time, decode bytes into `List<Matrix4x4>` per variant/tile-type, deduping shared faces via a position+orientation hash set.
  3. Pick variants deterministically from a spatial hash so all clients agree without networking the choice.
  4. Do heavy vertex-transform math for combining N tile instances into 1 mesh on a background thread; only touch Unity API on the main thread.
  5. Bake physics colliders on a background thread too, and assign `sharedMesh` only after the bake completes (with a timeout fallback).
  6. Cap concurrent chunk builds with a semaphore-like counter to bound peak CPU/frame cost during a big stream-in.

---

### 3. Authored-Panel Orientation Solver (mesh-dressing pattern)

- **Purpose:** A general reusable technique for fitting AI-generated (Meshy) art assets of unknown/inconsistent orientation onto procedurally-placed grid geometry, without manual per-asset tuning.
- **Key files:** `Prefabs/AvalorAssetManager.cs` (`DressCompositeWithAuthored`, `DressTorchWithAuthored`, `DressPortalWithAuthored`), `Gear/AvalorWeapons.cs` (`DressWeaponWithAuthored`, `MeasureTipSign`).
- **Architecture — the core insight:** AI-generated meshes do not export with a consistent up-axis or front-facing convention. Rather than hand-tune Euler rotations per asset, the solver:
  1. **Finds the panel's own local axes from its bounding box**: the *thinnest* bounds axis is the "normal" (front-facing) axis; of the remaining two, the longer is the "long in-plane" axis.
  2. **Determines which face is the "detail" face** by measuring actual surface area on each half split at the bounding-box midpoint — a carved/detailed face has measurably more true surface area than a flat back, so front/back is derived numerically, not guessed.
  3. **For weapons:** additionally derives which end is the "grip" vs "tip" by comparing the average radial vertex spread on each half of the longest axis. Falls back to a manually-measured constant only if the mesh is non-readable or too symmetric to call.
  4. **Builds the exact rotation** via `Quaternion.LookRotation(targetNormal, targetInPlane) * Quaternion.Inverse(Quaternion.LookRotation(sourceNormal, sourceInPlane))` — composing two `LookRotation`s this way (rather than a single `FromToRotation`) pins *both* the facing axis and the in-plane twist/roll, fixing a bug where walls/weapons could land edge-on or rotated 90° around their own long axis.
  5. **Scale is derived from measured donor bounds**, never a hardcoded metre value.
  6. Grid tiles are deliberately **fitted oversized** (an explicit `SEAM`/`WALL_OVERHANG` constant) so neighbouring tiles interpenetrate slightly rather than merely abut — closes hairline seams the exact-fit tiling would leave.
  7. For double-sided walls, the same panel mesh is baked twice into one combined mesh (front + 180°-rotated copy) via `Mesh.CombineMeshes`, since winding/normals would invert incorrectly under a naive negative-scale mirror.
- **How to implement:**
  1. Given a mesh with an unknown authoring convention, compute `mesh.bounds.size` and determine the thinnest axis (normal) and longer of the remaining two.
  2. Numerically determine "front" by comparing surface area (or vertex spread) between the two halves rather than assuming a convention.
  3. Compose the target rotation from two `LookRotation` calls and take the relative rotation between them — pins both facing and twist in one shot.
  4. Compute scale as `targetWorldSize / measuredSourceAxisSize` (never a magic number).
  5. Position by matching a named landmark point between the target attach point and the transformed source bounds — not by naively parenting at the origin.
  6. Oversize tiled/grid-fit meshes slightly along seams to avoid hairline gaps from floating-point/authoring imprecision.

---

### 4. Live Atmosphere / Fog / Lighting System

- **Purpose:** Drive the maze's dark, foggy, green "crypt" look from *live* config values every frame, so a lighting/fog slider takes effect instantly without regenerating the persisted (ZDO-backed) maze.
- **Key files:** `Atmosphere/AvalorCryptEnvZone.cs`, `Atmosphere/AvalorPortalEnvZone.cs`.
- **Architecture:**
  - Lives on a `DontDestroyOnLoad` persistent manager root (not a world-anchored object), because a world-anchored fog volume streams out once the player wanders a zone or two away in a big maze.
  - **`Update()`**: checks whether the local player crossed into/out of the maze region and calls `EnvMan.SetForceEnvironment("Crypt")` — checked every frame on entry to catch the crossing on frame 1 and avoid a visible one-second "wrong biome" flash.
  - **`LateUpdate()`** (deliberately *after* the game's own environment manager writes `RenderSettings`, so this system's writes win): paints `RenderSettings.fogColor/fogDensity/fogMode/ambientLight` from the *current* config value, every frame. Key gotcha: Unity's `FogMode.Linear` **ignores density entirely** — must explicitly pin `fogMode = FogMode.ExponentialSquared`.
  - **Never read your own output as next frame's input.** An earlier bug fed `RenderSettings.ambientLight` back into itself every frame, causing runaway feedback compounding toward black/flicker. Fixed by computing every frame's result from a **fixed base constant**, never from the live `RenderSettings` value.
  - **Third mist source gotcha:** Valheim's `EnvMan` reuses an `EnvSetup`'s built-in particle systems per-environment; suppressing fog density and the mod's own mist props still left ambient particle emitters running — fixed via reflection into `EnvMan`'s private `m_currentPSystems` field to deactivate/reactivate them.
  - **`AvalorPortalEnvZone`** (used at the *overworld* portal clearing): a radius-based fade that blends toward murky fog the closer the player gets — samples the world's *own* current weather only while **outside** the zone and caches it, to avoid the same self-feedback bug.
- **How to implement:**
  1. Put your atmosphere driver on a persistent (non-streaming) singleton object, not a world-anchored trigger volume.
  2. Write render settings in `LateUpdate`, after the game's own environment system has run for the frame.
  3. Always compute the frame's output from a captured constant/base value plus the *current* setting — never read back and re-blend your own previous output.
  4. Explicitly set fog *mode* as well as density/color.
  5. If the platform's env system owns hidden built-in particle emitters, discover and toggle them via reflection.
  6. For localized fade zones, sample the "before" state only while genuinely outside the effect radius, and cache it.

---

### 5. Per-Prop Live Density Culling (torches/mist)

- **Purpose:** Let a density/brightness config slider affect *already-spawned, persisted* props instantly and reversibly, in both directions, without regenerating the maze.
- **Key files:** `Atmosphere/AvalorAtmoProp.cs`.
- **Architecture:** The core trick: generation always places the **maximum possible density** of a prop type (under a hard cap), and each instance derives a **stable pseudo-random 0..1 "bias"** from its own quantized world position (a position hash, not `Random.value`, so it's identical every session and for every client without networking). Each prop polls the live config once a second and computes `on = (bias < currentFraction)`. Because the sets are nested (anything lit at "Some" density is still lit at "Most"), raising the setting only *adds* visible props and lowering it only *removes* them — deterministically, live, with zero ZDO writes and no regeneration.
  - Torches additionally support an `AlwaysLitKey` per-*instance* ZDO flag.
  - Guards against re-fighting other systems that also touch light intensity: prefabs used with this component have their `LightFlicker`/`LightLod` components stripped, since writing `intensity` once a second while something else animates it every frame produced a visible "snap" — the fix was ownership, not scheduling.
- **How to implement:**
  1. Spawn every candidate prop instance at generation time (up to a hard cap), regardless of the current density setting.
  2. Give each instance a stable position-derived hash → `0..1` value.
  3. Each frame/poll, compute the fraction of props that should be visible from the live setting, and toggle a given prop on iff `hash < fraction`.
  4. Never let two systems both own a per-frame-varying field — strip/replace one of them.
  5. Cache "base" values once at spawn and always compute the live value from that base, never from what was last written.

---

### 6. Procedural Rune Texture + Material System

- **Purpose:** Generate glowing runic textures and a compatible additive/unlit material at runtime with **zero shipped shaders or texture assets**, so custom VFX can never render "pink".
- **Key files:** `Atmosphere/AvalorRuneTexture.cs`, `Atmosphere/AvalorRuneMaterial.cs` (explicitly ported from an earlier project, "Wings of the Valkyrie").
- **Architecture:**
  - **`AvalorRuneTexture`**: pure CPU pixel-pushing into a `Texture2D` via `SetPixel`/`SetPixels` — a hand-authored line/circle rasterizer draws 16 distinct rune glyph patterns, each rendered **twice**: a wide, low-alpha "glow" pass then a thin, full-alpha "stroke" pass. `GenerateRuneAtlas` packs all 16 into a 4×4 512×512 atlas for texture-sheet particle animation.
  - **`AvalorRuneMaterial`**: at runtime, searches for a **known-working stock shader** in priority order, with a scored fallback that scans all loaded materials by shader-name heuristics. Configures additive blending manually, caches built materials keyed by a packed `(shaderID, textureID, colorHash)` composite key.
- **How to implement:**
  1. Never ship a custom shader asset into a game whose render pipeline you don't fully control — find and reuse a stock/vendor shader by name at runtime instead.
  2. Build glyph/pattern textures procedurally via `Texture2D.SetPixel`/`SetPixels` + a simple line-and-circle rasterizer; a double "glow, then stroke" pass reads as "lit" rather than flat.
  3. Pack multiple variant textures into an atlas so one particle system material can display many variants.
  4. Cache generated textures and materials aggressively.

---

### 7. Layered Portal Rune VFX (particle-system gateway effect)

- **Purpose:** A convincing "magic portal" visual built entirely from stock `ParticleSystem`/`LineRenderer` components plus the procedural rune material above — no custom shader, no bundled VFX asset.
- **Key files:** `Atmosphere/AvalorPortalRuneVFX.cs`.
- **Architecture:** Four independent layers combined: **Veil** (a thin box-emitter particle system filling the doorway); **Rings** (two counter-rotating `LineRenderer` circles plus spoke lines, animated by directly setting `transform.localRotation` each frame); **Sigil** (a horizontal `LineRenderer` circle scribed on the ground plus a rim-emitting particle system); **Motes** (slow world-space embers drifting up).
  - **Charge-driven intensification:** an externally-driven `Charge` float (0..1, fed by a walk-in trigger while the player dwells in the volume) scales emission rate and color intensity, and decays on its own every frame so removing the driving input naturally settles the effect back to idle with no explicit "stop" call.
  - **Documented gotcha:** Unity's `ParticleSystem.VelocityOverLifetimeModule` silently discards the whole module if you set only one axis as a two-constant `MinMaxCurve` while leaving others at single-constant mode — *all three axes must be set in the same curve mode*.
  - **`LineRenderer` + particle shader gotcha:** particle shaders multiply texture by *vertex color*, so a `LineRenderer` left at default (unset) start/end colors renders invisible or muddy.
- **How to implement:**
  1. Compose several separate, simple `ParticleSystem`/`LineRenderer` "layers" rather than one system trying to do everything.
  2. Build ring/circle geometry procedurally for `LineRenderer`.
  3. Drive intensity from an externally-set 0..1 "charge" value that decays passively each frame, so the effect always self-settles.
  4. Ensure all axes of a `VelocityOverLifetimeModule` (or any multi-curve module) are set in the same `MinMaxCurve` mode.
  5. Explicitly set `LineRenderer.startColor`/`endColor` when using additive/particle-style materials.

---

### 8. Mob Director (dynamic proximity spawner + end boss)

- **Purpose:** A server-authoritative director that keeps the maze populated with enemies in front of and around each player, cleans up distant mobs, and gates an "end boss" behind approach to the exit.
- **Key files:** `Mobs/AvalorMobDirector.cs`.
- **Architecture:**
  - Runs only on `ZNet.instance.IsServer()` (headless-server-safe: does **not** additionally gate on `Player.m_localPlayer`, since a dedicated server has none — an earlier version silently broke spawning on dedicated servers by requiring one).
  - **Population model:** target mob count = `playersInMaze.Count * maxMobsPerPlayer` (difficulty-scaled). Every 5-second tick: cull mobs no player is within `MAX_MOB_DISTANCE` of (server-owned destroy after `ClaimOwnership()`), then top up to target by spawning near under-populated players.
  - **Forward-cone spawn placement (`TryFindOpenSpawn`):** most attempts bias spawn direction into a ±70° cone in the player's facing direction at 10–26m distance (so enemies appear *ahead* of travel — deeper maze felt "empty" otherwise); remaining fall back to a full ring closer in. Each candidate validated with a downward raycast (find floor) + `Physics.CheckSphere` (reject if geometry occupies the spot).
  - **20-second arrival grace period** before any spawning/attacking begins after the first player is detected, to avoid loading-screen deaths.
  - **Rare elite ("Revenant") and boss ("Warden") systems:** distinct spawn-chance-gated tiers with guaranteed unique loot, each stamped with a replicated ZDO flag because the mob's *display name* (`Character.m_name`) does **not** replicate over the network — every other client reads the ZDO flag in `Character.Awake` to independently apply the name/boss-bar locally.
  - **Boss lifecycle correctness (`ScanWardenState`)**: two naive pieces of in-memory state are each individually wrong in *opposite* directions — a live object reference goes null when the zone streams out (under-reporting "boss exists"), a plain bool resets on server restart (over-reporting "boss is dead"). The fix scans the *ZDO table* for a "warden killed" flag stored **on the exit portal's own ZDO** (so its lifetime is tied to the maze's own lifetime) combined with a scan for any live warden-flagged ZDO.
  - **AI "hunt" activation (`MakeItHunt`)**: `BaseAI.SetHuntPlayer(true)` alone does not wake a mob — must also explicitly `Alert()` and widen view/hear range. Must **not** be called from inside a `Character.Awake` Harmony postfix because Unity does not guarantee component `Awake` ordering on the same prefab — deferred via a one-frame pending queue drained on the next `Update()`.
- **How to implement:**
  1. Gate all server-authoritative population logic on `IsServer()` alone — never additionally require a local player.
  2. Compute target population from `activePlayers * perPlayerCap`, difficulty-scaled; cull-then-spawn each tick.
  3. Bias spawn point selection into a forward cone from the player's facing direction, validated by ground raycast + overlap check, with a full-ring fallback.
  4. Use a short arrival grace window before any hostile activity begins.
  5. For any state that must be visible on every network client but isn't itself replicated, stamp a small ZDO int flag and have every client apply the consequence independently from that flag in an `Awake`/spawn hook.
  6. Store "is this encounter beaten" state on an object whose lifetime matches the encounter's lifetime, not in a plugin-lifetime static field.
  7. Defer any AI/component cross-talk that depends on sibling components' `Awake()` having completed to the next frame via a pending-queue pattern.

---

### 9. Sub-Boss Mutation System

- **Purpose:** Turn ~15% of ordinary spawns into visually/mechanically distinct "starred" mini-bosses without authoring new creatures.
- **Key files:** `Mobs/AvalorMobModifier.cs`.
- **Architecture:** `MutateIntoSubBoss` bumps `Character.SetLevel()` (native star system, scales model size/stats), applies extra `localScale`, multiplies max health, and picks one of 5 hand-authored "mutation" variants combining a cosmetic name prefix with a genuine mechanical effect via `Character.m_damageModifiers` or stat multipliers — deliberately avoiding "just a recolor with no mechanical identity."
- **How to implement:** Layer a small table of named "affix" variants, each pairing a display-name prefix with one concrete damage-modifier or stat change (never purely cosmetic), applied *after* the base game's own tier/star system so effects compound.

---

### 10. Custom Boss-Bar HUD

- **Purpose:** A screen-space health bar for tracked rare/boss enemies, built entirely from runtime-constructed `uGUI` objects (no bundled UI prefab).
- **Key files:** `Mobs/AvalorBossBar.cs` (ported near-verbatim from an earlier mod, "BlightedHeart").
- **Architecture:** Builds a `RectTransform` hierarchy by hand parented under the host game's existing HUD root; the **fill bar is scaled on X from a left-pivoted RectTransform** rather than `Image.fillAmount`, since `fillAmount` requires sprite fill-mode metadata a bare runtime `Image` doesn't have. **Font is borrowed from an existing HUD `Text` component** via `GetComponentInChildren<Text>()` rather than shipped. Health is read from the tracked character's replicated `GetHealth()/GetMaxHealth()`, so a boss bar for a mob owned by a different network peer just works with no custom networking.
- **How to implement:** Prefer scaling a left-pivoted `RectTransform` on one axis for a fill/progress bar over `Image.fillAmount`; borrow the host application's own font/style off an existing live UI element; read progress from the game's own replicated getter methods rather than adding custom networked state.

---

### 11. Cross-Mod AI Compatibility Layer

- **Purpose:** Detect and neutralize interference from a *different, unrelated* mod ("Shadows of Midgard" — a stealth/AI-replacement mod) that would otherwise silently make this mod's monsters passive, without a hard assembly reference.
- **Key files:** `Compat/AvalorStealthCompat.cs`, `Compat/AvalorStealthGuards.cs`.
- **Architecture:**
  - **Reflection-only detection**, deferred to the *first `Update()` frame*, never `Awake()` — a chainloader instantiates plugins one at a time, and probing for another plugin's types during `Awake()` can run before that plugin's assembly is even loaded, permanently latching a false "not installed" conclusion. Deferring to `Update()` is safe since Unity cannot execute a frame until every plugin has finished loading.
  - **Cooperation flag, but never depended on:** the "clean" path is a documented ZDO flag (`SoMStealthExemptKey`) a cooperating version of the other mod honors. It is stamped and verified at runtime (`StealthExemption.IsExempt`), but as of **v0.0.9 it no longer switches anything off** — see the revision below.
  - **Point-of-use pinning (`AvalorStealthGuards`)**: rather than fighting the other mod's state on a timer (tried first, documented as *not working*), the fix re-writes the contested state via **Harmony prefixes on the exact same methods** the other mod patches, using `[HarmonyPriority(Priority.First), HarmonyBefore("othermod.harmony.id")]` (and matching postfixes with `Priority.Last`/`HarmonyAfter`) so this mod's write always lands *immediately before* the other mod's own logic reads it.
- **How to implement:**
  1. Never probe for another mod's types during your own plugin's earliest init hook; defer to the first per-frame update tick.
  2. If forced to fight another mod's per-frame-recomputed state, patch the *same methods* it patches with explicit Harmony priority/ordering rather than racing it on an independent timer.
  3. Guard every such patch with a near-free first-line early-out so the cost against players without the conflicting mod installed is a single branch.

#### v0.0.9 revision — three lessons worth carrying to any interop layer

1. **A conditional remedy has as many silent failure modes as it has conditions.** Every "decide whether to
   intervene" branch here failed the same way — silence — and between them they covered every install. The
   no-stealth-mod branch did nothing on the theory that vanilla AI was already correct; it is not, because vanilla
   alertness **decays**, and in corridors that break line of sight constantly a one-off `Alert()` at spawn wore off
   within seconds. The cooperative branch stood down entirely and inherited the same decay. The mod now intervenes
   **unconditionally** and the detected mode only selects *how* (whether the extra reflection write is available)
   and keeps the log honest. Cheap universal remedy beats clever conditional remedy.
2. **A self-test that measures your own patch is worse than no self-test.** The layer had an "alert probe" that
   called `Alert()` and read `IsAlerted()` straight back to prove the other mod was not discarding it. That was
   valid only while nothing of *ours* patched `IsAlerted`. Once the guards became universal they forced that exact
   getter true, so the probe read our own postfix and reported success unconditionally — while still printing
   reassuring green lines. Deleted. Keep only probes whose answer is independent of your own intervention.
3. **Forcing a getter true can disable the setter that depends on it.** Vanilla is
   `void Alert() { if (m_nview.IsValid() && !IsAlerted()) SetAlerted(true); }`. Forcing `IsAlerted()` true makes
   `Alert()` a permanent no-op, so `SetAlerted` — which is where the **animator bool**, the ZDO replication and the
   alert effects live — never runs. The visible result is monsters that chase and attack while playing their idle
   animation. The fix is a re-entrancy bracket (`BeginRealAlertRead`/`EndRealAlertRead`) that stands the postfix
   down for exactly the length of your own call, so vanilla reads the true field once and does the whole job.
   **Generalise:** before force-overriding a getter, grep for setters guarded by that same getter.

**See also:** `avalor-patches-are-registered-by-hand` — this entire guard file was never added to the plugin's
manual `ApplyPatch` list, so all of it was dead code from the day it was written and only the losing timer ever
ran. The design was right and simply was not switched on. A patch class is not registered by its attribute.

---

### 12. Portal / Teleportation Mechanics

- **Purpose:** Walk-in (not just interact-key) portal travel between the overworld and the maze dimension, safe against falling into the void during first-time generation or collider-streaming races.
- **Key files:** `Portal/AvalorPortal.cs`.
- **Architecture:**
  - **Walk-in charge trigger:** `OnTriggerStay` on the local player accumulates a charge timer while inside the gate volume (feeding the VFX system's `Charge` value live), fires teleport once `ChargeTime` (1.1s) reached, only reacts to the **local player**. A global `ReArmDelay` (6s) suppresses instant round-trip bounces.
  - **Safe-teleport sequencing (`TeleportRoutine`)**: if the maze doesn't exist yet, message the player and kick off generation as a coroutine; `yield return new WaitUntil(() => !IsGenerating)`; then — critically — **spawn a temporary primitive-cube safety platform under the destination first**, destroy after 12s, *then* teleport, so the player can never fall through the void while the target chunk's collider is still baking/streaming. Uses non-"distant" teleport to avoid a black-screen hang.
  - **Shared travel logic**: a single static method decides direction purely from whether the *gate's own position* is inside the maze region or not, used identically by both walk-in and interact-key paths.
- **How to implement:**
  1. Drive portal activation from a trigger volume's `OnTriggerStay` dwell timer, filtered to only the locally-controlled player.
  2. Before teleporting into a location whose geometry might still be generating/streaming, spawn a short-lived safety-platform primitive under the destination and destroy it only after a generous buffer past the teleport.
  3. Wait on an explicit "is generation complete" flag/coroutine rather than assuming synchronous completion.
  4. Centralize "where does this gate lead" logic in one function keyed off gate position/region.
  5. Add a short global re-arm suppression window after any teleport.

---

### 13. Overworld Portal Site Generator (placement + decoration + path grading)

- **Purpose:** Procedurally chooses a valid site for the entrance portal near a fixed landmark, then dresses a full "processional avenue" (paved road, torches, dead trees, gravestones, ruins, mist) leading to it — entirely algorithmically.
- **Key files:** `Portal/AvalorPortalSpawner.cs` (701 lines).
- **Architecture:**
  - **Stone-gap-aware placement**: scans nearby colliders for "ring stone" prefabs, computes each one's angle from center, sorts, finds the **widest angular gap** — the landmark's natural "doorway." Portal placed inside that gap's cone, scored by a dryness/flatness heuristic (`ScoreSpot`); falls back to sweeping the full 360° ring if the gap cone is unusable.
  - **Graded processional path**: walks from landmark to portal in fixed steps, linearly interpolating between the two endpoints' *measured ground heights*, laying road tiles/torches at the interpolated height, and dropping terrain-flattening discs along the way — with an explicit blend ramp near the landmark so the avenue grows organically out of natural terrain.
  - **Sequencing gotcha (repeated warning):** terrain-modifying components only take effect in their own `Start()`, one frame after `Instantiate`, and the heightmap rebuild takes time — so sampling ground height too soon after placing a leveler reads the **pre-edit** height. Fix pattern: `yield return new WaitForSeconds(N)` between "place terrain-editing objects" and "sample ground height for anything seated on that terrain."
  - **Plotted, non-random decor layout**: gravestones/ruins placed on a deterministic grid of lateral/longitudinal offsets, specifically because random placement occasionally landed decor *in* the walkway or overlapping other decor.
  - **Repeated vegetation re-sweep**: re-runs `ClearAreaVegetation` several times over several seconds since new trees pop in as the zone finishes streaming.
  - **Protected-object guard (`IsProtected`)**: checked by *component type first* (`TombStone`, `Container`, `Player`) before falling back to substring name matching, specifically because a naive substring filter ("stone") previously matched and destroyed `Player_tombstone` objects — i.e., it deleted player graves during a routine vegetation sweep. A serious historical bug and the fix.
- **How to implement:**
  1. To place a new structure "in the gap between" existing landmark pieces, gather nearby matching colliders, compute angular position from the landmark center, sort, find the largest angular gap.
  2. Score candidate placement points on multiple criteria rather than accepting the first hit; sweep progressively wider search patterns if the preferred region fails.
  3. When grading a path between two points at different heights, interpolate the *intended* height along the path and only bring real terrain into agreement with it via flattening discs.
  4. Always insert an explicit wait between issuing a terrain edit and sampling ground height for anything that must be seated on the result.
  5. For decor scattered around a procedurally-placed structure, use a deterministic slot grid instead of random polar coordinates whenever multiple systems need to reason about "what's near what."
  6. When writing an area-clearing/protection filter, always check by concrete component type before falling back to substring name matching.

---

### 14. Cross-Zone Terrain Editing System

- **Purpose:** Apply a large (tens-of-meters) terrain flatten/smooth edit that correctly spans multiple of the host game's internal terrain-chunk boundaries, when the host engine resolves a terrain edit to only the single chunk containing the edit's center point.
- **Key files:** `Portal/AvalorTerrainOps.cs`, `Portal/AvalorTerrainFlattener.cs`, `Portal/AvalorPathLeveler.cs`.
- **Architecture:** Valheim resolves a terrain-modifier op to exactly one per-zone terrain-height-delta buffer keyed by the op's *center* position; any part of a large-radius op's footprint that falls in a neighboring zone is silently never written, producing a hard vertical seam along the (invisible) zone grid line. Fix: instead of one large `TerrainModifier` with a big radius, **emit a grid of many small, overlapping `TerrainModifier` ops**, all set to the same target height, tiered into a dense inner grid (flat pad) and a sparser outer ring (feather) — each grid point resolves to whichever zone it happens to fall in. A "keep-out" exclusion lets a large flattening operation skip a protected radius around another sensitive structure.
  - `MatchedGroundHeight` computes the *target* flatten height as a distance-weighted average of many ring-sampled surrounding ground heights (rather than the arbitrary height the site happened to be at), minimizing total cut+fill.
- **How to implement:**
  1. If your terrain system resolves an edit to a single chunk based on the edit's center, decompose a footprint-crossing edit into a grid of many small overlapping ops all targeting the same result value.
  2. Tier the grid density: dense/overlapping where exactly flat is required, sparse in the blend-into-surroundings feather zone.
  3. Compute a flatten target height as a weighted average of sampled surrounding terrain, not the arbitrary height the site was found at.
  4. Support an explicit keep-out exclusion radius around any other terrain-sensitive structure your edit's footprint might overlap.

---

### 15. Jotunn Prefab-Cloning Pipeline ("clone, strip, re-skin" pattern)

- **Purpose:** The umbrella pattern used to create every custom object in the mod (walls, floors, chests, traps, torches, portals, weapons) without shipping full custom Unity prefabs.
- **Key files:** `Prefabs/AvalorAssetManager.cs` (1750 lines), `Prefabs/AvalorKitBundle.cs`.
- **Architecture:**
  - Everything registered inside a callback (`PrefabManager.OnVanillaPrefabsAvailable`) — **never during the plugin's earliest init hook** (an explicitly documented past bug: weapons silently failed to register because they were wired to fire at plugin `Awake()`).
  - Each object: `CreateClonedPrefab` → strip components that bring unwanted native behavior (`WearNTear`, `Piece`/`Container`, `Rigidbody`, particle/light components) → either **re-skin the material** by cloning a known-good donor material and swapping in a custom albedo texture (same "never ship a custom shader" principle as System 6, applied to opaque geometry) or **swap the mesh** by disabling the donor's `MeshRenderer`s (preserving other renderer types) and parenting a new child with the authored mesh fitted via System 3's orientation solver.
  - **`MakeStaticDecor`**: a shared "neuter this into pure decoration" helper — strips interactive/behavioral components from the whole hierarchy (not just root), forces any `Rigidbody` kinematic before removing it, strips particle/light/projector components.
  - **Composite/mega-tile prefabs** built once at registration time by combining N placements of a small donor tile into one static mesh, giving the runtime chunk-streaming renderer (System 2) a single-mesh source to re-instance from.
  - Every custom-object creation function wrapped in its own `try/catch`, logging a named warning on failure — because an earlier all-or-nothing sequential registration meant one bad prefab clone silently aborted every clone attempted after it.
- **How to implement:**
  1. Only clone/register custom prefabs after the host game's own default asset tables have loaded.
  2. Clone from a donor object whose material/shader/physics behavior is already known-correct.
  3. Strip unwanted native behavior components explicitly (search the *whole hierarchy*).
  4. To re-skin appearance, prefer swapping only the *texture* on a clone of a known-good material.
  5. Wrap each independent object's registration in its own exception handler.
  6. For any "grid of many small tiles" system, do the initial component-mesh combine once at registration time.

---

### 16. Weapon "Donor Transform" Stat System

- **Purpose:** Design and implement new custom weapons whose stats stay balanced relative to the base game even as the base game patches/rebalances, by expressing every stat as a *multiplier or transform of a cloned donor's own stats*, never as an absolute typed-in number.
- **Key files:** `Gear/AvalorWeapons.cs`, `AVALOR_WEAPONS_SPEC.md`.
- **Architecture:** Every stat mutation reads the donor's already-cloned value and multiplies/transforms it in place, rather than assigning a literal constant — the base game's real item stats live in binary asset bundles the author cannot statically verify, so a hand-typed absolute number would be *recollection*, not *verification*, and would silently drift out of balance on the next game patch. A dedicated helper sums whatever elemental damage types the donor carries and reassigns the *total* to a different damage type, written generically ("whatever element") so it keeps working if the donor's element changes.
  - **Repairable-but-uncraftable item pattern**: registers a genuine, enabled, self-referencing recipe so the game's own repair-lookup succeeds, then **hides it from the crafting UI's recipe list build** via a Harmony patch on `InventoryGui.UpdateRecipeList` (filtering the item out of the passed list) — deliberately *not* achieved by disabling the recipe (which would also break repair) and deliberately targeted at the UI build step rather than the player's "available recipes" query.
  - **Custom equip-stat bonus with no native hook**: implemented by postfixing the single central method the game itself uses to sum total max HP/stamina/Eitr (`Player.GetTotalFoodValue`), checking the currently-wielded weapon and adding the bonus there — meaning the game's own subsequent stat application, UI, and cross-client replication all just work.
- **How to implement:**
  1. Express every stat change as a multiplier applied to the cloned donor's *own* value, never a typed literal — keeps the item correctly balanced across game patches automatically.
  2. For "type-shifted" damage/stat transforms, sum whatever the donor currently has and reassign the total (scaled) to the target category.
  3. To make an item repairable-but-not-craftable: register a genuine, enabled, self-referencing recipe, then filter it out specifically at the UI list-building step.
  4. For a stat category the game has no dedicated modifier hook for, find the single central method the game itself uses to *compute* that stat from all its native sources, and postfix/adjust the result there.

---

### 17. Banded / Depth-Scaled Loot Table System

- **Purpose:** Chest loot that varies meaningfully by chest tier and how deep into the procedural maze it sits, drawn from a real pool.
- **Key files:** `Gear/AvalorLootManager.cs`.
- **Architecture:** Three "bands" of material pools gated into a working pool by both the chest's fixed `tier` and its **continuous depth** (`depth01`) — deeper *or* higher-tier chests progressively unlock the mid/deep bands, weighted in by being appended to the candidate pool list *multiple times* (a deliberately simple, cheap weighting mechanism). Distinct-item sampling-without-replacement avoids duplicate stacks reading as a bug. Item insertion clones the donor `ItemData` directly and splits any amount exceeding the item's own max stack size across multiple `AddItem` calls — fixing a bug where a single oversized stack was silently rejected by the inventory API.
- **How to implement:**
  1. Structure loot as tiered pools; gate which pools are available by a continuous progression metric blended with a discrete container tier.
  2. Weight a pool cheaply by duplicating entries in the candidate list.
  3. Sample distinct entries via retry-with-a-cap against a "already used" set.
  4. When granting items programmatically, clone the donor `ItemData` and manually split any requested amount across multiple `AddItem` calls respecting max stack size.

---

### 18. Persistent Diagnostic Logger

- **Purpose:** A crash/bug-report-friendly log that survives across game relaunches, because the host game's standard log file is truncated on every launch.
- **Key files:** `Core/AvalorDiagLog.cs`.
- **Architecture:** Hooks the logging framework's own listener interface (`BepInEx.Logging.Logger.Listeners.Add(new Sink())`) rather than wrapping every call site — captures Harmony patch and third-party framework output too, with zero call-site changes. Filters to lines mentioning the mod's own name. **Appends** (never truncates), with a size-based roll-over (8MB cap, single backup), resolves the output path dynamically from the framework's own config-path API, and every write is guarded with "first failure disables it permanently."
- **How to implement:**
  1. If your host framework exposes a logging *listener* interface, hook that once rather than duplicating log calls at every site.
  2. Filter the mirrored log to your own component's messages via substring check.
  3. Append rather than truncate, with a size cap and single-rollover-backup, and resolve the output path from the host framework's own API.
  4. Guard every write with a "disable self permanently after first failure" latch.

---

### 19. Config System (BepInEx + Jotunn admin-sync + live-reactive settings)

- **Purpose:** Server-authoritative, live-adjustable configuration that cannot be used as a client-side cheat, while still allowing purely-cosmetic/local settings to be per-player.
- **Key files:** `Core/MistsofAvalorPlugin.cs`.
- **Architecture:** Every setting that shapes *world rules* is bound with `ConfigurationManagerAttributes { IsAdminOnly = true }`, which pushes the server's value over any connecting client's local override and prevents non-admin clients from changing it locally — important because `LightingLevel`/`MistDensity` are read *live, every frame, on the client* to drive fog/ambient, so an unsynced value would let any player self-remove difficulty by editing their local config, while everyone else in the same corridor experienced the real setting. Purely local/cosmetic settings are deliberately left unsynced.
- **How to implement:**
  1. Classify every config value as "shapes shared world state/rules" (must be server-authoritative and synced, especially if read live for anything gameplay-visible) or "purely local/cosmetic" (leave unsynced).
  2. Double-check any setting that's read *live, every frame, client-side* for a visible gameplay effect — that's exactly the shape of setting a player could otherwise exploit.

---

### 20. World-State Reset / ZDO-Wipe System

- **Purpose:** Cleanly and completely destroy and regenerate the entire procedural maze region without leaking objects, without racing generation, and without touching anything outside the maze's own reserved world-space pocket.
- **Key files:** `Core/AvalorResetManager.cs`, relevant sections of `Maze/AvalorMazeGenerator.cs`.
- **Architecture:**
  - **Region-bounded wipe, not tracked-list wipe:** rather than trusting an in-memory "objects I spawned" list, does a **full scan of the game's replicated-object table** filtered to the reserved world-space region — catches unloaded/distant objects a purely in-memory list would miss.
  - **Ownership-before-destroy gotcha (a real bug that shipped and was diagnosed from logs):** the "destroy" call is a **silent no-op on an object the current session doesn't own**; most of a large, distant, unloaded maze is unowned by any currently-connected session — a naive "destroy everything in region" wipe reported a plausible count and did *nothing at all*. Fix: explicitly `ClaimOwnership()`/`SetOwner(sessionId)` before every destroy call.
  - **Auto-reset scheduling** runs only on the server, checked *every tick* (unlike admin console commands, an `Update()` loop runs identically on every connected peer and would otherwise have every client independently racing to wipe/regenerate), polls at a coarse interval, defers if any player is currently near the maze.
  - **In-progress-generation interruption:** before wiping, any currently-running generation coroutine is explicitly stopped, or it would keep spawning new chunks/clutter *behind* the wipe.
- **How to implement:**
  1. For any "clear this whole region" operation in a networked/replicated-object system, scan the full replicated object table filtered by spatial region rather than trusting an in-memory list.
  2. Before calling a networked object's "destroy," explicitly claim/transfer ownership to the current session first if the API silently no-ops on objects you don't own.
  3. Gate any per-tick auto-scheduling logic on "am I the authoritative server" checked every tick, since an `Update()`-style loop runs identically on every peer.
  4. Before wiping/regenerating, explicitly stop any in-progress generation coroutine/process.

---

### 21. Grave Relocation System

- **Purpose:** When a player dies inside the procedural dungeon, their grave/tombstone is automatically and reliably relocated to a fixed overworld "graveyard" near the entrance.
- **Key files:** `Core/MistsofAvalorPlugin.cs` (`Avalor_TombstoneToGraveyard_Patch`).
- **Architecture:**
  - **Component-Awake-ordering race, and the coroutine fix:** a Harmony postfix on the tombstone's `Awake()` cannot safely read authoritative network state or position inline, because the networking component's own `Awake()` isn't guaranteed to have run yet on the same frame. Fix: the postfix only *starts a coroutine* that polls until the network handle is valid, then performs relocation separately (coroutines can't `try/catch` around a `yield`, so wait and act are split into two methods).
  - **Authoritative-position gotcha:** `transform.position` can read `(0,0,0)` at the exact moment of `Awake()`; the fix reads position from the network object's replicated position field instead.
  - **Fighting the base game's own "snap grave back" logic:** the base game periodically snaps a grave back if it drifted far from its *recorded* death/spawn-point field. Simply moving the grave's position without also **re-stamping the grave's own recorded spawn-point field** meant the base game's own correction logic fought this relocation. Fix explicitly re-stamps that field alongside the position move.
  - **Deterministic, non-stacking plot layout:** graves placed on a fixed, walked-in-order plotted grid (same pattern as System 13's decor), so this system and decor-placement never contend for the same ground. A fixed-count overflow fallback handles every plotted slot being occupied.
- **How to implement:**
  1. Never read a networked object's authoritative position or handle directly inside its own `Awake()` postfix if a sibling component might not have finished its own `Awake()` yet — defer via a polling coroutine, splitting "wait" from "act."
  2. Prefer a replicated/authoritative position field over the live `transform.position` when acting immediately after spawn.
  3. When relocating an object that the host system periodically "corrects" back toward some recorded reference point, find and update that recorded reference field too.
  4. For placement of many same-purpose objects around a shared landmark, use a fixed, ordered plot grid instead of random polar placement whenever non-overlap or coordination between systems matters.

---

### 22. Console Command Framework

- **Purpose:** Admin/debug tooling for forcing resets, teleporting, spawning, inspecting, and repairing world state.
- **Key files:** `Commands/AvalorCommands.cs`, `Commands/AvalorInspect.cs`.
- **Architecture:** Each command a small class implementing the mod framework's `ConsoleCommand` base, registered via `CommandManager.Instance.AddConsoleCommand(...)`. Includes a **maze cutaway/X-ray inspection tool**: toggles `MeshRenderer.enabled` on any object whose *name* carries a `_Combined` suffix and whose position falls in the maze region, re-applied on a timer by a small persistent driver component since chunks continuously stream in/out.
- **How to implement:** Implement each command as a small self-contained class with explicit cheat/network/server-only flags. For debug visualization applied to dynamically-streamed content, re-apply on a timer/driver component rather than once.

---

### 23. UnityBundleBuilder Asset Pipeline (Editor tool)

- **Purpose:** A one-click Unity-Editor menu tool (`Avalor → Build Kit AssetBundle`) that converts a folder of raw AI-generated `.glb` model files into a single, small, embeddable AssetBundle containing script-created readable meshes plus downscaled/compressed albedo-only textures.
- **Key files:** `UnityBundleBuilder/Editor/AvalorBundleBuilder.cs`.
- **Architecture / pipeline stages:**
  1. **Discovery:** recursively finds every `.glb`.
  2. **Mesh flattening:** instantiates each GLB, `Mesh.CombineMeshes`s all children into **one fresh, script-created `Mesh` asset** — *not* just re-exporting the imported mesh reference, because glTF/glTFast-imported meshes default to **Read/Write disabled** (`mesh.vertices` returns empty at runtime); a script-created combined mesh is always CPU-readable from a bundle, which the maze mesh-combiner (System 2) depends on.
  3. **Texture extraction:** finds the base-color/albedo texture by scanning the shader's exposed properties for likely names while excluding likely non-albedo maps.
  4. **Downscale + compress (`BakeCompressed`)**: blits through a temporary `RenderTexture` at reduced resolution (rounded to a multiple of 4 for DXT block compression), reads back, compresses to `DXT1`. Explicitly **no normal maps are ever shipped** — Meshy already bakes shading/AO into base-color, and a raw exported normal map risks the host game's texture-space swizzle convention rendering it wrong; albedo-only-plus-host-lighting is the documented "highest-success, smallest-bundle" tradeoff.
  5. **Per-asset texture-size budget:** most tiles get a shared 2048px cap; specific "hero" assets get a 4096px budget, reasoned per-asset as "walked past constantly" or "held in hand and viewed close" — instance count is irrelevant since *shared* materials cost nothing extra per instance.
  6. **AssetDatabase registration + bundle build:** tagged with `SetAssetBundleNameAndVariant`, then `BuildPipeline.BuildAssetBundles`.
  7. **Downstream integration:** the built bundle is embedded directly into the compiled DLL via an `<EmbeddedResource>` MSBuild item with `LogicalName` set to the bundle's exact name, loaded at runtime via `AssetUtils.LoadAssetBundleFromResources(name, assembly)` — the whole art kit ships as bytes inside the single mod DLL.
- **How to implement (rebuilding this pipeline in a fresh project):**
  1. Write an Editor-only static class with a `[MenuItem]` method; guard all `UnityEditor` usage behind an editor-only compile/project split.
  2. For any imported 3D model whose mesh needs to be read from at runtime, re-bake it into a fresh script-created `Mesh` asset at build time rather than shipping the raw imported mesh reference.
  3. Locate a material's base-color texture by scanning the shader's declared texture properties for likely names while excluding likely non-albedo maps.
  4. Downscale and compress textures via a `RenderTexture` blit + `ReadPixels` + `EditorUtility.CompressTexture`, rounding dimensions to the compression format's required block multiple.
  5. Maintain a small explicit per-asset-name override table for texture resolution budgets, reasoning by "is this asset ever viewed up close / held / one-off" rather than instance count.
  6. Tag generated assets with `SetAssetBundleNameAndVariant` and call `BuildPipeline.BuildAssetBundles`.
  7. To ship the bundle without a separate distributable file, embed it into the compiled assembly as an `EmbeddedResource` with an explicit `LogicalName`.

---

### 24. Meshy AI 3D-Generation Skill/Workflow

- **Purpose:** A documented, reusable Claude-agent "skill" capturing the practical operating knowledge for using the Meshy text/image-to-3D API to author the game's raw mesh kit — not itself game code, but the upstream art-generation tooling/process.
- **Key files:** `.agents/skills/meshy/SKILL.md`.
- **Key operational facts documented:**
  - Two-stage async workflow: submit a `preview` task (bare untextured geometry), poll until `SUCCEEDED`, then submit a `refine` task against the same `preview_task_id` (which actually generates UVs and PBR textures) — **never re-run the preview stage to retry a failed refine**.
  - `ai_model` must be `"meshy-6"`; `texture_resolution` must be the **string** `"2k"/"4k"/"8k"`, not a numeric pixel value.
  - There is **no free API retry** — every API submission is billed regardless of output quality.
  - `negative_prompt`/`seed`/`symmetry_mode`/`texture_richness` are deprecated no-ops; the *only* lever on generated geometry is the prompt text itself (capped at 600 characters).
  - The generator "strongly prefers solid volumes" and will quietly fill in requested open negative space unless the prompt describes the *empty region itself* as the subject.
  - **Output orientation and up-axis are not stable** between generations — must be derived per-model from measured bounding-box aspect ratios rather than assumed.
  - Documents a concrete verification recipe: parse the downloaded `.glb`'s JSON chunk for vertex/triangle counts and bounds, then rasterize triangles into an ASCII grid using a **barycentric point-in-triangle test** (explicitly *not* a bounding-box fill, which over-reports coverage and hides an actually-missing hole).
- **How to implement (reusable takeaways for any AI-mesh-generation pipeline):**
  1. Treat "preview" and "refine" stages as strictly separable and separately billed; cache preview task IDs so a failed refine can be retried against the existing preview.
  2. Never trust the model's self-reported orientation/up-axis; measure it from the actual bounding box dimensions of each individual generated asset.
  3. Build a lightweight local verification step (parse mesh bounds + rasterize a silhouette with a proper point-in-triangle test) to catch "technically succeeded but geometrically wrong" outputs before any integration work is invested.
  4. When a generator claims certain parameters but documentation/testing shows they're deprecated no-ops, keep that fact recorded prominently.

---

### 25. Draggable / Resizable / Persisted HUD Widget (no EventSystem, no sprites)

- **Purpose:** A HUD element the player can move and resize in-game, whose layout survives a restart. Added in v0.0.9 for the exit compass, which is drawn as an angled top-down disc (a compass rose seen from 60° above the horizontal).
- **Key files:** `Atmosphere/AvalorExitCompass.cs`. Shares its build recipe with `Mobs/AvalorBossBar.cs` and `Mobs/AvalorGraceCountdown.cs`.
- **Architecture:**
  - **Nothing is a sprite.** A bare `Image` with no sprite assigned renders as a solid rectangle, which is enough to build every primitive needed: an ellipse is N small quads positioned *and rotated to the local tangent*, ticks are quads, markers are quads rotated 45° into diamonds. No atlas, no bundle asset, and no custom shader — so none of it can render magenta (see System 15 and the pink-primitive note).
  - **The 3D read comes from one number.** Foreshortening is `sin(tiltDegrees)` applied to every Y coordinate: 90° leaves a true circle, lower angles lay the disc over into an ellipse. The tangent-derived segment length must be recomputed with it, or the ring reads as beads at the sides and gaps at the top.
  - **Dragging without the EventSystem.** Hit-testing is `RectTransformUtility.RectangleContainsScreenPoint(rt, Input.mousePosition, cam)` with `cam` = null for `ScreenSpaceOverlay` canvases and `canvas.worldCamera` otherwise. `blocksRaycasts` stays **false**, so the widget never eats clicks meant for the game's own UI.
  - **The Valheim-specific trap: the hardware cursor is LOCKED during play**, which pins `Input.mousePosition` to screen centre — a mouse-only implementation cannot be dragged while the player is actually playing. Releasing the lock from `LateUpdate` works only by being the last writer that frame, which is a race, not a guarantee. So the widget ships **two input paths under one modifier**: mouse drag (nice) *and* arrow-key nudge + `+`/`-` resize (cannot fail).
  - **Persistence is just config.** Position/size/opacity/tilt are ordinary BepInEx `ConfigEntry` values, written live and `Config.Save()`d **once on modifier release** — never per frame, since each save hits disk and a drag is 60 of them a second. Deliberately *not* admin-synced: a HUD position is per-player.
  - **Anchor at the screen centre** (`anchorMin = anchorMax = (0.5, 0.5)`) so a saved offset means the same thing at every resolution.
- **How to implement:** build primitives from sprite-less `Image` quads; derive an angled projection from a single `sin(tilt)` factor and rebuild geometry when *either* size or tilt changes; hit-test rects directly instead of adopting the EventSystem; always provide a keyboard path in a game that locks the cursor; save config on interaction *end*.

---

### 26. Mod-Placed Building Pieces: the two components that delete your object

- **Purpose:** Making a prefab cloned from a `piece_*` donor survive as mod-placed scenery. This is the root cause of the v0.0.9 "the end-of-maze chest was never there" bug and is worth its own entry because the two failure modes look identical and have different fixes.
- **Key files:** `Prefabs/AvalorAssetManager.cs` → `MakeIndestructibleContainer()`.
- **`WearNTear` → the object destroys itself.** It demolishes a piece it judges unsupported. A floor built as a combined `MeshCollider` is not a recognised building piece, so *anything standing on one* is judged unsupported and wears to death within seconds. **The tell is that your spawn log is correct** — the object really was placed; it simply is not there by the time a player arrives, which sends you hunting a placement bug that does not exist.
- **`Piece` → a player deletes it with a hammer, and removing `WearNTear` does not prevent this.** `Player.RemovePiece` is an if/**else**: with no `WearNTear` it falls through to `"Removing non WNT object with hammer"` and calls `ZNetScene.Destroy` outright. So WearNTear-less + Piece-ful is the worst combination — immune to damage, one click from oblivion. A no-build Harmony patch does not help; those patch `PlacePiece`, not removal.
- **Removing `Piece` from a `Container` needs one precaution:** `Container` caches `m_piece` in `Awake` and dereferences it in exactly one place — `CheckAccess` under `PrivacySetting.Private`. Pin `m_privacy = Public` (and `m_checkGuardStone = false`) rather than trusting the donor's default.
- **How to implement:** strip **both** components in one shared helper every clone site routes through, rather than as a property each site must remember. In this codebase three chest prefabs existed and only two had the fix — the one that was missed was the single most important container in the mod.

---

### 27. ZDO-Driven Mob Director (the dedicated-server pattern)

- **Purpose:** A server-authoritative spawn director that works identically on a player-host and a HEADLESS dedicated server — where there are no `Player` instances, no physics colliders at the play area, and Character references die seconds after every spawn. Added in v0.1.0.
- **Key files:** `Mobs/AvalorMobDirector.cs`, `Core/AvalorNet.cs` (presence helpers), `Maze/AvalorMazeGrid.cs` (placement).
- **Architecture — four substitutions, one rule ("operate on ZDO data, never on instances"):**
  - **Presence:** `ZNet.GetAllCharacterZDOS()` → position + rotation + owner uid per connected player (fresh to the physics tick), filtered to the play region. Replaces `Player.GetAllPlayers`, which lists only locally instantiated players (nobody, on a headless box).
  - **Tracking:** `List<ZDOID>` instead of `List<Character>`. Liveness = `ZDOMan.GetZDO(id) != null` — mob death destroys the ZDO on the owner and the destroy replicates, so a vanished ZDO IS the death signal. Boss variant: clear the tracked id in the wipe path BEFORE any ZDO dies, so "vanished while tracked" can only mean killed → safe place to stamp kill flags (no-refarm).
  - **Placement:** decode replicated world data (System 28) instead of raycasting. Cell centers of open cells are clear by construction — strictly better than the raycast it replaced.
  - **Culling/clearing:** `FindInstance→ClaimOwnership+Destroy` when an instance exists, else `SetOwner(GetSessionID())+DestroyZDO` (DestroyZDO on an unowned ZDO is a silent no-op).
  - **Spawn flow headless:** server `Instantiate`s (ZDO created; all replicated mutations written while the server owns it; `Alert()` state lands in the ZDO) → **`zdo.SetOwner(targetPlayerUid)` as the LAST step** → the server's instance is culled harmlessly; the hunted player's client instantiates from the ZDO on its next objects update and simulates the AI immediately (skipping the engine's ~2.5s ownership sweep). Component-level mutations (names, immunities, drops) are rebuilt per-client by the `Character.Awake` patch from ZDO flags (System 24's replication split is the load-bearing partner).
  - **Restart adoption:** monster ZDOs are persistent, so a live population outlives the server process; on the first tick with players present, rebuild the id list from a ZDO-table scan keyed on the mod's own stamp flag.
- **The one absolute constraint:** only PERSISTENT ZDOs are ever ownership-assigned to clients (`ReleaseNearbyZDOS` skips `!Persistent`). A non-persistent server-spawned creature would never simulate and would leak forever. Vanilla monsters are persistent; the first spawn asserts it in the log anyway.

---

### 28. Chunk-Data Decode: replicated world geometry as a physics substitute

- **Purpose:** Answer "is this world position open floor?" from ZDO data alone. The maze generator already writes every cell's wall layout into each chunk's ZDO for the renderer — the same bytes double as a server-side navigation/placement oracle.
- **Key files:** `Maze/AvalorMazeGrid.cs` (reader), `Maze/AvalorMazeGenerator.cs` (writer), `Maze/AvalorMazeChunkRenderer.cs` (the other reader, must stay in lockstep).
- **Spec:** ZDO keys `Avalor_ChunkData` byte[], `Avalor_ChunkWidth`/`Depth` (4; legacy 5), `Avalor_ChunkFloors` (1/2/3). Index `(y*cw+lx)*cd+lz`. Bits: `0x01` wallTop(+Z), `0x02` wallBottom(−Z), `0x04` wallLeft(−X), `0x08` wallRight(+X), `0x10` floor, `0x20` ceiling, `0x40` stair (floor removed, y>0). Cell world center = `chunkPos + ((lx−(cw−1)/2)*cell, y*floorH, (lz−(cd−1)/2)*cell)`; the half-terms cancel against the chunk position so every center lands on `MazeCenter + (int*cell, y*floorH, int*cell)` exactly → lossless integer cell keys.
- **Open-floor predicate:** reject `b == 0` (chunk padding), reject `(b & 0x0F) == 0x0F` (never-carved solid), reject stair bit at y>0 (no floor there). Walkable Y = `centerY + y*floorH + lift`.
- **Cache discipline:** one full-table decode into a dictionary; invalidate on every wipe/regeneration path AND refuse to build while generation is in progress (a half-written maze funnels every spawn into the first-emitted corner).

---

### 29. Routed-RPC Net Layer: roles, claims, queries

- **Purpose:** The coordination layer for "the server decides, the nearest machine executes". Added in v0.1.0.
- **Key files:** `Core/AvalorNet.cs`; consumers across `Portal/`, `Commands/`, `Atmosphere/`.
- **Hard-won rules:**
  - **GUID-prefix every RPC name.** Routed RPC names hash into ONE dictionary shared by the game and every mod; a bare name is a collision, and a collision throws out of `Dictionary.Add`, killing the later mod's handler.
  - **Key registration on the `ZRoutedRpc.instance` REFERENCE, never a bool.** The instance is rebuilt per world session; a stale bool means the second world in one process silently has no handlers.
  - **Isolate each Register in try/catch** — one foreign collision must not take the rest of the protocol down.
  - **Detect topology once, log it, route on it:** `DedicatedServer` (headless authority: full table, no bodies), `PlayerHost` (authority + player; also plain singleplayer), `Client` (partial table; executes anything local). Headless test that works when compiled against client DLLs: `SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null`.
  - **Version-gate the protocol:** adding RPCs is a Minor bump under Jotunn `NetworkCompatibility(EveryoneMustHaveMod, Minor)` so mismatched pairs refuse cleanly instead of half-working.
- **The three RPC shapes:**
  - **Ensure/act** (client → server, fire-and-forget): "make the maze exist", "relocate my grave (ZDOID)". Server validates against its whole-world table; handlers idempotent by construction (e.g. relocation no-ops once the position is overworld).
  - **Query/answer** (client → server → reply-to-sender): "where is the exit / my nearest grave". Client caches answers with timestamps and throttles sends; consumers try their LOCAL table first so hosts and near-range cases never network at all.
  - **Claim/grant** (client → server → grant one): delegate physics work (portal siting) to a machine that actually has the area loaded, with a server-held claim latch + timeout so exactly one machine builds and a crashed grantee doesn't wedge the system. Broadcasts to `Everybody` also run locally on the caller — a player-host evacuates its own player through the same handler as every guest.

---

### 30. Persistent Terrain Edits: `TerrainOp`/`TerrainComp` vs `TerrainModifier`

- **Purpose:** Making a runtime terrain edit that survives the zone unloading. Rewritten in v0.1.6 after the portal clearing was found reverting on every stream-out.
- **Key files:** `Portal/AvalorTerrainOps.cs` (`EmitOp`), `Portal/AvalorTerrainFlattener.cs`, `Portal/AvalorPathLeveler.cs`.
- **THE DISTINCTION, and it is the whole system:** Valheim has two terrain mechanisms and they are not interchangeable.
  - **`TerrainModifier` is a LIVE SCENE OBJECT, not stored data.** `Awake()` adds it to a static `s_instances` list and pokes every heightmap in range; `OnDestroy()` removes it and pokes them again. The ground is shaped **only while the component exists**. A modifier with no `ZNetView` is therefore purely local and purely temporary — destroy the object (or unload its zone) and the heightmap snaps straight back to natural.
  - **`TerrainComp` is the persistent per-zone delta store.** `TerrainOp.Awake()` → `Heightmap.FindHeightmap(pos, radius, list)` → for each: `GetAndCreateTerrainCompiler().ApplyOperation(this)` → `m_nview.InvokeRPC("ApplyOperation")` → **the zone OWNER** → `DoOperation` → `InternalDoOperation` + `Save()` → compressed into `ZDOVars.s_TCData`. Then the op destroys its own GameObject. This is the path a hoe uses.
- **Hard-won rules:**
  - **Never shape persistent world geometry with a bare `TerrainModifier`.** It looks correct in-session and evaporates the moment the player walks away, leaving anything you seated on it floating at the old height. This produced a gateway, its rune VFX, the paved road and every torch hanging metres in the air.
  - **A "wasteful" re-emission may be load-bearing.** Pre-0.1.3 the flattener re-emitted its whole 101-op grid on every stream-in. 0.1.3 removed that as pure waste — and it was the only thing holding the ground down. A one-shot `ClaimPass` flag is only correct once the edit itself is persistent.
  - **`TerrainOp` must be built INACTIVE.** `AddComponent` runs `Awake` synchronously, and `Awake` is where the op applies itself — configuring afterwards applies the *default* 2m smooth and silently discards your settings. `SetActive(false)` → position → `AddComponent` → `m_settings` → `SetActive(true)`.
  - **Routing to the owner solves ownership for free.** `TerrainComp.Save()` returns early unless `m_nview.IsOwner()`, and `InvokeRPC` addresses `m_zdo.GetOwner()` by construction — so a client's edit is applied and saved by whoever owns the zone.
  - **`TerrainOp` handles cross-zone spill natively** (`FindHeightmap` returns every heightmap in radius), which makes System 14's op-grid a *flatness* device rather than a cropping workaround.
  - **Version the pass flag, not just the geometry.** `AvalorTerrainPass` went 1 → 2 → 3 → 4 across 0.1.5/0.1.6/0.1.8; each bump re-shapes every already-saved site once, which is how a terrain fix reaches existing worlds without asking for a new one.
  - **⭐ LEVEL OVERWRITES; SMOOTH CONVERGES. Overlapping ops that DISAGREE must not level.** This is the 0.1.8 fix for "the road made a very janky dirt path", and it is the subtlest thing in this system.

    | | Portal clearing | Processional road |
    |---|---|---|
    | Ops | ~100 | ~15 |
    | Target height | **all identical** | **all different** (it is a grade) |
    | Overlap | heavy | heavy |
    | Order-sensitive? | **no** | **yes** |

    `TerrainComp.LevelTerrain` *sets* height outright, so where two level discs overlap the **last op applied wins**. `SmoothTerrain2` instead does `Lerp(currentHeight, opHeight, 1-(d/r)^power)` — so overlapping smooths settle *between* their heights, and at an op's own centre the weight is 1, meaning the surface still lands exactly on the intended height.

    The killer is that **ops do not arrive in the order you created them**: `ApplyOperation` routes each one by RPC to the zone owner, so on a dedicated server the winner of every overlap is decided by network timing. The clearing was immune the entire time *only because its ops agree with each other* — same machinery, same radii, no jank. A graded strip is precisely the case where hard levelling is the wrong tool.

    **Rule: level only where every overlapping op wants the same height. Anywhere the target varies along the run, smooth.**
  - **A chain of ops is not a cropping workaround.** `AvalorPathLeveler`'s comment claimed one op per leveller was fine because "a single op is cropped at a zone border and the next leveller covers it". That is `TerrainModifier` behaviour; `TerrainOp` spans zones natively (see above). The comment was stale by two releases and would have sent the next reader down the wrong path.

---

### 31. Sky-Region Presence: the airspace is a COLUMN, not a sphere

- **Purpose:** Answering "is anyone in the labyrinth?" — the question every destructive path must get right. Fixed in v0.1.7.
- **Key files:** `Core/AvalorNet.cs` (`AnyPlayersInMaze`, `GetMazePlayers`, `InMazeAirspace`).
- **The bug:** the check was `Vector3.Distance(pos, MazeCenter) < 400f` — a **3D sphere**. The maze floats at `y=500` with nothing beneath it, so a player who has fallen off is at the same x/z and hundreds of metres down, and reads as **absent at exactly the moment an evacuation would save them**. Field evidence: `avalor_reset` issued from `(x 29941, z 29940, y −243)` measured 748m and skipped the evacuation broadcast; the tester survived on `fly`.
- **Hard-won rules:**
  - **Test the region membership + HORIZONTAL distance.** `InSkyMaze` (`x > 25000`) already fences the pocket off from the real world, so height carries no information and only does harm.
  - **Two presence checks that disagree are a bug even when neither throws.** The mob director (`GetMazePlayers`) reported `players-in-maze=1` while the reset believed the maze empty — same player, different geometry. Route every presence question through one predicate.
  - **Every destructive path needs the same guard.** `avalor_remove_all` called `WipeSkyZone` directly with no presence check at all for four versions, while `avalor_reset` had evacuated since 0.1.0. Both now share `WipeSkyZoneAfterEvacuation`, which **abandons** the wipe if players are inside and no routed-RPC channel exists to warn them.

---

### 32. ⭐ Relocating a Physics Object Safely (and the Grave Watchdog)

> **Promoted to the top of [MASTER_IMPLEMENTATIONS.md](MASTER_IMPLEMENTATIONS.md) — the engine facts are vanilla and every mod in the workspace that moves, spawns or ground-seats an object is exposed to them.**

- **Purpose:** Moving a maze-death grave to the overworld graveyard **without losing it**. Fixed in v0.1.8, after a live dedicated session destroyed a full player inventory while logging `grave relocated (server)`.
- **Key files:** `Core/AvalorGraveGuard.cs` (new — sweep, re-seat, headless ground height, driver), `Core/MistsofAvalorPlugin.cs` (`Avalor_TombstoneToGraveyard_Patch`: `DoRelocateGrave`, `HoldGraveAt`, `PlotHeight`), `Core/AvalorNet.cs` (`RecoverGravesRPC`), `Commands/AvalorCommands.cs` (`avalor_tp_grave`, `avalor_recover_graves`).

**The bug, in three engine facts that are individually harmless:**

1. `Player_tombstone` carries a **`Rigidbody`** — confirmed from `Prefabs_Dump.json`, not assumed. A grave standing on mod-placed maze floor falls the instant that geometry streams out.
2. It also carries **`ZSyncTransform`**, so the owner writes its transform into the ZDO every frame. The grave does not just fall, it **saves itself falling** — and a ZDO position we write is overwritten by whatever machine still has the GameObject.
3. **Both vanilla guards are blind to it on a dedicated server.** `TombStone.PositionCheck` measures drift with `Utils.DistanceXZ`, so a vertical drop changes nothing; its buried-check calls `ZoneSystem.GetGroundHeight`, which **returns its input `y` on a raycast miss**, and a headless server has colliders only for loaded zones. The test degenerates to `if (y < y - 1f)`.

**Field evidence (2026-08-01, `AvalorDedi`):** server logged `grave relocated (server): maze (29959, 501, 29978) -> graveyard (71, 61, 32)`. The target `y=61` is exactly `portal.y (60) + GraveLift (0.95)` — the *fallback* height, because the graveyard zone was not loaded when the move ran. Nothing was at the graveyard. Meanwhile the client's own log twice reported `1 grave(s) in the world` at `(29959, 500, 29978)` — the pre-move position — and teleported the player there, once while standing at the graveyard. Two machines disagreed; no `[Avalor][FAIL]` fired on either.

**Hard-won rules:**

- **Setting a ZDO's position does not move an object.** Move the ZDO, restamp `ZDOVars.s_spawnPoint`, **and** move the live instance found via `ZNetScene.FindInstance(zdo.m_uid)` — transform, `rb.position`, and `rb.linearVelocity = Vector3.zero`. Re-seating a falling object without zeroing velocity only changes where it falls from.
- **Then hold it.** The previous owner keeps writing until the ownership change reaches it, so a one-shot move loses an invisible race. `HoldGraveAt` re-asserts for ~6s at a 3m tolerance (inside `PositionCheck`'s 4m, so the two never fight).
- **Never derive a height from a nearby object.** `WorldGenerator.GetHeight(x, z)` is noise math, needs no colliders, answers on any machine, and returns world-space Y — the engine assigns it directly. Raycast first (it accounts for our own terrain edits), generator second, object-relative **never**.
- **Anything holding player items gets a watchdog, not a fix.** `AvalorGraveGuard` reads tombstones straight from the ZDO table — no instances, no colliders, no loaded zones — classifies each as sky-region / below-void / buried, and re-seats the lost ones on world load and every 60s. A one-shot fix cannot help a grave that becomes lost *later*.
- **Widen the guard past the known cause.** The sweep recovers any unreachable grave whatever put it there, because a grave is the one object whose loss is unrecoverable.
- **A local ZDO hit can be stale — prefer the server.** `avalor_tp_grave` only asked the server when its local search found *nothing*; a client's table accumulates everything it has ever received, so it found a stale entry, succeeded, and never consulted the authority. It now always asks when a server exists and keeps the local hit as a timeout fallback only.
- **`OnlyServer` is permission, not location.** `avalor_recover_graves` routes through `RecoverGravesRPC`, because a Jotunn console command executes on the machine it was typed into and an admin's client cannot authoritatively own or move a ZDO.
- **The lifecycle cannot be resolved in `Awake`.** `ZNet.instance` is null when managers are created, and a dedicated server loads its world afterwards, so `AvalorGraveGuardDriver` waits for a live world on `Update` and resets when it goes away — same shape as the stealth probe, same reason.

---

## Dedicated-Server Field Findings (first live server deployment, 2026-08-01)

The dedicated pass shipped in 0.1.0 and was never run against a real headless server until now. Boot, topology detection, RPC registration, config sync and a 74-mod client handshake all passed first time. What failed was everything downstream of **"the site build is delegated to a client"**.

- **A server-gated component instantiated by a client never runs.** `AvalorTerrainFlattener`, `AvalorPathLeveler` and `AvalorTerrainOps.Apply` all opened with `if (!ZNet.instance.IsServer()) return;`, while the portal siting that creates them is deliberately delegated to a CLIENT (it needs stone-gap probes, ground raycasts and a heightmap that a headless box does not have). Neither machine ran them, and the site — which is deliberately seated *below* natural ground so the pass can cut the hill down to meet it — got the sink without the cut. **Half of a two-step move is worse than neither half.**
- **Terrain shaping is EXECUTION, not authority.** It belongs on whichever machine has the heightmap, under the architecture's own rule. Gate on *readiness* (`ZoneSystem.IsZoneLoaded` across the footprint) rather than on role — a client arriving at a fresh site is often faster than the world around it, and a fixed sleep tuned on a player-host is a coin toss there.
- **Silent early returns in an RPC handler are indistinguishable from a lost RPC.** `ServerRelocateGrave` had three. A maze death logged "asked the server to relocate it" on the client and produced *nothing* on the server, and the grave stayed in the labyrinth. Cause: the tombstone ZDO is created and **owned by the dying client**, and the RPC fires the instant it goes valid locally — before it has replicated to the server, so `GetZDO` returned null and the request was dropped. Player-hosts deliver the routed RPC to themselves with the ZDO already in the table, which is why this was never reproducible in a hosted session. Fixed by polling for the ZDO and making every branch speak.
- **Claim-then-destroy, always.** `DestroyZDO` on a ZDO you do not own is a silent no-op. `ClearObstacles`/`ClearAreaVegetation` skipped the claim, so avenue boulders survived on a dedicated server (the world belongs to the server; the sweep runs on a player) while working perfectly on a host.

### Two corrections — recorded so they are not re-investigated

- **A client's ZDO table is NOT limited to nearby zones.** `avalor_remove_all` run from the overworld, 30 km from the maze, logged `WipeSkyZone cleared 876 ZDO(s)` — the entire labyrinth. A client retains every ZDO it has received. An earlier diagnosis of "client-side wipes are ineffective on a dedicated server" was **wrong**, and a whole RPC-routing redesign was proposed on that false premise before the log line refuted it. *Check the count the code already prints before theorising about visibility.*
- ~~**`avalor_generate_all` rebuilding only the overworld gate is correct**, not a bug. The labyrinth is forged on entry via `EnsureMazeRPC`.~~ **RETRACTED 2026-08-02 — this entry was wrong, and being written down as "do not re-investigate" is what let it stand.** The reasoning (the maze is forged on entry, so regenerating it here is redundant) is true of the *normal* path and dangerous on *this* one: after `avalor_remove_all` the sky region is EMPTY, and anything that puts a player there without a portal — flying, `avalor_tp_end`, a stale spawn point — is a 500 m fall onto nothing. A playtester died exactly that way and their tombstone fell after them. Fixed in 0.1.8: `generate_all` now also invokes `EnsureMazeRPC` (idempotent, so free when a maze exists). **The lesson is about the note, not the command — "verified correct by design" is not the same as "verified safe in the field", and recording the former as settled suppressed the latter.**

### 0.1.8 verification pass — what was proven live, and what was not

The 0.1.5/0.1.6/0.1.7 fixes shipped **unplayed**; 0.1.8 was the session that actually exercised them. Recording the distinction between *verified* and *reasoned* matters more than the pass list, because three releases of confident changelog prose turned out to rest on nothing.

**Verified against a live dedicated server:**

| Behaviour | Evidence |
|---|---|
| Grave recovery sweep | `LOST GRAVE 1:63898 at (29959, 500, 29978)` → recovered → sweep reports `all reachable` |
| Drift catch (`HoldGraveAt`) | fired on **3/3** maze deaths, including one grave in free fall |
| `avalor_recover_graves` | RPC-routed from a client, swept server-side, correct `0 recovered` no-op |
| `avalor_tp_grave` server-first | `Checking with the server…` → `(located by the server.)` |
| `generate_all` rebuilds the maze | wipe → 9s later `Generation complete… chunks=25 tracked-ZDOs=800` |
| Terrain persistence (0.1.6) | clearing survived a Mistlands round trip; **one** op emission all session |
| Dedicated site build (0.1.5) | `portal siting GRANTED to this client` → `terrain: 100 op(s) spanning 6 zone(s)` |
| Road grading (0.1.8) | confirmed visually after the smooth-only change |

**The overwrite race is not an edge case.** Every maze death on a dedicated server produced `grave … drifted back … the owning machine overwrote the move`. The pre-0.1.8 code therefore lost the grave **every time**, and looked fine only on a player-host where the routed RPC is delivered to itself synchronously. *A bug that needs a second machine to appear will never be found in single-player testing.*

**Known weakness, deliberately shipped:** `HoldGraveAt` re-asserts twice a second against an owner writing every frame. It won because the fall stopped, not because it out-runs anything. The architecturally correct fix is **the owner performs the move** (server decides *where*, owning machine does the *write*), which wins by construction. The watchdog sweep is the safety net that makes the current design acceptable rather than correct.

**Constants calibrated against a broken input silently become wrong when the input is fixed.** `GraveLift = 0.95f` was tuned to compensate for the fabricated `portal.y` fallback height; once ground height became correct the same constant floated every grave ~0.9 m. The same shape as the `TerrainModifier` regression — in both cases fixing one thing exposed a compensation built on top of it. **When you fix a measurement, audit every constant derived from it.**

**Measuring is not automatically better than a tuned constant.** Replacing that lift with a mesh measurement returned `0.04 m` and buried the stone — `GetComponentsInChildren<MeshFilter>` picks up nameplate/effect quads sitting at the pivot, so the "lowest point" was never the headstone. Two field observations bracketed the true value at `0.45` far more reliably. *Measure the right thing or don't claim to be measuring.*

### Testbed operating rules (`libs-Tools\DEDICATED-SERVER-TESTBED`)

- **Direct-IP join does not work.** With `-crossplay 0 -public 0` the server binds only the Steam query port (2457) plus ephemeral relay ports — **never the game port 2456** — and answers no A2S query. Use `-crossplay 1` and the **join code** printed as `Session "<world>" registered with join code NNNNNN`. The README's `Join IP -> 127.0.0.1:2456` instruction is wrong for this build.
- **Never force-kill the server without `-saveinterval`.** Valheim saves on clean shutdown and otherwise every 30 minutes; `Stop-Process -Force` skips the save and destroyed a full playtest world (maze, portal site, grave — only the `.fwl` seed survived). Always launch with `-saveinterval 120`.
- **`wire-bepinex.ps1` resolves its donor Gale profile at runtime** and skips anything named `wonderland`. It previously hardcoded a profile name that no longer existed.
- **Do not `Copy-Item -Recurse` into an existing directory** — PowerShell nests it (`core/core`), and duplicate BepInEx core assemblies kill the preloader *before it can create a log file*, which presents as a live 1 GB process producing no output at all.
- **Two full modded Valheim instances do not fit in 32 GB.** A 74-mod client (8.4 GB) plus a 74-mod server (5.6 GB) drove `Memory Compression` to 3.8 GB and fresh-world generation to a crawl. A lean server (Jotunn + MoA) runs at ~1.3 GB and boots in ~90s. Mirror the client's plugin set only to test the connect handshake, then go lean.

---

## 0.1.11 — the reset ran on the wrong machine, and the Warden paid for it (2026-08-11 dedicated log)

**Symptom reported:** "no warden after a fresh reset." The maze itself came back complete — exit portal, prize chest, mobs, grid — so nothing looked broken.

**What the log actually said.** In 9,761 lines: the server logged the raw command text `76561198224105156/Thorium Wubarrk (…): avalor_reset` at 12:56:05 and then **nothing**. Zero `AvalorResetCommand` lines. Zero `AvalorResetManager` lines. Exactly **one** `AvalorMazeGenerator` line in the whole session, and it predates the reset. Zero occurrences of the string `warden`, anywhere, all session.

**Root cause: `OnlyServer = true` gates permission, not location.** A Jotunn `ConsoleCommand` executes on the machine it was typed into. `avalor_reset` therefore wiped and rebuilt the labyrinth inside the admin's *client* while the dedicated server ran none of it. The client's new exit and chest replicated back, which is exactly why the maze looked fine.

The codebase already knew this rule and had already applied it once — `RPC_RecoverGraves` in `Core/AvalorNet.cs` carries a comment spelling it out verbatim. **The reset commands were simply never given the same treatment.** *A lesson learned in one command does not propagate to its neighbours on its own; when you discover a framework trap, grep for every other site that could hit it.*

**Two consequences, and the second is worse than the reported one:**

1. **The Warden never returned.** `AvalorMobDirector` is server-side, and the only thing that re-arms a beaten boss is `OnMazeWiped()` — called from inside `WipeSkyZone`, which ran on the client. The server's `_endBossKilled` stayed latched from the previous maze, and `ManageEndBoss` returned at its early guard on every 5s tick for the rest of the server's uptime. Note the shape: the guard sat **in front of** `ScanWardenState`, the full ZDO-table scan that exists precisely to correct unreliable in-memory state. *A stale bool that short-circuits the check written to catch stale state is worse than having no check.*
2. **The evacuation decision was made from a client's partial ZDO table.** `AnyPlayersInMaze` answered from whatever that one client had streamed in. A wrong "nobody is inside" drops every player in the labyrinth 500 m. This one was never reported because it did not happen to fire — not because it was safe.

**Fixes shipped:**

- Both reset commands route through `ResetMazeRPC` → server-side handler that re-checks `IsServer()`. Same pattern as `RecoverGravesRPC`.
- **The Warden now re-arms from world state rather than from being told.** Boss state is keyed to the exit portal's ZDOID (`_stateMazeExitId`); a different exit standing means a different maze and the state is discarded. The exit portal is created with the labyrinth and destroyed with it, so its id is a free, exact name for "this maze" — and this covers every wipe path that will ever exist, including ones from other mods and ones this machine never hears about. **This is the fix that makes the symptom un-recurrable; the RPC routing is the fix for the cause.** Both were shipped, deliberately.
- `TryFindExit` now validates its cache by re-resolving the remembered ZDOID (an O(1) lookup) instead of trusting a remembered position for 30 s. A wipe does not move the exit, it *destroys* it — so liveness of the id is the correct validity test, and it doubles as the maze-changed signal above at no cost.
- `ScanWardenState` takes the caller's exit id instead of independently first-matching the ZDO table. **Two independent "first match in a dictionary" searches for the same logical object can disagree** — order is not guaranteed stable — and a half-wiped sky region (exactly what a misrouted wipe produces) is where a second exit portal can linger. Had they disagreed, `_stateMazeExitId` would flap and spawn a boss on top of a boss.
- The old `TryFindExit` miss path set a timestamp the fast path never read, so a server with no labyrinth standing walked its **entire ZDO table — 2,190,254 entries on this host — every tick**. Now genuinely rate-limited.
- Both reset commands wrapped their whole body in `if (AvalorMazeGenerator.Instance != null)` with no `else`: a reset on a machine without a generator was a completely silent no-op that looked like success. Every path reports now.

**Also confirmed healthy in the same log (no action needed):** the grave pipeline. Three tombstones present all session were ordinary overworld deaths at genuine terrain heights, correctly left alone by 1,057 sweeps. The one maze death after the reset relocated to the graveyard, drifted back once (`the owning machine overwrote the move`), was re-asserted, and its ZDO then disappeared from the count — i.e. looted. The 0.1.8 drift watchdog is still doing its job.

**New in this release:** `avalor_gather_graves` — moves *every* grave to the graveyard, lost or not. Kept as a separate command rather than a flag on `avalor_recover_graves`, so wanting the blunt behaviour never means loosening the reachability test that protects the automatic sweep.

**Version-matching note:** `ResetMazeRPC` is new, so an 0.1.10 server will not answer an 0.1.11 client's reset request. Both ends need updating for that command; everything else still interoperates.

---

## Notes on `_Attic/`

`_Attic/` contains three superseded files explicitly excluded from compilation: an early stub of the chunk renderer (superseded by System 2), an early standalone "overworld enforcer" for the processional road (absorbed into `AvalorPortalSpawner`, System 13), and an empty placeholder for the recall rune item (actually implemented by cloning a vanilla "Ruby" item in `AvalorAssetManager.CreateRecallRune()`). No unique, non-superseded systems found there.
