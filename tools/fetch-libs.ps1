# Populates .\libs from a local Valheim install.
# Run once per machine, from the solution root:  .\tools\fetch-libs.ps1
# Auto-detects Steam; pass -ValheimPath to override.
#
# libs\ is gitignored on purpose - game assemblies are not ours to redistribute.

param(
    [string]$ValheimPath
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
$publicized = Join-Path $managed "publicized_assemblies"
$bepinex    = Join-Path $ValheimPath "BepInEx\core"
$libs       = Join-Path $PSScriptRoot "..\libs"

New-Item -ItemType Directory -Force -Path $libs | Out-Null
$libs = (Resolve-Path $libs).Path

if (-not (Test-Path $publicized)) {
    Write-Host "publicized_assemblies not found at:" -ForegroundColor Red
    Write-Host "  $publicized"
    Write-Host "Generate them first (BepInEx.AssemblyPublicizer.MSBuild or a publicizer tool)."
    exit 1
}

if (-not (Test-Path $bepinex)) {
    Write-Host "BepInEx\core not found at:" -ForegroundColor Red
    Write-Host "  $bepinex"
    Write-Host "Install BepInExPack Valheim into the game folder first."
    exit 1
}

# source folder : file names
$sets = @(
    @{ Path = $publicized; Files = @(
        "assembly_valheim_publicized.dll",
        "assembly_utils_publicized.dll",
        "assembly_postprocessing_publicized.dll",
        "assembly_lux_publicized.dll",
        "assembly_sunshafts_publicized.dll",
        "assembly_guiutils_publicized.dll"
    )},
    @{ Path = $managed; Files = @(
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
        "UnityEngine.AnimationModule.dll"
    )},
    @{ Path = $bepinex; Files = @(
        "BepInEx.dll",
        "0Harmony.dll"
    )}
)

$copied = 0
$missing = @()

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
