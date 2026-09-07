# Valheim API Reference — for Jotunn-free BepInEx mods

**Generated 2026-07-31**, extended 2026-08-02 with files 10 and 11. Nine independent research passes over
`libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs`, cross-checked against real
serialized values in `libs-Tools\WubarrksEye_Dumps\2026-07-31_21-06-43\Values_Dump.json`.

**Every signature in these files carries a `:line` citation into the decompile.** Members that were
searched for and genuinely do not exist are listed under "NOT FOUND" at the end of each file — read
those before hunting for an API that isn't there.

This exists because **no working Jotunn-free item-cloning implementation existed in this workspace**.
Every prior item mod here (`MistsofAvalor`, `DvergrAllies`) is Jotunn-based, and Jotunn's
`CustomItem` / `PrefabManager.CreateClonedPrefab` / `ItemManager.AddItem` hide exactly the details
that matter when you do it yourself. These notes are that hidden layer, written out.

## Files

| File | Covers |
|---|---|
| [**00 — Consolidated reference**](00-CONSOLIDATED-REFERENCE.md) | ⭐ **The front door.** Reconciled summary of all nine passes: the 10 failures that crash / desync / brick a save, the single authoritative registration checklist, and explicit ⚠ CONFLICT callouts where two passes disagreed. Read this first, then drop into a detail file below. |
| [01 — Item cloning and registration](01-ITEM-CLONING-AND-REGISTRATION.md) | **Start here.** The three hash registers, the required order of operations, drop/save/load paths, and every way a modded item silently vanishes |
| [02 — ObjectDB](02-OBJECTDB.md) | `m_items` / `m_recipes` / `m_StatusEffects`, `UpdateRegisters`, `Awake` vs `CopyOtherDB` |
| [03 — ZNetScene and prefabs](03-ZNETSCENE-AND-PREFABS.md) | `m_prefabs` vs `m_namedPrefabs`, `GetPrefab`, ZNetView, safe cloning |
| [04 — ItemDrop / SharedData](04-ITEMDROP-SHAREDDATA.md) | Full `SharedData` field inventory, `ItemType` / `AnimationState` enums, `HitData.DamageTypes` |
| [05 — StatusEffect and SE_Stats](05-STATUSEFFECT-AND-SE-STATS.md) | Every field and virtual override, `SEMan`, set-bonus plumbing, `NameHash` |
| [06 — Recipes, stations, pieces](06-RECIPES-STATIONS-PIECES.md) | `Recipe`, `Piece.Requirement`, `CraftingStation`, `PieceTable` |
| [07 — World](07-WORLD-ZONESYSTEM-SMELTER-MINEROCK.md) | `ZoneSystem` / `ZoneVegetation`, `Heightmap.Biome`, `Smelter`, `MineRock`, `DropTable` |
| [08 — Attack, Projectile, effects](08-ATTACK-PROJECTILE-EFFECTS.md) | `Attack` fields, `AttackType`, `Projectile`, `EffectList`, eitr and bow-draw |
| [09 — Damage, ZDO, multiplayer](09-DAMAGE-ZDO-MULTIPLAYER.md) | `Character.Damage`, `HitData`, ZDO get/set, noise & stealth, which machine runs what |
| [10 — Minimap and map pins](10-MINIMAP-AND-PINS.md) | `PinData` fields, pin sizing and tinting (both are overwritten every `UpdatePins`), marker destroy/recreate, `m_pinUpdateRequired`, click vs double-click dispatch, acting on clicks over your own markers |
| [11 — Recolouring vanilla materials](11-MATERIALS-AND-TINTING.md) | ⚠ *Technique note, not a decompile pass.* Selecting emissive materials by the `_EMISSION` keyword, multiply-to-shift vs replace-to-match, and reversible tinting without leaking materials |
| [12 — Chat and custom routed RPCs](12-CHAT-AND-CUSTOM-RPC.md) | **`ZRoutedRpc.instance` is null for all of plugin `Awake` and is REPLACED every world join** — register per session or the mod silently does nothing. Plus the 6-generic ceiling and the `ZPackage` way past it, the asymmetric Shout-vs-Normal chat routes, why a prefix on `OnNewChatMessage` still reaches a strip that happens inside a closure (IL-verified), `IsChatDialogWindowVisible()` meaning "someone spoke in the last 10s" rather than "chat is open", the forced-case transpiler, a recipe for your own chat-shaped RPC that keeps vanilla's mute/filter/strip guarantees, and `m_worldLevel` reading 0 on prefab `ItemData` so cloned tooltips silently show world-level-0 stats |

## The five findings most likely to cost you a day

1. **`ZNetScene.m_prefabs` is read exactly once, inside `Awake`.** Adding to it later registers
   nothing. All runtime lookups go through the private `m_namedPrefabs` dictionary, which you must
   write directly — publicize the assembly.
2. **`Object.Instantiate` at runtime produces a LIVE scene object, not an inert template.** Its
   `ZNetView.Awake` creates a real ZDO that replicates to every peer. `m_forceDisableInit` is not an
   escape — it *destroys* the ZNetView. The only workable pattern is parenting under a GameObject
   that was `SetActive(false)` **before** anything was parented to it.
3. **A dedicated server that lacks your prefab PERMANENTLY DELETES the ZDO** of any dropped item
   using it (`ZNetScene.CreateObjectsSorted`). And there is **no prefab or ObjectDB validation on
   connect** — `RPC_PeerInfo` checks only the version string and `networkVersion == 36`. Enforce the
   lockstep yourself.
4. **Chest contents are one ZDO string.** A vanilla or stale client that opens a chest containing
   modded items and triggers a save writes the truncated inventory back — erasing those items for
   *everyone*, including modded players. This is the worst data-loss path in the game.
5. **A prefab name may not contain `(` or a space.** `ItemDrop` truncates at the first one while
   `ZNetScene` and `ObjectDB` hash the whole string, so the registers disagree and `m_dropPrefab`
   silently ends up null — which destroys the item on the next inventory save. `Instantiate` appends
   `"(Clone)"`, so renaming immediately is mandatory.
6. **`StatusEffect.NameHash()` hashes `base.name`, NOT `m_name`** — and caches the result on the
   first call without ever invalidating it. A `CreateInstance<T>()` effect starts with an empty/type
   name, so every unnamed effect hashes identically. Set `.name` before anything touches the effect.
   ⚠ **This settles a contradiction inside this workspace.** `Fatty\Synergies\SynergyEffects.cs:50`
   carries the comment *"NameHash() hashes this"* beside `m_name` — that comment is **wrong**;
   `DvergrAllies.md` has it right. Fatty happens to work anyway because it sets both fields. Copy
   Fatty's *code*, not its comment.
7. **`CopyOtherDB` aliases the item list by reference** (`m_items = other.m_items`), so the menu
   `ObjectDB` and the `FejdStartup` prefab share one `List<GameObject>` for the whole process.
   Registration must be genuinely **idempotent**, not merely "run once" — return to the main menu
   and back, and a non-idempotent add throws inside `UpdateRegisters` and half-builds the item DB.

## Provenance and limits

Produced by nine parallel research agents, each restricted to quoting the decompile with citations
and required to report NOT-FOUND explicitly rather than reconstruct a signature from memory. Values
are from a clean-profile dump (BepInEx + TheEye only), verified free of third-party contamination.

Two things live in `assembly_utils.dll`, which has no decompiled source here, so their exact
behaviour is **unverified**: `Utils.GetPrefabName` and the `string.GetStableHashCode()` extension.
Reference them from the assembly — do not reimplement `GetStableHashCode`, since a mismatched
implementation silently desynchronises every hash in the game.

First consumer: the `Runic` mod (`C:\WubarrkCODING\Runic`).
