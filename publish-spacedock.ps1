#Requires -Version 5.1
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Tag,
    [string]$ModId = $env:SPACEDOCK_MOD_ID
)

$ErrorActionPreference = "Stop"
if ($ModId -notmatch '^[0-9]+$') { throw "Set SPACEDOCK_MOD_ID to a numeric mod ID." }
if (-not $env:SPACEDOCK_USERNAME -or -not $env:SPACEDOCK_PASSWORD) {
    throw "Set SPACEDOCK_USERNAME and SPACEDOCK_PASSWORD secrets."
}

Add-Type -AssemblyName System.Net.Http
Add-Type -AssemblyName System.IO.Compression.FileSystem
$client = New-Object Net.Http.HttpClient
$client.Timeout = [timespan]::FromMinutes(2)
$client.DefaultRequestHeaders.UserAgent.ParseAdd('Glissade3D-release')
$temp = Join-Path ([IO.Path]::GetTempPath()) ([guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $temp | Out-Null

function Invoke-SpaceDock([string]$Method, [string]$Path, [Net.Http.HttpContent]$Content) {
    $request = New-Object Net.Http.HttpRequestMessage($Method, "https://spacedock.info$Path")
    $request.Content = $Content
    $response = $null
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        if (-not $response.IsSuccessStatusCode) { throw "SpaceDock $Path failed: HTTP $([int]$response.StatusCode)." }
        $result = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        if ($result.error) { throw "SpaceDock $Path returned an API error." }
        return $result
    } finally {
        if ($response) { $response.Dispose() }
        $request.Dispose()
    }
}

try {
    $releaseJson = & gh release view $Tag --repo stone-w4tch3r/glissade3d --json tagName,isDraft,isPrerelease,body,assets
    if ($LASTEXITCODE -ne 0) { throw "Cannot read GitHub release $Tag." }
    $release = $releaseJson | ConvertFrom-Json
    if ($release.isDraft -or $release.isPrerelease -or $release.tagName -cne $Tag) {
        throw "SpaceDock mirrors published stable releases only."
    }
    if ([string]::IsNullOrWhiteSpace($release.body)) { throw "GitHub release notes are empty." }
    $version = $Tag.Substring(1)
    $assetName = "Glissade3D-$version.zip"
    $assets = @($release.assets | Where-Object { $_.name -ceq $assetName })
    if ($assets.Count -ne 1) { throw "Expected exactly one GitHub asset named $assetName." }
    & gh release download $Tag --repo stone-w4tch3r/glissade3d --pattern $assetName --dir $temp
    if ($LASTEXITCODE -ne 0) { throw "Cannot download GitHub release ZIP." }
    $zipPath = Join-Path $temp $assetName
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($assets[0].digest -cne "sha256:$hash") { throw "GitHub ZIP checksum mismatch." }
    $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($zip.Entries | Where-Object { $_.FullName -ceq 'GameData/Glissade3D/Glissade3D.version' })
        if ($entries.Count -ne 1) { throw "ZIP must contain exactly one Glissade3D.version file." }
        $reader = New-Object IO.StreamReader($entries[0].Open())
        try { $info = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        $v = $info.VERSION
        if ("$($v.MAJOR).$($v.MINOR).$($v.PATCH).$($v.BUILD)" -cne $version) { throw "ZIP version differs from release tag." }
        $ksp = $info.KSP_VERSION
        $gameVersion = "$($ksp.MAJOR).$($ksp.MINOR).$($ksp.PATCH)"
        if ($gameVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "ZIP must specify exact KSP compatibility." }
    } finally { $zip.Dispose() }

    $mod = Invoke-SpaceDock 'GET' "/api/mod/$ModId" $null
    if ($mod.name -cne 'Glissade3D' -or $mod.game -cne 'Kerbal Space Program' -or
        $mod.source_code.TrimEnd('/') -cne 'https://github.com/stone-w4tch3r/glissade3d') {
        throw "SpaceDock mod ID does not identify this KSP mod and GitHub source."
    }
    $login = New-Object Net.Http.MultipartFormDataContent
    $login.Add((New-Object Net.Http.StringContent($env:SPACEDOCK_USERNAME)), 'username')
    $login.Add((New-Object Net.Http.StringContent($env:SPACEDOCK_PASSWORD)), 'password')
    Invoke-SpaceDock 'POST' '/api/login' $login | Out-Null
    if ($mod.author -ine $env:SPACEDOCK_USERNAME -and @($mod.shared_authors.username) -inotcontains $env:SPACEDOCK_USERNAME) {
        throw "SpaceDock account is not an author of this mod."
    }
    Write-Host "SpaceDock authentication passed. GitHub ZIP checksum verified."

    $existing = @($mod.versions | Where-Object { $_.friendly_version -ceq $version })
    if ($existing.Count -gt 1) { throw "SpaceDock contains duplicate version labels." }
    if ($existing.Count -eq 1) {
        if ($existing[0].game_version -cne $gameVersion) { throw "Existing SpaceDock version has different KSP compatibility." }
        $download = [uri]::new([uri]'https://spacedock.info', [string]$existing[0].download_path)
        if ($download.Scheme -cne 'https' -or $download.Host -cne 'spacedock.info') { throw "Unexpected SpaceDock download URL." }
        $bytes = $client.GetByteArrayAsync($download).GetAwaiter().GetResult()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $remoteHash = [BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() } finally { $sha.Dispose() }
        if ($remoteHash -cne $hash) { throw "Existing SpaceDock ZIP differs from GitHub. Refusing to replace it." }
        Write-Host "SpaceDock $version already matches GitHub. No upload needed."
        return
    }

    $form = New-Object Net.Http.MultipartFormDataContent
    $form.Add((New-Object Net.Http.StringContent($version)), 'version')
    $form.Add((New-Object Net.Http.StringContent($gameVersion)), 'game-version')
    $form.Add((New-Object Net.Http.StringContent($release.body)), 'changelog')
    $form.Add((New-Object Net.Http.StringContent('yes')), 'notify-followers')
    $form.Add((New-Object Net.Http.StreamContent([IO.File]::OpenRead($zipPath))), 'zipball', $assetName)
    $uploaded = Invoke-SpaceDock 'POST' "/api/mod/$ModId/update" $form
    if (-not $uploaded.id) { throw "SpaceDock did not confirm a new version ID." }
    Write-Host "Published SpaceDock $version for KSP $gameVersion (version ID $($uploaded.id))."
} finally {
    $client.Dispose()
    Remove-Item -LiteralPath $temp -Recurse -Force
}
