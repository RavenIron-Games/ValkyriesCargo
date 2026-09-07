# 11 — Recolouring vanilla materials

**Written 2026-08-02.** ⚠ **Provenance differs from files 01–10.** Those are decompile passes with
`:line` citations into `assembly_valheim`. This one is not — materials live in Unity asset data, not
in code, so there is nothing to cite. It is a **cross-project technique note**, distilled from two
implementations that solved the same problem years apart in the same workspace:

- `BlightedHeart\BlightedItemsManager.cs` — `TintItemPrefab` / `TintMaterial` (item prefabs, permanent)
- `BlightedHeart\BlightedWorldHeartAI.cs` — `ApplyBiomeVisuals` (scene instance, biome colour)
- `TortalPortal\VFX\TortalVanillaVFXState.cs` — the portal frame tint (scene instance, **reversible**)

---

## The rule that matters

**Select emissive materials by the shader keyword, never by the colour value.**

```csharp
if (mat.HasProperty("_EmissionColor") && mat.IsKeywordEnabled("_EMISSION"))
```

Plenty of Valheim materials carry a **non-black `_EmissionColor` with `_EMISSION` switched off**. In
that state the value is inert and the surface does not glow at all. Selecting on "is the emission
colour non-black" therefore repaints timber, stone and metalwork that were never lit, which looks
like a bug and is very hard to attribute after the fact. Selecting on the keyword lands on exactly
the panels that actually emit.

**And never `EnableKeyword("_EMISSION")` on a material that lacked it.** BlightedHeart's comment
records the symptom directly: *"DO NOT force enable emission, as that causes the pale green glow."*
Switching emission on for a surface the artist left unlit produces a flat wash over the whole piece.

---

## Multiply to shift, replace to match

Two different intents, two different sums, and picking the wrong one is the second-most common way
this goes wrong:

| Intent | Operation | Example |
|---|---|---|
| **Shift** an existing colour toward a mood | `emission * tint` | BlightedHeart's blight: everything drifts sickly green while keeping its own character |
| **Match** a target colour exactly | replace hue, keep vanilla's intensity | TortalPortal's frame tint: the gate's glow must equal `VFXColour` |

Multiply cannot do "match": orange `(1.0, 0.5, 0.1)` × green `(0.3, 1.0, 0.55)` is a murky dark
yellow, not green. To replace while preserving how *fiercely* the surface burns, take the intensity
off the largest channel — emission is frequently HDR and greater than 1, so normalising it away
flattens a fierce glow into a faint one:

```csharp
Color emission = m.GetColor("_EmissionColor");
float power = Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b));
instance.SetColor("_EmissionColor", new Color(colour.r * power, colour.g * power, colour.b * power, emission.a));
```

For the albedo underneath (`_Color`), **blend rather than overwrite** — BlightedHeart uses
`Color.Lerp(mat.color, tint, 0.8f)`. Pushing it all the way to the target flattens the surface's own
shading into a single slab. Leave alpha alone; it is doing transparency, not brightness.

---

## Ownership: `.materials` vs `new Material(...)`

`renderer.materials` and `renderer.material` **auto-instance** — Unity silently copies the shared
asset on first access, so the prefab asset is safe. That is the right tool when the tint is
permanent, which is why both BlightedHeart call sites use it.

It is the wrong tool when the tint must be **reversible** (a live config toggle, a colour dial the
player moves at runtime). You cannot restore what you did not record, and you cannot destroy copies
you do not hold a reference to. The reversible pattern:

1. Capture `renderer.sharedMaterials` **before** touching `.materials` — reading `.materials` is
   itself what triggers instancing.
2. Build explicit `new Material(shared[i])` copies, keep them in a list you own.
3. Assign the array back through `renderer.sharedMaterials = replacement`.
4. To restore: reassign the captured originals, then `Object.Destroy` every copy you made.

**Materials created from script are not garbage collected.** Skipping step 4 leaks one material per
glowing panel per object, *every time the colour changes* — which on a live colour dial is once per
drag frame.

**Never write to `renderer.sharedMaterial` directly.** That is the prefab's own asset: it recolours
every instance in the world, including ones your component knows nothing about, and it survives until
the game restarts.

---

## Lights

`GetComponentsInChildren<Light>(true)` and setting `l.color` covers the warm point light that
accompanies most glowing Valheim props. Set **hue only** — leave `intensity`, `range` and shadow
settings alone, so the object lights its surroundings exactly as far as it always did.

---

## Checklist

- [ ] Gate on `IsKeywordEnabled("_EMISSION")`, not on the colour value
- [ ] Never `EnableKeyword("_EMISSION")` on a material that lacked it
- [ ] Multiply to shift, replace-with-preserved-intensity to match
- [ ] `Lerp` the albedo, do not overwrite it; leave alpha untouched
- [ ] Capture `sharedMaterials` before reading `.materials`
- [ ] `Object.Destroy` every script-created material when undoing
- [ ] Skip `ParticleSystemRenderer` and `LineRenderer` — both derive from `Renderer` and will be
      caught by `GetComponentsInChildren<Renderer>()`, which is rarely what you meant
- [ ] Log the count of what you repainted; a count of `0` is the signal that your selector missed
