#!/usr/bin/env bash
# Scaffold the Unity project that bakes Ingvar into an AssetBundle, then bake it.
# The Linux twin of setup-ingvar-unity.ps1 -- same project layout, same gates, same landmines.
#
#   ./tools/setup-ingvar-unity.sh              # scaffold only
#   ./tools/setup-ingvar-unity.sh --build      # scaffold, then bake
#   ./tools/setup-ingvar-unity.sh --embed      # after a bake, copy the bundle into Assets/
#
# The Unity project is a SIBLING of the repo, never inside it: an SDK-style csproj globs every
# .cs beneath it and sweeps Unity's Library/PackageCache into the mod DLL.
#
# Linux notes the PowerShell twin cannot carry:
#   - the Editor is driven directly, not through the `unity` wrapper, so the log is ours.
#   - NO -nographics, on purpose (landmine 1). That needs a real X display; on a headless box
#     use `xvfb-run -a -s "-screen 0 1280x1024x24"`, which gives a device, not a null one.
#   - Arch ships libxml2 SONAME .so.16; the Editor wants .so.2 -> `pacman -S libxml2-legacy`.
set -euo pipefail

BUILD=0; EMBED=0
for a in "$@"; do case "$a" in --build) BUILD=1;; --embed) EMBED=1;; *) echo "unknown: $a"; exit 2;; esac; done

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY_PROJECT="${UNITY_PROJECT:-$(dirname "$REPO")/ValkyriesCargo-Unity}"
EDITOR_VERSION="${EDITOR_VERSION:-6000.0.61f1}"
EDITOR="${EDITOR:-$HOME/Unity/Hub/Editor/$EDITOR_VERSION/Editor/Unity}"
BUNDLE_NAME="valkyriescargo_kit"

say() { printf '  %s\n' "$*"; }
die() { printf '  %s\n' "$*" >&2; exit 1; }

echo; echo "Ingvar bundle setup"
say "repo   : $REPO"
say "unity  : $UNITY_PROJECT"

FBX="$REPO/models/ingvar.fbx"; ALBEDO="$REPO/models/ingvar_albedo.png"
[ -f "$FBX" ]    || die "missing $FBX - is the repo checked out fully?"
[ -f "$ALBEDO" ] || die "missing $ALBEDO - is the repo checked out fully?"
say "source art found"

mkdir -p "$UNITY_PROJECT/Assets/Editor" "$UNITY_PROJECT/ProjectSettings" "$UNITY_PROJECT/Packages"

PV="$UNITY_PROJECT/ProjectSettings/ProjectVersion.txt"
if [ ! -f "$PV" ]; then
  echo "m_EditorVersion: $EDITOR_VERSION" > "$PV"
  say "wrote ProjectVersion.txt ($EDITOR_VERSION)"
fi

# gltfast is left out on purpose: the FBX path does not need it, and its transitive
# com.unity.collections is what makes BuildAssetBundles fail silently (landmine 2).
MANIFEST="$UNITY_PROJECT/Packages/manifest.json"
if [ ! -f "$MANIFEST" ]; then
  cat > "$MANIFEST" <<'JSON'
{
  "dependencies": {
    "com.unity.modules.animation": "1.0.0",
    "com.unity.modules.assetbundle": "1.0.0",
    "com.unity.modules.imageconversion": "1.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.ui": "1.0.0"
  }
}
JSON
  say "wrote Packages/manifest.json (no gltfast)"
fi

cp -f "$FBX"    "$UNITY_PROJECT/Assets/ingvar.fbx"
cp -f "$ALBEDO" "$UNITY_PROJECT/Assets/ingvar_albedo.png"
cp -f "$REPO/tools/unity/IngvarBundleBuilder.cs" "$UNITY_PROJECT/Assets/Editor/IngvarBundleBuilder.cs"
say "copied ingvar.fbx, ingvar_albedo.png and the builder into the project"

BUNDLE="$UNITY_PROJECT/AssetBundles/$BUNDLE_NAME"
LOG="$UNITY_PROJECT/build.log"

if [ "$BUILD" = 1 ]; then
  [ -x "$EDITOR" ] || die "no Editor at $EDITOR (set EDITOR=/path/to/Unity)"
  echo; echo "Baking (a first run imports the FBX; minutes, not seconds)..."
  say "NOT passing -nographics on purpose: texture work goes through Blit+ReadPixels, which"
  say "silently produces EMPTY textures with no graphics device. AwayFromHome shipped that twice."
  rm -f "$LOG"
  "$EDITOR" -batchmode -quit -projectPath "$UNITY_PROJECT" \
            -executeMethod IngvarBundleBuilder.BuildKit -logFile "$LOG" || true
  [ -f "$LOG" ] && grep -E '\[ValkyriesCargo\]|error CS|Exception|Aborting' "$LOG" | sed 's/^/  /' || true
  [ -f "$BUNDLE" ] || die "no bundle at $BUNDLE - read $LOG"
  KB=$(( $(stat -c%s "$BUNDLE") / 1024 ))
  [ "$KB" -ge 64 ] || die "bundle is only $KB KB - the empty-texture or 0-byte failure, not a build."
  say "bundle baked: $KB KB"
fi

if [ "$EMBED" = 1 ]; then
  [ -f "$BUNDLE" ] || die "no bundle to embed - run with --build first."
  mkdir -p "$REPO/Assets"
  cp -f "$BUNDLE" "$REPO/Assets/$BUNDLE_NAME"
  say "copied the bundle to Assets/$BUNDLE_NAME"
  say "now rebuild and CONFIRM THE DLL GREW by roughly that much - a good bake plus a stale copy"
  say "ships the old asset with no change in DLL size."
fi
echo
