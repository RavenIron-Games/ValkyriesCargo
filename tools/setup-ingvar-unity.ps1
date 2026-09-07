# Scaffold the Unity project that bakes Ingvar into an AssetBundle, then bake it.
#
#   .\tools\setup-ingvar-unity.ps1                 # scaffold only, print the build command
#   .\tools\setup-ingvar-unity.ps1 -Build          # scaffold, then run Unity and bake
#   .\tools\setup-ingvar-unity.ps1 -Embed          # after a successful bake, wire it into the csproj
#
# The Unity project is created as a SIBLING of the repo, never inside it: an SDK-style csproj
# globs every .cs beneath it and will sweep Unity's Library\PackageCache into the mod DLL,
# which produces hundreds of CS0246/CS1069 and is nobody's idea of a good afternoon.
#
# Needs Unity 6000.0.61f1. Everything else it writes itself.

[CmdletBinding()]
param(
    [switch]$Build,
    [switch]$Embed,
    [string]$UnityProject = "",
    [string]$EditorVersion = "6000.0.61f1"
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $UnityProject) { $UnityProject = Join-Path (Split-Path $repo -Parent) "ValkyriesCargo-Unity" }

$BundleName = "valkyriescargo_kit"

function Say($m)  { Write-Host "  $m" }
function Ok($m)   { Write-Host "  $m" -ForegroundColor Green }
function Warn($m) { Write-Host "  $m" -ForegroundColor Yellow }
function Die($m)  { Write-Host "  $m" -ForegroundColor Red; exit 1 }

Write-Host "`nIngvar bundle setup" -ForegroundColor Cyan
Say "repo   : $repo"
Say "unity  : $UnityProject"

# ---- the source art ---------------------------------------------------------------------
$fbx    = Join-Path $repo "models\ingvar.fbx"
$albedo = Join-Path $repo "models\ingvar_albedo.png"
foreach ($f in @($fbx, $albedo)) {
    if (-not (Test-Path $f)) { Die "missing $f - is the repo checked out fully?" }
}
Ok "source art found"

# ---- scaffold ---------------------------------------------------------------------------
$assets = Join-Path $UnityProject "Assets"
$editor = Join-Path $assets "Editor"
$psets  = Join-Path $UnityProject "ProjectSettings"
$pkgs   = Join-Path $UnityProject "Packages"
foreach ($d in @($assets, $editor, $psets, $pkgs)) { New-Item -ItemType Directory -Force -Path $d | Out-Null }

# ProjectVersion.txt is what makes Unity open it as 6000.0.61f1 rather than upgrading it.
$pv = Join-Path $psets "ProjectVersion.txt"
if (-not (Test-Path $pv)) {
    "m_EditorVersion: $EditorVersion" | Set-Content -Path $pv -Encoding UTF8
    Ok "wrote ProjectVersion.txt ($EditorVersion)"
} else {
    $have = (Get-Content $pv | Select-String 'm_EditorVersion:').ToString().Split(' ')[1]
    if ($have -ne $EditorVersion) { Warn "project is $have, expected $EditorVersion" } else { Ok "editor version $have" }
}

# gltfast is not needed for the FBX path, but it is what pulls in com.unity.collections, and
# leaving it out is the simplest way to avoid landmine 2 entirely. Only the defaults go here.
$manifest = Join-Path $pkgs "manifest.json"
if (-not (Test-Path $manifest)) {
    @'
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
'@ | Set-Content -Path $manifest -Encoding UTF8
    Ok "wrote Packages/manifest.json (no gltfast - the FBX path does not need it, and its"
    Say "   transitive com.unity.collections is what causes the silent 0-byte bundle)"
}

Copy-Item $fbx    (Join-Path $assets "ingvar.fbx")        -Force
Copy-Item $albedo (Join-Path $assets "ingvar_albedo.png") -Force
Copy-Item (Join-Path $repo "tools\unity\IngvarBundleBuilder.cs") (Join-Path $editor "IngvarBundleBuilder.cs") -Force
Ok "copied ingvar.fbx, ingvar_albedo.png and the builder into the project"

# ---- bake -------------------------------------------------------------------------------
$bundle = Join-Path $UnityProject "AssetBundles\$BundleName"

if ($Build) {
    $unity = Get-Command unity -ErrorAction SilentlyContinue
    if (-not $unity) { Die "the 'unity' CLI is not on PATH. Install it, or open the project and use ValkyriesCargo > Build Ingvar Bundle." }

    Write-Host "`nBaking (this takes a few minutes on a first run - it imports the FBX)..." -ForegroundColor Cyan
    Warn "NOT passing -nographics on purpose: texture work goes through Blit+ReadPixels, which"
    Warn "silently produces EMPTY textures headless. The build would 'succeed' and ship a blank model."

    $log = Join-Path $UnityProject "build.log"
    & unity run "$UnityProject" --editor-version $EditorVersion --timeout 3600 --non-interactive --no-banner `
        -- -executeMethod IngvarBundleBuilder.BuildKit -logFile "$log"

    if (Test-Path $log) {
        Get-Content $log | Select-String -Pattern '\[ValkyriesCargo\]|error CS|Exception' | ForEach-Object { Say $_.ToString() }
    }

    if (-not (Test-Path $bundle)) { Die "no bundle at $bundle - read $log" }
    $kb = [int]((Get-Item $bundle).Length / 1KB)
    if ($kb -lt 64) { Die "bundle is only $kb KB - that is the empty-texture or 0-byte failure, not a build." }
    Ok "bundle baked: $kb KB"
} else {
    Write-Host "`nScaffold done. To bake:" -ForegroundColor Cyan
    Say ".\tools\setup-ingvar-unity.ps1 -Build"
    Say ""
    Say "or open $UnityProject in Unity $EditorVersion and use ValkyriesCargo > Build Ingvar Bundle."
}

# ---- embed ------------------------------------------------------------------------------
if ($Embed) {
    if (-not (Test-Path $bundle)) { Die "no bundle to embed - run with -Build first." }
    $dest = Join-Path $repo "Assets\$BundleName"
    New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
    Copy-Item $bundle $dest -Force
    Ok "copied the bundle to Assets\$BundleName"

    $csproj = Join-Path $repo "ValkyriesCargo\ValkyriesCargo.csproj"
    $xml = Get-Content $csproj -Raw
    if ($xml -match [regex]::Escape($BundleName)) {
        Say "csproj already references the bundle"
    } else {
        Warn "add these to $csproj by hand (a scripted XML edit on someone else's csproj is rude):"
        Say ""
        Say "  <ItemGroup>"
        Say "    <EmbeddedResource Include=`"..\Assets\$BundleName`" LogicalName=`"ValkyriesCargo.$BundleName`" />"
        Say "    <Reference Include=`"UnityEngine.AssetBundleModule`">"
        Say "      <HintPath>`$(LibsDir)\UnityEngine.AssetBundleModule.dll</HintPath>"
        Say "      <Private>false</Private>"
        Say "    </Reference>"
        Say "  </ItemGroup>"
        Say ""
        Warn "UnityEngine.AssetBundleModule.dll must also be added to tools\fetch-libs.ps1's list."
    }
    Say ""
    Warn "After rebuilding: confirm the DLL grew by roughly the bundle size. A good bake plus a"
    Warn "stale copy ships the OLD asset with no change in DLL size - that is how AwayFromHome"
    Warn "shipped blank textures three times."
}

Write-Host ""
