# Builds the store release zip: RavenIronStudios-ValkyriesCargo-<version>.zip in dist\.
# Goes to Hexium (hexium.gg). Thunderstore is not a channel we publish to - but the LAYOUT
# below is Thunderstore's package format, because that is the format Hexium consumes.
#
#   .\tools\package.ps1
#
# ASCII ONLY IN THIS FILE, deliberately. Windows PowerShell 5.1 reads a BOM-less file as
# ANSI, so a UTF-8 em-dash decodes to a sequence ending in a curly quote - which 5.1 treats
# as a string delimiter and the whole script stops parsing. Cost one run to find.
#
# THE VERSION HAS ONE SOURCE: the csproj. The C# constant is already generated from it
# (see GenerateVersionConst), and this script WRITES manifest.json from it rather than
# comparing them. Ragnarok's Wrath checks that three copies agree and refuses when they do
# not, which is a good guard - but against a problem that need not exist. A number that is
# written cannot drift from itself.
#
# The built DLL is still checked against the csproj, because that catches the one thing
# generation cannot: packaging a stale binary from an earlier build.

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

# --- the one source of truth -------------------------------------------------------
$csproj = "$root\ValkyriesCargo\ValkyriesCargo.csproj"
$version = (Select-String -Path $csproj -Pattern '<Version>([^<]+)</Version>').Matches[0].Groups[1].Value
if (-not $version) { Write-Host "No <Version> in $csproj" -ForegroundColor Red; exit 1 }
Write-Host "Version $version (from the csproj)" -ForegroundColor Cyan

# --- everything the stores require, checked BEFORE a long build ---------------------
$required = @("manifest.json", "README.md", "CHANGELOG.md", "icon.png")
$missing = @($required | Where-Object { -not (Test-Path (Join-Path $root $_)) })

if ($missing.Count -gt 0) {
    Write-Host "Missing required file(s) - refusing to package:" -ForegroundColor Red
    foreach ($m in $missing) { Write-Host "  $m" }
    if ($missing -contains "icon.png") {
        Write-Host ""
        Write-Host "icon.png must be a 256x256 PNG. Both stores reject a package without one," -ForegroundColor Yellow
        Write-Host "and neither says so clearly, so it is checked here instead." -ForegroundColor Yellow
    }
    exit 1
}

# The store rejects any other size, with an error that does not name the cause.
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Image]::FromFile("$root\icon.png")
$w = $icon.Width; $h = $icon.Height
$icon.Dispose()
if ($w -ne 256 -or $h -ne 256) {
    Write-Host "icon.png is ${w}x${h}; it must be 256x256." -ForegroundColor Red
    exit 1
}

# --- write the manifest's version rather than checking it ---------------------------
$manifestPath = "$root\manifest.json"
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
if ($manifest.version_number -ne $version) {
    Write-Host "manifest.json: $($manifest.version_number) -> $version" -ForegroundColor Yellow
    $manifest.version_number = $version
    # UTF8 without BOM: the stores parse this, and a BOM has broken that before.
    $json = ($manifest | ConvertTo-Json -Depth 8)
    [System.IO.File]::WriteAllText($manifestPath, $json, (New-Object System.Text.UTF8Encoding($false)))
}

# --- clean Release build ------------------------------------------------------------
dotnet build $csproj -c Release -v q --nologo
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed." -ForegroundColor Red; exit 1 }

$dll = "$root\ValkyriesCargo\bin\Release\ValkyriesCargo.dll"
if (-not (Test-Path $dll)) { Write-Host "No Release DLL at $dll" -ForegroundColor Red; exit 1 }

# The one drift generation cannot prevent: a stale binary from an earlier build.
$built = [System.Reflection.AssemblyName]::GetAssemblyName($dll).Version
$want = [Version]"$version.0"
if ($built -ne $want) {
    Write-Host "STALE BINARY - refusing to package:" -ForegroundColor Red
    Write-Host "  csproj says : $version"
    Write-Host "  the DLL says: $built"
    exit 1
}

# --- assemble the zip both stores expect --------------------------------------------
# Layout and writer both learned on FireFront's upload day, 2026-08-27: store files at the
# ROOT, the DLL under plugins/ (the BepInEx layout mod managers map onto BepInEx/plugins;
# Hexium refuses a root-level DLL), and entries written BY HAND because PS 5.1's
# Compress-Archive builds zips Hexium's parser rejects ("No manifest.json found") while
# .NET Framework's CreateFromDirectory names nested entries with spec-invalid BACKSLASHES.
$dist = "$root\dist"
$stage = "$dist\stage"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item "$root\manifest.json", "$root\README.md", "$root\CHANGELOG.md", "$root\icon.png" -Destination $stage
New-Item -ItemType Directory -Force -Path "$stage\plugins" | Out-Null
Copy-Item $dll -Destination "$stage\plugins"

$zip = "$dist\RavenIronStudios-ValkyriesCargo-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $stage -Recurse -File | ForEach-Object {
        $rel = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $_.FullName, $rel,
            [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
Remove-Item $stage -Recurse -Force

Write-Host ""
Write-Host "Packaged: $zip" -ForegroundColor Green
Get-Item $zip | Select-Object Name, Length | Format-Table -AutoSize

# Read the archive back and print it. A zip with the wrong layout uploads fine and fails on
# the store's side with a message that does not name the cause, so look at it here instead.
$check = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    Write-Host "Contents:" -ForegroundColor Cyan
    foreach ($e in $check.Entries) { Write-Host "  $($e.FullName)" }
} finally { $check.Dispose() }
