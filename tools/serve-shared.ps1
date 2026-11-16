[CmdletBinding()]
param(
    [string]$Shared,
    [int]$Port = 8765
)

$ErrorActionPreference = "Stop"
if (-not $Shared) {
    $here = Split-Path -Parent $PSCommandPath
    $Shared = Join-Path (Split-Path -Parent $here) "shared"
}
$root = (Resolve-Path -LiteralPath $Shared).Path

$listener = New-Object System.Net.Sockets.TcpListener -ArgumentList ([System.Net.IPAddress]::Loopback), $Port
$listener.Start()
Write-Host "Serving $root at http://127.0.0.1:$Port/"
Write-Host "Press Ctrl+C to stop."

try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $stream = $client.GetStream()
            $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::ASCII, $false, 1024, $true)
            $requestLine = $reader.ReadLine()
            if (-not [string]::IsNullOrEmpty($requestLine)) {
                while ($true) {
                    $line = $reader.ReadLine()
                    if ($null -eq $line -or $line -eq "") { break }
                }

                $parts = $requestLine.Split(' ')
                $status = "404 Not Found"
                $body = [System.Text.Encoding]::ASCII.GetBytes("not found")

                if ($parts.Length -ge 2) {
                    $relative = [System.Uri]::UnescapeDataString($parts[1].Split('?')[0]).TrimStart('/')
                    $full = [System.IO.Path]::GetFullPath((Join-Path $root $relative))
                    if ($full.StartsWith($root, [System.StringComparison]::Ordinal) -and (Test-Path -LiteralPath $full -PathType Leaf)) {
                        $body = [System.IO.File]::ReadAllBytes($full)
                        $status = "200 OK"
                    }
                }

                $header = [System.Text.Encoding]::ASCII.GetBytes(
                    "HTTP/1.1 $status`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n")
                $stream.Write($header, 0, $header.Length)
                $stream.Write($body, 0, $body.Length)
                $stream.Flush()
                Write-Host "$status $($parts[1])"
            }
        }
        finally {
            $client.Close()
        }
    }
}
finally {
    $listener.Stop()
}
