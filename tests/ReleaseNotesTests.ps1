#Requires -Version 5.1
$ErrorActionPreference = "Stop"
. (Join-Path (Split-Path $PSScriptRoot -Parent) "release-notes.ps1")

function Assert-Equal($Actual, $Expected) {
    if ($Actual -cne $Expected) { throw "Expected '$Expected'; got '$Actual'." }
}

function Assert-Rejected([string]$Text, [string]$Version, [string]$Message) {
    try { Get-ReleaseNotes $Text $Version | Out-Null } catch {
        if ($_.Exception.Message -notlike "*$Message*") { throw }
        return
    }
    throw "Expected rejection: $Message"
}

$section = "## [0.3.3.0] - 2026-11-17`n`n### Added`n`n- Gates.`n"
$expected = "### Added`n`n- Gates.`n"
$text = "## [Unreleased]`n`n- Pending.`n`n" + $section + "`n## [0.3.2.0] - 2026-11-16`n`n- Old.`n"
Assert-Equal (Get-ReleaseNotes $text "v0.3.3.0") $expected
Assert-Equal (Get-ReleaseNotes $section.Replace("`n", "`r`n") "0.3.3.0") $expected
Assert-Rejected "## [Unreleased]`n- Pending.`n" "0.3.3.0" "found 0"
Assert-Rejected ($section + $section) "0.3.3.0" "found 2"
Assert-Rejected "## [0.3.3.0] - 2026-11-17`n`n### Added`n" "0.3.3.0" "at least one changelog bullet"
Assert-Rejected ($section.Replace("2026-11-17", "2026-02-30")) "0.3.3.0" "valid YYYY-MM-DD"
Assert-Rejected ($section.Replace(" - 2026-11-17", "")) "0.3.3.0" "valid YYYY-MM-DD"
Assert-Rejected $section "0.3.3" "four-part release version"
Assert-Equal (Get-ReleaseNotes ("## [Unreleased]`n`n" + $section) "Unreleased") ""
Assert-Equal (Get-ReleaseNotes $text "Unreleased") "- Pending.`n"

# Check CLI output encoding and ensure a dated release's text survives file writing.
$temp = [IO.Path]::GetTempFileName()
try {
    & (Join-Path (Split-Path $PSScriptRoot -Parent) "release-notes.ps1") -Version v0.3.3.0 -Output $temp
    $bytes = [IO.File]::ReadAllBytes($temp)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) {
        throw "Release notes must use UTF-8 without BOM."
    }
    Assert-Equal ([IO.File]::ReadAllText($temp)) (Get-ReleaseNotes (Get-Content -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) "CHANGELOG.md") -Raw -Encoding UTF8) "v0.3.3.0")
} finally {
    Remove-Item -LiteralPath $temp
}
Write-Host "OK: all release note checks passed on PowerShell $($PSVersionTable.PSVersion)."
