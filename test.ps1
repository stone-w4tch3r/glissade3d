[CmdletBinding()]
param(
    [string]$Ksp = $env:KSP_DIR,
    [string]$Csc
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
. (Join-Path $root "common.ps1")

$kspPath = Get-KspPath $Ksp
$managed = Get-ManagedPath $kspPath

$compiler = Find-Compiler $root $Csc
if (-not $compiler) { Fail "Compiler not found. Run build.ps1 -Fetch or set ROSLYN_CSC." }

$out = Join-Path $root "dist\tests"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$env:KSP_DIR = $kspPath

function Invoke-Suite {
    param(
        [string]$Name,
        [string]$FileName,
        [string[]]$Sources,
        [string[]]$References,
        [string[]]$Arguments = @()
    )

    $exe = Join-Path $out $FileName
    $compile = @("/nologo", "/nostdlib+", "/target:exe", "/langversion:latest", "/out:$exe")
    foreach ($reference in $References) {
        $compile += "/reference:`"$(Join-Path $managed $reference)`""
    }
    foreach ($source in $Sources) {
        $compile += (Join-Path $root $source)
    }

    & $compiler @compile
    if ($LASTEXITCODE -ne 0) { Fail "$Name build failed." }

    & $exe @Arguments
    if ($LASTEXITCODE -ne 0) { Fail "$Name failed." }
}

$core = @("mscorlib.dll", "System.dll")
$config = @("mscorlib.dll", "System.dll", "System.Core.dll", "Assembly-CSharp.dll", "UnityEngine.dll", "UnityEngine.CoreModule.dll")

Invoke-Suite "Math tests" "MathTests.exe" `
    -Sources @("src\PathMath.cs", "src\MarkerScale.cs", "src\Shim.cs", "tests\MathTests.cs") `
    -References $core

Invoke-Suite "Config tests" "CfgTests.exe" `
    -Sources @("src\Shim.cs", "src\RunwayData.cs", "tests\CfgTests.cs") `
    -References $config `
    -Arguments @((Join-Path $root "GameData\Glissade3D\DefaultRunways.cfg"))

Invoke-Suite "Route tests" "RouteTests.exe" `
    -Sources @("src\Shim.cs", "src\RunwayData.cs", "src\Route.cs", "src\RouteStore.cs", "src\RouteMath.cs", "src\RouteDraft.cs", "tests\RouteTests.cs") `
    -References $config

Invoke-Suite "Storage tests" "StorageTests.exe" `
    -Sources @("src\Shim.cs", "src\Log.cs", "src\ConfigIo.cs", "src\RunwayData.cs", "src\Route.cs", "src\RouteStore.cs", "src\RouteNaming.cs", "src\RouteStorage.cs", "tests\StorageTests.cs") `
    -References $config

Write-Host "All tests passed."
