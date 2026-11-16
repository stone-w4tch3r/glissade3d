[CmdletBinding()]
param(
    [string]$Ksp = $env:KSP_DIR
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
. (Join-Path $root "common.ps1")

$mod = "Glissade3D"
$source = Join-Path $root "dist\GameData\$mod"
if (-not (Test-Path -LiteralPath $source)) { Fail "Build the mod first: build.ps1" }

$kspPath = Get-KspPath $Ksp

$running = Get-Process -Name "KSP_x64" -ErrorAction SilentlyContinue
if ($running) { Fail "Close KSP before install." }

$target = Join-Path $kspPath "GameData\$mod"
if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
Copy-Item -LiteralPath $source -Destination $target -Recurse -Force

Write-Host "Installed: $target"
