# Regenerates docs\ECONOMY-SIM.md: the off-game economy simulation, run against the shipping
# Core sources. Deterministic — two runs write the same bytes — so a diff here means a number
# in Market.cs, ModConfig.cs or the catalogue moved.
#
#   .\tools\run-econsim.ps1              # rewrite docs\ECONOMY-SIM.md
#   .\tools\run-econsim.ps1 -Check       # fail if it would change (for a pre-commit check)
#   .\tools\run-econsim.ps1 -Seed 7      # a different run of the seeded scenarios
#
# Exit code 1 also means an assertion in scenario 7 or 8 failed: this script is a test too.

[CmdletBinding()]
param(
    [switch]$Check,
    [long]$Seed = 20260906
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'tests\EconSim\EconSim.csproj'
$report = Join-Path $root 'docs\ECONOMY-SIM.md'

if ($Check) {
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ("econsim-" + [guid]::NewGuid().ToString('N') + ".md")
    dotnet run --project $project -c Release -- --out $temp --seed $Seed
    $code = $LASTEXITCODE
    if ($code -ne 0) {
        Write-Host "ECONSIM CHECKS FAILED" -ForegroundColor Red
        if (Test-Path $temp) { Remove-Item $temp -Force }
        exit $code
    }
    # Line endings are compared normalised: the tool always writes LF, and .gitattributes
    # checks the file out with the platform's endings.
    $same = $false
    if (Test-Path $report) {
        $a = [System.IO.File]::ReadAllText($temp).Replace("`r`n", "`n")
        $b = [System.IO.File]::ReadAllText($report).Replace("`r`n", "`n")
        $same = ($a -eq $b)
    }
    Remove-Item $temp -Force
    if (-not $same) {
        Write-Host "docs\ECONOMY-SIM.md is out of date - run .\tools\run-econsim.ps1" -ForegroundColor Red
        exit 1
    }
    Write-Host "docs\ECONOMY-SIM.md is up to date" -ForegroundColor Green
    exit 0
}

dotnet run --project $project -c Release -- --out $report --seed $Seed

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "ECONSIM CHECKS FAILED (the report was still written)" -ForegroundColor Red
    exit $LASTEXITCODE
}
