# Decompiles the three Valheim assemblies of one or more builds into project trees, one file
# per type, so two builds can be compared with a directory diff. P10a, docs/P10-P11-FOR-DON.md.
#
#   .\tools\decompile-builds.ps1 -Baseline            # the two INSTALLED builds (read-only)
#   .\tools\decompile-builds.ps1 -All                  # every shadow build under the shadow root
#   .\tools\decompile-builds.ps1 -Build server-live
#   .\tools\decompile-builds.ps1 -Baseline -All -Force # ignore the stamps, decompile everything again
#
# ASCII ONLY IN THIS FILE, deliberately - see tools\package.ps1 for why.
#
# OUTPUT LIVES OUTSIDE THE REPO: <ShadowRoot>\src\<build>\<assembly>\. Decompiled Valheim is
# a hundred thousand lines of somebody else's code and none of it is ever committed.
#
# The three assemblies are all of them: assembly_valheim (the game), assembly_utils (ZDO,
# ZPackage, the networking primitives) and assembly_guiutils - the last a real dependency
# since P7 (Localization). The others in Managed\ (googleanalytics, lux, postprocessing,
# simplemeshcombine, sunshafts) are named nowhere in this mod.
#
# IDEMPOTENT via a stamp file per (build, assembly) carrying the source DLL's size and write
# time. ilspycmd on assembly_valheim takes minutes; a re-run that has nothing to do says so
# and costs nothing. -Force decompiles anyway.

[CmdletBinding()]
param(
    [string] $Build = "",
    [switch] $All,
    [switch] $Baseline,
    [string] $ShadowRoot = "C:\Users\donfr\valheim-shadows",
    [switch] $Force
)

$ErrorActionPreference = "Stop"

$ASSEMBLIES = @("assembly_valheim", "assembly_utils", "assembly_guiutils")

# The two installed builds. NEVER WRITTEN TO - they are only ever read from here, and the
# output goes to the shadow root like everything else.
$BASELINES = @{
    "baseline-client" = "C:\Program Files (x86)\Steam\steamapps\common\Valheim"
    "baseline-server" = "C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server"
}

# ilspycmd is a .NET tool: a DOTNET_ROOT left over from another machine's layout stops it
# starting with "You must install .NET to run this application" - which reads like a missing
# runtime and is not one. Resolve it from the dotnet actually on PATH.
function Resolve-DotnetRoot {
    $cur = $env:DOTNET_ROOT
    if ($cur -and (Test-Path (Join-Path $cur "host\fxr"))) { return }
    $dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)
    if ($dotnet) { $env:DOTNET_ROOT = Split-Path $dotnet.Source -Parent } else { $env:DOTNET_ROOT = $null }
}

function Get-Ilspy {
    $p = "$env:USERPROFILE\.dotnet\tools\ilspycmd.exe"
    if (Test-Path $p) { return $p }
    $c = Get-Command ilspycmd -ErrorAction SilentlyContinue
    if ($c) { return $c.Source }
    return $null
}

function Get-ManagedDir([string] $installDir) {
    foreach ($d in @("valheim_Data\Managed", "valheim_server_Data\Managed")) {
        $p = Join-Path $installDir $d
        if (Test-Path (Join-Path $p "assembly_valheim.dll")) { return $p }
    }
    return $null
}

# --- work out which builds were asked for --------------------------------------------
$targets = [ordered]@{}

if ($Baseline) {
    foreach ($k in $BASELINES.Keys) { $targets[$k] = $BASELINES[$k] }
}
if ($All) {
    if (Test-Path $ShadowRoot) {
        foreach ($d in (Get-ChildItem $ShadowRoot -Directory | Sort-Object Name)) {
            if ($d.Name -eq "steamcmd" -or $d.Name -eq "src") { continue }
            if (Get-ManagedDir $d.FullName) { $targets[$d.Name] = $d.FullName }
        }
    }
}
if ($Build) {
    $dir = if (Test-Path $Build) { (Resolve-Path $Build).Path } else { Join-Path $ShadowRoot $Build }
    $name = Split-Path $dir -Leaf
    if ($BASELINES.ContainsKey($Build)) { $dir = $BASELINES[$Build]; $name = $Build }
    $targets[$name] = $dir
}

if ($targets.Count -eq 0) {
    Write-Host "Nothing to do. Pass -Baseline, -All, or -Build <name>." -ForegroundColor Yellow
    Write-Host "Shadow root: $ShadowRoot"
    if (Test-Path $ShadowRoot) { Get-ChildItem $ShadowRoot -Directory | ForEach-Object { Write-Host "  $($_.Name)" } }
    exit 1
}

$ilspy = Get-Ilspy
if (-not $ilspy) {
    Write-Host "ilspycmd not found. Install it with: dotnet tool install -g ilspycmd" -ForegroundColor Red
    exit 1
}
Resolve-DotnetRoot
Write-Host "ilspycmd     : $ilspy"
Write-Host "DOTNET_ROOT  : $env:DOTNET_ROOT"
Write-Host "output root  : $ShadowRoot\src"
Write-Host ""

$srcRoot = Join-Path $ShadowRoot "src"
New-Item -ItemType Directory -Force -Path $srcRoot | Out-Null

$grand = Get-Date
$failures = @()

foreach ($name in $targets.Keys) {
    $installDir = $targets[$name]
    Write-Host "=== $name" -ForegroundColor Cyan
    if (-not (Test-Path $installDir)) {
        Write-Host "  no such directory: $installDir" -ForegroundColor Yellow
        $failures += "$name (no directory)"
        continue
    }
    $managed = Get-ManagedDir $installDir
    if (-not $managed) {
        Write-Host "  no *_Data\Managed\assembly_valheim.dll under $installDir" -ForegroundColor Yellow
        $failures += "$name (no managed folder)"
        continue
    }
    Write-Host "  managed: $managed"

    foreach ($asm in $ASSEMBLIES) {
        $dll = Join-Path $managed "$asm.dll"
        if (-not (Test-Path $dll)) {
            Write-Host "  $asm : NOT PRESENT in this build" -ForegroundColor Yellow
            continue
        }
        $src = Get-Item $dll
        $out = Join-Path $srcRoot "$name\$asm"
        $stampPath = Join-Path $srcRoot "$name\$asm.stamp"
        $stamp = "$($src.Length) $($src.LastWriteTimeUtc.Ticks)"

        if (-not $Force -and (Test-Path $stampPath) -and (Get-Content $stampPath -Raw).Trim() -eq $stamp -and (Test-Path $out)) {
            $n = (Get-ChildItem $out -Recurse -Filter *.cs -File).Count
            Write-Host ("  {0,-18} up to date ({1:N0} .cs)" -f $asm, $n)
            continue
        }

        if (Test-Path $out) { Remove-Item $out -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $out | Out-Null

        $t0 = Get-Date
        # -p writes a project tree, one file per type: that is what makes a directory diff
        # between two builds readable at all. -r points the decompiler at the sibling
        # assemblies so cross-assembly types resolve to names instead of tokens.
        & $ilspy -p -r $managed $dll -o $out *> (Join-Path $srcRoot "$name\$asm.ilspy.log")
        $code = $LASTEXITCODE
        $elapsed = (Get-Date) - $t0
        $n = (Get-ChildItem $out -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue).Count

        if ($code -ne 0 -or $n -eq 0) {
            Write-Host ("  {0,-18} FAILED (exit {1}, {2:N0} .cs) - see {3}\{4}\{5}.ilspy.log" -f $asm, $code, $n, $srcRoot, $name, $asm) -ForegroundColor Red
            $failures += "$name/$asm"
            continue
        }
        Set-Content -Path $stampPath -Value $stamp -Encoding ASCII
        Write-Host ("  {0,-18} {1:N0} .cs in {2:mm\:ss}" -f $asm, $n, $elapsed) -ForegroundColor Green
    }
}

Write-Host ""
Write-Host ("Done in {0:hh\:mm\:ss}." -f ((Get-Date) - $grand))
if ($failures.Count -gt 0) {
    Write-Host "FAILED: $($failures -join ', ')" -ForegroundColor Red
    exit 1
}
