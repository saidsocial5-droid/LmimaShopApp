#Requires -Version 5.1
<#
.SYNOPSIS
    Builds (if needed) and starts the LmimaShop V2 desktop application.

.DESCRIPTION
    Uses only paths relative to this script. If the Release executable does not
    exist, the script runs `dotnet build -c Release` first, then launches the app.

.PARAMETER ForceBuild
    Rebuild the Release configuration even if the executable already exists.

.EXAMPLE
    .\scripts\start_desktop.ps1

.EXAMPLE
    .\scripts\start_desktop.ps1 -ForceBuild
#>
[CmdletBinding()]
param(
    [switch]$ForceBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$v2Root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $v2Root 'desktop\LmimaShopV2.Desktop.csproj'
$exePath = Join-Path $v2Root 'desktop\bin\Release\net10.0-windows\LmimaShopV2.Desktop.exe'

try {
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Desktop project not found: $projectPath"
    }

    $needsBuild = $ForceBuild -or -not (Test-Path -LiteralPath $exePath -PathType Leaf)

    if ($needsBuild) {
        $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
        if (-not $dotnetCommand) {
            throw '.NET SDK not found. Install the .NET 10 SDK and make sure "dotnet" is on PATH.'
        }

        Write-Host "Building LmimaShop V2 (Release)..." -ForegroundColor Cyan
        & $dotnetCommand.Source build $projectPath -c Release
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }
    }

    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
        throw "Build finished but the executable was not found: $exePath"
    }

    Write-Host "Starting LmimaShop V2..." -ForegroundColor Green
    Start-Process -FilePath $exePath -WorkingDirectory (Split-Path -Parent $exePath) | Out-Null
}
catch {
    Write-Host "Failed to start LmimaShop V2: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
