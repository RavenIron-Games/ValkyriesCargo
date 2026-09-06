# Off-game logic tests. Two seconds, and it covers exactly the logic that fails silently.
# Run before every commit:  .\tools\run-tests.ps1
#
# The harness compiles the SHIPPING source, not a copy — see tests\CoreTests\CoreTests.csproj.

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

dotnet run --project "$root\tests\CoreTests\CoreTests.csproj" --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "TESTS FAILED" -ForegroundColor Red
    exit $LASTEXITCODE
}
