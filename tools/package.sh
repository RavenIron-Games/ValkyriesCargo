#!/usr/bin/env bash
# Build the store package: dist/RavenIronStudios-ValkyriesCargo-<version>.zip and a populated
# HexiumDist/ folder. The Linux twin of package.ps1 -- same layout, same hard-won rules.
#
# The layout was learned on FireFront's upload day, 2026-08-27, and it is not negotiable:
#   - store files (manifest.json, README.md, CHANGELOG.md, icon.png) at the ROOT of the zip
#   - the DLL under plugins/, because Hexium REFUSES a root-level DLL
#   - entries named with FORWARD slashes; a zip with backslashes is spec-invalid and Hexium's
#     parser reports the useless "No manifest.json found"
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
ROOT="$PWD"
export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"; export PATH="$DOTNET_ROOT:$PATH"

VERSION=$(grep -oPm1 '(?<=<Version>)[^<]+' ValkyriesCargo/ValkyriesCargo.csproj)
[ -n "$VERSION" ] || { echo "no <Version> in the csproj"; exit 1; }
echo "version $VERSION"

# The bundle is a BUILD INPUT and is gitignored, so a clone that has not baked ships the stand-in.
# package.ps1 prints this either way and so do we: "no bundle" is also a fact about what is shipping.
BUNDLE="$ROOT/Assets/valkyriescargo_kit"
if [ -f "$BUNDLE" ]; then
  echo "body bundle present: $(stat -c%s "$BUNDLE") bytes"
else
  echo "WARNING: no Assets/valkyriescargo_kit - this package ships the STAND-IN body (Dverger)."
  echo "         Run ./tools/setup-ingvar-unity.sh --build --embed first."
fi

echo "building Release..."
dotnet build ValkyriesCargo/ValkyriesCargo.csproj -c Release -v q --nologo
DLL="$ROOT/ValkyriesCargo/bin/Release/ValkyriesCargo.dll"
[ -f "$DLL" ] || { echo "no Release DLL at $DLL"; exit 1; }
DLLSIZE=$(stat -c%s "$DLL")
echo "DLL $DLLSIZE bytes"
if [ -f "$BUNDLE" ] && [ "$DLLSIZE" -lt "$(stat -c%s "$BUNDLE")" ]; then
  echo "WARNING: the DLL is SMALLER than the bundle - it cannot be carrying it."
fi

STAGE="$ROOT/dist/stage"
rm -rf "$STAGE"; mkdir -p "$STAGE/plugins"
# The README that goes in the zip is the STORE PAGE, and the root README.md is the developer's:
# 257 lines of layout, house style and build commands, opening with a status line. Shipping that as
# the store page is a real mistake and it was one call away from happening.
STORE_README="$ROOT/HexiumDist/README.md"
[ -f "$STORE_README" ] || STORE_README="$ROOT/README.md"
echo "store page: ${STORE_README#$ROOT/}"
cp manifest.json CHANGELOG.md icon.png "$STAGE/"
cp "$STORE_README" "$STAGE/README.md"
cp "$DLL" "$STAGE/plugins/"

ZIP="$ROOT/dist/RavenIronStudios-ValkyriesCargo-$VERSION.zip"
rm -f "$ZIP"
( cd "$STAGE" && zip -q -X -D -r "$ZIP" . -x '.*' ) # -X drops attrs, -D drops directory entries
echo "zip $(stat -c%s "$ZIP") bytes -> ${ZIP#$ROOT/}"

# The Hexium folder is the same payload unzipped, kept in the repo so the store upload is a
# drag-and-drop and so what shipped is reviewable in a diff.
rm -rf "$ROOT/HexiumDist/plugins"
mkdir -p "$ROOT/HexiumDist/plugins"
cp manifest.json CHANGELOG.md icon.png "$ROOT/HexiumDist/"   # README.md there IS the store page
cp "$DLL" "$ROOT/HexiumDist/plugins/"
echo "HexiumDist/ populated:"
find "$ROOT/HexiumDist" -type f -printf '  %s\t%P\n' | sort -k2

echo
echo "entries in the zip (must be forward-slashed, manifest.json at the root):"
unzip -Z1 "$ZIP" | sed 's/^/  /'
