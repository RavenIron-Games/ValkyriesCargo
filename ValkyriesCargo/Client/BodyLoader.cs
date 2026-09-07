using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using RavenIron.ValkyriesCargo.Config;
using RavenIron.ValkyriesCargo.Core;
using UnityEngine;

namespace RavenIron.ValkyriesCargo.Client
{
    /// <summary>Where the bundle came from, in one word, for `cargo body`.</summary>
    public enum BodySource
    {
        /// <summary>Not looked for yet.</summary>
        Unknown,
        /// <summary>Nothing to load: no resource, no file beside the DLL, or this is a server.</summary>
        None,
        /// <summary>The resource inside this DLL. The only source that guarantees every player has the same body.</summary>
        Embedded,
        /// <summary>A loose file beside the DLL. A DEVELOPMENT path: it is not what other players have.</summary>
        File,
    }

    /// <summary>One clip the bundle carried, with the length read off it (never assumed).</summary>
    public sealed class BodyClipInfo
    {
        public string Name = "";
        public float Length;
        public AnimationClip Clip;
    }

    /// <summary>
    /// Loads Ingvar's body out of the AssetBundle embedded in this DLL, once, and puts it on the
    /// merchant (design 11.6: "swap the body under the same Humanoid/MonsterAI prefab clone, not a
    /// MeshFilter; keep Character's component set intact").
    ///
    /// **The swap is additive, never destructive.** The merchant stays the `Server.BodyPrefab` clone -
    /// the same `Character`, `Humanoid`, `MonsterAI`, `CapsuleCollider`, `ZNetView`, `ZSyncTransform`,
    /// `ZSyncAnimation`, `VisEquipment` and `CharacterAnimEvent` the engine built and vanilla drives.
    /// Ingvar is a CHILD appended to its root, and the Dverger is hidden by switching his `Renderer`s
    /// off, never by destroying them or deactivating the GameObject they sit on. Two engine facts make
    /// that the safe shape, both read out of the decompiles:
    ///
    /// - `Character.Awake` caches `m_animator = GetComponentInChildren&lt;Animator&gt;()` and takes
    ///   `m_animEvent` and `m_head` from it (Character.cs:502-509); `ZSyncAnimation.Awake` does the same
    ///   (ZSyncAnimation.cs:45) and `NpcTalk.Start` does it AFTER Awake (NpcTalk.cs:82). Unity's search
    ///   is depth-first in child order and our child is appended LAST, so every Animator that existed
    ///   before we arrived is found first. That is the whole defence, and it only holds while the
    ///   vanilla Animator's GameObject stays ACTIVE - which is why the renderers are disabled and the
    ///   `Visual` child is not.
    /// - The renderer sweeps vanilla runs after Awake are scoped to `m_visual`
    ///   (`Character.UpdateLodgroup`, Character.cs:3531; `VisEquipment.UpdateLodgroup`,
    ///   VisEquipment.cs:710), so a body hung off the ROOT is outside all of them. They still reach the
    ///   STAND-IN's renderers, though, through its `LODGroup` - see `HideStandIn`, which switches that
    ///   group off for the same reason it switches the renderers off.
    ///
    /// Never `Unload`. The bundle is loaded once and kept for the process: an unload while any instance
    /// is alive takes the mesh and the material out from under it (models/README.md section 6).
    /// </summary>
    public static class BodyLoader
    {
        /// <summary>The bundle's name, and the suffix its embedded resource is resolved by.</summary>
        public const string BundleName = "valkyriescargo_kit";
        /// <summary>The asset inside it: the whole rigged prefab, not a Mesh (IngvarBundleBuilder tags the FBX).</summary>
        public const string PrefabName = "ingvar";
        /// <summary>The name of the child we hang under the merchant; also how the swap stays idempotent.</summary>
        public const string ChildName = "IngvarBody";
        /// <summary>A ground offset further than this from zero is a warning: his origin is meant to be at his feet.</summary>
        public const float GroundOffsetWarnAt = 0.05f;

        private static bool _tried;
        private static AssetBundle _bundle;
        /// <summary>
        /// Held for the life of the process on purpose. `AssetBundle.LoadFromStream` reads out of the
        /// stream lazily, so the stream has to outlive the bundle - and models/README.md section 6 shows
        /// it inside a `using`, which closes it before the first `LoadAsset` and turns every later read
        /// into an ObjectDisposedException. Nothing is leaked: the bytes are the DLL's own resource, and
        /// the bundle is never unloaded anyway.
        /// </summary>
        private static Stream _stream;
        private static GameObject _prefab;
        private static readonly List<BodyClipInfo> _clips = new List<BodyClipInfo>();
        private static GameObject _preview;

        // ---- what `cargo body` prints ---------------------------------------------------------------

        public static BodySource Source { get; private set; } = BodySource.Unknown;
        /// <summary>The manifest resource name that matched, or "".</summary>
        public static string ResourceName { get; private set; } = "";
        /// <summary>The loose file that was used, or "". Set only when Source is File.</summary>
        public static string FilePath { get; private set; } = "";
        public static bool BundleLoaded => _bundle != null;
        public static bool PrefabFound => _prefab != null;
        public static IReadOnlyList<BodyClipInfo> Clips => _clips;
        public static bool HasSkinnedMesh { get; private set; }
        public static int BoneCount { get; private set; }
        public static int Triangles { get; private set; }
        /// <summary>The union of every renderer's `sharedMesh.bounds`; the authored box, in the model's own space.</summary>
        public static Bounds MeshBounds { get; private set; }
        public static bool BoundsKnown { get; private set; }
        /// <summary>The lift that puts his feet on the parent's origin. Derived, never hardcoded (SETUP section 6). Expected 0.</summary>
        public static float GroundOffset { get; private set; }
        /// <summary>Why there is no body, in words, when there is none.</summary>
        public static string Detail { get; private set; } = "not looked for yet";
        /// <summary>The preview body `cargo body preview` put in the world, or null.</summary>
        public static IngvarBody Preview { get; private set; }

        /// <summary>True when a body can actually be attached: a renderer, the config, and a prefab out of the bundle.</summary>
        public static bool Ready => ValkyriesCargo.HasRenderer && ModConfig.CustomBody.Value && _prefab != null;

        /// <summary>The clip by the name it carries in the bundle, or null.</summary>
        public static AnimationClip Clip(string name)
        {
            for (int i = 0; i < _clips.Count; i++)
                if (string.Equals(_clips[i].Name, name, StringComparison.Ordinal)) return _clips[i].Clip;
            return null;
        }

        // ---- loading --------------------------------------------------------------------------------

        /// <summary>
        /// Find and open the bundle. Runs at most once per process, whatever the answer: a missing
        /// bundle is a normal state (the Dverger stands in), not something to retry every frame.
        /// </summary>
        public static void Load()
        {
            if (_tried) return;
            _tried = true;

            // A dedicated server has no graphics device: every material resolves to
            // Hidden/InternalErrorShader and every appearance check reads as failure (SETUP section 4).
            // There is nothing to render and nothing honest to say, so it does not look.
            if (!ValkyriesCargo.HasRenderer)
            {
                Source = BodySource.None;
                Detail = "client only; not loaded here";
                return;
            }

            try
            {
                OpenBundle();
                if (_bundle == null)
                {
                    Source = BodySource.None;
                    if (Detail.Length == 0) Detail = "no bundle";
                    return;
                }
                _prefab = _bundle.LoadAsset<GameObject>(PrefabName);
                ReadClips();
                Measure();
            }
            catch (Exception ex)
            {
                Source = BodySource.None;
                Detail = "threw: " + ex.Message;
                ValkyriesCargo.Log.LogError("body: loading " + BundleName + " threw: " + ex);
                return;
            }

            if (_prefab == null)
            {
                Detail = "bundle opened but it holds no GameObject named '" + PrefabName + "'";
                ValkyriesCargo.Log.LogWarning("body: " + Detail + " (a bake whose asset-bundle tag did not stick builds perfectly and is missing the model)");
                return;
            }

            Detail = "loaded";
            ValkyriesCargo.Log.LogInfo(
                "body: source " + Source.ToString().ToLowerInvariant() + ", prefab '" + _prefab.name + "', " +
                _clips.Count + " clip(s) [" + ClipList() + "], SkinnedMeshRenderer=" + HasSkinnedMesh +
                ", bones=" + BoneCount + ", tris=" + Triangles + ", " + BoundsWords() +
                ", ground offset " + GroundOffset.ToString("0.###") + " m");

            if (BoundsKnown && Math.Abs(GroundOffset) > GroundOffsetWarnAt)
                ValkyriesCargo.Log.LogWarning(
                    "body: the ground offset is " + GroundOffset.ToString("0.###") + " m, not ~0. His origin is meant to be AT his feet " +
                    "(models/README.md section 1). It is used as measured rather than as expected, but the bake is worth a look.");

            for (int i = 0; i < BodyMotion.ClipCount; i++)
            {
                string want = BodyMotion.ClipName((BodyClip)i);
                if (Clip(want) == null)
                    ValkyriesCargo.Log.LogWarning("body: the bundle has no clip named '" + want + "'; it plays at weight 0 and the rest carry on.");
            }
        }

        /// <summary>The embedded resource first; a loose file beside the DLL only as a development fallback.</summary>
        private static void OpenBundle()
        {
            Assembly asm = Assembly.GetExecutingAssembly();

            // By SUFFIX, so a rename of the LogicalName prefix cannot silently break it.
            string[] names = asm.GetManifestResourceNames();
            string res = null;
            for (int i = 0; i < names.Length; i++)
                if (names[i].EndsWith(BundleName, StringComparison.Ordinal)) { res = names[i]; break; }

            if (res != null)
            {
                ResourceName = res;
                _stream = asm.GetManifestResourceStream(res);
                if (_stream != null) _bundle = AssetBundle.LoadFromStream(_stream);
                if (_bundle != null) { Source = BodySource.Embedded; return; }
                Detail = "the resource '" + res + "' is embedded but AssetBundle.LoadFromStream refused it (built with a different Unity than 6000.0.61f1?)";
                ValkyriesCargo.Log.LogError("body: " + Detail);
                return;
            }

            // Nothing embedded. A loose file lets a fresh bake be tried without a rebuild; it is NOT
            // what other players have, so it says so every time (models/README.md section 2).
            string dir = "";
            try { dir = Path.GetDirectoryName(asm.Location ?? ""); } catch { }
            if (string.IsNullOrEmpty(dir))
            {
                Detail = "no '" + BundleName + "' resource in this DLL and no folder to look in";
                return;
            }
            string file = Path.Combine(dir, BundleName);
            if (!File.Exists(file))
            {
                Detail = "no '" + BundleName + "' resource in this DLL and no file beside it; the Dverger stands in";
                ValkyriesCargo.Log.LogInfo("body: " + Detail + " (bake it with tools\\setup-ingvar-unity.ps1 -Build, embed it with -Embed)");
                return;
            }
            _bundle = AssetBundle.LoadFromFile(file);
            if (_bundle == null)
            {
                Detail = "the file " + file + " is not an AssetBundle this Unity can open";
                ValkyriesCargo.Log.LogError("body: " + Detail);
                return;
            }
            Source = BodySource.File;
            FilePath = file;
            ValkyriesCargo.Log.LogWarning(
                "body: loaded from the LOCAL FILE " + file + ", NOT from the copy embedded in this DLL. Other players are " +
                "running whatever their own DLL carries, so this body is yours alone. Fine for trying a bake; embed it before it ships.");
        }

        private static void ReadClips()
        {
            _clips.Clear();
            AnimationClip[] found = _bundle.LoadAllAssets<AnimationClip>();
            if (found == null) return;
            for (int i = 0; i < found.Length; i++)
            {
                AnimationClip c = found[i];
                if (c == null) continue;
                _clips.Add(new BodyClipInfo { Name = c.name, Length = c.length, Clip = c });
            }
        }

        /// <summary>
        /// The mesh box and the lift it implies. From `sharedMesh.bounds` unioned over the renderers,
        /// NOT `SkinnedMeshRenderer.bounds`: that one is the authored bind-pose box and was wrong by
        /// 0.27-0.64 m on IronCohort's Meshy rigs, which floated all four of them (SETUP section 6).
        /// </summary>
        private static void Measure()
        {
            HasSkinnedMesh = false;
            BoneCount = 0;
            Triangles = 0;
            BoundsKnown = false;
            GroundOffset = 0f;
            if (_prefab == null) return;

            Bounds box = new Bounds();
            SkinnedMeshRenderer[] skinned = _prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinned.Length; i++)
            {
                SkinnedMeshRenderer r = skinned[i];
                if (r == null) continue;
                HasSkinnedMesh = true;
                if (r.bones != null && r.bones.Length > BoneCount) BoneCount = r.bones.Length;
                AddMesh(r.sharedMesh, ref box);
            }
            MeshFilter[] filters = _prefab.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
                if (filters[i] != null) AddMesh(filters[i].sharedMesh, ref box);

            if (!BoundsKnown) return;
            MeshBounds = box;
            // The offset that puts the lowest authored vertex on the parent's origin. Expected 0.
            GroundOffset = -box.min.y;
        }

        private static void AddMesh(Mesh mesh, ref Bounds box)
        {
            if (mesh == null) return;
            // GetIndexCount reads the submesh descriptor, so it needs no Read/Write and allocates nothing;
            // mesh.triangles would copy the whole index buffer into managed memory to count it.
            for (int s = 0; s < mesh.subMeshCount; s++) Triangles += (int)(mesh.GetIndexCount(s) / 3);
            if (!BoundsKnown) { box = mesh.bounds; BoundsKnown = true; }
            else box.Encapsulate(mesh.bounds);
        }

        // ---- putting him on the merchant ------------------------------------------------------------

        /// <summary>
        /// Give this character Ingvar's body, or leave the stand-in alone and answer null. Idempotent:
        /// called twice on the same character it returns the same driver, so an ownership handoff or a
        /// second Awake cannot grow a second body.
        /// </summary>
        public static IngvarBody Attach(Character c)
        {
            if (c == null) return null;
            if (!ValkyriesCargo.HasRenderer) return null;           // a server draws nothing
            if (!ModConfig.CustomBody.Value) return null;           // the admin asked for the stand-in
            Load();
            if (_prefab == null) return null;                       // no bundle, or a bad one: the Dverger stands in

            try
            {
                Transform already = c.transform.Find(ChildName);
                if (already != null)
                {
                    IngvarBody had = already.GetComponent<IngvarBody>();
                    if (had != null) return had;
                    UnityEngine.Object.Destroy(already.gameObject);   // a half-built body from a throw: start again
                }

                GameObject go = UnityEngine.Object.Instantiate(_prefab);
                go.name = ChildName;
                // worldPositionStays:false so the local transform below is what lands, and the prefab's
                // own localScale survives - the Armature's 0.01 is CORRECT and must never be "fixed".
                go.transform.SetParent(c.transform, false);
                go.transform.localPosition = new Vector3(0f, GroundOffset, 0f);
                go.transform.localRotation = Quaternion.identity;

                int hidden = HideStandIn(c.transform, go.transform);

                IngvarBody body = go.AddComponent<IngvarBody>();
                body.Bind(c);
                ValkyriesCargo.Log.LogInfo(
                    "body: Ingvar attached to '" + c.name + "' at local y " + GroundOffset.ToString("0.###") +
                    "; " + hidden + " stand-in renderer(s) switched off (never destroyed: Character.m_animator, VisEquipment, " +
                    "CharacterAnimEvent, ZSyncAnimation and the CapsuleCollider all keep working)");
                return body;
            }
            catch (Exception ex)
            {
                ValkyriesCargo.Log.LogError("body: attaching to '" + c.name + "' threw; the stand-in is kept: " + ex);
                return null;
            }
        }

        /// <summary>
        /// Switch off every renderer under the character except ours. Disabled, never destroyed and never
        /// deactivated: the GameObjects have to stay active or a later `GetComponentInChildren&lt;Animator&gt;`
        /// would skip the vanilla animator and find OURS. Re-run on a cadence by the driver because
        /// `VisEquipment` instantiates a fresh crossbow whenever the merchant's equipment changes.
        ///
        /// The stand-in's `LODGroup` goes off with them, and it has to. Unity's LOD system OWNS
        /// `Renderer.enabled` for every renderer listed in a LOD level: it switches them on when the level
        /// becomes current, so a disable of ours survives only until the next LOD transition. Vanilla drives
        /// exactly that transition on this character - `Character.SetVisible` (Character.cs:3775) throws
        /// `m_lodGroup.localReferencePoint` out to 999999 and back on every change of ZDO ownership, and
        /// `VisEquipment.UpdateLodgroup` (VisEquipment.cs:704) refills `LOD[0].renderers` from every renderer
        /// under `Visual` on any equipment change. Without this the Dverger reappears on an ownership handoff
        /// and stays visible until the 2 s re-hide. Disabling the component is safe: both of those vanilla
        /// paths only read `GetLODs`/`SetLODs` and write `localReferencePoint`, neither of which needs it
        /// enabled, and every renderer it managed is one we just switched off.
        /// </summary>
        public static int HideStandIn(Transform root, Transform keep)
        {
            int hidden = 0;
            if (root == null) return 0;
            Renderer[] all = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null || !r.enabled) continue;
                if (keep != null && r.transform.IsChildOf(keep)) continue;
                r.enabled = false;
                hidden++;
            }

            LODGroup[] groups = root.GetComponentsInChildren<LODGroup>(true);
            for (int i = 0; i < groups.Length; i++)
            {
                LODGroup g = groups[i];
                if (g == null || !g.enabled) continue;
                if (keep != null && g.transform.IsChildOf(keep)) continue;
                g.enabled = false;
            }
            return hidden;
        }

        // ---- the console's preview ------------------------------------------------------------------

        /// <summary>
        /// A body with nothing behind it: no ZDO, no ZNetView, nothing networked, nobody else can see it.
        /// `cargo body preview` uses it to put Ingvar on a screen before P5 exists to carry him.
        /// A second call replaces the first.
        /// </summary>
        public static IngvarBody StartPreview(Vector3 groundPos, Quaternion rotation)
        {
            ClearPreview();
            Load();
            if (_prefab == null) return null;
            GameObject go = UnityEngine.Object.Instantiate(_prefab);
            go.name = ChildName + "_preview";
            go.transform.position = groundPos + new Vector3(0f, GroundOffset, 0f);
            go.transform.rotation = rotation;
            _preview = go;
            Preview = go.AddComponent<IngvarBody>();
            return Preview;
        }

        public static bool ClearPreview()
        {
            if (_preview == null) { Preview = null; return false; }
            UnityEngine.Object.Destroy(_preview);
            _preview = null;
            Preview = null;
            return true;
        }

        // ---- words for the console ------------------------------------------------------------------

        public static string ClipList()
        {
            if (_clips.Count == 0) return "none";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _clips.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(_clips[i].Name).Append(' ').Append(_clips[i].Length.ToString("0.00")).Append('s');
            }
            return sb.ToString();
        }

        public static string BoundsWords()
        {
            if (!BoundsKnown) return "mesh bounds unknown";
            Vector3 min = MeshBounds.min, size = MeshBounds.size;
            return "mesh bounds y " + min.y.ToString("0.###") + " to " + MeshBounds.max.y.ToString("0.###") +
                   " (" + size.x.ToString("0.##") + " x " + size.y.ToString("0.##") + " x " + size.z.ToString("0.##") + " m)";
        }

        /// <summary>The one line `cargo status` carries.</summary>
        public static string StatusLine()
        {
            return "body: " + Source.ToString().ToLowerInvariant() +
                   ", prefab " + (_prefab != null ? "yes" : "no") +
                   ", " + _clips.Count + " clip(s)" +
                   ", CustomBody " + (ModConfig.CustomBody != null ? ModConfig.CustomBody.Value.ToString().ToLowerInvariant() : "unbound");
        }
    }
}
