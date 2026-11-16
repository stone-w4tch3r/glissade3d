[CmdletBinding()]
param(
    [string]$Ksp = $env:KSP_DIR,
    [string]$Csc,
    [switch]$Fetch,
    [switch]$Package
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
. (Join-Path $root "common.ps1")

$mod = "Glissade3D"
$version = Get-ModVersion $root $mod
Assert-VersionMatch $root $version

$kspPath = Get-KspPath $Ksp
$managed = Get-ManagedPath $kspPath

$compiler = Find-Compiler $root $Csc
if (-not $compiler -and $Fetch) { $compiler = Install-Compiler $root }
if (-not $compiler) { Fail "Compiler not found. Run build.ps1 -Fetch or set ROSLYN_CSC." }

$sources = @(Get-ChildItem -LiteralPath (Join-Path $root "src") -Filter *.cs | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { Fail "No source files in src." }

$dist = Join-Path $root "dist"
if (Test-Path -LiteralPath $dist) { Remove-Item -LiteralPath $dist -Recurse -Force }

$ship = Join-Path $dist "GameData\$mod"
$plugins = Join-Path $ship "Plugins"
New-Item -ItemType Directory -Force -Path $plugins | Out-Null

Get-ChildItem -LiteralPath (Join-Path $root "GameData\$mod") -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $ship $_.Name) -Force
}
Copy-Item -LiteralPath (Join-Path $root "LICENSE.md") -Destination (Join-Path $ship "LICENSE.md")

$references = @(
    "mscorlib.dll",
    "System.dll",
    "System.Core.dll",
    "Assembly-CSharp.dll",
    "Assembly-CSharp-firstpass.dll",
    "UnityEngine.dll",
    "UnityEngine.CoreModule.dll",
    "UnityEngine.IMGUIModule.dll",
    "UnityEngine.UIModule.dll",
    "UnityEngine.InputLegacyModule.dll",
    "UnityEngine.AnimationModule.dll",
    "UnityEngine.PhysicsModule.dll",
    "UnityEngine.TextRenderingModule.dll"
)

$refArgs = foreach ($name in $references) { "/reference:`"$(Join-Path $managed $name)`"" }
$dll = Join-Path $plugins "$mod.dll"

Write-Host "Compiler: $compiler"
Write-Host "Version:  $version"
Write-Host "KSP:      $kspPath"

& $compiler /nologo /nostdlib+ /target:library /langversion:latest /optimize+ /debug- /deterministic+ /out:"$dll" @refArgs @sources
if ($LASTEXITCODE -ne 0) { Fail "Compiler failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath $dll)) { Fail "DLL not created: $dll" }

$size = (Get-Item -LiteralPath $dll).Length
Write-Host "Built: $dll ($size bytes)"

if ($Package) {
    $zip = Join-Path $dist "$mod-$version.zip"
    Compress-Archive -Path (Join-Path $dist "GameData") -DestinationPath $zip -Force
    Write-Host "Package: $zip"
}
