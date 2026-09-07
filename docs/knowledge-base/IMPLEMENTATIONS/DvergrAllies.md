# DvergrAllies — Technical System Report

## Overview

DvergrAllies is a Valheim BepInEx/Jotunn mod that turns the game's neutral Dvergr NPCs (Mistlands "Dverger" race) into tameable, breedable, fully-fightable player companions. It retrofits every wild Dvergr variant (vanilla and mod-added) with taming/breeding components, defines a roster of hand-crafted "combo" ally classes (Warrior, Berserker, Cleric, Spellsword, Elemental Mage, etc.) with class-specific AI behavior, implements a gender/trait-driven breeding simulation that produces leveled offspring (including rare "mutation" hybrid classes), and adds two acquisition paths — slow feed-taming and an instant-summon consumable sold by the vanilla trader Haldor. The whole system is built almost entirely from runtime `MonoBehaviour` components layered onto cloned vanilla prefabs plus Harmony patches, rather than any custom rendering/pathfinding engine — it is a good reference for "companion simulation on top of vanilla AI" patterns.

Toolchain: BepInEx 5, Jotunn (JotunnLib) 2.x, HarmonyLib, Unity 2022.3 (net48), targeting Valheim's `assembly_valheim` (publicized via `BepInEx.AssemblyPublicizer.MSBuild`).

---

### Plugin Bootstrap, Soft-Dependency Detection & Synced Config

- **Purpose:** Standard BepInEx entry point wiring: register the plugin, detect an optional compatibility mod at load time, initialize a categorized/admin-synced config file, hook Jotunn's prefab-ready event, and apply all Harmony patches in the assembly.
- **Key files:** `Plugin.cs`, `ConfigManager.cs`
- **Architecture:**
  - `Plugin : BaseUnityPlugin` with `[BepInPlugin(GUID, Name, Version)]`, `[BepInDependency(Jotunn.Main.ModGuid)]` (hard dependency), `[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]`.
  - `Awake()` order matters: (1) detect optional mod via `BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("<other.mod.guid>")` and stash the bool as a `public static` flag (`Plugin.HasBalrondIdleActors`) so every other file can branch on it without an assembly reference; (2) `ConfigManager.Init(Config)`; (3) subscribe multiple independent `Setup()` static methods to `Jotunn.Managers.PrefabManager.OnVanillaPrefabsAvailable`; (4) `harmony.PatchAll()` last, so patches are active before/independent of prefab setup.
  - `OnDestroy()` calls `harmony.UnpatchSelf()` for clean unload/hot-reload.
  - `ConfigManager` is a static class holding every `ConfigEntry<T>` as a public static field, grouped into numbered categories (`"1 - Taming"`, `"2 - Breeding"`, `"2.5 - AI"`, `"3 - Economy"`, `"4 - Stats"`, `"5 - Debug"`). A `SyncedConfig(string)` helper wraps `ConfigDescription` with Jotunn's `ConfigurationManagerAttributes { IsAdminOnly = true }` — this is Jotunn's mechanism to force server→client config sync in multiplayer while restricting edits to admins.
  - Four categorized debug-log helpers (`LogAI`, `LogBreeding`, `LogEquipment`, `LogDebug`) gated by a master `EnableDebugLogs` bool AND either a `DebugFull` override or a per-category bool.
- **How to implement:**
  1. Add BepInEx/Jotunn NuGet refs and a publicized `assembly_valheim` reference.
  2. Create `[BepInPlugin]` + `[BepInDependency(Jotunn.Main.ModGuid)]` + `[NetworkCompatibility]` class extending `BaseUnityPlugin`.
  3. In `Awake()`, check `Chainloader.PluginInfos.ContainsKey(otherModGuid)` for any soft/optional compat target and cache the result statically.
  4. Build a static `ConfigManager` with `config.Bind(category, key, default, ConfigDescription)`; use `ConfigurationManagerAttributes{ IsAdminOnly = true }` for values that must be synced/admin-locked in multiplayer.
  5. Subscribe prefab-building static methods to `PrefabManager.OnVanillaPrefabsAvailable` (never build custom prefabs before this fires).
  6. Call `new Harmony(GUID).PatchAll()` to auto-apply every `[HarmonyPatch]` class in the assembly.
- **Reusable pattern/snippet:**
```csharp
public static bool HasOtherMod;
private void Awake() {
    HasOtherMod = BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("author.other.modguid");
    ConfigManager.Init(Config);
    Jotunn.Managers.PrefabManager.OnVanillaPrefabsAvailable += MyPrefabFactory.Setup;
    new Harmony(PluginGUID).PatchAll();
}
private static ConfigDescription SyncedConfig(string desc) =>
    new ConfigDescription(desc, null, new ConfigurationManagerAttributes { IsAdminOnly = true });
```

---

### Ally Prefab Factory & Wild-Creature Retrofit System

- **Purpose:** Data-driven creation of every ally NPC prefab variant (5 "base" 1:1 vanilla-loadout clones + 6 hand-authored "combo" classes with custom weapon/armor loadouts), plus retroactively patching *any* wild Dvergr-named prefab in the game (vanilla, DLC, or other mods) into a tameable/breedable creature without knowing its exact name ahead of time.
- **Key files:** `AllyPrefabManager.cs`, referenced by `Plugin.cs`, `RecruiterManager.cs` (reuses `CustomVariants` list), `DvergrWeaponScaler.cs` (reuses `GetOrCloneWeaponWithMeleeAnim`).
- **Architecture:**
  - `CustomDvergrDef` is a plain data class: `PrefabName`, `BasePrefab` (which vanilla Dverger visual/animator rig to clone), `DisplayName`, `Loadout` (string[] of item prefab names), `HealthMultiplier`, `StripArmor`, `AddRage`, `IsMeleeAI`/`IsRangedAI`/`IsClericAI` flags.
  - `AllyPrefabManager.CustomVariants` is a single static `List<CustomDvergrDef>` — the entire roster is declared as data in one array literal, making it the canonical **registry pattern**: other systems iterate this same list instead of hardcoding names, so adding one new list entry automatically wires the class into taming, breeding-birth, and the summon pool.
  - `Setup()` (subscribed to `PrefabManager.OnVanillaPrefabsAvailable`, unsubscribes itself first line): (1) `CreateAllyVariant` for 5 "keep vanilla loadout" base classes; (2) `CreateCustomVariant(def)` for every entry in `CustomVariants`; (3) explicitly pre-clones several tier-upgrade weapons via `GetOrCloneWeaponWithMeleeAnim` so Jotunn registers them into `ZNetScene` *during load*, not lazily at runtime later.
  - `CreateAllyVariant(basePrefabName, newPrefabName, displayName, healthMultiplier, isMelee, isRanged, isCleric)`:
    1. `PrefabManager.Instance.GetPrefab(basePrefabName)` then `PrefabManager.Instance.CreateClonedPrefab(newPrefabName, basePrefabName)`.
    2. Wrap in `new CustomPrefab(prefab, true)`.
    3. `StripBalrondComponents(prefab)` — soft-compat cleanup.
    4. Remove vanilla `Tameable` (`DestroyImmediate`), add `DvergrTameable`; configure `m_tamingTime`/`m_fedDuration`/`m_commandable`.
    5. Configure `MonsterAI`: populate `m_consumeItems` from `ConfigManager.TamingItems`, set `m_consumeSearchRange`/`m_consumeRange`, and force off every "neutral NPC" behavior flag (`m_aggravatable`, `m_passiveAggresive`, `m_fleeIfLowHealth=0`, `m_fleeIfNotAlerted`, `m_circulateWhileCharging(Flying)`, `m_avoidFire`, `m_avoidWater`, `m_attackPlayerObjects`), plus `m_maxChaseDistance = 0` and `m_alertRange = 30`.
    6. Remove vanilla `Procreation`, add `DvergrProcreation`; add `DvergrGenetics`; add `DvergrWeaponScaler`; add `DvergrCombatAI` with its `IsMeleeClass`/`IsRangedClass`/`IsClericClass` set from the call args.
    7. Adjust `Character`: `m_health *= (globalMultiplier * perDefMultiplier)`, `m_faction = Character.Faction.Players` (this is what makes vanilla Valheim treat the NPC as a player ally for aggro/targeting purposes), floor `m_runSpeed`/`m_walkSpeed`.
    8. Set `Humanoid.m_name = displayName`.
    9. `PrefabManager.Instance.AddPrefab(customPrefab)`.
  - `CreateCustomVariant(def)` layers on top: clears `Humanoid.m_randomSets`/`m_randomWeapon`/`m_randomShield` (and `m_randomArmor` if `StripArmor`) to kill vanilla's random-loadout-picker, builds a clean `m_defaultItems` array from a suit prefab plus each `Loadout` entry resolved through `GetOrCloneWeaponWithMeleeAnim`, sets `comboHum.m_unarmedWeapon = null`, and finally attaches class-specific components: `AddComponent<DvergrBerserkerRage>()` if `def.AddRage`, or flips `DvergrCombatAI.IsElementalMage = true` by name match.
  - `MakePrefabTamable(GameObject prefab)` is the **retrofit** entry point used on prefabs the mod did *not* author: swaps `Tameable`→`DvergrTameable`, `Procreation`→`DvergrProcreation`, adds `DvergrGenetics`, and re-applies the same MonsterAI de-hostility + consume-item config.
  - `GetSuitName(basePrefabName)` — simple switch-based lookup table mapping base creature → clothing "suit" prefab name.
  - `StripBalrondComponents(prefab)` — soft-compat pattern: if `Plugin.HasBalrondIdleActors` is true, uses `GameObject.GetComponent(string)` (reflection-by-name) to find and `DestroyImmediate` named components that would otherwise conflict.
  - Two Harmony patches close the loop for prefabs the mod's own `Setup()` never sees:
    - `[HarmonyPatch(typeof(ZNetScene), "Awake")] Postfix` — iterates `__instance.m_prefabs` (the *fully populated* prefab list, including DLC/mod content), and for any prefab whose lowercased name contains `"dverg"`/`"dverger"` but not `"ally"`, and which has both `Humanoid` and `MonsterAI` components, calls `MakePrefabTamable`. This is the general technique for "catch every creature of a category regardless of source mod, by name/component heuristics, at the one guaranteed moment the full prefab list exists."
    - `[HarmonyPatch(typeof(MonsterAI), "UpdateAI")] Prefix` — runs on **every** `MonsterAI.UpdateAI()` call for **every** creature in the game, and for any non-ally Dvergr, force-resets the same hostility flags every frame as a belt-and-suspenders guard against vanilla/ZDO sync silently reverting them mid-session.
  - `GetOrCloneWeaponWithMeleeAnim(itemName)` — see the dedicated weapon-cloning section below; idempotent (checks for existing clone before creating a new one).
- **How to implement:**
  1. Define a small POCO describing each companion "class" you want (base prefab to clone, display name, loadout item names, per-class flags).
  2. Put all class definitions in one static `List<T>` so other systems can enumerate the same source of truth.
  3. On `PrefabManager.OnVanillaPrefabsAvailable`, for each definition: `CreateClonedPrefab(newName, baseName)` → wrap `new CustomPrefab(go, true)` → strip/replace vanilla components you're overriding → tune `MonsterAI` hostility fields → tune `Character` stats/faction → `PrefabManager.Instance.AddPrefab(customPrefab)`.
  4. To retrofit prefabs you don't control, do NOT try to hook their load event — instead Harmony-postfix `ZNetScene.Awake` and scan `m_prefabs` by name/component signature once the scene is fully populated.
  5. Layer a `Prefix` patch on the creature's AI update method only if a single `Awake`-time fix proves insufficient against runtime resets.
  6. For optional cross-mod compatibility, check `Chainloader.PluginInfos` once at boot, cache a bool, and branch skip/no-op paths on it everywhere relevant.
- **Reusable pattern/snippet:**
```csharp
[HarmonyPatch(typeof(ZNetScene), "Awake")]
public static class Patch_RetrofitByNameHeuristic
{
    [HarmonyPostfix]
    public static void Postfix(ZNetScene __instance)
    {
        foreach (var prefab in __instance.m_prefabs)
        {
            if (prefab == null) continue;
            string n = prefab.name.ToLower();
            if (n.Contains("targetcreature") && !n.Contains("ally")
                && prefab.GetComponent<Humanoid>() != null && prefab.GetComponent<MonsterAI>() != null)
                MyFactory.MakePrefabRetrofittable(prefab);
        }
    }
}
```

---

### Monster-Compatible Weapon Cloning Pattern ("Brain Transplant" + In-Memory ItemData Clone)

This is a cross-cutting technique used by three files (`AllyPrefabManager`, `DvergrCombatAI`, `DvergrWeaponScaler`) and is one of the most non-obvious/reusable tricks in the codebase.

- **Purpose:** Valheim's `MonsterAI`/`Humanoid` AI-attack engine only correctly drives weapons whose `ItemDrop.SharedData.m_attack` was authored for monster use (correct AI ranges, no stamina/eitr gating NPCs can't regen). Player-facing item prefabs, when simply cloned, won't be attacked with properly by NPCs. The mod solves this two ways: a registered-prefab path for real weapons, and an in-memory path for throwaway "virtual" gear.
- **Key files:** `AllyPrefabManager.cs` (`GetOrCloneWeaponWithMeleeAnim`), `DvergrCombatAI.cs` (`CreateNativeVariant`), consumed by `DvergrWeaponScaler.cs`.
- **Architecture:**
  - **Registered-prefab path — `GetOrCloneWeaponWithMeleeAnim(itemName)`**: checks for an existing prefab first (idempotent), else strips a prefix to get the vanilla base item name, `PrefabManager.Instance.CreateClonedPrefab(itemName, baseItemName)`, wraps `new CustomItem(cloned, true)`. For melee categories it performs the "brain transplant": fetches `Dverger_melee`'s `ItemDrop`, and does `drop.m_itemData.m_shared.m_attack = dvergerDrop.m_itemData.m_shared.m_attack.Clone();` — replacing the entire attack definition with one taken from a prefab already known-good for monster AI use. Then sets `m_aiAttackInterval=1.2f`, `m_aiAttackRange=3.5f`, zeroes `m_attackStamina`/`m_attackHealth`/`m_attackHealthPercentage`/`m_attackEitr` (critical — nonzero costs silently prevent NPCs from ever attacking), and sets `m_useDurability = false`. A global `ConfigManager.AllyDamageMultiplier` scale-up follows. `ItemManager.Instance.AddItem(customItem)` registers it.
  - **In-memory path — `CreateNativeVariant(baseWeaponName, newProjectileName=null)`** (for AI-only magic weapons that never need to exist as a lootable item): looks up the base weapon GameObject, clones just the `ItemDrop.ItemData` object (`.Clone()`), then **manually deep-copies `SharedData` via reflection** (`Clone()` only shallow-copies the nested `Attack` object references — `m_attack`/`m_secondaryAttack` are explicitly re-`.Clone()`'d afterward). Zeros attack costs, applies the global damage multiplier, and optionally swaps in a different projectile prefab. The resulting `ItemData` is **never registered with ObjectDB/ItemManager** — added directly into a live `Humanoid`'s `Inventory` via `AddItem(ItemData)` and immediately `EquipItem()`'d.
- **How to implement:**
  1. For any monster/companion prefab that must wield a weapon, prefer cloning the `m_attack` object from a prefab you know already works with `MonsterAI` rather than trusting a player-item's attack data.
  2. Always zero `m_attackStamina`, `m_attackHealth`, `m_attackHealthPercentage`, `m_attackEitr` on any weapon an NPC will use.
  3. If you need a network-visible, lootable, or tier-upgradeable weapon: clone via `PrefabManager.CreateClonedPrefab` + `CustomItem` + `ItemManager.Instance.AddItem`.
  4. If you need a purely AI-internal "virtual" weapon: clone just the `ItemDrop.ItemData` in memory via reflection over `SharedData`'s public fields, re-clone the nested `Attack`, and inject with `Inventory.AddItem(ItemData)` — no `ItemManager` registration needed.
  5. Guard any prefab-cloning helper with an existence check so it can be safely called both at load time and repeatedly at arbitrary runtime.
- **Reusable pattern/snippet:**
```csharp
// Deep-clone ItemData in memory for AI-only "virtual" gear:
ItemDrop.ItemData clone = baseItemData.Clone();
var orig = clone.m_shared;
clone.m_shared = new ItemDrop.ItemData.SharedData();
foreach (var f in typeof(ItemDrop.ItemData.SharedData).GetFields(BindingFlags.Public | BindingFlags.Instance))
    f.SetValue(clone.m_shared, f.GetValue(orig));
clone.m_shared.m_attack = orig.m_attack.Clone();
clone.m_shared.m_attack.m_attackStamina = 0f; // let NPCs actually cast/attack
inventory.AddItem(clone);
humanoid.EquipItem(clone);
```

---

### Taming System

- **Purpose:** Extend vanilla's feed-based taming (walk up, drop food, wait a timer) with mod-specific hover-text (pregnancy/hunger status) while reusing 100% of vanilla `Tameable`'s actual taming logic.
- **Key files:** `DvergrTameable.cs` (33 lines total — deliberately thin), wired up by `AllyPrefabManager.cs`.
- **Architecture:**
  - `DvergrTameable : Tameable, Hoverable` — actual C# **inheritance**, not composition. No new taming fields/logic are added; `m_tamingTime`, `m_fedDuration`, `m_commandable` are just vanilla `Tameable` fields set by `AllyPrefabManager` after construction.
  - The class exists to (1) give the modder a distinct component type so Harmony patches/factory code can tell "our ally-capable Dvergr" apart from a generic `Tameable`, and (2) override hover text using the `new` keyword to append pregnancy/hunger status.
  - **Gotcha:** `new` *hides*, it does not *override* — if the hover system dispatches through a `Tameable`- or `Hoverable`-typed reference rather than the concrete `DvergrTameable` type, this override may silently not fire. The codebase hedges against this by *also* implementing the same status-append logic independently inside `DvergrGenetics.LateUpdate()` by directly mutating `Hud.instance.m_hoverName.text` — a second, definitely-working mechanism layered on top.
- **How to implement:**
  1. Subclass the vanilla component you're extending rather than reimplementing its logic.
  2. Only override hover text or add cosmetic hooks; put actual new gameplay state in ZDO-backed *sibling* components.
  3. If your target method may not be virtual, don't rely solely on a `new` shadow — also implement a `LateUpdate()` hover-object check as a guaranteed-to-run fallback.
  4. Assign taming config at prefab-build time from `ConfigEntry<T>` values so they're tunable without recompiling.
- **Reusable pattern/snippet:**
```csharp
public class MyTameable : Tameable, Hoverable {
    public new string GetHoverText() {
        string t = base.GetHoverText();
        if (GetComponent<MyBreeding>()?.IsPregnant() == true) t += "\n<color=yellow>Pregnant</color>";
        return t;
    }
}
// Guaranteed-fire fallback for hover text, independent of override dispatch:
void LateUpdate() {
    if (Player.m_localPlayer?.GetHoverObject() == gameObject && Hud.instance?.m_hoverName != null)
        Hud.instance.m_hoverName.text += "\n(extra info)";
}
```

---

### Genetic Trait / Gender System

- **Purpose:** Give every Dvergr (wild or ally) a persistent, randomly-assigned, network-synced gender used to gate breeding pairs, plus a cross-mod compatibility signal (a ZDO flag another mod reads to exempt allies from its own stealth AI).
- **Key files:** `DvergrGenetics.cs`
- **Architecture:**
  - `DvergrGenetics : MonoBehaviour` holds no in-memory trait data itself — **all state lives on the `ZDO`**, making it automatically synced/persisted to the save file for free.
  - `enum Gender { Male, Female }` stored as an `int` under ZDO key `"dvergr_gender"`.
  - `Awake()`: caches `ZNetView`/`Humanoid`; **only the ZDO owner** initializes state — reads `GetInt("dvergr_gender", -1)`; if unset, does `Random.value > 0.5f ? Male : Female` and writes it once. This is the canonical "assign-once, owner-authoritative, ZDO-persisted random trait" pattern.
  - `GetGender()` is a pure ZDO read.
  - **Cross-mod compatibility signal:** `StealthExemptKey = "SoMStealthExempt"` — a ZDO int key that a *different, unrelated* mod ("Shadows of Midgard") reads on its own to decide whether to bypass its custom stealth-AI brain for this character. Two important correctness notes preserved from the source:
    - It is written **explicitly to 0**, not skipped, for non-allies, because a ZDO write persists/replicates.
    - `Character.SetTamed` does **not** synchronously update `m_tamed` — it fires an RPC and only the RPC handler assigns the field — so `IsTamed()` cannot be trusted immediately after calling `SetTamed()` unless the caller is guaranteed to be the ZDO owner.
  - `[HarmonyPatch(typeof(Character), nameof(Character.SetTamed))] Postfix(Character __instance, bool tamed)` — reads the **method's own `tamed` parameter** (not a re-derived `IsTamed()` call) to call `RefreshStealthExemption` at exactly the right moment, gated to `nview.IsOwner()`.
- **How to implement:**
  1. Store simple persistent per-instance traits directly on the `ZDO` rather than in C# fields — save-persistence and multiplayer sync for free.
  2. Gate *writes* to `IsOwner()` in `Awake()`/on-demand methods; reads are always safe from any client.
  3. Use a sentinel default (`-1`) to detect "never initialized" vs a valid value of `0`.
  4. To signal state to a third-party mod without taking a dependency on it, write a plain ZDO key using whatever key name that mod's documentation/source specifies it reads.
  5. When hooking a vanilla state-transition method with Harmony, prefer reading the *method's own parameter* over re-querying a getter after the fact if that getter's backing field is updated asynchronously.
- **Reusable pattern/snippet:**
```csharp
private void Awake() {
    m_nview = GetComponent<ZNetView>();
    if (m_nview != null && m_nview.IsValid() && m_nview.IsOwner()) {
        int g = m_nview.GetZDO().GetInt("gender", -1);
        if (g == -1) m_nview.GetZDO().Set("gender", (int)(Random.value > 0.5f ? 1 : 0));
    }
}
[HarmonyPatch(typeof(Character), nameof(Character.SetTamed))]
public static class Patch_SetTamed {
    [HarmonyPostfix] public static void Postfix(Character __instance, bool tamed) { /* use `tamed` directly */ }
}
```

---

### Breeding / Procreation / Birth System (Genetics core)

- **Purpose:** The actual "genetic simulation" — periodically finds a compatible nearby partner of opposite gender, rolls a pregnancy chance, tracks pregnancy duration on the mother's ZDO, and on term spawns offspring whose **type** is determined by combining both parents' class identities (with rare hybrid "mutations") and whose **level/stars** are determined by the higher parent level plus a level-up chance.
- **Key files:** `DvergrProcreation.cs`, depends on `DvergrGenetics.cs`, `DvergrTameable.cs`, `ConfigManager.cs`.
- **Architecture:**
  - `Awake()` starts `InvokeRepeating(nameof(Procreate), Random.Range(0, UpdateInterval), UpdateInterval)` — the **random initial phase offset** desynchronizes every Dvergr's heavy `Physics.OverlapSphere` scan across different frames.
  - **Pregnancy state lives entirely on the pregnant individual's own ZDO**, three keys: `"pregnant"` (a `long` = world-time ticks), `"dvergr_partner_prefab"` (string), `"dvergr_partner_level"` (int). This is the key genetics-persistence trick: **rather than storing an abstract "genome" struct, the mod stores the two parents' prefab-name strings and re-derives traits from those names at birth time.**
  - `Procreate()` (owner-only): if already pregnant and duration elapsed, starts `BirthingProcess()` coroutine. Else if `ReadyForProcreation()` fails, return. **Population cap:** counts tamed characters carrying `DvergrGenetics` within a 10m radius; abort if ≥ `BreedingLimit`. **Partner search:** finds the first other `Character` with a `DvergrGenetics` component of opposite gender whose own `ReadyForProcreation()` also passes, rolls pregnancy chance. Only the **first** valid candidate per tick is considered. On success, **the female always becomes pregnant** (deterministic role by gender).
  - `BirthingProcess()`: reads partner data off the ZDO, calls `ResetPregnancy()`, computes `comboPrefab = DetermineOffspring(myPrefabName, partnerPrefabName)`, and **directly `UnityEngine.Object.Instantiate`s** the prefab (not a Jotunn/ZNetScene spawn call — the prefab's own `ZNetView` self-registers on `Awake`/`Start`). Then: `childChar.SetTamed(parent.IsTamed())` propagates tame state; level/star inheritance = `finalLevel = Max(myLevel, partnerLevel)`, then `+1` if a chance roll succeeds, clamped, `childChar.SetLevel(finalLevel)`.
  - `DetermineOffspring(parentA, parentB)` — **the genetic-combination algorithm**, entirely string-substring-based ("the genome IS the prefab name"): derives 6 booleans by checking whether either parent's name `Contains(...)` a trait keyword; rolls a mutation chance; on success checks an **ordered, first-match-wins** list of hybrid rules mapping trait combinations to hybrid prefab names (note: one rule as written is tautological — `hasWarrior && hasWarrior` — a literal implementation quirk worth flagging if reimplementing/fixing this system); falls back to simple 50/50 single-parent-trait inheritance.
- **How to implement:**
  1. Store breeding state (pregnant flag/timestamp, partner identity, partner level) on the ZDO of the individual that will carry the pregnancy.
  2. Use `InvokeRepeating` with a randomized initial delay for any per-instance periodic scan to avoid synchronized spikes.
  3. Gate the periodic logic with `nview.IsOwner()` so only one authoritative simulation runs per object across the network.
  4. Encode "traits" as whatever data is cheapest to re-derive at birth time — reusing the prefab **name** as the genome by substring-matching known class keywords avoids a whole separate trait-storage system, at the cost of being fragile to renames.
  5. For offspring type resolution, use an ordered "first-matching-rule-wins" table of hybrid outcomes gated behind a configurable mutation-chance roll, with a uniform-random single-parent fallback otherwise.
  6. For offspring stats, take the max (or another dominance rule) of both parents plus a small configurable chance to exceed it, then clamp to a designed ceiling.
  7. Spawn offspring with plain `Object.Instantiate(prefab, pos, rot)` on a prefab that already has a `ZNetView` — no manual ZDO/network registration call needed.
- **Reusable pattern/snippet:**
```csharp
private void MakePregnant(string partnerPrefab, int partnerLevel) {
    m_nview.GetZDO().Set("pregnant", ZNet.instance.GetTime().Ticks);
    m_nview.GetZDO().Set("partner_prefab", partnerPrefab);
    m_nview.GetZDO().Set("partner_level", partnerLevel);
}
private string DetermineOffspring(string a, string b) {
    bool traitX = a.Contains("X") || b.Contains("X");
    bool traitY = a.Contains("Y") || b.Contains("Y");
    if (Random.Range(0,100) < mutationChance) {
        if (traitX && traitY) return "HybridPrefab";
    }
    return Random.value > 0.5f ? a : b; // fallback: inherit one parent's type
}
```

---

### Combat AI Behavior Override System

- **Purpose:** Turn vanilla's neutral/passive `MonsterAI` engine into a fully committed, class-appropriate combat companion (melee charger, ranged/caster that holds ground, cleric that heals, or an elemental mage that auto-rotates spell loadouts) purely by continuously overwriting the same public `MonsterAI` fields the vanilla engine reads, rather than replacing the AI engine itself. Also implements a distance-based "leash" and does the runtime magic-weapon injection described in the weapon-cloning section.
- **Key files:** `DvergrCombatAI.cs`
- **Architecture:**
  - Public flags `IsMeleeClass`/`IsRangedClass`/`IsClericClass`/`IsElementalMage` are set externally by `AllyPrefabManager` at prefab-build time.
  - `Start()`: calls `ForceAggressiveAI()` immediately, then again via `Invoke(..., 1f)` and `Invoke(..., 3f)` — a **staggered re-application** pattern to catch other systems that might initialize/override these fields slightly after this component's own `Start`. Also `Invoke(nameof(InjectMagicWeapons), 0.5f)`.
  - `ForceAggressiveAI()`: sets universal de-neutralize flags, then class-specific movement tuning: melee zeroes random-movement intervals for a full direct charge; ranged/cleric disable circling but keep some random movement.
  - `FixedUpdate()` (runs **every physics tick, on every client — no `IsOwner()` gate anywhere in this method**, worth flagging as a multiplayer correctness consideration): every 60 frames, re-checks the 3 critical hostility flags and re-runs `ForceAggressiveAI()` if any were externally reset. **Leash logic:** if distance to follow-target exceeds `MaxFollowLeash`, calls `m_monsterAI.SetTarget(null)` to forcibly drop aggro. Cleric branch calls `UpdateClericAI()` every 2s. Elemental-mage branch calls `UpdateElementalMageAI()` (internally rate-limited to 10s).
  - `UpdateClericAI()`: no-op if mid-attack or heal cooldown < 15s. Finds nearby damaged self/players/tamed characters (excludes wild neutral Dvergrs); heals qualifying targets by adding a `DvergrHoT` component (destroying any pre-existing one first, so re-application refreshes rather than stacks).
  - `InjectMagicWeapons()`: nerfs any heal-staff item's AI attack interval so the AI doesn't prioritize it as an attack weapon; for Spellsword variants injects a native fireball/icebolt item; for elemental mages populates `m_elementalStaves` and equips element index 0.
  - `UpdateElementalMageAI()`: a 10s accumulator round-robins through `m_elementalStaves`, each cycle fully unequipping/removing all current weapons and adding+equipping the new element — an "auto-rotating loadout" implemented purely through inventory manipulation.
- **How to implement:**
  1. Don't write a custom AI/pathfinding engine — identify the specific public fields on the vanilla AI component that gate "neutral"/"passive"/"flee" behavior, and force them via a runtime `MonoBehaviour` added post-prefab-clone.
  2. Reapply the force at multiple staggered delays after spawn to beat any late vanilla initialization, then periodically re-check in `FixedUpdate` as an ongoing guard.
  3. Implement a distance-based leash by comparing the AI's current follow-target distance against a configurable max and calling the AI's own "clear target" method.
  4. For special-ability classes, drive them from the same `FixedUpdate` with their own accumulator timers so you can gate them behind combat-state checks cheaply every tick.
  5. Delay any inventory-dependent setup by at least one frame/short `Invoke` after `Start()` so it runs after the engine's own default-item-give logic.
  6. If any of this logic performs effects with real gameplay consequences (healing, damage), add an explicit ZDO-ownership gate unless you've verified the underlying API call is itself safe to call from non-owner clients.
- **Reusable pattern/snippet:**
```csharp
private void FixedUpdate() {
    if (Time.frameCount % 60 == 0 && (m_ai.m_passiveAggresive || m_ai.m_fleeIfNotAlerted))
        ForceAggressiveAI(); // periodic re-assert, cheap check

    var followTarget = m_ai.GetFollowTarget();
    if (followTarget != null && m_ai.GetTargetCreature() != null &&
        Vector3.Distance(transform.position, followTarget.transform.position) > maxLeash)
        m_ai.SetTarget(null); // drop aggro, return to owner
}
```

---

### Berserker Rage Buff (Status Effect State Machine)

- **Purpose:** A minimal always-on combat buff for the Berserker class: speed, stamina regen, health regen boosts plus an axe-skill boost (indirect damage buff), continuously re-applied so it never expires while alive.
- **Key files:** `DvergrBerserkerRage.cs` (37 lines), attached conditionally when `CustomDvergrDef.AddRage == true`.
- **Architecture:**
  - `Start()` calls `InvokeRepeating(nameof(ApplyRage), 1f, 5f)`. `ApplyRage()`: checks `HaveStatusEffect("BerserkerRage".GetStableHashCode())`; if not already active, **constructs a `SE_Stats` `ScriptableObject` at runtime** — **not** registered through Jotunn's `ItemManager.AddStatusEffect`, i.e. a purely local, ephemeral instance never added to `ObjectDB` — configures speed/regen multipliers plus `m_raiseSkill = Skills.SkillType.Axes` / `m_raiseSkillModifier = 15f` (a skill-level buff, indirectly increasing damage through Valheim's skill-based damage scaling). Applied via `seMan.AddStatusEffect(rage)` (the instance-overload).
- **How to implement:**
  1. For a simple "always active while alive" buff with no visual duration bar requirements, skip full asset-pipeline registration entirely — just `ScriptableObject.CreateInstance<SE_Stats>()`, configure its fields, and call `character.GetSEMan().AddStatusEffect(instance)` directly.
  2. Poll with `InvokeRepeating` and a `HaveStatusEffect(name.GetStableHashCode())` guard to avoid re-adding every tick.
  3. Use `SE_Stats.m_raiseSkill`/`m_raiseSkillModifier` to grant an indirect damage buff via Valheim's own skill-scaling formula instead of hand-rolling a damage multiplier.
- **Reusable pattern/snippet:**
```csharp
private void ApplyRage() {
    var seMan = m_character.GetSEMan();
    if (!seMan.HaveStatusEffect("MyBuff".GetStableHashCode())) {
        var buff = ScriptableObject.CreateInstance<SE_Stats>();
        buff.name = "MyBuff"; buff.m_speedModifier = 1.15f;
        seMan.AddStatusEffect(buff); // instance overload, no registration required
    }
}
```

---

### Heal-over-Time Status Component

- **Purpose:** A lightweight, non-registered, non-ZDO-persisted timed heal effect applied by the Cleric AI to nearby damaged allies/players — 5 ticks over 5 seconds, potency scaled by caster level.
- **Key files:** `DvergrHoT.cs` (55 lines), attached/driven by `DvergrCombatAI.UpdateClericAI()`.
- **Architecture:**
  - `DvergrHoT : MonoBehaviour`, added dynamically via `targetGameObject.AddComponent<DvergrHoT>()` — **not** a Valheim `StatusEffect` asset at all, just a plain component with purely local (non-networked) state.
  - `Setup(int clericLevel)`: computes `m_healAmountPerTick = 5f + (5f * clericLevel)`, resets ticks, forces `m_tickTimer = 1f` so the first tick fires next `FixedUpdate`.
  - `FixedUpdate()`: if target null/dead, `Destroy(this)`. Otherwise accumulates `Time.fixedDeltaTime`; once ≥1s, decrements ticks, calls `m_target.Heal(m_healAmountPerTick, true)`, optionally spawns VFX, self-destroys once ticks exhausted.
  - Refresh-not-stack: the caller explicitly destroys any pre-existing `DvergrHoT` before adding a new one.
- **How to implement:**
  1. For a simple timed DoT/HoT that doesn't need to survive a scene reload or sync independently, `AddComponent` a plain `MonoBehaviour` with a `Setup(...)` initializer and let it self-destroy when its tick counter or target-null/dead check fails.
  2. Use a `float` accumulator against `Time.fixedDeltaTime` in `FixedUpdate` for tick timing, pre-seeding it near the threshold to force an immediate first tick.
  3. Always guard against the target being destroyed/dying mid-effect.
  4. To prevent duplicate/stacking application, have the *caller* destroy any existing instance of the same component type on the target before adding a fresh one.
- **Reusable pattern/snippet:**
```csharp
public class MyHoT : MonoBehaviour {
    private Character m_target; private int m_ticks = 5; private float m_perTick, m_timer = 1f;
    public void Setup(Character t, float amountPerTick) { m_target = t; m_perTick = amountPerTick; m_ticks = 5; m_timer = 1f; }
    private void FixedUpdate() {
        if (m_target == null || m_target.IsDead()) { Destroy(this); return; }
        m_timer += Time.fixedDeltaTime;
        if (m_timer >= 1f) { m_timer = 0f; m_ticks--; m_target.Heal(m_perTick, true); if (m_ticks <= 0) Destroy(this); }
    }
}
// caller: destroy-then-readd to refresh instead of stack
foreach (var old in target.GetComponents<MyHoT>()) Destroy(old);
target.AddComponent<MyHoT>().Setup(target, 15f);
```

---

### Weapon Tier Auto-Scaling by Character Level

- **Purpose:** Automatically upgrade a starred (leveled-up-via-breeding) ally's starting weapon/shield from Bronze to Iron (1-star) or Blackmetal (2-star+) tier, since base loadouts are fixed at prefab-creation time and can't know a unit's eventual star rating in advance.
- **Key files:** `DvergrWeaponScaler.cs`, reuses `AllyPrefabManager.GetOrCloneWeaponWithMeleeAnim`.
- **Architecture:**
  - `Start()`: only proceeds if `m_nview.IsOwner()`, then `Invoke(nameof(DoScaleWeapons), 1.0f)` — delayed to run after vanilla's `GiveDefaultItems` has populated the inventory.
  - `DoScaleWeapons()` (idempotency-guarded): no-op if level ≤ 1. Otherwise builds a `Dictionary<string,string>` old→new prefab-name map per tier, unequips both hands, scans and removes matching inventory items **iterating backward** (to keep earlier indices valid during removal), for each replacement calls `GetOrCloneWeaponWithMeleeAnim`, adds it, then explicitly `Humanoid.EquipItem(invItem)`s it (adding alone does not auto-equip).
  - Finally appends the new prefabs into `m_defaultItems` so the AI's re-equip-defaults fallback remembers the upgraded tier.
- **How to implement:**
  1. Run the scaling pass once, delayed after `Start()`, gated by ZDO ownership and an idempotency flag.
  2. Build a per-tier `Dictionary<oldPrefabName, newPrefabName>`.
  3. Unequip both weapon slots before mutating inventory contents.
  4. When removing multiple inventory items by index, always iterate backward.
  5. After `Inventory.AddItem(prefab)`, you must explicitly find the resulting `ItemData` and call `Humanoid.EquipItem`.
  6. Update `Humanoid.m_defaultItems` afterward if you want the AI's own re-equip-defaults fallback to respect the upgrade.
- **Reusable pattern/snippet:**
```csharp
var replacements = level >= 3
    ? new Dictionary<string,string> { ["SwordBronze"]="SwordBlackmetal" }
    : new Dictionary<string,string> { ["SwordBronze"]="SwordIron" };
for (int i = items.Count - 1; i >= 0; i--) // backward removal
    if (replacements.ContainsKey(items[i].m_dropPrefab.name)) inventory.RemoveItem(i);
foreach (var newName in toAdd) {
    var prefab = CloneWeapon(newName);
    inventory.AddItem(prefab, 1);
    var added = inventory.GetAllItems().First(x => x.m_dropPrefab.name == newName);
    humanoid.EquipItem(added); // must equip explicitly
}
```

---

### Custom Staves / Items with Custom Projectiles

- **Purpose:** Demonstrates the general Jotunn recipe for adding brand-new weapon items (two magic staves — Earth, Spirit) that clone an existing vanilla item's visuals/animation but fire a custom projectile with different damage typing and effects.
- **Key files:** `CustomStavesManager.cs` (82 lines).
- **Architecture:**
  - Both staves follow the same recipe: `new CustomItem("NewName", "DvergerStaffFire")` (Jotunn's string-constructor clones an existing prefab, keeping its cast animation), rename via `ItemDrop.m_itemData.m_shared.m_name`, then separately clone a **projectile** prefab, edit the `Projectile` component's `m_damage` fields directly plus a custom `m_statusEffect`/`m_hitEffects`, then wire the new projectile into the weapon via `itemDrop.m_itemData.m_shared.m_attack.m_attackProjectile = newProjectile`, and finally `ItemManager.Instance.AddItem(customItem)`.
- **How to implement:**
  1. Pick a vanilla item whose *animation/cast behavior* you want to reuse and clone it via `new CustomItem(newName, baseName)`.
  2. Separately clone a *projectile* prefab from whichever vanilla spell has the closest base behavior.
  3. Edit the cloned projectile's `Projectile` component fields directly: `m_damage`, `m_statusEffect`, `m_hitEffects`.
  4. Point the weapon's `ItemData.m_shared.m_attack.m_attackProjectile` at your new projectile prefab.
  5. Register the final item with `ItemManager.Instance.AddItem(customItem)`.
- **Reusable pattern/snippet:**
```csharp
var item = new CustomItem("MyStaff", "DvergerStaffFire"); // clone visuals/animator
item.ItemDrop.m_itemData.m_shared.m_name = "My Staff";
var proj = PrefabManager.Instance.CreateClonedPrefab("MyStaff_projectile", "DvergerStaffIce_projectile");
var p = proj.GetComponent<Projectile>();
p.m_damage.m_frost = 0; p.m_damage.m_poison = 40f;
p.m_statusEffect = "Tar";
item.ItemDrop.m_itemData.m_shared.m_attack.m_attackProjectile = proj;
ItemManager.Instance.AddItem(item);
```

---

### Recruiter Contract (Instant-Summon Companion) & Merchant Injection

- **Purpose:** A second, instant acquisition path for companions besides slow feed-taming: a consumable item ("Dvergr Contract") that, when used, immediately spawns and auto-tames a random ally class near the player, sold by the vanilla trader Haldor.
- **Key files:** `RecruiterManager.cs` (137 lines), reuses `AllyPrefabManager.CustomVariants` as its spawn pool source.
- **Architecture:**
  - `CreateContractItem()`: clones vanilla `"Amber"` as the base item, sets `m_itemType = Consumable`, zeroes food-related fields, `m_value = 0` (prevents selling it back), and **explicitly sets `m_dropPrefab = contract.ItemPrefab`** — required to prevent a null ref inside the Trader's `BuyItem` RPC.
  - `SummonDvergrStatusEffect : StatusEffect` is created via `ScriptableObject.CreateInstance`, `m_ttl = 0.1f` (instantaneous), set as the item's `m_consumeStatusEffect`. **Critically, it IS registered via `ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(effect, false))` before being assigned to the item** — because Valheim networks status effects by an ID resolved through `ObjectDB`, so an unregistered `StatusEffect` reference on a consumable can throw during inventory-add/consume RPCs. (Contrast with `DvergrBerserkerRage`'s `SE_Stats`, which deliberately skips registration because it's applied directly rather than referenced by a networked item — registration is only required when a networked object *references* the effect by identity.)
  - `SummonDvergrStatusEffect.Setup(Character character)`: builds a spawn pool of 5 hardcoded base class names + every `AllyPrefabManager.CustomVariants[i].PrefabName` (reusing the same registry), picks one randomly, instantiates near the player, then calls `clone.GetComponent<Tameable>().Tame()` directly — **bypasses the entire timed feeding/taming process**, marking it tamed instantly.
  - `[HarmonyPatch(typeof(Trader))] [HarmonyPatch(nameof(Trader.Start))] Postfix`: checks `__instance.gameObject.name.Contains("Haldor")`, de-dupes, constructs a `Trader.TradeItem`, appends to `m_items`. Then **reflects over every `string` field of `Trader.TradeItem`** and force-sets any `null` value to `""` — defensively guards against future Valheim updates adding new required string fields that would otherwise stay `null` and crash `StoreGui`/Trader RPCs silently.
- **How to implement:**
  1. To add an item to a specific vanilla merchant without a full custom trader: Harmony-postfix `Trader.Start`, match the NPC by `gameObject.name.Contains("KnownTraderName")`, de-dupe against `m_items`, and append a manually constructed `Trader.TradeItem`.
  2. Defensively null-check/blank-fill every `string` field on any struct/class you construct manually for a vanilla API (via reflection over `GetFields()` filtered to `FieldType == typeof(string)`).
  3. For an instant-summon consumable: create a `Consumable`-type `CustomItem`, set `m_value = 0` if not resellable, explicitly set `m_dropPrefab` if it will ever be sold by a `Trader`, attach a custom `StatusEffect` subclass overriding `Setup(Character)` as `m_consumeStatusEffect`, and **register that status effect via `ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(se, false))` before wiring it onto the item**.
  4. In the `StatusEffect.Setup` override, spawn via `Instantiate` and call `Tameable.Tame()` directly to skip the normal taming timer for an instant-summon mechanic.
  5. Reuse whatever central "roster" data list your prefab factory already maintains as the summon pool.
- **Reusable pattern/snippet:**
```csharp
[HarmonyPatch(typeof(Trader), nameof(Trader.Start))]
public static class Patch_AddItemToTrader {
    [HarmonyPostfix]
    public static void Postfix(Trader __instance) {
        if (!__instance.gameObject.name.Contains("Haldor")) return;
        if (__instance.m_items.Any(i => i.m_prefab?.gameObject.name == "MyItem")) return;
        var trade = new Trader.TradeItem { m_prefab = myItemDrop, m_price = 999, m_stack = 1, m_requiredGlobalKey = "" };
        foreach (var f in typeof(Trader.TradeItem).GetFields())
            if (f.FieldType == typeof(string) && f.GetValue(trade) == null) f.SetValue(trade, ""); // future-proofing
        __instance.m_items.Add(trade);
    }
}
```

---

## Cross-Cutting Architectural Notes

- **Component-stack composition:** every ally prefab ends up with `Character` + `Humanoid` + `MonsterAI` (vanilla) plus `DvergrTameable` + `DvergrProcreation` + `DvergrGenetics` + `DvergrWeaponScaler` + `DvergrCombatAI` (+ `DvergrBerserkerRage` for one class) stacked as independent sibling `MonoBehaviour`s that cross-reference each other via `GetComponent<T>()` and coordinate through shared ZDO keys, rather than one monolithic AI class. This is a directly reusable architecture for any "companion simulation" mod.
- **One-shot Jotunn setup idiom:** every `*Manager.Setup()` static method unsubscribes itself from `PrefabManager.OnVanillaPrefabsAvailable` as its first statement.
- **ZDO as the persistence/sync layer:** all "genetic"/breeding state is stored as raw ZDO key/value pairs rather than custom RPCs or serialized fields — free save-persistence and multiplayer replication, at the cost of stringly-typed keys with no compile-time safety.
- **Ownership gating is inconsistent across files** — `DvergrGenetics.Awake`, `DvergrProcreation.Procreate`, and `DvergrWeaponScaler.Start` all gate on `m_nview.IsOwner()`, but `DvergrCombatAI.FixedUpdate` does not. Anyone reusing the Combat AI pattern should audit whether the underlying vanilla calls are self-safe for non-owner clients before assuming the un-gated pattern is fine to copy as-is.
- **Soft cross-mod compatibility** is handled uniformly via `Chainloader.PluginInfos.ContainsKey(guid)` cached once at boot into a `public static bool`, checked everywhere relevant, with reflection-based `GetComponent(string)`/`DestroyImmediate` to remove named components from an unreferenced assembly.
