# SoM v2 Survey — UI + Config + ServerSync

## 0. Files surveyed

| Path | Lines | Role |
|---|---|---|
| `c:\WubarrkCODING\ShadowsOfMidgard\Systems\SteathUI\StealthUIRoot.cs` | 57 | Canvas + **EventSystem creation** |
| `c:\WubarrkCODING\ShadowsOfMidgard\Systems\SteathUI\StealthUIController.cs` | 93 | Per-frame driver, `GetHighestAlertness` |
| `c:\WubarrkCODING\ShadowsOfMidgard\Systems\SteathUI\StealthGem.cs` | 129 | Eye icon, drag handlers |
| `c:\WubarrkCODING\ShadowsOfMidgard\Systems\SteathUI\NoiseMeter.cs` | 106 | Fill bar, drag handlers |
| `c:\WubarrkCODING\ShadowsOfMidgard\Systems\SteathUI\SpriteLoader.cs` | 195 | PNG → Sprite, embedded + disk |
| `c:\WubarrkCODING\ShadowsOfMidgard\Config\SOMConfig.cs` | 224 | 43 binds, 34 ServerSync entries |
| `c:\WubarrkCODING\ShadowsOfMidgard\Config\StealthConfigModel.cs` | 146 | POCO snapshot, 34 fields |
| `c:\WubarrkCODING\libs-Tools\ServerSync.cs` | 1415 | vendored; byte-identical to `ShadowsOfMidgard\libs\ServerSync.cs` (which is **excluded** from compile — see csproj `Exclude="…libs\**\*.cs…"`; the compiled copy is `$(SoMLibs)\ServerSync.cs`) |

**No external mod touches `SOMConfig`, `StealthConfigModel`, or any UI type.** Verified by grep across `MistsofAvalor` and `DvergrAllies` for `SOMConfig|StealthUI|StealthConfigModel|EnableStealthUI` — zero hits. The entire config/UI layer is free to be reshaped; only `StealthExemption` is load-bearing API.

---

## 1. UI object graph

Constructed once, at BepInEx plugin `Awake` (`ShadowsOfMidgard.cs:29-30`), i.e. **before any Valheim scene UI exists**.

```
[DontDestroyOnLoad] "StealthUIRoot"                      StealthUIRoot.cs:53-54
  ├─ Canvas            renderMode=ScreenSpaceOverlay, sortingOrder=9999   :24-26
  ├─ CanvasScaler      ScaleWithScreenSize, ref 1920x1080                 :28-30
  │                    (matchWidthOrHeight left at default 0 = match width)
  ├─ GraphicRaycaster                                                     :33-34
  ├─ StealthUIRoot (MonoBehaviour)
  │
  ├── child "StealthGem"                                StealthGem.cs:19-25
  │     ├─ Image  sprite=_eyeSprite, raycastTarget=TRUE, 40x40   :31-38
  │     └─ StealthGem : IBeginDrag/IDrag/IEndDragHandler          :7
  │
  └── child "NoiseMeter"                                NoiseMeter.cs:28-34
        ├─ Image (background) raycastTarget=TRUE, 90x12           :39-44
        ├─ NoiseMeter : IBeginDrag/IDrag/IEndDragHandler          :13
        └── child "NoiseFill"
              └─ Image raycastTarget=false, stretched left-anchored :54-65

[DontDestroyOnLoad] "StealthUIController"                StealthUIController.cs:18-20
  └─ StealthUIController (MonoBehaviour)   ← the Update() driver, NOT parented to the canvas

[DontDestroyOnLoad] "EventSystem"          ← created only if none found  StealthUIRoot.cs:37-43
  ├─ EventSystem
  └─ StandaloneInputModule
```

Two `DontDestroyOnLoad` roots plus a third (`SOM_CoroutineManager`, `CoroutineManager.cs:16-18`). Three separate persistent GameObjects where one would do.

Notable structural facts:
- `StealthUIController.Init()` is called **twice** — `StealthUIRoot.cs:45` and `ShadowsOfMidgard.cs:30`. The second is a no-op via the `Instance != null` guard (`StealthUIController.cs:15-16`). Harmless, but the ordering dependency is undocumented.
- `StealthUIController.OnAwarenessUpdated` (`StealthUIController.cs:88-91`) is **dead code** — empty body, zero callers.
- `StealthUIController.Instance` is written but **never read** anywhere in the codebase.
- The gem/meter RectTransforms are bare `new GameObject()` + `Image`, so anchors default to centre `(0.5, 0.5)`. `EyePosX=80, EyePosY=80` (`SOMConfig.cs:208-209`) therefore means *80 px right / 80 px up of screen centre*, not from a corner. Saved drag positions are in that centre-relative space, so a resolution change relocates both widgets.

---

## 2. Per-frame cost

`StealthUIController.Update()` runs **every frame, forever**, on a `DontDestroyOnLoad` object — including in the main menu (it early-outs at `:25-27` on `Player.m_localPlayer` null). It is unconditional otherwise: there is no interval, no dirty check, no `Hud`-visibility check.

Per frame, with the HUD enabled:

**a) `VisibilitySystem.GetVisibility(p)` — `StealthUIController.cs:53`**
- `ShadowBonus` fires a **`Physics.Raycast`, 20 m, 5-layer mask** — `VisibilitySystem.cs:68`
- `WeatherReduction` calls `EnvironmentUtils.IsRaining()/IsSnowing()/IsMisty()`, each of which does `EnvMan.instance?.GetCurrentEnvironment()?.m_name.ToLowerInvariant()` → **3 string allocations** (`EnvironmentUtils.cs:14, 20, 26`)
- `ArmorVisibility` → `ArmorUtils.GetTotalStealthPenalty` → `player.GetInventory().GetEquippedItems()`, which in vanilla is
  ```csharp
  public List<ItemDrop.ItemData> GetEquippedItems() {
      List<ItemDrop.ItemData> list = new List<ItemDrop.ItemData>();
      foreach (ItemDrop.ItemData item in m_inventory) { if (item.m_equipped) list.Add(item); }
      return list; }
  ```
  (`assembly_valheim.decompiled.cs:57488-57499`) → **a fresh `List<>` plus a full inventory walk**
- `VisibilitySystem.cs:39-40` builds an interpolated string **at the call site**, so it is allocated *before* `StealthDebugger.LogPlayerSensing` checks `VisionDebugEnabled` (`StealthDebugger.cs:236`). **Allocated even with debug off**, plus 6 float boxes.

**b) `NoiseSystem.GetNoise(p)` — `StealthUIController.cs:54`**
- **Second** `GetEquippedItems()` `List<>` allocation (`ArmorUtils.cs:52`)
- `NoiseSystem.cs:53` — a **4th** `ToLowerInvariant()` string
- `NoiseSystem.cs:20-21` — another eagerly-built interpolated string behind a check that never sees it

**c) `GetHighestAlertness(p)` — `StealthUIController.cs:62-86`**
- `AwarenessSystem.GetAllData()` returns the `Dictionary<Character, AwarenessData>` typed as `IEnumerable<KeyValuePair<…>>` (`AwarenessSystem.cs:33-36`). `foreach` over the *interface* calls the explicit `IEnumerable<T>.GetEnumerator()` → **the struct `Dictionary.Enumerator` is boxed → one heap allocation per frame**, plus interface dispatch on every `MoveNext`/`Current`.
- Per entry: null check, `IsDead()` (ZDO read), and a **`Vector3.Distance`** — a `sqrt` — against `StealthOvermind.MAX_TRACKING_RANGE` (64 f, `StealthOvermind.cs:13`). O(all creatures ever tracked), not O(nearby).
- The dictionary is only pruned by `Character_OnDestroy_Patch.Prefix` (`Character_OnDestroy_Patch.cs:8-14`) and `ClearAll()`. It holds every non-tame, non-exempt `MonsterAI` that has come within 64 m for as long as it stays loaded. **This is the term that scales with the "many more monsters in multiplayer" goal.**

**d) uGUI churn**
- `_gem.SetVisible(true)` / `_noiseMeter.SetVisible(true)` are called unconditionally every frame (`StealthUIController.cs:50-51`) → 2× `GameObject.SetActive` native transitions.
- `StealthGem.UpdateGem` writes `_rect.localScale` every frame (`StealthGem.cs:73` and again `:102`) and `_gem.color` every frame (`:79-92`).
- `NoiseMeter.UpdateNoise` writes `_fillRect.sizeDelta` (`:74`) **and** `_fill.color` (`:78-82`) every frame.
- `Graphic.color` and `RectTransform.sizeDelta` both call `SetVerticesDirty` / `SetLayoutDirty` → **the SoM canvas rebuilds its batch every single frame, forever**, even when nothing changed. A dedicated Canvas with 3 Images that never goes clean.

**Summary of per-frame garbage from the HUD alone (debug OFF):** 1 boxed dictionary enumerator, 2 `List<ItemDrop.ItemData>`, 4 lowercased env-name strings, 2 interpolated debug strings, ~10 float boxes. Plus 1 physics raycast and 1 full canvas rebuild. Every frame.

**Bugs found in passing**
- `StealthUIController.cs:30` dereferences `SOMConfig.EnableStealthUI.Value` **without a null guard**, unlike every other `SOMConfig` read in the codebase (`ShadowsOfMidgard.cs:39`, `StealthDebugger.cs:63-70`). If binding order ever changes, this NREs every frame.
- `StealthGem.UpdateGem` at `:69-74` deliberately skips the scale update when `Engaged`, then `Pulse()` at `:99-102` overwrites `localScale` anyway. **The visibility term is silently discarded at Engaged.** Two meanings crammed into one transform.
- The canvas is `sortingOrder = 9999` and never checks `Hud.instance.m_userHidden`, map open, cinematic mode, or death screen. The gem draws over the inventory and the map.

---

## 3. EventSystem creation — what it breaks

```csharp
// StealthUIRoot.cs:36-43
if (Object.FindAnyObjectByType<EventSystem>() == null)
{
    GameObject es = new GameObject("EventSystem");
    es.AddComponent<EventSystem>();
    es.AddComponent<StandaloneInputModule>();
    DontDestroyOnLoad(es);
}
```

This runs at plugin `Awake` — during the BepInEx chainloader, **before FejdStartup's scene and its EventSystem exist**. So the guard always passes and SoM always creates one.

uGUI semantics: `EventSystem` keeps a `static List<EventSystem>`; `OnEnable` appends, and `EventSystem.current` is **element `[0]` — the first-enabled instance**. `EventSystem.Update()` opens with `if (current != this) return;`. Therefore SoM's `DontDestroyOnLoad` instance permanently owns `EventSystem.current`, and **Valheim's own scene EventSystem is inert for the rest of the session**. (`FindAnyObjectByType` also defaults to `FindObjectsInactive.Exclude`, so an existing-but-disabled EventSystem would be missed too.)

Valheim calls `EventSystem.current` **unguarded** in at least these places, all of which now route to SoM's object rather than the scene's:

| `assembly_valheim.decompiled.cs` | What it does |
|---|---|
| `:34324` | Chat window: `SetSelectedGameObject(null)` on close |
| `:43275`, `:43771`, `:43976`, `:44017` | `KeyButton` / slider / `KeyUI` / toggle gamepad focus |
| `:45935` | `SelectEntry` coroutine — menu default selection |
| `:53637` | `SetSelectedGameObject(m_doneButton)` |
| `:54329`, `:54374` | list selection |
| `:84086` | server options done-button focus |
| `:84694`, `:84712` | FejdStartup default menu button for gamepad |
| `:84781` | `if (ZInput.GetMouseButton(0) && !EventSystem.current.IsPointerOverGameObject())` — character rotation |
| `:137984`, `:138290`, `:138750`, `:138776`, `:138927-928` | Settings + **key rebinding dialog** |
| `:144473` | (this one *does* null-check) |

Concrete breakage:

1. **Gamepad navigation and the key-rebinding dialog.** All of the above depend on the EventSystem that the scene authored — its input module, its `firstSelectedGameObject`, its navigation config. SoM's replacement is a bare `EventSystem` + stock `StandaloneInputModule` with none of that.
2. **`StandaloneInputModule` polls legacy `Input.*`, not `ZInput`.** Valheim ships its own input module wired through `ZInput`. Injecting a stock `StandaloneInputModule` as *the live one* means Unity's default axis names (`"Horizontal"`, `"Vertical"`, `"Submit"`, `"Cancel"`) are queried against Valheim's InputManager asset. Any missing axis throws `ArgumentException: Input Axis … is not setup` from `Input.GetAxisRaw` **every frame**. This is the classic BepInEx-mod failure signature.
3. **Click-through false positives.** `GraphicRaycaster` (`StealthUIRoot.cs:33-34`) at `sortingOrder = 9999`, with `raycastTarget = true` on the 40×40 gem (`StealthGem.cs:36`) and the 90×12 meter background (`NoiseMeter.cs:41`). `EventSystem.current.IsPointerOverGameObject()` now returns **true** whenever the cursor sits over those rects — including on the character-select screen (`:84781`), where the widgets are invisible but *still active*, because they are only deactivated when `EnableStealthUI` is false and the whole tree is `DontDestroyOnLoad`. Any mod (or vanilla code) using `IsPointerOverGameObject()` to suppress world-click-through gets a phantom dead zone.
4. **Cross-mod direction that matters.** Another mod doing the same "create one if missing" dance will find SoM's and skip — benign. The damaging direction is any mod that calls `EventSystem.current.SetSelectedGameObject(...)` or inspects `currentInputModule` expecting the game's.

**Correct shape for v2:**
- **Never create an `EventSystem`.** Build the canvas lazily on first `Player.m_localPlayer` (or a `Hud.Awake` postfix), by which point Valheim's EventSystem exists.
- Parent the canvas under Valheim's own HUD root instead of standing up a private `DontDestroyOnLoad` Canvas + GraphicRaycaster, so sorting, scaling and hide-HUD all come for free.
- Keep `raycastTarget = false` at all times **except** while an explicit "reposition HUD" mode is active; drive drag from `Input.mousePosition` deltas in `Update()` during that mode rather than from `IDragHandler`, and the whole EventSystem dependency disappears.
- If drag handlers are kept, guard: `if (EventSystem.current == null) { /* no drag, no creation */ }`.

---

## 4. Sprite loading path

Call chain: `StealthGem.Build` → `LoadSprites` (`StealthGem.cs:46-60`) → `SpriteLoader.LoadEmbeddedPNG("eye.png")` → on null → `SpriteLoader.LoadSprite("eye.png")`.

**Embedded path** (`SpriteLoader.cs:78-141`):
- `GetManifestResourceStream("eye.png")` — **always misses.** `ShadowsOfMidgard.csproj` has `<EmbeddedResource Include="Assets\eye.png" />` with `RootNamespace = ShadowsOfMidgard`, so the manifest name is `ShadowsOfMidgard.Assets.eye.png`.
- Falls through to the `name.EndsWith(resourceName, OrdinalIgnoreCase)` scan over `GetManifestResourceNames()` (`:115-122`) — **this is the only path that ever succeeds**, and it does a full manifest scan on every cache miss. Should just use `"ShadowsOfMidgard.Assets." + fileName`, with the scan as fallback.
- Source asset: `ShadowsOfMidgard\Assets\eye.png`, 4355 bytes, the only file in `Assets\`.

**Disk path** (`SpriteLoader.cs:19-76`) — effectively dead and actively harmful:
- `Path.Combine(Paths.PluginPath, "ShadowsOfMidgard", "Assets", fileName)` (`:24`) **hardcodes the plugin folder name.** The shipped package (`HexiumDistrib\plugins\ShadowsOfMidgard.dll` + `manifest.json`) contains **no `Assets` folder at all**, and under a Gale/Hexium profile the plugin lands in an author-prefixed directory. This path essentially never resolves on a real install. Derive it from `Path.GetDirectoryName(typeof(SpriteLoader).Assembly.Location)`.
- **`SpriteLoader.cs:58-67` writes the embedded PNG out to disk** — `Directory.CreateDirectory` + `File.WriteAllBytes` into the user's `plugins` tree, on first sprite load, silently. Needs write permission, can fail on junctioned/read-only profile dirs, and serves no purpose ("for compatibility" with nothing). **Delete this block.**

**Decode** (`SpriteLoader.cs:146-193`):
- Reflects `UnityEngine.ImageConversion.LoadImage` even though `ShadowsOfMidgard.csproj` already `<Reference Include="UnityEngine.ImageConversionModule">`. The `MethodInfo` is cached (`_loadImageMethod`), so cost is one-time, but this is reflection where a direct call compiles.
- `new Texture2D(2, 2, RGBA32, false)` then `LoadImage` — no `hideFlags`, no `Apply` control, never released. One 4 KB texture, so not a real leak, but it is unmanaged and unowned.
- `Cache` (`SpriteLoader.cs:12`) is keyed on the short name and shared by both entry points, so `"eye.png"` is only decoded once regardless of which path wins. Good.

**Failure behaviour:** if both paths fail, `_gem.sprite` is left `null` (`StealthGem.cs:34`). A uGUI `Image` with a null sprite **still draws** — a solid white 40×40 quad, tinted by the alertness colour. It logs an error (`:58`) and carries on. It should disable itself.

---

## 5. The 34× config-binding boilerplate

Exact counts in `SOMConfig.cs`:
- `file.Bind(` — **43**
- `configSync.AddConfigEntry(` — **34**
- `SettingChanged +=` — **34**
- `ConfigDescription` / `AcceptableValueRange` — **0**

The repeated unit is 3 lines (`SOMConfig.cs:47-49` and 33 more), i.e. **102 lines of the 224-line file**:

```csharp
var visionThresholdEntry = configSync.AddConfigEntry(file.Bind("2 - StealthBrain", "VisionThreshold", 0.25f, "Minimum visibility to be seen (0-1)."));
cfg.VisionThreshold = Clamp(visionThresholdEntry.Value, 0f, 1f);
visionThresholdEntry.SourceConfig.SettingChanged += (_, _) => cfg.VisionThreshold = Clamp(visionThresholdEntry.Value, 0f, 1f);
```

The min/max literals are **duplicated on every one of those pairs** (`0f, 1f` written twice on lines 65 and 66; `0f, 5f` twice on 73/74; etc.) — 68 opportunities to typo a bound and have the clamp disagree with itself between the initial read and the change handler. Nothing enforces they match.

Other defects this shape produces:
- **Zero `ConfigDescription`s.** Every entry is a bare `Bind(section, key, default, "string")`, so ConfigurationManager renders all 43 as free-text boxes with no sliders and no enforced range. The clamp exists only inside SoM's POCO.
- Section ordering relies on the `"1 - "`, `"2 - "` string prefixes rather than `ConfigurationManagerAttributes.Order`.
- `GrassBonus` is bound twice — `"3 - Visibility"/"GrassBonus"` (`:105`) and `"5 - Hiding"/"GrassBonus"` (`:147`). Distinct sections so both BepInEx and ServerSync's `section+key` packing are fine, but the local variable names (`grassBonus_VisEntry`, `grassBonus_HidingEntry`) are the only thing keeping them straight.
- No entry records *whether* it is synced anywhere queryable; sync-ness is implied by whether the author remembered to wrap the `Bind` call.

### Concrete less-repetitive shape

A single binder that owns bind + sync + clamp + initial push + change push:

```csharp
// Config/Binder.cs
internal static class Binder
{
    internal static ConfigEntry<float> F(
        ConfigFile file, ConfigSync sync, string section, string key,
        float def, float min, float max, string desc, Action<float> apply)
    {
        var e = file.Bind(section, key, def,
            new ConfigDescription(desc, new AcceptableValueRange<float>(min, max)));
        if (sync != null) sync.AddConfigEntry(e);          // sync==null => strictly local
        void Push() => apply(Sanitize(e.Value, min, max));
        e.SettingChanged += (_, _) => Push();
        Push();                                             // initial value, never forgotten
        return e;
    }

    internal static ConfigEntry<bool> B(
        ConfigFile file, ConfigSync sync, string section, string key,
        bool def, string desc, Action<bool> apply) { /* same shape, no clamp */ }

    private static float Sanitize(float v, float min, float max)
        => (float.IsNaN(v) || float.IsInfinity(v)) ? min : Mathf.Clamp(v, min, max);
}
```

Call sites collapse to one line, with each bound written exactly once:

```csharp
Binder.F(file, Sync, "2 - StealthBrain", "VisionThreshold", 0.25f, 0f, 1f,
         "Minimum visibility to be seen (0-1).", v => cfg.VisionThreshold = v);
Binder.B(file, Sync, "1 - Systems", "EnableVisibilitySystem", true,
         "Enable visibility calculations.", v => cfg.EnableVisibilitySystem = v);
Binder.F(file, null, "UI", "EyePosX", 80f, -4000f, 4000f,
         "Horizontal position of the stealth gem.", v => UI.EyePosX = v);
```

102 lines → 34, one clamp implementation, one initial-push implementation, and `sync: null` at the call site becomes the *explicit, greppable* declaration of "this one is local". The 34 closures are allocated once at startup — irrelevant. `SOMConfig.Clamp` (`:217-222`) folds into `Sanitize`.

---

## 6. Server-synced vs local

The current *selection* is right; what is missing is the lock. Classification:

**MUST be server-synced** — these change simulation outcomes, and because the **owning client** simulates (`AIAuthority.IsAuthoritative` → `nview.IsOwner()`, `AIAuthority.cs:18`), two clients with different values will drive the *same* creature differently depending on who owns it. Any per-player detection state replicated through ZDO then means different things on different peers.

All 34 currently-synced entries: `1 - Systems` (4), `2 - StealthBrain` (8), `3 - Visibility` (7), `4 - Noise` (4), `5 - Hiding` (3), `6 - Camo` (2), `7 - Awareness` (3), `8 - Detection Ranges` (3). `SOMConfig.cs:47-188`.

**MUST stay local** — currently correct (all bound without `AddConfigEntry`):
- `UI/EnableStealthUI`, `UI/EyePosX`, `UI/EyePosY`, `UI/NoisePosX`, `UI/NoisePosY` (`SOMConfig.cs:207-211`) — per-player screen layout. Syncing these would drag other players' widgets around.
- `9 - Debug/EnableVisionDebug`, `EnableAIDebug`, `DebugLogInterval` (`SOMConfig.cs:191-196`) — per-client log volume.
- `9 - Debug/ShowPatchSaga` (`:193`).

**New in v2, and which side each belongs on:**

| Setting (currently hardcoded) | Where | Side |
|---|---|---|
| `StealthOvermind.MAX_TRACKING_RANGE = 64f` (`StealthOvermind.cs:13`) | **split** — a creature beyond it is never evaluated, which is *gameplay*; but it is also the main cost lever | sync the gameplay `EvaluationRange`; keep a separate local `PerfRangeCap` that only ever *lowers* it |
| `MAX_EVALS_PER_FRAME = 5` (`:15`) | pure frame budget | **local** |
| tier boundaries `25f` / `50f`, strides `2` / `6` / `20` (`:101-106`) | frame budget | **local** |
| `MAX_EVAL_TIMESTEP = 0.5f` (`:18`) | affects gain/decay integration → gameplay | **sync** |
| `StealthBrain.NEARBY_ALLY_RADIUS = 30f`, `ALLY_CALL_COOLDOWN = 3f` (`StealthBrain.cs:8-9`) — **duplicated verbatim as `CoroutineManager.NEARBY_ALLY_RADIUS` (`CoroutineManager.cs:39`)**; de-duplicate | gameplay | **sync** |
| `MAX_DECISION_AGE_FRAMES = 60` (`StealthBrain.cs:166`) | cache staleness | **local** |
| Per-creature AI profile / sense-range override table (the v2 brief) | gameplay, and it is a *table*, not a scalar | **`CustomSyncedValue<string>`**, not a `ConfigEntry` |

On that last one: `ConfigEntry` cannot express a table. ServerSync's `CustomSyncedValue<T>` (`ServerSync.cs:91-115`) is the native mechanism — carry YAML/JSON in a `CustomSyncedValue<string>`, and ServerSync fragments (250 KB slices, `:323-324`, `:664-690`) and Deflate-compresses (`:325`, `:400-413`) it for free. Give it `priority > 0` so it lands before the scalars — `AddCustomValue` re-sorts by descending `Priority` (`ServerSync.cs:226`).

---

## 7. Correct ServerSync setup

Current (`SOMConfig.cs:8-13`):

```csharp
private static readonly ServerSync.ConfigSync configSync = new ServerSync.ConfigSync(ShadowsOfMidgard.ModGUID)
{ DisplayName = ShadowsOfMidgard.ModName, CurrentVersion = ShadowsOfMidgard.ModVersion, ModRequired = false };
```

### Defect 1 — no locking entry. This is the important one.

`AddLockingConfigEntry` is **never called**. Therefore:

```csharp
public bool IsLocked => (forceConfigLocking ?? lockedConfig != null && …) && !lockExempt;   // ServerSync.cs:135-139
```
`forceConfigLocking` is null and `lockedConfig` is null → **`IsLocked` is permanently `false`**. Consequences:
- The server's admin gate never runs: `if (isServer && IsLocked && …currentRpc…GetHostName() is { } client)` → `if (!exempt) return false;` (`ServerSync.cs:347-356`) is dead.
- `AddConfigEntry` installs `configEntry.SettingChanged += … Broadcast(ZRoutedRpc.Everybody, configEntry)` (`ServerSync.cs:192-198`). With no lock, **any non-admin client that edits its own TOML or uses ConfigurationManager broadcasts that value to every other player on the server.** A client can set `MaxVisualRange` to 5 for the whole server.
- `serverLockedSettingChanged` (`ServerSync.cs:585-590`) never marks anything `ReadOnly`, so ConfigurationManager shows every synced field as freely editable to everyone.

Fix — bind the locking entry **first**, before any synced entry, so `ReadOnly` flags are already correct on first render:

```csharp
ServerConfigLocked = file.Bind("0 - General", "Lock Configuration", true,
    new ConfigDescription("If on, the server's values overwrite every client's and only server admins may change them."));
Sync.AddLockingConfigEntry(ServerConfigLocked);   // T : IConvertible; throws if called twice (ServerSync.cs:205-216)
```

### Defect 2 — `ModRequired = false`

With `false`:
- `MinimumRequiredVersion` degrades to `"0.0.0"` (`ServerSync.cs:1142-1146`)
- the client **skips sending its version entirely**: `if (!check.ModRequired && !__instance.IsServer()) continue;` (`ServerSync.cs:1351-1354`)
- `IsVersionOk()` returns `!ModRequired` = `true` when nothing was received (`ServerSync.cs:1211-1214`)

→ a **vanilla client can join**. For SoM this is not cosmetic: the owning client simulates, so a vanilla client that owns a mob runs *vanilla* perception on it while every SoM client assumes SoM rules for the same creature. Set `ModRequired = true` unless mixed-mode is a deliberate, specified behaviour — in which case it needs a design, not a flag default.

### Defect 3 — three disagreeing version numbers

- `ShadowsOfMidgard.cs:13` — `public const string ModVersion = "2.0.0";` (hardcoded, feeds `[BepInPlugin]` **and** `ConfigSync.CurrentVersion`)
- `ShadowsOfMidgard\version.txt` — currently **`1.9.1`**, drives `AssemblyVersion`/`FileVersion` and the generated `SoMBuild.Version` const
- `ShadowsOfMidgard.csproj`'s own comment claims *"The value is surfaced to C# as the generated const SoMBuild.Version, which [BepInPlugin] consumes"* — **it does not.** Nothing references `SoMBuild.Version`.

The ServerSync handshake therefore advertises `2.0.0` while the DLL reports `1.9.1.0`. Pick one: either `public const string ModVersion = SoMBuild.Version;` or delete the generator target. Separately: **`MinimumRequiredVersion` must not be auto-ticked from `version.txt`** — the csproj bumps the patch on every build, and a min-version tied to that hard-kicks every client on every rebuild. Bump it by hand only on wire/behaviour breaks.

### Defect 4 — no failure floor

`SOMConfig.Init` (`:33-37`) runs `LoadUIConfig` then `LoadGameplayConfig`, both inside `ShadowsOfMidgard.Awake`'s single try/catch (`ShadowsOfMidgard.cs:23-53`). `Active` is assigned only on the **last line** of `LoadGameplayConfig` (`:198`). Any throw in between leaves `Active == null`.

And `AddConfigEntry` contains an undefended hard reflection dependency on a BepInEx-internal compiler-generated field name:

```csharp
AccessTools.DeclaredField(typeof(ConfigDescription), "<Tags>k__BackingField").SetValue(…)   // ServerSync.cs:191
```

If BepInEx ever changes that, `DeclaredField` returns null → `NullReferenceException` on the **first** synced bind → `Active` stays null. Then:

- `VisibilitySystem.GetVisibility` returns **`1f`** when `cfg == null` (`VisibilitySystem.cs:24-25`) — *maximally visible*
- `NoiseSystem.GetNoise` returns `0f` (`NoiseSystem.cs:10-11`)
- the UI never comes up, so there is no indication anything is wrong

**A config failure currently means "every creature sees you perfectly, forever, silently."** The failure floor must be vanilla behaviour: assign `Active = new StealthConfigModel()` **before** binding anything, wrap each bind so one bad entry cannot take down the rest, and have the systems' null-guard fall back to *disabled*, not to *worst case*.

### Reference setup

```csharp
private static readonly ConfigSync Sync = new(ShadowsOfMidgard.ModGUID)
{
    DisplayName            = ShadowsOfMidgard.ModName,
    CurrentVersion         = ShadowsOfMidgard.ModVersion,   // == SoMBuild.Version
    MinimumRequiredVersion = "2.0.0",                       // hand-bumped only
    ModRequired            = true,
};

public static void Init(ConfigFile file)
{
    Active = new StealthConfigModel();                      // defaults land first
    ServerConfigLocked = file.Bind("0 - General", "Lock Configuration", true, …);
    Sync.AddLockingConfigEntry(ServerConfigLocked);         // before any synced entry
    BindLocal(file);                                        // UI + debug, sync: null
    BindSynced(file, Active);                               // 34 one-liners via Binder
    CreatureProfiles = new CustomSyncedValue<string>(Sync, "creatureprofiles", "", priority: 10);
}
```

What you get for free once a `ConfigSync` exists — no extra code:
- `new ConfigSync(name)` constructs `new VersionCheck(this)` (`ServerSync.cs:183`), whose static ctor schedules `PatchServerSync` onto the main thread via `ThreadingHelper.StartSyncInvoke` (`:1174-1177`)
- `ZNet.OnNewConnection` prefix exchanges `(name, minRequired, current)` both ways (`:1330-1364`)
- `ZNet.RPC_PeerInfo` prefix disconnects the client (server side) or `Game.instance.Logout()` + `ConnectionStatus.ErrorVersion` (client side) on mismatch (`:1305-1328`, `:1251-1259`)
- `FejdStartup.ShowConnectError` postfix appends a readable reason and resizes the panel (`:1379-1413`)
- `ZNet.Shutdown` postfix restores every local value and resets `IsSourceOfTruth = true`, `InitialSyncDone = false` (`:559-573`)
- `ConfigEntryBase.GetSerializedValue` / `SetSerializedValue` prefixes keep the **file** holding the player's own value while the **runtime** holds the server's (`:948-985`)
- Server side: `InitialSyncDone = true` immediately, plus a 30 s `WatchAdminListChanges` coroutine pushing `Internal/lockexempt` to admins (`:245-305`)

**Architectural caveat for the dossier:** ServerSync's authority is the *server*; SoM's simulation authority is the *owning client* (`AIAuthority.cs:18`). These are two different authorities. ServerSync guarantees every peer agrees on the *numbers*. It guarantees nothing about the *simulation*. Do not let "we added ServerSync" be mistaken for "multiplayer is consistent."

Minor, but real: `StealthGem.OnEndDrag` (`StealthGem.cs:126`) and `NoiseMeter.OnEndDrag` (`NoiseMeter.cs:102`) call `ConfigFile.Save()` on **every mouse-up** — a full 43-entry TOML rewrite on the main thread. Debounce, or set `SaveOnConfigSet` and drop the explicit call.

---

## 8. What the HUD must show to be correct in multiplayer

### Current behaviour

```csharp
// StealthUIController.cs:62-86
private VanillaAlertness GetHighestAlertness(Player p)
{
    VanillaAlertness highest = VanillaAlertness.Unaware;
    foreach (var kvp in AwarenessSystem.GetAllData())
    {
        Character c = kvp.Key;
        if (c == null || c.IsDead()) continue;
        if (Vector3.Distance(c.transform.position, playerPos) > StealthOvermind.MAX_TRACKING_RANGE) continue;
        if (kvp.Value != null && kvp.Value.CurrentState > highest) highest = kvp.Value.CurrentState;
    }
    return highest;
}
```

`AwarenessData.CurrentState` is **one scalar per creature** (`UnifiedStealthTypes.cs:72`), written by `StealthBrain.Evaluate(ai, p, dt)` against whichever `Player` was passed — and the Overmind only ever passes `Player.m_localPlayer` (`StealthOvermind.cs:39`, `:119`). So on any given client every state in the dictionary happens to be "relative to me," which is why the gem *looks* right in single-player.

### Why it is wrong in multiplayer

1. **Coverage hole.** The Overmind only evaluates creatures this client **owns** — `if (!AIAuthority.IsAuthoritative(monster)) continue;` (`StealthOvermind.cs:75`). A creature owned by player B is *never inserted into A's `AwarenessSystem.Data`* → it cannot appear in A's `GetHighestAlertness` at all. **A's gem stays blue while B's owned draugr charges A.** In a group this is the majority of nearby creatures.
2. **False positive from the mirror case.** A creature that *A* owns but which is hunting *B* still shows on A's HUD as "you are detected," because `CurrentState` was computed against A and only A. With per-player tracks, `CurrentState` becomes an aggregate over all targets and the "who is this about" information is destroyed at exactly the layer the HUD reads.
3. **`kvp.Value.CurrentState` has no meaning once tracks land.** The moment a creature keeps a record per nearby player, "the creature's state" is a summary, not an answer to "does it see *me*."
4. **The distance filter is the wrong predicate.** A creature 60 m away that has never sensed anyone still passes the 64 m gate and would light the gem if anything else set its state; a creature at 70 m walking to your last-known-position — the exact thing the search behaviour exists to model — is filtered out. Filter on *track existence and recency*, not on Euclidean distance.

### What it must show

- **Detection about *me*, from tracks about *me*.** `max over creatures c of c.Track(localPlayer).State / .DetectionLevel`, where a track only exists if that creature has actually evaluated against the local player. Never fall back to a creature-global scalar.
- **Coverage for creatures this client does not own.** Per the brief, cross-peer agreement rides on replicated ZDO state. The owner writes a compact per-target detection summary into the creature's ZDO — e.g. `SoM_DetTarget` (ZDOID of highest-detection target) + `SoM_DetLevel` (float) + `SoM_DetState` (byte), or a small packed blob for the top N targets — and every non-owning client reads it and matches the ZDOID against its own player. **Without this the HUD is structurally blind to every creature it does not own**, which in a group is most of them. This is the single reason the HUD cannot be fixed inside the UI layer alone.
- **Split the two meanings currently crammed into one Image.**
  - *Exposure* — `VisibilitySystem.GetVisibility(p)` — is genuinely player-local, identical for every observer, needs no network. It is the only thing on the HUD that is already MP-correct. This is the gem's **scale**.
  - *Threat* — per-creature-per-me, must come from tracks. This is the gem's **colour**. And note the current code discards the scale term entirely at `Engaged` (`StealthGem.cs:69-74` skips it, `:99-102` overwrites it), so the two are already fighting.
- **Group stealth must be legible — this is the acceptance test.** The decided architecture supports "one player sneaks while another tanks." A sneaking player standing two metres from three mobs fighting his party-mate must see a **quiet, blue, small** gem. `GetHighestAlertness` returns `Engaged` in exactly that scenario. If that one case reads correctly, the per-player-track redesign is working end to end; if it does not, nothing else about it is verified.
- **Suggested shape**, so the HUD stops owning any query logic:
  ```csharp
  struct LocalThreat { VanillaAlertness State; float Detection; int SourceCount; bool AnyLos; }
  // owner-side tracks ∪ ZDO-read tracks for non-owned creatures
  LocalThreat q = StealthQuery.ForPlayer(Player.m_localPlayer);
  ```
  Published **by the Overmind at the end of each pass**, cached, and read by `Update()`, which then does nothing but `Mathf.Lerp` toward cached numbers and only writes `color`/`localScale` when the target actually changed.

That one change removes from the render loop: the per-frame dictionary walk, the boxed enumerator, both `GetEquippedItems()` allocations, the 20 m shadow raycast, four `ToLowerInvariant()` strings, two eager debug-string allocations, and the unconditional per-frame canvas rebuild — while being the only version of the HUD that tells the truth in a group.