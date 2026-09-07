#!/usr/bin/env bash
# Pulls SHADOW COPIES of the Valheim client and dedicated-server builds with steamcmd, into
# throwaway directories that are never a real install. P10a, docs/P10-P11-FOR-DON.md.
# The Linux twin of fetch-builds.ps1 -- same jobs, same refusals, same argument-order trap.
#
#   ./tools/fetch-builds.sh                                    # both server branches, live and public-test
#   ./tools/fetch-builds.sh --branch live                      # live dedicated server only
#   ./tools/fetch-builds.sh --which client --account <name>    # client; steamcmd PROMPTS for the password
#   ./tools/fetch-builds.sh --which both --account <name> --whatif
#
# THE ARGUMENT-ORDER TRAP, same as the PowerShell twin. steamcmd applies its + arguments IN
# ORDER. If +force_install_dir comes after +login, or after +app_update, steamcmd silently uses
# the DEFAULT library -- which on this machine is the one holding the real Valheim install. That
# is exactly how a "shadow copy" quietly becomes an overwrite of a working game. Order here is
# always:
#
#     +force_install_dir <dir>   +login <who>   +app_update <id> [-beta <b>] validate   +quit
#
# THE REFUSALS. Everything is checked BEFORE steamcmd is started, not per-app while it runs. A
# target directory must be exactly $SHADOW_ROOT/<known leaf>, and must not sit inside the Steam
# install, any library from libraryfolders.vdf, or this repo.
#
# NO PASSWORD EVER TOUCHES THIS SCRIPT. --account passes only the account NAME. steamcmd asks
# for the password (and Steam Guard) on the console itself, and the answer goes to steamcmd.
# There is deliberately no --password flag to add one to, and no credential file is written.
#
# Linux notes the PowerShell twin cannot carry (all found running this script, 2026-09-07):
#   - Valve ships a SEPARATE tarball for Linux (steamcmd_linux.tar.gz, ~/.exe on Windows is a
#     different file); the extracted entry point is steamcmd.sh, not steamcmd.exe.
#   - NO 32-BIT-LIBRARY LANDMINE FOUND on this Arch box: steamcmd's linux32 binary ran and
#     self-updated cleanly with no missing .so. If a future box refuses to start, that is the
#     usual steamcmd-on-Linux failure (needs lib32-* / multilib) and worth a line here once seen
#     for real -- not assumed in advance, per CLAUDE.md's "read the actual body" rule.
#   - steamcmd writes ITS OWN logs to ~/.local/share/Steam/logs regardless of where steamcmd.sh
#     lives or is run from. That is steamcmd's own behaviour, not a leak from this script's
#     refusals -- it is logging, not game files, and nothing this script forbids covers it.
#   - No Windows registry, so there is no HKCU/HKLM SteamPath lookup: Valve's own launcher always
#     creates ~/.steam/steam as a stable symlink to the real root, and that plus this repo plus
#     every libraryfolders.vdf entry is the whole forbidden list.
#   - The default shadow root is $HOME/valheim-shadows, never a hardcoded username (the Windows
#     twin's default bakes in C:\Users\donfr\...; this box's user is not Don).
#   - ASCII-ONLY was the Windows twin's rule because PowerShell 5.1 misreads a BOM-less UTF-8 file
#     as ANSI and an em-dash decodes into a curly quote that stops the parser. Bash has no such
#     landmine; this file uses "--" instead of an em-dash anyway, to keep a straight diff against
#     the PowerShell prose where the wording matches.
set -euo pipefail

STEAMCMD_URL="https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz"
APP_CLIENT=892970
APP_SERVER=896660

WHICH="server"
BRANCH="both"
ACCOUNT=""
SHADOW_ROOT="${HOME}/valheim-shadows"
WHATIF=0

usage() {
  echo "usage: $0 [--which server|client|both] [--branch live|public-test|both]"
  echo "          [--account <name>] [--shadow-root <dir>] [--whatif]"
}

while [ $# -gt 0 ]; do
  case "$1" in
    --which) WHICH="$2"; shift 2 ;;
    --branch) BRANCH="$2"; shift 2 ;;
    --account) ACCOUNT="$2"; shift 2 ;;
    --shadow-root) SHADOW_ROOT="$2"; shift 2 ;;
    --whatif) WHATIF=1; shift ;;
    --help|-h) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage; exit 2 ;;
  esac
done
case "$WHICH" in server|client|both) ;; *) echo "--which must be server, client or both" >&2; exit 2 ;; esac
case "$BRANCH" in live|public-test|both) ;; *) echo "--branch must be live, public-test or both" >&2; exit 2 ;; esac

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# --- the only directory names this script will ever write to -------------------------------
# One leaf per (app, branch), same four as the PowerShell twin. Anything not on this list is
# refused, so a typo in an argument cannot invent a path.
declare -A LEAF_OF=( [server:live]="server-live" [server:public-test]="server-test"
                     [client:live]="client-live" [client:public-test]="client-test" )
declare -A APP_OF=( [server:live]=$APP_SERVER [server:public-test]=$APP_SERVER
                    [client:live]=$APP_CLIENT [client:public-test]=$APP_CLIENT )
declare -A BETA_OF=( [server:live]="" [server:public-test]="public-test"
                     [client:live]="" [client:public-test]="public-test" )
declare -A ANON_OF=( [server:live]=1 [server:public-test]=1 [client:live]=0 [client:public-test]=0 )

normal_path() {                                    # $1: a path, possibly not yet existing
  [ -n "${1:-}" ] || { echo ""; return; }
  realpath -m -- "$1" 2>/dev/null | sed 's:/*$::'
}

# True when $2 is $1 or lives underneath it. Compares whole path SEGMENTS, so
# "$HOME/valheim-shadows-old" is not treated as inside "$HOME/valheim-shadows".
is_inside() {                                       # $1: child  $2: parent
  local c p
  c="$(normal_path "$1")"; p="$(normal_path "$2")"
  [ -n "$c" ] && [ -n "$p" ] || return 1
  [ "$c" = "$p" ] && return 0
  case "$c" in "$p"/*) return 0 ;; esac
  return 1
}

# Every place a real install could live: the Steam root Valve's own launcher always symlinks,
# this repo, and every library in libraryfolders.vdf. No registry on Linux -- see the header.
forbidden_roots() {
  local roots=() vdf path
  [ -d "${HOME}/.steam/steam" ] && roots+=("${HOME}/.steam/steam")
  [ -d "${HOME}/.local/share/Steam" ] && roots+=("${HOME}/.local/share/Steam")
  roots+=("$REPO_ROOT")
  for vdf in "${HOME}/.steam/steam/steamapps/libraryfolders.vdf" \
             "${HOME}/.steam/steam/config/libraryfolders.vdf" \
             "${HOME}/.local/share/Steam/steamapps/libraryfolders.vdf" \
             "${HOME}/.local/share/Steam/config/libraryfolders.vdf"; do
    [ -f "$vdf" ] || continue
    while IFS= read -r path; do roots+=("$path"); done < <(
      grep -oP '"path"\s+"\K[^"]+' "$vdf" 2>/dev/null || true)
  done
  local n out=() seen=""
  for path in "${roots[@]}"; do
    n="$(normal_path "$path")"
    [ -n "$n" ] || continue
    case "$seen" in *"|$n|"*) continue ;; esac
    seen="$seen|$n|"
    out+=("$n")
  done
  printf '%s\n' "${out[@]}"
}

# ilspycmd is a .NET tool: a DOTNET_ROOT left over from another machine's layout stops it
# starting with "You must install .NET to run this application". Resolve it from the dotnet
# actually on PATH rather than trusting the environment -- same rule as the PowerShell twin.
# ON THIS BOX dotnet is not on PATH at all (only under ~/.dotnet); if yours is not either, pass
# DOTNET_ROOT=$HOME/.dotnet yourself, same as CLAUDE.md's decompile instructions do.
#
# set -e TRAP, found by testing this script rather than assumed: a helper whose LAST executed
# statement is a plain `[ ... ]` or `[[ ... ]]` test that comes out false returns that test's
# exit status (1) as the function's own return code. Called bare (not inside an `if`/`&&`), that
# silently kills the whole script the moment nothing was found -- exactly the "nothing found"
# case every one of these helpers exists to handle gracefully. Empirically confirmed here (not
# just reasoned about): `var=$(cmd | grep-with-no-match | head -1)` and a bare call to a function
# ending on a false test BOTH abort a `set -e` script with no message, while the same pattern
# inside a `[ "$(...)" = x ]` test or a `mapfile < <(...)` does not. Every helper below ends on an
# explicit `return 0` or `|| true` for exactly this reason -- delete one and grep for it losing
# output silently, never a bash error, is what comes back.
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

# The four numbers that are the real identity of a build. The Steam build id alone is not
# enough, because a branch can be re-pushed. Read from the assembly by decompiling the
# (internal) Version type -- same regexes as the PowerShell twin, against ilspycmd's own text.
get_valheim_versions() {                            # $1: managed dir  -> "game net player world"
  local managed="$1" ilspy text game=? net=? player=? world=?
  ilspy="$(find_ilspycmd)"
  [ -n "$ilspy" ] || { echo "? ? ? ?"; return; }
  resolve_dotnet_root
  text="$(DOTNET_ROOT="${DOTNET_ROOT:-}" "$ilspy" -r "$managed" "$managed/assembly_valheim.dll" -t Version 2>/dev/null || true)"
  [ -n "$text" ] || { echo "? ? ? ?"; return; }
  # Anchored on CurrentVersion specifically: Version.cs declares FOUR `new GameVersion(...)`
  # calls (three FirstVersionWith* historical markers plus CurrentVersion itself), and an
  # unanchored match silently takes the first of the four -- caught by testing this script
  # against the real assembly, where it read 0.214.301 instead of 0.221.12.
  # Every pipeline ends `|| true`: pipefail makes a grep that finds nothing fail the whole
  # assignment, and under set -e that would abort the script here rather than fall through to
  # the "?" default below -- confirmed by testing (see the note above resolve_dotnet_root).
  game="$(printf '%s' "$text" | grep -oP 'CurrentVersion\s*\{[^}]*\}\s*=\s*new GameVersion\(\K\d+, *\d+, *\d+' | head -1 | tr -d ' ' | tr ',' '.' || true)"
  [ -n "$game" ] || game="?"
  net="$(printf '%s' "$text" | grep -oP 'm_networkVersion\s*=\s*\K\d+' | head -1 || true)"; [ -n "$net" ] || net="?"
  player="$(printf '%s' "$text" | grep -oP 'm_playerVersion\s*=\s*\K\d+' | head -1 || true)"; [ -n "$player" ] || player="?"
  world="$(printf '%s' "$text" | grep -oP 'm_worldVersion\s*=\s*\K\d+' | head -1 || true)"; [ -n "$world" ] || world="?"
  echo "$game $net $player $world"
}

# What branches this app actually advertises today. steamcmd's answer to a branch that does not
# exist is "ERROR! Failed to set beta '<name>'" and nothing else -- it does not say which names
# would have worked, and it exits non-zero whether the branch is gone, renamed, or needs a
# password. Asking first turns that into a sentence. Costs one anonymous app_info round trip.
# Same brace-depth walk as the PowerShell twin's Get-SteamBranches, over steamcmd's own KeyValue
# text dump.
get_steam_branches() {                              # $1: steamcmd.sh  $2: app id
  "$1" +login anonymous +app_info_update 1 +app_info_print "$2" +quit 2>/dev/null | awk '
    /^\s*"branches"\s*$/ { inb=1; next }
    inb && /\{/ { depth++; next }
    inb && /\}/ { depth--; if (depth <= 0) { exit }; next }
    inb && depth == 1 && match($0, /^\s*"([^"]+)"\s*$/, m) { print m[1] }
  '
}

write_build_facts() {                               # $1: label  $2: install dir  $3: app id
  local label="$1" dir="$2" app="$3" acf managed b f sz mt v
  echo
  echo "$label -> $dir"
  # A shadow install keeps its manifest under <dir>/steamapps; a real Steam install keeps it two
  # levels up, beside the "common" folder. Look in both, same as the PowerShell twin.
  acf="$dir/steamapps/appmanifest_$app.acf"
  [ -f "$acf" ] || acf="$(dirname "$(dirname "$dir")")/appmanifest_$app.acf"
  if [ -f "$acf" ]; then
    b="$(grep -oP '"buildid"\s+"\K\d+' "$acf" | head -1 || true)"
    echo "  steam buildid : $b"
  else
    echo "  steam buildid : NO $acf - the fetch did not complete"
  fi
  managed="$(get_managed_dir "$dir")"
  if [ -z "$managed" ]; then
    echo "  assembly      : no *_Data/Managed/assembly_valheim.dll under this directory"
    return
  fi
  f="$managed/assembly_valheim.dll"
  sz="$(stat -c%s "$f")"; mt="$(stat -c%y "$f")"
  printf "  assembly      : %s bytes, %s\n" "$sz" "$mt"
  v="$(get_valheim_versions "$managed")"
  if [ "$v" = "? ? ? ?" ]; then
    echo "  versions      : ilspycmd not available or DOTNET_ROOT unset - see the header note"
  else
    read -r vg vn vp vw <<<"$v"
    echo "  game version  : $vg"
    echo "  network / player / world : $vn / $vp / $vw"
  fi
}

# --- work out what was asked, before anything is downloaded ---------------------------------
which_list=(); [ "$WHICH" = both ] && which_list=(server client) || which_list=("$WHICH")
branch_list=(); [ "$BRANCH" = both ] && branch_list=(live public-test) || branch_list=("$BRANCH")

jobs=()
for w in "${which_list[@]}"; do
  for b in "${branch_list[@]}"; do
    key="$w:$b"
    [ -n "${LEAF_OF[$key]+x}" ] || { echo "No known leaf for $key - refusing." >&2; exit 1; }
    jobs+=("$key")
  done
done

need_account=0
for key in "${jobs[@]}"; do [ "${ANON_OF[$key]}" = 0 ] && need_account=1; done
if [ "$need_account" = 1 ] && [ -z "$ACCOUNT" ]; then
  echo "The client (app $APP_CLIENT) needs an account that owns Valheim." >&2
  echo "Pass --account <name>. steamcmd will ask for the password itself; this script never sees it." >&2
  exit 1
fi
if [ -n "$ACCOUNT" ] && [ "$need_account" = 0 ]; then
  echo "--account is ignored: the dedicated server (app $APP_SERVER) takes +login anonymous."
fi

# --- HARD REFUSALS, all of them, before a single byte moves ----------------------------------
shadow_root_norm="$(normal_path "$SHADOW_ROOT")"
mapfile -t forbidden < <(forbidden_roots)

echo "Shadow root : $shadow_root_norm"
printf 'Refused     : %s\n' "$(IFS='; '; echo "${forbidden[*]}")"

fatal=()
[ -n "$shadow_root_norm" ] || fatal+=("the shadow root is not a usable path: '$SHADOW_ROOT'")
for f in "${forbidden[@]}"; do
  is_inside "$shadow_root_norm" "$f" && fatal+=("the shadow root is inside a real install or the repo: '$shadow_root_norm' is under '$f'")
  is_inside "$f" "$shadow_root_norm" && fatal+=("a real install or the repo is inside the shadow root: '$f' is under '$shadow_root_norm'")
done
for key in "${jobs[@]}"; do
  leaf="${LEAF_OF[$key]}"; app="${APP_OF[$key]}"
  d="$(normal_path "$shadow_root_norm/$leaf")"
  [ "$(dirname "$d")" = "$shadow_root_norm" ] || fatal+=("$leaf: '$d' is not directly under the shadow root")
  [ "$(basename "$d")" = "$leaf" ] || fatal+=("$leaf: leaf name does not match")
  for f in "${forbidden[@]}"; do
    is_inside "$d" "$f" && fatal+=("$leaf: target '$d' is inside '$f'")
  done
  # A directory that already holds a Steam manifest for a DIFFERENT app is somebody else's
  # install that happens to sit here. Refuse rather than validate over it.
  if [ -d "$d/steamapps" ]; then
    while IFS= read -r -d '' other; do
      [ "$(basename "$other")" = "appmanifest_$app.acf" ] || fatal+=("$leaf: '$d' already holds $(basename "$other") - that is another app's install")
    done < <(find "$d/steamapps" -maxdepth 1 -name 'appmanifest_*.acf' -print0 2>/dev/null)
  fi
done
if [ "${#fatal[@]}" -gt 0 ]; then
  echo
  echo "REFUSING TO RUN:" >&2
  printf '  %s\n' "${fatal[@]}" >&2
  exit 1
fi
passed_leaves=(); for key in "${jobs[@]}"; do passed_leaves+=("${LEAF_OF[$key]}"); done
echo "Refusal checks passed for: $(IFS=', '; echo "${passed_leaves[*]}")"

# --- steamcmd, installed into the shadow root -------------------------------------------------
steamcmd_dir="$shadow_root_norm/steamcmd"
steamcmd_exe="$steamcmd_dir/steamcmd.sh"

if [ ! -x "$steamcmd_exe" ]; then
  echo
  echo "steamcmd is not installed at $steamcmd_exe"
  echo "  source: $STEAMCMD_URL (Valve's official installer, about 2.4 MB)"
  if [ "$WHATIF" = 1 ]; then
    echo "  --whatif: not downloading."
  else
    mkdir -p "$steamcmd_dir"
    tarball="$steamcmd_dir/steamcmd_linux.tar.gz"
    curl -sL --fail -o "$tarball" "$STEAMCMD_URL"
    echo "  downloaded $(stat -c%s "$tarball") bytes"
    tar -xzf "$tarball" -C "$steamcmd_dir"
    rm -f "$tarball"
    [ -x "$steamcmd_exe" ] || { echo "steamcmd.sh did not appear after unpacking." >&2; exit 1; }
    echo "  installed: $steamcmd_exe"
  fi
fi

# --- the fetches --------------------------------------------------------------------------
for key in "${jobs[@]}"; do
  leaf="${LEAF_OF[$key]}"; app="${APP_OF[$key]}"; beta="${BETA_OF[$key]}"; anon="${ANON_OF[$key]}"
  dir="$shadow_root_norm/$leaf"
  w="${key%%:*}"; br="${key##*:}"

  # ORDER IS THE WHOLE POINT: force_install_dir, then login, then app_update.
  sc_args=(+force_install_dir "$dir" +login)
  if [ "$anon" = 1 ]; then sc_args+=(anonymous); else sc_args+=("$ACCOUNT"); fi
  sc_args+=(+app_update "$app")
  [ -n "$beta" ] && sc_args+=(-beta "$beta")
  sc_args+=(validate +quit)

  echo
  echo "=== $w / $br  ->  $dir"
  echo "  $steamcmd_exe ${sc_args[*]}"

  if [ "$WHATIF" = 1 ]; then echo "  --whatif: not run."; continue; fi

  # A branch that is not on the app today cannot be fetched, and steamcmd's error does not say
  # so. Ask before spending the download. 'public' is always there and is not checked.
  if [ -n "$beta" ]; then
    mapfile -t branches < <(get_steam_branches "$steamcmd_exe" "$app")
    found=0
    for br_name in "${branches[@]}"; do [ "$br_name" = "$beta" ] && found=1; done
    if [ "$found" = 0 ]; then
      echo "  SKIPPED: app $app does not advertise a '$beta' branch today."
      echo "  It advertises: $(IFS=', '; echo "${branches[*]}")"
      echo "  (A playtest branch exists only while a playtest is running. Nothing is wrong here.)"
      continue
    fi
  fi

  if [ "$anon" = 0 ]; then
    echo "  steamcmd will now ask for the password for '$ACCOUNT' (and Steam Guard)."
    echo "  Type it into steamcmd. This script neither reads nor stores it."
  fi

  mkdir -p "$dir"
  t0=$(date +%s)
  set +e
  "$steamcmd_exe" "${sc_args[@]}"
  code=$?
  set -e
  elapsed=$(( $(date +%s) - t0 ))
  printf "  steamcmd exit %s after %02d:%02d\n" "$code" $((elapsed/60)) $((elapsed%60))
  [ "$code" -ne 0 ] && echo "  FETCH FAILED - read the output above before trusting anything in $dir."

  write_build_facts "$w / $br" "$dir" "$app"
done

echo
echo "Baseline, for comparison (the installed builds, never written to):"
write_build_facts "baseline client" "${HOME}/.steam/steam/steamapps/common/Valheim" "$APP_CLIENT"
write_build_facts "baseline server" "${HOME}/.steam/steam/steamapps/common/Valheim dedicated server" "$APP_SERVER"
