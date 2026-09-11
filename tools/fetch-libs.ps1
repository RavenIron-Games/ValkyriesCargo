# Populates .\libs from a local Valheim install.
# Run once per machine, from the solution root:  .\tools\fetch-libs.ps1
# Auto-detects Steam; pass -ValheimPath to override.
#
# libs\ is gitignored on purpose - game assemblies are not ours to redistribute.

param(
    [string]$ValheimPath,
    # Where the *publicized* assemblies live, if not one of the known locations below.
    [string]$PublicizedPath,
    # Where BepInEx\core lives, if not one of the known locations below.
    [string]$BepInExCorePath
)

$ErrorActionPreference = 'Stop'

function Find-Valheim {
    $candidates = @()

    # Default Steam locations
    $candidates += "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
    $candidates += "C:\Program Files\Steam\steamapps\common\Valheim"

    # Extra Steam libraries declared in libraryfolders.vdf
    $vdf = "C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
        Select-String -Path $vdf -Pattern '"path"\s+"(.+?)"' -AllMatches |
            ForEach-Object { $_.Matches } |
            ForEach-Object {
                $p = $_.Groups[1].Value -replace '\\\\', '\'
                $candidates += Join-Path $p "steamapps\common\Valheim"
            }
    }

    foreach ($c in $candidates) {
        if (Test-Path (Join-Path $c "valheim_Data\Managed")) { return $c }
    }
    return $null
}

if (-not $ValheimPath) { $ValheimPath = Find-Valheim }

if (-not $ValheimPath -or -not (Test-Path $ValheimPath)) {
    Write-Host "Could not locate Valheim automatically." -ForegroundColor Red
    Write-Host "Re-run with:  .\tools\fetch-libs.ps1 -ValheimPath 'D:\Games\Valheim'"
    exit 1
}

Write-Host "Valheim: $ValheimPath" -ForegroundColor Cyan

$managed    = Join-Path $ValheimPath "valheim_Data\Managed"
$bepinex    = Join-Path $ValheimPath "BepInEx\core"
$libs       = Join-Path $PSScriptRoot "..\libs"

New-Item -ItemType Directory -Force -Path $libs | Out-Null
$libs = (Resolve-Path $libs).Path

# The publicized assemblies are NOT necessarily under the game folder, and the copies there are
# not stale or current as a UNIT.
#
# Learned 2026-09-10, the day Valheim shipped 1.0.7: a game update does NOT regenerate the
# in-game publicized_assemblies folder, so reading only from there - which this script used to do -
# rebuilds against the OLD API. Clean compile, MissingMethodException in-game. The owner
# publicizes into a working folder on the Desktop instead.
#
# Then on 2026-09-11 the sibling repos' version of this script, which DID consider both folders,
# shipped a stale file anyway: it chose a FOLDER by the date of assembly_valheim_publicized.dll,
# the in-game one was a minute newer, and it dragged along a July assembly_utils_publicized.dll.
# So the unit of choice is a FILE. Take the newest copy of each name across every candidate, and
# refuse per file when it predates the game assembly it was made from.
$publicizedCandidates = @()
if ($PublicizedPath) { $publicizedCandidates += $PublicizedPath }
$publicizedCandidates += (Join-Path $env:USERPROFILE "Desktop\ValheimModding\publicized_assemblies")
$publicizedCandidates += (Join-Path $managed "publicized_assemblies")

# Note what is NOT here: assembly_guiutils. This mod takes the STOCK one from $managed instead,
# because the publicized copy lacks the Localization types - see the $sets block below.
$publicizedNames = @(
    "assembly_valheim_publicized.dll",
    "assembly_utils_publicized.dll",
    "assembly_postprocessing_publicized.dll",
    "assembly_lux_publicized.dll",
    "assembly_sunshafts_publicized.dll"
)

$publicizedResolved = [ordered]@{}
foreach ($name in $publicizedNames) {
    foreach ($candidate in $publicizedCandidates) {
        $probe = Join-Path $candidate $name
        if (-not (Test-Path $probe)) { continue }
        $stamp = (Get-Item $probe).LastWriteTime
        if (-not $publicizedResolved[$name] -or $stamp -gt $publicizedResolved[$name].Stamp) {
            $publicizedResolved[$name] = @{ Path = $probe; Stamp = $stamp }
        }
    }
}

if ($publicizedResolved.Count -eq 0) {
    Write-Host "No publicized assemblies found. Looked in:" -ForegroundColor Red
    $publicizedCandidates | ForEach-Object { Write-Host "  $_" }
    Write-Host "Generate them first (BepInEx.AssemblyPublicizer.MSBuild or a publicizer tool),"
    Write-Host "or point at them:  .\tools\fetch-libs.ps1 -PublicizedPath 'C:\path\to\publicized_assemblies'"
    exit 1
}

$stale = @()
foreach ($name in $publicizedResolved.Keys) {
    $gameName = $name -replace '_publicized\.dll$', '.dll'
    $gameAssembly = Join-Path $managed $gameName
    if (-not (Test-Path $gameAssembly)) { continue }
    $gameStamp = (Get-Item $gameAssembly).LastWriteTime
    if ($publicizedResolved[$name].Stamp -lt $gameStamp) {
        $stale += [pscustomobject]@{
            Name = $name; Publicized = $publicizedResolved[$name].Stamp; Game = $gameStamp
            From = Split-Path $publicizedResolved[$name].Path -Parent
        }
    }
}

if ($stale.Count -gt 0) {
    Write-Host "STALE publicized assemblies - refusing to copy." -ForegroundColor Red
    foreach ($s in $stale) {
        Write-Host ("  {0}" -f $s.Name)
        Write-Host ("      publicized {0}   game {1}" -f $s.Publicized.ToString('yyyy-MM-dd HH:mm'), $s.Game.ToString('yyyy-MM-dd HH:mm'))
        Write-Host ("      newest copy found in {0}" -f $s.From)
    }
    Write-Host ""
    Write-Host "Valheim has been updated since these were generated. Re-publicize the ones named"
    Write-Host "above, then run this again. Building against the old ones gives a clean compile"
    Write-Host "and a mod that throws MissingMethodException in-game."
    exit 1
}

Write-Host "Publicized:" -ForegroundColor Cyan
foreach ($name in $publicizedResolved.Keys) {
    Write-Host ("  {0,-42} {1}  {2}" -f $name,
        $publicizedResolved[$name].Stamp.ToString('yyyy-MM-dd HH:mm'),
        (Split-Path $publicizedResolved[$name].Path -Parent))
}

# BepInEx is not necessarily in the game folder either: this machine runs its client through
# Gale, which keeps a full BepInEx per profile and leaves the Steam install vanilla. Only
# BepInEx.dll and 0Harmony.dll are wanted, and they are identical across profiles.
if (-not (Test-Path $bepinex)) {
    $coreCandidates = @()
    if ($BepInExCorePath) { $coreCandidates += $BepInExCorePath }
    $galeProfiles = Join-Path $env:APPDATA "com.kesomannen.gale\valheim\profiles"
    if (Test-Path $galeProfiles) {
        $coreCandidates += (Get-ChildItem $galeProfiles -Directory |
            ForEach-Object { Join-Path $_.FullName "BepInEx\core" })
    }

    $bestCore = $null
    $bestCoreStamp = [datetime]::MinValue
    foreach ($candidate in $coreCandidates) {
        $probe = Join-Path $candidate "BepInEx.dll"
        if (Test-Path $probe) {
            $stamp = (Get-Item $probe).LastWriteTime
            if ($stamp -gt $bestCoreStamp) { $bestCore = $candidate; $bestCoreStamp = $stamp }
        }
    }
    if ($bestCore) {
        $bepinex = $bestCore
        Write-Host "BepInEx:    $bepinex" -ForegroundColor Cyan
    }
}

if (-not (Test-Path $bepinex)) {
    Write-Host "BepInEx\core not found. Looked in the game folder and every Gale profile." -ForegroundColor Red
    Write-Host "Install BepInExPack Valheim, or point at it:"
    Write-Host "  .\tools\fetch-libs.ps1 -BepInExCorePath 'C:\path\to\BepInEx\core'"
    exit 1
}

# source folder : file names. The publicized set is NOT here - it is copied from its per-file
# resolved paths above, because those files do not all come from the same folder.
$sets = @(
    @{ Path = $managed; Files = @(
        "assembly_guiutils.dll",   # STOCK, not publicized: the publicized copy lacks the Localization types; the terminal uses only public members
        "UnityEngine.dll",
        "UnityEngine.CoreModule.dll",
        "UnityEngine.PhysicsModule.dll",
        "UnityEngine.ParticleSystemModule.dll",
        "UnityEngine.AudioModule.dll",
        "UnityEngine.ImageConversionModule.dll",
        "UnityEngine.IMGUIModule.dll",
        "UnityEngine.TextRenderingModule.dll",
        "UnityEngine.InputLegacyModule.dll",
        "UnityEngine.UI.dll",
        "Unity.TextMeshPro.dll",
        "UnityEngine.UIModule.dll",
        "UnityEngine.AnimationModule.dll",
        "UnityEngine.AssetBundleModule.dll",   # AssetBundle: Ingvar's body, embedded in the DLL (Client\BodyLoader.cs)
        "UnityEngine.DirectorModule.dll"       # not referenced today; UnityEngine.Playables.PlayableGraph turned out to live in CoreModule
    )},
    @{ Path = $bepinex; Files = @(
        "BepInEx.dll",
        "0Harmony.dll"
    )}
    # No third-party DLLs: the BarrkBOT export writes JSON through the pure Core\Json.cs (owner, 2026-09-07).
)

$copied = 0
$missing = @()

# The publicized set is copied from its per-file resolved paths, not from one folder.
foreach ($name in $publicizedResolved.Keys) {
    Copy-Item $publicizedResolved[$name].Path -Destination (Join-Path $libs $name) -Force
    Write-Host "  + $name"
    $copied++
}
foreach ($name in $publicizedNames) {
    if (-not $publicizedResolved[$name]) { $missing += $name }
}

foreach ($set in $sets) {
    foreach ($f in $set.Files) {
        $src = Join-Path $set.Path $f
        if (Test-Path $src) {
            Copy-Item $src -Destination $libs -Force
            Write-Host "  + $f"
            $copied++
        } else {
            $missing += $f
        }
    }
}

Write-Host ""
Write-Host "Copied $copied file(s) to $libs" -ForegroundColor Green

if ($missing.Count -gt 0) {
    Write-Host ""
    Write-Host "Not found (build will fail if any are actually referenced):" -ForegroundColor Yellow
    $missing | ForEach-Object { Write-Host "  - $_" }
}
