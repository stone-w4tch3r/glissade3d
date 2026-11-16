# Build with compile-only KSP references; run pure math tests with real .NET.
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
. (Join-Path $root "common.ps1")

$version = Get-ModVersion $root "Glissade3D"
Assert-VersionMatch $root $version
& (Join-Path $root "tests\ReleaseNotesTests.ps1")
# Validate notes before the build; packaging clears dist, so write them afterwards.
$notesVersion = if ($env:GITHUB_REF_TYPE -eq "tag") { $version } else { "Unreleased" }
& (Join-Path $root "release-notes.ps1") -Version $notesVersion
if ($env:GITHUB_REF_TYPE -eq "tag") {
    if ($env:GITHUB_REF_NAME -cne "v$version") { Fail "Tag must be v$version." }
    & git merge-base --is-ancestor HEAD origin/main
    if ($LASTEXITCODE -ne 0) { Fail "Release commit must belong to public main." }
}

$refs = Join-Path $root ".tools\ksp-references"
$archive = Join-Path $root ".tools\KSP-1.12.5.zip"
New-Item -ItemType Directory -Force -Path (Split-Path $archive) | Out-Null
$url = "https://raw.githubusercontent.com/KSPModdingLibs/KSPLibs/529db6956a70a9e4fc0591382ff0fe4f55d96010/KSP-1.12.5.zip"
Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing
$expectedHash = "144a73888645e4ec62af9cc0354d96a3b175bd694f2f463e5699ca391259b033"
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
    Fail "KSP reference archive checksum mismatch."
}
Expand-Archive -LiteralPath $archive -DestinationPath $refs -Force
& (Join-Path $root "build.ps1") -Ksp $refs -Fetch -Package
if ($LASTEXITCODE -ne 0) { Fail "Build failed." }
& (Join-Path $root "release-notes.ps1") -Version $notesVersion -Output (Join-Path $root "dist\RELEASE_NOTES.md")

$compiler = Find-Compiler $root ""
$math = Join-Path $root "dist\MathTests.exe"
$framework = [System.Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()
& $compiler /nologo /nostdlib+ /target:exe /langversion:latest "/out:$math" `
    "/reference:$(Join-Path $framework 'mscorlib.dll')" `
    "/reference:$(Join-Path $framework 'System.dll')" `
    (Join-Path $root "src\PathMath.cs") (Join-Path $root "src\MarkerScale.cs") `
    (Join-Path $root "src\Shim.cs") (Join-Path $root "tests\MathTests.cs")
if ($LASTEXITCODE -ne 0) { Fail "Math test compilation failed." }
& $math
if ($LASTEXITCODE -ne 0) { Fail "Math tests failed." }

$dll = Join-Path $root "dist\GameData\Glissade3D\Plugins\Glissade3D.dll"
if ([Reflection.AssemblyName]::GetAssemblyName($dll).Version.ToString() -ne $version) {
    Fail "Built assembly version mismatch."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = Join-Path $root "dist\Glissade3D-$version.zip"
$zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $gameData = Join-Path $root "GameData\Glissade3D"
    $expected = @("GameData/Glissade3D/LICENSE.md", "GameData/Glissade3D/Plugins/Glissade3D.dll")
    $expected += @(Get-ChildItem -LiteralPath $gameData -File -Recurse | ForEach-Object {
        "GameData/Glissade3D/" + $_.FullName.Substring($gameData.Length + 1).Replace('\', '/')
    })
    $files = @($zip.Entries | Where-Object { $_.Name } | ForEach-Object { $_.FullName })
    $difference = Compare-Object $expected $files
    if ($difference) {
        $difference | Format-Table -AutoSize | Out-Host
        Fail "ZIP file list does not match the release layout. => ZIP only; <= expected only."
    }
    foreach ($entry in $zip.Entries) {
        if ($entry.Name -and $entry.Length -eq 0) { Fail "Empty release file: $($entry.FullName)" }
    }
} finally {
    $zip.Dispose()
}
Write-Host "CI checks passed: $zipPath"
