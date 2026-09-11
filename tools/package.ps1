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

# --- write the store page's version badge, for the same reason ----------------------
# HexiumDist\README.md is the store page, and its badge was the LAST hand-typed copy of
# the version left in this repo. The family runbook
# (docs\knowledge-base\IMPLEMENTATIONS\HexiumPublishing.md, step 2) bumps it by hand and
# warns in the same breath that this "is the step that gets missed". Same answer as the
# manifest above, and for the reason given at the top of this file: write it, do not
# compare it. A number that is written cannot drift from itself.
#
# A MISSING badge is refused rather than ignored: writing cannot fix a page whose shape
# changed, and a silent no-op here would put a stale version on the store page forever.
$storePage = "$root\HexiumDist\README.md"
if (Test-Path $storePage) {
    $page = [System.IO.File]::ReadAllText($storePage)
    # shields.io reads a literal dash as a field separator, so dashes are doubled:
    # 0.1.0-rc5 has to reach the URL as 0.1.0--rc5 or the badge renders as "0.1.0" grey "rc5".
    $badgeVersion = $version -replace '-', '--'
    $pattern = '(badge/Version-)(.*?)(-lightgrey\.svg)'
    $found = [regex]::Match($page, $pattern)
    if (-not $found.Success) {
        Write-Host "No version badge in HexiumDist\README.md - refusing to package." -ForegroundColor Red
        Write-Host "Expected a shields.io badge matching: badge/Version-<version>-lightgrey.svg" -ForegroundColor Yellow
        Write-Host "Either put the badge back, or delete this block if the page dropped badges." -ForegroundColor Yellow
        exit 1
    }
    if ($found.Groups[2].Value -ne $badgeVersion) {
        Write-Host "store page badge: $($found.Groups[2].Value) -> $badgeVersion" -ForegroundColor Yellow
        $page = [regex]::Replace($page, $pattern, "`${1}$badgeVersion`${3}")
        # UTF8 without BOM, same as the manifest: this file carries emoji and ships as-is.
        [System.IO.File]::WriteAllText($storePage, $page, (New-Object System.Text.UTF8Encoding($false)))
    }
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

# --- the body bundle, when it has been baked ----------------------------------------
# The csproj embeds Assets\valkyriescargo_kit as a resource WHEN THAT FILE EXISTS, so a DLL
# carrying it must be bigger than it. models\SETUP-FOR-CLAUDE.md section 3 records the failure
# this catches: the copy from the Unity output into the repo is manual, and a stale copy ships
# the old asset with no change in DLL size. A DLL smaller than the bundle is not carrying it
# at all. Printed either way, because "no bundle" is also a fact about what is being shipped.
$bundle = "$root\Assets\valkyriescargo_kit"
$dllSize = (Get-Item $dll).Length
if (Test-Path $bundle) {
    $bundleSize = (Get-Item $bundle).Length
    Write-Host ("Body bundle {0:N0} bytes, DLL {1:N0} bytes" -f $bundleSize, $dllSize) -ForegroundColor Cyan
    if ($dllSize -lt $bundleSize) {
        Write-Host "WARNING: the DLL is SMALLER than the bundle - it cannot be embedded." -ForegroundColor Yellow
        Write-Host "         Check the EmbeddedResource line in the csproj, then rebuild." -ForegroundColor Yellow
    }
} else {
    Write-Host ("No Assets\valkyriescargo_kit: this package ships the STAND-IN body (Server.BodyPrefab, default Dverger). DLL {0:N0} bytes." -f $dllSize) -ForegroundColor Yellow
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

# The README in the zip is the STORE PAGE. The root README.md is the DEVELOPER's -- 257 lines of
# layout, house style and build commands opening with a status line -- and shipping that as the store
# page was one call away from happening on 2026-09-07.
$storeReadme = "$root\HexiumDist\README.md"
if (-not (Test-Path $storeReadme)) { $storeReadme = "$root\README.md" }
Write-Host "  store page: $storeReadme"
Copy-Item "$root\manifest.json", "$root\CHANGELOG.md", "$root\icon.png" -Destination $stage
Copy-Item $storeReadme -Destination "$stage\README.md"
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
