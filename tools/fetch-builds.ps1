# Pulls SHADOW COPIES of the Valheim client and dedicated-server builds with steamcmd, into
# throwaway directories that are never a real install. P10a, docs/P10-P11-FOR-DON.md.
#
#   .\tools\fetch-builds.ps1                                  # both server branches, live and public-test
#   .\tools\fetch-builds.ps1 -Branch live                     # live dedicated server only
#   .\tools\fetch-builds.ps1 -Which client -Account <name>    # client; steamcmd PROMPTS for the password
#   .\tools\fetch-builds.ps1 -Which both -Account <name> -WhatIf
#
# ASCII ONLY IN THIS FILE, deliberately - same reason as tools\package.ps1: Windows PowerShell
# 5.1 reads a BOM-less file as ANSI, a UTF-8 em-dash decodes to a sequence ending in a curly
# quote, and 5.1 treats that as a string delimiter. The whole script stops parsing.
#
# THE ARGUMENT-ORDER TRAP. steamcmd applies its + arguments IN ORDER. If +force_install_dir
# comes after +login, or after +app_update, steamcmd silently uses the DEFAULT library - which
# on this machine is the one holding the real Valheim install. That is exactly how a "shadow
# copy" quietly becomes an overwrite of a working game. Order here is always:
#
#     +force_install_dir <dir>   +login <who>   +app_update <id> [-beta <b>] validate   +quit
#
# THE REFUSALS. Everything is checked BEFORE steamcmd is started, not per-app while it runs.
# A target directory must be exactly $ShadowRoot\<known leaf>, and must not sit inside the
# Steam install, any library from libraryfolders.vdf, the Steam client folder, or this repo.
#
# NO PASSWORD EVER TOUCHES THIS SCRIPT. -Account passes only the account NAME. steamcmd asks
# for the password (and Steam Guard) on the console itself, and the answer goes to steamcmd.
# There is deliberately no -Password parameter to add one to, and no credential file is written.

[CmdletBinding()]
param(
    [ValidateSet("server", "client", "both")]
    [string] $Which = "server",

    [ValidateSet("live", "public-test", "both")]
    [string] $Branch = "both",

    [string] $Account = "",

    [string] $ShadowRoot = "C:\Users\donfr\valheim-shadows",

    [switch] $WhatIf
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent

$STEAMCMD_URL = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip"
$APP_CLIENT = 892970
$APP_SERVER = 896660

# --- the only directory names this script will ever write to -------------------------
# One leaf per (app, branch). Anything not on this list is refused, so a typo in a
# parameter cannot invent a path.
$KNOWN_LEAVES = @{
    "server:live"        = @{ Leaf = "server-live";  App = $APP_SERVER; Beta = "";            Anonymous = $true  }
    "server:public-test" = @{ Leaf = "server-test";  App = $APP_SERVER; Beta = "public-test"; Anonymous = $true  }
    "client:live"        = @{ Leaf = "client-live";  App = $APP_CLIENT; Beta = "";            Anonymous = $false }
    "client:public-test" = @{ Leaf = "client-test";  App = $APP_CLIENT; Beta = "public-test"; Anonymous = $false }
}

function Get-NormalPath([string] $p) {
    if ([string]::IsNullOrWhiteSpace($p)) { return "" }
    try { $full = [System.IO.Path]::GetFullPath($p) } catch { return "" }
    return $full.TrimEnd('\')
}

# True when $child is $parent or lives underneath it. Compares whole path SEGMENTS, so
# "C:\valheim-shadows-old" is not treated as inside "C:\valheim-shadows".
function Test-IsInside([string] $child, [string] $parent) {
    $c = Get-NormalPath $child
    $p = Get-NormalPath $parent
    if ($c -eq "" -or $p -eq "") { return $false }
    if ($c -eq $p) { return $true }
    return $c.StartsWith($p + '\', [System.StringComparison]::OrdinalIgnoreCase)
}

# Every place a real install could live: the Steam install itself, every library in
# libraryfolders.vdf, the Steam client folder from the registry, and this repo.
function Get-ForbiddenRoots {
    $roots = New-Object System.Collections.Generic.List[string]
    $roots.Add("C:\Program Files (x86)\Steam")
    $roots.Add($repoRoot)

    foreach ($key in @("HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam")) {
        try {
            $item = Get-ItemProperty -Path $key -ErrorAction Stop
            foreach ($name in @("SteamPath", "InstallPath")) {
                if ($item.PSObject.Properties.Name -contains $name -and $item.$name) { $roots.Add(([string]$item.$name).Replace('/', '\')) }
            }
        } catch { }
    }

    foreach ($vdf in @("C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf", "C:\Program Files (x86)\Steam\config\libraryfolders.vdf")) {
        if (Test-Path $vdf) {
            foreach ($m in (Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' -AllMatches).Matches) {
                $roots.Add($m.Groups[1].Value.Replace('\\', '\'))
            }
        }
    }

    $out = @()
    foreach ($r in $roots) { $n = Get-NormalPath $r; if ($n -ne "" -and $out -notcontains $n) { $out += $n } }
    return $out
}

# ilspycmd is a .NET tool: a DOTNET_ROOT left over from another machine's layout stops it
# starting with "You must install .NET to run this application". Resolve it from the dotnet
# actually on PATH rather than trusting the environment.
function Resolve-DotnetRoot {
    $cur = $env:DOTNET_ROOT
    if ($cur -and (Test-Path (Join-Path $cur "host\fxr"))) { return }
    $dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)
    if ($dotnet) { $env:DOTNET_ROOT = Split-Path $dotnet.Source -Parent } else { $env:DOTNET_ROOT = $null }
}

function Get-ManagedDir([string] $installDir) {
    foreach ($d in @("valheim_Data\Managed", "valheim_server_Data\Managed")) {
        $p = Join-Path $installDir $d
        if (Test-Path (Join-Path $p "assembly_valheim.dll")) { return $p }
    }
    return $null
}

# The four numbers that are the real identity of a build. The Steam build id alone is not
# enough, because a branch can be re-pushed. Read from the assembly by decompiling the
# (internal) Version type - the member names are m_networkVersion / m_playerVersion /
# m_worldVersion and the CurrentVersion property.
function Get-ValheimVersions([string] $managedDir) {
    $ilspy = "$env:USERPROFILE\.dotnet\tools\ilspycmd.exe"
    if (-not (Test-Path $ilspy)) { $c = Get-Command ilspycmd -ErrorAction SilentlyContinue; if ($c) { $ilspy = $c.Source } }
    if (-not (Test-Path $ilspy)) { return $null }
    Resolve-DotnetRoot
    $dll = Join-Path $managedDir "assembly_valheim.dll"
    $text = (& $ilspy -r $managedDir $dll -t Version 2>$null) -join "`n"
    if (-not $text) { return $null }
    $v = [ordered]@{ Game = "?"; Network = "?"; Player = "?"; World = "?" }
    $m = [regex]::Match($text, 'CurrentVersion\s*\{[^}]*\}\s*=\s*new GameVersion\((\d+),\s*(\d+),\s*(\d+)\)')
    if ($m.Success) { $v.Game = "$($m.Groups[1].Value).$($m.Groups[2].Value).$($m.Groups[3].Value)" }
    foreach ($pair in @(@("Network", "m_networkVersion"), @("Player", "m_playerVersion"), @("World", "m_worldVersion"))) {
        $mm = [regex]::Match($text, [regex]::Escape($pair[1]) + '\s*=\s*(\d+)')
        if ($mm.Success) { $v[$pair[0]] = $mm.Groups[1].Value }
    }
    return $v
}

# What branches this app actually advertises today. steamcmd's answer to a branch that does
# not exist is "ERROR! Failed to set beta '<name>'" and nothing else - it does not say which
# names would have worked, and it exits 8 whether the branch is gone, renamed, or needs a
# password. Asking first turns that into a sentence. Costs one anonymous app_info round trip.
function Get-SteamBranches([string] $steamcmdExe, [int] $appId) {
    $out = & $steamcmdExe +login anonymous +app_info_update 1 +app_info_print "$appId" +quit 2>&1
    $lines = @($out | ForEach-Object { "$_" })
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match '^\s*"branches"\s*$') { $start = $i; break } }
    if ($start -lt 0) { return $null }
    $names = @(); $depth = 0
    for ($j = $start; $j -lt $lines.Count; $j++) {
        $l = $lines[$j]
        if ($l -match '\{') { $depth++ }
        if ($l -match '\}') { $depth--; if ($depth -le 0) { break } }
        if ($depth -eq 1 -and $l -match '^\s*"([^"]+)"\s*$') { $names += $Matches[1] }
    }
    return $names
}

function Write-BuildFacts([string] $label, [string] $installDir, [int] $appId) {
    Write-Host ""
    Write-Host "$label -> $installDir" -ForegroundColor Cyan
    # A shadow install keeps its manifest under <dir>\steamapps; a real Steam install keeps
    # it two levels up, beside the "common" folder. Look in both.
    $acf = Join-Path $installDir "steamapps\appmanifest_$appId.acf"
    if (-not (Test-Path $acf)) {
        $sibling = Join-Path (Split-Path (Split-Path $installDir -Parent) -Parent) "appmanifest_$appId.acf"
        if (Test-Path $sibling) { $acf = $sibling }
    }
    if (Test-Path $acf) {
        $b = (Select-String -Path $acf -Pattern '"buildid"\s+"(\d+)"').Matches[0].Groups[1].Value
        Write-Host "  steam buildid : $b"
    } else {
        Write-Host "  steam buildid : NO $acf - the fetch did not complete" -ForegroundColor Yellow
    }
    $managed = Get-ManagedDir $installDir
    if (-not $managed) {
        Write-Host "  assembly      : no *_Data\Managed\assembly_valheim.dll under this directory" -ForegroundColor Yellow
        return
    }
    $f = Get-Item (Join-Path $managed "assembly_valheim.dll")
    Write-Host ("  assembly      : {0:N0} bytes, {1}" -f $f.Length, $f.LastWriteTime)
    $v = Get-ValheimVersions $managed
    if ($v) {
        Write-Host "  game version  : $($v.Game)"
        Write-Host "  network / player / world : $($v.Network) / $($v.Player) / $($v.World)"
    } else {
        Write-Host "  versions      : ilspycmd not available; read them with decompile-builds.ps1" -ForegroundColor Yellow
    }
}

# --- work out what was asked, before anything is downloaded --------------------------
$whichList = if ($Which -eq "both") { @("server", "client") } else { @($Which) }
$branchList = if ($Branch -eq "both") { @("live", "public-test") } else { @($Branch) }

$jobs = @()
foreach ($w in $whichList) {
    foreach ($b in $branchList) {
        $key = "${w}:${b}"
        if (-not $KNOWN_LEAVES.ContainsKey($key)) { Write-Host "No known leaf for $key - refusing." -ForegroundColor Red; exit 1 }
        $spec = $KNOWN_LEAVES[$key]
        $jobs += [pscustomobject]@{
            Which = $w; Branch = $b; Leaf = $spec.Leaf; App = $spec.App
            Beta = $spec.Beta; Anonymous = $spec.Anonymous
            Dir = (Join-Path $ShadowRoot $spec.Leaf)
        }
    }
}

if (($jobs | Where-Object { -not $_.Anonymous }) -and -not $Account) {
    Write-Host "The client (app $APP_CLIENT) needs an account that owns Valheim." -ForegroundColor Red
    Write-Host "Pass -Account <name>. steamcmd will ask for the password itself; this script never sees it." -ForegroundColor Yellow
    exit 1
}
if ($Account -and -not ($jobs | Where-Object { -not $_.Anonymous })) {
    Write-Host "-Account is ignored: the dedicated server (app $APP_SERVER) takes +login anonymous." -ForegroundColor Yellow
}

# --- HARD REFUSALS, all of them, before a single byte moves --------------------------
$shadowRootNorm = Get-NormalPath $ShadowRoot
$forbidden = Get-ForbiddenRoots

Write-Host "Shadow root : $shadowRootNorm"
Write-Host "Refused     : $($forbidden -join '; ')"

$fatal = @()
if ($shadowRootNorm -eq "") { $fatal += "the shadow root is not a usable path: '$ShadowRoot'" }
foreach ($f in $forbidden) {
    if (Test-IsInside $shadowRootNorm $f) { $fatal += "the shadow root is inside a real install or the repo: '$shadowRootNorm' is under '$f'" }
    if (Test-IsInside $f $shadowRootNorm) { $fatal += "a real install or the repo is inside the shadow root: '$f' is under '$shadowRootNorm'" }
}
foreach ($j in $jobs) {
    $d = Get-NormalPath $j.Dir
    # Exactly the shadow root plus ONE known leaf. Not a grandchild, not a sibling.
    if ((Split-Path $d -Parent).TrimEnd('\') -ne $shadowRootNorm) { $fatal += "$($j.Leaf): '$d' is not directly under the shadow root" }
    if ((Split-Path $d -Leaf) -ne $j.Leaf) { $fatal += "$($j.Leaf): leaf name does not match" }
    foreach ($f in $forbidden) {
        if (Test-IsInside $d $f) { $fatal += "$($j.Leaf): target '$d' is inside '$f'" }
    }
    # A directory that already holds a Steam manifest for a DIFFERENT app is somebody
    # else's install that happens to sit here. Refuse rather than validate over it.
    if (Test-Path (Join-Path $d "steamapps")) {
        foreach ($acf in (Get-ChildItem (Join-Path $d "steamapps") -Filter "appmanifest_*.acf" -ErrorAction SilentlyContinue)) {
            if ($acf.Name -ne "appmanifest_$($j.App).acf") { $fatal += "$($j.Leaf): '$d' already holds $($acf.Name) - that is another app's install" }
        }
    }
}
if ($fatal.Count -gt 0) {
    Write-Host ""
    Write-Host "REFUSING TO RUN:" -ForegroundColor Red
    foreach ($f in ($fatal | Select-Object -Unique)) { Write-Host "  $f" -ForegroundColor Red }
    exit 1
}
Write-Host "Refusal checks passed for: $(($jobs | ForEach-Object { $_.Leaf }) -join ', ')" -ForegroundColor Green

# --- steamcmd, installed into the shadow root ----------------------------------------
$steamcmdDir = Join-Path $ShadowRoot "steamcmd"
$steamcmdExe = Join-Path $steamcmdDir "steamcmd.exe"

if (-not (Test-Path $steamcmdExe)) {
    Write-Host ""
    Write-Host "steamcmd is not installed at $steamcmdExe" -ForegroundColor Yellow
    Write-Host "  source: $STEAMCMD_URL (Valve's official installer, about 3 MB)"
    if ($WhatIf) {
        Write-Host "  -WhatIf: not downloading."
    } else {
        New-Item -ItemType Directory -Force -Path $steamcmdDir | Out-Null
        $zip = Join-Path $steamcmdDir "steamcmd.zip"
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $STEAMCMD_URL -OutFile $zip -UseBasicParsing
        Write-Host ("  downloaded {0:N0} bytes" -f (Get-Item $zip).Length)
        Expand-Archive -Path $zip -DestinationPath $steamcmdDir -Force
        Remove-Item $zip -Force
        if (-not (Test-Path $steamcmdExe)) { Write-Host "steamcmd.exe did not appear after unzipping." -ForegroundColor Red; exit 1 }
        Write-Host "  installed: $steamcmdExe" -ForegroundColor Green
    }
}

# --- the fetches ----------------------------------------------------------------------
foreach ($j in $jobs) {
    $login = if ($j.Anonymous) { @("anonymous") } else { @($Account) }

    # ORDER IS THE WHOLE POINT: force_install_dir, then login, then app_update.
    $scArgs = @("+force_install_dir", $j.Dir, "+login") + $login + @("+app_update", "$($j.App)")
    if ($j.Beta) { $scArgs += @("-beta", $j.Beta) }
    $scArgs += @("validate", "+quit")

    Write-Host ""
    Write-Host "=== $($j.Which) / $($j.Branch)  ->  $($j.Dir)" -ForegroundColor Cyan
    Write-Host "  $steamcmdExe $($scArgs -join ' ')"

    if ($WhatIf) { Write-Host "  -WhatIf: not run." -ForegroundColor Yellow; continue }

    # A branch that is not on the app today cannot be fetched, and steamcmd's error does not
    # say so. Ask before spending the download. `public` is always there and is not checked.
    if ($j.Beta) {
        $branches = Get-SteamBranches $steamcmdExe $j.App
        if ($branches -and ($branches -notcontains $j.Beta)) {
            Write-Host "  SKIPPED: app $($j.App) does not advertise a '$($j.Beta)' branch today." -ForegroundColor Yellow
            Write-Host "  It advertises: $($branches -join ', ')" -ForegroundColor Yellow
            Write-Host "  (A playtest branch exists only while a playtest is running. Nothing is wrong here.)" -ForegroundColor Yellow
            continue
        }
    }

    if (-not $j.Anonymous) {
        Write-Host "  steamcmd will now ask for the password for '$Account' (and Steam Guard)." -ForegroundColor Yellow
        Write-Host "  Type it into steamcmd. This script neither reads nor stores it." -ForegroundColor Yellow
    }

    New-Item -ItemType Directory -Force -Path $j.Dir | Out-Null
    $t0 = Get-Date
    & $steamcmdExe @scArgs
    $code = $LASTEXITCODE
    $elapsed = (Get-Date) - $t0
    Write-Host ("  steamcmd exit {0} after {1:mm\:ss}" -f $code, $elapsed)
    if ($code -ne 0) { Write-Host "  FETCH FAILED - read the output above before trusting anything in $($j.Dir)." -ForegroundColor Red }

    Write-BuildFacts "$($j.Which) / $($j.Branch)" $j.Dir $j.App
}

Write-Host ""
Write-Host "Baseline, for comparison (the installed builds, never written to):" -ForegroundColor Cyan
Write-BuildFacts "baseline client" "C:\Program Files (x86)\Steam\steamapps\common\Valheim" $APP_CLIENT
Write-BuildFacts "baseline server" "C:\Program Files (x86)\Steam\steamapps\common\Valheim dedicated server" $APP_SERVER
