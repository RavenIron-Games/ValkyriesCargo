# Driving Vanilla Pieces From a Mod — without patching them

**Applies to every project in `c:\WubarrkCODING`.** Written 2026-08-15 out of AwayFromHome v1.0.0, which
had to make a custom build piece hand ore to a `Smelter`, hold a real `Container`, and draw a shaped
outline on the ground — three jobs that all look like "add a Harmony patch" and are all better done from
the outside.

The theme: **Valheim's pieces already have an entry point for "a player just did this to me."** Calling
that entry point is almost always better than patching the method behind it. It costs no patch surface, it
keeps every other mod's patches on that method running, and it inherits all of vanilla's own validation
for free. What it demands in return is that you copy the caller's **order of operations exactly**, because
that order is usually load-bearing in a way the method signature does not advertise.

---

## 1. Copy the player's hand, including the order — the check must precede the removal

`Smelter.OnAddOre` is what runs when a player puts ore in a smelter. Stripped to its spine:

```csharp
if (!IsItemAllowed(item.m_dropPrefab.name)) { user.Message("$msg_wontwork"); return false; }
if (GetQueueSize() >= m_maxOre)             { user.Message("$msg_itsfull");  return false; }
user.GetInventory().RemoveItem(item, 1);
m_nview.InvokeRPC("RPC_AddOre", item.m_dropPrefab.name);
```

Reproduce that and a mod can stock a furnace with no patch at all. **But `RPC_AddOre` re-validates on
arrival and silently drops anything it does not like:**

```csharp
private void RPC_AddOre(long sender, string name)
{
    if (m_nview.IsOwner())
    {
        if (!IsItemAllowed(name)) { ZLog.Log("Item not allowed " + name); return; }   // <- item is GONE
        QueueOre(name);
        ...
    }
}
```

So removing first and asking afterwards **deletes the player's item into nothing** — no exception, one
`ZLog` line nobody reads. The check-before-remove ordering is not style; it is the only thing standing
between your mod and a bug report titled *"your mod ate my iron"*.

**Generalise this.** Before driving any vanilla piece from outside, read its own caller and ask: *does the
receiving end re-validate, and what happens to my resource if it says no?* Anywhere the answer is "it
returns quietly", every irreversible step must come after every check.

## 2. `m_shared.m_name` and `m_dropPrefab.name` are different strings, and mixing them loses items

- `m_shared.m_name` — the display/localisation token. What `Inventory.GetItem(string)`,
  `Inventory.HaveItem`, `Inventory.RemoveItem(string, int)` and `Smelter.m_conversion` match on.
- `m_dropPrefab.name` — the prefab name. What `IsItemAllowed` and `RPC_AddOre` validate.

Vanilla uses both, in the same method, two lines apart, and it is right both times. A mod that picks one
and uses it everywhere will either fail to find items that are present or hand the receiver a string it
rejects — and per §1, the second one destroys the item.

`Inventory.GetItem` is the trap's quiet edge:

```csharp
public ItemDrop.ItemData GetItem(string name, int quality = -1, bool isPrefabName = false)
```

It defaults to matching `m_shared.m_name`. Passing a prefab name without setting `isPrefabName: true`
returns null, which reads as "the container is empty" rather than "you asked the wrong question".

## 3. `ZRoutedRpc.InvokeRoutedRPC` runs SYNCHRONOUSLY when the target is yourself

```csharp
if (targetPeerID == m_id || targetPeerID == 0L) HandleRoutedRPC(routedRPCData);
if (targetPeerID != m_id)                       RouteRPC(routedRPCData);
```

`ZNetView.InvokeRPC(method, args)` targets `m_zdo.GetOwner()`. So **if you own the object, the RPC
executes inline before `InvokeRPC` returns** — no frame delay, no packet, nothing in flight.

That is worth designing around. AwayFromHome requires ownership of *both* the container it takes from and
the smelter it gives to, which turns take-and-give into a single synchronous step that a disconnect cannot
tear in half. Ownership costs nothing to require when your mod already claims the objects it tends; it
buys an atomicity guarantee you would otherwise have to build.

**Corollary:** `targetPeerID == 0L` also dispatches locally. A ZDO with no owner is not a no-op.

## 4. Register arities differ between `ZRoutedRpc` and `ZNetView`

| | Max type parameters after the sender/`long` |
| :--- | :--- |
| `ZRoutedRpc.Register<T,U,V,B,K,M>` | **6** |
| `ZNetView.Register<T,U,V,B>` | **4** |

Outgrow them and you are packing a `ZPackage` by hand and versioning a parser. Check the arity *before*
designing the message — it is cheaper to split an RPC than to retrofit a blob. (AwayFromHome's leash RPC
carries `(ZDOID, int, float, float, float)` = 5, deliberately as separate primitives so the wire stays
readable in a log and a future field is an added parameter rather than a format bump.)

## 5. Putting a vanilla `Container` on a custom piece — three silent failures

All three produce a container that *looks* mounted and does nothing. None logs.

1. **`Container` implements `Hoverable` AND `Interactable`, and so does your piece's own hover script.**
   `Hud.UpdateCrosshair` and `Player.Interact` both resolve with `GetComponentInParent<T>()`, which returns
   whichever match Unity happens to order first — and that order can differ between a fresh place and a
   world reload. Two on one object is a coin-flip readout. **Fix: put the `Container` on a CHILD object
   with no collider.** Nothing can raycast-hit it, `GetComponentInParent` walks *up* from the hit collider
   and never sees it, and opening becomes something you do deliberately by calling `Container.Interact`.

2. **`Container.Awake` is private and its entire body is inside `if (m_nview.GetZDO() != null)`.** Lose the
   race with `ZNetView.Awake` and it never builds an `Inventory`, never registers its six RPCs, never
   starts `CheckForChanges` — and never says so. Private also means a subclass cannot call it: Unity
   dispatches only the **most derived** `Awake`, so `class MyContainer : Container` with an `Awake`
   silently replaces the real one. **Fix: build it from `Start()`** (guaranteed after every `Awake`) and
   never subclass `Container`.

3. **`AddComponent` runs `Awake` immediately.** Setting `m_width`/`m_height`/`m_rootObjectOverride`
   afterwards is too late — the `Inventory` is already built at the default size and `m_nview` already
   resolved. **Fix: create the child INACTIVE, configure it, then `SetActive(true)`.**

**Where the data lives:** `m_rootObjectOverride` (a `ZNetView`) points `Container` at the parent's ZDO, so
the items save under the parent's `ZDOVars.s_items`, travel with the piece, and survive restarts. It also
makes `Container` find the parent's `WearNTear`/`Destructible`, so knocking the piece down spills the
contents the way a player expects. It does **not** redirect `m_piece` — which is why
`PrivacySetting.Private` is unusable on a child (it keys off `GetComponent<Piece>()` on its *own*
GameObject, so it is a null dereference rather than a permission check). Use `Public` +
`m_checkGuardStone = true` and let the player's ward answer "who may open this".

**Payoff for doing it this way:** other container-aware mods see it with zero compatibility code.
AzuAutoStore, for one, registers containers from a postfix on `Container.Awake` and every eligibility test
it applies reads the **`m_nview` field** — which respects `m_rootObjectOverride` — rather than re-fetching
a `ZNetView` or a `Piece` off the container's own GameObject. It keys its per-container config on
`transform.root.name`, so a child-mounted container presents as the *parent piece's* prefab name, which is
what you want. Its one non-obvious gate is that `m_nview.GetZDO().GetLong(ZDOVars.s_creator, 0L)` must be
non-zero — free if the piece went through `Player.PlacePiece`, absent if you spawned it programmatically.

## 6. `CircleProjector` draws circles and nothing else

It is the only ground-outline component in the game — workbench build radius, guard stone ward, turret
arc. `LineAttach`/`LineConnect` are the only other line drawers and have no terrain awareness at all.

If you need a non-circular outline, do **not** subclass it: its `Update` is private and unconditional, so
a subclass draws its own circle underneath yours forever. Harvest the two fields that matter —
`m_prefab` (the segment) and `m_mask` (the ground-raycast layers, which you will get wrong if you guess)
— and write your own. Find them by **scanning `ZNetScene.m_prefabs` for the first `CraftingStation` with
an `m_areaMarker` carrying a `CircleProjector`**, not by hardcoding a prefab name: no prefab-name string
for the workbench exists anywhere in the decompile to check a guess against, and a wrong name fails
silently.

Its `Update` does three things, and every one of them is a thing a naive reimplementation gets wrong and
only notices on sloped ground:

1. Raycast each segment **down** (vanilla: from +500 m, for 1000 m) and seat it on the hit. Without this
   the outline is a flat plane at the object's height — buried or floating on any hill.
2. Rotate each segment to look from its previous neighbour toward its next, **in a second pass**. The
   segment art is a directional dash. The split matters: aiming uses neighbours' *final seated* positions,
   so doing both in one loop aims each segment at where its successor used to be, which reads as a visible
   twist in the line on a slope.
3. Space segments by **arc length**, not by angle, if the shape is not a circle. An angular
   parameterisation bunches them at the short ends of a long rectangle.

**And whatever draws the outline must share its maths with whatever ENFORCES the boundary.** If the drawn
shape and the containment test are two implementations, they can disagree — and a pen that draws where the
player's fence is while correcting somewhere else looks exactly like a broken feature and is nearly
unreadable from a log. Put `Contains()` and `PerimeterPoint()` on one type. Do the rotation with
`Quaternion.Euler(0, ±yaw, 0)` in both, so the inverse is a minus sign in one obvious place rather than a
hand-transposed matrix somebody has to check.

## 7. The publicizer reaches private members; that does not make them public API

`BepInEx.AssemblyPublicizer.MSBuild` over `assembly_valheim` makes `Smelter.GetQueueSize()`,
`GetFuel()`, `IsItemAllowed()` and `m_nview` directly callable. Use them — reimplementing
`GetQueueSize()` as "read `ZDOVars.s_queued`" duplicates a decision that can change under you.

But they are private *by intent*, so treat every one as a version-fragile dependency: assert your
assumptions in the code that uses them, and re-verify against a fresh decompile after any game patch.
Copy vanilla's own boundary tests verbatim rather than simplifying them — `GetFuel() > m_maxFuel - 1`
and `GetFuel() < m_maxFuel` differ at the boundary, and the difference is one wasted log of wood per
top-up, forever.

## 8. A cloned donor material carries its texture TILING, and no property dump will show it

*(AwayFromHome 1.0.2 → 1.0.6, 2026-09-01 — four releases to find one pair of numbers.)*

The cheap way to get Valheim's own shader, lighting and weather on a custom mesh is to clone a vanilla
piece's material — `new Material(donor.sharedMaterial)` — and `SetTexture` your own maps into it.
AwayFromHome does this with `sign_notext`. Three things ride along with that clone and none of them is
what you asked for:

- **`_Color`** — `Custom/Piece` multiplies it into `_MainTex`, so the donor's tint recolours your albedo.
- **`_ValueNoise`** — Valheim's per-piece world-space mottle, shipped at 0.3 on the sign. Invisible on a
  wall of identical planks, a dirty overlay on a hand-textured one-off.
- **`_MainTex_ST`** (and `_BumpMap_ST`, `_EmissionMap_ST`…) — the per-slot **scale/offset** the shader
  applies to the mesh UVs before sampling. `SetTexture` does not reset it. A sign *tiles* its plank
  texture across a board; `sign_notext` carries `scale=(0.33, 0.30)`. Over a fragmented, 1:1 atlas that
  samples the map at roughly 3× frequency, so every UV island lands on the wrong part of the atlas and the
  model wears hard-edged confetti of the atlas's own colours — cap-blue on the trunk, gill-cream on the
  caps, bark-black everywhere.

The first two were found and fixed in one release. The third took three more, and the reason is the
lesson: **ST vectors are not shader properties.** A diagnostic that walks `Shader.GetPropertyCount()` /
`GetPropertyType()` — the natural way to write "dump this material" — lists every texture, float, range,
colour and vector the shader declares and **never mentions scale/offset**, because they are per-texture
state read with `Material.GetTextureScale/Offset`. So the client log said `_MainTex` was bound, tinted
white, noise off — a complete-looking picture with the one wrong number cropped out — and three releases
of texture work (a tint, a shadow lift, a tone remap) were aimed at a symptom. Meanwhile every stage of the
asset pipeline was being proven faithful: shipped DXT1 correlating 0.996 with the source, bundled mesh
matching the GLB index-for-index, bundle bytes present in the DLL.

**Do this when cloning any donor material:**

```csharp
foreach (string slot in new[] { "_MainTex", "_BumpMap", "_EmissionMap", "_MetallicTex" })
{
    if (!m.HasProperty(slot)) continue;
    // log GetTextureScale/Offset BEFORE the reset, so a client log states what the donor had
    m.SetTextureScale(slot, Vector2.one);
    m.SetTextureOffset(slot, Vector2.zero);
}
```

and print `[st scale/offset]` beside every texture in whatever material dump you keep. glTF materials
without `KHR_texture_transform` specify identity, so identity is not a preference — it is what the asset
says.

**The generalisation:** when every stage of an asset is proven byte-faithful and it still renders wrong,
the remaining suspects are the parts of the material that are *not* properties — ST vectors, shader
keywords, render queue. Print those too, before touching the texture again. And a texture reported "too
dark" in shade and "too bright" in daylight from the *same* patchwork is telling you the colours are in
the wrong **places**, not at the wrong **levels**.

---

## The meta-lesson

Every item above is a case where the **obvious** move is a Harmony patch and the **better** move is a
call. Patching `Smelter.OnAddOre` to inject ore would work, would fight every other mod that patches it,
and would still have needed §1's ordering. Patching `MonsterAI.FindClosestConsumableItem` to feed animals
from a chest would work, and would put a hook on the hottest per-creature path in the game — and it still
must *return an `ItemDrop`*, so you build the real item anyway.

**Ask what a player's hand does, and do that.** The piece already knows how to be used.

See also: `HEADLESS-AND-EMPTY-SERVER-FACTS.md` (what breaks when nobody is logged in),
`IMPLEMENTATIONS/AwayFromHome.md` (the mod all of this came out of).

---

## Item and hull ids: look them up, never recall them (verified 2026-09-01, Njord 2.0.0)

The display name and the prefab id routinely differ, and the differences are not guessable:

| Shown in game | Prefab id |
| :--- | :--- |
| Ancient Bark | `ElderBark` |
| Bronze nails | `BronzeNails` *(plural)* |
| Iron nails | `IronNails` |
| Leather scraps | `LeatherScraps` |

Njord shipped `BronzeNail` and `AncientBark` in its default hull prices from memory. Neither
exists, so **three of five hulls were unbuyable** and the only symptom was a shop line reading
"there is no item called …" — indistinguishable from a mod that is not installed.

**The source of truth is `libs-Tools/WubarrksEye_Dumps/<date>/ObjectDB_Dump.json`**, and grepping
it takes seconds:

```bash
grep -oiE '"[A-Za-z_]*nail[A-Za-z_]*"' ObjectDB_Dump.json | sort -u
```

`Prefabs_Dump.json` is the equivalent for world objects — it is how vanilla's `Trailership` was
found to be a real ship prefab rather than a mod's.

**Two corollaries.** When a configured id misses, say which real id is nearest rather than only
that it does not exist; the near-miss is the common case and it turns a dead end into a fix. And
for a hull or piece's **display** name, read the prefab's own `Piece.m_name` through
`Localization.instance.Localize` instead of hardcoding a map — the game already knows the name, in
the player's language, for modded content too.

