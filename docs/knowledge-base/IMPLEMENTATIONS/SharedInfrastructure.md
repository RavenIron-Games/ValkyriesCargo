# Shared Infrastructure (`libs-Tools`) — Technical Report

## Overview

`libs-Tools` (at `c:/WubarrkCODING/libs-Tools`) is the author's central, cross-project **shared dependency and reference-material folder** for all the Valheim BepInEx/Harmony/Jotunn mod projects (BlightedHeart, DeathBeforeDishonor, Fatty, Njord, ShadowsOfMidgard, TheEye, DvergrAllies, WingsoftheValkyrie, MistsofAvalor, BarrkUI, etc.). It serves two distinct purposes bundled in one folder: (1) a **compile-time reference-assembly cache** — vendored copies of the real game/engine/framework DLLs (Valheim, Unity, BepInEx, Harmony, Jotunn) that every project's `.csproj` points `HintPath` at via `..\libs-Tools\*.dll`, plus decompiled/publicized source dumps of those assemblies for IDE lookups; and (2) a **grab-bag of hand-vendored C# utility/pattern source files** (`ServerSync.cs`, `DeepReflectionWalker.cs`, and — as of 2026-08-04 — the `SharedUI/` folder) that get literally copied or `<Compile Include>`-referenced into individual mod projects because BepInEx mods are typically single-assembly and don't support NuGet-style shared libraries well.

**Standing rule for every new project (added 2026-08-04, for `BarrkUI`): shared/reusable source lives here, not in the new project.** Anything that isn't that one mod's own business logic — a config-sync library, a UI theme, an input-handling gotcha fix, a soft-dependency probe — belongs under `libs-Tools` (its own root, or a topic subfolder like `SharedUI/`) and gets pulled in via `<Compile Include>`. A new mod's own folder should only contain code that is actually specific to that mod. When a pattern already exists project-locally in an older mod and a new mod needs it too, that is the moment to promote it here (parameterizing away whatever made it project-specific) rather than copy-pasting the old project's copy — see `SharedUI/` below for a worked example of exactly that promotion.

---

### ServerSync / ConfigSync (Config Synchronization Framework)

- **Purpose:** The well-known community-standard ("Community ServerSync") pattern for keeping BepInEx `ConfigEntry<T>` values synchronized between a Valheim dedicated/hosted server and connecting clients, including a "lock configuration" admin feature and a mod-version handshake so mismatched client/server mod versions can be rejected or warned about. This is the config-sync backbone used by nearly every mod in the codebase (Fatty, Njord, DeathBeforeDishonor, ShadowsOfMidgard, DvergrAllies, WingsoftheValkyrie).
- **Key files:**
  - `libs-Tools/ServerSync.cs` (root, canonical copy — 1416 lines)
  - `libs-Tools/CSharp/ServerSync.cs` (byte-identical duplicate)
  - `libs-Tools/ServerSync.dll` (49 KB precompiled build of the same source, for projects that want to reference it as a binary instead of compiling the source)
  - Vendored per-project copies confirmed byte-identical: `DeathBeforeDishonor/System/ServerSync.cs`, `Fatty/Libs/ServerSync.cs`, `ShadowsOfMidgard/libs/ServerSync.cs`
- **Architecture:**
  - `namespace ServerSync` contains:
    - `ConfigSync` — the main orchestrator. One instance per mod (`new ConfigSync("com.author.modid")`). Tracks all bound `ConfigEntry<T>` (wrapped as `SyncedConfigEntry<T>`) and arbitrary `CustomSyncedValue<T>` (non-BepInEx-config values that still need syncing).
    - Uses **Harmony patches** (declared as private nested classes, auto-patched via a static constructor running `Harmony("org.bepinex.helpers.ServerSync").PatchAll(...)`) on:
      - `ZNet.Awake` — registers a `"{Name} ConfigSync"` routed RPC and starts an `AdminListChanges` watcher coroutine (server only).
      - `ZNet.OnNewConnection` — client registers its RPC handler for receiving server config pushes.
      - `ZNet.RPC_PeerInfo` (via a `BufferingSocket` socket-wrapper trick) — server buffers all outgoing traffic until it has pushed the config package to a freshly connecting peer, guaranteeing configs land before gameplay data.
      - `ConfigEntryBase.GetSerializedValue` / `SetSerializedValue` — prevented from persisting server-pushed values into the client's local `.cfg` file.
      - `ZNet.Shutdown` — resets all configs back to the client's local values when leaving a server.
    - Values are serialized into a `ZPackage`, with support for **fragmentation** (payloads > 250,000 bytes split across multiple RPCs) and **DEFLATE compression** (payloads > 10,000 bytes).
    - **Locking:** `AddLockingConfigEntry<T>(ConfigEntry<bool> lockingConfig)` designates one config entry as the master switch. When locked, only server admins can change synced settings; other clients' local edits are silently overridden and their config UI becomes read-only (`ReadOnly` tag consumed by BepInEx ConfigurationManager).
    - `VersionCheck` (separate `[HarmonyPatch]` class, auto-instantiated by every `ConfigSync`) exchanges `CurrentVersion`/`MinimumRequiredVersion` strings during `ZNet.RPC_PeerInfo` and disconnects mismatched clients/servers with a formatted in-game error message.
- **How to implement/set up (step-by-step recipe):**
  1. Reference the required assemblies in the mod's `.csproj` (Harmony, BepInEx, Unity core, assembly_valheim) — already present in every project via `..\libs-Tools\*.dll`.
  2. Either **(a)** add `<Compile Include="..\libs-Tools\ServerSync.cs" />` directly to the `.csproj` (used by `Njord`), or **(b)** copy `ServerSync.cs` into a local `System/`/`Libs/` folder inside the project (used by `DeathBeforeDishonor`, `Fatty`, `ShadowsOfMidgard`), or **(c)** reference the prebuilt `ServerSync.dll` (used by `DeathBeforeDishonor/temp_resurrection/Resurrection` and `PAUSED/RuneboundRest`).
  3. In the plugin's config class, create one `ConfigSync` instance: `new ConfigSync("com.author.modid") { DisplayName = "...", CurrentVersion = "1.0.0", MinimumRequiredVersion = "1.0.0" }`.
  4. Bind a `ConfigEntry<bool>` for "Lock Configuration" and register it via `configSync.AddLockingConfigEntry(lockEntry)`.
  5. For every other config value: `configFile.Bind(...)` as normal, then wrap with `configSync.AddConfigEntry(configEntry)`. Optionally set `.SynchronizedConfig = false` on the returned `SyncedConfigEntry<T>` for client-only/cosmetic settings.
  6. No further plumbing needed — Harmony auto-patches `ZNet` on load; sync happens transparently on connect and on `SettingChanged`.
- **Reusable pattern/snippet:**
  ```csharp
  private ConfigSync _configSync = new ConfigSync("com.wubarrk.modid")
  { DisplayName = "My Mod", CurrentVersion = "1.0.0", MinimumRequiredVersion = "1.0.0" };

  private ConfigEntry<T> BindSync<T>(string group, string name, T value, string desc, bool sync = true)
  {
      var entry = _configFile.Bind(group, name, value, desc);
      _configSync.AddConfigEntry(entry).SynchronizedConfig = sync;
      return entry;
  }
  var lockEntry = _configFile.Bind("1 - General", "Lock Configuration", true, "...");
  _configSync.AddLockingConfigEntry(lockEntry);
  ```

---

### DeepReflectionWalker (Runtime Object-Graph Debug Dumper)

- **Purpose:** A defensive, depth/size-bounded reflection walker that recursively traverses an arbitrary live object's instance fields (and, if `IEnumerable`, its elements) and invokes a callback for every reachable object — used for debugging/dumping the runtime state of Unity/Valheim objects without writing bespoke inspection code each time.
- **Key files:** `libs-Tools/DeepReflectionWalker.cs` (root) and byte-identical copies at `libs-Tools/CSharp/DeepReflectionWalker.cs` and `TheEye/DeepReflectionWalker.cs`. No active call site was found anywhere in the codebase — it is currently vendored/staged but not wired into any plugin yet.
- **Architecture:**
  - Static class, single public entry point: `public static void Walk(object root, Action<string, object> onField)`.
  - Maintains a `HashSet<object>` of already-visited objects keyed by **reference identity** (custom `ReferenceEqualityComparer` using `RuntimeHelpers.GetHashCode`), preventing infinite loops on cyclic graphs.
  - Hard safety caps: `MAX_DEPTH = 32` recursion levels and `MAX_ITEMS = 500000` total visited objects, both silently truncating rather than throwing.
  - `ForbiddenTypePrefixes` — a deny-list of ~35 namespace/type prefixes (`PlayFab`, `Steamworks`, `UnityEngine.Networking`, most of `System.Runtime.*`, compiler-generated closures `<>`/`c__DisplayClass`/`d__`, `PrivateImplementationDetails`) that are skipped outright.
  - For each object: calls `onField(path, obj)`, then reflects `GetFields(Instance|Public|NonPublic)` (excluding static and pointer fields) and recurses into each non-null field value, and separately iterates `IEnumerable` (unless a `string`) recursing into each element with an indexed path.
  - Field access failures are swallowed silently.
- **How to implement/set up (step-by-step recipe):**
  1. Copy `DeepReflectionWalker.cs` into the target project (it's a plain static class with only `System`/`UnityEngine` dependencies).
  2. Call `DeepReflectionWalker.Walk(someObject, (path, value) => { /* log or collect */ })` from a debug console command, F-key hotkey handler, or gated `Update()`.
  3. Typically pair with a `StringBuilder`/`File.WriteAllText` sink to dump to a text file for offline inspection.
- **Reusable pattern/snippet:**
  ```csharp
  var sb = new StringBuilder();
  DeepReflectionWalker.Walk(playerInstance, (path, value) =>
      sb.AppendLine($"{path} = {value?.GetType().Name}: {value}"));
  File.WriteAllText("player_dump.txt", sb.ToString());
  ```

---

### SE_Rested.cs (Decompiled Status-Effect Reference Example)

- **Purpose:** Not authored code — a **decompiled dump of Valheim's actual built-in `SE_Rested` status effect** (the "Rested" buff from sitting near comfort items/fires), kept purely as a **reference template** for how to author a custom time-limited, environment-driven status effect on top of the game's `SE_Stats`/`StatusEffect` base classes.
- **Key files:** `libs-Tools/SE_Rested.cs` (100 lines).
- **Architecture (what it demonstrates):**
  - `class SE_Rested : SE_Stats` — subclassing pattern for stat-affecting status effects.
  - Lifecycle overrides: `Setup(Character character)` (fires once on apply), `UpdateStatusEffect(float dt)` (per-tick), `ResetTime()` (re-invoked on refresh).
  - `UpdateTTL()` — computes total duration as `m_baseTTL + (comfortLevel - 1) * m_TTLPerComfortLevel`, and only extends (never shortens) the current remaining time.
  - `CalculateComfortLevel(Player)` — scans nearby `Piece` objects via `Piece.GetAllComfortPiecesInRadius(point, 10f, buffer)`, sorts by `m_comfortGroup` then descending comfort value then name, sums contributions while de-duplicating same-group/same-name pieces — the exact algorithm for vanilla's "comfort" mechanic.
  - Uses a static reusable `List<Piece> s_tempPieces` scratch buffer to avoid GC allocation on repeated scans.
- **How to implement/set up:** Treat as read-only reference. To build a custom status effect: (1) subclass `SE_Stats` or `StatusEffect`, (2) override `Setup`/`UpdateStatusEffect`/`ResetTime` following this shape, (3) mirror "only extend TTL, never shorten" for stacking/refreshing buffs, (4) reuse the static-scratch-list idiom for per-tick proximity scans.

---

### `CSharp/` Folder (Reference Staging Area + Offline API-Lookup Tool)

- **Purpose:** A secondary staging copy bundling (a) duplicate reference source files also found at `libs-Tools` root (`Player.cs`, `Player.il`, `SE_Rested.cs`, `ServerSync.cs`, `DeepReflectionWalker.cs`) and (b) a small standalone **offline reflection console tool** called `DumpHud`.
- **Key files:** `libs-Tools/CSharp/DumpHud/Program.cs`, `libs-Tools/CSharp/DumpHud/DumpHud.csproj`.
- **Architecture:**
  - `DumpHud.csproj` is a bare `net8.0` console `Exe` (not a BepInEx plugin) that references only three DLLs directly by absolute `HintPath`.
  - `Program.cs` is a ~25-line `Main` that does `typeof(Hud).GetFields(...)`/`GetProperties(...)` filtered by a name substring and writes matches to `output.txt`. A **static, offline, compile-time reflection query** pattern: because the referenced `assembly_valheim.dll` is a real (optionally publicized) game assembly, `typeof(Hud)` resolves against the actual game type without launching Valheim/BepInEx at all.
- **How to implement/set up (step-by-step recipe):** For a quick "what fields does class X have that match Y" lookup: (1) create a throwaway `net8.0` console project, (2) add `<Reference>` entries with `HintPath` pointing at the relevant `libs-Tools\*.dll` (a publicized copy is preferable so private/internal members show up too), (3) `typeof(TargetClass).GetFields(...)`/`GetProperties(...)`, filter/print. Much faster than opening a full decompiled `.cs` dump and searching for one-off lookups.
- **Reusable pattern/snippet:**
  ```csharp
  var type = typeof(Hud);
  foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
      if (f.Name.ToLower().Contains("food")) Console.WriteLine($"{f.FieldType.Name} {f.Name}");
  ```

---

### PluginLookup.cs (Case-Insensitive Soft-Dependency Probe) — **canonical copy, copy-paste target**

- **Purpose:** The one supported way to ask "is plugin X loaded?" across every project in this root. Canonical source: `libs-Tools/CSharp/PluginLookup.cs`.
- **The bug it exists to prevent:** `Chainloader.PluginInfos` is a plain `Dictionary<string, PluginInfo>` built with the **default comparer — ordinal and case-sensitive**. `ContainsKey("some.guid")` therefore returns `false` against a plugin registered as `"Some.Guid"`. A mod author owns their own GUID and re-casing it between releases does not look like a breaking change from where they stand — **Shadows of Midgard did exactly that between 1.0.0 and 2.0.0.** The failure mode is the dangerous kind: no exception, no log line, the soft dependency just looks absent and the dependent feature silently does nothing. Fatty 1.1.5 hit it twice at once (Food Duration Multiplier compat hand-off; Valheim Cuisine categorisation — the latter's GUID was read out of a strings table and was never case-guaranteed to begin with).
- **STANDING RULE:** never compare a plugin GUID with `==`, `ContainsKey` or `TryGetValue`. Every soft-dependency probe goes through `PluginLookup.TryFind` / `PluginLookup.IsLoaded`.
- **Deployed in (2026-08-01):** Fatty, Njord, BlightedHeart, DvergrAllies, DeathBeforeDishonor. It is **copy-pasted, not shared by reference** (these are independent plugin assemblies), so the copies drift — verified 2026-08-01 that all five are logically identical, differing only in namespace plus DeathBeforeDishonor's fully-qualified `global::System.*`. **Change the canonical copy and push it to all five; fix a copy and bring it back here.**
- **Two naming traps, both already stepped on:**
  1. Write `global::BepInEx.PluginInfo`, never bare `PluginInfo` — BepInEx's `PluginInfoProps` generates a **static** `PluginInfo` class into the project's own root namespace, which wins the bare name and cannot be used as a parameter type.
  2. Write `global::System.*` rather than relying on `using System;` — projects here have hit collisions on the bare names. Uglier, but compiles everywhere, so the canonical copy uses it.
- **Known outstanding instance of the bug:** `PAUSED/DvergrWarbandST/DvergrAllies/Plugin.cs:22` still calls `Chainloader.PluginInfos.ContainsKey("balrond.astafaraios.BalrondIdleActors")`. Paused project, so not urgent — fix it if that project is ever revived.

---

### `SharedUI/` Folder (Promoted Cross-Project IMGUI Infrastructure) — **canonical, `<Compile Include>` target**

- **Purpose:** The first folder in `libs-Tools` populated specifically to act on the standardization recommendation below (§ Summary of Cross-Cutting Observations, item 1) — shared IMGUI source consumed by `<Compile Include>` from the start, rather than copy-pasted and left to drift like `ServerSync.cs`'s per-project copies did. Created **2026-08-04** for `BarrkUI` (a new UI-overhaul mod), which is the first project to need the gilt-frame theme AND an input-focus fix in the same build — exactly the situation that makes copy-paste-per-project expensive.
- **Key files:** `libs-Tools/SharedUI/GiltFrameTheme.cs`, `libs-Tools/SharedUI/UIFocus.cs`.

- **`GiltFrameTheme.cs`** — the procedural gilt picture-frame IMGUI theme (double-rail embossed gold border, mitred acanthus corners, top/bottom palmette crests, coverage+height painter baked against one fixed light — full mechanism described under TortalPortal's `TortalUITheme` in `TortalPortal.md`). **This is the promotion the two existing copies were already asking for**: TortalPortal's own `UI/TortalUITheme.cs` doc note says *"Candidate for vendoring into `libs-Tools` as a shared source file... once those three [Configuration couplings] are parameterised"*, and Fatty's port (`Patches/GiltFrameTheme.cs`) is **already visibly behind** TortalPortal's (missing the live gold-recolour, the favourite heart, `DrawDot`, `ImageButton`) — the exact drift the copy-paste approach predicts.
  - **The one change from the TortalPortal original:** `EnsureBuilt()` took no arguments and read `Configuration.uiGoldColour` / `Configuration.uiTextScale` / `Configuration.uiFontSizeDelta` off a project-specific static class — which is precisely what made the file non-shareable as source. The canonical copy is `EnsureBuilt(Color goldColour, float textScale = 1f, int fontSizeDelta = 0)`; the caller's own `ConfigEntry<T>.Value`s are passed in directly. No other logic changed — same painter, same ornament, same style-building.
  - **Not yet migrated:** TortalPortal's `TortalUITheme.cs` and Fatty's `GiltFrameTheme.cs` still carry their original pre-promotion, project-coupled copies. Re-pointing them at this file (deleting the local copy, adding `<Compile Include>`, and changing their `EnsureBuilt()` call sites to pass values instead of relying on the static read) is a clean follow-up but was **not done as part of this promotion** — it touches two already-shipped mods' build output and wasn't asked for. Do it opportunistically the next time either project's UI is touched.
- **`UIFocus.cs`** — generalizes TortalPortal's `InputFocusPatch.cs` (a `Chat.HasFocus` postfix + a `GameCamera.UpdateMouseCapture` prefix, fixing "typing in an IMGUI field also drives player movement/Use" and "IMGUI windows open with the mouse cursor still locked to the camera") from **one hardcoded window** to **any number of independently-open windows**, via a named-token registry (`SetWantsCursor(windowId, bool)` / `SetHasTextFocus(windowId, bool)` / `SetBlocksGameInput(windowId, bool)`, with `WantsCursor` / `HasTextFocus` / `BlocksGameInput` true while any token is active). A single shared bool (TortalPortal's original shape) does not compose once a mod has more than one IMGUI window: two windows open at once would have the second one's `OnGUI` stomp the first one's flag on the same frame. Self-installing via `PatchAll(typeof(SharedUI.UIFocus.UIFocusPatch))` — it owns its own `ManualLogSource` (`Logger.CreateLogSource("SharedUI.UIFocus")`), so it needs zero per-project edits to compile in.
  - **Both underlying gotchas are still exactly as documented in `TortalPortal.md`'s "Cross-cutting IMGUI gotchas" table** — this file does not change the mechanism, only how many windows can share it. Read that table before touching either patch; in particular, the cursor fix **must** be a prefix that replaces `UpdateMouseCapture`, not a postfix that corrects the lock state afterwards — Unity snaps the cursor to screen centre the instant it (re-)locks, so a postfix still yanks the pointer every frame.
  - **A confirmed real sibling-mod conflict, found auditing `BarrkUI`'s compat-against dump of Azumatt's `AzuExtendedPlayerInventory` (2026-08-04):** that mod carries its own `[HarmonyPostfix]` on this exact `GameCamera.UpdateMouseCapture` method (`JCBPUIUseBleedGuard`, dormant unless Jewelcrafting or Backpacks is also installed) that re-touches `Cursor.lockState` to keep the cursor warped over inventory slots. A postfix from another mod always runs after *our* prefix regardless of what the prefix returned, so it can silently re-lock the cursor the instant both are active together. Fixed by adding our OWN `[HarmonyPostfix][HarmonyPriority(Priority.First)]` on the same method that simply re-asserts our already-decided cursor state — Harmony runs postfixes low-priority-first/high-priority-last, so `Priority.First` guarantees the final word over any sibling mod sitting at the default priority. This is deliberately **not** the retracted "max-priority-prefix + min-priority-postfix-reassert" anti-pattern from `ShadowsOfMidgard.md` (cross-cutting pattern 5) — that one re-ran entire guard chains at `int.MaxValue`/`int.MinValue` specifically to steamroll every sibling mod's ordering; this is a two-line reassertion of a value we already own, gated on a real, named, confirmed conflict rather than applied defensively everywhere.
- **How to implement/set up (step-by-step recipe) for a new mod:**
  1. `<Compile Include="..\libs-Tools\SharedUI\GiltFrameTheme.cs" />` and/or `<Compile Include="..\libs-Tools\SharedUI\UIFocus.cs" />` in the `.csproj` (same mechanism as `ServerSync.cs`, see above).
  2. For the theme: call `SharedUI.GiltFrameTheme.EnsureBuilt(myGoldConfigEntry.Value, myScaleConfigEntry.Value, myFontDeltaConfigEntry.Value)` at the top of every `OnGUI`, then `DrawWindow`/`Body`/`FooterLine` etc. exactly as documented for `TortalUITheme` in `TortalPortal.md`.
  3. For focus: register `typeof(SharedUI.UIFocus.UIFocusPatch)` in whatever `Harmony.PatchAll` loop the mod already runs per patch class. In each window's own code, call `SharedUI.UIFocus.SetWantsCursor(windowId, isOpen)` and `SharedUI.UIFocus.SetHasTextFocus(windowId, aTextFieldInThisWindowIsFocused)` every frame the window runs (both are idempotent no-ops when the value hasn't changed). **Read the two ⚠ entries below before writing either call** — there is a third registry, and there is a wrong place to call all three from.
- **⚠ There are THREE registries, not two — `SetBlocksGameInput` is the one for windows nobody types into** (added 2026-08-08, BarrkUI 0.9.2). It reaches the same `Chat.HasFocus` postfix as `SetHasTextFocus`, but they answer different questions and one caller in every consuming mod needs them apart: the gate that stands a hotkey down *while the player is typing*. A drag-to-arrange overlay takes the mouse and the arrow keys and must stop the game reading them — but nobody is typing, and claiming text focus jams that gate on for as long as the overlay is up, which for an overlay whose only way out **is** a hotkey means being locked in. Use `SetHasTextFocus` only for an actual focused text field. **Note the side effect either way:** while any input-blocking token is held, vanilla's own Tab and M do nothing (`InventoryGui.Update` 41462 and `Minimap.Update` 47165 both open with `!Chat.instance.HasFocus()`), so a window that expects the player to open those screens has to relay the two keys itself — `BarrkUI/Layout/LayoutEditor.cs:HandlePanelKeys` is the worked example, and it relays for whoever holds the token rather than only for its own overlay.
- **⚠⚠ RAISE THE TOKEN FROM `Update`, NOT FROM `OnGUI`, AND RAISE IT ON HOVER** (added 2026-08-12, BarrkUI 0.9.4). **The most expensive mistake available to a consumer of this file**, because announcing a window from inside its own `OnGUI` is the obvious reading of step 3 above, compiles, and works from the *second* frame onward. Unity runs every `Update` before any `OnGUI`, and `Player.TakeInput` (decompile 17774) / `PlayerController.TakeInput` (22606) are consulted from `Update` — so a token set while drawing is set *after* the game has already asked and already been refused. On the frame a window opens, which is the frame the player clicked, which is the frame a mouse button is down, the game reads that button as an attack. Worse, the click is what removes the cover you did not know you had: `Chat.Update` (34306-34311) drops the chat input's focus on **any** Mouse0 down while focused, so a window opened from a focused chat box had vanilla's `Chat.HasFocus()` covering for it right up until the press that opened it. **`e.Use()` cannot fix this** — IMGUI events and ZInput are two independent readings of the same physical button. Symptom in the wild: *"mouse trapping and clicking issues, very specific to using the mouse with the emoji menu"* — i.e. the one control the player clicks repeatedly. Two riders: **gate the hover test on `Cursor.visible`** (`Input.mousePosition` keeps its last value while the cursor is locked, so a stale position latches the claim on forever during normal play), and **give any "the button is down on my chrome" latch a deadline** (a `MouseUp` your `OnGUI` never runs to see used to mean "a panel lingers" and now means "the player cannot move, with nothing on screen to explain it").
- **⚠ Window ids must carry the MOD's name, not just the window's** (added 2026-08-04, BarrkUI). `UIFocus` is a registry shared *between* mods, so `nameof(MyWindow)` collides with any other mod that happens to have a class of the same name — and because `SetWantsCursor(id, false)` **removes** that token, one mod closing its window would clear another mod's live cursor request. Use `"MyMod_MyWindow"`. Same reasoning applies to `GUI.Window` ids, which share one process-wide IMGUI namespace: derive them from the prefixed string (`"MyMod_MyWindow".GetStableHashCode()`) rather than picking a literal and hoping.
- **⚠ The theme is ONE process-wide baked set, and consumers that disagree used to thrash it** (fixed 2026-08-04, BarrkUI). `EnsureBuilt` rebakes every procedural texture when the gold colour changes and reallocates all 12 `GUIStyle`s when the scale/delta changes. Two mods calling it with *different* values each frame therefore tore the whole set down and rebuilt it **twice per frame, forever** — a config mismatch presenting as unexplained frame time, with nothing in the logs. `EnsureBuilt` now records the frame it last applied a set in: a caller whose values match the live set takes a three-compare fast path, and a caller asking for something different in a frame another consumer already built **no-ops and logs once**. First caller each frame wins and keeps winning. The fix is deliberately not a multi-set cache — one shared look is the point of the file, so the right resolution is still "set the same `UIGoldColour` / `UITextScale` / `UIFontSizeDelta` in every mod that uses it", and the warning now says so instead of leaving you to find it with a profiler.
- **When to add more to this folder:** any IMGUI/uGUI pattern that a SECOND mod ends up needing verbatim (not "inspired by" — verbatim) is a promotion candidate. Promote it here, parameterize away any single-project coupling the same way `GiltFrameTheme.EnsureBuilt` was, and update this entry plus `MASTER_IMPLEMENTATIONS.md`'s pointer to it.

---

### `Editor/` Folder (Vendored Unity Editor Installation)

- **Purpose:** Not custom tooling — a full vendored copy of a **Unity Editor installation for Linux** (a stripped x86-64 ELF `Unity` executable plus native plugins for baking/compression/raytracing, mirroring the standard Unity Editor `Data` folder layout). Likely vendored so the author has (a) a reference copy of the exact `UnityEditor.dll`/`UnityEngine.dll` matching Valheim's Unity version for IntelliSense/type resolution, and/or (b) a headless Linux Editor binary usable for batch-mode AssetBundle building (a common need in Jotunn-based mods) without a full local Editor install.
- **Key files:** `libs-Tools/Editor/Unity`, `libs-Tools/Editor/Data/Managed/*.dll`.
- **How to implement/set up:** Not something a new project "adopts" via code changes; if a new mod needs to build custom AssetBundles, point an AssetBundle-build script/CI job at `Editor/Unity -batchmode -quit -projectPath ... -executeMethod ...` rather than requiring a full local Unity install. (See also `MistsofAvalor.md` System 23 for the actual `UnityBundleBuilder` pipeline this could support.)

---

### Decompiled/Publicized Valheim Assembly Reference Library (Build & Tooling Pattern)

- **Purpose:** The folder's most valuable *reusable system*: a shared, versioned cache of the game's compiled and decompiled internals so that (a) every mod project can **compile** against real Valheim/Unity/BepInEx types via simple relative DLL references, and (b) the author/IDE/AI agents can **read** full C# source reconstructions of those types for IntelliSense and manual lookup, without redundantly decompiling per-project.
- **Key files/components:**
  - Vendored binary DLLs at `libs-Tools/` root: `assembly_valheim.dll` (core game logic), `assembly_utils.dll`, `assembly_guiutils.dll`, `assembly_postprocessing.dll`, `0Harmony.dll`, `BepInEx.dll`, `Jotunn.dll`, `Newtonsoft.Json.dll`, `YamlDotNet.dll`, `steamworks.net.dll`, `Unity.TextMeshPro.dll`, `gui_framework.dll`, `PlayerDLL.dll`, and the full set of `UnityEngine.*Module.dll` engine modules — exactly the reference-assembly set needed to compile a BepInEx/Jotunn Valheim mod.
  - `assembly_publicizer.dll` (2.1 MB) — the assembly-publicizing tool artifact. The mechanism actually used by every inspected `.csproj` is the **`BepInEx.AssemblyPublicizer.MSBuild` NuGet package**, invoked declaratively per-reference via `<Reference Include="assembly_valheim" Publicize="true">` — this rewrites `private`/`internal` members to `public` in a build-time-generated copy of the DLL (cached under each project's `obj/` folder). `assembly_publicizer.dll` sitting standalone is most likely the extracted publicizer engine used once manually to pre-publicize the DLL before feeding it to the decompiler.
  - `DECOMPILED ASSEMBLY VALHEIM/assembly_valheim.decompiled.cs` — a single flattened ~4.0 MB C# file containing the whole-assembly decompile of `assembly_valheim.dll` (confirmed real class bodies present). The naming convention is consistent with output from **ILSpy** / its CLI (`ilspycmd`) — the standard free decompiler used throughout the Valheim modding community.
  - `decompiled_core/UnityEngine.CoreModule.decompiled.cs` (3.5 MB) — same treatment applied to `UnityEngine.CoreModule.dll`.
  - `decompiled_guiutils/UIInputHandler.decompiled.cs` (4 KB) — a single-class decompile of Valheim's `UIInputHandler` extracted from `assembly_guiutils.dll`.
  - `OLD-TheEye Dumps/` (`AllTypes_Deep.txt`, `All_Deep.txt` 97 MB, `All_Deep_ApiBurst.txt` 46 MB) — **not static decompiles**; saved output from the author's own separate mod/tool project **"The Eye"** (see `DeathBeforeDishonor_TheEye.md`), which reflects over every *currently loaded* assembly (game + all installed mods) **at runtime, in-game** to emit structured API dumps — complementary to the static ILSpy decompiles because it captures the *actual runtime-loaded* surface (including other mods' injected types).
  - `Jotunn.dll` — the Jötunn modding library itself, vendored here for projects that reference it directly.
- **How to implement/set up (step-by-step recipe) — referencing this shared library from a new mod project:**
  1. Place the new project as a sibling folder to `libs-Tools`, so `..\libs-Tools\` resolves correctly.
  2. In the new `.csproj`, add standard BepInEx NuGet packages: `BepInEx.Core` (5.*), `BepInEx.PluginInfoProps`, `BepInEx.Analyzers`, `UnityEngine.Modules` (pin to the game's Unity version), and `BepInEx.AssemblyPublicizer.MSBuild` (0.4.1, `PrivateAssets=all`).
  3. Add `<RestoreAdditionalProjectSources>` pointing at `https://nuget.bepinex.dev/v3/index.json` alongside the default NuGet feed.
  4. Add `<Reference>` entries with `HintPath` pointing at `..\libs-Tools\<dll>.dll` for every game/engine assembly used — at minimum `assembly_valheim` (with `Publicize="true"` and `<Private>false</Private>`). Always set `<Private>false</Private>` (don't copy these into the build output).
  5. For debugging/inspection convenience, open the corresponding file under `DECOMPILED ASSEMBLY VALHEIM/`, `decompiled_core/`, or `decompiled_guiutils/` to read real method bodies, or run a `DumpHud`-style throwaway console app for a targeted reflection query, or use `OLD-TheEye Dumps/*.txt` (or trigger a fresh dump via the actual `TheEye` mod) for structured API text searchable by grep.
  6. If a member isn't accessible even with `Publicize="true"`, confirm the specific DLL that declares it is also referenced with `Publicize="true"` — publicization is per-reference, not global.
- **⚠️ A missing-reference build failure is NEVER a cue to create a local `libs\` folder.** This is the one way the
  shared-library pattern gets silently undone, and it happened on 2026-07-30: `MistsofAvalor` had its local `libs\`
  deleted as part of migrating to `..\libs-Tools\`, but its `.csproj` still pointed at `libs\`, so every reference
  failed to resolve. The instinctive repair — copy the DLLs from `libs-Tools` into a fresh local `libs\` — compiles
  perfectly and reverts the project to a private stale copy of `assembly_valheim.dll`, which is exactly what this
  folder exists to prevent. **Fix the `HintPath`, not the filesystem.** If a DLL genuinely is not in `libs-Tools`,
  add it *there*; that is the folder's job. Also delete any leftover `<Compile Remove="libs\**" />` when migrating.
- **Migration status (2026-07-30):** `Fatty` and `MistsofAvalor` reference `..\libs-Tools\`. `ShadowsOfMidgard` and
  the `PAUSED/*` projects still carry local `libs\` folders and are the outstanding migrations.
- **Reusable pattern/snippet (.csproj reference block):**
  ```xml
  <ItemGroup>
    <PackageReference Include="BepInEx.AssemblyPublicizer.MSBuild" Version="0.4.1" PrivateAssets="all" />
  </ItemGroup>
  <ItemGroup>
    <Reference Include="assembly_valheim" Publicize="true">
      <HintPath>..\libs-Tools\assembly_valheim.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>..\libs-Tools\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
  ```

---

### Player.cs / Player.il (Full Decompiled Class Reference Dump)

- **Purpose:** A standalone, extremely large decompiled/disassembled dump of Valheim's core `public class Player : Humanoid` — one of the largest and most-patched classes in the game for most mods. Kept purely as an offline reference so the author doesn't need to reopen the full assembly decompile to look up `Player` member signatures/logic.
- **Key files:** `libs-Tools/Player.cs` (379 KB, real reconstructed C# with method bodies), `libs-Tools/Player.il` (42 MB — raw CIL/IL disassembly, useful when the decompiled C# is ambiguous or exact IL/metabehavior matters). Byte-identical duplicates at `libs-Tools/CSharp/Player.cs`/`Player.il`. Pure reference material, not an authored system.

---

### `BepInEx/` Folder (Local Plugin Staging / Build-Output Drop, not framework files)

- **Purpose:** Despite the name, this is **not** a copy of the BepInEx framework's own files/config — it contains exactly one thing: `BepInEx/plugins/PortalLock/net472/PortalLock.dll` (+ `.pdb` + a `YamlDotNet.dll` copy). `PortalLock` is one of the author's own mod projects (paused — see `PAUSED_Projects.md`), whose active source lives at `PAUSED/PortalLock/src/PortalLock/`. This folder is most plausibly a **stray/leftover build-output drop** (a misconfigured post-build copy step or manual staging for testing), not a reusable pattern. Flagged here so it isn't mistaken for shared framework/config material.
- **Key files:** `libs-Tools/BepInEx/plugins/PortalLock/net472/PortalLock.dll`, `.pdb`.

---

## Summary of Cross-Cutting Observations

1. **Two coexisting vendoring styles** were observed across projects for the same shared source files (`ServerSync.cs`): direct `<Compile Include="..\libs-Tools\ServerSync.cs" />` (e.g. `Njord`), versus a **locally copied duplicate** committed inside the project itself (e.g. `DeathBeforeDishonor/System/ServerSync.cs`, `Fatty/Libs/ServerSync.cs`, `ShadowsOfMidgard/libs/ServerSync.cs` — all confirmed byte-identical), versus referencing the **precompiled `ServerSync.dll`** as an external binary. **Recommendation for future mods:** standardize on the `<Compile Include>` pattern to keep a single source of truth without drift risk. **Adopted 2026-08-04:** `BarrkUI` is the first project built entirely on this rule — `ServerSync.cs` via `<Compile Include>` against the root canonical copy, plus the new `SharedUI/` folder (below) for UI infrastructure, and nothing vendored locally. The drift this recommendation warned about is not hypothetical: Fatty's copy-pasted `GiltFrameTheme.cs` is already measurably behind TortalPortal's `TortalUITheme.cs` it was ported from (see `SharedUI/` below).
2. All game/engine reference DLLs are consistently pathed as `..\libs-Tools\<name>.dll` with `<Private>false</Private>`, and `assembly_valheim` is consistently publicized via the `BepInEx.AssemblyPublicizer.MSBuild` NuGet package rather than a manually-run publicizer tool at commit time.
3. The decompiled-source tree (`DECOMPILED ASSEMBLY VALHEIM/`, `decompiled_core/`, `decompiled_guiutils/`) and the runtime API-dump tree (`OLD-TheEye Dumps/`, produced by the author's own `TheEye`/`WubarrksEye` mod project) are complementary reference layers: static shipped-assembly structure vs. actual runtime-loaded API surface (including other mods).

---

### DEDICATED-SERVER-TESTBED (headless verification rig)

- **Purpose:** A reusable, self-contained Valheim dedicated-server rig for verifying any mod in the solution against the headless topology (no `Player` instances, instances only near world origin, full ZDO table). Built for the Mists of Avalor 0.1.0 dedicated pass; serves every mod.
- **Key files:** `libs-Tools/DEDICATED-SERVER-TESTBED/` — `install-server.ps1` (steamcmd + app 896660 into `server/`), `wire-bepinex.ps1` (assembles a LEAN profile — BepInEx core/patchers + Jotunn + the mod under test — and wires doorstop with an absolute `target_assembly` so BepInEx roots at the profile), `start-avalor-server.ps1` (headless launch, throwaway `saves/`), `README.md` (log-marker acceptance checklist).
- **Engine background:** `libs-Tools/VALHEIM-DEDICATED-SERVER-FACTS.md` — the verified dossier (reference position rules, ghost zones, persistent-only ownership assignment, `GetAllCharacterZDOS`, routed-RPC rules) every headless design decision traces to.
- **How to reuse for another mod:** point `$modBuild` in `wire-bepinex.ps1` at the other project's build output (and add its hard dependencies to the lean profile); the launch and log locations are mod-agnostic.
