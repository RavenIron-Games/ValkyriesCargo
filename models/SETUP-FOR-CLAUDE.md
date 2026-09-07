# Setup: Ingvar's body → AssetBundle → the DLL

**Written for the Claude session driving this, not for a person to read start to finish.**
Wu'barrk's side produced the asset; this is everything needed to get it into the game. Follow
the order, run the checks, and do not skip section 0.

Sibling docs: [`models/README.md`](README.md) is the reference (asset spec, the ServerSync
question, the animator fork). This file is the procedure.

---

## 0. What will go wrong if you skip it

Three failures are already on record in this workspace. Each cost a release or a day.

1. **`-nographics` ships blank textures.** Texture work goes through `Graphics.Blit` +
   `ReadPixels`, which returns EMPTY with no graphics device. The build *succeeds*. AwayFromHome
   1.1.0 shipped a 381 KB bundle where a good one is ~4.8 MB, and it was reported three times as
   "the model looks like crap" while the client log truthfully said the texture was bound.
   `IngvarBundleBuilder` refuses to run without a device — **do not "fix" that guard.**
2. **`BuildAssetBundles` fails silently.** It compiles Player scripts for the target first;
   `com.unity.collections` fails there with CS7036 and `BuildAssetBundles` **returns without
   throwing**, leaving a 0-byte bundle while the script prints DONE. Two defences are in place:
   the `ENABLE_UNITY_COLLECTIONS_CHECKS` wrapper, and the scaffold omits gltfast entirely (the
   FBX path does not need it, and it is what drags collections in).
3. **A stale copy ships the old asset with no change in DLL size.** The copy from
   `AssetBundles\` into the repo is manual. Always confirm the DLL grew.

And one that is specific to this asset:

4. **Do not follow `KeeperStoneBundleBuilder`.** It combines `MeshFilter`s into a single `Mesh`.
   `CombineMeshes` carries only the channels the source meshes have, so on a skinned character it
   discards bone weights, the bind pose and every clip. Ingvar ships as the imported model asset.

---

## 1. Preconditions

```powershell
# Unity 6000.0.61f1 - the version every project in this studio uses, and the one bundles must
# be built with. Anything else and the bundle may not load.
unity install 6000.0.61f1 -y --accept-eula     # if not already installed
```

- **Linux only:** the Editor binary fails with `libxml2.so.2: cannot open shared object file` on
  Arch, which ships SONAME `.so.16`. Fix: `sudo pacman -S libxml2-legacy` (official `extra`).
- `windows-mono` is **not** required for AssetBundles, despite what an old note may say. A
  successful `StandaloneWindows64` bundle has been built here with it absent. If you hit CS7036,
  it is landmine 2, not a missing module — do not chase the module install.

Source art, already in the repo:

| File | | |
|---|---|---|
| `models/ingvar.fbx` | 4.3 MB | rig + 6 clips, textures external |
| `models/ingvar_albedo.png` | 6.6 MB | 2048², sRGB |

**Use the FBX, not the GLB.** Unity imports `.glb` through glTFast's `ScriptedImporter`, which
does not expose `ModelImporter` — so `animationType`, per-clip `loopTime` and `globalScale` are
all unreachable. The builder detects this and fails with that message rather than guessing.

---

## 2. Run it

```powershell
.\tools\setup-ingvar-unity.ps1            # scaffold the sibling Unity project
.\tools\setup-ingvar-unity.ps1 -Build     # scaffold + bake
.\tools\setup-ingvar-unity.ps1 -Embed     # copy the bundle in, print the csproj lines
```

The project is created as a **sibling** of the repo (`../ValkyriesCargo-Unity`). Do not move it
inside: an SDK-style csproj globs every `.cs` beneath it and sweeps Unity's `Library/PackageCache`
into the mod DLL — hundreds of CS0246. If it must live inside, guard it:

```xml
<Compile Remove="Unity\**" /><None Remove="Unity\**" /><EmbeddedResource Remove="Unity\**" />
```

### Check the bake actually worked

The builder prints these. **Read them, do not assume.**

```
[ValkyriesCargo] model: 6 clip(s) [Walk, Idle, Talk, Hello, Shrug, Nod], SkinnedMeshRenderer=True, bones=24, tris=31112
[ValkyriesCargo] texture: max 2048, DXT1, uncrunched, sRGB
[ValkyriesCargo] bundle valkyriescargo_kit: <N> KB, 2 asset(s): Assets/ingvar.fbx, Assets/ingvar_albedo.png
```

| Gate | Pass |
|---|---|
| `SkinnedMeshRenderer=True`, `bones=24` | the rig survived import |
| `6 clip(s)` | all animations came through |
| bundle **≥ 64 KB** | below that is the empty-texture or 0-byte failure, not a build |
| `2 asset(s)` | a tag that failed to stick builds perfectly and is missing the model |

---

## 3. Embed it in the DLL

Add to `ValkyriesCargo/ValkyriesCargo.csproj`:

```xml
<EmbeddedResource Include="..\Assets\valkyriescargo_kit" LogicalName="ValkyriesCargo.valkyriescargo_kit" />
<Reference Include="UnityEngine.AssetBundleModule">
  <HintPath>$(LibsDir)\UnityEngine.AssetBundleModule.dll</HintPath>
  <Private>false</Private>
</Reference>
```

Add `UnityEngine.AssetBundleModule.dll` to the list in `tools/fetch-libs.ps1`, or a fresh clone
cannot build — the same class of bug as the `libs/` gitignore that hid `ServerSync.cs`.

Then **confirm the DLL grew by roughly the bundle size.** This is the check that catches a stale
copy, and it has caught one before.

---

## 4. Load it — `Client/BodyLoader.cs`

Resolve by suffix so a rename cannot silently break it, load once, cache, and never
`Unload(true)` while an instance is alive.

```csharp
private const string BundleName = "valkyriescargo_kit";
private static AssetBundle _bundle;
private static GameObject  _body;

private static void Load()
{
    if (_bundle != null) return;
    Assembly asm = Assembly.GetExecutingAssembly();
    string res = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith(BundleName, StringComparison.Ordinal));
    if (res == null) { Log.LogError($"{BundleName} is not embedded in this DLL"); return; }
    using (Stream s = asm.GetManifestResourceStream(res)) _bundle = AssetBundle.LoadFromStream(s);
    _body = _bundle != null ? _bundle.LoadAsset<GameObject>("ingvar") : null;
    Log.LogInfo($"body: bundle={_bundle != null}, prefab={_body != null}");
}
```

**On a dedicated server this whole path is meaningless and will read as failure** — donor
materials resolve to `Hidden/InternalErrorShader` with no properties, so every `HasProperty`
misses. Label those log lines as client-only; only a client log says anything about appearance.

---

## 5. Gate it behind the config that already exists

`Server.BodyPrefab` is bound, synced and locked in `Config/ModConfig.cs`. `Dverger` keeps the
stand-in; `Ingvar` uses the bundle. That is the whole integration, and it means the server
decides which body every client builds without a rebuild.

**The bundle must never travel over ServerSync.** ServerSync is a config channel: it broadcasts
on change with no heartbeat, and payloads under 10,000 bytes go uncompressed. `VisitState` and
`MarketState` are hundreds of bytes; that is its scale. What guarantees every player sees the
same body is that the bundle is *inside the DLL*, and `ModRequired` with
`MinimumRequiredVersion == CurrentVersion` refuses any client on a different build.

---

## 6. The decision that is not made

`models/README.md` §7 has the fork, and it shapes P5:

- **Own rig, we drive it** *(Wu'barrk's recommendation)* — keep the 24-bone skeleton and six
  clips, give the prefab a plain `Animator`, drive it from `CargoMerchant` off agent velocity.
  We own the merchant, so `ZSyncAnimation` is not needed. Avoids bone-mapping onto Valheim's
  skeleton, **which nobody in this workspace has ever done.**
- **Marry it to the vanilla Dverger rig** — free vanilla locomotion and netcode, but needs our
  mesh skinned to Valheim's exact bone names. New ground, no worked example.

Whichever: `applyRootMotion = false`. The server owns position, and a clip that walks the
transform forward fights the netcode. Every clip here is already in-place (largest hip travel
0.19 m, on the idle sway), so there is nothing to strip.

**Ground offset:** Ingvar's origin is at his feet (`bounds.min.y == 0`), so he should need no
lift — but derive it from the mesh bounds and log it anyway. `SkinnedMeshRenderer.bounds` is the
authored bind-pose box and was wrong by 0.27–0.64 m on IronCohort's Meshy rigs, floating all four
characters. Never hardcode the number: guessing it failed twice in both directions on the Keeper
Stone.

---

## 7. Rebuilding the asset (only if the art changes)

Needs Blender 5.2 and the raw Meshy output, which is **not** in the repo — it is 343 MB, six
files each carrying a duplicate mesh and 22 MB of the same textures.

```bash
blender --background --python tools/build_ingvar.py     # merge, nod, textures, export
blender --background --python tools/preview_ingvar.py   # contact sheets into models/preview
```

The nod is authored, not bought — Meshy's 678-action library has no head gesture at all
(`Agree Gesture` is an arms-up cheer). It is −13.9° of `Head` pitch over 1.25 s in `make_nod()`.
If it does not carry at `ApproachDistance` 3.5 m, raise it to −18 or −20 and re-run.

The Meshy task ids are in `meshy_tasks.json` and `meshy_clips.json`. ⚠️ Meshy assets **expire**
— the original generation was due to lapse 2026-09-09. Anything not already downloaded is gone.
