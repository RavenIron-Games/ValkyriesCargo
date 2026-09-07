# Runic — Jotunn-free custom item creation (weapons, staves, armor, ore chain)

**Project:** `C:\WubarrkCODING\Runic` · **Status: PAUSED 2026-08-01, mid-implementation.**
See `Runic\STATUS-PAUSED.md` for the exact file-by-file state. The user is deciding whether to
redirect it into a full magic mod.

**Why this doc exists even though the mod is unfinished:** the research and the registration spine
are finished, reviewed and reusable, and they solve a problem **no other project in this workspace
had solved** — adding custom items to Valheim without Jotunn. That knowledge is worth more than the
mod itself and should not have to be re-derived.

---

## 1. The gap this project filled

Every prior item-adding mod here (`MistsofAvalor`, `DvergrAllies`) is **Jotunn-based**. Jotunn's
`CustomItem` / `PrefabManager.CreateClonedPrefab` / `ItemManager.AddItem` hide the registration
layer entirely. A search across every Jotunn-free project in the workspace (`TortalPortal`, `Fatty`,
`Njord`, `ShadowsOfMidgard`, `Wonderland`) for `m_items.Add` / `CreateClonedPrefab` / any
prefab-clone registration returned **zero hits**.

So this was original work, and it is now written down in
**[`libs-Tools\VALHEIM-API-REFERENCE\`](../VALHEIM-API-REFERENCE/README.md)** — 9 area files plus a
consolidated front door, every signature cited to a decompile line, with explicit NOT-FOUND lists.

**Read `01-ITEM-CLONING-AND-REGISTRATION.md` before adding a custom item to any Valheim mod.**

---

## 2. The registration spine — reusable as-is

Two files, both complete and compiling, both content-agnostic:

- `Runic\Core\PrefabCloner.cs` — clone a donor, validate the name, collision-check both hash
  registers, register into `ObjectDB` and `ZNetScene`.
- `Runic\Core\RunicRegistry.cs` — the re-entrant orchestrator wired to `ObjectDB.Awake`,
  `ObjectDB.CopyOtherDB` and `ZNetScene.Awake`.

Port these by renaming the namespace. The five rules they encode:

### 2.1 `ZNetScene.m_prefabs` is dead after `Awake`
It is read at exactly two lines in the whole assembly: its declaration and the `Awake` loop
(`:69584`, `:69607`). Every runtime lookup goes through the **private readonly**
`m_namedPrefabs` dictionary (`:69588`). A postfix that only appends to `m_prefabs` registers
**nothing**. You must write the dictionary directly.

### 2.2 The publicizer is not optional
`ObjectDB.UpdateRegisters()` is private (`:90528`) and `ZNetScene.m_namedPrefabs` is private
readonly. Both are required. **Verified working** via `BepInEx.AssemblyPublicizer.MSBuild` 0.4.1 +
`<Publicize>true</Publicize>` — `PrefabCloner.cs` compiles clean against both.
(`readonly` blocks reassignment, not `.Add`.)

### 2.3 `Object.Instantiate` at runtime is NOT inert
It produces a **live, replicated entity at world origin**. `ZNetView.Awake` reaches
`ZDOMan.CreateNewZDO` + `ZNetScene.AddInstance`. Neither vanilla escape hatch works:
`m_forceDisableInit` *destroys* the ZNetView (so the template can never be dropped), and
`StartGhostInit`/`FinishGhostInit` still creates the ZDO.

**The only working pattern** — and there is no vanilla helper for it, this is original:
```csharp
var holder = new GameObject("YourMod_PrefabContainer");
holder.SetActive(false);                       // MUST precede any parenting
Object.DontDestroyOnLoad(holder);

GameObject clone = Object.Instantiate(donor, holder.transform, false);
clone.name = "YourItemName";                   // strips "(Clone)" — this IS the identity
```

### 2.4 A duplicate name hash bricks the game
`ZNetScene.Awake` uses `m_namedPrefabs.Add` (`:69609`) and `ObjectDB.UpdateRegisters` uses
`m_itemByHash.Add` (`:90534`) — both throw `ArgumentException` on a duplicate, and `ZNetScene.Awake`
aborting means the `ZDOMan` hookup and the `SpawnObject` RPC never register. Always collision-check
first. Also: **a name may not contain `(` or a space** — `ItemDrop` truncates at the first one while
the two registers hash the full string, so they silently disagree.

### 2.5 Registration must be idempotent, not "run once"
`CopyOtherDB` does `m_items = other.m_items` — a **reference** assignment, so the menu `ObjectDB`
and the `FejdStartup` prefab share one list for the whole process. Main menu → world → main menu
runs it again; a non-idempotent add throws inside `UpdateRegisters` and half-builds the item DB.

---

## 3. The ServerSync timing trap (applies to every mod here)

**Config values are NOT available at `ObjectDB.Awake`.** ConfigSync delivers the server's values
during the `ZNet` connect handshake, long after `Awake` has fired against the main-menu DB. Any
value read there is the client's local default, and baking it into a cloned `SharedData` means the
server's setting **silently never applies**.

The discipline, encoded in `RunicRegistry`:
- Gate object **creation** on "does it already exist" — once only.
- Re-apply every config-derived **value** unconditionally on **every** pass.
- Treat `Awake` as "make the objects exist" and `CopyOtherDB` (which runs in-game, post-handshake)
  as "write the real numbers".
- Never cache a `ConfigEntry<T>.Value` in a `static readonly` field or a static constructor.

Also documented as gotcha 8 in [`Fatty.md`](Fatty.md); Fatty marks the live workaround in-code at
`Synergies\DrinkBuffs.cs:119`.

### 3.1 The compounding-multiplier corollary
Because registration re-runs, **donor-transform must read the DONOR's value, never the clone's**.
Reading the clone compounds: pass two multiplies an already-multiplied number, and a player who
reconnects three times is wearing `1.1³` armor. `RunicArmorFactory.cs` gets this right and documents
it; copy that shape.

---

## 4. Multiplayer failure modes that have no warning

Three ways custom items are destroyed, none of which produce an obvious error:

1. **A server missing the prefab PERMANENTLY DELETES it.** `ZNetScene.CreateObjectsSorted`
   (`:69798-69804`): `GetPrefab` returns null → `if (IsServer())` → `ZDOMan.DestroyZDO`. On a
   *client* the same miss is completely silent and retried forever.
2. **A chest round-trip destroys modded items for everyone.** Container contents are one base64
   `ZPackage` in one ZDO string. `Inventory.Load` skips entries whose name misses `ObjectDB`; the
   next `Container.Save` writes the truncated package back to the **shared** ZDO. One vanilla client
   opening the chest erases the items for modded players too. This is the worst data-loss path.
3. **`m_dropPrefab == null` ⇒ silent permanent inventory loss.** It is `[NonSerialized]`, set only at
   runtime by `ItemDrop.Awake` from `ObjectDB`. If ObjectDB lacks the entry it stays null,
   `Inventory.Save` writes an empty string, and `Inventory.Load` skips it. Gone, no exception. The
   "self-heal" line beneath it is inside `if (Application.isEditor)` and never runs shipped.

**And there is no prefab or ObjectDB validation on connect** — `ZNet.RPC_PeerInfo` checks only the
version string and `networkVersion == 36`. You must enforce client/server lockstep yourself via
ServerSync.

---

## 5. Live-measured donor data (2026-07-31 clean-profile dump)

Corrections found by measuring rather than name-grepping. **Five weapon/staff donor names in the
original plan were wrong**, and three world-prefab assumptions were wrong:

| Assumed | Reality |
|---|---|
| `Greatsword` | Does not exist. The 2H sword line is `THSwordWood` → `THSwordKrom` → `THSwordSlayer` |
| `Mistwalker` | Real name `SwordMistwalker`, and it is **one-handed** — nothing to do with the scythe |
| `DvergerStaffHeal` | Not a staff: `ItemType.Utility`, `AnimationState.OneHanded`, levels **Swords** |
| "all staves are `TwoHandedWeaponLeft`" | **Exactly one item in the game** uses it (`StaffSkeleton`). Every other staff is plain `TwoHandedWeapon` + `AnimationState.Staves` |
| `MineRock_Flametal` | Does not exist. The vein is a **two-stage pair**: `FlametalRockstand` (a 1-HP `Destructible`) spawns `FlametalRockstand_frac` (the `MineRock5` carrying health + drop table). Clone **both** and rewire `m_spawnWhenDestroyed` |
| `FlametalOre` / `Flametal` | **Deprecated.** Display tokens are literally `$item_flametalore_old` / `$item_flametal_old`. The live chain is `FlametalOreNew` → `FlametalNew`, per blastfurnace's own conversion table |
| "flametal vegetation entry" | **No flametal `ZoneVegetation` entry exists** — checked all 156 records and all 188 locations. Flametal veins are not placed by `m_vegetation` |

Other useful measurements:
- Armor bases: Flametal **38**, AshlandsMedium **28**, Mage **16**. All nine pieces share
  `m_armorPerLevel = 2` and `m_maxQuality = 4` — an evenly spaced heavy/medium/light ladder.
- `ArmorMage` `m_eitrRegenModifier` = 0.2 / 0.4 / 0.4, summing to **1.0** for a full set.
- **Vanilla already ships this mod's core pattern**: `THSwordSlayerNature` = 170 slash + 10 poison +
  `m_attackStatusEffect = ImmobilizedAshlands` @ `m_attackStatusEffectChance = 0.2`. So the
  elemental rider is ~6 % of physical and the proc anchor is **0.2**, not the 25–30 % typically
  reached for.
- The AshlandsMedium armor trio ships a **live vanilla set** (`AshlandsMediumArmor` +
  `SetEffect_AshlandsMediumArmor`). Clone it unchanged and your set grants vanilla's bonus *and*
  mixes with real Ashlands pieces. Overwrite all three set fields.
- `blackforge` costs `BlackMarble×10`, `YggdrasilWood×10`, `BlackCore×5`; station is
  `m_craftRequireFire=false`, `m_craftRequireRoof=true`, `m_rangeBuild=20`, `m_useDistance=1.7`.
- `blastfurnace` already contains a **duplicate** conversion (indices 0 and 2 are both
  `FlametalOreNew→FlametalNew`), so any append must be idempotent and cannot assume a clean list.

---

## 6. `Humanoid.GetSetCount` counts eight slots — including both hands

`:14328` counts equipment slots by `m_setName`, and that includes `m_rightItem`. Useful (a weapon
can contribute to an armor set bonus) and dangerous: if a **weapon** and an **armor set** share a
`m_setName`, the armor's piece threshold completes early. Runic hit exactly this — 2 armor pieces +
1 runic weapon satisfied a 3-piece set. Give a weapon-only bonus its own distinct `m_setName`.

`HaveSetEffect` (`:14311`) requires a non-null `m_setStatusEffect`, a **non-empty** `m_setName`
**and** `m_setSize > 1` before it counts anything. The Mage donors ship `m_setSize = 3` with an empty
`m_setName` — that is vanilla's idiom for "no set".

---

## 7. VFX — procedural runes, and a bug in the donor

`Runic\VFX\RunicRuneTexture.cs` and `RunicRuneMaterial.cs` are ported from
`TortalPortal\VFX\` (originally Wings of the Valkyrie) and are complete.

- **The shader priority chain is the valuable part.** Shipping a *custom* shader into Valheim is
  what turns modded materials pink — it is not in the game's build and cannot compile at runtime.
  Only use stock shaders; the chain is `Particles/Standard Unlit` → `Legacy Shaders/Particles/Alpha
  Blended` → `Particles/Alpha Blended` → `Particles/Additive`, with a scoring fallback over loaded
  materials.
- **The 4×4 atlas matters**: it lets one `ParticleSystem` show a *different* glyph per particle via
  texture-sheet animation with a random start frame.
- **⚠ Bug in the donor:** `TortalRuneTexture.GenerateRune(color, index)` fuses `case 7:` with
  `default:`, so glyphs **8–15 are unreachable** in the single-texture path — every index above 7
  silently draws the Sowilo zigzag. The atlas path has all 16. Runic's port declares the glyph table
  once as data so the two paths cannot drift. **If you reuse TortalPortal's version, fix this first.**
- Two more donor fixes carried in Runic's port: `Shader.Find(...) ?? Shader.Find(...)` is unsafe
  because Unity overloads `==` and `??` uses CLR-null only; and the donor's `ClearCache` empties the
  dictionary without `Destroy`-ing the materials, leaking one native allocation per entry.

---

## 8. Files worth stealing

| Path | Why |
|---|---|
| `Runic\Core\PrefabCloner.cs` | The whole Jotunn-free clone+register solution, heavily commented with decompile citations |
| `Runic\Core\RunicRegistry.cs` | The re-entrant registration pattern with per-group try/catch |
| `Runic\VFX\RunicRuneTexture.cs` | 16-glyph procedural rune generator + atlas, cached, donor bug fixed |
| `Runic\VFX\RunicRuneMaterial.cs` | Stock-shader discovery — the anti-pink-material knowledge |
| `Runic\Core\RunicNames.cs` | Pattern for treating prefab names as append-only save-file identity |
| `Runic\Configuration.cs` | 43-entry ServerSync config with the local-vs-synced split reasoned out |

## Related

- [`../VALHEIM-API-REFERENCE/`](../VALHEIM-API-REFERENCE/README.md) — the full API research
- [`Fatty.md`](Fatty.md) — the `ObjectDB.Awake` + `CopyOtherDB` dual-hook this builds on, and the
  `SE_Stats` multiplier trap (a default of 0 on `m_damageModifier` deletes all the player's damage)
- [`TortalPortal.md`](TortalPortal.md) — the scaffold donor and the rune VFX origin
- [`DvergrAllies.md`](DvergrAllies.md) — the `.name`-not-`.m_name` gotcha, and the Jotunn-shaped
  version of the custom-projectile pattern
- [`SharedInfrastructure.md`](SharedInfrastructure.md) — ServerSync
