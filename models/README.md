# Ingvar's body — the asset, and how to get it into the game

**Track B deliverable, 2026-09-06.** The rigged, animated merchant and everything needed to
turn it into a shipping AssetBundle embedded in `ValkyriesCargo.dll`.

**Wired in now.** `Client/BodyLoader.cs` opens the bundle and hangs Ingvar on the merchant;
`Client/IngvarBody.cs` drives the six clips; `Client/CargoMerchant.cs` (P5) calls
`BodyLoader.Attach` from `Patch_Humanoid_Awake`, on every machine. This folder stays the source
of the asset itself — the FBX, the bake recipe, the import settings; section 7 records what
wiring it onto the merchant actually does now, not a plan for doing it.

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

ServerSync's *only* job around the body is the two config entries that already exist:

```csharp
BodyPrefab = S(cfg, "Server", "BodyPrefab", "Dverger", ...)   // synced + locked
CustomBody = S(cfg, "Server", "CustomBody", true, ...)        // synced + locked
```

`BodyPrefab` is NOT the custom-body switch: it stays the engine prefab the merchant is cloned
from (Character, MonsterAI, the collider), whatever body is drawn on top. The switch is the
separate `Server.CustomBody` (synced + locked, default true), added in P8. Because both are
`Server.*` entries they are synced and locked, so the server decides which body every client
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
- **No gltfast.** `tools/setup-ingvar-unity.sh` writes a `Packages/manifest.json` with none —
  left out on purpose: the FBX path this project actually uses does not need it, and its
  transitive `com.unity.collections` is exactly what landmine 2 below is a workaround for.
- Give this mod its **own** project. Sharing one risks the next rebuild folding Ingvar into
  someone else's shipping bundle.

Drop `ingvar.fbx` and `ingvar_albedo.png` at `Assets/` (`tools/setup-ingvar-unity.sh` does this
for you). Not `ingvar.glb` — section 1's `Use the FBX, not the GLB` is why.

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
| Global Scale | **1** | The FBX is already real-world metres. A second scale factor is the classic route to a 100× character. |
| Read/Write | ✅ enabled | The importer defaults it **off**, and a disabled mesh returns empty `vertices` at runtime. Only needed if anything reads mesh data CPU-side — enable it and stop guessing. |
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

    // tag the imported prefab (NOT a combined mesh) AND the texture into the bundle
    AssetImporter.GetAtPath("Assets/ingvar.fbx").SetAssetBundleNameAndVariant(BundleName, "");
    AssetImporter.GetAtPath("Assets/ingvar_albedo.png").SetAssetBundleNameAndVariant(BundleName, "");

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

Exactly the AwayFromHome pattern. Copy the built bundle to `Assets\valkyriescargo_kit` at the
repo root (gitignored — a build input, not source), then:

```xml
<EmbeddedResource Include="..\Assets\valkyriescargo_kit"
                  LogicalName="ValkyriesCargo.valkyriescargo_kit"
                  Condition="Exists('..\Assets\valkyriescargo_kit')" />
```

Conditional, because the bundle never reaches git: a fresh clone with no bake still builds, and
`cargo body` then answers `source none` with the Dverger stand-in kept.

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
_stream = asm.GetManifestResourceStream(res);          // kept open — see below, not `using`
_bundle = AssetBundle.LoadFromStream(_stream);

_prefab = _bundle.LoadAsset<GameObject>("ingvar");     // the prefab, with its SkinnedMeshRenderer
```

⚠️ **Do not wrap the stream in a `using`.** An earlier draft of this section did —
`using (Stream s = ...) { _bundle = AssetBundle.LoadFromStream(s); }` — and that is wrong:
`LoadFromStream` reads the bundle lazily, as assets are requested, so the stream has to outlive
it. A `using` closes the stream the moment `LoadFromStream` returns, and the first `LoadAsset`
after that throws `ObjectDisposedException`. `BodyLoader` keeps it in a static field for the
life of the process instead — the same lifetime as the bundle itself — and nothing leaks: the
bytes are the DLL's own resource, never a file handle.

Load **once**, cache it, and never call `AssetBundle.Unload(true)` while an instance is alive.

---

## 7. Wiring it onto the merchant (P5, built 2026-09-07)

The documented pattern in this workspace is **clone the vanilla creature, swap only what is
visual** — `DvergrAllies` clones a Dverger precisely to inherit its `Animator`,
`ZSyncAnimation` and `Character`, then drives it with vanilla calls. `CargoMerchant` clones the
same way and for the same reason (`Server.BodyPrefab`, still `Dverger`, is where `Character`,
`Humanoid`, `MonsterAI` and the `CapsuleCollider` all come from) — but Ingvar's body is not a
swap of what that clone draws.

**The choice was own rig, we drive it — built as an additive child, not a hand-built
`Animator` controller.** `BodyLoader.Attach` hangs the 24-bone rig under the clone's root as a
child named `IngvarBody`, appended last so every vanilla `GetComponentInChildren<Animator>()`
still finds the clone's own `Animator` first, and disables the clone's `Renderer`s and
`LODGroup` rather than destroying anything or deactivating the GameObject they sit on —
`Character.m_animator`, `VisEquipment` and `ZSyncAnimation` all keep reading it undisturbed.
**There is no `AnimatorController` anywhere.** `Client/IngvarBody.cs` plays the six clips
through a `PlayableGraph` instead — one `AnimationMixerPlayable`, one `AnimationClipPlayable`
per clip found BY NAME — blended by `Core/BodyMotion.cs` off this transform's own frame-to-frame
displacement (idle below 0.05 m/s, walk above 0.06, a 0.15 s crossfade, a one-shot handed back
at 85% of its own length): every machine derives the same walk from the same replicated
position, so the animation needs no `ZSyncAnimation` field of its own.

`animator.applyRootMotion = false` — the server owns position, and a clip that walks the
transform forward fights the netcode. `cullingMode = CullUpdateTransforms`.

**Ground offset:** never hardcode the lift, and never derive it from `sharedMesh.bounds` — tried
on this asset, and wrong. That box is bind-pose data in the mesh's own space that FBX axis
conversion never touches, so here it puts his 1.36 m of height on **Z** and implies a lift that
is really half his width; neither `ModelImporter.bakeAxisConversion` nor a Blender re-export
with `axis_up='Y'` fixes it. `BodyLoader` measures the POSED mesh instead
(`SkinnedMeshRenderer.BakeMesh`, the lowest corner transformed into the parent's space) — right
whatever the source axes are, and it comes out near 0, because Ingvar's origin really is
authored at his feet.

---

## 8. What is proven and what is not

**Proven here:** the asset itself — geometry, scale, skeleton, six clips, in-place motion,
single-sided material, 2048 texture. All read back out of the exported file and rendered
(`preview/`).

**Proven in sibling mods, and hit again here:** the bundle recipe, both landmines, the
embedded-resource load and the donor-material traps — first found in AwayFromHome, Let It Grow
and Mists of Avalor, then found again on this asset and fixed
(`docs/knowledge-base/SKINNED-CHARACTER-BUNDLE-FACTS.md` records all five, including the one —
the bind-pose box lying about the up axis — that no bake can fix, only the loader).

**Seen on a screen, 2026-09-07:** `cargo body preview` stands him upright, textured, feet on the
ground, at a client — a custom *skinned* character in Valheim, where every other art integration
this studio has shipped is a static mesh swap on a building piece. On an actual merchant, the
integrated run logged `body=Ingvar`, not the stand-in (`CLAUDE.md`'s INTEGRATED IN-GAME RUN).
**Not yet seen:** how he reads in daylight (that run was at midnight), and the walk and one-shot
clips actually playing on a merchant rather than a preview — `CLAUDE.md` keeps its own checklist
item 20 open on exactly those two. Put `cargo prefab Dverger` output beside a run before
trusting the comparison.
