function Fail([string]$Message) {
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}

function Get-KspPath([string]$Explicit) {
    $path = if ($Explicit) { $Explicit } else { $env:KSP_DIR }
    if (-not $path) { Fail "Set KSP_DIR or pass -Ksp <path>." }
    if (-not (Test-Path -LiteralPath $path)) { Fail "KSP folder not found: $path" }
    return (Resolve-Path -LiteralPath $path).Path
}

function Get-ManagedPath([string]$Ksp) {
    $managed = Join-Path $Ksp "KSP_x64_Data\Managed"
    if (-not (Test-Path -LiteralPath $managed)) { Fail "Managed folder not found: $managed" }

    $required = @(
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

    foreach ($name in $required) {
        $file = Join-Path $managed $name
        if (-not (Test-Path -LiteralPath $file)) { Fail "Missing assembly: $file" }
    }

    return $managed
}

function Find-Compiler([string]$Root, [string]$Explicit) {
    $candidates = @()
    if ($Explicit) { $candidates += $Explicit }
    if ($env:ROSLYN_CSC) { $candidates += $env:ROSLYN_CSC }
    if ($env:CSC) { $candidates += $env:CSC }
    $candidates += (Join-Path $Root ".tools\roslyn\tasks\net472\csc.exe")
    $candidates += (Join-Path $Root "..\tools\roslyn\tasks\net472\csc.exe")

    foreach ($item in $candidates) {
        if ($item -and (Test-Path -LiteralPath $item)) { return (Resolve-Path -LiteralPath $item).Path }
    }

    return $null
}

function Install-Compiler([string]$Root, [string]$Version = "4.14.0") {
    $target = Join-Path $Root ".tools\roslyn"
    New-Item -ItemType Directory -Force -Path $target | Out-Null

    $url = "https://api.nuget.org/v3-flatcontainer/microsoft.net.compilers.toolset/$Version/microsoft.net.compilers.toolset.$Version.nupkg"
    $temp = Join-Path $env:TEMP "roslyn-$Version.zip"

    Write-Host "Downloading Roslyn $Version..."
    Invoke-WebRequest -Uri $url -OutFile $temp -UseBasicParsing
    Expand-Archive -LiteralPath $temp -DestinationPath $target -Force
    Remove-Item -LiteralPath $temp -Force

    $compiler = Join-Path $target "tasks\net472\csc.exe"
    if (-not (Test-Path -LiteralPath $compiler)) { Fail "Compiler not found after download: $compiler" }
    return $compiler
}

function Get-ModVersion([string]$Root, [string]$ModName) {
    $file = Join-Path $Root "GameData\$ModName\$ModName.version"
    if (-not (Test-Path -LiteralPath $file)) { Fail "Version file not found: $file" }
    $json = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    return "$($json.VERSION.MAJOR).$($json.VERSION.MINOR).$($json.VERSION.PATCH).$($json.VERSION.BUILD)"
}

function Assert-VersionMatch([string]$Root, [string]$Version) {
    $assembly = Join-Path $Root "src\AssemblyInfo.cs"
    $text = Get-Content -LiteralPath $assembly -Raw
    if ($text -notmatch 'AssemblyVersion\("([0-9]+(?:\.[0-9]+){1,3})"\)') {
        Fail "AssemblyVersion not found in $assembly"
    }
    $declared = $Matches[1]
    if ($declared -ne $Version) {
        Fail "Version mismatch: .version is $Version, AssemblyInfo is $declared"
    }
}
