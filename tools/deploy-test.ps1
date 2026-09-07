# Copies the built Release DLL to a test server and, on request, to the client.
#
#   .\tools\deploy-test.ps1 -Build -Client            # build, then server + Steam client
#   .\tools\deploy-test.ps1 -GaleProfile raveniron    # server + that Gale profile
#   .\tools\deploy-test.ps1 -Client -ClientOnly       # the client alone (the version wall)
#   .\tools\deploy-test.ps1 -Server CairnTest -Force  # CairnTest needs -Force: it is in use
#   .\tools\deploy-test.ps1 -WhatIf                   # say what it would do, touch nothing
#
# It NEVER starts a server or the game. It refuses while the target is running, because
# Valheim holds the DLL open and a copy either fails or lands half-written.
#
# The version gate is why both sides are one command: ServerSync is ModRequired with
# MinimumRequiredVersion == CurrentVersion, so a client and a server on different builds
# do not connect - and "different builds" includes the same version number built twice.
# Every destination is printed back with its size and its ASSEMBLY version, read from the
# file that is now on disk, so a stale copy is visible before the game says so.
#
# ASCII ONLY IN THIS FILE, deliberately. Windows PowerShell 5.1 reads a BOM-less file as
# ANSI, so a UTF-8 em-dash decodes to a sequence ending in a curly quote - which 5.1 treats
# as a string delimiter and the whole script stops parsing. (tools\package.ps1 header.)

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('StormTest', 'CairnTest')]
    [string]$Server = 'StormTest',

    # Also copy to the Steam client's plugins folder.
    [switch]$Client,

    # Copy to this Gale profile's plugins folder INSTEAD of the Steam client's.
    [string]$GaleProfile,

    # Client only, leaving the server's DLL alone. This is how the version wall (CLAUDE.md
    # item 3) is set up: build a different version, put it on ONE side, try to join.
    [switch]$ClientOnly,

    # Run the Release build first.
    [switch]$Build,

    # CairnTest is the owner's other test bed; -Force says you mean it.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

$ServerRoots = @{
    'StormTest' = 'C:\Users\donfr\ValheimServers\StormTest'
    'CairnTest' = 'C:\Users\donfr\ValheimServers\CairnTest'
}
$SteamClient = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim'
$GaleRoot = Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles'

# The package layout every RavenIron mod uses; the mod manager maps it onto BepInEx\plugins.
$PluginDir = 'RavenIronStudios-ValkyriesCargo'
$DllName = 'ValkyriesCargo.dll'

# --- CairnTest is somebody else's test right now --------------------------------------
if ($Server -eq 'CairnTest' -and -not $Force) {
    Write-Host "CairnTest is the owner's other test bed (Yggdrasil's Reckoning). Refusing." -ForegroundColor Red
    Write-Host "Pass -Force if you really mean CairnTest; the default is StormTest." -ForegroundColor Yellow
    exit 1
}

# --- the source -----------------------------------------------------------------------
$csproj = Join-Path $root 'ValkyriesCargo\ValkyriesCargo.csproj'
$src = Join-Path $root 'ValkyriesCargo\bin\Release\ValkyriesCargo.dll'

if ($Build) {
    if ($PSCmdlet.ShouldProcess($csproj, 'dotnet build -c Release')) {
        dotnet build $csproj -c Release -v q --nologo
        if ($LASTEXITCODE -ne 0) { Write-Host 'Build failed.' -ForegroundColor Red; exit 1 }
    }
}

$haveSrc = Test-Path $src
if (-not $haveSrc) {
    if ($WhatIfPreference) {
        Write-Host "WhatIf: there is no built DLL at $src yet; the plan below is still shown." -ForegroundColor Yellow
    }
    else {
        Write-Host "No Release DLL at $src" -ForegroundColor Red
        Write-Host "Run:  .\tools\deploy-test.ps1 -Build     (or dotnet build $csproj -c Release)" -ForegroundColor Yellow
        exit 1
    }
}

# --- destinations ---------------------------------------------------------------------
if ($ClientOnly -and -not ($Client -or $GaleProfile)) {
    Write-Host '-ClientOnly needs -Client or -GaleProfile: there would be nowhere to copy to.' -ForegroundColor Red
    exit 1
}

$destinations = @()
if (-not $ClientOnly) {
    $destinations += [pscustomobject]@{
        What = "server $Server"
        Dir  = Join-Path $ServerRoots[$Server] "BepInEx\plugins\$PluginDir"
        Root = $ServerRoots[$Server]
        Kind = 'server'
    }
}

if ($GaleProfile) {
    $profileDir = Join-Path $GaleRoot $GaleProfile
    if (-not (Test-Path $profileDir)) {
        Write-Host "No Gale profile named '$GaleProfile' under $GaleRoot" -ForegroundColor Red
        if (Test-Path $GaleRoot) {
            Write-Host 'Profiles here:' -ForegroundColor Yellow
            Get-ChildItem $GaleRoot -Directory | ForEach-Object { Write-Host "  $($_.Name)" }
        }
        exit 1
    }
    $destinations += [pscustomobject]@{
        What = "Gale profile $GaleProfile"
        Dir  = Join-Path $profileDir "BepInEx\plugins\$PluginDir"
        Root = $profileDir
        Kind = 'client'
    }
}
elseif ($Client) {
    $destinations += [pscustomobject]@{
        What = 'Steam client'
        Dir  = Join-Path $SteamClient "BepInEx\plugins\$PluginDir"
        Root = $SteamClient
        Kind = 'client'
    }
}

# "Valheim - Clean" sits beside the Steam install and is never written to. Belt and braces:
foreach ($d in $destinations) {
    if ($d.Dir -like '*Valheim - Clean*') {
        Write-Host "Refusing: '$($d.Dir)' is inside the untouched clean copy." -ForegroundColor Red
        exit 1
    }
}

# --- refuse while it is running --------------------------------------------------------
# The start script runs "valheim_server", so the image name is valheim_server.exe; the game
# is valheim.exe. Both hold the DLL open. Match by the process's own path where Windows lets
# us read it, so StormTest is not blocked by a CairnTest process; when the path is unreadable,
# refuse anyway and name the pid.
function Test-Running {
    param([string]$ProcName, [string]$UnderRoot)

    $hits = @()
    $procs = @(Get-Process -Name $ProcName -ErrorAction SilentlyContinue)
    foreach ($p in $procs) {
        $path = $null
        try { $path = $p.Path } catch { $path = $null }
        if ($null -eq $path) { $hits += "pid $($p.Id) (path unreadable)" }
        elseif ($path -like "$UnderRoot*") { $hits += "pid $($p.Id) $path" }
    }
    return $hits
}

foreach ($d in $destinations) {
    $procName = if ($d.Kind -eq 'server') { 'valheim_server' } else { 'valheim' }
    $running = Test-Running -ProcName $procName -UnderRoot $d.Root
    if ($running.Count -gt 0) {
        Write-Host "Refusing: $procName is running for $($d.What):" -ForegroundColor Red
        foreach ($h in $running) { Write-Host "  $h" }
        Write-Host 'Valheim holds the DLL open. Stop it first (CTRL-BREAK in the server window), then run this again.' -ForegroundColor Yellow
        exit 1
    }
}

# --- copy -------------------------------------------------------------------------------
function Show-Dll {
    param([string]$Label, [string]$FilePath)

    if (-not (Test-Path $FilePath)) {
        Write-Host ("  {0,-22} MISSING" -f $Label) -ForegroundColor DarkGray
        return
    }
    $item = Get-Item $FilePath
    $ver = 'unreadable'
    try { $ver = [System.Reflection.AssemblyName]::GetAssemblyName($FilePath).Version.ToString() } catch { }
    Write-Host ("  {0,-22} {1,8} bytes  assembly {2}  {3:yyyy-MM-dd HH:mm:ss}" -f $Label, $item.Length, $ver, $item.LastWriteTime)
}

Write-Host ''
Write-Host 'Source' -ForegroundColor Cyan
Show-Dll -Label 'bin\Release' -FilePath $src

foreach ($d in $destinations) {
    Write-Host ''
    Write-Host "$($d.What)" -ForegroundColor Cyan
    Write-Host "  $($d.Dir)" -ForegroundColor DarkGray

    $dest = Join-Path $d.Dir $DllName
    $prev = "$dest.prev"

    if (-not (Test-Path $d.Dir)) {
        if ($PSCmdlet.ShouldProcess($d.Dir, 'create plugin folder')) {
            New-Item -ItemType Directory -Force -Path $d.Dir | Out-Null
        }
        else { Write-Host '  (would create the plugin folder)' -ForegroundColor DarkGray }
    }

    Show-Dll -Label 'before' -FilePath $dest

    if (Test-Path $dest) {
        if ($PSCmdlet.ShouldProcess($dest, "back up as $DllName.prev")) {
            Copy-Item $dest $prev -Force
        }
    }

    if ($haveSrc) {
        if ($PSCmdlet.ShouldProcess($dest, "copy from $src")) {
            Copy-Item $src $dest -Force
            Show-Dll -Label 'after' -FilePath $dest
        }
    }
    else {
        Write-Host '  (no built DLL to copy)' -ForegroundColor DarkGray
    }
}

Write-Host ''
if ($WhatIfPreference) {
    Write-Host 'WhatIf: nothing was written. Nothing was started either - this script never starts anything.' -ForegroundColor Yellow
}
else {
    Write-Host 'Done. Nothing was started: launch the server and the game yourself.' -ForegroundColor Green
    Write-Host 'Every "after" line above must show the SAME assembly version, or the handshake will refuse.' -ForegroundColor DarkGray
}
