# Writes the fast-test values into a test server's com.raveniron.valkyriescargo.cfg.
#
#   .\tools\set-test-config.ps1 -Show          # what the file says now
#   .\tools\set-test-config.ps1                # apply the fast-test values (backs up first)
#   .\tools\set-test-config.ps1 -Relax         # ...and drop the eligibility gates too
#   .\tools\set-test-config.ps1 -Restore       # put the .cfg.bak back
#   .\tools\set-test-config.ps1 -WhatIf        # say what it would change, touch nothing
#
# WHAT IT CHANGES AND WHY (docs\PROOF-CLIENT.md section C has the full table):
#   EventCheckIntervalMinutes 25 -> 1    Scheduler.Tick only rolls once an interval
#   EventChancePercent        25 -> 100  a roll under the chance prints
#                                        "rolled 0.xx >= 0.25: no visit (...)"
#   DaytimeOnly             true -> false   otherwise: "held: night, and DaytimeOnly is on"
# and with -Relax, for a session with no proper base to hand:
#   RequireRested           true -> false   otherwise: "... not eligible: not rested"
#   MinComfortLevel            4 -> 0       otherwise: "... not eligible: comfort < 4"
#   MinBaseValue               1 -> 0       otherwise: "... not eligible: baseValue < 1"
# WITHOUT -Relax the three eligibility gates keep their real values, which is what makes
# CLAUDE.md items 7 and 12 mean anything: `cargo visit` off a base is refused by name.
#
# Server.* entries are synced and LOCKED (ServerSync), so this file is the only place they
# can be set for a dedicated server: a client's local edit is rejected unless that client is
# on adminlist.txt (which is item 4's whole point).
#
# It refuses while the server is running: BepInEx rewrites its config files, and an edit made
# under a live process is lost the moment it exits.
#
# ASCII ONLY IN THIS FILE, deliberately. Windows PowerShell 5.1 reads a BOM-less file as
# ANSI, so a UTF-8 em-dash decodes to a sequence ending in a curly quote - which 5.1 treats
# as a string delimiter and the whole script stops parsing. (tools\package.ps1 header.)

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [ValidateSet('StormTest', 'CairnTest')]
    [string]$Server = 'StormTest',

    # Print the current values of the keys this script touches and stop.
    [switch]$Show,

    # Copy the .cfg.bak back over the .cfg.
    [switch]$Restore,

    # Also drop RequireRested / MinComfortLevel / MinBaseValue.
    [switch]$Relax,

    # CairnTest is the owner's other test bed; -Force says you mean it.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$ServerRoots = @{
    'StormTest' = 'C:\Users\donfr\ValheimServers\StormTest'
    'CairnTest' = 'C:\Users\donfr\ValheimServers\CairnTest'
}
$CfgName = 'com.raveniron.valkyriescargo.cfg'

if ($Server -eq 'CairnTest' -and -not $Force) {
    Write-Host "CairnTest is the owner's other test bed. Refusing." -ForegroundColor Red
    Write-Host 'Pass -Force if you really mean CairnTest; the default is StormTest.' -ForegroundColor Yellow
    exit 1
}

$root = $ServerRoots[$Server]
$cfg = Join-Path $root "BepInEx\config\$CfgName"
$bak = "$cfg.bak"

if (-not (Test-Path $cfg)) {
    Write-Host "No config at $cfg" -ForegroundColor Red
    Write-Host 'BepInEx writes it on the first boot with the DLL in place. Boot the server once, stop it, run this again.' -ForegroundColor Yellow
    exit 1
}

# The keys this script owns, in the order the runbook lists them.
$Fast = [ordered]@{
    'EventCheckIntervalMinutes' = '1'
    'EventChancePercent'        = '100'
    'DaytimeOnly'               = 'false'
}
$Gates = [ordered]@{
    'RequireRested'   = 'false'
    'MinComfortLevel' = '0'
    'MinBaseValue'    = '0'
}
$Watched = @($Fast.Keys) + @($Gates.Keys)

# --- do not edit under a running server -------------------------------------------------
function Test-Running {
    param([string]$UnderRoot)

    $hits = @()
    foreach ($p in @(Get-Process -Name 'valheim_server' -ErrorAction SilentlyContinue)) {
        $path = $null
        try { $path = $p.Path } catch { $path = $null }
        if ($null -eq $path) { $hits += "pid $($p.Id) (path unreadable)" }
        elseif ($path -like "$UnderRoot*") { $hits += "pid $($p.Id) $path" }
    }
    return $hits
}

# --- reading the file --------------------------------------------------------------------
# BepInEx writes "[Section]" then "Key = Value". Only the [Server] section is touched:
# the same key name could exist under [Client] in a later build.
function Get-CfgIndex {
    param([string[]]$Lines, [string]$Section)

    $map = @{}
    $current = ''
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        $line = $Lines[$i]
        if ($line -match '^\s*\[(.+)\]\s*$') { $current = $Matches[1]; continue }
        if ($current -ne $Section) { continue }
        if ($line -match '^\s*([A-Za-z0-9_]+)\s*=\s*(.*)$') {
            if (-not $map.ContainsKey($Matches[1])) { $map[$Matches[1]] = [pscustomobject]@{ Line = $i; Value = $Matches[2] } }
        }
    }
    return $map
}

function Show-Values {
    param([string]$Label, [string]$FilePath)

    if (-not (Test-Path $FilePath)) { Write-Host "  $Label : missing" -ForegroundColor DarkGray; return }
    $lines = [System.IO.File]::ReadAllLines($FilePath)
    $idx = Get-CfgIndex -Lines $lines -Section 'Server'
    $item = Get-Item $FilePath
    Write-Host "  $Label  ($($item.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')))" -ForegroundColor DarkGray
    foreach ($k in $Watched) {
        if ($idx.ContainsKey($k)) { Write-Host ("    {0,-26} {1}" -f $k, $idx[$k].Value) }
        else { Write-Host ("    {0,-26} KEY NOT IN THE FILE" -f $k) -ForegroundColor Red }
    }
}

Write-Host ''
Write-Host "$cfg" -ForegroundColor Cyan

if ($Show) {
    Show-Values -Label '[Server], now' -FilePath $cfg
    Write-Host ''
    if (Test-Path $bak) { Show-Values -Label "$CfgName.bak" -FilePath $bak }
    else { Write-Host '  no .cfg.bak yet (an apply makes one)' -ForegroundColor DarkGray }
    return
}

$running = Test-Running -UnderRoot $root
if ($running.Count -gt 0) {
    Write-Host "Refusing: valheim_server is running for $Server :" -ForegroundColor Red
    foreach ($h in $running) { Write-Host "  $h" }
    Write-Host 'BepInEx rewrites its config on exit and would lose the edit. Stop the server (CTRL-BREAK), then run this again.' -ForegroundColor Yellow
    exit 1
}

# --- restore ------------------------------------------------------------------------------
if ($Restore) {
    if (-not (Test-Path $bak)) {
        Write-Host "No backup at $bak - nothing to restore." -ForegroundColor Red
        exit 1
    }
    Write-Host 'Before:' -ForegroundColor DarkGray
    Show-Values -Label '[Server], now' -FilePath $cfg
    if ($PSCmdlet.ShouldProcess($cfg, "restore from $CfgName.bak")) {
        Copy-Item $bak $cfg -Force
        Write-Host ''
        Write-Host 'After:' -ForegroundColor DarkGray
        Show-Values -Label '[Server], now' -FilePath $cfg
        Write-Host ''
        Write-Host 'Restored. Start the server for it to take effect.' -ForegroundColor Green
    }
    return
}

# --- apply ---------------------------------------------------------------------------------
$wanted = [ordered]@{}
foreach ($k in $Fast.Keys) { $wanted[$k] = $Fast[$k] }
if ($Relax) { foreach ($k in $Gates.Keys) { $wanted[$k] = $Gates[$k] } }

$lines = [System.IO.File]::ReadAllLines($cfg)
$idx = Get-CfgIndex -Lines $lines -Section 'Server'

# Only keys that are already in the file. A missing key means BepInEx has not written this
# build's config yet, and inventing the line would put it in the wrong section or the wrong
# type: say so and stop.
$missing = @($wanted.Keys | Where-Object { -not $idx.ContainsKey($_) })
if ($missing.Count -gt 0) {
    Write-Host 'These keys are not in the [Server] section of that file:' -ForegroundColor Red
    foreach ($m in $missing) { Write-Host "  $m" }
    Write-Host ''
    Write-Host 'BepInEx writes the config on the first boot with the DLL in place. Deploy the DLL,' -ForegroundColor Yellow
    Write-Host 'boot the server once, stop it, and run this again. Nothing was changed.' -ForegroundColor Yellow
    exit 1
}

Write-Host 'Before:' -ForegroundColor DarkGray
Show-Values -Label '[Server], now' -FilePath $cfg

# The .bak is made ONCE and kept: run this twice and the second run must not overwrite the
# backup with fast-test values, or -Restore would restore the test setup.
if (Test-Path $bak) {
    Write-Host ''
    Write-Host "Keeping the existing backup: $CfgName.bak ($((Get-Item $bak).LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')))" -ForegroundColor DarkGray
    Write-Host '-Restore returns the file to THAT state, not to the defaults.' -ForegroundColor DarkGray
}
else {
    if ($PSCmdlet.ShouldProcess($bak, "save a backup of $CfgName")) {
        Copy-Item $cfg $bak -Force
        Write-Host ''
        Write-Host "Backed up to $CfgName.bak" -ForegroundColor DarkGray
    }
}

$changes = @()
foreach ($k in $wanted.Keys) {
    $was = $idx[$k].Value
    $now = $wanted[$k]
    if ($was -eq $now) { continue }
    $i = $idx[$k].Line
    $lines[$i] = ($lines[$i] -replace '^(\s*' + [regex]::Escape($k) + '\s*=\s*).*$', ('${1}' + $now))
    $changes += "$k : $was -> $now"
}

Write-Host ''
if ($changes.Count -eq 0) {
    Write-Host 'Already at the fast-test values; nothing to write.' -ForegroundColor Green
    return
}
foreach ($c in $changes) { Write-Host "  $c" }

if ($PSCmdlet.ShouldProcess($cfg, 'write the fast-test values')) {
    # UTF8 without a BOM: BepInEx reads these files itself and a BOM has broken parsers before.
    [System.IO.File]::WriteAllLines($cfg, $lines, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ''
    Show-Values -Label '[Server], now' -FilePath $cfg
    Write-Host ''
    Write-Host 'Written. Start the server for it to take effect; `cargo status` prints these back.' -ForegroundColor Green
    if (-not $Relax) {
        Write-Host 'RequireRested / MinComfortLevel / MinBaseValue are untouched on purpose: items 7 and 12' -ForegroundColor DarkGray
        Write-Host 'need a real gate to refuse. Pass -Relax if there is no base to stand in.' -ForegroundColor DarkGray
    }
}
else {
    Write-Host ''
    Write-Host 'WhatIf: nothing was written.' -ForegroundColor Yellow
}
