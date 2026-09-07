# ItemValueDump — Serialized Prefab **Value** Extraction

> **⚠ SUPERSEDED 2026-07-31 — historical reference only, do not extend.**
>
> This capability now lives in **TheEye** as the `ValueExtractor` / `Values_Dump.json` output,
> emitted by the `dump_eye` console command. The prototype source
> (`libs-Tools\CSharp\ItemValueDump\`) has been **deleted** — TheEye already owned the coroutine
> batching, cancel support, progress reporting, zip output and console command that this tool
> re-solved from scratch. The real gap was one missing `IDataExtractor`, not a missing tool.
>
> **Canonical output:** `libs-Tools\WubarrksEye_Dumps\2026-07-31_21-06-43\Values_Dump.json`
> — 5,339 records across Items (1084), Recipes (365), StatusEffects (89), Prefabs (3458),
> Vegetation (156), Locations (188). Verified against this prototype's baseline: **exact item-count
> parity, all spot-checks match, 0 budget-exceeded markers, 0 contamination hits.**
>
> This document is retained for the rationale, gotchas and output-format discussion, which carry
> over verbatim. The v1 baseline it describes remains at `libs-Tools\ITEM-DUMPS-v1-archive\`.

## Overview

`ItemValueDump` is a small, disposable BepInEx research plugin (`wubarrk.ItemValueDump`, `net48`,
source at `libs-Tools\CSharp\ItemValueDump\`) that dumps the **serialized field values** of every
item in `ObjectDB` — `m_armor`, `m_damages.m_fire`, `m_weight`, `m_maxQuality`, `m_setName`,
`m_eitrRegenModifier`, and every other `ItemDrop.ItemData.SharedData` field — to a greppable TSV
and a JSON, plus optional dumps of `m_recipes` and `m_StatusEffects`.

It is not a gameplay mod. Drop it in, launch once, take the files, remove it.

---

## Why it exists — the gap it fills

Three evidence sources existed in this workspace before it, and **none** of them carries a value:

| Source | What it gives | What it cannot give |
|---|---|---|
| `DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs` | Field *declarations*, types, and the code that reads them. | Any serialized value — those live in Unity prefab/asset data, not in code. |
| `OLD-TheEye Dumps\All_Deep.txt` — `=== API BEGIN ===` section | Per-type field/property/method declarations. | Same. Declarations only. |
| `OLD-TheEye Dumps\All_Deep.txt` — `<znetscene>` section | `Path: <znetscene>/<Prefab> \| GameObject: <child> \| Type: <ComponentType> \| …` — proves a prefab exists and what components hang off it. | Any component's field values. It is a type walk, not a value walk. |

The root cause is in the tool, not the format: TheEye's `ObjectDbExtractor`
(`TheEye\WubarrksEye_Project\WubarrksEye\Core\Commands\DumpManager.cs:377-426`) iterates
`ObjectDB.instance.m_items`, but its `DumpGameObjectData` writes only `Name`, the list of
`ComponentType` strings, and `ZNetView` → `PrefabHash`. It never dereferences
`ItemDrop.m_itemData.m_shared`.

Ironically TheEye already ships a value-capable walker — `DeepReflectionWalker.Walk(root, onField)`,
32 levels deep with a 35-entry forbidden-type-prefix denylist — the ObjectDB extractor just never
called it.

**Practical consequence before this tool:** any question of the form "what is prefab X's base
armor/damage/weight?" could only be answered by equipping the item in-game and reading a tooltip.
That blocks any "donor-transform" balancing approach (`MistsofAvalor.md` §16), where every stat on
a custom item is expressed as a multiplier of the cloned donor's own value — you cannot multiply a
number you do not have.

---

## v2 — what it captures

v1 dumped ObjectDB items/recipes/status effects at depth 2, value-types only. v2 covers the whole
runtime object graph across **8 sections**:

| Section | Source | Why it matters |
|---|---|---|
| `Items` | `ObjectDB.m_items` | Every `ItemDrop.m_itemData` field, now depth-4 so `m_shared.m_attack.*` is reached. |
| `Recipes` | `ObjectDB.m_recipes` | Costs, station, min station level, per-level requirements. |
| `StatusEffects` | `ObjectDB.m_StatusEffects` | `SE_Stats` modifier values, plus the concrete subclass name. |
| `Prefabs` | `ZNetScene.m_prefabs` | **Every prefab in the game** — ore veins, pieces, creatures, crafting stations, smelters — with the field values of every `assembly_valheim` component on it, plus its component list and child-object names. |
| `Vegetation` | `ZoneSystem.m_vegetation` | Biome, density, placement rules — the clone source for custom ore spawns. |
| `Locations` | `ZoneSystem.m_locations` | Location placement data. |
| `Shaders` | `Resources.FindObjectsOfTypeAll` | Every loaded shader (with render queue and pass count) and material→shader mapping. Ground truth for `Shader.Find` priority chains in procedural-VFX work, instead of guessing names. |
| `Enums` | reflection over `assembly_valheim` | Every enum with numeric values. Turns "`ItemType.TwoHandedWeaponLeft = 22`" from a decompile citation into a checkable fact. |

Plus **localization resolution** — `$item_*` tokens resolved to display text via a reflective
`Localization.instance.Localize` call (reflective so a signature change can never break the build).

### Five independent triggers

`ObjectDB.Awake` → `ObjectDB.CopyOtherDB` → `ZNetScene.Awake` → `ZoneSystem.Start` → and the one
that actually matters, the **`runicdump` console command**. By the time you can type into the
console, every subsystem is populated and every mod has finished registering — no timing guesswork.
The command re-dumps all 8 sections on demand and overwrites its previous output.

Each hook is patched **individually** via `harmony.CreateClassProcessor(t).Patch()` inside its own
try/catch, not `PatchAll`. `PatchAll` is all-or-nothing: one missing target after a Valheim update
takes the whole plugin down, and a dump tool that dies silently is worse than one that delivers
seven sections out of eight. Failures log as `Feature Lost - <hook>` and the rest carry on.

## Architecture

- **Hooks.** Postfixes on `ObjectDB.Awake` and `ObjectDB.CopyOtherDB` — the same dual-hook pair
  used for registration in `Fatty\Synergies\SynergyEffects.cs:134-169`. Each hook is individually
  try/catch'd so a dump failure can never take the game down.
- **Two phases, two file sets.** Output is suffixed `_Awake` (main-menu DB) and `_CopyOtherDB`
  (in-game DB). **Diffing the two is a feature, not redundancy** — it shows exactly which values a
  mod or `ConfigSync` rewrites between menu load and in-game, which is the direct empirical test for
  the ServerSync timing trap in `Fatty.md` gotcha 8 (config values are not populated at
  `ObjectDB.Awake`; they arrive on the `ZNet` connect handshake).
- **Idempotent per phase.** `ObjectDB.Awake` can fire more than once; a `HashSet<string>` of
  completed phase names means each set of files is written once per session.
- **Flattening walk.** Public instance fields are walked to depth 2, with value-type aggregates
  flattened into dotted paths — so `HitData.DamageTypes` lands as separate `m_damages.m_fire`,
  `m_damages.m_blunt`, … rows rather than one opaque struct blob. That is what makes the TSV
  usefully greppable.
- **Recursion guard.** `ShouldRecurse` descends into value types with fields only. It explicitly
  never descends into a `UnityEngine.Object` reference — doing so walks the entire scene graph and
  hangs the load frame. Object references are emitted as their `.name` instead.

### Gotchas encoded in the source

1. **`.name`, not `.m_name`.** `m_name` is the localization token (`"$item_..."`). Prefab identity
   and `GetStableHashCode()` key off the GameObject/ScriptableObject `.name`. Same gotcha
   `DvergrAllies.md` flags for StatusEffect registration; the dump keys on `.name` so its output
   joins correctly against `ZNetScene` prefab names.
2. **Unity's overloaded `==`.** A destroyed or missing `UnityEngine.Object` compares equal to null
   without being a CLR null. The formatter checks `ReferenceEquals(uo, null)` first, then `uo == null`,
   so genuinely-missing references are recorded as `<missing>` rather than crashing or silently
   reading as absent.
3. **`"R"` round-trip float formatting** with `CultureInfo.InvariantCulture` — a comma decimal
   separator or a truncated float would make the numbers useless for the multiplier arithmetic
   they exist to feed.

---

## Output

Written to `OutputDirectory` (config, default `C:\WubarrkCODING\libs-Tools\ITEM-DUMPS`):

```
ItemValues_Awake.tsv        ItemValues_Awake.json
ItemValues_CopyOtherDB.tsv  ItemValues_CopyOtherDB.json
Recipes_<phase>.tsv
StatusEffects_<phase>.tsv
```

TSV layout is `prefab <TAB> field <TAB> value`, one field per line:

```
# prefab	field	value
ArmorAshlandsMediumChest	m_armor	18
ArmorAshlandsMediumChest	m_armorPerLevel	2
ArmorAshlandsMediumChest	m_maxQuality	4
BattleaxeCrystal	m_damages.m_frost	60
```

Grep straight out of it:

```bash
grep -P '^ArmorAshlandsMedium\w+\tm_armor(PerLevel)?\t' ItemValues_CopyOtherDB.tsv
grep -P '\tm_damages\.m_fire\t(?!0$)' ItemValues_CopyOtherDB.tsv   # every item with real fire damage
```

---

## Usage

1. `.\deploy.ps1 -Profile "<name>"` — builds and deploys in one step.
2. **Deploy to a CLEAN profile.** This matters more than anything else here. Every existing Gale
   profile in this workspace carries content mods, several of them Therzie packs — the `_TW`-suffixed
   contamination tell. Any mod that rebalances vanilla gear silently poisons the numbers, and a
   poisoned donor value propagates into every derived stat without ever looking wrong.
   Use a profile containing only BepInEx + this DLL.
3. Launch → main menu → load a world. Then **open the console and type `runicdump`** — this is the
   trigger to rely on; the passive hooks are a fallback for whatever fired earlier. Quit.
4. **Sanity-check the output before trusting it**: `grep -c '_TW\|TMMonsterItem' ItemValues_*.tsv`
   should be `0`. Anything above zero means the profile was not clean and the dump must be redone.
5. Remove the DLL from the profile.

### Config

| Key | Default | Purpose |
|---|---|---|
| `OutputDirectory` | `C:\WubarrkCODING\libs-Tools\ITEM-DUMPS` | Created if missing. |
| `DumpOnMenuDb` | `true` | Dump at `ObjectDB.Awake` too. Turn off if only in-game values matter. |
| `DumpRecipes` | `true` | `ObjectDB.m_recipes` — costs, station, min station level. |
| `DumpStatusEffects` | `true` | `ObjectDB.m_StatusEffects` — `SE_Stats` modifier values. |

---

## First run — 2026-07-31, and two bugs it exposed

Run against a clean `Runic Tester-Dumper` profile (BepInEx + this DLL only) produced
**1084 items / 160,432 field rows**, 365 recipes, 89 status effects — all from the `CopyOtherDB`
phase. It immediately corrected five prefab names and three item-type assumptions in the Runic
plan that had been rated "High confidence" off name-grepping alone. Two bugs surfaced, both now
fixed:

1. **`deploy.ps1 -Verify` reported 444 contamination hits on a genuinely clean dump.**
   `Select-String` is **case-insensitive by default**, so the `_TW` contamination pattern matched
   the vanilla Ashlands mob `charred_twitcher` 448 times. Case-sensitive `grep` found zero. Fixed
   by adding `-CaseSensitive`. *Lesson: a contamination check that cries wolf is worse than none —
   it would have caused a clean dump to be discarded and re-run.*
2. **The `_Awake` phase dumped 0 items.** `ObjectDB.Awake` fires more than once and the **first**
   fire carries an empty `m_items`. The once-per-phase `HashSet` guard latched on that empty pass
   and permanently suppressed the later populated one. Fixed: an empty DB now returns *without*
   marking the phase done, so a later fire still gets captured. *This also means the menu-vs-in-game
   value diff — the whole reason for dumping both phases — has not actually been exercised yet.*

Both are worth knowing generally: the first is a false-positive in a safety check, the second is a
latching-guard bug that produced a silent empty result rather than an error.

## Reusable beyond items

The `Flatten` / `ShouldRecurse` / `Describe` trio is generic — it takes any `object` and produces
flat `path = value` pairs. Repointing it at `ZNetScene.instance.m_prefabs`, `ObjectDB.m_itemDB`, or
any other runtime collection is a matter of swapping the source enumerable and the key selector
(`DumpList` already takes a `Func<object,string> keyOf` for exactly this). If a future question is
"what is the runtime value of X", this is the harness, not a new tool.

**Related:** `DvergrAllies.md` (`.name` gotcha), `Fatty.md` (dual-hook registration, gotcha 8 on
ServerSync timing), `MistsofAvalor.md` §16 (donor-transform balancing — the consumer of this data),
`TortalPortal.md` (the offline-harness alternative when the game does not need to be running).
