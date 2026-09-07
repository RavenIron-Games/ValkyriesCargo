# Putting a custom CHARACTER in Valheim from an AssetBundle

Found the hard way on 2026-09-07 baking Ingvar the Far-Travelled for **Valkyrie's Cargo**, on a
Linux box with Unity 6000.0.61f1 and Blender 5.2.0 LTS. Every fact below was seen on a screen, not
reasoned about; where a thing was tried and did not work, that is recorded too, because the round
trip is fifteen minutes and nobody should spend it twice.

This is the character-shaped companion to `VANILLA-PIECE-INTEROP-FACTS.md` §8 (donor materials on a
static PIECE) and to `HEADLESS-AND-EMPTY-SERVER-FACTS.md` (the `-nographics` blank-texture trap,
which this bake guards against and did not hit). A skinned character adds a rig, a clip set and a
bind pose, and all three have their own ways of being silently wrong.

**The through-line: five separate defects, and every one of them produced a bundle that passed every
gate a build script can check.** Right size, right asset count, `SkinnedMeshRenderer=True`, correct
bone count, all six clips present, exit code 0. The build script cannot see any of this. **Only a
screen can.**

---

## 1. Clip names carry the exporter's prefix, and it breaks two things at once

Blender's FBX exporter names actions `Armature|<Clip>` — and in this asset **doubly**,
`Armature|Armature|Walk`, which Unity imports as `Armature|Walk`.

A clip animation's `name` is what the clip asset is **called** in the bundle. Any loader that
resolves clips by name — which is the obvious way to drive a `PlayableGraph` — misses all of them,
every weight stays 0, and the character stands frozen.

Worse, it silently defeats the loop table in the same loop:

```csharp
takes[i].loopTime = Looping.Contains(takes[i].name);   // matching {"Walk","Idle"} against "Armature|Walk"
```

Nothing matches, so **everything imports non-looping**, and a non-looping idle freezes on its last
frame after ten seconds — which reads as a bug in the animation driver, not in the bake.

**Strip the prefix before deciding looping**, in that order, and fail the build if a `|` survives:

```csharp
int bar = takes[i].name.LastIndexOf('|');
if (bar >= 0) takes[i].name = takes[i].name.Substring(bar + 1);
takes[i].loopTime = Looping.Contains(takes[i].name);
```

**And report the clips as they came OUT of the import, not as you asked for them** — name, `length`,
`isLooping`, `frameRate`, one line each. That report is what caught the next three defects.

## 2. Stray geometry ships, and the triangle count is the only tell

The FBX carried an unparented 2 m `Icosphere` with no material — leftover source geometry. In game
it rendered as a **white ellipsoid swallowing the character whole**.

The build script had reported `tris=31112`, because it looked at
`GetComponentInChildren<SkinnedMeshRenderer>()` — the first one, singular. The runtime reported
**31192**. That 80 is the sphere, and an 80-triangle discrepancy in a 31k mesh looks exactly like
rounding noise until you subtract it.

**Enumerate every `Renderer` in the imported asset and print each one**, not just the skinned one you
expect. A second renderer in a character bundle is always wrong.

## 3. `sharedMesh.bounds` on a SKINNED mesh is bind-pose data, and it lies

This one cost the most, so it gets the most words.

The mesh reported `Extents(0.47, 0.24, 0.68)` — putting a 1.36 m height on **Z**, not Y. Every
bounds-derived number was therefore measured off an axis that was never his up, and a loader
deriving a ground offset from `-bounds.min.y` came up with **0.244 m**, which was **half his width**.
It then lifted him that far into the air, and the log line explaining the lift was perfectly
self-consistent.

**`bounds`, `localBounds` and `sharedMesh.bounds` are the same wrong box wearing three hats.** FBX
axis conversion goes into the BONES; it does not touch bind-pose bounds. So:

- **`ModelImporter.bakeAxisConversion = true` does not fix it.** Tried. It only flipped the sign
  (centre z 0.68 → −0.68, extents unchanged), because the FBX header declares Y-up while the
  geometry is not.
- **Re-exporting from Blender with `axis_up='Y'` does not fix it either.** Also tried. Same reason.

**Measure the POSED mesh instead.** `SkinnedMeshRenderer.BakeMesh(mesh)` gives the actual skinned
result in the renderer's local space, and it is the only measurement here that does not come back
through the bad box:

```csharp
Mesh baked = new Mesh();
smr.BakeMesh(baked);
Bounds b = baked.bounds;          // now real; transform its corners into the parent's space
```

**Set `smr.updateWhenOffscreen = true` at the same time.** Unity culls a renderer by that same bad
box, so without it the character vanishes at angles where the real body is plainly on screen.

Corollary worth its own line: **a lift derived from geometry needs a sanity ceiling.** A metre and a
half of "lift" is a broken bake, not a lift. Refuse it, log it, and stand the character at zero —
burying him is a better failure than launching him.

## 4. A bundle baked in the Editor carries Unity's `Standard`, and Valheim does not light it

First in-game look: a **lit head on a flat black body**. Lit only by direct light, no ambient at all.

Valheim's own creature shading — its ambient, fog, wind and wet/snow handling — lives in the game's
shaders. `Standard` receives none of it.

**Swapping only the shader is not enough.** Tried, and it is the trap: `material.shader = x` keeps
only the properties that match **by name**. Valheim's creature shader has properties `Standard`
never had, and the unset ones fall back to shader defaults that read as black under ambient. So the
body was still wrong, in a subtler way that is easy to blame on the texture.

**Copy the whole material off a vanilla prefab and put your albedo in it:**

```csharp
GameObject stand = ZNetScene.instance.GetPrefab("Dverger");
Material donor  = stand.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterial;
Material mine   = new Material(donor);          // 'DvergerBody', shader 'Custom/Creature'
mine.SetTexture("_MainTex", myAlbedo);
```

Take it from **the prefab you are replacing**, not by `Shader.Find`: it is the shader that character
is meant to be drawn with, and it cannot be missing — if it did not resolve there would be no
character to dress.

## 5. Clearing a donor's emission MASK without its emission COLOUR lights the whole body

Immediately after §4 the character was a **glowing blue silhouette**.

The Dverger glows — blue eyes, blue runes — and that glow is an emission colour gated by an emission
**mask**. The donor's maps are authored against the DONOR's UVs, so clearing them is right; clearing
the mask while leaving the colour lights everything the mask used to hold back.

Kill the colour, the keyword and the GI flag together — Unity gates the emission pass on the keyword,
and a stale one keeps the pass alive at black on some variants:

```csharp
mat.SetColor("_EmissionColor", Color.black);
mat.DisableKeyword("_EMISSION");
mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
```

**This is lesson 22's mirror.** There, cloning a donor material carried things nobody asked for
(`_Color`, `_ValueNoise`, `_MainTex_ST`). Here, cloning is *mandatory* — and it carries the donor's
glow. **Whenever you clone a vanilla material, enumerate what the donor was doing that you are not.**

## 6. Two things that turned out fine, recorded so nobody re-litigates them

- **A `StandaloneWindows64` bundle loads on a LINUX client.** Standalone bundles are compatible
  across the desktop platforms; no separate Linux bake is needed to test one.
- **Unity's material import binds a texture by NAME.** The albedo was linked in Blender as
  `texture_0` and imported with `_MainTex` empty, so the model drew pure white. Renaming the image
  datablock to match the PNG beside the model (`ingvar_albedo`) made the import bind it with no code
  at all. Cheaper than any runtime fix, and it fixes the asset for everyone downstream.

## 7. The order to debug this in

A character that renders wrong is five candidate defects deep, and they mask each other — the white
blob hid the black body, which hid the glow. Go in this order, because each step's evidence is only
readable once the one before it is clean:

1. **Dump the imported hierarchy**: every renderer, its triangle count, its materials, each
   material's shader and `_MainTex`. Two renderers or `tex=NONE` are both answers on their own.
2. **Check the clip names and loop flags** as imported.
3. **Stand it up and look at it.** Silhouette first — shape and orientation — then colour.
4. **Only then** touch materials, and change one thing per look.

And keep a Blender pass available. Two of the five (§2, §6) were fixable at source in about a minute
each, and a source fix costs no runtime code and helps every consumer of the asset. The other three
could not be, so the loader carries them.
