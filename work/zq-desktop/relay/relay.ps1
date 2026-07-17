# ZhuaQian Desktop - minimal self-hosted relay (zero-knowledge: only forwards .zqp ciphertext)
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File relay.ps1 [-Port 8080] [-HostPrefix +] [-Dir blobs]
#
# Endpoints:
#   POST /upload      body = raw .zqp bytes -> returns plain-text id
#   GET  /<id>         returns the bytes (Import from URL reuses this)
#   POST /session/<id> body = raw snapshot bytes -> stores session
#   GET  /session/<id> returns the latest session snapshot bytes
#   GET  /health       returns ok
#
# Note: HttpListener binding to non-localhost needs a URL ACL reservation (run once as admin):
#   netsh http add urlacl url=http://+:8080/ user=Everyone
# Then normal users can start it. For cross-internet, set up port forwarding / reverse proxy.

param(
    [string]$HostPrefix = '+',
    [int]$Port = 8080,
    [string]$Dir = 'blobs'
)

$ErrorActionPreference = 'Stop'
$base = 'http://' + $HostPrefix + ':' + $Port + '/'
$blobs = Join-Path $Pwd $Dir
New-Item -ItemType Directory -Force -Path $blobs | Out-Null

try {
    netsh http add urlacl url=$base user=Everyone 2>$null | Out-Null
} catch { }

Add-Type -AssemblyName System.Net

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add($base)
try {
    $listener.Start()
} catch {
    Write-Host ('Cannot start listener ' + $base + '. Try running once as admin: netsh http add urlacl url=' + $base + ' user=Everyone') -ForegroundColor Red
    Write-Host $_.Exception.Message
    exit 1
}

Write-Host ('ZhuaQian Relay running: ' + $base) -ForegroundColor Green
Write-Host ('Store dir: ' + $blobs) -ForegroundColor Gray
Write-Host 'Press Ctrl+C to stop.' -ForegroundColor Gray

function RandId($n) {
    $c = '0123456789abcdefghijklmnopqrstuvwxyz'
    $s = ''
    for ($i = 0; $i -lt $n; $i++) { $s += $c[(Get-Random -Minimum 0 -Maximum $c.Length)] }
    return $s
}

$disp = 'attachment; filename="zq-package.zqp"'

try {
    while ($true) {
        $ctx = $listener.GetContext()
        $req = $ctx.Request
        $resp = $ctx.Response
        try {
            $path = $req.Url.LocalPath.TrimStart('/')
            if ($path.StartsWith('session/') -and $path.Length -gt 8) {
                $sid = $path.Substring(8).Trim('/')
                $sessDir = Join-Path $blobs 'sessions'
                New-Item -ItemType Directory -Force -Path $sessDir | Out-Null
                $sfile = Join-Path $sessDir ($sid + '.bin')
                if ($req.HttpMethod -eq 'POST') {
                    $ms = New-Object System.IO.MemoryStream
                    $req.InputStream.CopyTo($ms)
                    [System.IO.File]::WriteAllBytes($sfile, $ms.ToArray())
                    $buf = [System.Text.Encoding]::UTF8.GetBytes('ok')
                    $resp.ContentType = 'text/plain'
                    $resp.OutputStream.Write($buf, 0, $buf.Length)
                }
                elseif ($req.HttpMethod -eq 'GET') {
                    if (Test-Path $sfile) {
                        $data = [System.IO.File]::ReadAllBytes($sfile)
                        $resp.ContentType = 'application/octet-stream'
                        $resp.OutputStream.Write($data, 0, $data.Length)
                    } else {
                        $resp.StatusCode = 404
                    }
                }
                else { $resp.StatusCode = 404 }
            }
            elseif ($req.HttpMethod -eq 'POST' -and $path -eq 'upload') {
                $id = RandId(16)
                $ms = New-Object System.IO.MemoryStream
                $req.InputStream.CopyTo($ms)
                [System.IO.File]::WriteAllBytes((Join-Path $blobs ($id + '.zqp')), $ms.ToArray())
                $buf = [System.Text.Encoding]::UTF8.GetBytes($id)
                $resp.ContentType = 'text/plain'
                $resp.OutputStream.Write($buf, 0, $buf.Length)
            }
            elseif ($req.HttpMethod -eq 'GET' -and $path -eq 'health') {
                $buf = [System.Text.Encoding]::UTF8.GetBytes('ok')
                $resp.OutputStream.Write($buf, 0, $buf.Length)
            }
            elseif ($req.HttpMethod -eq 'GET' -and $path.Length -gt 0 -and -not $path.Contains('/')) {
                $file = Join-Path $blobs ($path + '.zqp')
                if (Test-Path $file) {
                    $data = [System.IO.File]::ReadAllBytes($file)
                    $resp.ContentType = 'application/octet-stream'
                    $resp.AddHeader('Content-Disposition', $disp)
                    $resp.OutputStream.Write($data, 0, $data.Length)
                } else {
                    $resp.StatusCode = 404
                }
            }
            else {
                $resp.StatusCode = 404
            }
        }
        catch {
            try { $resp.StatusCode = 500 } catch { }
        }
        finally {
            $resp.Close()
        }
    }
}
finally {
    $listener.Stop()
    $listener.Close()
}
