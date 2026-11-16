[CmdletBinding()]
param(
    [string]$Shared
)

$ErrorActionPreference = "Stop"
if (-not $Shared) {
    $here = Split-Path -Parent $PSCommandPath
    $Shared = Join-Path (Split-Path -Parent $here) "shared"
}
$root = (Resolve-Path -LiteralPath $Shared).Path
$files = Get-ChildItem -LiteralPath $root -Recurse -Filter *.cfg |
    Where-Object { $_.Name -ne "index.cfg" } |
    Sort-Object FullName

if ($files.Count -eq 0) { throw "No route files in $root" }

$builder = New-Object System.Text.StringBuilder
[void]$builder.AppendLine("SharedRoutes")
[void]$builder.AppendLine("{")
[void]$builder.AppendLine("    version = 1")

foreach ($file in $files) {
    $text = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
    $id = [regex]::Match($text, '(?m)^\s*id\s*=\s*(\S+)').Groups[1].Value
    $name = [regex]::Match($text, '(?m)^\s*name\s*=\s*(.+)$').Groups[1].Value.Trim()
    if (-not $id -or -not $name) { throw "Route file without id/name: $($file.FullName)" }

    $relative = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()

    [void]$builder.AppendLine("    Entry")
    [void]$builder.AppendLine("    {")
    [void]$builder.AppendLine("        id = $id")
    [void]$builder.AppendLine("        name = $name")
    [void]$builder.AppendLine("        file = $relative")
    [void]$builder.AppendLine("        sha256 = $hash")
    [void]$builder.AppendLine("    }")
}

[void]$builder.AppendLine("}")
$target = Join-Path $root "index.cfg"
[System.IO.File]::WriteAllText($target, $builder.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Wrote $target with $($files.Count) entries"
