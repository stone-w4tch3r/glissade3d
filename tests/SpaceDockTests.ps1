#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$script = Join-Path (Split-Path $PSScriptRoot -Parent) 'publish-spacedock.ps1'
function Assert-Rejected([string]$Tag, [string]$ModId, [string]$Expected) {
    try { & $script -Tag $Tag -ModId $ModId } catch {
        if ($_.Exception.Message -notlike "*$Expected*") { throw }
        return
    }
    throw "Expected failure: $Expected"
}
Assert-Rejected 'main' '4847' 'does not match'
Assert-Rejected 'v0.3.5.2' '../4847' 'numeric mod ID'
$username = $env:SPACEDOCK_USERNAME
$password = $env:SPACEDOCK_PASSWORD
try {
    $env:SPACEDOCK_USERNAME = ''
    $env:SPACEDOCK_PASSWORD = ''
    Assert-Rejected 'v0.3.5.2' '4847' 'secrets'
} finally {
    $env:SPACEDOCK_USERNAME = $username
    $env:SPACEDOCK_PASSWORD = $password
}
Write-Host 'OK: SpaceDock input and missing-credential checks passed without network requests.'
