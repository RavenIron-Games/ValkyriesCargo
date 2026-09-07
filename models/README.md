# Ingvar's body — the asset, and how to get it into the game

**Track B deliverable, 2026-09-06.** The rigged, animated merchant and everything needed to
turn it into a shipping AssetBundle embedded in `ValkyriesCargo.dll`.

Nothing here is wired into the mod yet. It cannot be: there is no merchant to attach a body
to. `Client/CargoMerchant.cs`, `Client/BodyLoader.cs` and `Server/Spawner.cs` do not exist,
and the merchant is **P5**, three packages down Track A's chain. This folder is the body
waiting for its owner.

---

## 1. What is here

| File | What it is |
|---|---|
| `ingvar.fbx` | **The deliverable.** One skinned mesh, one skeleton, six clips. 4.3 MB. |
| `ingvar_albedo.png` | Its texture, 2048² sRGB, 6.6 MB. Kept external so Unity gets a real Texture2D with import settings. |
| `SETUP-FOR-CLAUDE.md` | **The procedure.** Scaffold → bake → embed → load, with the verification gates. |
| `meshy_tasks.json` | Meshy task ids for the remesh and the rig. |
| `meshy_clips.json` | Meshy task ids for the five bought clips. |
| `preview/` | Rendered contact sheets — what each clip actually looks like. |
| `10ktridwarf.glb`, `dwarf_*.glb/fbx`, `ingvar_*.glb/fbx` | Meshy's raw per-clip output, 343 MB, **gitignored**. Every one carries a duplicate copy of the mesh and its 22 MB of textures; `ingvar.glb` is all of them merged. |

`ingvar.fbx` (and `ingvar.glb`, the same asset in a browser-previewable form, gitignored):

```
height      1.370 m      (design's dwarf scale; origin at the FEET, centred in X/Z)
geometry    31,112 triangles, 18k verts, single-sided
skeleton    24 joints, root "Hips" under an "Armature" node with scale 0.01
material    one 2048² base colour, scalar roughness 0.41, no metallic/roughness map
clips       Walk 4.17s · Idle 10.00s · Talk 5.13s · Hello 3.75s · Shrug 1.96s · Nod 1.25s
            (as Unity reports them after import, 2026-09-07. The earlier row here said 4.21 /
            5.17 / 3.79 / 2.00 -- each exactly one frame at 24 fps longer, because a frame
            COUNT over the rate counts both ends and AnimationClip.length does not. Same
            clips; nothing to chase. `cargo body` prints these, so item 19 checks THESE.)
```

**Every clip is in-place.** Largest hip travel is 0.19 m (the idle sway); the walk moves
0.05 m. There is no baked locomotion to fight the netcode — the AI owns position.

⚠️ The `Armature` node carries **scale 0.01** (centimetres → metres). Raw hip translation
values in the file look like 19 m; multiply by 0.01. Don't "fix" this — the mesh is correct
at 1.370 m and the scale is what makes it so.

Provenance: Meshy image-to-3d → web-studio remesh to 10,355 polys → **API** remesh to 30k →
Meshy auto-rig (24 bones) → five clips from Meshy's 678-action library → merged, nod authored,
textures halved in Blender. 18 Meshy credits. **Use the FBX, not the GLB** — Unity imports `.glb` through glTFast's `ScriptedImporter`, which does
not expose `ModelImporter`, so the rig and animation settings are unreachable. Rebuild with
[`tools/build_ingvar.py`](../tools/build_ingvar.py); re-render the previews with
[`tools/preview_ingvar.py`](../tools/preview_ingvar.py).

**The nod is ours.** Meshy's library has 678 full-body actions and not one head gesture —
`Agree Gesture` is an arms-up cheer, not a nod. It is authored in `build_ingvar.py`: the
`Head` bone pitching to −13.9° and back over 1.25 s, eased. It is deliberately subtle;
if it doesn't carry at 3.5 m, raise the `-14.0` in `make_nod()` to −18 or −20 and re-run.

---

## 2. ServerSync's role — read this before you plan around it

**The bundle does NOT travel over ServerSync. It must never travel over ServerSync.**

ServerSync is a *config* channel. It broadcasts on change with no heartbeat, and payloads
under 10,000 bytes go uncompressed. Pushing a 9 MB asset through it would flood every peer
on every change. `VisitState` and `MarketState` are strings measured in hundreds of bytes;
that is the scale it is designed for.

What actually guarantees every player sees the same Ingvar is the thing already built:

- The bundle is **embedded in `ValkyriesCargo.dll`** as a resource, so every machine that has
  the DLL has the body. No transfer, no download, no versioning problem.
- `ModRequired = true` with `MinimumRequiredVersion == CurrentVersion` (`Config/ModConfig.cs`)
  means a client on any other build is refused at handshake. So "everyone has the same DLL"
  is enforced, and therefore "everyone has the same bundle" is enforced.

ServerSync's *only* job around the body is the one config entry that already exists:

```csharp
BodyPrefab = S(cfg, "Server", "BodyPrefab", "Dverger", ...)   // synced + locked
```

Set it to `Ingvar` to use the custom body, leave it `Dverger` to fall back. Because it is a
`Server.*` entry it is synced and locked, so the server decides which body every client
builds, and an admin can flip it without a rebuild. That is the whole integration.

---

## 3. Unity project

A **sibling** directory, never inside the repo — an SDK-style csproj globs every `.cs`
beneath it and will sweep Unity's `Library/PackageCache` into the mod DLL (hundreds of
CS0246). If you do put it inside, the guard is:

```xml
<Compile Remove="Unity\**" />
<None Remove="Unity\**" />
<EmbeddedResource Remove="Unity\**" />
```

- Editor **6000.0.61f1** exactly — same as AwayFromHome, Let It Grow and IronCohort22.
- One package beyond the 3D template: `"com.unity.cloud.gltfast": "6.19.0"` in
  `Packages/manifest.json`.
- Give this mod its **own** project. Avalor's builder sweeps every `.glb` under `Assets/`
  into one bundle; sharing a project means the next rebuild folds Ingvar into someone else's
  shipping bundle.

Drop `ingvar.glb` at `Assets/ingvar.glb`.

---

## 4. Import settings — this is where a skinned character differs from every prior asset

**Do not follow `KeeperStoneBundleBuilder` literally.** Keeper Stone is a *static* mesh: it
combines `MeshFilter`s into one `Mesh` asset and ships the mesh plus baked textures. That
whole path destroys a skinned character — `CombineMeshes` drops bone weights, the bind pose
and every clip.

For Ingvar, ship a **prefab**, not a mesh:

| Setting | Value | Why |
|---|---|---|
| Animation Type | **Generic** | Every clip is on the same skeleton that rigged the mesh, so bone paths already match. Humanoid adds a retarget step that can silently mangle a pose and buys nothing when nothing retargets across characters. (IronCohort22's `RiggedCharacterPipeline.cs` reached the same conclusion the hard way.) |
| Avatar Definition | Create From This Model | |
| Import Animation | ✅ | six clips live in the same file |
| Global Scale | **1** | glTF is already real-world metres. A second scale factor is the classic route to a 100× character. |
| Read/Write | ✅ enabled | glTFast defaults it **off**, and a disabled mesh returns empty `vertices` at runtime. Only needed if anything reads mesh data CPU-side — enable it and stop guessing. |
| Loop Time | ✅ on `Walk`, `Idle`, `Talk` | not on `Hello`, `Shrug`, `Nod` — those are one-shots |
| Material Import | Import Standard | one material, one texture |
| Texture max size | 2048, DXT1, **not** crunched | crunch is a second lossy pass stacked on DXT1, traded for download size — and this texture doesn't travel on its own, it's read from the DLL. The trade buys nothing and costs detail. |

If you want the walk shorter, set the clip range in the importer's Animation tab. One stride
is **frames 12–46** of 101. Do it there, not in the source — the clip already loops, and
trimming the glb destructively risks breaking the loop for no gain.

---

## 5. Building the bundle

The parts of the AwayFromHome recipe that **do** carry over, and both of its landmines:

```csharp
[MenuItem("ValkyriesCargo/Build Ingvar Bundle")]
public static void BuildKit()
{
    // LANDMINE 1 — refuse to bake without a GPU.
    if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
    {
        Debug.LogError("NO GRAPHICS DEVICE. Texture baking goes through Graphics.Blit + " +
                       "ReadPixels, which silently produces EMPTY textures under -nographics. " +
                       "The build 'succeeds' and ships a blank model. Re-run WITHOUT -nographics.");
        return;
    }

    // tag the imported prefab (NOT a combined mesh) into the bundle
    AssetImporter.GetAtPath("Assets/ingvar.glb").SetAssetBundleNameAndVariant(BundleName, "");

    // LANDMINE 2 — BuildAssetBundles compiles Player scripts for the target first, and
    // com.unity.collections@2.6.6 (pulled in transitively by gltfast) fails there with
    // CS7036. BuildAssetBundles then returns WITHOUT THROWING, leaving a 0-byte bundle
    // while the script prints DONE.
    var nbt = NamedBuildTarget.Standalone;
    string prev = PlayerSettings.GetScriptingDefineSymbols(nbt);
    bool had = prev.Split(';').Contains("ENABLE_UNITY_COLLECTIONS_CHECKS");
    if (!had) PlayerSettings.SetScriptingDefineSymbols(nbt,
        string.IsNullOrEmpty(prev) ? "ENABLE_UNITY_COLLECTIONS_CHECKS"
                                   : prev + ";ENABLE_UNITY_COLLECTIONS_CHECKS");
    AssetBundleManifest manifest;
    try   { manifest = BuildPipeline.BuildAssetBundles(outDir, BuildAssetBundleOptions.None,
                                                       BuildTarget.StandaloneWindows64); }
    finally { if (!had) PlayerSettings.SetScriptingDefineSymbols(nbt, prev); }

    // VERIFY THE ARTEFACT, NOT THE EXIT CODE.
    if (manifest == null || !File.Exists(bundleFile) || new FileInfo(bundleFile).Length == 0)
    { Debug.LogError("bundle missing or empty"); return; }
    Debug.Log($"bundle contains: {string.Join(", ", AssetDatabase.GetAssetPathsFromAssetBundle(BundleName))}");
}
```

The headless command — note the deliberately **absent** `-nographics`:

```
unity run <UnityProject> --editor-version 6000.0.61f1 --timeout 3600 --non-interactive \
  -- -executeMethod IngvarBundleBuilder.BuildKit -logFile build.log
```

Cost of getting landmine 1 wrong, on record: the whole of AwayFromHome 1.1.0 — a 381 KB
bundle of blank textures where a good one is 4,779 KB, reported three times as "the model
looks like crap" and chased through the material every time.

---

## 6. Embedding it in the DLL

Exactly the AwayFromHome pattern. Copy the built bundle into the repo, then:

```xml
<EmbeddedResource Include="Assets\valkyriescargo_kit" LogicalName="ValkyriesCargo.valkyriescargo_kit" />
```

Add the reference the loader needs:

```xml
<Reference Include="UnityEngine.AssetBundleModule">
  <HintPath>$(LibsDir)\UnityEngine.AssetBundleModule.dll</HintPath>
  <Private>false</Private>
</Reference>
```

⚠️ **The copy from `Unity/AssetBundles/` into the repo is manual.** A good bake plus a stale
copy ships the old broken asset *with no change in DLL size*. Check the DLL grew by roughly
the bundle size after every rebuild.

Runtime load — no Jotunn, resolve the resource by suffix so a rename can't silently break it:

```csharp
Assembly asm = Assembly.GetExecutingAssembly();
string res = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(BundleName, StringComparison.Ordinal));
using (Stream s = asm.GetManifestResourceStream(res))
    _bundle = AssetBundle.LoadFromStream(s);

_body = _bundle.LoadAsset<GameObject>("ingvar");   // the prefab, with its SkinnedMeshRenderer
```

Load **once**, cache it, and never call `AssetBundle.Unload(true)` while an instance is alive.

---

## 7. Wiring it onto the merchant (P5, when it exists)

The documented pattern in this workspace is **clone the vanilla creature, swap only what is
visual** — `DvergrAllies` clones a Dverger precisely to inherit its `Animator`,
`ZSyncAnimation` and `Character`, then drives it with vanilla calls.

For Ingvar there is a choice, and it is not yet made:

- **Own rig, we drive it.** Keep the 24-bone skeleton and its six clips, give the prefab a
  plain `Animator` with a small controller, and drive it from `CargoMerchant` off the agent's
  velocity. We own the merchant, so we do not need `ZSyncAnimation`. **Recommended** — it
  avoids bone-mapping onto Valheim's skeleton, which nobody in this workspace has ever done.
- **Marry it to the vanilla Dverger rig.** Free vanilla locomotion and netcode, but requires
  our mesh skinned to Valheim's exact bone names. Genuinely new ground, no worked example.

A minimal controller for the first option — parameters `Speed` (float) and `Greet`/`Nod`
(triggers):

```
Idle  --(Speed > 0.06)-->  Walk        hasExitTime=false, duration 0.15
Walk  --(Speed < 0.05)-->  Idle
AnyState --(Greet)-->      Hello       duration 0.06, canTransitionToSelf=false
AnyState --(Nod)-->        Nod
Hello/Nod --> Idle                     hasExitTime, exitTime 0.85
```

`animator.applyRootMotion = false` — the server owns position, and a clip that walks the
transform forward fights the netcode. `cullingMode = CullUpdateTransforms`.

**Ground offset:** never hardcode the lift. Ingvar's origin is already at his feet
(`bounds.min.y == 0`), so he should need none — but derive it anyway and log it, because
`SkinnedMeshRenderer.bounds` is the authored bind-pose box and was wrong by 0.27–0.64 m on
IronCohort's Meshy rigs. Measure `sharedMesh.bounds` from the renderer, union it, and warn if
the result is not what you expect.

---

## 8. What is proven and what is not

**Proven here:** the asset itself — geometry, scale, skeleton, six clips, in-place motion,
single-sided material, 2048 texture. All read back out of the exported file and rendered
(`preview/`).

**Proven in sibling mods:** the bundle recipe, both landmines, the embedded-resource load,
and the donor-material traps — from AwayFromHome, Let It Grow and Mists of Avalor.

**Not proven anywhere:** a custom *skinned* character in Valheim. Every art integration this
studio has shipped is a static mesh swap on a building piece. Section 7's first option is the
low-risk route, but it has not been run in a live world by anyone here. Budget for surprises
at the animator and at ownership handoff, and put `cargo prefab Dverger` output beside this
before wiring, so the vanilla comparison is on the desk.
