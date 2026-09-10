#!/usr/bin/env bash
# Decompiles the four Valheim assemblies of one or more builds into project trees, one file per
# type, so two builds can be compared with a directory diff. P10a, docs/P10-P11-FOR-DON.md.
# The Linux twin of decompile-builds.ps1 -- same assemblies, same stamp files, same output shape.
#
#   ./tools/decompile-builds.sh --baseline             # the two INSTALLED builds (read-only)
#   ./tools/decompile-builds.sh --all                  # every shadow build under the shadow root
#   ./tools/decompile-builds.sh --build server-live
#   ./tools/decompile-builds.sh --baseline --all --force   # ignore the stamps, decompile again
#
# OUTPUT LIVES OUTSIDE THE REPO: $SHADOW_ROOT/src/<build>/<assembly>/. Decompiled Valheim is a
# hundred thousand lines of somebody else's code and none of it is ever committed.
#
# The four assemblies are all of them: Splatform (PlatformUserID and the platform ids the admin
# lists are matched with; on 0.221.12 too, 92 types, but never decompiled before PR #71; a build
# without it is skipped with a line), assembly_valheim (the game), assembly_utils (ZDO,
# ZPackage, the networking primitives) and assembly_guiutils - the last a real dependency since
# P7 (Localization). The others in Managed/ (googleanalytics, lux, postprocessing,
# simplemeshcombine, sunshafts) are named nowhere in this mod.
#
# IDEMPOTENT via a stamp file per (build, assembly) carrying the source DLL's size and mtime.
# ilspycmd on assembly_valheim takes minutes; a re-run that has nothing to do says so and costs
# nothing. --force decompiles anyway.
#
# Linux notes the PowerShell twin cannot carry (found running this script, 2026-09-07):
#   - The two baselines resolve through ~/.steam/steam, Valve's own stable symlink to the real
#     Steam root, rather than a hardcoded drive-letter path -- there is no per-machine "Program
#     Files (x86)" equivalent to bake in, and the symlink is the one thing Valve promises stays
#     put across every Linux install.
#   - `dotnet` is not on PATH on this box even though the SDK is installed (only
#     ~/.dotnet/dotnet); resolve_dotnet_root() below falls back to ~/.dotnet directly when
#     `command -v dotnet` finds nothing, which the PowerShell twin's Get-Command equivalent does
#     not need because the Windows installer puts dotnet.exe on PATH.
#   - ilspycmd's own decompile is IDENTICAL in shape to the Windows run: same -p project-tree
#     output, same per-type file layout, so tools/diff-engine.js reads either one unmodified. The
#     file COUNT can differ by one even for the "same" version: the Linux client build carries a
#     <PrivateImplementationDetails> compiler-scratch type the Windows client build does not
#     (docs/engine-sweeps/2026-09-07-linux-baseline-client-vs-server.md) - a compiler artifact
#     nothing here names, not a sign the decompile went wrong.
set -euo pipefail

ASSEMBLIES=(assembly_valheim assembly_utils assembly_guiutils Splatform)

# The two installed builds. NEVER WRITTEN TO - only ever read from here; the output goes to the
# shadow root like everything else. Resolved through Valve's own stable symlink (see header).
BASELINE_CLIENT="${HOME}/.steam/steam/steamapps/common/Valheim"
BASELINE_SERVER="${HOME}/.steam/steam/steamapps/common/Valheim dedicated server"

BUILD=""
ALL=0
BASELINE=0
SHADOW_ROOT="${HOME}/valheim-shadows"
FORCE=0

usage() {
  echo "usage: $0 [--build <name>] [--all] [--baseline] [--shadow-root <dir>] [--force]"
}

while [ $# -gt 0 ]; do
  case "$1" in
    --build) BUILD="$2"; shift 2 ;;
    --all) ALL=1; shift ;;
    --baseline) BASELINE=1; shift ;;
    --shadow-root) SHADOW_ROOT="$2"; shift 2 ;;
    --force) FORCE=1; shift ;;
    --help|-h) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage; exit 2 ;;
  esac
done

# ilspycmd is a .NET tool: a DOTNET_ROOT left over from another machine's layout stops it
# starting with "You must install .NET to run this application" - which reads like a missing
# runtime and is not one. Resolve it from the dotnet actually on PATH, same rule as the
# PowerShell twin; see the header for why this box also tries ~/.dotnet directly.
#
# set -e TRAP, found by testing this script rather than assumed: a helper whose LAST executed
# statement is a plain `[ ... ]` test that comes out false returns that test's exit status (1) as
# the function's own return code. Called bare (not inside an `if`/`&&`), that silently kills the
# whole script the moment nothing was found -- exactly the "nothing found" case every one of
# these helpers exists to handle gracefully. Every helper below ends on an explicit `return 0` for
# exactly this reason; delete one and a build with no Managed folder kills the run with no message
# instead of the "no *_Data/Managed/..." line further down that is supposed to report it.
resolve_dotnet_root() {
  if [ -n "${DOTNET_ROOT:-}" ] && [ -d "${DOTNET_ROOT}/host/fxr" ]; then return 0; fi
  local d
  d="$(command -v dotnet 2>/dev/null || true)"
  if [ -n "$d" ]; then export DOTNET_ROOT="$(dirname "$(readlink -f "$d")")"
  elif [ -x "${HOME}/.dotnet/dotnet" ]; then export DOTNET_ROOT="${HOME}/.dotnet"
  fi
  return 0
}

find_ilspycmd() {
  if [ -x "${HOME}/.dotnet/tools/ilspycmd" ]; then echo "${HOME}/.dotnet/tools/ilspycmd"; return 0; fi
  command -v ilspycmd 2>/dev/null || true
}

get_managed_dir() {                                 # $1: install dir
  local d
  for d in valheim_Data/Managed valheim_server_Data/Managed; do
    [ -f "$1/$d/assembly_valheim.dll" ] && { echo "$1/$d"; return 0; }
  done
  return 0
}

# --- work out which builds were asked for -----------------------------------------------------
declare -A TARGET_DIR=()
target_order=()
add_target() { # $1: name  $2: dir
  if [ -z "${TARGET_DIR[$1]+x}" ]; then target_order+=("$1"); fi
  TARGET_DIR["$1"]="$2"
}

[ "$BASELINE" = 1 ] && { add_target "baseline-client" "$BASELINE_CLIENT"; add_target "baseline-server" "$BASELINE_SERVER"; }
if [ "$ALL" = 1 ] && [ -d "$SHADOW_ROOT" ]; then
  while IFS= read -r -d '' d; do
    name="$(basename "$d")"
    [ "$name" = steamcmd ] && continue
    [ "$name" = src ] && continue
    [ -n "$(get_managed_dir "$d")" ] && add_target "$name" "$d"
  done < <(find -L "$SHADOW_ROOT" -mindepth 1 -maxdepth 1 -type d -print0 | sort -z)
fi
if [ -n "$BUILD" ]; then
  if [ "$BUILD" = "baseline-client" ]; then add_target "baseline-client" "$BASELINE_CLIENT"
  elif [ "$BUILD" = "baseline-server" ]; then add_target "baseline-server" "$BASELINE_SERVER"
  elif [ -d "$BUILD" ]; then add_target "$(basename "$BUILD")" "$(cd "$BUILD" && pwd)"
  else add_target "$BUILD" "$SHADOW_ROOT/$BUILD"
  fi
fi

if [ "${#target_order[@]}" -eq 0 ]; then
  echo "Nothing to do. Pass --baseline, --all, or --build <name>."
  echo "Shadow root: $SHADOW_ROOT"
  [ -d "$SHADOW_ROOT" ] && find -L "$SHADOW_ROOT" -mindepth 1 -maxdepth 1 -type d -printf '  %f\n'
  exit 1
fi

ilspy="$(find_ilspycmd)"
if [ -z "$ilspy" ]; then
  echo "ilspycmd not found. Install it with: dotnet tool install -g ilspycmd" >&2
  exit 1
fi
resolve_dotnet_root
echo "ilspycmd     : $ilspy"
echo "DOTNET_ROOT  : ${DOTNET_ROOT:-<unset - ilspycmd will refuse>}"
echo "output root  : $SHADOW_ROOT/src"
echo

src_root="$SHADOW_ROOT/src"
mkdir -p "$src_root"

grand_t0=$(date +%s)
failures=()

for name in "${target_order[@]}"; do
  install_dir="${TARGET_DIR[$name]}"
  echo "=== $name"
  if [ ! -d "$install_dir" ]; then
    echo "  no such directory: $install_dir"
    failures+=("$name (no directory)")
    continue
  fi
  managed="$(get_managed_dir "$install_dir")"
  if [ -z "$managed" ]; then
    echo "  no *_Data/Managed/assembly_valheim.dll under $install_dir"
    failures+=("$name (no managed folder)")
    continue
  fi
  echo "  managed: $managed"

  for asm in "${ASSEMBLIES[@]}"; do
    dll="$managed/$asm.dll"
    if [ ! -f "$dll" ]; then
      echo "  $asm : NOT PRESENT in this build"
      continue
    fi
    out="$src_root/$name/$asm"
    stamp_path="$src_root/$name/$asm.stamp"
    stamp="$(stat -c '%s %Y' "$dll")"

    if [ "$FORCE" != 1 ] && [ -f "$stamp_path" ] && [ "$(cat "$stamp_path")" = "$stamp" ] && [ -d "$out" ]; then
      n=$(find "$out" -name '*.cs' -type f 2>/dev/null | wc -l || true)
      printf "  %-18s up to date (%s .cs)\n" "$asm" "$n"
      continue
    fi

    rm -rf "$out"
    mkdir -p "$out"

    t0=$(date +%s)
    # -p writes a project tree, one file per type: that is what makes a directory diff between
    # two builds usable at all. -r points the decompiler at the sibling assemblies so
    # cross-assembly types resolve to names instead of tokens.
    set +e
    DOTNET_ROOT="${DOTNET_ROOT:-}" "$ilspy" -p -r "$managed" "$dll" -o "$out" >"$src_root/$name/$asm.ilspy.log" 2>&1
    code=$?
    set -e
    elapsed=$(( $(date +%s) - t0 ))
    n=$(find "$out" -name '*.cs' -type f 2>/dev/null | wc -l || true)

    if [ "$code" -ne 0 ] || [ "$n" -eq 0 ]; then
      echo "  $asm FAILED (exit $code, $n .cs) - see $src_root/$name/$asm.ilspy.log"
      failures+=("$name/$asm")
      continue
    fi
    printf '%s' "$stamp" > "$stamp_path"
    printf "  %-18s %s .cs in %02d:%02d\n" "$asm" "$n" $((elapsed/60)) $((elapsed%60))
  done
done

echo
grand_elapsed=$(( $(date +%s) - grand_t0 ))
printf "Done in %02d:%02d:%02d.\n" $((grand_elapsed/3600)) $(((grand_elapsed%3600)/60)) $((grand_elapsed%60))
if [ "${#failures[@]}" -gt 0 ]; then
  echo "FAILED: $(IFS=', '; echo "${failures[*]}")" >&2
  exit 1
fi
