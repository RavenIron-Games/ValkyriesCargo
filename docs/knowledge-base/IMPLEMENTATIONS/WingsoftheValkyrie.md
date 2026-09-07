# WingsoftheValkyrie — Technical System Report

**Overview:** Wings of the Valkyrie is a BepInEx/Jotunn mod for Valheim that adds four craftable tiers of wings (Crude, Troll, Lox, Dragon) which replace the vanilla cape slot. Equipping wings and jumping mid-air deploys a physics-driven glide/flap flight system with per-tier speed, lift, stamina cost and altitude-ceiling stats, all server-configurable. The flight state and procedurally generated glowing rune-wing VFX (built entirely from runtime-generated meshes, LineRenderers and ParticleSystems — no custom models/shaders shipped) are synchronized across multiplayer clients purely through custom fields on the player's existing `ZDO`.

**Current as of 2.0.1.** Three releases have reshaped it since the original report and the systems below reflect all of them:

- **2.0.0** added a real Valheim skill (Valkyrie Flight), a self-migrating config, embedded icon art, and much heavier recipes.
- **2.0.1** made the skill the *gate* rather than a bonus — a tier's printed stats became what mastery buys — and added a per-character flight logbook plus a server-aggregated BarrkBOT export.
- The "no custom RPCs" claim in the original overview **no longer holds**: 2.0.1 introduces exactly one (`WOTV_FlightSaga`), for the reason set out in System 12. The *VFX* sync remains RPC-free and ZDO-only, which is the part worth copying.

Source tree:
```
CapeVisualPatch.cs
ConfigMigration.cs          <- 2.0.0
FlightController.cs
FlightLog.cs                <- 2.0.1
FlightLogCommand.cs         <- 2.0.1
FlightLogPatches.cs         <- 2.0.1
FlightReport.cs             <- 2.0.1
FlightSaga.cs               <- 2.0.1
FlyingSkill.cs              <- 2.0.0
IconLoader.cs               <- 2.0.0
ModConfig.cs
ReflectionUtil.cs
ValkyrieInput.cs
WingsItem.cs
WingsoftheValkyriePlugin.cs
VFX/RuneWingVFX.cs
VFX/ValkyrieRuneTextureGenerator.cs
VFX/ValkyrieRuneVFX_Helper.cs
Resources/Icons/*.png       <- embedded in the DLL
tests/                      <- 2.0.1, off-game harnesses (System 13)
discord/*.txt
```

---

### 1. Flight / Glide Movement System

- **Purpose:** Gives the player a physics-based auto-glide + flap-boost flight mode, gated by equipped "wings" item, stamina, and a per-tier altitude ceiling. Also (via a side effect of a vanilla field write) prevents fall damage while gliding.
- **Key files:** `FlightController.cs`, `ModConfig.cs`, `WingsItem.cs`
- **Architecture:**
  - `[HarmonyPatch(typeof(Player))]` static class patches `Player.Update` (Postfix) and `Player.FixedUpdate` (Postfix) — no new MonoBehaviour driving physics; it rides the vanilla player update loop.
  - `UpdatePostfix` runs for **every** `Player` instance that exists on a client (local player + all remote player "ghosts" Valheim instantiates for visible peers). It lazily attaches a `RuneWingVFX` component to each `Player.gameObject` the first time it sees them (`GetComponent` → `AddComponent` fallback).
  - Branches on `__instance == Player.m_localPlayer`:
    - **Local:** `UpdateLocalGlide()` reads `ZInput.GetButtonDown("Jump")` while airborne to trigger a flap (deduct stamina via `Player.HaveStamina`/`UseStamina`, tier-specific cost from `ModConfig`), and auto-enters glide either on flap or when falling faster than `-5f` on Y velocity. Grounded/swimming/in-water immediately cancels glide.
    - **Remote:** `ReadRemoteState()` — no physics is applied to remote players at all (Valheim's normal `ZDO` transform sync already moves them); this method only pulls the synced gliding bool/flap counter to drive the *visual* state.
  - `FixedUpdatePostfix` (local player only) is where actual flight forces are applied directly to `Rigidbody.velocity`:
    - Flap → instantaneous upward velocity set to `flapForce`.
    - No flap → automatic descent rate, made steeper the more the camera looks down (`Player.GetLookDir().y`), via `Mathf.Lerp(baseSink, -MaxDiveSpeed, -lookDir.y)`. **As of 2.0.1 both ends are config entries** (`BaseGlideSinkRate`, `MaxDiveSpeed`) rather than the literals `-2f` and `-20f`; `baseSink` is additionally flattened by skill.
    - Horizontal movement: reads `m_moveDir` off the player via `Traverse.Create(player).Field("m_moveDir").GetValue<Vector3>()` and `Vector3.Lerp`s the rigidbody's horizontal velocity toward `moveDir * glideSpeed`.
    - Ceiling clamp: `ZoneSystem.instance.GetGroundHeight(pos)` vs `transform.position.y`; if above `ceilingLimit`, upward velocity is zeroed (soft ceiling, not a hard wall).
  - **Skill gating (2.0.1) — the design idea worth stealing.** Rather than adding skill bonuses on top of a full-strength baseline, the tier stats were rebased so that *the printed number is what mastery buys*, and low skill is the penalty:
    - `ceilingLimit = stats.FlightCeiling * Mathf.Lerp(CeilingAtNovice, 1f, skillFactor)` — altitude is the headline thing the skill buys, and the same wings hold a novice at ~35% of the height they carry a master to.
    - Per-tier `MinSkillToFlap` (0/15/30/50) blocks *flapping* below a skill level, but never gliding. That distinction is what keeps it from being a soft-lock: gliding is what earns the skill, so a player who crafts straight into the top tier is slowed down rather than stranded with an item they cannot use and no way to qualify for it.
    - Both gates are wrapped in `FlyingSkill.IsAvailable` and **fail open**. If skill registration ever fails, nothing can raise the skill, so a level requirement would permanently brick flight through an unrelated bug. A gate whose precondition is itself fallible must stand aside when that precondition is missing.
  - **Rebasing an existing balance is a config-migration problem, not just a number change** — see System 9. Every default that moved had to be listed as a rebase or players' `.cfg` files would keep silently overriding the new balance with the old one.
  - **Fall-damage prevention (non-obvious):** while gliding, every frame it does `Traverse.Create(player).Field("m_maxAirAltitude").SetValue(player.transform.position.y)`. `m_maxAirAltitude` is the vanilla field Valheim's own fall-damage code compares against landing height; continuously resetting it to the current position means the game never "remembers" having been high up, so landing after a glide never triggers fall damage. This is implemented as a side effect, not a Harmony patch on damage code.
  - Per-tier stats (ceiling, glide speed, flap force, stamina cost, crafting station/level/requirements) all come from `ModConfig` (System 6) and are looked up by wings name string (`WingsItem.CrudeName` etc.) via if/else chains — no dictionary/enum abstraction.
- **How to implement:**
  1. Add a Harmony patch class targeting `typeof(Player)`, patching `"Update"` and `"FixedUpdate"` as **Postfix**.
  2. In the `Update` postfix, get-or-add a custom flight-state component to `__instance.gameObject`; branch behavior on `__instance == Player.m_localPlayer`.
  3. For the local player: listen for your fly-trigger input in `Update` (not `FixedUpdate` — input polling must happen once per frame), gate on `player.IsOnGround()`/`IsSwimming()`/`InWater()`, and use `player.HaveStamina(cost)` / `player.UseStamina(cost)` for resource gating.
  4. For actual force application, do it in `FixedUpdate` (postfix) and only for the local player — get the `Rigidbody` component and mutate `rb.velocity` directly; do **not** try to also move remote players' rigidbodies, Valheim's `ZSyncTransform`/`ZDO` position replication already does that.
  5. To read player-authored fields not exposed publicly (`m_moveDir`, `m_maxAirAltitude`), use `HarmonyLib.Traverse.Create(instance).Field("name").GetValue<T>()` / `SetValue()`, or better, cache `AccessTools.FieldRefAccess<T,F>` for a hot path (see System 7, ReflectionUtil).
  6. To cancel fall damage during a custom flight state, continuously overwrite the vanilla "max air altitude" tracking field to the player's current Y position for every frame flight is active.
  7. Use `ZoneSystem.instance.GetGroundHeight(worldPos)` for terrain-relative altitude ceilings rather than absolute world Y.
- **Reusable pattern/snippet:**
```csharp
[HarmonyPatch(typeof(Player), "FixedUpdate")]
[HarmonyPostfix]
public static void FixedUpdatePostfix(Player __instance)
{
    if (__instance != Player.m_localPlayer) return; // only simulate physics for the local player
    if (!Gliding(__instance)) return;
    var rb = __instance.GetComponent<Rigidbody>();
    Traverse.Create(__instance).Field("m_maxAirAltitude").SetValue(__instance.transform.position.y); // suppress fall damage
    rb.velocity = new Vector3(rb.velocity.x, targetY, rb.velocity.z);
}
```

---

### 2. Multiplayer VFX State Sync (ZDO custom fields — the standout system)

- **Purpose:** Lets every client see every *other* player's wing VFX (gliding pose, flapping bursts, tier color) in perfect sync, without a custom RPC channel, by piggy-backing on Valheim's existing per-object `ZDO` key/value replication.
- **Key files:** `FlightController.cs` (publish/read logic), `VFX/RuneWingVFX.cs` (state flags consumed by the visuals)
- **Architecture:**
  - No `ZRoutedRpc.Register`/`Invoke` is used anywhere in the project. Instead the mod defines two custom ZDO keys as stable string hashes, computed once as `static readonly int`:
    ```csharp
    private static readonly int ZdoGliding = "wotv_gliding".GetStableHashCode();
    private static readonly int ZdoFlapCount = "wotv_flapcount".GetStableHashCode();
    ```
    `GetStableHashCode()` is Valheim's built-in deterministic string hash (same algorithm as vanilla ZDO key hashing) — using a namespaced prefix (`wotv_`) avoids collisions with vanilla or other mods' keys.
  - **Publish (owner only):** `PublishState()` grabs the `ZNetView` cached on `RuneWingVFX` (`vfx.NView`), checks `nview.IsValid() && nview.IsOwner()`, then does `zdo.Set(ZdoGliding, boolValue)` and `zdo.Set(ZdoFlapCount, intValue)` — only writing when the value actually changed (dirty check) to minimize network churn. `ZDO.Set` on an owned object is automatically delta-replicated to all clients that have that ZDO in range by Valheim's `ZDOMan`; no explicit send call is needed.
  - **Read (all clients, including the owner's own remote view of itself is skipped since local branch is used):** `ReadRemoteState()` calls `nview.GetZDO()` — on a non-owning client this returns the locally-cached *shadow* copy of the ZDO that `ZDOMan` keeps synced automatically — then `zdo.GetBool(ZdoGliding, out bool gliding)` / `zdo.GetInt(ZdoFlapCount, 0)`.
  - **Edge-triggered replay via monotonic counter:** Continuous state (`gliding`) is trivial to sync as a bool, but a *discrete event* (a single flap) needs edge detection. The owner increments `FlapCount` (a plain `int` field on `RuneWingVFX`) once per flap and publishes it. Each remote observer keeps its own `LastSeenFlapCount` (initialized to `int.MinValue` as a sentinel). On first sighting of a player, it **adopts** the current count without replaying (so it doesn't spam-replay a flight history it never saw), then on every subsequent difference it triggers exactly one local flap animation (`vfx.TriggerFlap()`).
  - **Version-mismatch fallback:** Because `[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]` allows peers on different minor versions to connect, an older peer's ZDO might not have these custom keys at all. `zdo.GetBool(key, out val)` returns `false` if the key is absent, so the code explicitly checks the out-bool return and falls back to inferring "gliding" purely from the remote player's vertical velocity (`!IsOnGround && !Swimming && !InWater && Mathf.Abs(vel.y) > 2f`) — graceful degradation rather than hard failure.
  - Net effect: bandwidth cost is a handful of bytes per state *change* (not per frame), and all the expensive per-frame wing-bone animation (Bezier curves, mesh rebuild, particle emission) still runs **locally on each client**, driven only by the tiny synced bool/int — the actual wing geometry/position is never sent over the network.
- **How to implement:**
  1. Ensure the GameObject you want to sync already has (or add) a `ZNetView` — for `Player` it's already present on the prefab, so just cache `GetComponent<ZNetView>()` in `Awake()`.
  2. Pick unique string keys and hash them once: `int key = "yourmod_something".GetStableHashCode();` — store as `static readonly int`.
  3. On the **owner** only (`nview.IsOwner()`), when local state changes, call `nview.GetZDO().Set(key, value)`. Guard with a dirty-check (`if (zdo.GetBool(key,false) != newValue)`) to avoid redundant writes.
  4. On **every** client (including non-owners), read with `nview.GetZDO().GetBool(key, out val)` / `GetInt`/`GetFloat`/`GetString` as appropriate. Always check `nview != null && nview.IsValid()` first (remote objects may not have their `ZNetView` fully bound yet).
  5. For discrete one-shot events (as opposed to continuous state), publish a monotonically-incrementing counter rather than a "trigger" bool (bools risk being missed between two polls if toggled twice quickly). Have each observer track its own "last seen" value per remote instance and diff.
  6. Initialize "last seen" trackers to a sentinel (e.g. `int.MinValue`) so you can special-case "first time observing this remote entity" and adopt its current count silently instead of replaying its entire history.
  7. Always write a fallback heuristic path for when the synced key is simply absent (covers older-mod-version peers, or object not yet replicated) so the feature degrades rather than throwing/breaking.
  8. This whole approach avoids `ZRoutedRpc` entirely — appropriate for state that (a) belongs to an object that already has a `ZNetView`/`ZDO`, and (b) is fine being "last write wins" persistent state rather than a fire-and-forget message. Use actual RPCs (`ZRoutedRpc.Instance.Invoke`/`Register`) only when you need one-off broadcast events unrelated to any single object's persistent state.
- **Reusable pattern/snippet:**
```csharp
static readonly int MyKey = "modid_flag".GetStableHashCode();

// owner: publish
var zdo = nview.GetZDO();
if (nview.IsOwner() && zdo.GetBool(MyKey, false) != localValue)
    zdo.Set(MyKey, localValue);

// everyone: read (with graceful fallback if key absent)
if (!zdo.GetBool(MyKey, out bool val)) val = FallbackHeuristic();
```

---

### 3. Procedural Wing VFX (mesh + particle rig, no art assets)

- **Purpose:** Renders the actual glowing rune-wing visuals — an animated "skeleton" of bone lines with a stretched membrane mesh and a particle trail of rotating rune sprites — entirely from code, with zero imported models, textures, or shaders.
- **Key files:** `VFX/RuneWingVFX.cs`, `VFX/ValkyrieRuneTextureGenerator.cs`, `VFX/ValkyrieRuneVFX_Helper.cs`
- **Architecture:**
  - `RuneWingVFX : MonoBehaviour` is added to the `Player` GameObject (see System 1). `Awake()` caches `GetComponent<Player>()` and `GetComponent<ZNetView>()`.
  - `EnsureEmitters()` (lazy, called on first use) builds, per wing side:
    - A root `Transform` parented to the player spine at a local offset (e.g. `(-0.3, 1.2, -0.3)` for the left wing), used purely as an animatable pivot (rotated each frame for flap/idle sway).
    - **7 `LineRenderer`s** ("bone lines": arm, thumb, 4 fingers, body strut), each on its own child `GameObject`, `useWorldSpace = false`, initially `enabled = false`.
    - A **dynamic `Mesh`** (23 vertices, `mesh.MarkDynamic()`) rendered via `MeshFilter`+`MeshRenderer` for the membrane between the bones. Vertices/UVs/triangles are pre-built once (a fixed fan/strip topology); triangles are **doubled and flipped** (`doubleTris`) so the membrane is visible from both sides (no separate two-sided-shader trick needed). A gotcha called out explicitly in the code: *Unity silently drops UV/triangle assignments if you set them before the mesh has any vertices* — so `mesh.vertices` must be assigned first, even with placeholder data, before `mesh.uv`/`mesh.triangles`.
    - A **`ParticleSystem`** per wing (`CreateParticleSystem`) configured for manual-only emission: `emission.enabled = false` (built-in automatic emission disabled), `main.startSpeed = 0` (particles stay put — used as a positional trail, not projectiles), `simulationSpace = World`, a `ParticleSystemShapeType.SingleSidedEdge` shape, and `textureSheetAnimation` configured as a 4x4 sprite sheet (`numTilesX/Y = 4`) with a random `startFrame` 0–15 so each particle shows one of 16 pre-baked rune glyphs from the atlas.
  - **Per-frame animation (`Update`)**: no Animator/rig — pure procedural math:
    - `GetBezierCurve(p0,p1,p2,segments)` — quadratic Bezier sampler used to generate the arm and 4 finger "bone" curves from a handful of control points that are themselves offset by `foldZ`/`foldY` values driven by a flap-progress curve (three-phase `Mathf.Lerp` easing keyed to `flapProgress` 0→0.2→0.5→1) or, when idle, by `Mathf.Sin`/`Cos` of elapsed time for a breathing sway.
    - `SetupBoneLine()` pushes the sampled points into each `LineRenderer.SetPositions` and sets width taper + tier color (with fade-to-alpha along the line for a "energy dissipating toward the tip" look).
    - `UpdateWingMeshes()`/`UpdateSingleWing()` recomputes all 23 membrane vertices from the same bone-curve math each frame, then `mesh.RecalculateNormals()` and — called out as **critical** in a code comment — `mesh.RecalculateBounds()`, otherwise Unity's frustum culling can incorrectly cull the dynamically-deformed mesh since its cached bounds go stale.
    - Manual particle emission: rather than relying on the particle system's automatic emission-over-time, the code tracks `_lastEmitPosL/R` and calls `ps.Emit(1)` only when the wingtip root has moved more than `0.1` units since the last emission — a "distance-based trail" emitter, avoiding gaps at low framerate or a cloud of particles when standing still. A larger `ps.Emit(15)` burst fires on `TriggerFlap()`.
    - Vertex colors: the particle-membrane shader multiplies texture by vertex color, so the mesh is given `Color.white` for all vertices once and re-applied every rebuild (`mesh.colors = _whiteColors`) — otherwise it can render black/invisible with certain shaders. Called out explicitly as a gotcha in comments.
  - **Runtime shader/material discovery (no shipped shader asset):** `ValkyrieRuneVFX_Helper.GetOrCreateRuneMaterial()` tries `Shader.Find` against a priority list (`"Particles/Standard Unlit"` → legacy alpha-blended → additive), and if none exist, **scores every loaded `Material`** (`Resources.FindObjectsOfTypeAll<Material>()`) by substring-matching its shader name for `"particle"/"add"/"unlit"/"transparent"` and requiring a `_Color`/`_TintColor` property, picking the highest-scoring shader found anywhere in the currently loaded game. Once a shader is chosen, it builds a `new Material(shader)`, sets texture via whichever of `_MainTex`/`_BaseMap`/`.mainTexture` exists, sets color/emission via whichever of `_Color`/`_TintColor`/`_EmissionColor` exists, and forces **additive blending** manually: `SetInt("_SrcBlend", One); SetInt("_DstBlend", One); SetInt("_ZWrite", 0)` plus keyword toggling — independent of which shader was actually found. Materials are cached in a `Dictionary<long,Material>` keyed by a packed `(shaderInstanceId, texInstanceId, colorHash)` composite.
  - **Procedural textures:** `ValkyrieRuneTextureGenerator` builds `Texture2D`s pixel-by-pixel at runtime with a hand-rolled Bresenham line (`DrawLine`) and filled-circle stamp (`DrawCircle`) for "brush thickness", drawing 16 distinct rune glyph patterns (each a `switch` case of hardcoded line segments, styled after real Elder Futhark runes — Algiz, Fehu, Isa, etc.) into either a single 128×128 texture or a 512×512 4×4 sprite atlas (`GenerateRuneAtlas`, feeding the particle system's texture-sheet animation). A separate `GenerateSoftTrail` produces a radially-symmetric soft glow (smoothstep + power falloff) for a non-rune trail texture. Textures are built with mipmaps (`TextureFormat.RGBA32, true`) and `tex.Apply(true)`.
- **How to implement:**
  1. Do not ship texture/shader assets if you want a fully self-contained code-only mod: generate `Texture2D`s at runtime via nested loops calling `tex.SetPixel`, finishing with `tex.Apply(true)`.
  2. For "line art" (runes, sigils, procedural glyphs), implement a basic Bresenham line algorithm plus a filled-circle stamp function for stroke thickness — this is ~30 lines of code and needs no external library.
  3. To find a usable glow/particle shader without owning one, try `Shader.Find` against known built-in/Valheim shader names first, and as a fallback scan `Resources.FindObjectsOfTypeAll<Material>()` for shaders whose name matches heuristics (`particle`, `additive`, `unlit`) that also expose a `_Color` property — this makes the VFX resilient to running in any Unity game version that ships *some* particle shader, even if you don't know its exact name ahead of time.
  4. Force additive/glow blending on the resulting material by directly setting the shader's blend-state ints (`_SrcBlend`/`_DstBlend` = `One`/`One`, `_ZWrite` = 0) rather than depending on the shader having a premade additive variant.
  5. For an animated dynamic mesh: create the `Mesh` once, call `MarkDynamic()`, **assign `vertices` before `uv`/`triangles`** (mesh needs a vertex buffer to exist before those arrays will "stick"), and every frame after mutating vertices call `RecalculateNormals()` **and** `RecalculateBounds()` (skipping bounds recalculation causes incorrect frustum culling of deforming meshes).
  6. If using a particle-style shader for a mesh renderer (common for glow effects), also assign flat white `mesh.colors` — many such shaders multiply by vertex color and will render black without it.
  7. For a "trail that only appears while moving" particle effect, disable the built-in `emission` module and manually call `ParticleSystem.Emit(n)` once per frame gated on a minimum distance-moved threshold from the last emission position — smoother and cheaper than continuous automatic emission.
  8. For a 2D sprite-sheet of many small variant icons (e.g. multiple rune glyphs) rendered via one particle system, set `textureSheetAnimation.enabled = true`, `numTilesX/Y`, `animation = WholeSheet`, and randomize `startFrame` via a `MinMaxCurve` — each particle then shows a different atlas cell.
  9. Cache generated materials in a dictionary keyed by a cheap composite of shader/texture/color identity to avoid rebuilding materials every frame or per-entity.
- **Reusable pattern/snippet:**
```csharp
Shader best = Shader.Find("Particles/Standard Unlit") ?? FindByHeuristicScan();
var m = new Material(best);
m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
m.SetInt("_ZWrite", 0);
m.EnableKeyword("_ALPHABLEND_ON");

// Distance-gated manual particle trail:
if (Vector3.Distance(tip.position, lastEmitPos) > 0.1f) { ps.Emit(1); lastEmitPos = tip.position; }
```

---

### 4. Custom Equipment Item Creation via Jotunn (item cloning)

- **Purpose:** Defines the four wing items as real, craftable, equippable Valheim items using Jotunn's item pipeline, by cloning existing vanilla cape items rather than authoring new models.
- **Key files:** `WingsItem.cs`
- **Architecture:**
  - `WingsItem.Init()` subscribes to `Jotunn.Managers.PrefabManager.OnVanillaPrefabsAvailable`, deferring item creation until vanilla prefabs are actually loaded (they aren't available at plugin `Awake` time).
  - Each tier is created via `new Jotunn.Entities.CustomItem(newPrefabName, existingVanillaPrefabName, new ItemConfig{...})` — e.g. `new CustomItem(CrudeName, "CapeDeerHide", ...)` clones the vanilla Deer Hide Cape prefab (mesh, `ItemDrop`, stats, icon) and renames it, then `ItemConfig` overrides `Name`, `Description`, `CraftingStation`, `MinStationLevel`, and `Requirements` (array of `RequirementConfig{Item, Amount}` parsed from a `"Item:Amount,Item:Amount"` config string via `ParseRequirements()`).
  - Registered into the game with `Jotunn.Managers.ItemManager.Instance.AddItem(customItem)`.
  - Higher tiers clone progressively "better" vanilla capes (`CapeTrollHide`, `CapeLox`, `CapeFeather`) purely to reuse their existing meshes/icons as a starting visual, even though the actual mesh gets suppressed later (System 5) since the mod supplies its own procedural VFX instead.
  - `StripCapeVisuals(prefab)` post-processes the cloned prefab: nulls out `m_itemData.m_shared.m_equipStatusEffect` (removing whatever vanilla on-equip effect the cape had), and reflectively finds any `Cloth` component (`comp.GetType().Name == "Cloth"`, avoided a direct reference to `UnityEngine.ClothModule` to skip adding that dependency) and disables it via reflection (`GetType().GetProperty("enabled").SetValue(comp, false, null)`) so the cloth-physics cape simulation never runs, even on the dropped-item ground model.
  - Each tier's hash is precomputed once: `int hash = ItemName.GetStableHashCode()`, used later to identify equipped items by hash rather than string (see System 5) because `VisEquipment` only exposes hashes for remote players.
- **How to implement:**
  1. Add `[BepInDependency(Jotunn.Main.ModGuid)]` to the plugin and reference `Jotunn.dll`.
  2. Defer custom item/prefab creation to `Jotunn.Managers.PrefabManager.OnVanillaPrefabsAvailable` (unsubscribe after firing once) — vanilla prefabs are not guaranteed loaded during `Awake`.
  3. To reuse an existing vanilla item's visuals/behavior without building a new model, use `new CustomItem(yourNewName, "VanillaPrefabName", new ItemConfig{...})` — this clones the vanilla prefab under a new name.
  4. Set `ItemConfig.CraftingStation` (vanilla station prefab name string), `MinStationLevel`, and `Requirements` (`RequirementConfig[]` of item name + amount) to define crafting recipe.
  5. Call `ItemManager.Instance.AddItem(customItem)` to register it so it appears in the crafting UI and can be spawned/equipped like any vanilla item.
  6. If you need to strip an inherited visual/behavior component you don't want (e.g. cloth simulation, on-equip status effect) but don't want a hard assembly reference to the module that defines it, use `Type.GetProperty`/reflection to toggle it — avoids adding an extra DLL reference just to disable one component.
  7. Precompute `GetStableHashCode()` for your item's prefab name once at class-init and keep it around — Valheim's networked equipment-visual system (`VisEquipment`) frequently only exposes item identity as an `int` hash, not a name/reference, especially for remote players.

---

### 5. Suppressing a Vanilla Equipment Visual (Harmony patch pair on `VisEquipment`)

- **Purpose:** Because the wing items are clones of vanilla capes, equipping them would normally also attach the original cape mesh to the player's shoulders via Valheim's standard equipment-visual pipeline. This system prevents that attachment from ever being built, so only the mod's own procedural VFX (System 3) is visible.
- **Key files:** `CapeVisualPatch.cs`, `WingsItem.cs` (`IsWingsHash`)
- **Architecture:**
  - Explains (in code comments) *why* the naive fixes don't work: `Renderer.forceRenderingOff` is a runtime-only flag not preserved across `Instantiate`; `Renderer.enabled` would survive but still shows the item on the ground and can be flipped back on by other mods/game logic that walks the renderer list; hiding renderers on the source prefab doesn't help because `VisEquipment.AttachArmor` instantiates a **fresh copy** of the item's `attach_skin` child at equip time, independent of the prefab's current renderer state.
  - **Primary fix — Harmony Prefix on `VisEquipment.AttachArmor(int itemHash)`:** checks `WingsItem.IsWingsHash(itemHash)`; if true, sets `__result = new List<GameObject>()` (empty, not `null`, because callers store this directly into internal `m_*ItemInstances` lists and iterate them later to destroy old attachments — a `null` would NRE elsewhere) and returns `false` to **skip the original method entirely**, so the attachment is never instantiated in the first place.
  - **Safety-net — Harmony Postfix on `VisEquipment.SetShoulderEquipped`, `[HarmonyPriority(Priority.Last)]`:** in case some other mod's own prefix on the same method bypasses this mod's prefix and the attachment gets built anyway, this postfix runs after all other patches (guaranteed by `Priority.Last`) and force-`SetActive(false)`s every entry in the private `m_shoulderItemInstances` list. Access to that private field is via a cached `AccessTools.FieldRef<VisEquipment, List<GameObject>>` built through the shared `ReflectionUtil.TryFieldRef` helper (System 7) rather than `Traverse` (faster, since this runs frequently).
- **How to implement:**
  1. Identify the vanilla method responsible for instantiating the unwanted attached visual (for equipment slots this is typically on `VisEquipment`, e.g. `AttachArmor`/`AttachShoulderItem`/etc. depending on slot).
  2. Add a `[HarmonyPrefix]` on that method that checks whether the item hash/id belongs to your custom item, and if so, sets `__result` to a safe non-null empty value of the correct return type and returns `false` to skip original execution — this is the cleanest way to fully suppress a vanilla visual-attachment pipeline.
  3. Inspect (via decompiler/ILSpy) what the *caller* of that method does with the return value before deciding whether `null` or an empty collection is safe — mismatching can NRE deep in game code.
  4. Add a defensive `[HarmonyPostfix]` with `[HarmonyPriority(Priority.Last)]` on whatever method actually flips the attachment's active/visible state, as a second line of defense against mod-load-order conflicts; use it to forcibly hide any instances that got created anyway.
  5. Cache any private field access needed for the safety-net via `AccessTools.FieldRefAccess<T,F>` (wrapped in a try/catch at static-init so a future game update that renames/removes the field disables only this one feature, not the whole plugin — see System 7).

---

### 6. Config System with Server-Enforced Sync (Jotunn `ConfigurationManagerAttributes`)

- **Purpose:** Centralizes every tunable stat (per-tier flight ceiling, glide speed, flap force, stamina cost, crafting station/level/requirements, plus a global enable toggle and wing-size multiplier) as BepInEx config entries, and marks them admin-only so a dedicated server enforces identical values on all connecting clients.
- **Key files:** `ModConfig.cs`, `WingsoftheValkyriePlugin.cs`
- **Architecture:**
  - `ModConfig.Init(ConfigFile config)` is called from `Plugin.Awake()` with the plugin's own `Config` object, and binds ~30 `ConfigEntry<T>` fields organized into numbered sections (`"1. General"`, `"2. Crude Wings"`, … `"5. Dragon Wings"` — numeric prefixes force ordering in the BepInEx Configuration Manager UI).
  - Each `config.Bind(section, key, default, ConfigDescription)` call passes a shared `ConfigurationManagerAttributes { IsAdminOnly = true }` (from `Jotunn.Configs`) as the tag object on the `ConfigDescription`. `AcceptableValueRange<T>` instances constrain sliders/inputs in the config UI (e.g. ceiling 10–5000, speed/force 1–100, stamina 0–100, station level 1–10).
  - Because the plugin declares `[BepInDependency(Jotunn.Main.ModGuid)]`, Jotunn's synchronization layer automatically watches this plugin's `ConfigFile` and, per the `general_info.txt` documentation shipped with the mod, replicates admin-only-tagged values from the server to all connected clients live (no relog required), while preventing non-admin clients from overriding them locally. This sync behavior is provided by the Jotunn framework itself (the mod code contains no explicit sync/RPC calls for config) — the mod's only job is tagging entries `IsAdminOnly = true` via `ConfigurationManagerAttributes`.
  - `CraftingRequirements` are stored as flat strings (`"Feathers:10,LeatherScraps:10"`) rather than structured config, parsed at item-creation time by `WingsItem.ParseRequirements` (`Split(',')` then `Split(':')`, `int.TryParse` for amount, skipping malformed entries) — a simple approach to let a single string config field describe an arbitrary-length requirement list without needing a custom config type.
- **How to implement:**
  1. Reference `Jotunn.dll` and add `[BepInDependency(Jotunn.Main.ModGuid)]` plus `[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]` on your `BaseUnityPlugin`.
  2. In `Awake()`, call `Config.Bind(section, key, default, new ConfigDescription(desc, acceptableValueRange, new ConfigurationManagerAttributes { IsAdminOnly = true }))` for every server-controlled tunable — Jotunn handles pushing these to clients and locking client-side edits automatically once the mod is loaded through Jotunn on both ends.
  3. Use numeric section-name prefixes (`"1. General"`, `"2. ..."`) purely to control display order in the config-manager UI, since it typically sorts alphabetically.
  4. For variable-length list-like settings (e.g. crafting requirements) that don't map to a single scalar, encode them as a delimited string (`"Item:Amount,Item:Amount"`) and parse manually at consumption time, rather than trying to bind a complex type through `ConfigEntry<T>`.
  5. `VersionStrictness.Minor` (rather than `.Full`) lets you ship minor-version updates without forcing every peer to update in lockstep — but then any new sync data (like the ZDO keys in System 2) must have a fallback path for peers who don't publish it yet (see System 2's fallback).

---

### 7. Reflection Utility for Forward-Compatible Private Field Access

- **Purpose:** A single guarded helper for binding fast private-field accessors to vanilla game types, so that if a future Valheim update renames/removes a field, only the dependent feature silently degrades instead of the whole plugin throwing a `TypeInitializationException` at load and disabling itself entirely.
- **Key files:** `ReflectionUtil.cs` (used by `CapeVisualPatch.cs` and `WingsItem.cs`)
- **Architecture:** One static method:
  ```csharp
  public static AccessTools.FieldRef<T, F> TryFieldRef<T, F>(string fieldName)
  {
      try { return AccessTools.FieldRefAccess<T, F>(fieldName); }
      catch (Exception ex) { Jotunn.Logger.LogWarning(...); return null; }
  }
  ```
  Consumers store the result in a `static readonly` field (so the binding attempt happens once, at type-init) and null-check before use at every call site (e.g. `WingsItem.ShoulderItemRef`, `CurrentShoulderHashRef`; `CapeVisualPatch.ShoulderInstancesRef`). This is chosen over `HarmonyLib.Traverse` for any code path invoked every frame per-entity, since `Traverse` re-resolves reflection each call and is noted in comments as "too slow" for that use case, whereas `AccessTools.FieldRefAccess` compiles a cached delegate once.
- **How to implement:**
  1. Wrap `HarmonyLib.AccessTools.FieldRefAccess<TInstanceType, TFieldType>("fieldName")` in a try/catch that logs a warning and returns `null` on failure, instead of letting the exception propagate out of a static initializer.
  2. Call it once per field, storing the result in a `private static readonly AccessTools.FieldRef<T,F>` — this front-loads all reflection binding to plugin/type load time.
  3. At every use site, null-check the field ref before invoking it (`fieldRef(instance)` to read, or the delegate supports `= value` style writes since `FieldRef` returns a `ref`), and skip/no-op the dependent feature gracefully if null.
  4. Prefer this pattern over `Traverse.Create(x).Field(...)` for anything called per-frame/per-entity; reserve `Traverse` for one-off or low-frequency access where the extra convenience outweighs the reflection overhead (as `FlightController.cs` does for `m_moveDir`/`m_maxAirAltitude`, called once per fixed-update per local player only).

---

### 8. `discord/` Folder — Marketing Copy, Not Code or Integration

- **Purpose:** Contrary to the "unusual for a Valheim mod" framing, this folder is **not** a Discord bot, webhook client, or any executable integration. It contains three plain-text `.txt` files that are draft release-announcement copy and a player-facing reference guide, intended to be manually copy-pasted into a Discord server/channel by the mod author when publishing releases. There is no code here at all (no `.cs`, no JSON manifest, no webhook URL, no bot token, no API calls).
- **Key files:**
  - `discord/announcement.txt` — a v1.0.0 release announcement (`@everyone` ping, feature bullets, "Hexium link here" placeholder — Hexium being the Thunderstore-alternative mod host this author distributes through).
  - `discord/announcement_v2.txt` — a longer, more polished rewrite of the same announcement (same structure: ping, hook, feature list, download CTA placeholder), suggesting iterative copywriting rather than versioned releases tied to the mod version.
  - `discord/general_info.txt` — a "Skald's Guide" formatted as player-facing documentation: flight controls (double-jump to glide, flap to ascend, look-down to dive), the "no fall damage while gliding" feature description (confirms/explains System 1's `m_maxAirAltitude` trick from a design-intent perspective), a table of all four wing tiers' stats and recipes (mirrors `ModConfig.cs` defaults exactly), and a short section on config/server-sync behavior explaining the `IsAdminOnly` server-sync system (System 6) in player-facing terms, plus the literal config file path (`BepInEx/config/wubarrk.wingsofthevalkyrie.cfg`).
- **How to implement (i.e., how to replicate this practice in another mod project):** This is a documentation/community-ops convention, not an engineering system — there is no API or library to integrate. To replicate: keep a `discord/` (or `docs/`, `marketing/`) folder alongside the mod source containing (a) one or more announcement drafts written in Discord markdown (`**bold**`, `@everyone`, emoji headers) sized for a single Discord message, and (b) a plain-text player guide summarizing controls/config/recipes that mirrors the actual shipped config defaults, so the author can keep announcement/doc copy under version control next to the code it describes and update it in the same commits as balance changes, without needing any bot/webhook infrastructure.
- **Reusable pattern/snippet:** N/A (no code) — worth noting for the master cross-mod reference doc only as a repo-organization convention this author uses, not a technical pattern.

---

### 9. Config Migration — rebasing DEFAULTS across versions (2.0.0, extended 2.0.1)

- **Purpose:** Solves a failure mode every configurable mod has and most ship with: BepInEx writes *every* bound value into the `.cfg`, so an old config file carries the old defaults as literal lines, and on upgrade those stale lines silently win over the new balance. The player is then playing last version's numbers with this version's changelog, with nothing anywhere saying so.
- **Key files:** `ConfigMigration.cs`, `ModConfig.cs`
- **Architecture:**
  - Distinct from TortalPortal's migration, which carries *renamed keys* to new homes. Here **no key ever moved** — what changed is the DEFAULTS. So the rule is: *a stored value still equal to its old default belongs to the mod and is rebased; a stored value an admin changed is real work and is preserved untouched.*
  - A `[0. Meta] ConfigVersion` stamp on the file. Deliberately **not** admin-synced — it describes the local file's layout, not a gameplay rule the server should push out.
  - `Begin(config)` runs **before the first `Bind`**, which is the only moment the raw file is still as the previous version left it. It snapshots the INI by hand, reads the stamp, backs the file up, and *plans* the rebases.
  - `Finish(config, versionEntry)` runs **after every `Bind`** and applies them as `entry.BoxedValue = entry.DefaultValue`. No string round-trip, so type conversion and range clamping stay BepInEx's problem.
  - Migrations are a `Dictionary<int, Rebase[]>` keyed by the version they *produce*, so a file at version 0 walks 0→1→2 in one pass and picks up every step's rebases.
  - Every failure path is caught and logged: a broken migration must never stop the mod loading. Worst case the config binds exactly as it always did.
- **The trap:** each version step compares against the value **on disk**, not the post-previous-step value. That is correct as long as no key is rebased by two different steps; if one ever is, the later step must list the earlier step's default in its `OldDefaults` too. The array is `OldDefaults` (plural) for exactly this reason.
- **Verification (2.0.1):** the migration is exercised off-game against committed copies of real `.cfg` files — see System 13. That is also how the `OldDefaults` strings were confirmed to match what BepInEx actually serialises (`120`, not `120.0`; `0.15`, not `0.150000`), which is otherwise a guess.
- **How to implement:** bump `CurrentConfigVersion`; add the moved entries to `Rebases` keyed by the new version, each listing every old default it might find on disk. `Begin`/`Finish` do the rest.

---

### 10. Custom Skill via Jotunn `SkillManager` (2.0.0)

- **Purpose:** Adds "Valkyrie Flight" as a real Valheim skill — in the skills panel, with its own icon, levelled by play, and docked on death like any vanilla skill.
- **Key files:** `FlyingSkill.cs`
- **Architecture:**
  - `SkillManager.Instance.AddSkill(new SkillConfig { Identifier, Name, Description, Icon, IncreaseStep })`. Jotunn hashes `Identifier` into the `Skills.SkillType` value.
  - **The identifier must never change.** It *is* the save key: renaming it orphans every player's accumulated levels under the old hash, invisibly, with no error.
  - Because progress lives in the player save like any vanilla skill, the **death penalty and skill-loss protection come for free** — no custom persistence and no patch on death.
  - XP from two sources: a discrete event (`AddFlapXP`) and a continuous one (`AccumulateGlideXP`). The continuous one awards in whole-second ticks and **carries the fractional remainder across sessions of gliding**, so short hops accumulate instead of being rounded away at every landing.
  - `Factor()` returns `GetSkillFactor` (0..1) for scaling stats; `Level()` returns `GetSkillLevel` (0..100) for threshold comparisons. Keep both — mixing them up is a silent factor-of-100 error.
- **How to implement:** register in `Awake` before `harmony.PatchAll()`; store the returned `SkillType`; guard every use on `SkillType != Skills.SkillType.None` and decide deliberately whether that guard fails open or closed (see System 1 — for a *gate*, open).

---

### 11. Flight Logbook — per-character stats in `Player.m_customData` (2.0.1)

- **Purpose:** Records a character's whole flying history — time, distance, wingbeats, records, and a set of deliberately odd counters — with no save file of the mod's own.
- **Key files:** `FlightSaga.cs` (data + format), `FlightLog.cs` (tracking), `FlightLogPatches.cs` (load/save hooks), `FlightLogCommand.cs` (readout)
- **Architecture:**
  - Storage is one entry in `Player.m_customData`, the vanilla `Dictionary<string,string>` Valheim serialises inside the character save. The saga therefore travels with the character across worlds, servers and reinstalls. (Same lever as Fatty/DvergrAllies/ShadowsOfMidgard — see the master doc's cross-cutting list.)
  - Format is `key=value;key=value`, invariant culture — chosen over JSON so the saga costs one dictionary entry and pulls in no serialiser. **Unknown keys are skipped on read**, so a saga written by a newer build still loads in an older one.
  - **Load ordering is not something a Harmony patch may assume.** `Player.Update` can run before the profile has been read back into `m_customData`. The resolution: a *forced* read from the `Player.Load` postfix (the exact moment custom data is known populated) always wins, while `OnSpawned` and the per-frame path only `EnsureLoaded` — an unforced bind can never overwrite a forced one with an empty saga. A brand-new character is never `Load()`ed at all, which is why the `OnSpawned` hook has to exist too.
  - `Flush` refuses to write unless the saga was read *from that same character*. Without that guard, a save during the pre-bind window stamps an empty logbook over a real one.
  - Written on landing, on a 10-second in-flight checkpoint (dying mid-air never reaches a landing), and from a `Player.Save` **prefix** — prefix, not postfix, so the data is inside `m_customData` before the game serialises it.
  - Sanity guards on the inputs: steps implying >200 m/s are dropped, so portals and physics hiccups do not become distance or speed records.
- **Readout:** a Jotunn `ConsoleCommand` (`wov log` / `oddities` / `export` / `where`), `IsCheat = false`.

---

### 12. Client→Server Stat Reporting + BarrkBOT Export (2.0.1) ⭐

- **Purpose:** Gets client-measured flight numbers onto the server so BarrkBOT can answer questions about them in Discord.
- **Key files:** `FlightReport.cs`
- **Why it exists at all — the finding worth carrying to every other mod:** everything the logbook measures (glide time, altitude, speed, wingbeats) is **client-side physics**, simulated only on the peer that owns the ZDO. A dedicated server never sees any of it. And BarrkBOT can read the **server box and nothing else**. A mod that just wrote a file locally would write it on the wrong machine — correct-looking code, permanently invisible output. Same family as `Character.OnDeath` not firing server-side (BlightedHeart) and TheRavensCall's findings doc.
- **Architecture:** clients send totals up over one routed RPC (`WOTV_FlightSaga`, `long playerId, string name, string saga`), the server keeps the latest row per character, and **the server** writes `BepInEx/config/WingsOfTheValkyrie/barrkbot_flight.json`.
- **Details that are all bug-shaped if missed:**
  - Registration keyed off the **`ZRoutedRpc` instance**, not a bool — the router is rebuilt per network session, so an instance compare re-registers on world change with no teardown patch to forget.
  - `ZRoutedRpc.GetServerPeerID()` is **private**; `ZNet.instance.GetServerPeer().m_uid` is public and identical.
  - `ZNet.instance.IsServer()` short-circuits the RPC — solo and listen-server are "the server is right here", and a world with no peers may have nothing to route to.
  - Register **before** the send throttle and before accepting a row, since registration is what clears the previous session's state.
  - A dedicated server has no local player to hang an update on, so the **plugin's own `Update()`** carries the export heartbeat.
  - The server's row set persists to `flight_registry.dat` beside the export — a non-`barrkbot_*` name deliberately, so the sweep never sees two files claiming the same facts. Without it a restart blanks the export until everyone logs in again.
  - Reported numbers are whatever the client says they are. Stated plainly in the README rather than pretended otherwise.
- **The full export contract lives in [BarrkBOTExports.md](BarrkBOTExports.md)** — filename regex, depth limit, `_notes` guidance keys, unit-suffixed field names, and the rule that an empty `players` map means "not recorded yet" and never zero. Read that before writing an export for any mod.

---

### 13. Off-Game Test Harnesses (2.0.1) ⭐

- **Purpose:** Makes the parts most likely to fail silently — serialization, config migration, the export written for an outside reader — testable without launching Valheim.
- **Key files:** `tests/run-tests.sh`, `tests/FlightLogTests/`, `tests/ConfigMigrationTests/`
- **Architecture:** each harness is a plain **net8 console program** that pulls the mod's *real* source files in via `<Compile Include="../../FlightLog.cs" />` and supplies small stand-ins for the game surface those files touch (`Player`, `ZNet`, `ZRoutedRpc`, `ZoneSystem`, `EnvMan`, BepInEx's `ConfigFile`). No test framework, no mocking library.
  - The mod targets `net48` and cannot run on this box at all (no Mono); the harnesses target `net8.0`, which runs anywhere `dotnet` does. The source files are shared, the target framework is not.
  - **It compiles the shipping source, not a copy.** A harness that duplicates the logic proves nothing about the logic that ships, and drifts.
  - `ConfigMigrationTests` runs the real `Begin`→bind→`Finish` cycle against **committed copies of real `.cfg` files** in `fixtures/`. The first version read a live Gale profile, which bound the test to one machine and would have kept "passing" against a file that had drifted underneath it.
  - `FlightLogTests` also emits the two sample exports (empty server, populated server) that BarrkBOT's reader wants for a read-back before an export ships.
- **The one gotcha:** `Microsoft.NET.Sdk` globs `**/*.cs`, so the plugin's own `.csproj` needs `<Compile Remove="tests/**" />` or it will try to compile the harnesses and their stubs into the mod.
- **How to implement elsewhere:** identify the files with no Unity dependency in their *logic* (serializers, parsers, state machines), stub only the game types their signatures mention, and target a runtime you can actually execute. The stub surface is usually far smaller than it looks — `FlightLog.cs` needed nine types.
- **Reusable pattern/snippet:**
```xml
<!-- tests/XTests/XTests.csproj — the real source, compiled against stubs -->
<PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup>
<ItemGroup><Compile Include="../../TheRealFile.cs" /></ItemGroup>
```
```xml
<!-- TheMod.csproj — keep the harnesses out of the plugin build -->
<ItemGroup><Compile Remove="tests/**" /><None Remove="tests/**" /></ItemGroup>
```
