#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes a self-contained Release build of LmimaShop V2.

.DESCRIPTION
    Runs dotnet publish with:
        -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
    then copies mobile\ next to the executable so the embedded server can serve
    the PWA.

    Two locations are produced from the same build:
      - desktop\bin\<Configuration>\net10.0-windows\publish\
        (consumed by installer\LmimaShopV2.iss)
      - dist\publish\ (the distributable copy requested by the release process)

.PARAMETER Configuration
    Build configuration. Default: Release (required by the installer).

.PARAMETER Runtime
    .NET runtime identifier. Default: win-x64.

.PARAMETER OutputPath
    Destination folder. Default: <V2>\dist\publish.

.PARAMETER Force
    Required to clean an output folder outside dist\.

.EXAMPLE
    .\scripts\publish.ps1

.EXAMPLE
    .\scripts\publish.ps1 -OutputPath D:\build\LmimaShopV2 -Force
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$OutputPath,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$v2Root = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\')
$projectPath = Join-Path $v2Root 'desktop\LmimaShopV2.Desktop.csproj'
$mobilePath = Join-Path $v2Root 'mobile'

if (-not $OutputPath) {
    $OutputPath = Join-Path $v2Root 'dist\publish'
}

try {
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "Desktop project not found: $projectPath"
    }
    if (-not (Test-Path -LiteralPath $mobilePath -PathType Container)) {
        throw "Mobile folder not found: $mobilePath"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $mobilePath 'index.html') -PathType Leaf)) {
        throw "mobile\index.html not found in: $mobilePath"
    }

    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnetCommand) {
        throw '.NET SDK not found. Install the .NET 10 SDK and make sure "dotnet" is on PATH.'
    }

    if ($Configuration -notmatch '^[A-Za-z0-9_.-]+$') {
        throw "Invalid configuration name: $Configuration"
    }

    $outputRaw = [System.IO.Path]::GetFullPath($OutputPath)
    $outputFull = $outputRaw.TrimEnd('\')
    if ([string]::IsNullOrWhiteSpace($outputFull) -or $outputFull -match '^[A-Za-z]:$') {
        throw "Refusing to use a drive root as the publish output: $outputRaw"
    }
    if ($outputFull -eq $v2Root) {
        throw 'Refusing to publish into the LmimaShopV2 root folder. Use dist\publish or a dedicated folder.'
    }

    # Never delete source folders, even with -Force.
    $protectedFolders = @('desktop', 'mobile', 'db', 'docs', 'scripts', 'installer', 'templates', '.git', '.vs')
    foreach ($folder in $protectedFolders) {
        $protectedPath = [System.IO.Path]::GetFullPath((Join-Path $v2Root $folder)).TrimEnd('\')
        if ($outputFull -eq $protectedPath) {
            throw "Refusing to publish into a project source folder: $outputFull"
        }
    }
    if ($v2Root.StartsWith($outputFull + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to publish into an ancestor of the project: $outputFull"
    }

    $distRoot = [System.IO.Path]::GetFullPath((Join-Path $v2Root 'dist')).TrimEnd('\')
    $insideDist = $outputFull.StartsWith($distRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)
    if (-not $insideDist -and -not $Force) {
        throw "Output folder is outside dist\: $outputFull. Pass -Force if this is intentional."
    }

    $stageFull = [System.IO.Path]::GetFullPath((Join-Path $v2Root "desktop\bin\$Configuration\net10.0-windows\publish")).TrimEnd('\')
    $sameFolder = $outputFull -eq $stageFull

    if (Test-Path -LiteralPath $stageFull) {
        Write-Host "Cleaning previous staged publish: $stageFull"
        Remove-Item -LiteralPath $stageFull -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stageFull -Force | Out-Null

    if (-not $sameFolder) {
        if (Test-Path -LiteralPath $outputFull) {
            Write-Host "Cleaning previous output: $outputFull"
            Remove-Item -LiteralPath $outputFull -Recurse -Force
        }
        New-Item -ItemType Directory -Path $outputFull -Force | Out-Null
    }

    Write-Host "Publishing LmimaShop V2 ($Configuration / $Runtime)..." -ForegroundColor Cyan
    $publishArgs = @(
        'publish',
        $projectPath,
        '-c', $Configuration,
        '-r', $Runtime,
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        '-o', $stageFull
    )

    & $dotnetCommand.Source @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $exePath = Join-Path $stageFull 'LmimaShopV2.Desktop.exe'
    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
        throw "Publish completed but the executable was not found: $exePath. Check the project AssemblyName."
    }

    # The embedded server serves mobile\ from the application folder.
    $stagedMobile = Join-Path $stageFull 'mobile'
    New-Item -ItemType Directory -Path $stagedMobile -Force | Out-Null
    Copy-Item -Path (Join-Path $mobilePath '*') -Destination $stagedMobile -Recurse -Force

    if (-not (Test-Path -LiteralPath (Join-Path $stagedMobile 'index.html') -PathType Leaf)) {
        throw "Failed to copy the mobile PWA into the staged publish folder: $stagedMobile"
    }

    if (-not $sameFolder) {
        Write-Host "Copying the distributable build to: $outputFull"
        Copy-Item -Path (Join-Path $stageFull '*') -Destination $outputFull -Recurse -Force

        $finalExe = Join-Path $outputFull 'LmimaShopV2.Desktop.exe'
        $finalMobile = Join-Path $outputFull 'mobile\index.html'
        if (-not (Test-Path -LiteralPath $finalExe -PathType Leaf)) {
            throw "Failed to copy the executable into the output folder: $finalExe"
        }
        if (-not (Test-Path -LiteralPath $finalMobile -PathType Leaf)) {
            throw "Failed to copy the mobile PWA into the output folder: $finalMobile"
        }
    }
    else {
        Write-Host 'OutputPath equals the staged publish folder; skipping the dist copy.'
    }

    $exeSizeMb = [math]::Round((Get-Item -LiteralPath $exePath).Length / 1MB, 2)
    Write-Host ''
    Write-Host 'Publish completed successfully.' -ForegroundColor Green
    Write-Host "Staged : $stageFull"
    Write-Host "Dist   : $outputFull"
    Write-Host "Exe    : $exePath ($exeSizeMb MB)"
    Write-Host ''
    if ($Configuration -ne 'Release') {
        Write-Warning 'The installer expects Configuration=Release; rebuild with -Configuration Release before packaging.'
    }
    Write-Host 'Next step (optional): build the installer with'
    Write-Host '    iscc installer\LmimaShopV2.iss'

    exit 0
}
catch {
    Write-Host ''
    Write-Host "Publish failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
