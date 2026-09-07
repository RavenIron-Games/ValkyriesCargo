# SoM v1 SENSING SURVEY — VisibilitySystem / NoiseSystem / HidingSystem / CamoSystem / Armor / Utils

All line refs are `file:line`. Vanilla refs are `assembly_valheim.decompiled.cs:LINE` (abbrev. `AV:`), server build `assembly_valheim_SERVER.decompiled.cs` (abbrev. `AVS:`). Every vanilla member below was grepped and confirmed present unless explicitly marked NOT FOUND.

---

## 0. HEADLINE

**All four sensing entry points are 100% PLAYER-INTRINSIC.** There is not one observer-dependent term inside `VisibilitySystem.GetVisibility`, `NoiseSystem.GetNoise`, `HidingSystem.GetHidingFactor`, or `CamoSystem.GetCamoFactor`. Every observer-relative term (distance, falloff, FOV, LOS) lives in `Systems/StealthBrain/SensingEvaluator.cs:16-35` and `Systems/AwarenessSystem.cs:38-52`. The per-player-track refactor is therefore *clean*: hoist the four getters to one per-player-per-tick cache, and the only thing that must stay per-(observer,target) is ~6 lines of arithmetic plus one `Physics.Linecast`.

**But** they are *not* multiplayer-correct today, for a different reason: `Player.IsCrouching()` and `Player.GetVelocity()` are the only two remote-safe inputs; the grass/bush counters (`CoroutineManager.PlayerGrassHitCount/PlayerBushHitCount`) are written **only for `Player.m_localPlayer`** (`Systems/CoroutineManager.cs:67-80`), so every remote player permanently reads `grass=0, bush=0` → **no grass/bush hiding for anyone but you**. And `StealthOvermind.EvaluateLoop` (`Systems/StealthOvermind.cs:39`) only ever evaluates against `Player.m_localPlayer`, so remote players are invisible to the whole brain regardless.

---

## 1. VisibilitySystem.cs (122 lines)

### Public surface
`public static float GetVisibility(Player p)` — `VisibilitySystem.cs:21`

### Exact top-level formula (`:37`)
```
vis = Clamp01( light − shadow − grass − weather + movement + armor )
```
Early-out `return 1f` (fully visible) if `p == null || cfg == null || !cfg.EnableVisibilitySystem` (`:24-25`). Note the polarity: the *disabled* path returns **1.0**, not 0 — so turning the visibility system off makes everyone maximally visible.

### Term-by-term

**`LightExposure(p, cfg)` — `:45-58`**
```csharp
float light = RenderSettings.sun != null ? RenderSettings.sun.intensity : 0.5f;
float nightFloor = 0.15f;                       // hardcoded
float dayBonus   = cfg.LightPenalty;            // default 0.35
return Clamp01(0.15f + cfg.LightPenalty * Clamp01(light));
```
- `p` **is unused** — this term is world-global, not even player-dependent.
- Magic numbers: `0.5f` fallback, `0.15f` night floor.
- Default range: `0.15` (light=0) → `0.50` (light≥1).

**`ShadowBonus(p, cfg)` — `:60-70`**
```csharp
if (RenderSettings.sun == null) return 0f;
Vector3 pos    = p.transform.position + Vector3.up * 1.5f;   // magic 1.5
Vector3 sunDir = -RenderSettings.sun.transform.forward;
bool inShadow  = Physics.Raycast(pos, sunDir, 20f, VegetationMask);  // magic 20
return inShadow ? cfg.ShadowBonus : 0f;                       // default 0.25
```
1 raycast per call.

**`GrassBonus(p, cfg)` — `:72-84`**
```csharp
int hitsCount = CoroutineManager.PlayerGrassHitCount[p] (0 if absent);
grassBonus = hitsCount > 0 ? Min(hitsCount * 0.15f, 0.6f) : 0f;
```
- **`cfg` is unused.** `cfg.GrassBonus` (`StealthConfigModel.cs:53`, default `0.20`, bound at `SOMConfig.cs:105-107` as section "3 - Visibility" key "GrassBonus") is **write-only dead config** — nothing in the codebase reads it. Verified by full-repo grep.
- Magic numbers `0.15f` per hit, `0.6f` cap.

**`MovementPenalty(p, cfg)` — `:86-94`**
```csharp
float speed      = p.GetVelocity().magnitude;
float normalized = Clamp01(speed / 7f);        // magic 7 m/s
return normalized * cfg.MovementPenalty;       // default 0.8
```

**`ArmorVisibility(p, cfg)` — `:96-100`**
```csharp
return ArmorUtils.GetTotalStealthPenalty(p) * cfg.ArmorVisibility;  // default 0.6
```

**`WeatherReduction(cfg)` — `:102-120`** (no player argument at all — world-global)
```csharp
reduction = 0;
if (IsRaining() || IsSnowing()) reduction += cfg.WeatherVisReduction;   // 0.20
if (IsMisty())                  reduction += cfg.FogVisReduction;       // 0.30
float fogDensity = EnvironmentUtils.GetFogDensity();
if (fogDensity > 0.01f)         reduction += fogDensity * cfg.FogVisReduction;
return Clamp01(reduction);
```
Magic: `0.01f` fog epsilon. Note **double-dip**: env `"Mistlands_rain"` lowercases to contain both `"mist"` and `"rain"` → `0.20 + 0.30 + fog*0.30` = up to a full `1.0` reduction, i.e. total invisibility in Mistlands rain.

**`VegetationMask` — `:9-19`** — lazily cached in `_vegetationMask` (sentinel `-1`), one `LayerMask.GetMask("Default","piece","piece_nonsolid","static_solid","terrain")`. All five layer names confirmed used by vanilla (`AV:1165-1168`, `AV:4023-4025`).

---

## 2. NoiseSystem.cs (60 lines)

### Public surface
`public static float GetNoise(Player p)` — `NoiseSystem.cs:7`

### Exact top-level formula (`:18`)
```
noise = Clamp01( movement + armor − crouch − weather )
```
Disabled/null path returns **0f** (`:11`) — opposite polarity to VisibilitySystem, which returns 1f.

**`MovementNoise` — `:26-34`**: `Clamp01(p.GetVelocity().magnitude / 7f) * cfg.MovementNoise` (default `0.7`). Same magic `7f`.
**`ArmorNoise` — `:36-40`**: `ArmorUtils.GetTotalArmorNoise(p) * cfg.ArmorNoise` (default `0.6`).
**`CrouchReduction` — `:42-45`**: `p.IsCrouching() ? cfg.CrouchNoiseReduction : 0f` (default `0.6`).
**`WeatherReduction` — `:47-58`**:
```csharp
string name = EnvMan.instance.GetCurrentEnvironment()?.m_name?.ToLowerInvariant() ?? "";
if (name.Contains("rain") || name.Contains("snow") || name.Contains("mist"))
    return cfg.WeatherNoiseReduction;   // default 0.4
```
This duplicates `EnvironmentUtils` logic inline with a *different* code path (direct `EnvMan.instance` deref with an `if (env == null) return 0f;` guard at `:49-51`, vs. `?.` chaining in EnvironmentUtils).

---

## 3. HidingSystem.cs (70 lines)

### Public surface
`public static float GetHidingFactor(Player p)` — `HidingSystem.cs:21`

### Exact formula (`:31`)
```
hiding = Clamp01( bush + grass + crouch )
```
Disabled path returns `0f`.

- **`BushBonus` — `:39-50`**: `CoroutineManager.PlayerBushHitCount[p] > 0 ? cfg.BushBonus : 0f` (default `0.4`). Binary, count is discarded.
- **`GrassBonus` — `:52-63`**: `CoroutineManager.PlayerGrassHitCount[p] > 0 ? cfg.GrassBonus_Hiding : 0f` (default `0.25`). Binary.
- **`CrouchBonus` — `:65-68`**: `p.IsCrouching() ? cfg.CrouchBonus : 0f` (default `0.15`).

### Dead code
`HidingSystem._vegetationMask` / `VegetationMask` (`:7-19`, mask `"Default","piece","piece_nonsolid","static_solid"`) is **never referenced** — HidingSystem casts no rays.

### Grass is double-counted
The same `PlayerGrassHitCount` entry feeds both `VisibilitySystem.GrassBonus` (subtracted from vis, up to `0.6`) and `HidingSystem.GrassBonus` (added to hiding, `0.25`), and `SensingEvaluator.cs:15` then applies `vis * (1 - hiding)`. One grass collider therefore reduces effective visibility twice, multiplicatively.

---

## 4. CamoSystem.cs (119 lines)

### Public surface
`public static float GetCamoFactor(Player p)` — `CamoSystem.cs:43`

### Exact formula (`:52`)
```
camo = Clamp01( biome + armor )
```

**Static tables**
- `CamoFriendlyBiomes` (`:9-16`): Meadows, BlackForest, Swamp, Plains, Mistlands → full `cfg.BiomeCamo`.
- `PartialCamoBiomes` (`:19-22`): Mountain only → `cfg.BiomeCamo * 0.5f`.
- Everything else (Ocean, AshLands, DeepNorth, None) → `0f` (`:79`).
- `ArmorBiomeMap` (`:25-41`), keyword → biome set, 10 entries:
  `forest`/`troll` → {BlackForest, Meadows}; `swamp`/`root` → {Swamp}; `snow`/`fenring`/`wolf` → {Mountain, DeepNorth}; `plains`/`lox` → {Plains}; `mist` → {Mistlands}.

**`GetPlayerBiome(p)` — `:60-66`**: `Heightmap.FindHeightmap(pos)` then `hm.GetBiome(pos)`, else `Biome.None`.

**`BiomeMatch` — `:68-80`**: default `cfg.BiomeCamo = 0.15`.

**`ArmorMatch` — `:82-117`**:
```csharp
biome = GetPlayerBiome(p);                      // SECOND heightmap lookup this call
foreach (item in p.GetInventory().GetEquippedItems()) {
    profile = ArmorProfileSystem.GetProfile(item);
    if (profile == null || !profile.HasCamoTag) continue;
    string itemName = item.m_shared?.m_name?.ToLowerInvariant() ?? "";
    foreach (kvp in ArmorBiomeMap)              // 10 iterations × string.Contains
        if (itemName.Contains(kvp.Key) && kvp.Value.Contains(biome)) { matched = true; break; }
    if (matched) camoCount++;
}
if (camoCount > 0) bonus = cfg.ArmorCamo * Min(1f, camoCount / 4f);   // magic 4
```
Default `cfg.ArmorCamo = 0.10`. Max camo total = `0.15 + 0.10 = 0.25`.

### The name matching is against a LOCALIZATION TOKEN, not a display name
`ItemDrop.ItemData.SharedData.m_name` (`AV:58007`) is the raw token — vanilla always pipes it through `Localization.instance.Localize(item.m_shared.m_name)` for display (`AV:42543`, `AV:140465`, `AV:140721`). So every `.Contains(...)` in CamoSystem **and** `ArmorProfileSystem` is matching `"$item_helmet_trollleather"`-style strings. Consequences the architects must price in:
- `"troll"`, `"root"`, `"fenring"`, `"wolf"`, `"lox"` do appear in real tokens → those paths fire.
- `"forest"`, `"snow"`, `"plains"`, `"mist"` almost certainly match **no vanilla armor token** (Mistlands armor is `mage`/`carapace`/`feather`, Plains armor is `padded`, there is no `snow` armor). The Mistlands and Plains rows of `ArmorBiomeMap` are therefore effectively dead. (Token spellings should be re-verified against prefabs — they are not in the decompile.)
- `DetectCamo` also tags `"camo"` and `"ghillie"` (`ArmorProfileSystem.cs:66-67`), but neither keyword exists in `ArmorBiomeMap`, so a modded ghillie suit sets `HasCamoTag=true` and then always fails the biome match → contributes 0. Pure dead path.
- It also makes camo **non-localization-safe by accident in a good way** (tokens don't translate) but **breaks for any mod that sets a literal display name**.

---

## 5. Armor/

### ArmorProfile.cs (33 lines)
`ArmorProfile` = `{ ArmorWeight Weight = Light; ArmorMaterial Material = Cloth; bool HasCamoTag = false; }`.
`enum ArmorWeight { Light, Medium, Heavy }`; `enum ArmorMaterial { Cloth, Leather, Metal, Bone, Bark, Magic, Unknown }` — **`Bone`, `Bark`, `Magic`, `Unknown` are never assigned anywhere**, so `ArmorUtils.GetTotalArmorNoise`'s `default: noise += 0.2f` branch (`ArmorUtils.cs:73-75`) is unreachable.

### ArmorProfileSystem.cs (70 lines)
`private static readonly Dictionary<string, ArmorProfile> Cache = new();` (`:10`) — keyed on `item.m_shared.m_name`, **never pruned, never invalidated**. Bounded by item-type count so acceptable, but it means quality/upgrade level is invisible to profiling.

`GetProfile(ItemDrop.ItemData)` — `:12-29`. Null-guards `item`/`item.m_shared`.

**`DetectMaterial` — `:31-40`** (one `ToLowerInvariant()` alloc):
```
name.Contains("iron")||"bronze"||"metal" → Metal
else "leather"||"hide"                   → Leather
else                                     → Cloth
```

**`DetectWeight` — `:42-51`**, on `item.m_shared.m_armor`:
```
armor <= 12  → Light
armor <= 28  → Medium
else         → Heavy
```
Uses the **base** armor field, not `ItemData.GetArmor()` (`AV:58365-58373`, which adds `Max(0, quality-1) * m_armorPerLevel + worldLevel * Game.instance.m_worldLevelGearBaseAC`). A fully-upgraded item is scored at its quality-1 value. Also note **weapons and shields have `m_armor == 0`** → always `Light` → still `+0.1`.

**`DetectCamo` — `:53-68`** (a second `ToLowerInvariant()` alloc): 12 `Contains` checks — `forest, troll, swamp, root, snow, fenring, wolf, plains, lox, mist, camo, ghillie`.

**Trap**: `"$item_helmet_trollleather"` contains `"troll"` *and* `"leather"`. `DetectMaterial` checks metal first so it lands `Leather` — correct by luck, not by design.

### ArmorUtils.cs (82 lines)

`GetArmorProfile(item)` — `:7-10`, a pure pass-through to `ArmorProfileSystem.GetProfile`.

**`GetTotalStealthPenalty(Player)` — `:15-43`**
```csharp
foreach (item in player.GetInventory().GetEquippedItems())
    penalty += Light:0.1f | Medium:0.25f | Heavy:0.45f
return Clamp01(penalty);
```
No `player == null` guard — a null player NREs here (callers guard, but the API doesn't).

**`GetTotalArmorNoise(Player)` — `:48-80`**
```csharp
foreach (item in player.GetInventory().GetEquippedItems())
    noise += Metal:0.5f | Leather:0.25f | Cloth:0.1f | default:0.2f(unreachable)
return Clamp01(noise);
```
Both saturate at 1.0 almost immediately: 3 Metal pieces = 1.5 → clamps to 1.0. A player in full iron carrying an iron sword and shield is indistinguishable from one in bronze. The clamp destroys the resolution the tiers were meant to provide.

**Both iterate ALL equipped items, not just armor.** `Inventory.GetEquippedItems()` (`AV:57488-57499`) returns everything with `m_equipped == true` — weapon, shield, tool, utility, cape, plus the 4 armor slots. So an equipped bronze axe adds `+0.5` noise and `+0.1` visibility.

---

## 6. Utils/

### EnvironmentUtils.cs (35 lines)
```csharp
GetFogDensity()  => Clamp01(RenderSettings.fogDensity);                       // :7-10
IsRaining()      => (EnvMan.instance?.GetCurrentEnvironment()?.m_name.ToLowerInvariant() ?? "").Contains("rain");   // :12-16
IsSnowing()      => ...Contains("snow");                                      // :18-22
IsMisty()        => ...Contains("mist");                                      // :24-28
GetAmbientLight()=> Clamp(RenderSettings.ambientIntensity, 0f, 2f);           // :30-34  — NO CALLERS, dead
```
Each `Is*()` allocates a fresh lowercased string. `WeatherReduction` calls three of them → **3 string allocs per `GetVisibility`**. Null-safety is correct (`EnvSetup.m_name` is initialized to `""` at `AV:81636`, so `.m_name.ToLowerInvariant()` after `?.` can't NRE).

**Env-name matching is wrong for `"ThunderStorm"`** — the vanilla env name literal is confirmed at `AV:81796` (`m_introEnvironment = "ThunderStorm"`), and it contains neither `"rain"` nor `"snow"`. Vanilla exposes the correct name-independent flag: `EnvSetup.m_isWet` (`AV:81641`), consumed by `EnvMan.CalculateWet()` → `GetCurrentEnvironment()?.m_isWet ?? false` (`AV:82625`). **That is the v1.0-safe binding.** Similarly `EnvMan.IsDaylight()` is `public static` and computed in FixedUpdate (`AV:82029`, `AV:82582-82585`), independent of any camera.

### RaycastUtils.cs (88 lines)
**`TerrainMask` — `:15-26`**, lazily cached (`-1` sentinel):
```csharp
LayerMask.GetMask("Default","static_solid","Default_small","piece","terrain","viewblock","vehicle")
```
This is an **exact, verbatim match** for vanilla `BaseAI.m_viewBlockMask` (`AV:4024`, field decl `AV:3953`, class `BaseAI` starts `AV:3809`). Same order, same seven names. Good.
- The XML comment at `:12-13` claims it "notably excludes `vegetation`" — **there is no `vegetation` layer in Valheim**; grep for `"vegetation"` in the client assembly hits only a console-command name (`AV:36549`). The comment is misleading but harmless.
- `m_viewBlockMask` is `private static` in `BaseAI`, so SoM cannot read it directly; mirroring is the right call, but a v1.0 layer-list change silently desyncs SoM from vanilla. Recommend reflecting the field with a fallback to this literal.

**`HasLineOfSight(from, to)` — `:31-36`**: `!Physics.Linecast(from, to, TerrainMask)`. **The only used member of this class.**

**Dead code (no callers anywhere in the repo):**
- `SkyVisible(pos)` — `:41-44`, `Physics.Raycast(pos, up, 50f, TerrainMask)`.
- `SampleOcclusion(pos, radius=1.5f)` — `:49-71`, **allocates a `Vector3[6]` array on the stack-less heap every call** and fires 6 raycasts, returns `hits/6f`.
- `InTallGrass(pos)` — `:76-86`, `Physics.SphereCast(pos + up*0.5f, 0.5f, down, out hit, 1f, TerrainMask)` then `hit.collider.name.ToLowerInvariant().Contains("grass"|"bush"|"plant")`.

**Divergence from vanilla LOS**: vanilla `BaseAI.CanSeeTarget(Character)` also rejects on `ParticleMist.IsMistBlocked(eyePoint, vector)` when `!m_mistVision` (`AV:4618`). SoM's `HasLineOfSight` ignores mist entirely — Mistlands creatures with `m_mistVision=false` see through mist under SoM.

---

## 7. THE GRASS/BUSH DATA SOURCE (CoroutineManager) — critical

`Systems/CoroutineManager.cs:36-37`:
```csharp
public static Dictionary<Player, int> PlayerBushHitCount  = new Dictionary<Player, int>();
public static Dictionary<Player, int> PlayerGrassHitCount = new Dictionary<Player, int>();
```
`UpdatePlayerVegetationCoroutine` — `:61-86`:
```csharp
int layerMask = LayerMask.GetMask("Default","static_solid","Default_small","piece");  // computed once, good
Collider[] colBuffer = new Collider[10];                                              // reused, good
while (true) {
    if (Player.m_localPlayer != null) {                                    // <<< LOCAL PLAYER ONLY
        int hits = Physics.OverlapSphereNonAlloc(Player.m_localPlayer.transform.position, 1f, colBuffer, layerMask);
        for (i..hits) { name = colBuffer[i].name.ToLowerInvariant();       // <<< 1 string alloc PER COLLIDER
                        if (name.Contains("bush")) bush++;
                        if (name.Contains("grass")) grass++; }
        PlayerBushHitCount[Player.m_localPlayer]  = bush;
        PlayerGrassHitCount[Player.m_localPlayer] = grass;
    }
    for (int i = 0; i < 5; i++) yield return null;    // ~every 6th frame
}
```
Findings:
1. **Local player only.** Every remote `Player` misses the dictionary → `hiding` loses up to `0.65`, `vis` loses up to `0.6` for them.
2. **Dictionaries are never pruned.** Entries keyed on a destroyed `Player` leak for the process lifetime.
3. **Radius `1f`, buffer 10** — magic numbers; buffer overflow silently truncates.
4. **Valheim grass has no colliders.** Ground clutter is `ClutterSystem` instanced rendering (`AV:102873`, `AVS:101028`); there is nothing on `Default`/`piece` named `"grass"` to overlap. Bushes (Raspberry/Blueberry pickables, `Bush01`) do have colliders, but `Collider.name` is the *collider GameObject's* name, which for many vegetation prefabs is a child named `collider`/`Cylinder`/`capsule`, not the prefab name. **Expect `grass` to be ~always 0 and `bush` to be unreliable in practice.** This needs an in-game measurement before the v2 design leans on it.

`UpdateNearbyAlliesCoroutine` — `:88-125` (adjacent, but the same allocation pattern): `new List<Character>(AwarenessSystem.GetAllTrackedCharacters())` **allocated every outer pass** (`:95`), `Physics.OverlapSphereNonAlloc(..., 30f, hitBuffer)` **with no layer mask** (defaults to `DefaultRaycastLayers`, `:104`), then `GetComponent<Character>()` per hit and `GetComponent<MonsterAI>()` **twice** per candidate (`:109` and `:111`).

---

## 8. VANILLA API SURFACE — every member touched, verified

| SoM call site | Vanilla member | Verified at | Notes / v1.0 risk |
|---|---|---|---|
| `VisibilitySystem.cs:48,62,66` | `UnityEngine.RenderSettings.sun` | — | **NOT set anywhere in either Valheim assembly.** Grep for `RenderSettings.sun` returns zero hits in client and server builds. Valheim's real sun is `EnvMan.m_dirLight` (`AV:81759`, public field), driven only inside `EnvMan.SetEnv` (`AV:82399+`). Whether `RenderSettings.sun` resolves to that same Light depends on the scene's Lighting-settings asset, which is not in the decompile. **If it is null, `LightExposure` is pinned at `0.15+0.35*0.5=0.325` forever and `ShadowBonus` is always 0.** Must be measured in-game. Safe replacements: `EnvMan.instance.GetSunDirection()` (`AV:82715`), `EnvMan.instance.m_dirLight.intensity`, `EnvMan.IsDaylight()` (`AV:82582`), or `StealthSystem.instance.GetLightFactor(point)` (`AV:96672`). |
| `EnvironmentUtils.cs:9` | `RenderSettings.fogDensity` | `AV:82436-82440` | Written **only inside `EnvMan.SetEnv`**, which early-returns when `Utils.GetMainCamera() == null` (`AV:82399-82403`; identical guard on server at `AVS:80975-80980`). Stale/default on a headless server. |
| `EnvironmentUtils.cs:32` | `RenderSettings.ambientIntensity` | `AV:96685` | Dead in SoM. |
| `EnvironmentUtils.cs:14,20,26`, `NoiseSystem.cs:49,53` | `EnvMan.instance`, `EnvMan.GetCurrentEnvironment()`, `EnvSetup.m_name` | `AV:82697-82715`, `AV:81634-81636` | Present. `GetCurrentEnvironment()` can return `null` (`m_currentEnv`) — both SoM call sites handle it. Camera-independent, works headless. |
| `VisibilitySystem.cs:88`, `NoiseSystem.cs:28` | `Character.GetVelocity()` | `AV:10273-10284` | `!m_nview.IsValid() → Vector3.zero`; `IsOwner() → m_body.linearVelocity`; **else → `m_nview.GetZDO().GetVec3(ZDOVars.s_bodyVelocity, Vector3.zero)`** (`ZDOVars.s_bodyVelocity` = `"BodyVelocity".GetStableHashCode()`, `AV:66440`). **This IS remote-safe** — a ZDO hash lookup, updated at ZDO sync rate (coarser than a frame). Note the property rename risk: Unity's `Rigidbody.velocity` → `linearWithVelocity` migration is already baked into this decompile. |
| `NoiseSystem.cs:44`, `HidingSystem.cs:67`, `AwarenessSystem.cs:47` | `Player.IsCrouching()` | `AV:21129-21132` (override), `AV:9755` (virtual base returns false) | `return GetCurrentAnimHash() == s_animatorTagCrouch`; `s_animatorTagCrouch = ZSyncAnimation.GetHash("crouch")` (`AV:15839`); `GetCurrentAnimHash` frame-caches off `m_animator.GetCurrentAnimatorStateInfo(0).tagHash` (`AV:10004-10035`). **Remote-safe** because ZSyncAnimation replicates the animator — but only while the remote player's Animator is actually updating (culling/LOD is a real risk at range). Cheap: frame-cached. |
| `AwarenessSystem.cs:48,49` | `Character.GetCenterPoint()`, `Character.m_eye` | `AV:8657-8660` (`m_collider.bounds.center`), `AV:6951` (`public Transform m_eye`) | Present. SoM aims at the same point vanilla does (`AV:4612`). |
| `AwarenessSystem.cs:51` | `Character.GetEyePoint()` | `AV:8652-8655` (`m_eye.position`) | Present on `Character` (class starts `AV:6814`). |
| `CamoSystem.cs:62` | `Heightmap.FindHeightmap(Vector3)` | `AV:111624-111634` | **Linear scan of `s_heightmaps` calling `IsPointInside` per entry.** Not free. |
| `CamoSystem.cs:64` | `Heightmap.GetBiome(Vector3, float=0.02f, bool=false)` | `AV:111022-111052` | Optional params → SoM's 1-arg call compiles to a 3-arg call site baked with the current defaults. If v1.0 changes the defaults or arity, SoM gets a `MissingMethodException` at runtime. Fast path when all 4 corners agree; otherwise 4 `Distance` + array scan. Alternative one-liner: `Heightmap.FindBiome(point)` (`AV:111648-111655`). |
| `CamoSystem.cs:9-41,70,84` | `Heightmap.Biome` enum | `AV:110589-110602` | `None=0, Meadows=1, Swamp=2, Mountain=4, BlackForest=8, Plains=0x10, AshLands=0x20, DeepNorth=0x40, Ocean=0x100, Mistlands=0x200, All=0x37F`. All six SoM-referenced members exist. AshLands is spelled with a capital `L`. |
| `CamoSystem.cs:88`, `ArmorUtils.cs:19,52` | `Humanoid.GetInventory()` → `Inventory.GetEquippedItems()` | `AV:13632-13635`, `AV:57488-57499` | **`GetEquippedItems()` allocates `new List<ItemDrop.ItemData>()` and walks the entire inventory on every call.** See §9. |
| `CamoSystem.cs:95`, `ArmorProfileSystem.cs:17,33,44,55` | `ItemDrop.ItemData.m_shared.m_name` / `.m_armor` | `AV:58005-58007` (SharedData, `m_name`), `AV:58370-58373` (`GetArmor`, references `m_shared.m_armor` + `m_armorPerLevel`) | Present. `m_name` is a localization token (see §4). |
| `RaycastUtils.cs:21-24` | `BaseAI.m_viewBlockMask` layer list | `AV:4024` | Mirrored verbatim; the field itself is `private static` (`AV:3953`) so it can't be read. |
| `StealthOvermind.cs:52` | `Character.GetAllCharacters()` | `AV:10316-10319` (`return s_characters`) | Present, returns the live list — **do not mutate; do not hold across frames**. |
| `StealthOvermind.cs:61,72` | `Character.IsTamed()`, `MonsterAI.IsSleeping()` | `AV:10644`, `AV:21463-21466` | Present. |
| `StealthExemption.cs:13` | `ZDO.GetInt(string, int=0)` | `AV:62713` | String overload present alongside `GetInt(int hash, int)` (`AV:62718`). The string overload hashes on every call — the int-hash overload is the cheap one for v2. |
| all | `Physics.Raycast/Linecast/SphereCast/OverlapSphereNonAlloc`, `LayerMask.GetMask`, `Mathf.*` | Unity | Fine. |

**Vanilla stealth machinery SoM bypasses entirely** (worth knowing for the v2 brief):
- `Character.GetStealthFactor()` — `AV:10094` virtual, `AV:21842-21853` Player override: owner returns `m_stealthFactor`, **non-owner returns `m_nview.GetZDO().GetFloat(ZDOVars.s_stealth)`** (`ZDOVars.s_stealth = "Stealth".GetStableHashCode()`, `AV:66670`). **This is an already-replicated, already-remote-safe, already-throttled per-player stealth scalar.** It is the single best existing hook for a per-player track, and SoM ignores it.
- `Player.UpdateStealth(dt)` — `AV:21812-21840`, runs at 2 Hz (`m_stealthFactorUpdateTimer > 0.5f`), formula `Lerp(0.5 + light*0.5, 0.2 + light*0.4, sneakSkill)` then `SEMan.ModifyStealth`, then `MoveTowards(..., dt/4f)` toward target. Note it also feeds status effects into stealth — SoM's model has **no `SEMan.ModifyStealth` hook at all**, so vanilla/modded stealth SEs are inert under SoM.
- `StealthSystem.instance.GetLightFactor(point)` / `GetLightLevel(point)` — `AV:96672-96720`. `GetLightLevel` refreshes `FindObjectsOfType<Light>()` once per second and then does up to 2 raycasts per shadow-casting light. Correct but expensive; `m_minLightLevel = 0.2f`, `m_maxLightLevel = 1.6f`.
- `BaseAI.CanSeeTarget(Character)` reference implementation — `AV:4596-4623`: range → `viewRange * target.GetStealthFactor()` → angle → `Physics.Raycast(eye→(crouch?center:eye), m_viewBlockMask)` → `ParticleMist.IsMistBlocked`.

---

## 9. PER-CALL ALLOCATIONS AND UNCACHED LOOKUPS

Costed per **one full `SensingEvaluator.EvaluateSensing`** (one AI × one player), which is what `StealthOvermind` fires at up to 5/frame, tiered 2/6/20 frames by distance.

### A. The debug strings are the single largest cost, and they fire with debug OFF
`VisibilitySystem.cs:39-40`, `NoiseSystem.cs:20-21`, `HidingSystem.cs:33-34`, `CamoSystem.cs:54-55` all do:
```csharp
StealthDebugger.LogPlayerSensing(p, SLOT_X, "Label",
    $"vis={vis:F2} (light={light:F2} shadow={shadow:F2} ...)");
```
The interpolated string is an **argument** — C# evaluates it at the call site, *before* `LogPlayerSensing` (`StealthDebugger.cs:234-241`) gets a chance to check `VisionDebugEnabled`. The project is `net472` (`ShadowsOfMidgard.csproj:61`) with `LangVersion latest` (`:62`); net472 has no `DefaultInterpolatedStringHandler`, so `$"..."` lowers to `string.Format(string, object[])` — **every float is boxed**.

Per full evaluation, with debug disabled:
- Visibility: 7 boxed floats + `object[7]` + `string.Format` result + 7 `float.ToString("F2")` intermediates
- Noise: 5 boxed + array + result
- Hiding: 4 boxed + array + result
- Camo: 3 boxed + array + result

≈ **19 boxes + 4 object arrays + ~23 strings ≈ 46 allocations per AI-evaluation, all discarded.** At 5 evals/frame × 60 fps that is ~14k allocations/sec of pure garbage before a single mob has moved. **Fix: guard-then-format (`if (StealthDebugger.VisionDebugEnabled) …`) or take a `Func<string>`/params-free overload.**

### B. `Inventory.GetEquippedItems()` — 3 List allocations + 3 full inventory scans
Called from `ArmorUtils.cs:19` (visibility), `ArmorUtils.cs:52` (noise), `CamoSystem.cs:88` (camo). Each allocates a `List<ItemDrop.ItemData>` and iterates every inventory slot (`AV:57490-57498`). Nothing about the result changes between the three calls, or between frames unless the player equips something.

### C. `CamoSystem` calls `GetPlayerBiome(p)` twice per invocation
`CamoSystem.cs:70` (BiomeMatch) and `CamoSystem.cs:84` (ArmorMatch) → **2× `Heightmap.FindHeightmap` linear scans + 2× `GetBiome`**.

### D. `CamoSystem.ArmorMatch` string churn
Per equipped item: one `ToLowerInvariant()` alloc (`:95`) + up to 10 `string.Contains` (`:98-105`). With ~8 equipped items → 8 allocs and up to 80 substring searches per call.

### E. `EnvironmentUtils` string churn
`IsRaining()` + `IsSnowing()` + `IsMisty()` = **3 `ToLowerInvariant()` allocations per `GetVisibility`**, plus a 4th inside `NoiseSystem.WeatherReduction` (`:53`).

### F. Physics per full evaluation
- 1 × `Physics.Raycast` — `VisibilitySystem.ShadowBonus` (`:68`), **player-intrinsic, wastefully repeated per observer**
- 1 × `Physics.Linecast` — `RaycastUtils.HasLineOfSight` via `AwarenessSystem:51`, genuinely per-(observer,target)
- Amortised: 1 × `Physics.OverlapSphereNonAlloc` per ~6 frames (grass/bush, local player only), 1 × `OverlapSphereNonAlloc(30f, no mask)` per AI per pass (allies)

### G. Cheap-but-not-free, uncached
- `CoroutineManager.PlayerGrassHitCount.TryGetValue(p, …)` — `Dictionary<Player,int>` uses `EqualityComparer<Player>.Default`, which routes through `UnityEngine.Object.Equals/GetHashCode` (native-null-aware). Called 3× per evaluation (VisibilitySystem `:75`, HidingSystem `:42` and `:55`).
- `ArmorProfileSystem.Cache` lookups hash the full `$item_...` token string, once per equipped item per each of the 3 armor passes → ~24 string hashes per evaluation.
- `StealthDebugger.ShouldLog` runs `Prune()` (`StealthDebugger.cs:104-122`) which every 30 s allocates a `List<ThrottleKey>` and walks the whole dictionary. Reached from `LogPlayerSensing` **only after** the `VisionDebugEnabled` early-out, so this one is genuinely free when disabled.

---

## 10. WHERE THE COMPUTATION IS IMPLICITLY "THE LOCAL PLAYER"

Inside the four sensing systems the `Player p` argument is honoured everywhere — **the bug is upstream and downstream**:

1. **`StealthOvermind.EvaluateLoop`, `Systems/StealthOvermind.cs:39`**
   `Player p = Player.m_localPlayer;` — the *only* player the whole brain ever considers. If `p == null || p.IsDead()` the entire loop sleeps 1 s (`:40-44`) and **no AI on this client evaluates at all**, even ones that own remote players' aggro. Distance tiering (`:64-65`, `:101-106`) is measured to the local player. `MAX_TRACKING_RANGE = 64f` (`:13`) is a radius around the local player.

2. **`MonsterAI_StealthBrain_UpdateAI_Patch.cs:25`** — `Player player = Player.m_localPlayer;` (same pattern; other surveyors own this file, flagged for completeness).

3. **`CoroutineManager.UpdatePlayerVegetationCoroutine`, `Systems/CoroutineManager.cs:67-80`** — grass/bush sampled **only** for `Player.m_localPlayer`. Every remote player is permanently `bush=0, grass=0`. This is the one place inside the sensing data pipeline that is hard-wired local.

4. **`StealthUIController.Update`, `Systems/SteathUI/StealthUIController.cs:25,53-54`** — legitimately local (it's a HUD), but it calls `VisibilitySystem.GetVisibility(p)` and `NoiseSystem.GetNoise(p)` **every frame**, duplicating the whole cost (including all the debug-string garbage of §9A) on top of whatever the Overmind already computed for the same player in the same frame. A shared per-player cache eliminates this outright.

5. **`Systems/StealthBrain.cs:51`** — `Evaluate(MonsterAI ai, Player p, float dt)` takes a player, but `_evalCache` is keyed on `StableAIKey(NetworkUid, InstanceId)` alone (`:18-47`, `:73-77`, `:119-124`) — **no player component in the key**. So the moment a second player is passed in, the cache returns the *other* player's decision, and `AwarenessSystem`'s `Dictionary<Character, AwarenessData>` (`AwarenessSystem.cs:9`) stores exactly one `DetectionLevel`/`LastKnownPosition`/`TimeSinceSeen` per creature, overwritten by whichever player was evaluated last. This is the per-player-track hole in its concrete form.

6. **`Player.GetVelocity()` returns `Vector3.zero` when `!m_nview.IsValid()`** (`AV:10275-10278`) — a remote player whose ZNetView hasn't resolved reads as motionless: zero movement noise, zero movement visibility. Silent, not an exception.

7. **World-global values are recomputed per (AI, player) pair**: `LightExposure` ignores `p` entirely (`VisibilitySystem.cs:45-58`), `VisibilitySystem.WeatherReduction` takes no player (`:102`), `NoiseSystem.WeatherReduction` takes no player (`:47`). These are one-value-per-frame quantities being computed hundreds of times per frame.

---

## 11. THE SPLIT — PLAYER-INTRINSIC vs OBSERVER-RELATIVE

### Tier 0 — WORLD-GLOBAL (one value per frame for the whole client)
| Value | Site | Cost |
|---|---|---|
| `LightExposure` (`p` unused) | `VisibilitySystem.cs:45-58` | 2 property reads |
| `WeatherReduction` (visibility) | `VisibilitySystem.cs:102-120` | 3 `ToLowerInvariant` allocs + fogDensity |
| `WeatherReduction` (noise) | `NoiseSystem.cs:47-58` | 1 `ToLowerInvariant` alloc |

### Tier 1 — PLAYER-INTRINSIC, cheap, per-tick
| Value | Site | Input |
|---|---|---|
| `MovementPenalty` | `VisibilitySystem.cs:86-94` | `GetVelocity()` (ZDO read for remotes) |
| `MovementNoise` | `NoiseSystem.cs:26-34` | same velocity — **compute once, reuse for both** |
| `CrouchReduction` | `NoiseSystem.cs:42-45` | `IsCrouching()` (frame-cached anim hash) |
| `CrouchBonus` | `HidingSystem.cs:65-68` | same `IsCrouching()` — **compute once** |

### Tier 2 — PLAYER-INTRINSIC, expensive, sample at 1–5 Hz
| Value | Site | Cost |
|---|---|---|
| `ShadowBonus` | `VisibilitySystem.cs:60-70` | 1 raycast |
| `GrassBonus` (vis) | `VisibilitySystem.cs:72-84` | dict read; source = 1 OverlapSphere/6 frames |
| `BushBonus` + `GrassBonus` (hiding) | `HidingSystem.cs:39-63` | same dict, same source |
| `BiomeMatch` | `CamoSystem.cs:68-80` | `FindHeightmap` linear scan + `GetBiome` |

### Tier 3 — PLAYER-INTRINSIC, event-driven (equipment change only)
| Value | Site | Cost |
|---|---|---|
| `ArmorVisibility` | `VisibilitySystem.cs:96-100` → `ArmorUtils.cs:15-43` | List alloc + inventory scan |
| `ArmorNoise` | `NoiseSystem.cs:36-40` → `ArmorUtils.cs:48-80` | List alloc + inventory scan |
| `ArmorMatch` | `CamoSystem.cs:82-117` | List alloc + scan + N `ToLowerInvariant` + 10N `Contains`; also biome-dependent, so cache on `(equipHash, biome)` |

### Tier 4 — GENUINELY OBSERVER-RELATIVE (the only per-(observer,target) work)
All of it lives in **`Systems/StealthBrain/SensingEvaluator.cs`**:
```csharp
:15  float adjustedVis = vis * (1f - hiding) * (1f - camo);      // intrinsic combine, still Tier 1
:16  bool  hasLOS      = AwarenessSystem.HasLineOfSight(c, p);   // ← 1 Physics.Linecast, PAIR
:19  float distToPlayer = Vector3.Distance(c.pos, p.pos);        // ← PAIR
:20  float visFalloff   = 1f - Clamp01(distToPlayer / cfg.MaxVisualRange);    // MaxVisualRange 40
:21  float hearFalloff  = 1f - Clamp01(distToPlayer / cfg.MaxHearingRange);   // MaxHearingRange 25
:23  adjustedVis   *= visFalloff;
:24  adjustedNoise  = noise * hearFalloff;
:27  Vector3 dirToPlayer = (p.pos - c.pos).normalized;           // ← PAIR
:28  float dot = Vector3.Dot(c.transform.forward, dirToPlayer);  // ← PAIR (creature facing)
:29  float fovThreshold = Cos(cfg.VisionConeHalfAngle * Deg2Rad);// VisionConeHalfAngle 60
:31-35  if (dot < fovThreshold)
            adjustedVis *= Clamp01((dot + 1f) / (fovThreshold + 1f)) * 0.1f;  // magic 0.1 peripheral
:37  bool ignoreLoS = data.CurrentState >= VanillaAlertness.Alerted;          // ← PER-TRACK state
:39  decision.CanSee  = (hasLOS || ignoreLoS) && adjustedVis > cfg.VisionThreshold;   // 0.25
:40  decision.CanHear = adjustedNoise > cfg.HearingThreshold;                          // 0.15
:44/:46  SensingConfidence = Clamp01(adjustedVis / (VisionThreshold*2f)) or (adjustedNoise / (HearingThreshold*2f));
:51-56  gain = CanSee ? cfg.AlertGain(3.0) : CanHear ? cfg.SuspicionGain(2.5) : -cfg.DetectionDecay(0.5);
:58  data.DetectionLevel = Clamp01(data.DetectionLevel + gain * dt);          // ← PER-TRACK accumulator
:62-67  LastKnownPosition / TimeSinceSeen                                      // ← PER-TRACK
```

**Design consequence, stated plainly:** a per-(creature, player) track needs to store only `{DetectionLevel, LastKnownPosition, TimeSinceSeen, CurrentState, lastLOSresult, lastLOSframe}`. Everything else it needs is a lookup into a per-player struct of 4 floats `{vis, noise, hiding, camo}` that is computed **once per player per tick, regardless of how many creatures are looking**. Going from N creatures × 1 player to N creatures × M players costs one extra 4-float row per player, one extra `Linecast` + ~15 flops per pair — not one extra full sensing pass.

---

## 12. BUG / FRAGILITY LEDGER (sensing scope only)

1. **`cfg.GrassBonus` is dead config.** Declared `StealthConfigModel.cs:53`, bound and ServerSync'd at `SOMConfig.cs:105-107` under section `"3 - Visibility"`, key `"GrassBonus"`. `VisibilitySystem.GrassBonus` (`:72-84`) never reads it, using hardcoded `0.15f`/`0.6f`. Users turning this dial see zero effect. (There is a *second*, live entry also literally named `"GrassBonus"` in section `"5 - Hiding"` → `cfg.GrassBonus_Hiding` — confusing but legal.)
2. **Grass double-counted** — same `PlayerGrassHitCount` reduces `vis` *and* raises `hiding`, and `SensingEvaluator.cs:15` multiplies them.
3. **`"ThunderStorm"` is not detected as rain** (`AV:81796`); `"Mistlands_rain"` triggers both rain *and* mist reductions. Use `EnvSetup.m_isWet` (`AV:81641`).
4. **`RenderSettings.sun` is never assigned by Valheim** — light and shadow terms may be permanently constant. Must be measured; replace with `EnvMan.m_dirLight` / `EnvMan.GetSunDirection()` / `EnvMan.IsDaylight()` behind soft binding.
5. **Grass has no colliders in vanilla** (ClutterSystem is instanced, `AV:102873`) and `Collider.name` is the child collider's name, not the prefab's — the entire bush/grass pipeline is likely returning 0 in practice.
6. **`Mathf.Clamp01` on armor totals** destroys tier resolution (3 metal pieces already saturate).
7. **Weapons/shields/tools count as armor** — `GetEquippedItems()` returns all equipped items.
8. **`DetectWeight` uses base `m_shared.m_armor`**, ignoring `GetArmor()`'s quality and world-level scaling (`AV:58370-58373`).
9. **`ArmorMaterial.Bone/Bark/Magic/Unknown` never assigned** → `ArmorUtils.cs:73-75` default branch unreachable.
10. **`"camo"` / `"ghillie"` tags dead** — set `HasCamoTag` but have no `ArmorBiomeMap` row.
11. **`ArmorProfileSystem.Cache` never invalidated** — if another mod mutates `m_shared` at runtime (Avalor-style), the profile is frozen at first sighting.
12. **`ArmorUtils.GetTotalStealthPenalty/GetTotalArmorNoise` have no null guard** on `player` or on `GetInventory()`.
13. **`HidingSystem.VegetationMask` is dead**; `RaycastUtils.SkyVisible/SampleOcclusion/InTallGrass` are dead (`SampleOcclusion` allocates a `Vector3[6]` per call); `EnvironmentUtils.GetAmbientLight` is dead.
14. **`Heightmap.GetBiome(point)` optional-parameter call site** is a v1.0 binary-compat landmine — prefer `Heightmap.FindBiome(point)` (`AV:111648`) or reflect.
15. **`RaycastUtils.TerrainMask` hand-copies `BaseAI.m_viewBlockMask`** — correct today (`AV:4024`), silently wrong if v1.0 edits the list. Reflect the private static field with the literal as fallback.
16. **SoM's LOS ignores `ParticleMist.IsMistBlocked`** (`AV:4618`) — Mistlands mobs see through mist under SoM.
17. **SoM ignores `SEMan.ModifyStealth`** (`AV:21826`) — every stealth-affecting status effect, vanilla or modded, is inert.
18. **Debug-string arguments are built even with debug off** — ~46 allocations per AI-evaluation (§9A). Highest-value single fix in this whole surface.
19. **`CoroutineManager.PlayerBushHitCount/PlayerGrassHitCount` never prune** destroyed `Player` keys.

## 13. FILES SURVEYED (absolute paths)
```
c:\WubarrkCODING\ShadowsOfMidgard\Systems\VisibilitySystem.cs        (122 lines)
c:\WubarrkCODING\ShadowsOfMidgard\Systems\NoiseSystem.cs             ( 60)
c:\WubarrkCODING\ShadowsOfMidgard\Systems\HidingSystem.cs            ( 70)
c:\WubarrkCODING\ShadowsOfMidgard\Systems\CamoSystem.cs              (119)
c:\WubarrkCODING\ShadowsOfMidgard\Armor\ArmorProfile.cs              ( 33)
c:\WubarrkCODING\ShadowsOfMidgard\Armor\ArmorProfileSystem.cs        ( 70)
c:\WubarrkCODING\ShadowsOfMidgard\Armor\ArmorUtils.cs                ( 82)
c:\WubarrkCODING\ShadowsOfMidgard\Utils\EnvironmentUtils.cs          ( 35)
c:\WubarrkCODING\ShadowsOfMidgard\Utils\RaycastUtils.cs              ( 88)
```
Read for context (owned by other surveyors, cited only where load-bearing):
```
c:\WubarrkCODING\ShadowsOfMidgard\Systems\StealthBrain\SensingEvaluator.cs
c:\WubarrkCODING\ShadowsOfMidgard\Systems\StealthBrain.cs
c:\WubarrkCODING\ShadowsOfMidgard\Systems\AwarenessSystem.cs
c:\WubarrkCODING\ShadowsOfMidgard\Systems\CoroutineManager.cs
c:\WubarrkCODING\ShadowsOfMidgard\Systems\StealthDebugger.cs
c:\WubarrkCODING\ShadowsOfMidgard\Systems\StealthOvermind.cs
c:\WubarrkCODING\ShadowsOfMidgard\Systems\StealthExemption.cs
c:\WubarrkCODING\ShadowsOfMidgard\Systems\AIAuthority.cs
c:\WubarrkCODING\ShadowsOfMidgard\Systems\SteathUI\StealthUIController.cs
c:\WubarrkCODING\ShadowsOfMidgard\Config\SOMConfig.cs
c:\WubarrkCODING\ShadowsOfMidgard\Config\StealthConfigModel.cs
c:\WubarrkCODING\ShadowsOfMidgard\ShadowsOfMidgard.csproj   (net472, LangVersion latest)
```
Grep confirmed: **no file in `c:\WubarrkCODING\MistsofAvalor` or `c:\WubarrkCODING\DvergrAllies` references `VisibilitySystem`, `NoiseSystem`, `HidingSystem`, `CamoSystem`, `ArmorUtils`, `ArmorProfile*`, `RaycastUtils`, or `EnvironmentUtils`.** These nine files carry **no external API contract** and may be freely reshaped.