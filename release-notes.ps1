#Requires -Version 5.1
param([string]$Version, [string]$Output)

$ErrorActionPreference = "Stop"

function Get-ReleaseNotes([string]$Text, [string]$Version) {
    $Version = $Version -creplace '^v', ''
    if ($Version -cne "Unreleased" -and $Version -cnotmatch '^\d+\.\d+\.\d+\.\d+$') {
        throw "Expected Unreleased or a four-part release version, optionally prefixed with v."
    }
    $Text = $Text.Replace("`r`n", "`n")
    $headings = [regex]::Matches($Text, '(?m)^## .*$')
    $sections = @()
    for ($index = 0; $index -lt $headings.Count; $index++) {
        $heading = $headings[$index]
        $match = [regex]::Match($heading.Value, '^## \[([^\]]+)\](?: - (\S+))?[ \t]*$')
        if ($match.Success -and $match.Groups[1].Value -ceq $Version) {
            $start = $heading.Index + $heading.Length
            $end = if ($index + 1 -lt $headings.Count) { $headings[$index + 1].Index } else { $Text.Length }
            $sections += @{ Date = $match.Groups[2].Value; Body = $Text.Substring($start, $end - $start).Trim() }
        }
    }
    if ($sections.Count -ne 1) {
        throw "Expected exactly one changelog section for $Version; found $($sections.Count)."
    }
    $section = $sections[0]
    if ($Version -cne "Unreleased") {
        $parsed = [datetime]::MinValue
        if (-not [datetime]::TryParseExact($section.Date, 'yyyy-MM-dd',
                [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsed)) {
            throw "Section $Version needs a valid YYYY-MM-DD release date."
        }
        if ($section.Body -cnotmatch '(?m)^- \S') {
            throw "Section $Version needs at least one changelog bullet."
        }
    }
    if ($section.Body) { return $section.Body + "`n" }
    return ""
}

# Dot-source this script to test the same extractor used by the command line.
if ($MyInvocation.InvocationName -ne '.') {
    $notes = Get-ReleaseNotes (Get-Content -LiteralPath (Join-Path $PSScriptRoot "CHANGELOG.md") -Raw -Encoding UTF8) $Version
    if ($Output) {
        $path = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
        [IO.File]::WriteAllText($path, $notes, (New-Object Text.UTF8Encoding($false)))
    } else {
        [Console]::Out.Write($notes)
    }
}
