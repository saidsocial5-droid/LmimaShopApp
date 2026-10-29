#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the LmimaShop V2 Android APK from the mobile PWA sources.

.DESCRIPTION
    Pipeline:
      1. Decode the reusable APK template (if an .apk was supplied).
      2. Copy mobile\ into a temporary staging folder (%TEMP%\LmimaShopV2Apk\assets).
      3. Replace the template assets with the staged PWA files.
      4. Rebuild the APK with apktool.
      5. Sign it with uber-apk-signer (debug signature).
      6. Copy the result to dist\LmimaShopV2.apk.

    Build tools are expected OUTSIDE this repository in ..\..\ApkBuilderTools
    (apktool.jar, uber-apk-signer.jar and optionally a bundled jre). Every path
    can be overridden with a parameter.

    The template file templates\LmimaShopV2Template.apk is NOT committed.
    See the error message printed when it is missing for one-time setup steps.

.PARAMETER ApkToolPath
    Path to apktool.jar. Default: <repo>\ApkBuilderTools\apktool.jar.

.PARAMETER SignerPath
    Path to uber-apk-signer.jar. Default: <repo>\ApkBuilderTools\uber-apk-signer.jar.

.PARAMETER JavaPath
    Path to java.exe. Default: bundled JRE in ApkBuilderTools\jre, else PATH.

.PARAMETER TemplatePath
    Template APK (or an already-decoded apktool folder).
    Default: <V2>\templates\LmimaShopV2Template.apk.

.PARAMETER OutputPath
    Final signed APK path. Default: <V2>\dist\LmimaShopV2.apk.

.PARAMETER MobilePath
    PWA source folder. Default: <V2>\mobile.

.PARAMETER KeepWork
    Keep the temporary work folder for debugging.

.EXAMPLE
    .\scripts\build_apk.ps1

.EXAMPLE
    .\scripts\build_apk.ps1 -TemplatePath D:\build\LmimaShopV2Template.apk -KeepWork
#>
[CmdletBinding()]
param(
    [string]$ApkToolPath,
    [string]$SignerPath,
    [string]$JavaPath,
    [string]$TemplatePath,
    [string]$OutputPath,
    [string]$MobilePath,
    [switch]$KeepWork
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$v2Root = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent $v2Root
$toolsRoot = Join-Path $repoRoot 'ApkBuilderTools'
$workRoot = Join-Path ([System.IO.Path]::GetTempPath()) 'LmimaShopV2Apk'

if (-not $MobilePath) {
    $MobilePath = Join-Path $v2Root 'mobile'
}
if (-not $TemplatePath) {
    $TemplatePath = Join-Path $v2Root 'templates\LmimaShopV2Template.apk'
}
if (-not $OutputPath) {
    $OutputPath = Join-Path $v2Root 'dist\LmimaShopV2.apk'
}
if (-not $ApkToolPath) {
    $ApkToolPath = Join-Path $toolsRoot 'apktool.jar'
}
if (-not $SignerPath) {
    $SignerPath = Join-Path $toolsRoot 'uber-apk-signer.jar'
}
if (-not $JavaPath) {
    $bundledJava = Join-Path $toolsRoot 'jre\bin\java.exe'
    if (Test-Path -LiteralPath $bundledJava -PathType Leaf) {
        $JavaPath = $bundledJava
    }
    else {
        $javaCommand = Get-Command -Name 'java.exe' -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($javaCommand) {
            $JavaPath = $javaCommand.Source
        }
    }
}

try {
    if (-not (Test-Path -LiteralPath $MobilePath -PathType Container)) {
        throw "Mobile source folder not found: $MobilePath"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $MobilePath 'index.html') -PathType Leaf)) {
        throw "mobile\index.html not found in: $MobilePath"
    }
    if (-not (Test-Path -LiteralPath $ApkToolPath -PathType Leaf)) {
        throw "apktool.jar not found: $ApkToolPath. Pass -ApkToolPath explicitly if it lives elsewhere."
    }
    if (-not (Test-Path -LiteralPath $SignerPath -PathType Leaf)) {
        throw "uber-apk-signer.jar not found: $SignerPath. Pass -SignerPath explicitly if it lives elsewhere."
    }
    if (-not $JavaPath -or -not (Test-Path -LiteralPath $JavaPath -PathType Leaf)) {
        throw @"
java.exe was not found.
Pass -JavaPath "C:\path\to\java.exe" or place a JRE in:
    $toolsRoot\jre
"@
    }
    if (-not (Test-Path -LiteralPath $TemplatePath)) {
        throw @"
APK template not found:
    $TemplatePath

Create it once (it is intentionally NOT stored in the repository):
  1. In Android Studio create an empty project with a single WebView activity
     that loads file:///android_asset/index.html. Enable JavaScript, DOM storage
     and android:usesCleartextTraffic="true" (the LAN server uses plain HTTP).
  2. Build a debug APK, then either copy it to:
         $TemplatePath
     or decode it into a reusable folder and pass that folder via -TemplatePath:
         & "$JavaPath" -jar "$ApkToolPath" d "<your-app>.apk" -o "$v2Root\templates\LmimaShopV2Template"
  3. Run this script again. It replaces the template assets with mobile\ and
     rebuilds + signs the APK.
"@
    }

    # --- Prepare a clean temporary work folder -------------------------------
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
    $workFull = [System.IO.Path]::GetFullPath($workRoot)
    if (-not $workFull.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a work folder outside the temporary directory: $workFull"
    }
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
    Write-Host "Work folder: $workRoot"

    # --- Stage mobile assets exactly as requested ----------------------------
    $stageAssets = Join-Path $workRoot 'assets'
    New-Item -ItemType Directory -Path $stageAssets -Force | Out-Null
    Copy-Item -Path (Join-Path $MobilePath '*') -Destination $stageAssets -Recurse -Force
    if (-not (Test-Path -LiteralPath (Join-Path $stageAssets 'index.html') -PathType Leaf)) {
        throw "Failed to stage mobile assets (index.html missing after copy)."
    }

    # --- Resolve/decode the template -----------------------------------------
    $templateDir = $null
    if (Test-Path -LiteralPath $TemplatePath -PathType Container) {
        $sourceTemplateDir = (Resolve-Path -LiteralPath $TemplatePath).Path
        $templateDir = Join-Path $workRoot 'template'
        New-Item -ItemType Directory -Path $templateDir -Force | Out-Null
        Copy-Item -Path (Join-Path $sourceTemplateDir '*') -Destination $templateDir -Recurse -Force
        if (-not (Test-Path -LiteralPath (Join-Path $templateDir 'AndroidManifest.xml') -PathType Leaf)) {
            throw "Template folder does not contain AndroidManifest.xml: $sourceTemplateDir"
        }
        Write-Host "Using decoded template folder: $sourceTemplateDir"
    }
    else {
        if ([System.IO.Path]::GetExtension($TemplatePath) -ne '.apk') {
            throw "Template must be an .apk file or a decoded apktool folder: $TemplatePath"
        }

        $templateDir = Join-Path $workRoot 'template'
        Write-Host 'Decoding APK template...'
        & $JavaPath -jar $ApkToolPath d $TemplatePath -o $templateDir -f
        if ($LASTEXITCODE -ne 0) {
            throw "apktool failed to decode the template (exit code $LASTEXITCODE)."
        }
        if (-not (Test-Path -LiteralPath (Join-Path $templateDir 'AndroidManifest.xml') -PathType Leaf)) {
            throw "Decoded template does not contain AndroidManifest.xml: $templateDir"
        }
    }

    # --- Replace template assets with the PWA -------------------------------
    $templateAssets = Join-Path $templateDir 'assets'
    if (Test-Path -LiteralPath $templateAssets) {
        Remove-Item -LiteralPath $templateAssets -Recurse -Force
    }
    New-Item -ItemType Directory -Path $templateAssets -Force | Out-Null
    Copy-Item -Path (Join-Path $stageAssets '*') -Destination $templateAssets -Recurse -Force
    Write-Host 'Mobile assets copied into the template.'

    # --- Build unsigned APK --------------------------------------------------
    $unsignedApk = Join-Path $workRoot 'unsigned.apk'
    Write-Host 'Building APK with apktool...'
    & $JavaPath -jar $ApkToolPath b $templateDir -o $unsignedApk
    if ($LASTEXITCODE -ne 0) {
        throw "apktool build failed (exit code $LASTEXITCODE)."
    }
    if (-not (Test-Path -LiteralPath $unsignedApk -PathType Leaf)) {
        throw "apktool did not produce the expected file: $unsignedApk"
    }

    # --- Sign (debug) -------------------------------------------------------
    $signDir = Join-Path $workRoot 'signed'
    New-Item -ItemType Directory -Path $signDir -Force | Out-Null
    Write-Host 'Signing APK (debug)...'
    & $JavaPath -jar $SignerPath --apks $unsignedApk --out $signDir --overwrite --allowResign
    if ($LASTEXITCODE -ne 0) {
        throw "uber-apk-signer failed (exit code $LASTEXITCODE)."
    }

    $signedApk = Get-ChildItem -LiteralPath $signDir -Filter '*.apk' -File -ErrorAction SilentlyContinue |
        Sort-Object -Property LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if (-not $signedApk) {
        throw "No signed APK was produced in: $signDir"
    }

    # --- Publish result ------------------------------------------------------
    $outputFull = [System.IO.Path]::GetFullPath($OutputPath)
    $outputDir = Split-Path -Parent $outputFull
    if (-not (Test-Path -LiteralPath $outputDir -PathType Container)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }
    Copy-Item -LiteralPath $signedApk.FullName -Destination $outputFull -Force

    $sizeMb = [math]::Round((Get-Item -LiteralPath $outputFull).Length / 1MB, 2)
    Write-Host ''
    Write-Host 'APK built successfully.' -ForegroundColor Green
    Write-Host "Output : $outputFull"
    Write-Host "Size   : $sizeMb MB"
    Write-Host 'Note   : debug signature - suitable for sideloading/testing, not Play Store.'

    if (-not $KeepWork) {
        try {
            Remove-Item -LiteralPath $workRoot -Recurse -Force
        }
        catch {
            Write-Warning "Could not remove the work folder: $workRoot"
        }
    }

    exit 0
}
catch {
    Write-Host ''
    Write-Host "APK build failed: $($_.Exception.Message)" -ForegroundColor Red
    if (Test-Path -LiteralPath $workRoot) {
        Write-Host "Work folder kept for inspection: $workRoot"
    }
    exit 1
}
