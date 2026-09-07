# Fatty Mod — Technical Report

## Overview

Fatty is a Valheim BepInEx/Harmony mod (`wubarrk.fatty`, v1.1.5) that overhauls the food/eating system: it expands the stomach to 5 slots (4 solid food + 1 dedicated drink) with configurable same-food stacking and diminishing returns, replaces vanilla's decaying food-stat curve with an optional "no decay until it expires" model, and adds emergent bonuses on top — dietary "synergy" status effects (Balanced Diet, Sugar Rush, Fisherman's Friend, Lumberjack's Feast), drink-category buffs (Arcane/Warrior's/Reveler's/Seafarer's Draught + a "Drunk" state), permanent lifetime-eating milestones across six independent stats (carry weight, stamina, eitr, swim speed, all-round regen, and a separate total-food-eaten "Fatness" axis for base HP) - five of which alternate between two different reward types across their 3 tiers rather than granting the same stat three times - and stamina/eitr regen rescaling to keep refill *time* at vanilla parity despite the bigger pools. It ships a fully custom 5-slot food HUD bar (steady, non-flashing, with same-prefab stacks draining sequentially rather than in lockstep) plus an IMGUI "Feast Ledger" debug/inspection panel wearing a fully procedural gilt picture-frame theme (double-rail gold border, mitred corner flourishes, crest ornaments, per-entry item icons) ported from a sister project, and it auto-classifies every food/drink in the game (vanilla + modded) into diet categories via an ObjectDB scan with a versioned, self-migrating JSON data file.

---

### Config System (BepInEx.Configuration + ServerSync)
- **Purpose:** Centralizes every tunable value as a `BepInEx.Configuration.ConfigEntry<T>`, with server-authoritative sync for gameplay values (so a server admin's config overrides connecting clients) and a separate unsynced tier for pure client/UI preferences (HUD panel position/size, debug logging, one-shot maintenance switches).
- **Key files:** `Configuration/ConfigManager.cs`, `Libs/ServerSync.cs` (vendored third-party library by Azumatt, namespace `ServerSync`), `Plugin.cs`
- **Architecture:**
  - `ServerSync.ConfigSync` is instantiated once as a static field: `new ConfigSync("com.wubarrk.fatty") { DisplayName = "Fatty", CurrentVersion = ..., MinimumRequiredVersion = ... }`.
  - A boolean "lock" config entry (`serverConfigLocked`, default `true`) is registered via `configSync.AddLockingConfigEntry(serverConfigLocked)` — when true, only the server admin can edit synced values; clients receive them read-only.
  - Every gameplay `ConfigEntry<T>` is created through a private `BindSynced<T>` helper that calls `config.Bind(section, key, default, description[, range])` then `configSync.AddConfigEntry(entry)`. Numeric entries route through a ranged overload that attaches `new AcceptableValueRange<T>(min, max)` via `ConfigDescription`.
  - Client-only entries (panel X/Y/width/height, debug toggle, a "rebuild food data" one-shot switch, hotkey) go through separate `BindLocal`/`BindLocalRanged` helpers that call `config.Bind` **without** registering with `configSync` — this is the deliberate synced/unsynced split.
  - `ConfigManager.Init(Config)` is called from `Plugin.Awake()` right after compat detection and before Harmony patching.
  - Config sections are numbered strings (`"1 - General"`, `"2 - Core"`, `"3 - Stacking"`, etc.) purely to control display ordering.
- **How to implement (step-by-step recipe):**
  1. Add BepInEx and `0Harmony` references; vendor `ServerSync.cs` into a `Libs/` folder.
  2. In your `static class ConfigManager`, create `private static readonly ConfigSync configSync = new ConfigSync("<your.unique.guid>") { DisplayName = "...", CurrentVersion = PluginVersion, MinimumRequiredVersion = PluginVersion };`.
  3. Add a `ConfigEntry<bool> serverConfigLocked = config.Bind("1 - General", "Lock Configuration", true, "...")` then `configSync.AddLockingConfigEntry(serverConfigLocked)`.
  4. For each gameplay value: `var entry = config.Bind(section, key, default, description);` then `configSync.AddConfigEntry(entry);` — wrap this pair in a small helper. Give numeric entries an `AcceptableValueRange`.
  5. For client-only/UI values: call `config.Bind(...)` directly and never pass the entry to `configSync`.
  6. Call `ConfigManager.Init(Config)` in `Awake()` before `harmony.PatchAll()`.
  7. ServerSync automatically hooks its own RPCs on `ZNet`/`ZRoutedRpc` connect to push server values to clients — no extra wiring required beyond constructing `ConfigSync` and adding entries.
  8. Gotcha: values registered with `configSync` may not reflect the server's pushed value until the client is actually **in-game** (connect handshake), not at main-menu load — code paths that read config at `ObjectDB.Awake` (menu DB) must be re-run later (see Synergy system below) rather than baking values in once.
- **Reusable pattern/snippet:**
```csharp
private static readonly ConfigSync configSync = new ConfigSync("com.you.modid")
{ DisplayName = "YourMod", CurrentVersion = Plugin.Version, MinimumRequiredVersion = Plugin.Version };

private static ConfigEntry<T> BindSynced<T>(ConfigFile cfg, string section, string key, T def, string desc)
{
    var e = cfg.Bind(section, key, def, desc);
    configSync.AddConfigEntry(e);
    return e;
}
// unsynced/local:
private static ConfigEntry<float> BindLocal(ConfigFile cfg, string section, string key, float def, string desc)
    => cfg.Bind(section, key, def, desc); // never passed to configSync
```

---

### JSON Food-Data Store + Version Migration System
- **Purpose:** Persists every scanned food/drink's classification (diet category, drink flag) and raw stats to a hand-editable JSON file on disk (`Fatty_FoodData.json` in the BepInEx config folder), and automatically migrates/re-derives that data when the classification *rules* change between mod versions, without losing the underlying entry list. **This is the config + JSON migration system called out as a standout feature.**
- **Key files:** `Configuration/CategoryManager.cs`, `Scanners/FoodScanner.cs` (writer/trigger side)
- **Architecture:**
  - File path: `Path.Combine(BepInEx.Paths.ConfigPath, "Fatty_FoodData.json")`; backup path is the same string + `.bak`.
  - `public const int CurrentDataVersion = 5;` — a single integer bumped whenever the on-disk **shape** or **classification rules** change in a way that invalidates previously-derived data. The class doc-comments each version's meaning (1 = original shape, 2 = category/drink split, 3 = drink slot detection changed, 4 = drink diet read from status effect, 5 = eitr-dominance rule relaxed) — this is effectively an inline changelog that doubles as migration rationale.
  - On-disk shape is a **versioned wrapper**, not a bare array: `class FoodDataFile { int DataVersion; List<FoodData> Foods; }`, serialized/deserialized with `Newtonsoft.Json` (`JsonConvert.SerializeObject(file, Formatting.Indented)` / `DeserializeObject<FoodDataFile>`).
  - Backward compatibility: `ReadFile(json)` detects the OLD pre-versioned shape by checking if the trimmed JSON text starts with `[` (a bare array) — if so it deserializes directly into `List<FoodData>` and returns a synthetic `DataVersion = 1`; otherwise it deserializes the wrapper and reads its `DataVersion`.
  - `Load()` flow: clear all in-memory indices → read file → rebuild the prefab↔in-game-name lookup (this survives migration, since it's derived from the item, not the classification rules) → if `fileVersion < CurrentDataVersion`, call `Migrate(fromVersion)` and **return early** (skip normal category-loading) → otherwise walk `AllFoodData` and repopulate `Categories`/`Drinks` indices normally.
  - `Migrate(fromVersion)`: copies the current file to `.bak` (`File.Copy(..., overwrite:true)`), logs a warning explaining that hand-edited categories will be lost, then **clears** the category/drink indices (but keeps `AllFoodData`'s entry list) and sets a public flag `NeedsReclassify = true`. It does NOT touch the JSON file itself — the file on disk is only rewritten later, once `FoodScanner` has re-derived every entry.
  - `FoodScanner.ScanRoutine()` (a coroutine, see below) checks `CategoryManager.NeedsReclassify`; if true, it force-reclassifies every ObjectDB item (bypassing the normal "only reclassify if Unknown" shortcut) and calls `CategoryManager.Save()` unconditionally after the pass, even if nothing else changed — "a migration MUST persist even if nothing else changed, otherwise it re-runs every launch." `NeedsReclassify` is cleared only after a *successful* full pass, so an aborted/crashed scan retries the migration next launch.
  - A second, independent migration trigger exists: a **user-facing config toggle** `rebuildFoodData` (unsynced, one-shot). `Plugin.Awake()` checks it right after `CategoryManager.Init()` and, if set, calls `CategoryManager.RequestRebuild()` (same backup + clear-indices + `NeedsReclassify = true` pattern as a version migration, but not gated on a version bump). `FoodScanner` turns the config value back off (`ConfigManager.rebuildFoodData.Value = false`) once the rebuild completes, so it's a genuine one-shot switch.
  - Hand-authored per-item overrides ("hardcoded integrations" for other mods, e.g. Valheim Cuisine) are tracked in a separate `HashSet<string> _hardcoded` via `AddHardcodedCategory`/`IsHardcoded` — these survive a reclassify pass (the scanner checks `IsHardcoded` and keeps the existing category instead of re-deriving it), while the drink-slot flag is always re-derived even for hardcoded-category items.
  - Performance: a reverse index `Dictionary<string,string> _categoryByPrefab` is maintained in lockstep with the forward `Dictionary<string, List<string>> Categories` (via `AddToCategory`) specifically because `GetCategoryForPrefab` is called per-food, per-tick from several hot paths (synergy counting, drink buffs, milestone lookups) — this is a "reverse index next to a forward index, updated in the same setter" pattern worth reusing generally.
- **How to implement (step-by-step recipe):**
  1. Define a POCO for one record (`FoodData`) and a wrapper POCO carrying `{ int DataVersion; List<T> Items; }`.
  2. Pick a `CurrentDataVersion` constant and comment its history as you bump it — treat this as your migration changelog.
  3. On load: read raw text, sniff the top-level JSON token (`[` = old bare-array shape vs `{` = wrapper shape) to support reading files from *before* you introduced versioning at all.
  4. If `fileVersion < CurrentDataVersion`: back up the raw file (`File.Copy(path, path+".bak", overwrite:true)`) BEFORE mutating anything, log a clear warning naming the backup path, clear only the *derived* indices (not the raw entry list if it's still readable), and set a `NeedsReclassify`-style flag.
  5. Have your "populate from source of truth" pass (here: an ObjectDB scan) check that flag and force a *full* re-derivation instead of its normal incremental/only-unknowns shortcut.
  6. Persist (`Save()`) unconditionally when the flag was set, even if the re-derived data is identical to what's already in memory — otherwise a crash before save means the migration silently re-triggers forever, and a successful-but-no-op migration would otherwise never write the bumped version number to disk.
  7. Only clear the `NeedsReclassify` flag after the save/pass fully completes, so a crash mid-migration retries cleanly next launch instead of leaving a corrupt intermediate state that looks "migrated."
  8. Optionally add a manual user-facing rebuild trigger as a separate, unsynced one-shot `ConfigEntry<bool>` that funnels into the exact same backup+reclassify code path as an automatic version migration, and have the code that consumes it flip the config value back to `false` once done.
- **Reusable pattern/snippet:**
```csharp
private static int ReadFile(string json) {
    string t = json.TrimStart();
    if (t.StartsWith("[")) { Data = JsonConvert.DeserializeObject<List<T>>(json); return 1; } // pre-versioning
    var file = JsonConvert.DeserializeObject<Wrapper>(json);
    Data = file?.Items ?? new List<T>();
    return file?.DataVersion ?? 1;
}
if (fileVersion < CurrentDataVersion) {
    File.Copy(path, path + ".bak", overwrite: true);
    ClearDerivedIndicesOnly();       // keep the raw entry list
    NeedsReclassify = true;          // consumer does the real re-derivation + Save()
    return;
}
```

---

### Food Scanner (Dynamic ObjectDB Classification)
- **Purpose:** Automatically discovers every food/drink item in the game — vanilla and modded — at runtime by walking `ObjectDB.instance.m_items`, and classifies each into a diet category (Meat/Vegetable/Sweet/Fish/Eitr/Unknown) plus a drink-slot flag, using item stats first and word-matching against the prefab/display name second. Feeds `CategoryManager`'s JSON store.
- **Key files:** `Scanners/FoodScanner.cs`, `Scanners/Integrations.cs`
- **Architecture:**
  - `[HarmonyPatch(typeof(ZNetScene), "Awake")]` postfix starts a coroutine scan early (works when ObjectDB is already populated, i.e. vanilla-only setups).
  - `[HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]` postfix is the **reliable** trigger — by player spawn, modded items (e.g. injected by Jotunn) are guaranteed loaded. Both hooks are guarded by a `_hasScanned` bool so the real work runs exactly once per session.
  - The scan itself is a `IEnumerator ScanRoutine()` run via `MonoBehaviour.StartCoroutine`, batching `ItemsPerFrame = 100` items and `yield return null` between batches so a large modded item DB doesn't stall the load frame.
  - Classification (`ProcessItem`): an item qualifies if `m_food/m_foodStamina/m_foodEitr > 0` OR `m_itemType == Consumable`. Words are extracted from `m_shared.m_name` and the prefab name via a regex word-splitter (PascalCase/camelCase tokenizer) and matched as **whole words** against category `HashSet<string>` word lists (avoids `"tea"` false-matching inside `"Steak"`). Category priority: Eitr-stat-dominance check first → drink-effect classification (reads an `SE_Stats` status effect's numeric fields to infer diet from *what the drink does*) → name-word matching → fallback (`Meat` for unclassifiable drinks, `Unknown` for unclassifiable solid food — deliberately NOT defaulting everything unknown to Vegetable, a prior bug).
  - Drink-slot detection (`IsDrink`) is **separate** from diet classification and uses naming evidence only (`DrinkWords` set) — deliberately not "any zero-food Consumable," because that swept up potions/scrolls/boss-power items.
  - `Integrations.cs` hardcodes a compatibility table for the "Valheim Cuisine" mod, mapping ~60 known `VC_*` prefab names to categories/drink-slot by hand.
- **How to implement (step-by-step recipe):**
  1. Patch a scene/DB-ready lifecycle hook (`ZNetScene.Awake` postfix) AND a later, more-reliable hook (`Player.OnSpawned` postfix) — guard both with a single "already scanned" flag.
  2. Run the actual scan as a coroutine, batching N items per frame with `yield return null` between batches.
  3. Snapshot the item list before iterating so a concurrent mod mutating `ObjectDB.m_items` mid-scan can't invalidate your enumerator.
  4. For classification, tokenize names with a regex that splits PascalCase/camelCase into lowercase words, then match those words as a set-overlap against category keyword `HashSet<string>`s — never use raw `string.Contains` for keyword matching.
  5. For drinks with no informative name (meads), read the numeric fields of their status effect instead of trying to parse the name — infer category from mechanical effect.
  6. Provide a hand-authored override table, keyed by exact prefab name, for known third-party mods; apply it before the generic heuristic and mark those prefabs as protected from re-classification during a data migration.
  7. Persist results into a queryable in-memory model and save to disk only when something actually changed — except during a forced migration/rebuild pass, where you must save unconditionally.

---

### Custom Eating / Stomach / Stacking System
- **Purpose:** Fully replaces vanilla's 3-slot, non-stackable food system with a configurable N-slot (4 food + 1 drink) system that supports multiple stacks of the same item with diminishing returns per additional stack, an optional "no gradual decay" mode, and correct interop with other mods that also read/write player max-stat totals.
- **Key files:** `Patches/PlayerPatches.cs`
- **Architecture:**
  - `[HarmonyPatch(typeof(Player), nameof(Player.CanEat))] Prefix` fully replaces vanilla's gate: `CanAddFood()` counts existing same-food stacks vs `ConfigManager.maxFoodStacks`, and existing distinct food-types vs a food-slot cap (4) / drink-slot cap (1), returning a specific block message for each failure reason.
  - `[HarmonyPatch(typeof(Player), nameof(Player.EatFood))] Prefix` fully replaces vanilla: validates via `CanAddFood`, computes an invented burn time for zero-burn-time drinks, appends a **new** `Player.Food` entry to `___m_foods` (never clears the list), fires vanilla-style "+X food_health" messages, records milestone/lifetime-log progress, and force-refreshes stats.
  - **Prefab-name resolution** (`ResolvePrefabName`) is centralized in one method used by every other system, because 4 independently-inlined copies of "get prefab name" previously drifted apart.
  - **Stat totals** (`GetTotalFoodValue` patch): the most technically distinctive piece. Because other mods also patch/transpile this same vanilla method, Fatty cannot simply assign the HP/Stamina/Eitr out-params. The solution: a `[HarmonyPriority(Priority.Last)] Prefix` **empties `___m_foods` in place** right before the original method body runs, so whatever the original returns is purely the "base" contribution from every *other* patch. A `[HarmonyPriority(Priority.First)] Postfix` immediately restores the stomach contents and then adds Fatty's own per-food HP/Stamina/Eitr sum on top of the now-uncontaminated base. A `[HarmonyFinalizer]` also calls the restore, guaranteeing the stomach is never left empty if the original method throws. This "stash → let vanilla run 'empty' → restore → add delta" bracketing pattern is a general, reusable Harmony technique for cleanly composing with unknown other patches on a method you don't fully control.
  - `FoodContribution` struct (stack index, per-stack multiplier, decay factor, burn time, HP/Sta/Eitr) is computed once per food per tick and shared verbatim between the stat-total postfix, the HUD draw, and the breakdown panel.
  - Diminishing returns ladder (`GetStackMultiplier`): stack 0 = 100%, stack 1 = config `stackDiminishingReturnStep1` (default 50%), stack 2 = `Step2` (default 25%), stack 3+ = `Step3` (default 5%).
  - `UpdateFood` prefix reimplements vanilla's periodic (10s) food-regen tick, applying the same stack ladder (vanilla summed `m_foodRegen` raw across all stacks with no diminishing returns, over-healing with big stacks).
  - **Sequential stack decay via snapshot-and-restore ("freeze"), not a transpiler**: vanilla's own `UpdateFood` decays *every* entry in `___m_foods` unconditionally each ~1s tick (`food.m_time -= 1f` over the whole list, confirmed by decompile) — fine for vanilla's one-item-per-slot model, but wrong once multiple stacks of the *same* prefab share a slot, since it made every stack expire in lockstep instead of one at a time. Rather than transplicing vanilla's decrement loop, a second prefix/postfix pair on the SAME `Player.UpdateFood` method brackets it: the prefix groups `___m_foods` by resolved prefab name, identifies the LAST (highest list-index) entry in each group as "active" (this works with zero extra state, because `EatFood` only ever `.Add()`s and vanilla's own expiry-removal is an ordinary order-preserving `List.Remove` — so within a same-prefab group, highest index is always the most-recently-eaten survivor), and snapshots every OTHER entry's `m_time`/`m_health`/`m_stamina`/`m_eitr` into a small reusable buffer. The postfix then writes those snapshotted values straight back, undoing whatever vanilla's decrement (and possible removal) just did to every non-active entry. Net effect: only the active entry actually ticks down; when it expires and vanilla removes it, the very next tick's prefix naturally recomputes a new "last" survivor as active — no queue, no promotion code, no persistent per-item state at all, it falls out of "which entry currently has the highest index" being recomputed fresh every tick. Accepted tradeoffs, both cosmetic/negligible and explicitly not worth closing with a transpiler: (a) a frozen entry's contribution is understated by a fixed non-growing ~1-tick offset in one internal nested call inside vanilla's own method body (invisible to the HUD/any postfix, which only observe the fully-restored value), and (b) a stack displaced from active→frozen with under 1 tick of `m_time` left can still expire on that same transition tick.
  - **This "freeze via snapshot-then-restore" technique generalizes to any vanilla per-tick loop you want to selectively exempt certain elements from, without a transpiler**: cache the pre-call state of the elements you want unaffected in a prefix, let the original method run completely untouched, then overwrite just those elements back to their cached state in a postfix. Cheaper to write and far more resilient to game updates than patching vanilla's IL directly, at the cost of one tick's worth of transient "wrong" state existing only inside the original method's own call frame.
  - **Stat-only drinks** (zero-food-value meads): a Postfix on `Player.ConsumeItem` detects this class of item and calls `__instance.EatFood(item)` manually after the vanilla consume succeeds.
  - **Tasty Mead stacking / "Drunk"**: a Prefix on `Player.CanConsumeItem` narrowly exempts `MeadTasty` from vanilla's same-effect-category refusal, replicating vanilla's other checks so the exemption can only loosen the same-effect gate.
  - **Puke fix**: a Prefix on `Player.RemoveOneFood` clears the whole `___m_foods` list at once (vanilla's puke effect removes one item per second, tuned for a 3-slot stomach).
- **How to implement (step-by-step recipe):**
  1. Centralize "resolve the canonical item key" into one function with clear fallback priority and use it *everywhere* an item identity is needed.
  2. Replace the vanilla "can I do X" and "do X" methods via Harmony `[HarmonyPrefix]` returning `false` to fully take over, always wrapped in try/catch that falls back to `return true` on any exception.
  3. For any vanilla method that sums per-stack/per-item values and that other mods might also patch, use the "stash empty → run original → restore + add delta" bracketing technique with `HarmonyPriority.Last` on the prefix and `HarmonyPriority.First` on the postfix plus a `[HarmonyFinalizer]` restore as a safety net against exceptions.
  4. Compute a single "contribution" struct/record per stacked item, shared by every consumer.
  5. Implement diminishing returns as a small lookup indexed by "which stack number is this" (0-based), backed by a `Dictionary<name,count>` counter reset every evaluation pass.
  6. For invented durations on items with no natural timer, apply a **floor**, not an override.
  7. Where vanilla gates two independent concerns behind one check, patch the check narrowly by item identity so only the specific case you need is exempted.
- **Reusable pattern/snippet (method-bracketing to compose safely with unknown other Harmony patches):**
```csharp
[HarmonyPatch(typeof(Player), "SomeSummingMethod")]
[HarmonyPrefix, HarmonyPriority(Priority.Last)]
static void Prefix(List<Item> ___items) {
    if (_stashed || items.Count == 0) return;
    _stash.Clear(); _stash.AddRange(___items); ___items.Clear(); _stashed = true;
}
[HarmonyPatch(typeof(Player), "SomeSummingMethod")]
[HarmonyPostfix, HarmonyPriority(Priority.First)]
static void Postfix(ref float total, List<Item> ___items) {
    Restore(___items);          // "total" now = everyone ELSE's contribution, untouched
    total += MyOwnDelta(___items);
}
[HarmonyPatch(typeof(Player), "SomeSummingMethod")]
[HarmonyFinalizer]
static void Finalizer(List<Item> ___items) => Restore(___items); // safety net if original throws
```

---

### HUD System — 5-Slot Food Bar (Steady Draw + Runtime Slot Expansion)
- **Purpose:** Replaces vanilla's flashing/pulsing 3-slot food bar with a steady-colored, non-pulsing bar expanded (best-effort, at runtime, by cloning existing UI elements) to 5 slots, groups multiple stacks of the same food behind a single icon with an "xN" badge or segmented bar, and shows a countdown based on the longest-lived stack rather than vanilla's per-entry decay. **This is the HUD system called out as a standout feature.**
- **Key files:** `Patches/HudPatches.cs`, `Patches/FoodBreakdownUI.cs` (segmented bar add-on)
- **Architecture:**
  - `[HarmonyPatch(typeof(Hud), "Awake")] Postfix` — reads vanilla's private food-bar arrays via Harmony's `___fieldName` reverse-patch-arg convention. If the arrays are usable, it **runtime-clones** extra UI elements to grow from vanilla's slot count up to `TargetSlots = 5`: `TryExpand` differentiates two possible hierarchy shapes, clones accordingly via `Object.Instantiate` + re-deriving anchored-position offsets from the delta between existing slot 0 and slot 1. Every failure path logs a specific reason via `Bail()` and degrades gracefully — if expansion fails, the mod still takes over drawing the original slot count with steady (non-pulsing) colors.
  - Every early-out / failure is explicitly logged — "Feature Lost - X. Reason: {ex}" used to make user bug reports self-diagnosing from their log file alone.
  - `[HarmonyPatch(typeof(Hud), "UpdateFood")] Prefix` returning `false` fully replaces vanilla's per-frame draw. It groups `player.GetFoods()` entries by resolved prefab name into pooled `FoodGroup` objects, tracks `Count`, `MaxTime` (what the countdown shows — vanilla decays every stomach entry in lock-step, so a stack's slot survives exactly until its longest-remaining member expires) and `TotalTime`. For each of the (possibly expanded) slots it sets icon sprite/color (`Color.white`, i.e. steady — this is what removes vanilla's `Mathf.Sin(Time.time...)` pulse), toggles active state, and shows either an "xN" text badge or leaves room for the segmented bar.
  - A second `[HarmonyPatch(typeof(Hud), "UpdateStatusEffects")] Postfix` hides the vanilla `"TimeBar"`/`"Cooldown"` child elements specifically for Fatty's own status-effect icons (matched by hash) — surgically disables just that one child transform for just Fatty's effect hashes.
  - `TMP_Text` creation is centralized in `HudPatches.CloneText` — **never** `AddComponent<TextMeshProUGUI>()` on a bare GameObject, because that produces text with no font asset. Always `Object.Instantiate` a live vanilla `TMP_Text` template and clear its text.
  - The F8 hotkey toggle for the Feast Ledger panel is polled inside this same `UpdateFood` prefix, gated by the same input-focus checks vanilla itself uses.
- **How to implement (step-by-step recipe):**
  1. Harmony-patch the vanilla HUD's `Awake` (postfix) to grab its private icon/bar/time arrays via `___fieldName` parameters, and its per-frame update method (prefix, `return false`) to take over drawing.
  2. To expand a fixed-size vanilla array of UI slots at runtime: detect the layout shape by comparing `transform.parent` identity between existing slot elements, `Object.Instantiate` a clone of slot 0 under the same parent, derive per-slot spacing from `(slot1.anchoredPosition - slot0.anchoredPosition)`, and apply `slot0Position + delta * newIndex` to each new clone. Always fall back gracefully.
  3. To remove a "flashing/pulsing" vanilla visual, find where vanilla derives color/alpha from `Time.time`/`Mathf.Sin` and simply assign a constant color in your replacement draw call.
  4. Never create bare `TMP_Text`/`TextMeshProUGUI` components — always clone an existing live template.
  5. To hide part of a vanilla-cloned UI row only for *your* content: patch the vanilla per-row update method as a postfix, identify "is this my row" via a stable identity check, and `SetActive(false)` only the specific child `Transform` you don't want.
  6. Poll hotkeys from inside a frequently-running Harmony patch using `KeyboardShortcut.IsDown()`, gated behind the same "is a text input / menu currently focused" checks vanilla itself performs.
- **Reusable pattern/snippet (deriving slot spacing from two existing UI elements to clone a third):**
```csharp
Vector2 delta = (next.transform as RectTransform).anchoredPosition
              - (template.transform as RectTransform).anchoredPosition;
var clone = Object.Instantiate(template.gameObject, template.transform.parent);
(clone.transform as RectTransform).anchoredPosition =
    (template.transform as RectTransform).anchoredPosition + delta * slotIndex;
```

---

### IMGUI Floating Debug/Inspection Panel ("Feast Ledger")
- **Purpose:** A draggable, resizable, toggle-able on-screen panel (stomach contents breakdown, milestone progress, lifetime feast log), built in IMGUI rather than uGUI.
- **⚠️ RETRACTION (2026-08-04):** this entry used to justify that choice by claiming "Valheim's HUD canvas has no `EventSystem`/`GraphicRaycaster` support for interactive elements". **That is false and was never sourced.** A live dump reports `GraphicRaycaster: present` on the HUD canvas — see `UI-DUMPS/README.md`. IMGUI is still a defensible choice here for the reasons below (no canvas/prefab plumbing, `Event.current` is always readable, `GUI.Window` gives dragging for free), but "uGUI cannot receive input on the HUD canvas" is not one of them. Do not build on it.
- **Key files:** `Patches/FeastLedgerGui.cs`, hook point `Plugin.cs` (`FattyPlugin.OnGUI`)
- **Architecture:**
  - Uses **legacy Unity IMGUI** (`OnGUI`, `GUILayout`, `GUI.Window`, `Event.current`) rather than uGUI, specifically because IMGUI reads `Event.current` directly every call regardless of any Canvas/EventSystem/Raycaster state.
  - Hook point: `BaseUnityPlugin` is itself a `MonoBehaviour`, so it gets its own `OnGUI()` callback for free.
  - `Draw()` computes a `Rect` (open = full size, closed = just a title strip) and calls `GUI.Window(id, rect, DrawWindow, GUIContent.none, style)`. `GUI.DragWindow` is scoped to a sub-rect (title bar minus the toggle button) so dragging and clicking the toggle button never fight for the same input event.
  - Resizing is hand-rolled: a grip box drawn *outside* `GUI.Window`'s own coordinate space, in screen space, with manual `Event.current.type` checks, clamped to min/max width/height, calling `Event.Use()` to consume the event.
  - Panel position/size are persisted to unsynced config entries, written every frame the rect changes.
  - Styling: cached `GUIStyle` objects built once, including a `Texture2D`-per-solid-color background trick.
  - Content sections are collapsible via plain `GUILayout.Button` acting as a foldout toggle (arrow glyph `▶`/`▼` prefixed) — no dedicated foldout widget exists in IMGUI.
- **How to implement (step-by-step recipe):**
  1. For a debug/utility panel needing real mouse interaction (drag, resize, buttons) on top of a HUD that wasn't designed for interactive elements, prefer legacy IMGUI over a uGUI Canvas hierarchy layered onto the game's existing HUD canvas.
  2. Implement `OnGUI()` on your plugin's `BaseUnityPlugin` and call into your panel's static `Draw()` from there, wrapped in try/catch.
  3. Use `GUI.Window(id, rect, callback, content, style)` for a draggable window shell; use `GUI.DragWindow(subRect)` scoped to exclude any clickable controls in the title bar.
  4. For resize handles, draw a grip `Rect` outside the window's local coordinate space and manually track `Event.current` mouse down/drag/up against it.
  5. Persist position/size to unsynced config entries.
  6. Build and cache all `GUIStyle`s once in a lazy-init method; use the `Texture2D(1,1)` + `SetPixel`/`Apply` trick for solid-color backgrounds.
  7. Implement collapsible sections as a `bool` toggled by a `GUILayout.Button` styled to look like a label.
- **Reusable pattern/snippet:**
```csharp
void OnGUI() {
    Rect r = GUI.Window(myId, _rect, DrawWindow, GUIContent.none, _windowStyle);
    _rect.x = r.x; _rect.y = r.y;              // always take position back
    if (_open) { _rect.width = r.width; _rect.height = r.height; HandleResize(); }
}
void DrawWindow(int id) {
    GUI.DragWindow(new Rect(0, 0, width - toggleBtnWidth, titleHeight)); // exclude the toggle button
    GUILayout.BeginArea(...); GUILayout.BeginScrollView(...);
    /* content */
    GUILayout.EndScrollView(); GUILayout.EndArea();
}
```

---

### Segmented Stack Bar (uGUI decorative overlay)
- **Purpose:** An alternative, purely decorative visualization to the "xN" text badge — a thin multi-segment bar under each food icon, one segment per stacked item, each segment's color lerped from dim to bright based on that item's remaining-time fraction.
- **Key files:** `Patches/FoodBreakdownUI.cs`
- **Architecture:** Built once per HUD slot in `BuildSlotExtras`: creates a container `GameObject` with a `RectTransform` anchored along the bottom of the icon, then `MaxSegments = 8` child `Image` objects each occupying an even horizontal slice — a plain `Image` with no sprite renders a flat-colored quad, so it needs no font/raycaster setup. Updated every frame from the same `HudPatches.FoodGroup`/`FoodContribution` data the main HUD draw already computed, and is mutually exclusive with the "xN" badge via a config flag.
- **How to implement:** Build a container `RectTransform` anchored to a stretch region of a parent icon; create N evenly-spaced child `Image` components (no sprite = flat color quad) with a small gap between them; toggle each segment's `activeSelf` based on stack count, and `Color.Lerp(dim, bright, fraction)` each visible segment's color.

---

### Regen Scaling System
- **Purpose:** Vanilla stamina and eitr regeneration are **flat absolute rates** (~5 points/sec) that don't scale with pool size, so a mod that enlarges max stamina/eitr (as Fatty's bigger stomach does, ~3x vanilla) makes the bar refill proportionally slower — "actively worse than vanilla." This system rescales the regen-rate fields so refill *time* (percentage-wise) stays at vanilla parity, never below it.
- **Key files:** `Regen/RegenScaling.cs`, called from `Patches/PlayerPatches.cs` (`GetTotalFoodValue_Postfix`)
- **Architecture:**
  - `Apply(player, stamina, refStamina, eitr, refEitr)` is called once per `GetTotalFoodValue` tick with Fatty's actual max pools alongside "what vanilla's own slot rules would have granted" reference pools.
  - Per-player baseline state is tracked via `ConditionalWeakTable<Player, Baseline>` (weak-keyed so a destroyed `Player` doesn't leak) storing `StaminaBase`/`EitrBase` (the rate to scale FROM) and `StaminaWritten`/`EitrWritten` (what Fatty itself last wrote).
  - **Re-baselining every call, not caching once**: before applying a new scale, it checks `if (!b.Has || !Mathf.Approximately(player.m_staminaRegen, b.StaminaWritten)) b.StaminaBase = player.m_staminaRegen;` — i.e., if the live field no longer equals what Fatty itself last wrote, some *other* mod has since written it, and Fatty adopts that new value as the base to scale from rather than clobbering it. This is a general pattern for coexisting with other mods that also own the same field.
  - `Scale(pool, reference, cap) = Clamp(pool / reference, 1f, cap)` — never scales below 1.0, capped by config `maxRegenScale` (default 4x), and returns 1.0 (no-op) when there's no reference pool to compare against.
  - `Restore(player)` puts the base rate back when the feature is toggled off mid-session, but *only if* the field still holds Fatty's own last-written value.
- **How to implement (step-by-step recipe):**
  1. Identify the vanilla flat-rate field(s) driving a per-tick regen/recovery calculation.
  2. Compute two totals in parallel wherever your mod expands the underlying pool: your actual (expanded) total, and a "reference" total representing what vanilla's own unmodified rules would have produced.
  3. Store per-entity baseline state in a `ConditionalWeakTable<TEntity, TState>` keyed by the entity instance, so state is automatically GC'd when the entity is destroyed.
  4. On each application, detect whether the live field still equals what you last wrote; if not, adopt the new value as your baseline rather than overwriting it blindly.
  5. Scale the baseline rate by `Clamp(yourPool / referencePool, 1f, safetyCapFromConfig)` so the effect is always "at least as good as vanilla, capped for safety."
  6. Provide a config toggle to disable the feature entirely, restoring the baseline only if you still own the field.

---

### Progression: Lifetime Food Milestones (Six Independent Permanent Stats)
- **Purpose:** Eating a large cumulative number of items from a diet category over a character's lifetime permanently raises a stat *matched to that category's flavor* (Meat→carry weight, Sweet→stamina, Eitr→eitr, Fish→swim speed, Vegetable→all-round regen), plus a sixth, category-independent "Fatness" axis (total items eaten across every category, including unidentified food) that grants permanent base max HP. Started as HP-only for every category; generalizing it surfaced three distinct techniques worth reusing depending on what kind of field the target stat has.
- **Key files:** `Progression/FoodMilestones.cs`, `Progression/MilestoneStore.cs`, `Progression/MilestonePerks.cs`
- **Architecture:**
  - Persistence (`MilestoneStore`): per-category eaten counts stored in `Player.m_customData` (a public `Dictionary<string,string>` vanilla already serializes and — crucially — is **not** cleared by `Player.ResetCharacter()`, unlike skills/recipes/known-texts). Serialized as one compact delimited string, format `"Meat=142,Vegetable=88,Fish=31"`. This is a reusable "cheap durable per-character key/value store with zero save-patch code" pattern. The "Fatness" total is NOT stored separately — it's just the sum of every value already in this same dictionary (including "Unknown"), computed on demand.
  - `FoodMilestones.RecordEaten(player, category)` increments the stored count, calls a single `Apply(player)` that unconditionally recomputes and rewrites every one of the six axes from the stored counts (idempotent, not incremental — needed anyway since `OnSpawned` must do the same full recompute), then separately checks whether this bite just crossed a NEW tier (per-category, and independently for the Fatness total) purely to decide whether to show a toast, so display/announcement logic never has to duplicate the bonus math.
  - Tier thresholds (3 tiers) are computed via `TiersEarned(eaten)`, which explicitly **sorts** the three configured thresholds ascending before comparing so a server admin misconfiguring tier1 > tier2 doesn't award tiers out of order. The Fatness axis uses a structurally identical `FatnessTiersEarned` against its OWN, separate (and larger) threshold set — reusing the general-category thresholds would make it trivially maxed out, since a total-across-5-categories counter climbs roughly 5x faster than any single category's.
  - **Three different application mechanisms, picked per-target-field, all funneled through the same `Apply(player)`:**
    1. **Direct base-field write** (HP, Stamina, Carry Weight): `player.m_baseHP`/`m_baseStamina`/`m_maxCarryWeight` are all fields the decompiled vanilla assembly only ever *reads* elsewhere (confirmed via decompile: `GetTotalFoodValue` seeds `hp=m_baseHP; stamina=m_baseStamina`, and `GetMaxCarryWeight()` reads `m_maxCarryWeight` then applies SEMan modifiers *on top* via `ModifyMaxCarryWeight`) — so overwriting any of them is safe, additive with any status-effect modifier on the same stat, and flows through vanilla's own downstream calculation for free. Because these are prefab-serialized fields that reset on every new `Player` instance, the pristine (un-bonused) value for EACH of the three is captured exactly once (`_capturedBase` guard covers all three together) and every later `Apply` writes `pristine + bonus`, never `current + bonus`.
    2. **Delta injection into an existing postfix** (Eitr): vanilla has **no base-eitr field at all** — `GetTotalFoodValue` hardcodes `eitr = 0f` before summing food (confirmed via decompile), so there's nothing to write directly. Instead, `FoodMilestones.GetEitrBonus(player)` is called once, unconditionally (not per-stack), from inside `PlayerPatches.GetTotalFoodValue_Postfix`, and added straight into the `eitr` out-param — the exact same injection point an existing feature (`DrinkBuffs.GetMaxHealthBonus`) already used for a *temporary*, drink-gated max-HP bonus; the only difference for a *permanent* bonus is it's added once outside the per-food loop instead of once per qualifying stack inside it.
    3. **Always-on tiered status effects, one per tier** (Fish swim speed, Vegetable all-round regen): for stats with neither a writable base field nor a `GetTotalFoodValue` hook (a flat swim-speed modifier, a regen-rate multiplier), the target becomes `SE_Stats` — the SAME tool the mod's diet-driven Synergy system already uses, but driven by a PERMANENT counter instead of current stomach contents. Key design choice: **one separate `SE_Stats` ScriptableObject per tier** (3 objects per stat) rather than one shared object whose field gets rewritten with a bigger number as more tiers are earned — a single shared ScriptableObject instance can't hold a different magnitude per player in multiplayer (SEMan's own per-player active-effect list is what actually needs to differ), and Valheim already combines multiple simultaneously-active `SE_Stats` correctly on its own — so simply adding/removing each tier's own fixed-magnitude effect via `SEMan.AddStatusEffect`/`RemoveStatusEffect` based on `tiersEarned >= tierNumber` gets cumulative stacking for free, no extra math needed. **How they combine is NOT what the field names suggest** (verified against the decompile, corrected 2026-08-01 — this doc previously claimed regen multipliers multiply, which is wrong): `SE_Stats.ModifyHealthRegen`/`ModifyStaminaRegen`/`ModifyEitrRegen` are all `if (m > 1f) { mult += m - 1f; } else { mult *= m; }`. So regen values **above 1 are ADDITIVE**, and only the `<= 1` branch multiplies. Three veg tiers at 1.02/1.04/1.06 total exactly +12% (`1 + .02 + .04 + .06`), not `1.02*1.04*1.06`. The `<= 1` branch is also precisely why leaving a regen field at its `0` default would wipe the stat rather than do nothing.

- **⚠️ `SEMan.AddStatusEffect` stores a `Clone()`, so the add/remove pattern alone leaves permanent effects stale.** Decompile: `StatusEffect statusEffect3 = statusEffect.Clone(); m_statusEffects.Add(statusEffect3);` — the player carries a *copy* snapshotted at add-time, not the ObjectDB template. Any later mutation of the template (e.g. an `ApplyConfigValues()` refresh after ServerSync delivers the server's values) therefore **never reaches an effect that is already active**. Diet-driven effects mask this completely — they're added/removed constantly, so each re-add re-clones current values within seconds — which is exactly why the bug survives code review in a codebase whose synergy system is the reference pattern. It bites only where the same pattern is reused for an **always-on** effect that is granted once and never removed: on a server, config sync typically lands *after* the effect is active, so the client silently runs its own local values for the rest of the session despite the setting being server-synced. Fix: re-read config and copy the tunable fields template→live on every sync pass, resolving the live instance with `seman.GetStatusEffect(hash)`. Copy an explicit field list, **not** a wholesale re-`Clone()` — the live instance also holds per-player runtime state (`m_time`, `m_character`) that must not be reset just because a config value moved.
  - **Respawn correctness gotcha for tiered status effects**: a fresh `Player` instance gets a fresh `SEMan` with zero active effects, exactly like `m_baseHP` resetting to 25 — so a lifetime-earned tier's status effect must be re-synced on EVERY respawn (`OnSpawned` → `Apply` → `MilestonePerks.Sync`), not just re-applied when a new tier is crossed, or an already-earned perk silently vanishes on death until the next matching bite.
  - **Toast-spam gotcha**: `SE_Stats.m_startMessage` fires automatically on every inactive→active `AddStatusEffect` transition — which, combined with the respawn-resync above, means a lifetime perk would otherwise re-announce itself on every single death. Fix: leave `m_startMessage` unset on all always-on tiered effects, and route the one genuine "you just earned this" announcement through the category's own tier-crossing detection in `RecordEaten` instead (which only fires on a true first-cross, never on respawn) — giving flat-field and status-effect axes alike a single, consistent messaging path.
  - **Follow-up refinement: alternating reward types within one axis's 3 tiers.** Five of the six axes (all but Vegetable) were changed so tier 1 and tier 3 grant the ORIGINAL flat/base-field reward, but tier 2 grants a DIFFERENT, category-flavoured reward instead of a third helping of the same one (e.g. Eitr: flat eitr, then eitr regen%, then flat eitr again). Implementation-wise this composes cleanly with the three mechanisms above rather than needing a fourth: the flat-total functions (`CarryWeightBonus`/`StaminaBonus`/`EitrBonus`/`FatnessBonus`) simply sum ONLY tiers 1 and 3 (`(tiers>=1?ForTier(1):0) + (tiers>=3?ForTier(3):0)`, skipping 2 entirely), while tier 2's alternate reward becomes its OWN single always-on `SE_Stats` effect (mechanism 3 above, but just ONE effect instead of three, since only one tier ever grants it — gated on a plain `tier >= 2` boolean, not summed/stacked). A single `DescribeTierAward(category, tier)` function became the one place that knows "what does tier N of category X actually grant" — both the in-game toast and the debug-panel display call it, so the two can never independently drift out of sync the way two hand-written format strings could.
- **How to implement (step-by-step recipe):**
  1. Find a persistent, per-character (not per-world) dictionary field vanilla already saves/loads unconditionally and doesn't clear on "reset" — that gives you free save/load with zero patches.
  2. Serialize your counts as one compact string into a single dictionary entry. A cross-category "total" derived stat needs no separate storage if it's just the sum of values already in the same dictionary.
  3. Increment counts at the single authoritative point where the triggering action is known to have succeeded, then call one idempotent `Apply()` that fully recomputes every derived stat from the stored counts — reuse that same `Apply()` from both the increment path and the respawn path, rather than writing two divergent code paths.
  4. Only recompute/re-announce a *toast* when a threshold was actually just crossed; the underlying stat recompute in `Apply()` should stay unconditional and idempotent regardless.
  5. Sort/validate configurable thresholds defensively, and give any "total across everything" axis its own separate, larger threshold set rather than reusing per-category thresholds.
  6. Pick your application mechanism per target field: a vanilla field that's write-once-safe (only ever read elsewhere) can be written directly with a captured-pristine-value pattern; a stat with no such field but an existing per-tick recompute postfix can receive a one-time delta injected into that postfix; a stat reachable only through a status-effect field needs one always-on `SE_Stats` PER TIER (not one shared, rewritten object) so multiplayer per-player magnitude and natural cross-tier stacking both fall out for free.
  7. If a target field resets on respawn/relogin (prefab-serialized fields, or a fresh SEMan), capture pristine values once and re-apply the FULL bonus (not just re-add a delta) on every spawn event — and for status effects specifically, leave `m_startMessage` unset and route the actual "you earned this" announcement through your own tier-crossing detection instead, or a permanent unlock will re-toast itself on every death.

---

### Progression: Lifetime Feast Log (Per-Item Eaten Counter)
- **Purpose:** A separate, finer-grained lifetime counter than the milestone system — tracks exact eaten counts **per prefab** (e.g. "CookedMeat x142") rather than per category, purely for display in the Feast Ledger panel; carries no gameplay effect.
- **Key files:** `Progression/FoodLifetimeLog.cs`
- **Architecture:** Structurally identical to `MilestoneStore` (same `Player.m_customData` persistence approach), under a different key. `RecordEaten(player, prefabName)` called alongside `FoodMilestones.RecordEaten`. Displayed in `FeastLedgerGui.DrawFeastLogSection`, sorted descending by count.
- **How to implement:** Duplicate the MilestoneStore pattern with a different key and a different granularity of counting key; no new persistence mechanism needed since it reuses the same customData dictionary slot pattern.

---

### Synergy System (Dynamic StatusEffect Registration + Diet-Driven Add/Remove)
- **Purpose:** Four passive buffs granted while the stomach's current contents satisfy a recipe (e.g. "Balanced Diet" = ≥1 Meat + ≥1 Vegetable simultaneously in the stomach) — each is a normal Valheim `StatusEffect` (specifically `SE_Stats`) that's added/removed every tick based on whether the recipe is currently satisfied, rather than being a fire-and-forget timed buff.
- **Key files:** `Synergies/SynergyManager.cs`, `Synergies/SynergyEffects.cs`, `Synergies/SynergyIcons.cs`
- **Architecture:**
  - **Effect creation**: `SE_Stats` instances are built via `ScriptableObject.CreateInstance<SE_Stats>()` — built entirely in code. Regen multiplier fields (`m_healthRegenMultiplier`, `m_staminaRegenMultiplier`, `m_eitrRegenMultiplier`) and `m_damageModifier` are explicitly baselined to `1f` on creation — a documented Valheim gotcha: `SE_Stats` treats these multiplicatively when ≤ 1, so a default of `0` would zero the stat entirely.
  - **ObjectDB registration**: effects must be added to `ObjectDB.m_StatusEffects` to be resolvable by hash at runtime. Registration is patched onto **both** `[HarmonyPatch(typeof(ObjectDB), "Awake")]` (the main-menu DB) **and** `[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]` (the full in-game DB copy that happens on world load) — the second hook exists because `CopyOtherDB` rebuilds the status-effect list from scratch and would silently drop custom-added effects otherwise.
  - **Config-value refresh timing gotcha**: `ObjectDB.Awake` fires *before* ServerSync has delivered the server's synced config values on connect. Fix: config-derived fields are re-applied in a separate `ApplyConfigValues()` call, invoked from `Register()` on **every** call (both Awake and CopyOtherDB).
  - **Diet evaluation** (`SynergyManager.Player_UpdateFood_Postfix`, patched onto `Player.UpdateFood` postfix): counts current stomach contents by category, including a synthetic pseudo-category `"Drink"`. Evaluates each of the 4 recipes as a boolean AND of category-count thresholds, then calls `ApplySynergy(seman, hash, qualified, remainingTime)` per effect.
  - **Countdown display trick**: since these effects use `m_ttl = 0` (infinite), but the mod knows exactly when the recipe will stop being satisfied, `PublishRemaining` fetches the **live** effect instance and writes `live.m_ttl = remaining + live.GetDuration()`, exploiting the fact that vanilla's `GetIconText()` renders `m_ttl - GetDuration()` while `IsDone()` stays false the whole time.
  - `NthLargestRemaining(foods, matchPredicate, need)`: since vanilla decays every stomach entry simultaneously, a category's count drops below a required threshold exactly when the *N-th longest-lived* qualifying entry expires — computed by collecting matching `m_time` values, sorting descending, and indexing `[need-1]`.
- **How to implement (step-by-step recipe):**
  1. Create status effects entirely in code: `ScriptableObject.CreateInstance<SE_Stats>()`, baseline any multiplicative fields to their neutral value (1, not 0).
  2. Register into the game's effect database on every relevant lifecycle hook that (re)builds or could clear that database.
  3. Guard registration with an existence check keyed by the same hash your runtime code will use to add/remove the effect.
  4. If any effect field is config-driven, separate "build" from "apply config," and call the stamping function on every registration pass.
  5. Drive presence with a plain per-tick boolean evaluation (`HaveStatusEffect` / `AddStatusEffect` / `RemoveStatusEffect`) rather than a timed grant.
  6. For infinite-duration effects that still need a meaningful on-screen countdown, compute the actual "time until this will stop being true" externally and write it into the **live instance's** ttl field.
- **Reusable pattern/snippet:**
```csharp
static SE_Stats CreateEffect(string internalName, string displayName) {
    var se = ScriptableObject.CreateInstance<SE_Stats>();
    se.name = internalName; se.m_name = displayName; se.m_ttl = 0f; // infinite, externally driven
    se.m_healthRegenMultiplier = se.m_staminaRegenMultiplier = se.m_eitrRegenMultiplier = 1f; // neutral!
    se.m_damageModifier = 1f; // neutral multiplier, NOT 0
    return se;
}
void Register(ObjectDB odb) { if (odb.GetStatusEffect(se.NameHash()) == null) odb.m_StatusEffects.Add(se); ApplyConfigValues(); }
bool active = seman.HaveStatusEffect(hash);
if (qualified && !active) seman.AddStatusEffect(hash, true);
else if (!qualified && active) seman.RemoveStatusEffect(hash, false);
```

---

### Drink Buffs + "Drunk" System
- **Purpose:** Whichever drink category currently occupies the single drink slot grants a themed buff (Arcane Draught for Eitr drinks, Warrior's for Meat, Reveler's for Sweet, Seafarer's for Fish) lasting exactly as long as that drink remains in the stomach; separately, stacking enough of vanilla's generic "Tasty Mead" triggers a "Drunk" state (damage/stagger bonus, blocking penalty).
- **Key files:** `Synergies/DrinkBuffs.cs`, invoked from `Synergies/SynergyManager.cs`
- **Architecture:** Structurally the same ObjectDB-registration + per-tick add/remove pattern as the core Synergy system, applied to a `Dictionary<string /*category*/, SE_Stats>`. `Evaluate` walks the current stomach, finds the first drink-slot item, resolves its diet category, and activates exactly the one buff mapped to that category. Gotchas: `m_damageModifier` is multiplicative (`1f + bonusFraction`, not the raw fraction), and `m_skillLevel2`/`m_skillLevelModifier2` are dead unless `m_skillLevel` (slot 1) is also set. "Drunk" uses its own independent stack-count threshold rather than reusing the general stack cap.
- **How to implement:** Follow the general Synergy System recipe, but key your effect set by a category/type lookup instead of fixed named fields, and evaluate "which one is active" as "find the single qualifying slot/item, map its category, activate only that one" each tick. When porting multiplicative-vs-additive stat fields from a decompile, always verify sign/scale by testing in-game rather than assuming.

---

### Procedural Icon Generation + Embedded Resource Pipeline
- **Purpose:** Provides valid icons for every custom status effect without requiring the mod to ship external image files — checks for an embedded PNG first, and falls back to a procedurally-rasterized colored badge (circle + simple vector emblem) drawn entirely in code if no PNG was provided.
- **Key files:** `Synergies/SynergyIcons.cs`, `Fatty.csproj` (`<EmbeddedResource Include="Icons\*.png" />`), `Icons/*.png`
- **Architecture:**
  - PNGs dropped in `Icons/` are embedded directly into the compiled DLL via the csproj `EmbeddedResource` item group.
  - `SynergyIcons.Resolve(baseName, proceduralFallbackFunc)`: tries `LoadEmbedded(baseName)` first, falls back to invoking the procedural function on any failure/absence — every failure point wrapped in try/catch + warning log.
  - PNG decoding uses `UnityEngine.ImageConversion.LoadImage` **via reflection**, because that type lives in `UnityEngine.ImageConversionModule`, which targets a newer netstandard than this project's `net48` TFM can directly reference.
  - **Alpha-flattening gotcha**: Valheim's status-effect icon slot template draws the sprite over a light/parchment-colored background; `FlattenAlpha(tex, bgColor)` manually composites every pixel onto a fixed dark backdrop color **in code**, producing a fully opaque texture.
  - Procedural icons (`Build(baseColor, Emblem)`) rasterize a 64×64 `Texture2D` pixel-by-pixel: outer transparent-to-background falloff, a gold ring, a shaded inner disc, and a simple vector "emblem" selected by an `enum Emblem` and tested via simple analytic geometry against normalized `[-1,1]` coordinates.
  - `Sprite.Create(tex, rect, pivot(0.5,0.5), pixelsPerUnit)` converts the finished `Texture2D` into a usable `Sprite`.
- **How to implement (step-by-step recipe):**
  1. Add `<EmbeddedResource Include="YourFolder\*.png" />` to the csproj so art assets compile into the DLL.
  2. To load one at runtime: find the manifest resource entry ending in your expected filename, copy to a `MemoryStream`, get the byte array.
  3. If your mod targets an older TFM than the Unity API you need, resolve the type/method via `AccessTools.TypeByName`/`AccessTools.Method` and invoke via reflection.
  4. If icons will be composited onto a non-transparent game UI background, flatten alpha yourself against a fixed backdrop color before calling `Sprite.Create`.
  5. For a code-only procedural fallback, rasterize a small square texture pixel-by-pixel using simple analytic shape tests against normalized coordinates.

---

### Procedural 9-Slice Frame Ornamentation (IMGUI, No Art Assets) — SUPERSEDED, kept as the lightweight option
- **Superseded 2026-07-30** by the richer ported theme below (`GiltFrameTheme.cs`) once the user asked for a specific higher-fidelity look — `Patches/GoldFrameStyle.cs` (described below) was deleted from Fatty. Left documented here because it's still the right, much cheaper choice when a full acanthus-flourish frame is overkill: a single distance-function ring + one mirrored corner medallion, versus the ported theme's coverage/height-buffer painter with a dozen ornament primitives. Pick based on how much visual weight the panel actually needs.
- **Purpose:** Give an IMGUI panel a decorative gilt picture-frame border (beveled metallic ring + corner medallions) without shipping any external texture asset — IMGUI has no sprite-import pipeline to hang a real 9-slice art asset off, so the whole thing is rasterized at runtime from a distance-function.
- **Key files (as they existed before deletion):** `Patches/GoldFrameStyle.cs`, consumed from `Patches/FeastLedgerGui.cs`
- **Architecture:**
  - **The ring texture is a single small square, expressed purely as a function of distance-to-edge**: for every pixel, `distToEdge = min(x, y, size-1-x, size-1-y)`, then `t = distToEdge / ringThickness` selects a band (dark outline → bright highlight → mid-tone → dark inset groove → fade to fully transparent past the ring's own thickness). Because Unity's `GUIStyle.border` 9-slicing stretches the thin strip of pixels between a texture's corner tiles along whichever axis is the edge's length, and that strip's `distToEdge` formula degenerates to a pure `x`- or `y`-based gradient away from a corner, the SAME per-pixel band logic that defines a correct-looking corner ALSO reproduces the identical band sequence when Unity stretches it into an arbitrary-length straight edge — no separate "edge" vs "corner" art needed, one function does both for free.
  - `GUIStyle.border = new RectOffset(ringThickness, ringThickness, ringThickness, ringThickness)` on a style whose `normal.background` is that texture gives a real 9-sliced draw via a plain `GUI.Box(rect, GUIContent.none, style)` — the transparent interior (everything past `ringThickness`) means the ring can be drawn as a pure overlay AFTER a panel's own content, without hiding anything underneath it.
  - **Corner medallions are a separate, much smaller texture, baked ONCE for a single corner and mirrored into the other three via UV-flip rather than four separate rotated bakes**: `GUI.DrawTextureWithTexCoords(rect, tex, new Rect(flipX?1:0, flipY?0:1, flipX?-1:1, flipY?1:-1))` — negative-width/height UV rects are a standard, cheap way to flip a texture's sample direction without any extra render target or actual pixel duplication.
  - The medallion shape itself is also a pure distance function: Manhattan distance from a fixed local center, modulated by `1 + k·cos(angle·4)` (an angle-dependent "wobble" against the point's polar angle) to turn a plain diamond outline into a 4-petal ornamental shape — a cheap, general trick for turning any distance-based shape into a "flower/star" variant: wrap the distance threshold with a low-frequency cosine of the angle before comparing.
  - Both textures are generated once and cached (`_built` guard), never regenerated per-frame or per-draw-call.
- **How to implement (step-by-step recipe):**
  1. For any bordered/framed IMGUI look, write your border pattern as a pure function of `distToEdge = min(x, y, w-1-x, h-1-y)` over a small square texture — this one function correctly produces both the corners AND (via Unity's 9-slice edge-stretching) the straight edges, with no separate art needed for each.
  2. Fade alpha to 0 past your border's own thickness so the ring can be drawn as a top-level overlay without needing to also own/cover whatever's inside it.
  3. Apply via `GUIStyle.border = new RectOffset(t,t,t,t)` + `normal.background = yourTexture`, drawn with a plain `GUI.Box`.
  4. For repeated corner/edge ornaments, bake ONE orientation and mirror the rest via `GUI.DrawTextureWithTexCoords` with a negative-width/height UV rect, rather than baking multiple rotated textures.
  5. To turn a plain analytic shape (circle/diamond/square distance test) into an ornamental one, multiply its distance threshold by `1 + k·cos(angle·N)` where `angle = atan2(dy,dx)` — cheap N-fold "petal" or "star" variation on any existing distance-based shape test.
  6. Build and cache procedural textures once behind a bool guard; never regenerate them per-frame.

---

### Ported Gilt Picture-Frame Theme (Cross-Project Reuse, Full Fidelity)
- **Purpose:** A complete, high-fidelity "gilt Valheim-native" IMGUI theme — double-rail embossed gold border, mitred acanthus-flourish corners, top/bottom palmette crests, near-black leather panel, Valheim's own body font — ported near-verbatim from a SIBLING project rather than designed from scratch, because the user pointed at that project's existing look and said "that's how I want my UI to work" (style, not mechanics). This is the general lesson: when a look already exists and works in another project under the same author, port the actual file rather than re-deriving the same visual language from a text description of it — faithful reuse beat re-invention here exactly the way porting Njord's IMGUI drag/window pattern literally (rather than "translating" it) already had, earlier in this same mod's history (see the food-transparency-feature notes).
- **Key files:** source `TortalPortal\UI\TortalUITheme.cs` (sibling project, untouched); port `Fatty\Patches\GiltFrameTheme.cs`, consumed from `Patches\FeastLedgerGui.cs`.
- **Architecture:**
  - **Coverage + height buffer painter, baked with ONE global light.** A private `Painter` class accumulates two parallel float arrays per pixel — alpha coverage and a pseudo-height value — from primitive calls (`Disc`, `Lozenge` i.e. a diamond/rhombus falloff, `Taper` i.e. a width-interpolated line of discs, `Bezier`, `Spiral` i.e. a logarithmic spiral stroked from its eye outward, `RailPixel`/`RailElbow` for straight-and-mitred double-line borders). `Bake()` then does a single Sobel-style finite-difference pass over the height buffer to derive a per-pixel "shade" value, and maps that shade through a 2-stop gradient (dark→gold→bright-gold) to get the final embossed color. Doing lighting ONCE, globally, after every shape is already painted is what lets four independently-built straight edges and four MIRRORED corners all agree on where the light source is — mirroring (`MirrorX`/`MirrorY`) and edge-repeating (`Transpose`, a diagonal reflection used to paint one ornament element then reuse it on the adjoining edge) only ever move which pixel a value lands on, never touch the height/shade math, so a mirrored corner is still correctly lit rather than looking lit from the wrong side.
  - **One border-cross-section function serves BOTH corners and straight edges**, same technique as the simpler ring approach above but with two concentric rail lines instead of one: `RailElbow(mid, half, centre)` computes distance-to-centerline for either a straight run (before the bend) or a circular arc (through the bend, sharing the same radius as the straight run's offset so the curve is tangent-continuous) — and a 4-pixel-wide/tall `BuildRail` texture reuses the same per-pixel distance formula, degenerate to the straight-run case, for whatever Unity's 9-slice stretches along a plain edge. Two rails (`OuterMid`/`OuterHalf` and `InnerMid`/`InnerHalf`) painted into the same buffer at different offsets is what produces the "double rail" look from one function.
  - **Ornament placement is deliberately shallow and long** (nothing reaches past ~36px from the edge in the 84px corner tile) specifically so it clears `Body()`'s own content inset (`Band + Pad`) — ornament depth and content-safe-margin are two numbers that must agree, and here they were designed together rather than the ornament being sized independently and the margin worked out after.
  - **`Body(win)`/`TitleBar(win)`/`FooterLine(win)` are the load-bearing layout helpers** — they return already-inset `Rect`s (accounting for `Band` on all sides, `Pad` extra on left/right, `TitleHeight` top, `FooterHeight` bottom) so a consumer never hand-computes a content margin that could drift out of sync with the actual ornament size. Fatty's port added `TitleBar(win)` (not in the original) because TortalPortal's own window is fixed/non-draggable and never needed a title-bar drag-region rect of its own — a `GUI.Window`-based consumer with its own toggle button/drag region needs that extra seam.
  - **Config-driven customization added on port, without touching any geometry**: a single `_fontSizeDelta` int, read once in `EnsureBuilt()` from a `ConfigEntry<int>`, routed through a `Sized(int baseSize) => Mathf.Max(6, baseSize + _fontSizeDelta)` helper that every `Text`/`Patch` style-builder call passes its literal size through — the geometry/ornament code is completely untouched by this, only `BuildStyles()` changed.
  - **Font selection borrows the HOST GAME's own loaded font** rather than bundling one: `Resources.FindObjectsOfTypeAll<Font>()` searched for known Valheim font names (`AveriaSerifLibre`, `Norsebold`, `Norse`), falling back to `null` (which IMGUI itself then resolves to `GUI.skin.font`) if none match — deliberately not `Font.CreateDynamicFontFromOSFont("Arial", ...)`, since Arial stopped being a Unity builtin resource in the version Valheim ships on.
- **How to implement / reuse (step-by-step recipe):**
  1. When a look already exists in another project by the same author, COPY the actual theme file and rename only the namespace/class — don't re-derive the same visual language from a description of it. Verify it has no dependencies on the source project's other types first (this one only needed `UnityEngine`).
  2. If the ported theme assumed a fixed/non-draggable window (`DrawWindow(rect, title)` doing everything in one call) but the new consumer needs interactive chrome (a toggle button, a drag region), split that call into its constituent pieces (`DrawPanelFill`, `DrawFrame`, manual title/rule placement) and lay out the interactive bits using the theme's own exposed constants (`Band`, `TitleHeight`, `Pad`) so they still align with what the frame itself drew.
  3. Always route real screen-space content through the theme's own inset helper (`Body()` or equivalent) rather than a hand-picked margin — the ornament's reach and the content margin are coupled and should have exactly one number in common.
  4. If the ported theme's ornament sizes are fixed pixel constants (not proportional to window size, which is normal for this kind of asset), audit the CONSUMER's own minimum window size against those constants — a smaller minimum than the theme was designed for will make the corner ornaments overlap themselves.
  5. Layer any new config-driven customization behind the theme's existing "build once, cache, guard with a bool/null check" pattern; don't add per-frame reads or you'll be rebuilding lazily-cached `Texture2D`s every `OnGUI` call.

---

### Third-Party Mod Compatibility (Soft Dependency via Reflection)
- **Purpose:** Detect an optional companion mod (`blacks7ar.FoodDurationMultiplier`) at runtime and, if present and functioning, defer to its config values instead of stacking/multiplying with Fatty's own equivalent settings — without taking a hard assembly reference/dependency on it.
- **Key files:** `Compat/FoodDurationMultiplierCompat.cs`
- **Architecture:**
  - Detection: `BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(pluginGuid, out pluginInfo)`. Critically checks `pluginInfo.Instance != null` too, because `PluginInfos` is populated as soon as chainloader discovers the plugin (metadata only), while `Instance` stays null until that plugin's own `Awake()` has actually run.
  - Value access: resolves specific `static` fields on the other plugin's type via `Type.GetField`, then that field's value's own `Value` property via `PropertyInfo` reflection — all done **once and cached**.
  - **Fail-once, not fail-every-time**: a bool is set the first time reflection fails and subsequent calls short-circuit to a safe default instead of re-attempting and re-logging every frame.
  - Semantic compatibility, not just presence detection: Fatty's own duration multiplier is **ignored entirely** while the other mod is installed and working, preventing a double-application gameplay bug.
- **How to implement (step-by-step recipe):**
  1. Never take a hard project reference to an optional companion mod's assembly — detect it via `Chainloader.PluginInfos.TryGetValue(theirGuid, out info)`.
  2. Check `info.Instance != null` before touching anything on it.
  3. Resolve any values you need via reflection, and **cache** the resolved `FieldInfo`/`PropertyInfo`/target-object once.
  4. On first reflection failure, set a "give up" flag and return a safe neutral default from then on, logging the failure exactly once.
  5. Think through what the *combination* of both mods' behavior actually does — specifically watch for two mods each applying their own multiplier/bonus to the same underlying value.

---

### Multiplayer / Networking Notes (No Custom ZDO Sync)
- **Purpose:** Documents why Fatty needs no custom `ZDO` networking despite adding significant new player state.
- **Key files:** `Patches/NetworkPatches.cs`
- **Architecture/rationale:** All food effects are computed and applied **client-side, on the owning player's own client**. The *results* — final max Health/Stamina/Eitr — are pushed through vanilla's own `SetMaxHealth`/`SetMaxStamina`/`SetMaxEitr`, which Valheim already syncs to other clients via the character's `ZDO`, and Fatty's synergy `StatusEffect`s are registered identically in every client's `ObjectDB` and driven from each owner's own diet — so other players correctly see your health/stamina bars and active buffs with zero extra network code. The one accepted cosmetic limitation: vanilla's `ZDO` fields for broadcasting "what food is a remote player eating" only cover 3 slots, so a remote player's 4th/5th food icon may not render for observers — a deliberate tradeoff rather than shipping fragile custom `ZDO` packing for a purely cosmetic detail.
- **How to implement / lesson to reuse:** When expanding a stat/mechanic that already flows through a vanilla stat-sync pathway, prefer computing everything client-side and letting the *existing* sync mechanism carry the result, rather than building custom networking.

---

## Cross-Cutting Conventions Worth Reusing Verbatim Across Mods

1. **"Feature Lost" resilience pattern**: virtually every Harmony patch method and coroutine in Fatty is wrapped in `try { ... } catch (Exception ex) { Log.LogError($"Fatty Mod: Feature Lost - <FeatureName>. Reason: {ex}"); return true /* or other safe fallback */; }`. This means one broken feature degrades that feature alone and logs a clearly-greppable diagnostic line, rather than crashing the whole mod.
2. **Single-source-of-truth resolvers**: any value computed multiple places is centralized into one function used everywhere.
3. **Config re-application over config-baking**: any object built once at an early lifecycle point but needing config values that may arrive later separates "build" from "apply config," and re-invokes "apply config" on every later opportunity.
4. **Throttled diagnostic logging**: `PlayerPatches.LogBlock` throttles identical log lines per (item, reason) key with a cooldown.
5. **`Player.m_customData` as a free persistence layer**: both progression subsystems exploit this vanilla dictionary field for per-character durable storage with zero save/load patch code.
6. **Case-insensitive soft-dependency probes** (1.1.5): every "is mod X loaded?" check goes through `PluginLookup`, never `Chainloader.PluginInfos.ContainsKey` — that dictionary is ordinal/case-sensitive, so a mod re-casing its own GUID between releases silently makes the dependent feature dead code with no error. Canonical copy and the full write-up: `IMPLEMENTATIONS/SharedInfrastructure.md`.
7. **Style semantics carry meaning — don't share a GUIStyle across roles** (1.1.5): the Feast Ledger's `_smallLabelStyle` (muted grey) was doing double duty for the indented stomach sub-lines *and* the top-level Lifetime Feast Log. On the sub-lines dimness is the cue that they're subordinate; on a top-level list it just reads as greyed-out. Split into its own style rather than brightening the shared one.

## Release history since this report was first written

- **1.1.3 — Perk Sync Fix.** `SEMan.AddStatusEffect` stores a `Clone()` of the ObjectDB template, so mutating the template never reaches an already-active effect. Fine for the diet-driven buffs (added/removed constantly, self-heal in seconds), **broken for the permanent milestone perks**, which are granted once and never removed — on a server, config sync typically lands *after* they activate, so a client could run a whole session on local values. Fixed by resolving the live instance via `seman.GetStatusEffect(hash)` and copying tunables onto it. See the ⚠️ note earlier in this file.
- **1.1.4 — Compatibility Pass.** No source change; rebuilt after the shared `libs-Tools` reference DLLs were refreshed from Feb→Jul (the live game build). Worth recording that the refresh changed **nothing** in the IL: decompiling the shipped 1.1.3 DLL against the rebuild diffed to a single line, the embedded git hash in `AssemblyInformationalVersion`. A reference refresh does not by itself justify a re-release.
- **1.1.5 — Compatibility Detection Fix.** `PluginLookup` (item 6 above) + Feast Log readability (item 7).
