// Bake Ingvar into an AssetBundle. Copy this to <UnityProject>/Assets/Editor/ and run:
//
//   unity run <UnityProject> --editor-version 6000.0.61f1 --timeout 3600 --non-interactive \
//     -- -executeMethod IngvarBundleBuilder.BuildKit -logFile build.log
//
// or from the Editor:  ValkyriesCargo > Build Ingvar Bundle
//
// NOTE THE ABSENT -nographics. See landmine 1.
//
// This is NOT KeeperStoneBundleBuilder. That one combines MeshFilters into a single Mesh asset,
// which is right for a static prop and fatal here: CombineMeshes carries only the channels the
// source meshes have, so it discards bone weights, the bind pose and every animation clip. A
// skinned character ships as the imported model asset, with its rig and clips intact.

using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class IngvarBundleBuilder
{
    private const string Fbx        = "Assets/ingvar.fbx";
    private const string Albedo     = "Assets/ingvar_albedo.png";
    private const string BundleName = "valkyriescargo_kit";
    private const int    TexMaxSize = 2048;

    // Meshy's clips: the three that loop, and the three one-shots. Getting this wrong is not
    // subtle - a non-looping idle freezes on its last frame after ten seconds.
    private static readonly string[] Looping = { "Walk", "Idle", "Talk" };

    [MenuItem("ValkyriesCargo/Build Ingvar Bundle")]
    public static void BuildKit()
    {
        // ---- LANDMINE 1 -------------------------------------------------------------------
        // Texture work goes through Graphics.Blit + ReadPixels, which silently returns EMPTY
        // textures with no graphics device. The build then "succeeds" and ships a blank model.
        // This cost AwayFromHome the whole of 1.1.0 - a 381 KB bundle where a good one is ~4.8 MB,
        // reported three times as "the model looks like crap" and chased through the material
        // every time. Refuse rather than produce a plausible-looking lie.
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Fail("NO GRAPHICS DEVICE. Re-run the same command WITHOUT -nographics.");
            return;
        }

        if (!File.Exists(Fbx)) { Fail($"{Fbx} not found. Copy models/ingvar.fbx into Assets/."); return; }
        if (!File.Exists(Albedo)) { Fail($"{Albedo} not found. Copy models/ingvar_albedo.png into Assets/."); return; }

        if (!ConfigureModel()) return;
        ConfigureTexture();

        // Tag the imported model. LoadAsset<GameObject>("ingvar") then returns the whole rigged
        // prefab - SkinnedMeshRenderer, armature and clips - rather than a bare Mesh.
        AssetImporter.GetAtPath(Fbx).SetAssetBundleNameAndVariant(BundleName, "");
        AssetImporter.GetAtPath(Albedo).SetAssetBundleNameAndVariant(BundleName, "");

        string outDir = Path.Combine(Directory.GetCurrentDirectory(), "AssetBundles");
        Directory.CreateDirectory(outDir);

        // ---- LANDMINE 2 -------------------------------------------------------------------
        // BuildAssetBundles compiles PLAYER scripts for the target first, even for a bundle of
        // one model. com.unity.collections (pulled in transitively by gltfast) fails there with
        // CS7036, and BuildAssetBundles RETURNS WITHOUT THROWING - leaving a 0-byte bundle while
        // the script cheerfully prints DONE. The define makes that compile succeed.
        var nbt = NamedBuildTarget.Standalone;
        string prevDefines = PlayerSettings.GetScriptingDefineSymbols(nbt);
        bool had = prevDefines.Split(';').Contains("ENABLE_UNITY_COLLECTIONS_CHECKS");
        if (!had)
            PlayerSettings.SetScriptingDefineSymbols(nbt, string.IsNullOrEmpty(prevDefines)
                ? "ENABLE_UNITY_COLLECTIONS_CHECKS"
                : prevDefines + ";ENABLE_UNITY_COLLECTIONS_CHECKS");

        AssetBundleManifest manifest;
        try
        {
            manifest = BuildPipeline.BuildAssetBundles(
                outDir, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
        }
        finally
        {
            if (!had) PlayerSettings.SetScriptingDefineSymbols(nbt, prevDefines);
        }

        // ---- verify the ARTEFACT, not the exit code ---------------------------------------
        // A tag that failed to stick produces a bundle that builds perfectly and is missing the
        // model. Print what is actually inside it.
        string bundleFile = Path.Combine(outDir, BundleName);
        if (manifest == null || !File.Exists(bundleFile) || new FileInfo(bundleFile).Length == 0)
        {
            Fail("bundle missing or empty after build - see landmine 2 above.");
            return;
        }

        string[] contents = AssetDatabase.GetAssetPathsFromAssetBundle(BundleName);
        long kb = new FileInfo(bundleFile).Length / 1024;
        Debug.Log($"[ValkyriesCargo] bundle {BundleName}: {kb} KB, {contents.Length} asset(s): {string.Join(", ", contents)}");
        if (contents.Length < 2)
            Debug.LogWarning("[ValkyriesCargo] expected the model AND the texture in the bundle.");
        Debug.Log($"[ValkyriesCargo] NEXT: copy {bundleFile} into the mod repo at Assets\\{BundleName} " +
                  "and confirm the DLL grows by roughly that much. A good bake plus a stale copy " +
                  "ships the old asset with NO change in DLL size.");
    }

    private static bool ConfigureModel()
    {
        var mi = AssetImporter.GetAtPath(Fbx) as ModelImporter;
        if (mi == null)
        {
            Fail($"{Fbx} did not import as a ModelImporter. If you swapped in the .glb, glTFast " +
                 "handles it with a ScriptedImporter and none of these rig settings are reachable. " +
                 "Use the FBX.");
            return false;
        }

        // Generic, not Humanoid. Every clip sits on the SAME skeleton that rigged the mesh, so the
        // bone paths already match exactly; Humanoid would add a retarget step that is the only
        // thing here capable of silently mangling a pose, and buys nothing since nothing retargets
        // across characters.
        mi.animationType      = ModelImporterAnimationType.Generic;
        mi.avatarSetup        = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation    = true;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.importCameras      = false;
        mi.importLights       = false;
        mi.isReadable         = true;   // Read/Write: a disabled mesh returns EMPTY vertices at runtime
        mi.globalScale        = 1f;     // the FBX is already real-world metres; a second factor here
                                        // is the classic route to a 100x character

        // ---- LANDMINE 6 -------------------------------------------------------------------
        // The source is authored Z-UP: `char1`'s mesh bounds put the 1.36 m height on Z and only
        // 0.49 m on Y, so the model lies on its back and every bounds-derived number is measured
        // off the wrong axis. The first bake reported a 0.244 m "ground offset" for a character
        // 1.36 m tall for exactly this reason, and in-game he was flat (2026-09-07).
        //
        // `mi.bakeAxisConversion = true` was tried here and DOES NOT FIX IT: the FBX header
        // declares Y-up while the geometry is Z-up, so Unity has nothing to convert and only
        // flipped the sign (centre z 0.68 -> -0.68, extents unchanged). Re-adding it will not
        // help; do not spend the round trip.
        //
        // The correction therefore lives in `Client/BodyLoader.cs`, which rotates the body on
        // attach and measures the offset off the corrected axis. The proper fix is a re-export
        // with Blender's "-Y forward, Z up" settings -- but the raw Meshy source is not in this
        // repo and the generation expires 2026-09-09, so load-side is where it can actually be
        // done.

        var takes = mi.defaultClipAnimations;
        for (int i = 0; i < takes.Length; i++)
        {
            // ---- LANDMINE 5 ---------------------------------------------------------------
            // Blender's FBX exporter names every action "Armature|Clip", and `name` is what the
            // clip asset is CALLED in the bundle. `IngvarBody` resolves its six clips BY NAME, so
            // the prefix means all six lookups miss, all six weights stay 0, and Ingvar stands
            // frozen -- with a bundle that passes every other gate: right size, right asset count,
            // rig intact, six clips present. The first bake here shipped exactly that.
            // It also silently defeats the loop table below, because `Looping.Contains` is matching
            // against "Armature|Walk" and never hits. A non-looping idle freezes on its last frame
            // after ten seconds. Strip first, then decide looping, in that order.
            int bar = takes[i].name.LastIndexOf('|');
            if (bar >= 0) takes[i].name = takes[i].name.Substring(bar + 1);

            takes[i].loopTime           = Looping.Contains(takes[i].name);
            takes[i].lockRootRotation   = true;
            takes[i].keepOriginalPositionY = true;
        }
        mi.clipAnimations = takes;
        mi.SaveAndReimport();

        var go = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        var smr = go != null ? go.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
        Debug.Log($"[ValkyriesCargo] model: {takes.Length} clip(s) [{string.Join(", ", takes.Select(t => t.name))}], " +
                  $"SkinnedMeshRenderer={(smr != null)}, bones={(smr != null ? smr.bones.Length : 0)}, " +
                  $"tris={(smr != null && smr.sharedMesh != null ? smr.sharedMesh.triangles.Length / 3 : 0)}");
        if (smr == null)
            Debug.LogWarning("[ValkyriesCargo] no SkinnedMeshRenderer - the rig did not survive import.");

        // Report the clips as they came OUT of the import, not as we asked for them: the name in
        // the bundle and the loop flag are what `IngvarBody` and `BodyMotion` actually meet, and
        // the lengths are what verify item 19 checks against models/README.md.
        var clips = AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<AnimationClip>()
                                 .Where(c => !c.name.StartsWith("__preview"))
                                 .OrderBy(c => c.name).ToArray();
        foreach (var c in clips)
            Debug.Log($"[ValkyriesCargo] clip '{c.name}': {c.length:0.00}s, loop={c.isLooping}, frameRate={c.frameRate}");
        var wrong = clips.Where(c => c.name.Contains("|")).Select(c => c.name).ToArray();
        if (wrong.Length > 0)
            Fail("clip names still carry the exporter's prefix (" + string.Join(", ", wrong) +
                 "). IngvarBody resolves BY NAME and every lookup would miss - see landmine 5.");
        foreach (var want in Looping)
            if (!clips.Any(c => c.name == want && c.isLooping))
                Debug.LogWarning($"[ValkyriesCargo] '{want}' is not looping - it will freeze on its last frame.");
        return true;
    }

    private static void ConfigureTexture()
    {
        var ti = AssetImporter.GetAtPath(Albedo) as TextureImporter;
        if (ti == null) return;
        ti.maxTextureSize     = TexMaxSize;
        ti.textureCompression = TextureImporterCompression.Compressed;   // DXT1, NOT crunched:
        ti.crunchedCompression = false;                                  // crunch is a second lossy
        ti.sRGBTexture        = true;                                    // pass stacked on DXT1, traded
        ti.mipmapEnabled      = true;                                    // for download size - and this
        ti.SaveAndReimport();                                            // texture rides inside the DLL
        Debug.Log($"[ValkyriesCargo] texture: max {TexMaxSize}, DXT1, uncrunched, sRGB");
    }

    private static void Fail(string msg)
    {
        Debug.LogError("[ValkyriesCargo] " + msg);
        if (Application.isBatchMode) EditorApplication.Exit(1);
    }
}
