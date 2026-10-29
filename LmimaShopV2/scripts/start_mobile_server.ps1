#Requires -Version 5.1
<#
.SYNOPSIS
    Development-only static file server for the LmimaShop V2 mobile PWA.

.DESCRIPTION
    Serves the "mobile" folder over HTTP on port 8091 so the PWA can be tested
    on a phone while developing.

    FOR DEVELOPMENT/TESTING ONLY.
    In production the desktop application (port 8090) serves the same "mobile"
    folder through its embedded server. This script has no authentication and
    must not be exposed to untrusted networks.

.PARAMETER Port
    TCP port to listen on. Default: 8091.

.PARAMETER BindAddress
    HttpListener bind address. Default "+" (all interfaces). Binding to "+"
    may require an elevated shell or a URL ACL:
      netsh http add urlacl url=http://+:8091/ user=%USERNAME%
    Use "-BindAddress localhost" to avoid the elevation requirement.

.EXAMPLE
    .\scripts\start_mobile_server.ps1

.EXAMPLE
    .\scripts\start_mobile_server.ps1 -Port 8091 -BindAddress localhost
#>
[CmdletBinding()]
param(
    [int]$Port = 8091,
    [string]$BindAddress = '+'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$v2Root = Split-Path -Parent $PSScriptRoot
$mobileRoot = [System.IO.Path]::GetFullPath((Join-Path $v2Root 'mobile')).TrimEnd('\')

if (-not (Test-Path -LiteralPath $mobileRoot -PathType Container)) {
    Write-Host "Mobile folder not found: $mobileRoot" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path -LiteralPath (Join-Path $mobileRoot 'index.html') -PathType Leaf)) {
    Write-Warning "mobile\index.html was not found; the root URL will return 404."
}

$contentTypes = @{
    '.html'        = 'text/html; charset=utf-8'
    '.htm'         = 'text/html; charset=utf-8'
    '.css'         = 'text/css; charset=utf-8'
    '.js'          = 'application/javascript; charset=utf-8'
    '.mjs'         = 'application/javascript; charset=utf-8'
    '.json'        = 'application/json; charset=utf-8'
    '.webmanifest' = 'application/manifest+json; charset=utf-8'
    '.txt'         = 'text/plain; charset=utf-8'
    '.map'         = 'application/json; charset=utf-8'
    '.png'         = 'image/png'
    '.jpg'         = 'image/jpeg'
    '.jpeg'        = 'image/jpeg'
    '.gif'         = 'image/gif'
    '.webp'        = 'image/webp'
    '.svg'         = 'image/svg+xml'
    '.ico'         = 'image/x-icon'
    '.woff'        = 'font/woff'
    '.woff2'       = 'font/woff2'
    '.ttf'         = 'font/ttf'
    '.otf'         = 'font/otf'
    '.wasm'        = 'application/wasm'
    '.mp3'         = 'audio/mpeg'
    '.mp4'         = 'video/mp4'
}

function Resolve-StaticPath {
    <#
        Maps a URL path to a file under $mobileRoot.
        Returns a PSCustomObject with StatusCode and FullPath.
        Rejects traversal attempts (403) and missing files (404).
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$UrlPath
    )

    $decoded = ''
    try {
        $decoded = [System.Uri]::UnescapeDataString($UrlPath)
    }
    catch {
        return [pscustomobject]@{ StatusCode = 400; FullPath = $null }
    }

    if ([string]::IsNullOrEmpty($decoded)) {
        $decoded = '/'
    }

    # Reject null bytes and any '..' path segment (encoded or not).
    if ($decoded.IndexOf([char]0) -ge 0) {
        return [pscustomobject]@{ StatusCode = 403; FullPath = $null }
    }
    if ($decoded -match '(^|[\\/])\.\.([\\/]|$)') {
        return [pscustomobject]@{ StatusCode = 403; FullPath = $null }
    }

    $relative = $decoded.Replace('\', '/').TrimStart('/')
    if ($relative -eq '') {
        $relative = 'index.html'
    }
    elseif ($decoded.EndsWith('/')) {
        $relative = $relative.TrimEnd('/') + '/index.html'
    }

    if ([System.IO.Path]::IsPathRooted($relative) -or $relative -match ':') {
        return [pscustomobject]@{ StatusCode = 403; FullPath = $null }
    }

    $combined = [System.IO.Path]::Combine($mobileRoot, $relative.Replace('/', '\'))
    $candidate = [System.IO.Path]::GetFullPath($combined)
    $rootPrefix = $mobileRoot.TrimEnd('\') + '\'

    if (-not $candidate.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{ StatusCode = 403; FullPath = $null }
    }

    if ([System.IO.Directory]::Exists($candidate)) {
        $candidate = [System.IO.Path]::GetFullPath((Join-Path $candidate 'index.html'))
        if (-not $candidate.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return [pscustomobject]@{ StatusCode = 403; FullPath = $null }
        }
    }

    if (-not [System.IO.File]::Exists($candidate)) {
        return [pscustomobject]@{ StatusCode = 404; FullPath = $null }
    }

    return [pscustomobject]@{ StatusCode = 200; FullPath = $candidate }
}

function Get-MimeType {
    param([string]$Extension)

    $key = $Extension.ToLowerInvariant()
    if ($script:contentTypes.ContainsKey($key)) {
        return $script:contentTypes[$key]
    }
    return 'application/octet-stream'
}

function Write-TextResponse {
    param(
        [Parameter(Mandatory = $true)] [System.Net.HttpListenerResponse]$Response,
        [Parameter(Mandatory = $true)] [int]$StatusCode,
        [Parameter(Mandatory = $true)] [string]$Body,
        [bool]$HeadOnly = $false
    )

    $Response.StatusCode = $StatusCode
    $Response.ContentType = 'text/plain; charset=utf-8'
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Body)
    $Response.ContentLength64 = $bytes.Length
    if (-not $HeadOnly) {
        $Response.OutputStream.Write($bytes, 0, $bytes.Length)
    }
}

$prefix = "http://${BindAddress}:${Port}/"
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add($prefix)

try {
    $listener.Start()
}
catch [System.Net.HttpListenerException] {
    Write-Host @"
Failed to start the development server on $prefix
Reason: $($_.Exception.Message)

The port may already be in use, or this shell may lack permission to bind it.

Try one of the following:
  - Choose another port:
      .\scripts\start_mobile_server.ps1 -Port 8092
  - Bind to localhost only:
      .\scripts\start_mobile_server.ps1 -BindAddress localhost
  - Run PowerShell as Administrator, or register a URL ACL:
      netsh http add urlacl url=$prefix user=$env:USERNAME
"@ -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'LmimaShop V2 - mobile development server' -ForegroundColor Cyan
Write-Host 'FOR DEVELOPMENT/TESTING ONLY - no authentication, no TLS.' -ForegroundColor Yellow
Write-Host "Serving : $mobileRoot"
Write-Host "URL     : $prefix"
Write-Host 'Press Ctrl+C to stop.'
Write-Host ''

try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        $request = $context.Request
        $response = $context.Response

        try {
            # Development CORS policy: allow any origin so the PWA can call the
            # desktop API on port 8090 during local testing.
            $response.Headers['Access-Control-Allow-Origin'] = '*'
            $response.Headers['Access-Control-Allow-Methods'] = 'GET, HEAD, OPTIONS'
            $response.Headers['Access-Control-Allow-Headers'] = 'Content-Type, Authorization'
            $response.Headers['Access-Control-Max-Age'] = '600'
            $response.Headers['Cache-Control'] = 'no-store'
            $response.Headers['X-Content-Type-Options'] = 'nosniff'

            if ($request.HttpMethod -eq 'OPTIONS') {
                $response.StatusCode = 204
                $response.ContentLength64 = 0
            }
            elseif ($request.HttpMethod -eq 'GET' -or $request.HttpMethod -eq 'HEAD') {
                $isHead = $request.HttpMethod -eq 'HEAD'
                $resolved = Resolve-StaticPath -UrlPath $request.Url.AbsolutePath

                if ($resolved.StatusCode -ne 200) {
                    $message = if ($resolved.StatusCode -eq 404) { 'File not found.' } else { 'Forbidden.' }
                    Write-TextResponse -Response $response -StatusCode $resolved.StatusCode -Body $message -HeadOnly $isHead
                    Write-Host ("{0} {1} -> {2}" -f $request.HttpMethod, $request.Url.AbsolutePath, $resolved.StatusCode)
                }
                else {
                    $fileInfo = [System.IO.FileInfo]::new($resolved.FullPath)
                    $response.StatusCode = 200
                    $response.ContentType = Get-MimeType -Extension $fileInfo.Extension
                    $response.ContentLength64 = $fileInfo.Length

                    if (-not $isHead) {
                        $fileStream = [System.IO.File]::OpenRead($fileInfo.FullName)
                        try {
                            $fileStream.CopyTo($response.OutputStream)
                        }
                        finally {
                            $fileStream.Dispose()
                        }
                    }
                    Write-Host ("{0} {1} -> 200" -f $request.HttpMethod, $request.Url.AbsolutePath)
                }
            }
            else {
                $response.Headers['Allow'] = 'GET, HEAD, OPTIONS'
                Write-TextResponse -Response $response -StatusCode 405 -Body 'Method not allowed.'
                Write-Host ("{0} {1} -> 405" -f $request.HttpMethod, $request.Url.AbsolutePath)
            }
        }
        catch {
            Write-Warning "Request failed: $($_.Exception.Message)"
            try {
                if ($response.OutputStream.CanWrite) {
                    Write-TextResponse -Response $response -StatusCode 500 -Body 'Internal server error.'
                }
            }
            catch {
                # Response may already be partially sent; nothing else to do.
            }
        }
        finally {
            try {
                $response.Close()
            }
            catch {
                # Ignore close errors for aborted client connections.
            }
        }
    }
}
finally {
    if ($listener.IsListening) {
        $listener.Stop()
    }
    $listener.Close()
    Write-Host 'Development server stopped.'
}
