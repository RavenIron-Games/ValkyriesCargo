# Reads a BepInEx log and prints THIS BOOT only, filtered to Valkyrie's Cargo.
#
#   .\tools\tail-log.ps1                       # StormTest, this boot, our lines
#   .\tools\tail-log.ps1 -Last 15              # ...the last 15 of them
#   .\tools\tail-log.ps1 -Client               # the Steam client's log
#   .\tools\tail-log.ps1 -GaleProfile raveniron
#   .\tools\tail-log.ps1 -Server CairnTest -All
#   .\tools\tail-log.ps1 -Follow               # keep printing as the log grows
#
# WHY "this boot": StormTest's BepInEx.cfg has [Logging.Disk] AppendLog = true, so
# BepInEx\LogOutput.log accumulates across boots (531 KB / 4406 lines on 2026-09-06).
# The current run is everything AFTER the LAST "Chainloader started" line. Reading the
# whole file and believing the first `director up` you see is how you prove yesterday.
# The Steam client's BepInEx.cfg has AppendLog = false, so its file is one boot already;
# the same rule is applied anyway and costs nothing.
#
# ASCII ONLY IN THIS FILE, deliberately. Windows PowerShell 5.1 reads a BOM-less file as
# ANSI, so a UTF-8 em-dash decodes to a sequence ending in a curly quote - which 5.1 treats
# as a string delimiter and the whole script stops parsing. (tools\package.ps1 header.)
#
# The log is opened with FileShare ReadWrite+Delete: the server holds it open while it runs,
# and a plain Get-Content can fail on a locked file. This script only ever READS.

[CmdletBinding()]
param(
    [ValidateSet('StormTest', 'CairnTest')]
    [string]$Server = 'StormTest',

    # The Steam client's log instead of a server's.
    [switch]$Client,

    # A Gale profile's log instead of Steam's (the owner's client usually runs through Gale).
    [string]$GaleProfile,

    # Any other log file; wins over everything above.
    [string]$Path,

    # Print every line of the boot, not just ours.
    [switch]$All,

    # Print only the last N matching lines.
    [int]$Last = 0,

    # Keep printing as the log grows (Ctrl-C to stop).
    [switch]$Follow
)

$ErrorActionPreference = 'Stop'

$ServerRoots = @{
    'StormTest' = 'C:\Users\donfr\ValheimServers\StormTest'
    'CairnTest' = 'C:\Users\donfr\ValheimServers\CairnTest'
}
$SteamClient = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim'
$GaleRoot = Join-Path $env:APPDATA 'com.kesomannen.gale\valheim\profiles'

# The BepInEx log source tag: BepInPlugin's PluginName in ValkyriesCargo\ValkyriesCargo.cs
# ("Valkyrie's Cargo"). Every line the mod writes is "[Level  :Valkyrie's Cargo] ...".
$ModTag = "Valkyrie's Cargo"

# --- which file ---------------------------------------------------------------------
if ($Path) {
    $logPath = $Path
    $what = "file $Path"
}
elseif ($GaleProfile) {
    $profileDir = Join-Path $GaleRoot $GaleProfile
    if (-not (Test-Path $profileDir)) {
        Write-Host "No Gale profile named '$GaleProfile' under $GaleRoot" -ForegroundColor Red
        Write-Host "Profiles here:" -ForegroundColor Yellow
        if (Test-Path $GaleRoot) { Get-ChildItem $GaleRoot -Directory | ForEach-Object { Write-Host "  $($_.Name)" } }
        exit 1
    }
    $logPath = Join-Path $profileDir 'BepInEx\LogOutput.log'
    $what = "Gale profile $GaleProfile"
}
elseif ($Client) {
    $logPath = Join-Path $SteamClient 'BepInEx\LogOutput.log'
    $what = 'the Steam client'
}
else {
    $logPath = Join-Path $ServerRoots[$Server] 'BepInEx\LogOutput.log'
    $what = "server $Server"
}

if (-not (Test-Path $logPath)) {
    Write-Host "No log at $logPath" -ForegroundColor Red
    exit 1
}

# --- reading a file somebody else is writing -----------------------------------------
function Read-Shared {
    param([string]$FilePath, [long]$From = 0)

    $share = ([System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete)
    $fs = New-Object System.IO.FileStream($FilePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
    try {
        $len = $fs.Length
        if ($From -gt $len) { $From = 0 }   # truncated or rotated under us
        if ($From -gt 0) { [void]$fs.Seek($From, [System.IO.SeekOrigin]::Begin) }
        $sr = New-Object System.IO.StreamReader($fs)
        try { $text = $sr.ReadToEnd() } finally { $sr.Dispose() }
    }
    finally { $fs.Dispose() }

    return [pscustomobject]@{ Text = $text; Length = $len }
}

$esc = [regex]::Escape($ModTag)
$Filter = "(:$esc\])|(Loading \[$esc)|(Chainloader started)|(Load world)|(Exception)"

# NOTE: never name a local $all here - PowerShell variables are case-insensitive and it
# would silently overwrite the -All switch. Cost one run to find.
$read = Read-Shared -FilePath $logPath
$fileLines = $read.Text -split "`r?`n"

# --- this boot ------------------------------------------------------------------------
$start = 0
for ($i = 0; $i -lt $fileLines.Count; $i++) {
    if ($fileLines[$i] -match 'Chainloader started') { $start = $i }
}
$boot = $fileLines[$start..($fileLines.Count - 1)]

Write-Host "$logPath" -ForegroundColor Cyan
Write-Host ("$what - {0} line(s) in the file, this boot starts at line {1}; showing {2}" -f `
        $fileLines.Count, ($start + 1), $(if ($All) { 'every line' } else { "our lines, 'Chainloader started', 'Load world' and anything with 'Exception'" })) -ForegroundColor DarkGray
Write-Host ''

$out = @()
foreach ($line in $boot) {
    if ($line.Length -eq 0) { continue }
    if ($All -or $line -match $Filter) { $out += $line }
}
if ($Last -gt 0 -and $out.Count -gt $Last) { $out = $out[($out.Count - $Last)..($out.Count - 1)] }
foreach ($line in $out) { Write-Output $line }

if ($out.Count -eq 0) {
    Write-Host ''
    Write-Host "Nothing from this mod in this boot. Either the DLL is not in that plugins folder," -ForegroundColor Yellow
    Write-Host "or the process has not reached Chainloader yet. -All shows the whole boot." -ForegroundColor Yellow
}

# --- follow ---------------------------------------------------------------------------
if (-not $Follow) { return }

Write-Host ''
Write-Host '--- following (Ctrl-C to stop) ---' -ForegroundColor DarkGray
$pos = $read.Length
while ($true) {
    Start-Sleep -Milliseconds 500
    try { $more = Read-Shared -FilePath $logPath -From $pos } catch { continue }
    if ($more.Length -eq $pos -and $more.Text.Length -eq 0) { continue }
    $pos = $more.Length
    foreach ($line in ($more.Text -split "`r?`n")) {
        if ($line.Length -eq 0) { continue }
        if ($All -or $line -match $Filter) { Write-Output $line }
    }
}
